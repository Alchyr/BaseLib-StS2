using BaseLib.Abstracts;
using BaseLib.CustomAttribute;
using BaseLib.Extensions;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BaseLib.Patches.Content;

[HarmonyPatch(typeof(ModelDb), nameof(ModelDb.GetEntry))]
public static partial class PrefixIdPatch
{
    private static readonly ConcurrentDictionary<Type, string?> IdCache = new();

    [HarmonyPostfix]
    static string AdjustID(string __result, Type type)
    {
        if (IdCache.TryGetValue(type, out var cachedId))
        {
            if (cachedId != null)
            {
                return cachedId;
            }
        }

        var attr = type.GetCustomAttribute<CustomIDAttribute>();
        if (attr != null)
        {
            var customId = BuildCustomEntry(type, attr.ID ?? type.Name);
            IdCache[type] = customId;
            return customId;
        }

        if (type.IsAssignableTo(typeof(ICustomModel)))
        {
            //MainFile.Logger.Info(s);
            return type.GetPrefix() + __result;
        }
        return __result;
    }

    public static Mod? GetOwningMod(Type type)
    {
        var asm = type.Assembly;
        return ModManager.GetLoadedMods().FirstOrDefault(m => m.assembly == asm);
    }

    public static string? GetModId(Type type)
    {
        return GetOwningMod(type)?.manifest?.id;
    }

    private static string BuildCustomEntry(Type type, string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
            throw new InvalidOperationException($"CustomIDAttribute on {type.FullName} has an empty ID.");

        var ns = type.Namespace;
        if (string.IsNullOrWhiteSpace(ns))
            throw new InvalidOperationException($"Type {type.FullName} has no namespace, cannot build custom ID.");

        var nsParts = ns.Split('.', StringSplitOptions.RemoveEmptyEntries);
        switch (nsParts.Length)
        {
            case 0:
                return $"{NormalizeIdPart(GetModId(type) ?? "NoNamespace")}_{NormalizeIdPart(rawId)}";
            case 1:
                return $"{NormalizeIdPart(nsParts[0])}_{NormalizeIdPart(rawId)}";
        }

        var firstNs = NormalizeIdPart(nsParts[0]);
        var lastNs = NormalizeIdPart(nsParts[^1]);
        var id = NormalizeIdPart(rawId);

        return $"{firstNs}_{lastNs}_{id}";
    }

    private static string NormalizeIdPart(string value)
    {
        value = value.Trim();

        if (!Normal().IsMatch(value))
        {
            return ToUppercaseEnglishString(value, 16);
        }

        return value.ToUpperInvariant();
    }

    [GeneratedRegex("^[A-Za-z0-9_]+$")]
    private static partial Regex Normal();

    public static string ToUppercaseEnglishString(string input, int length)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);

        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ_";
        byte[] salt = Encoding.UTF8.GetBytes("Any_Fixed_Global_Salt_String");

        byte[] hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            password: input,
            salt: salt,
            iterations: 10,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: length
        );

        char[] result = new char[length];

        for (int i = 0; i < length; i++)
        {
            result[i] = alphabet[hashBytes[i] % alphabet.Length];
        }

        return new string(result);
    }
}
