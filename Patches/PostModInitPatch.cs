using System.Reflection;
using BaseLib.Abstracts;
using BaseLib.Patches.Content;
using BaseLib.Patches.Features;
using BaseLib.Patches.Saves;
using BaseLib.Patches.Utils;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;
using SmartFormat.Core.Extensions;

namespace BaseLib.Patches;

//Patch that occurs after mod initialization, before anything else is done.
//See OneTimeInitialization.ExecuteEssential for ordering

//TODO - If no mods that modify gameplay and use baselib as a dependency are enabled, exclude basemod models from database?
//This would allow features like vitality to be merged.
//This seems to be something added to basegame; will be left for now.


[HarmonyPatch] 
class PostModInitPatch
{
    private static bool _earlyInit = false, _lateInit = false;
    public static bool CanModifyGameplay { get; private set; } = false;

    [HarmonyPatch(typeof(LocManager), nameof(LocManager.Initialize))] 
    [HarmonyPrefix]
    private static void EarlyPostInit()
    {
        if (_earlyInit) return;
        _earlyInit = true;
        
        BaseLibMain.Logger.Info("Performing early post-mod init");
        
        WhatMod.BuildAfterInit();

        foreach (var mod in ModManager.GetLoadedMods())
        {
            // Enable gameplay modification if ANY loaded gameplay-affecting mod depends on
            // BaseLib. Both conditions must be checked together: breaking on the first
            // gameplay-affecting mod regardless of its dependency would leave this false
            // whenever a non-BaseLib gameplay mod happens to load first.
            if (mod.manifest?.affectsGameplay == true &&
                BetaMainCompatibility._ModManifest.HasDependency(mod.manifest, "BaseLib"))
            {
                BaseLibMain.Logger.Info($"Mod {mod.manifest.id} that modifies gameplay has BaseLib dependency; gameplay modification enabled.");
                CanModifyGameplay = true;
                break;
            }
        }

        if (CanModifyGameplay)
        {
            //Register custom save data.
            CardModifier.RegisterSave();
        }
        
        //Loads custom message types into custom message type maps
        CustomMessageWrapper.Initialize();
        CustomTargetedMessageWrapper.Initialize();
        
        Harmony harmony = new("PostModInit");

        AddActContent.Patch(harmony);
        
        ModInterop interop = new();
        
        foreach (var type in ReflectionHelper.ModTypes)
        {
            interop.ProcessType(harmony, type);

            if (type.IsAbstract || type.IsInterface) continue;
            
            if (type.IsAssignableTo(typeof(CustomResource)))
            {
                try
                {
                    var resourceManager = typeof(CustomResources<>).MakeGenericType(type);
                    var registerMethod = resourceManager.GetMethod("Register", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

                    if (registerMethod == null)
                    {
                        BaseLibMain.Logger.Warn($"Failed to get registration method for custom resource type {type}");
                    }
                    else if (Activator.CreateInstance(type) is not CustomResource resource)
                    {
                        BaseLibMain.Logger.Warn($"Failed to initialize custom resource type {type}");
                    }
                    else
                    {
                        BaseLibMain.Logger.Info($"Registering custom resource {type.Name}");
                        registerMethod.Invoke(null, [resource]);
                    }
                }
                catch (Exception e)
                {
                    BaseLibMain.Logger.Error($"Exception occurred registering custom resource {type}; {e}");
                }
            }
            if (type.IsAssignableTo(typeof(IAutoRegisterFormatSpecifier)))
            {
                try
                {
                    if (Activator.CreateInstance(type) is IFormatter formatter)
                    {
                        AddLaterFormatters.Add(formatter);
                        BaseLibMain.Logger.Info($"Instantiated custom format specifier {type.Name} to add later");
                    }
                    else
                    {
                        BaseLibMain.Logger.Warn($"Failed to initialize IAutoRegisterFormatSpecifier type {type}");
                    }
                }
                catch (Exception e)
                {
                    BaseLibMain.Logger.Error($"Exception occurred adding format specifier {type}; {e}");
                }
            }
        }
    }
    
    private static readonly List<IFormatter> AddLaterFormatters = [];
    [HarmonyPatch(typeof(LocManager), nameof(LocManager.LoadLocFormatters))]
    [HarmonyPostfix]
    private static void AddFormattersOnLocInit(LocManager __instance)
    {
        if (AddLaterFormatters.Count == 0) return;
        BaseLibMain.Logger.Debug($"Added {AddLaterFormatters.Count} formatters after LoadLocFormatters.");
        LocManager._smartFormatter.AddExtensions(AddLaterFormatters.ToArray());
    }


    /// <summary>
    /// After SavedPropertiesTypeCache is initialized.
    /// </summary>
    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.InitIds))]
    [HarmonyPrefix]
    private static void LatePostInit()
    {
        if (_lateInit) return;
        _lateInit = true;
        
        BaseLibMain.Logger.Info("Performing late post-mod init");
        
        foreach (var type in ReflectionHelper.ModTypes)
        {
            bool hasSavedProperty = false;
            foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var savedPropertyAttr = prop.GetCustomAttribute<SavedPropertyAttribute>();
                if (savedPropertyAttr == null) continue;
                if (prop.DeclaringType == null) continue;

                if (!SavePatchUtils.IsStoreTypeBaseSupported(prop.PropertyType))
                {
                    BaseLibMain.Logger.Warn($"SavedProperty does not support values of type {prop.PropertyType}; change {type.Name}.{prop.Name} to a SavedSpireField for BaseLib to save it.");
                }
                else if (!SavePatchUtils.IsHolderTypeBaseSupported(prop.DeclaringType))
                {
                    var endMsg = ExtendedSaveTypes.IsSaveHolderSupported(type)
                        ? "change to a SavedSpireField for BaseLib to save it."
                        : "this type is currently also unsupported by BaseLib for saved values.";
                    BaseLibMain.Logger.Warn($"SavedProperty {prop.Name} will not work on type {type.Name}; {endMsg}");
                }
                else
                {
                    hasSavedProperty = true;
                }
            }

            foreach (var field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                CheckSpecialSpireField(field);
            }

            //TODO - Remove on next beta->main merge; now already loads modded types.
            if (hasSavedProperty)
            {
                /*if (SavedPropertiesTypeCache._cache.Count == 0)
                {
                    BaseLibMain.Logger.Warn("Adding saved properties too early; type cache is still empty.");
                }*/
                
                BetaMainCompatibility.CacheSavedProperties(type);
                //SavedPropertiesTypeCache.InjectTypeIntoCache(type);
            }
        }
        SavedSpireFieldPatch.AddFieldsSorted();
    }

    private static void CheckSpecialSpireField(FieldInfo field)
    {
        Type fType = field.FieldType;
                
        if (!fType.IsGenericType)
            return;
        
        var genericTypeDef = fType.GetGenericTypeDefinition();

        if (genericTypeDef != typeof(SavedSpireField<,>) &&
            genericTypeDef != typeof(AddedNode<,>))
            return;

        field.GetValue(null); //Trigger field initialization
    }
    
    /// <summary>
    /// Registers custom scene paths.
    /// Called through a patch because virtual properties like CustomVisualPath
    /// may depend on fields set in derived constructors that haven't run yet when
    /// the base constructor occurs.
    /// </summary>
    [HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Preload))]
    class RegisterSceneConversions
    {
        [HarmonyPostfix]
        private static void EnsureScenePathsRegistered()
        {
            foreach (var type in ReflectionHelper.ModTypes)
            {
                if (type is not { IsAbstract: false, IsInterface: false }
                    || !type.IsAssignableTo(typeof(AbstractModel))
                    || !type.IsAssignableTo(typeof(ISceneConversions))) continue;
                
                var model = ModelDb.GetById<AbstractModel>(ModelDb.GetId(type));
                (model as ISceneConversions)?.RegisterSceneConversions();
            }
        }
    }
}