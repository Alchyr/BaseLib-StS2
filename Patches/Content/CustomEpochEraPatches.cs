using System.Reflection;
using System.Reflection.Emit;
using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.Timeline;
using MegaCrit.Sts2.Core.Timeline;

namespace BaseLib.Patches.Content;

/// <summary>
/// Massive patch to hand insertion of entirely new Eras
/// </summary>
[HarmonyPatch]
public class AddCustomEpochSlots
{
    private static List<EpochSlotData> _customEpochSlotData = [];
    private static Vector2 _originalEpochSlotsContainerPosition = new(0, 0);

    private static readonly FieldInfo? UniqueEpochErasField = AccessTools.Field(typeof(NTimelineScreen), "_uniqueEpochEras");
    private static readonly FieldInfo? EpochSlotContainerField = AccessTools.Field(typeof(NTimelineScreen), "_epochSlotContainer");
    private static readonly FieldInfo? SlotsContainerField = AccessTools.Field(typeof(NTimelineScreen), "_slotsContainer");


    [HarmonyPrefix]
    [HarmonyPatch(typeof(NTimelineScreen), nameof(NTimelineScreen.AddEpochSlots))]
    private static void RemoveSelectCustomEpochsFromDefaultLogic(NTimelineScreen __instance, ref List<EpochSlotData> slotsToAdd)
    {
        _customEpochSlotData = [];
        var allSlots = slotsToAdd;
        slotsToAdd = allSlots.Where(s => s.Model is not CustomEpochModel model || model.CustomEra is null).ToList();
        _customEpochSlotData = allSlots.Where(s => s.Model is CustomEpochModel { CustomEra: not null }).ToList();
        //_customEpochSlotData =  allSlots.Except(slotsToAdd).ToList();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(NTimelineScreen), nameof(NTimelineScreen.AddEpochSlots), MethodType.Async)]
    private static List<CodeInstruction> InsertCustomEraColumnsAndEpochs(IEnumerable<CodeInstruction> instructions, MethodBase originalMethod)
    {
        var customEpochEraHandler = typeof(AddCustomEpochSlots).Method(nameof(CustomEpochEraHandler));
        var thisField = AccessTools.GetDeclaredFields(originalMethod.DeclaringType).First(f => f.FieldType == typeof(NTimelineScreen));

        var matcher = new CodeMatcher(instructions)
                    .MatchStartForward([
                                new CodeMatch(OpCodes.Ldloca_S),
                                new CodeMatch(OpCodes.Ldstr, " Created "),
                    ])
                    .ThrowIfInvalid("Could not find position to insert custom epoch era Handler")
                    .Insert([
                                new CodeInstruction(OpCodes.Ldarg_0),
                                new CodeInstruction(OpCodes.Ldfld, thisField),
                                new CodeInstruction(OpCodes.Ldloc_2),
                                new CodeInstruction(OpCodes.Call, customEpochEraHandler),
                    ]);
        return matcher.InstructionEnumeration().ToList();
    }

    private static void CustomEpochEraHandler(NTimelineScreen instance, List<NEraColumn> newlyCreatedColumns)
    {
        BaseLibMain.Logger.Warn($"Called {nameof(CustomEpochEraHandler)}");
        BaseLibMain.Logger.Warn($"List count: {newlyCreatedColumns.Count}");

        if (UniqueEpochErasField is null || EpochSlotContainerField is null) return;
        var uniqueEpochErasObject = UniqueEpochErasField.GetValue(instance);
        var epochSlotContainerObject = EpochSlotContainerField.GetValue(instance);
        if (uniqueEpochErasObject == null || epochSlotContainerObject == null) return;
        if (uniqueEpochErasObject is not Dictionary<EpochEra, NEraColumn> uniqueEpochEras) return;
        if (epochSlotContainerObject is not HBoxContainer epochSlotContainer) return;

        BaseLibMain.Logger.Warn("Reflection successful");

        var customEraColumnData = SortSlotsIntoCustomEraColumns();

        var originalNEraColumns = epochSlotContainer.GetChildren().OfType<NEraColumn>().ToList();
        var ourCustomColumns = new Dictionary<EpochEra, CustomEpochEra>();

        // For every column we have
        foreach (var columnData in customEraColumnData)
        {
            var customEpochEra = columnData.CustomEpochEra.CustomEra;
            // for every slot inside that column
            for (var epochSlotDataIndex = 0; epochSlotDataIndex < columnData.EpochSlotData.Count; epochSlotDataIndex++)
            {
                var customEpochModel = columnData.CustomEpochs[epochSlotDataIndex]; // data and model count should always be equal
                if (customEpochModel.CustomEra is null) continue; // Should never be true

                if (uniqueEpochEras.TryGetValue(customEpochEra, out var nEraColumn))
                {
                    nEraColumn.AddSlot(columnData.EpochSlotData[epochSlotDataIndex]);
                }
                else
                {
                    var newNEraColumn = NEraColumn.Create(columnData.EpochSlotData[epochSlotDataIndex]);
                    ourCustomColumns.Add(columnData.CustomEpochEra.CustomEra, columnData.CustomEpochEra);

                    // is being modified so grab it again every time
                    var nEraColumns = epochSlotContainer.GetChildren().OfType<NEraColumn>().ToList();

                    var validEraEntryPoint = FindValidCustomEraEntryPoint(nEraColumns, customEpochModel, columnData, originalNEraColumns);

                    BaseLibMain.Logger.Info($"FirstIndex: {validEraEntryPoint.NEraColumnIndex.ToString()}");

                    var searchBefore = validEraEntryPoint.RelativeInsertDirection == RelativeEraDirection.Before;
                    var curDepth = searchBefore ? -1 : 1;
                    var curIndex = validEraEntryPoint.NEraColumnIndex + curDepth;
                    while (curIndex >= 0 && curIndex < nEraColumns.Count)
                    {
                        if (originalNEraColumns.Contains(nEraColumns[curIndex]))
                        {
                            InsertNewEraColumn(epochSlotContainer, newlyCreatedColumns, uniqueEpochEras, customEpochEra, newNEraColumn, searchBefore ? curIndex + 1 : curIndex);
                            break;
                        }

                        if (ourCustomColumns.TryGetValue(nEraColumns[curIndex].era, out var era))
                        {
                            if (era.DirectionDepth > validEraEntryPoint.PositionDepth)
                            {
                                InsertNewEraColumn(epochSlotContainer, newlyCreatedColumns, uniqueEpochEras, customEpochEra, newNEraColumn, searchBefore ? curIndex + 1 : curIndex);
                                break;
                            }
                        }

                        curDepth += Math.Sign(curDepth);
                        curIndex = validEraEntryPoint.NEraColumnIndex + curDepth;
                    }

                    if (curIndex == -1) // insert at the very left
                        InsertNewEraColumn(epochSlotContainer, newlyCreatedColumns, uniqueEpochEras, customEpochEra, newNEraColumn, 0);
                    if (curIndex == nEraColumns.Count) // insert at the very right
                        InsertNewEraColumn(epochSlotContainer, newlyCreatedColumns, uniqueEpochEras, customEpochEra, newNEraColumn, nEraColumns.Count);
                }
            }
        }

        // I originally wanted to do this by referencing the state before and after inserting the custom epochs, but the container has not yet updated its size
        // so instead we do it manually
        var largestEpochCount = epochSlotContainer.GetChildren().OfType<NEraColumn>().ToList().Select(nEraColumn => nEraColumn.GetChildCount() - 1).Prepend(0).Max();
        var overCount = largestEpochCount - 5;
        if (overCount <= 0) return;
        const float epochSize = -24f;
        const float spacing = -32f;
        _originalEpochSlotsContainerPosition = new Vector2(0, overCount * epochSize + (overCount - 1) * spacing);
        epochSlotContainer.Position += _originalEpochSlotsContainerPosition;
    }


    private static (RelativeEraDirection RelativeInsertDirection, int PositionDepth, int NEraColumnIndex)
                FindValidCustomEraEntryPoint(List<NEraColumn> nEraColumns, CustomEpochModel customEpochModel,
                            CustomEraColumnData columnData, List<NEraColumn> originalNEraColumns)
    {
        var relativeInsertDirection = columnData.CustomEpochEra.Direction;
        var positionDepth = customEpochModel.CustomEra!.DirectionDepth;
        var referenceEpochEra = columnData.CustomEpochEra.ReferenceEra;

        var nEraColumnIndex = nEraColumns.FirstIndex(e => e.era == referenceEpochEra);
        if (nEraColumnIndex != -1) return (relativeInsertDirection, positionDepth, nEraColumnIndex);

        BaseLibMain.Logger.Info($"The required era has not been unlocked yet. Finding alternative entry point.");
        for (var i = 0; i < originalNEraColumns.Count; i++)
        {
            var reverse = customEpochModel.CustomEra.Direction != RelativeEraDirection.Before;
            var index = reverse ? originalNEraColumns.Count - i - 1 : 1;

            if ((!reverse && originalNEraColumns[index].era < referenceEpochEra) ||
                (reverse && originalNEraColumns[index].era > referenceEpochEra)) continue;

            nEraColumnIndex = nEraColumns.IndexOf(originalNEraColumns[index]);
            break;
        }

        if (nEraColumnIndex != -1) return (relativeInsertDirection, positionDepth, nEraColumnIndex);
        // We reached the last era and still nothing. Insert at the end with reversed direction and depth
        nEraColumnIndex = nEraColumns.IndexOf((columnData.CustomEpochEra.Direction == RelativeEraDirection.Before) ? originalNEraColumns.Last() : originalNEraColumns.First());
        relativeInsertDirection = relativeInsertDirection == RelativeEraDirection.Before ? RelativeEraDirection.After : RelativeEraDirection.Before;
        positionDepth *= -1;
        return (relativeInsertDirection, positionDepth, nEraColumnIndex);
    }

    private static void InsertNewEraColumn(HBoxContainer epochSlotContainer, List<NEraColumn> newlyCreatedColumns,
                Dictionary<EpochEra, NEraColumn> uniqueEpochEras, EpochEra epochEra, NEraColumn newNEraColumn, int index)
    {
        epochSlotContainer.AddChildSafely(newNEraColumn);
        epochSlotContainer.MoveChildSafely(newNEraColumn, index);
        newlyCreatedColumns.Add(newNEraColumn);
        uniqueEpochEras.Add(epochEra, newNEraColumn);
    }


    private static List<CustomEraColumnData> SortSlotsIntoCustomEraColumns()
    {
        List<CustomEraColumnData> columns = [];
        foreach (var epochSlotData in _customEpochSlotData)
        {
            if (epochSlotData.Model is not CustomEpochModel customEpochModel) continue;
            if (customEpochModel.CustomEra is null) continue;

            if (columns.Any(c => c.CustomEpochEra == customEpochModel.CustomEra))
            {
                var columnData = columns.Where(c => c.CustomEpochEra == customEpochModel.CustomEra).First();
                columnData.EpochSlotData.Add(epochSlotData);
                columnData.CustomEpochs.Add(customEpochModel);
            }
            else
            {
                var newColumnData = new CustomEraColumnData(customEpochModel.CustomEra);
                newColumnData.EpochSlotData.Add(epochSlotData);
                newColumnData.CustomEpochs.Add(customEpochModel);
                columns.Add(newColumnData);
            }
        }

        return columns;
    }

    private struct CustomEraColumnData(CustomEpochEra customEpochEra)
    {
        public CustomEpochEra CustomEpochEra { get; init; } = customEpochEra;
        public List<EpochSlotData> EpochSlotData = [];
        public List<CustomEpochModel> CustomEpochs = []; // Save Epochs separate from Data so we don't have to re-cast to the custom Model.
    }


    [HarmonyPatch]
    public static class NTimelineScreenVerticalDragging
    {
        private static bool _allowHorizontalDrag = false;
        private static bool _allowVerticalDrag = false;

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NTimelineScreen), nameof(NTimelineScreen.SetScreenDraggability))]
        private static void ShouldScreenBeDraggableBecauseOfColumnHeight(NTimelineScreen __instance)
        {
            var slotsContainerFieldObject = SlotsContainerField?.GetValue(__instance);
            if (slotsContainerFieldObject is not NSlotsContainer nSlotsContainer) return;
            var epochSlotContainerObject = EpochSlotContainerField?.GetValue(__instance);
            if (epochSlotContainerObject is not HBoxContainer epochSlotContainer) return;

            _allowHorizontalDrag = nSlotsContainer.MouseFilter == Control.MouseFilterEnum.Stop;
            _allowVerticalDrag = false;
            if (epochSlotContainer.GetChildren().OfType<NEraColumn>().ToList().Select(nEraColumn => nEraColumn.GetChildCount() - 1).Prepend(0).Max() <= 5) return;
            nSlotsContainer.MouseFilter = Control.MouseFilterEnum.Stop;
            _allowVerticalDrag = true;
        }


        private static readonly FieldInfo? DragStartPositionField = AccessTools.Field(typeof(NSlotsContainer), "_dragStartPosition");
        private static readonly FieldInfo? TargetPositionField = AccessTools.Field(typeof(NSlotsContainer), "_targetPosition");
        private static readonly FieldInfo? IsDraggingField = AccessTools.Field(typeof(NSlotsContainer), "_isDragging");
        private static readonly FieldInfo? EpochSlotsField = AccessTools.Field(typeof(NSlotsContainer), "_epochSlots");
        private static readonly FieldInfo? WhatsMovedField = AccessTools.Field(typeof(NSlotsContainer), "_whatsMoved");

        [HarmonyTranspiler]
        [HarmonyPatch(typeof(NSlotsContainer), "ProcessPanEvent")]
        private static List<CodeInstruction> AdjustProcessPanEventForVertical(IEnumerable<CodeInstruction> instructions)
        {
            var setTargetPosition = typeof(NTimelineScreenVerticalDragging).Method(nameof(SetTargetPosition));
            var matcher = new CodeMatcher(instructions)
                        .MatchStartForward([
                                    new CodeMatch(OpCodes.Ldloc_1),
                                    new CodeMatch(OpCodes.Callvirt),
                                    new CodeMatch(OpCodes.Ldarg_0),
                        ])
                        .ThrowIfInvalid("Could not find correct position")
                        .RemoveUntilForward([
                                    new CodeMatch(OpCodes.Ldarg_0),
                                    new CodeMatch(OpCodes.Ldloc_1),
                                    new CodeMatch(OpCodes.Callvirt),
                        ])
                        .Insert([
                                    new CodeInstruction(OpCodes.Ldarg_0),
                                    new CodeInstruction(OpCodes.Ldloc_1),
                                    new CodeInstruction(OpCodes.Call, setTargetPosition),
                        ]);
            return matcher.InstructionEnumeration().ToList();
        }

        private static void SetTargetPosition(NSlotsContainer instance, InputEventMouseMotion eventMouseMotion)
        {
            // This code runs a lot. Might be better to hold a reference in a conditional weak table?
            var dragStartPositionObject = DragStartPositionField?.GetValue(instance);
            if (dragStartPositionObject is not Vector2 dragStartPosition) throw new NullReferenceException();
            var targetPositionObject = TargetPositionField?.GetValue(instance);
            if (targetPositionObject is not Vector2 targetPosition) throw new NullReferenceException();

            var allowDrag = new Vector2(_allowHorizontalDrag ? 1 : 0, _allowVerticalDrag ? 1 : 0);
            TargetPositionField?.SetValue(instance, targetPosition + (eventMouseMotion.Position - dragStartPosition) * allowDrag);
        }


        [HarmonyPostfix]
        [HarmonyPatch(typeof(NSlotsContainer), nameof(NSlotsContainer._Process))]
        private static void LerpToMinMaxHeights(NSlotsContainer __instance, double delta)
        {
            var isDraggingObject = IsDraggingField?.GetValue(__instance);
            if (isDraggingObject is not bool isDragging) return;
            if (!isDragging || !_allowVerticalDrag) return;

            var targetPositionObject = TargetPositionField?.GetValue(__instance);
            if (targetPositionObject is not Vector2 targetPosition) throw new NullReferenceException();
            var epochSlotsObject = EpochSlotsField?.GetValue(__instance);
            if (epochSlotsObject is not Control epochSlots) throw new NullReferenceException();
            var whatsMovedObject = WhatsMovedField?.GetValue(__instance);
            if (whatsMovedObject is not Control whatsMoved) throw new NullReferenceException();

            var num = targetPosition.Y;
            var upperBound = epochSlots.Size.Y - 700; // - whatsMoved.Size.Y; //27 1080
            var lowerBound = -550; // epochSlots.Position.Y + epochSlots.Size.Y - whatsMoved.Size.Y;
            if (num > upperBound)
                num = Mathf.Lerp(num, upperBound, (float)delta * 36f);
            else if (num < lowerBound)
                num = Mathf.Lerp(num, lowerBound, (float)delta * 36f);
            TargetPositionField?.SetValue(__instance, new Vector2(targetPosition.X, num));
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(NSlotsContainer), nameof(NSlotsContainer.Reset))]
        private static void ResetWhatsMovedYPosition(NSlotsContainer __instance)
        {
            if (!_allowVerticalDrag) return;
            var whatsMovedObject = WhatsMovedField?.GetValue(__instance);
            if (whatsMovedObject is not Control whatsMoved) throw new NullReferenceException();
            whatsMoved.Position = new Vector2(whatsMoved.Position.X, -540f);
        }


        [HarmonyPostfix]
        [HarmonyPatch(typeof(NTimelineScreen), "ResetScreen")]
        private static void ResetEpochSlotContainerPosition(NTimelineScreen __instance)
        {
            if (_originalEpochSlotsContainerPosition == new Vector2(0, 0)) return;
            var epochSlotContainerObject = EpochSlotContainerField?.GetValue(__instance);
            if (epochSlotContainerObject is not HBoxContainer epochSlotContainer) return;
            epochSlotContainer.Position -= _originalEpochSlotsContainerPosition;
            _originalEpochSlotsContainerPosition = new Vector2(0, 0);
        }
    }
}