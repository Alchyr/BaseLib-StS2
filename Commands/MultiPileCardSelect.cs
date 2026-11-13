using BaseLib.Utils;
using BaseLib.Utils.Patching;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.CardLibrary;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;

namespace BaseLib.Commands;

/// <summary>
/// Contains commands that allow the player to select cards from multiple piles (e.g. "Put a 0 cost card from your draw or discard pile into your hand.")
/// </summary>
public static class MultiPileCardSelect
{
    private const float IconSize = 65;
    private const float LabelMargin = 12;
    private const float LabelSize = 250;
    private const float LabelTweenTime = 0.2f;

    //private static readonly SpireField<NSimpleCardSelectScreen, bool> IsMultiPile = new(static _ => false);
    private static readonly SpireField<NCardGrid, bool> IsGridMultiPile = new(static _ => false);
    private static readonly SpireField<NCardGrid, PileType[]> PileTypes = new(static _ => null);
    private static readonly SpireField<NGridCardHolder, TextureRect> PileIndicator = new(static _ => null);
    private static readonly SpireField<NGridCardHolder, MegaLabel> PileNameLabel = new(static _ => null);

    /// <summary>
    /// Pile indicator infos (tooltip title + icon) keyed by pile type. Pre-populated with the vanilla piles;
    /// custom piles can be added via <see cref="RegisterPileIndicator(PileType, string, LocString)"/>.
    /// </summary>
    private static readonly Dictionary<PileType, (LocString Title, string TexturePath)> RegisteredPileIndicators = new() {
        [PileType.Draw] = (new LocString("card_selection", "BASELIB-DRAW_PILE"), "res://images/packed/combat_ui/draw_pile.png"),
        [PileType.Discard] = (new LocString("card_selection", "BASELIB-DISCARD_PILE"), "res://images/packed/combat_ui/discard_pile.png"),
        [PileType.Exhaust] = (new LocString("card_selection", "BASELIB-EXHAUST_PILE"), "res://images/packed/combat_ui/exhaust_pile.png"),
        [PileType.Hand] = (new LocString("card_selection", "BASELIB-HAND"), "res://images/powers/hello_world_power.png"),
        [PileType.Deck] = (new LocString("card_selection", "BASELIB-DECK"), "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_deck.tres"),
    };

    /// <summary>
    /// Registers (or overrides) the indicator shown on cards belonging to the given pile type in multi-pile selection screens.
    /// </summary>
    /// <param name="pileType">The pile type to register an indicator for</param>
    /// <param name="texturePath">Resource path of the icon texture</param>
    /// <param name="name">Localized pile name, shown as a tooltip when the card is focused</param>
    internal static void RegisterPileIndicator(PileType pileType, string texturePath, LocString name) =>
        RegisteredPileIndicators[pileType] = (name, texturePath);
    
    /// <summary>
    /// Opens a card selection screen where a specific number of cards must be selected from specified card piles and returns the selection result.
    /// If all piles are combat piles, the screen stays up to date when the piles change while it is open
    /// (e.g. another player makes you draw a card).
    /// </summary>
    /// <param name="filter">A predicate that takes a card and returns true</param>
    /// <param name="pileTypes">The card pile types from which the player can select cards. Shown cards will be sorted by pile type in the order you supply them.</param>
    /// <returns>
    /// The selected cards
    /// </returns>
    public static async Task<IEnumerable<CardModel>> Select(PlayerChoiceContext context, Player player, CardSelectorPrefs prefs, Func<CardModel, bool>? filter = null, params PileType[] pileTypes)
    {
        if (CombatManager.Instance.IsEnding) return [];
        var isLive = pileTypes.All(static p => p.IsCombatPile());
        return await Select(context, player, prefs, GatherCards(player, filter, pileTypes), pileTypes,
            isLive ? () => GatherCards(player, filter, pileTypes) : null);
    }

    private static List<CardModel> GatherCards(Player player, Func<CardModel, bool>? filter, PileType[] pileTypes)
    {
        var enumerable = pileTypes.SelectMany(p => {
            IEnumerable<CardModel> cards = p.GetPile(player).Cards;
            if (p == PileType.Draw)
                cards = from c in cards orderby c.Rarity, c.Id select c;
            return cards;
        });
        if (filter != null) enumerable = enumerable.Where(filter);
        return [..enumerable];
    }

    /// <summary>
    /// Opens a card selection screen where a specific number of cards must be selected from an array of cards and returns the selection result. The cards will be displayed with an icon denoting which card pile they currently reside in.
    /// The shown cards are fixed; they are not updated if the piles change while the screen is open.
    /// </summary>
    /// <param name="cards">The cards to select from</param>
    /// <param name="pileTypes">Shown cards will be sorted by pile type in the order they appear in this array.</param>
    /// <returns>
    /// The selected cards
    /// </returns>
    public static Task<IEnumerable<CardModel>> Select(PlayerChoiceContext context, Player player, CardSelectorPrefs prefs, List<CardModel> cards, PileType[]? pileTypes = null)
        => Select(context, player, prefs, cards, pileTypes, null);

    /// <param name="getCurrentCards">
    /// If set, the screen is rebuilt from this whenever one of the piles changes, and the choice is synced as combat cards
    /// instead of indexes into <paramref name="cards"/> (which would be outdated by then).
    /// </param>
    private static async Task<IEnumerable<CardModel>> Select(PlayerChoiceContext context, Player player, CardSelectorPrefs prefs, List<CardModel> cards, PileType[]? pileTypes, Func<List<CardModel>>? getCurrentCards)
    {
        if (CombatManager.Instance.IsEnding || cards.Count == 0) return [];
        List<CardModel> result;
        if (!prefs.RequireManualConfirmation && cards.Count <= prefs.MinSelect)
            result = cards;
        else {
            var choiceId = RunManager.Instance.PlayerChoiceSynchronizer.ReserveChoiceId(player);
            await context.SignalPlayerChoiceBegunCompatibility(player, PlayerChoiceOptions.None);
            var isLive = getCurrentCards != null;
            if ((bool)AccessTools.Method(typeof(CardSelectCmd), nameof(CardSelectCmd.ShouldSelectLocalCard)).Invoke(null, [player])!) {
                result = await ShowScreen(player, prefs, cards, pileTypes ?? [], getCurrentCards);
                RunManager.Instance.PlayerChoiceSynchronizer.SyncLocalChoice(player, choiceId, ToChoiceResult(result, cards, isLive));
            } else {
                var remoteChoice = await RunManager.Instance.PlayerChoiceSynchronizer.WaitForRemoteChoice(player, choiceId);
                result = FromChoiceResult(remoteChoice, cards, isLive);
            }
            await context.SignalPlayerChoiceEnded();
        }
        AccessTools.Method(typeof(CardSelectCmd), nameof(CardSelectCmd.LogChoice)).Invoke(null, [player, result]);
        return result;
    }

    private static async Task<List<CardModel>> ShowScreen(Player player, CardSelectorPrefs prefs, List<CardModel> cards, PileType[] pileTypes, Func<List<CardModel>>? getCurrentCards)
    {
        NPlayerHand.Instance?.CancelAllCardPlay();
        var screen = NSimpleCardSelectScreen.Create(cards, prefs);
        var grid = screen.GetNode<NCardGrid>("%CardGrid");
        IsGridMultiPile.Set(grid, true);
        PileTypes.Set(grid, pileTypes);
        NOverlayStack.Instance!.Push(screen);
        var stopUpdating = getCurrentCards == null ? null : KeepUpdated(screen, player, pileTypes, getCurrentCards);
        try {
            return [..await screen.CardsSelected()];
        } finally {
            stopUpdating?.Invoke();
            //cleanup cus nodes r pooled
            screen._grid._cardRows.ForEach(static r => r.ForEach(static n => ClearHolderIndicatorNodes(n)));
        }
    }

    private static PlayerChoiceResult ToChoiceResult(List<CardModel> result, List<CardModel> cards, bool isLive) =>
        isLive
            ? PlayerChoiceResult.FromMutableCombatCards(result)
            : PlayerChoiceResult.FromIndexes([..result.Select(c => cards.IndexOf(c))]);

    private static List<CardModel> FromChoiceResult(PlayerChoiceResult choice, List<CardModel> cards, bool isLive) =>
        isLive
            ? [..choice.AsCombatCards()]
            : [..choice.AsIndexes().Select(i => cards[i])];

    /// <summary>
    /// Rebuilds the screen's cards whenever one of the piles changes: cards that left the piles are dropped from the
    /// selection, and the screen closes once nothing is left to choose from. Returns an action which stops this.
    /// </summary>
    private static Action KeepUpdated(NSimpleCardSelectScreen screen, Player player, PileType[] pileTypes, Func<List<CardModel>> getCurrentCards)
    {
        var piles = pileTypes.Select(p => p.GetPile(player)).Distinct().ToList();
        var updateQueued = false;
        var stopped = false;
        foreach (var pile in piles) pile.ContentsChanged += QueueUpdate;
        return () => {
            stopped = true;
            foreach (var pile in piles) pile.ContentsChanged -= QueueUpdate;
        };
        
        void QueueUpdate()
        {
            if (updateQueued) return;
            updateQueued = true;
            Callable.From(Update).CallDeferred();
        }

        void Update()
        {
            updateQueued = false;
            if (stopped || !GodotObject.IsInstanceValid(screen) || screen._completionSource.Task.IsCompleted) return;
            var cards = getCurrentCards();
            if (cards.SequenceEqual(screen._cards)) return;
            screen._selectedCards.RemoveWhere(c => !cards.Contains(c));
            if (cards.Count == 0 || cards.Count == screen._selectedCards.Count) {
                screen.CompleteSelection();
                return;
            }

            screen._cards = cards;
            screen._grid.SetCards(cards, PileType.None, [SortingOrders.Ascending]);
            foreach (var card in screen._selectedCards) screen._grid.HighlightCard(card);
            if (screen._selectedCards.Count >= screen._prefs.MinSelect && screen._prefs.RequireManualConfirmation)
                screen._confirmButton.Enable();
            else
                screen._confirmButton.Disable();
        }
    }

    private static void ClearHolderIndicatorNodes(NGridCardHolder holder)
    {   
        PileIndicator.Get(holder)?.QueueFree();
        PileIndicator.Set(holder, null);
        PileNameLabel.Set(holder, null);
    }

    private static void AddIndicatorNode(NCardGrid grid, NGridCardHolder holder)
    {
        ClearHolderIndicatorNodes(holder);
        if (!IsGridMultiPile.Get(grid)) return;
        if (holder.CardNode?.Model?.Pile?.Type is not { } pileType) return;
        if (!RegisteredPileIndicators.TryGetValue(pileType, out var pile)) return;
        TextureRect icon = new() {
            Position = new(110, -230),
            Size = Vector2.One * IconSize,
            MouseFilter = Control.MouseFilterEnum.Pass,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            Texture = ResourceLoader.Load<Texture2D>(pile.TexturePath),
        };
        PileIndicator.Set(holder, icon);
        holder.AddChild(icon);
        MegaLabel label = new()
        {
            Modulate = Colors.White with { A = 0f },
            Size = new(LabelSize, IconSize),
            Position = new(-LabelSize + IconSize, 0),
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            MinFontSize = 16,
            MaxFontSize = 24,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ShowBehindParent = true,
        };
        label.AddThemeFontOverride(ThemeConstants.Label.Font,
            ResourceLoader.Load<FontVariation>("res://themes/kreon_regular_shared.tres"));
        label.AddThemeColorOverride(ThemeConstants.Label.FontColor, Colors.White with { A = 0.75f });
        label.AddThemeColorOverride(ThemeConstants.Label.FontOutlineColor, Colors.Black with { A = 0.75f });
        label.AddThemeColorOverride(ThemeConstants.Label.FontShadowColor, Colors.Black with { A = 0.2f });
        label.AddThemeConstantOverride(ThemeConstants.Label.OutlineSize, 10);
        label.AddThemeConstantOverride("shadow_offset_x", 3);
        label.AddThemeConstantOverride("shadow_offset_y", 3);
        label.SetTextAutoSize(pile.Title.GetFormattedText());
        icon.AddChild(label);
        PileNameLabel.Set(holder, label);
      
    }

    [HarmonyPatch(typeof(NCardGrid), nameof(NCardGrid.SetCards))]
    private static class SortCardsPatch
    {
        static void Postfix(NCardGrid __instance)
        {
            if (!IsGridMultiPile.Get(__instance) || PileTypes.Get(__instance) is not { } pileTypes) return;
            var field = AccessTools.Field(typeof(NCardGrid), nameof(NCardGrid._cards));
            var cards = (List<CardModel>)field.GetValue(__instance)!;
            cards.Sort((a, b) => PileOrder(a).CompareTo(PileOrder(b)));
            return;
            int PileOrder(CardModel c) => c.Pile == null || !pileTypes.Contains(c.Pile.Type) ? int.MaxValue - 1 : pileTypes.IndexOf(c.Pile.Type);
        }
    }

    [HarmonyPatch(typeof(NCardGrid), nameof(NCardGrid.InitGrid), [])]
    private static class AddPileIndicatorNodePatch
    {
        static List<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => new InstructionPatcher(instructions)
            .Match(new InstructionMatcher()
                .call(AccessTools.Method(typeof(NGridCardHolder), nameof(NGridCardHolder.Create)))
                .stloc_s()
            )
            .Insert([
                CodeInstruction.LoadArgument(0),
                CodeInstruction.LoadLocal(10),
                CodeInstruction.Call(typeof(MultiPileCardSelect), nameof(AddIndicatorNode)),
            ]);
    }

    [HarmonyPatch(typeof(NCardGrid), "AssignCardsToRow")]
    private static class RefreshPileIndicatorNodePatch
    {
        static List<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => new InstructionPatcher(instructions)
            .Match(new InstructionMatcher()
                .callvirt(AccessTools.Method(typeof(NCardHolder), nameof(NCardHolder.ReassignToCard)))
            )
            .Insert([
                CodeInstruction.LoadArgument(0),
                CodeInstruction.LoadLocal(1),
                CodeInstruction.Call(typeof(MultiPileCardSelect), nameof(AddIndicatorNode)),
            ]);
    }

    [HarmonyPatch(typeof(NGridCardHolder), "OnFocus")]
    private static class ShowTipPatch
    {
        static void Postfix(NGridCardHolder __instance)
        {
            var label = PileNameLabel.Get(__instance);
            if (label == null) return;
            var tween = __instance.CreateTween();
            tween.SetParallel();
            tween.SetEase(Tween.EaseType.Out);
            tween.SetTrans(Tween.TransitionType.Quad);
            tween.TweenProperty(label, "position:x", -LabelSize - LabelMargin, LabelTweenTime);
            tween.TweenProperty(label, "modulate:a", 1f, LabelTweenTime);
        }
    }

    [HarmonyPatch(typeof(NCardHolder), "OnUnfocus")]
    private static class HideTipPatch
    {
        static void Postfix(NCardHolder __instance)
        {
            if (__instance is not NGridCardHolder holder) return;
            var label = PileNameLabel.Get(holder);
            if (label == null) return;
            var tween = __instance.CreateTween();
            tween.SetParallel();
            tween.SetEase(Tween.EaseType.Out);
            tween.SetTrans(Tween.TransitionType.Quad);
            tween.TweenProperty(label, "position:x", -LabelSize + IconSize, LabelTweenTime);
            tween.TweenProperty(label, "modulate:a", 0f, LabelTweenTime);
        }
    }
}