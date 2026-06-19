using BaseLib.Abstracts;
using BaseLib.Patches.Content;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace BaseLib.Utils;

/// <summary>
/// Class containing methods for sorting custom characters.
/// </summary>
public static class CustomCharacterUtils
{ 
    internal static List<List<Type>>? TypesToSort { get; set; } = [];

    /// <summary>
    /// Tries to order the custom characters on the character select screen and in compendium.
    /// Call this in your mod's intialize method. Returns false if called too late or if any type passed is not a CustomCharacterModel.
    /// </summary>
    /// <param name="characters">The types of your CustomCharacters</param>
    /// <returns>True if sorting was successful. False if sorting failed for some reason.</returns>
    public static bool TryOrderCustomCharacters(params List<Type> characters)
    {
        if (TypesToSort is null || characters.Any(t => !t.IsSubclassOf(typeof(CustomCharacterModel))))
        {
            return false;
        }

        TypesToSort.Add(characters);
        return true;
    }

    /// <summary>
    /// Tries to sort custom characters on the character select screen. Must be called from your mod's initialize method. Returns false if called too late.
    /// </summary>
    /// <typeparam name="T1">The first character.</typeparam>
    /// <typeparam name="T2">The second character.</typeparam>
    /// <returns>True if sorting was successful, false if this was called too late.</returns>
    public static bool TryOrderCustomCharacters<T1, T2>()
        where T1 : CustomCharacterModel
        where T2 : CustomCharacterModel
    {
        return TryOrderCustomCharacters(typeof(T1), typeof(T2));
    }

    /// <summary>
    /// Tries to sort custom characters on the character select screen. Must be called from your mod's initialize method. Returns false if called too late.
    /// </summary>
    /// <typeparam name="T1">The first character.</typeparam>
    /// <typeparam name="T2">The second character.</typeparam>
    /// <typeparam name="T3">The third character.</typeparam>
    /// <returns>True if sorting was successful, false if this was called too late.</returns>
    public static bool TryOrderCustomCharacters<T1, T2, T3>()
        where T1 : CustomCharacterModel
        where T2 : CustomCharacterModel
        where T3 : CustomCharacterModel
    {
        return TryOrderCustomCharacters(typeof(T1), typeof(T2), typeof(T3));
    }

    /// <summary>
    /// Tries to sort custom characters on the character select screen. Must be called from your mod's initialize method. Returns false if called too late.
    /// </summary>
    /// <typeparam name="T1">The first character.</typeparam>
    /// <typeparam name="T2">The second character.</typeparam>
    /// <typeparam name="T3">The third character.</typeparam>
    /// <typeparam name="T4">The fourth character.</typeparam>
    /// <returns>True if sorting was successful, false if this was called too late.</returns>
    public static bool TryOrderCustomCharacters<T1, T2, T3, T4>()
        where T1 : CustomCharacterModel
        where T2 : CustomCharacterModel
        where T3 : CustomCharacterModel
        where T4 : CustomCharacterModel
    {
        return TryOrderCustomCharacters(typeof(T1), typeof(T2), typeof(T3), typeof(T4));
    }

    /// <summary>
    /// Tries to sort custom characters on the character select screen. Must be called from your mod's initialize method. Returns false if called too late.
    /// </summary>
    /// <typeparam name="T1">The first character.</typeparam>
    /// <typeparam name="T2">The second character.</typeparam>
    /// <typeparam name="T3">The third character.</typeparam>
    /// <typeparam name="T4">The fourth character.</typeparam>
    /// <typeparam name="T5">The fifth character.</typeparam>
    /// <returns>True if sorting was successful, false if this was called too late.</returns>
    public static bool TryOrderCustomCharacters<T1, T2, T3, T4, T5>()
        where T1 : CustomCharacterModel
        where T2 : CustomCharacterModel
        where T3 : CustomCharacterModel
        where T4 : CustomCharacterModel
        where T5 : CustomCharacterModel
    {
        return TryOrderCustomCharacters(typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5));
    }

    /// <summary>
    /// Tries to sort custom characters on the character select screen. Must be called from your mod's initialize method. Returns false if called too late.
    /// </summary>
    /// <typeparam name="T1">The first character.</typeparam>
    /// <typeparam name="T2">The second character.</typeparam>
    /// <typeparam name="T3">The third character.</typeparam>
    /// <typeparam name="T4">The fourth character.</typeparam>
    /// <typeparam name="T5">The fifth character.</typeparam>
    /// <typeparam name="T6">The sixth character.</typeparam>
    /// <returns>True if sorting was successful, false if this was called too late.</returns>
    public static bool TryOrderCustomCharacters<T1, T2, T3, T4, T5, T6>()
        where T1 : CustomCharacterModel
        where T2 : CustomCharacterModel
        where T3 : CustomCharacterModel
        where T4 : CustomCharacterModel
        where T5 : CustomCharacterModel
        where T6 : CustomCharacterModel
    {
        return TryOrderCustomCharacters(typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6));
    }

    /// <summary>
    /// Tries to sort custom characters on the character select screen. Must be called from your mod's initialize method. Returns false if called too late.
    /// </summary>
    /// <typeparam name="T1">The first character.</typeparam>
    /// <typeparam name="T2">The second character.</typeparam>
    /// <typeparam name="T3">The third character.</typeparam>
    /// <typeparam name="T4">The fourth character.</typeparam>
    /// <typeparam name="T5">The fifth character.</typeparam>
    /// <typeparam name="T6">The sixth character.</typeparam>
    /// <typeparam name="T7">The seventh character.</typeparam>
    /// <returns>True if sorting was successful, false if this was called too late.</returns>
    public static bool TryOrderCustomCharacters<T1, T2, T3, T4, T5, T6, T7>()
        where T1 : CustomCharacterModel
        where T2 : CustomCharacterModel
        where T3 : CustomCharacterModel
        where T4 : CustomCharacterModel
        where T5 : CustomCharacterModel
        where T6 : CustomCharacterModel
        where T7 : CustomCharacterModel
    {
        return TryOrderCustomCharacters(typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7));
    }

    /// <summary>
    /// Tries to sort custom characters on the character select screen. Must be called from your mod's initialize method. Returns false if called too late.
    /// </summary>
    /// <typeparam name="T1">The first character.</typeparam>
    /// <typeparam name="T2">The second character.</typeparam>
    /// <typeparam name="T3">The third character.</typeparam>
    /// <typeparam name="T4">The fourth character.</typeparam>
    /// <typeparam name="T5">The fifth character.</typeparam>
    /// <typeparam name="T6">The sixth character.</typeparam>
    /// <typeparam name="T7">The seventh character.</typeparam>
    /// <typeparam name="T8">The eighth character.</typeparam>
    /// <returns>True if sorting was successful, false if this was called too late.</returns>
    public static bool TryOrderCustomCharacters<T1, T2, T3, T4, T5, T6, T7, T8>()
        where T1 : CustomCharacterModel
        where T2 : CustomCharacterModel
        where T3 : CustomCharacterModel
        where T4 : CustomCharacterModel
        where T5 : CustomCharacterModel
        where T6 : CustomCharacterModel
        where T7 : CustomCharacterModel
        where T8 : CustomCharacterModel
    {
        return TryOrderCustomCharacters(typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8));
    }

    /// <summary>
    /// Tries to sort custom characters on the character select screen. Must be called from your mod's initialize method. Returns false if called too late.
    /// </summary>
    /// <typeparam name="T1">The first character.</typeparam>
    /// <typeparam name="T2">The second character.</typeparam>
    /// <typeparam name="T3">The third character.</typeparam>
    /// <typeparam name="T4">The fourth character.</typeparam>
    /// <typeparam name="T5">The fifth character.</typeparam>
    /// <typeparam name="T6">The sixth character.</typeparam>
    /// <typeparam name="T7">The seventh character.</typeparam>
    /// <typeparam name="T8">The eighth character.</typeparam>
    /// <typeparam name="T9">The ninth character.</typeparam>
    /// <returns>True if sorting was successful, false if this was called too late.</returns>
    public static bool TryOrderCustomCharacters<T1, T2, T3, T4, T5, T6, T7, T8, T9>()
        where T1 : CustomCharacterModel
        where T2 : CustomCharacterModel
        where T3 : CustomCharacterModel
        where T4 : CustomCharacterModel
        where T5 : CustomCharacterModel
        where T6 : CustomCharacterModel
        where T7 : CustomCharacterModel
        where T8 : CustomCharacterModel
        where T9 : CustomCharacterModel
    {
        return TryOrderCustomCharacters(typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9));
    }

    /// <summary>
    /// Tries to sort custom characters on the character select screen. Must be called from your mod's initialize method. Returns false if called too late.
    /// </summary>
    /// <typeparam name="T1">The first character.</typeparam>
    /// <typeparam name="T2">The second character.</typeparam>
    /// <typeparam name="T3">The third character.</typeparam>
    /// <typeparam name="T4">The fourth character.</typeparam>
    /// <typeparam name="T5">The fifth character.</typeparam>
    /// <typeparam name="T6">The sixth character.</typeparam>
    /// <typeparam name="T7">The seventh character.</typeparam>
    /// <typeparam name="T8">The eighth character.</typeparam>
    /// <typeparam name="T9">The ninth character.</typeparam>
    /// <typeparam name="T10">The tenth character.</typeparam>
    /// <returns>True if sorting was successful, false if this was called too late.</returns>
    public static bool TryOrderCustomCharacters<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>()
        where T1 : CustomCharacterModel
        where T2 : CustomCharacterModel
        where T3 : CustomCharacterModel
        where T4 : CustomCharacterModel
        where T5 : CustomCharacterModel
        where T6 : CustomCharacterModel
        where T7 : CustomCharacterModel
        where T8 : CustomCharacterModel
        where T9 : CustomCharacterModel
        where T10 : CustomCharacterModel
    {
        return TryOrderCustomCharacters(typeof(T1), typeof(T2), typeof(T3), typeof(T4), typeof(T5), typeof(T6), typeof(T7), typeof(T8), typeof(T9), typeof(T10));
    }
}

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.Init))]
internal static class CustomCharacterSortPatch
{
    [HarmonyPostfix]
    public static void ApplySorting()
    {
        if (CustomCharacterUtils.TypesToSort is null)
        {
            BaseLibMain.Logger.Debug("Sorting was already performed.");
            return;
        }

        BaseLibMain.Logger.Debug($"Sorting {CustomContentDictionary.CustomCharacters.Count} custom characters with " +
                                 $"{CustomCharacterUtils.TypesToSort.Count} declared sort orders.");
        foreach (List<CustomCharacterModel> characters in CustomCharacterUtils.TypesToSort.Select(list =>
                     list.Select(ModelDb.Get).OfType<CustomCharacterModel>().ToList()))
        {
            CustomContentDictionary.CustomCharacters.Sort(Comparison);
            continue;

            int Comparison(CustomCharacterModel y, CustomCharacterModel x)
            {
                return characters.IndexOf(y) - characters.IndexOf(x);
            }
        }

        CustomCharacterUtils.TypesToSort = null;
    }
}