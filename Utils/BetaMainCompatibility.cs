using System.Reflection;
using BaseLib.Extensions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Singleton;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace BaseLib.Utils;

public class BetaMainCompatibility
{
    public static class Renamed
    {
        [Obsolete("No longer differs between main and beta.")]
        public static VariableReference<IEnumerable<Mod>> 
            LoadedMods = new(typeof(ModManager), "LoadedMods", "GetLoadedMods()");
        
        [Obsolete("No longer differs between main and beta.")]
        public static VariableReference<StringName>
            FontSize = new(typeof(ThemeConstants.Label), "FontSize", "fontSize");
        [Obsolete("No longer differs between main and beta.")]
        public static VariableReference<StringName>
            Font = new(typeof(ThemeConstants.Label), "Font", "font");
        [Obsolete("No longer differs between main and beta.")]
        public static VariableReference<StringName>
            LineSpacing = new(typeof(ThemeConstants.Label), "LineSpacing", "lineSpacing");
        [Obsolete("No longer differs between main and beta.")]
        public static VariableReference<StringName>
            OutlineSize = new(typeof(ThemeConstants.Label), "OutlineSize", "outlineSize");
        [Obsolete("No longer differs between main and beta.")]
        public static VariableReference<StringName>
            FontColor = new(typeof(ThemeConstants.Label), "FontColor", "fontColor");
        [Obsolete("No longer differs between main and beta.")]
        public static VariableReference<StringName>
            FontOutlineColor = new(typeof(ThemeConstants.Label), "FontOutlineColor", "fontOutlineColor");
        [Obsolete("No longer differs between main and beta.")]
        public static VariableReference<StringName>
            FontShadowColor = new(typeof(ThemeConstants.Label), "FontShadowColor", "fontShadowColor");
    }

    public static class AttackCommand_
    {
        public static VariableMethod TargetingAllOpponents = new((typeof(AttackCommand), "TargetingAllOpponents",
            [null],
            [0])
        );
        public static VariableMethod TargetingRandomOpponents = new((typeof(AttackCommand), "TargetingRandomOpponents",
                [null, typeof(bool)],
                [0, 1])
        );
    }

    public static class Hook_
    {
        public static VariableMethod ModifyBlock = new((typeof(Hook), "ModifyBlock", 
                [null, typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardModel), typeof(CardPlay), typeof(IEnumerable<AbstractModel>)], 
                [0, 1, 2, 3, 4, 5, 6])
        );
    }

    public static class Creature_
    {
        public static CombatStateWrapper? WrappedCombatState(Creature creature)
        {
            var state = CombatState.Get(creature);
            if (state == null) return null;
            return new CombatStateWrapper(state);
        }

        private static MethodInfo? OldInfiniteHp = typeof(Creature).PropertyGetter("ShowsInfiniteHp");
        private static MethodInfo? NewInfiniteHp = typeof(Creature).PropertyGetter(nameof(Creature.HpDisplay));
        public static bool ShowsInfiniteHp(Creature creature)
        {
            if (OldInfiniteHp != null) return (bool) (OldInfiniteHp.Invoke(creature, []) ?? throw new InvalidOperationException());
            if (NewInfiniteHp != null)
            {
                int hpDisplayEnumVal = (int?) NewInfiniteHp.Invoke(creature, []) ?? 0;
                return hpDisplayEnumVal is 1 or 2;
            }

            throw new InvalidOperationException("Could not find property for infinite hp check");
        }

        public static VariableReference<object?> CombatState = new(typeof(Creature), "CombatState");
    }

    public static class CardModel_
    {
        public static CombatStateWrapper? WrappedCombatState(CardModel card) 
        {
            var state = CombatState.Get(card);
            if (state == null) return null;
            return new CombatStateWrapper(state);
        }
        public static VariableReference<object?> CombatState = new(typeof(CardModel), "CombatState");
    }

    public static class PowerCmd_
    {
        public static VariableMethod Apply = new(
            (typeof(PowerCmd), "Apply", 
                [typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool)], 
                [0, 1, 2, 3, 4, 5]),
            (typeof(PowerCmd), "Apply", 
                [typeof(Creature), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool)], 
                [1, 2, 3, 4, 5]));
        public static VariableMethod ApplyMulti = new(
            (typeof(PowerCmd), "Apply", 
                [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool)], 
                [0, 1, 2, 3, 4, 5]),
            (typeof(PowerCmd), "Apply", 
                [typeof(IEnumerable<Creature>), typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool)], 
                [1, 2, 3, 4, 5]));
    }

    public static class RunState
    {
        public static VariableMethod IterateHookListeners = new(
            (typeof(IRunState), "IterateHookListeners", 
                [null], 
                [0])
            );
    }
}

/// <summary>
/// Reference to a field/property/method with multiple possible names.
/// </summary>
/// <typeparam name="T"></typeparam>
public class VariableReference<T>
{
    private Func<object?, T?> _get;
    
    public static implicit operator T(VariableReference<T> obj)
    {
        return obj._get.Invoke(null)!;
    }

    public T Get(object? obj = null)
    {
        return _get.Invoke(obj)!;
    }
    
    public VariableReference(params (Type, string)[] possibleReferences)
    {
        foreach (var entry in possibleReferences)
        {
            var func = TryName(entry.Item1, entry.Item2);
            if (func == null) continue;
            
            _get = func;
            return;
        }
        throw new Exception(
            $"Unable to find any field or property of type {typeof(T)} from set {string.Join(",", possibleReferences)}");
    }
    
    public VariableReference(Type definingType, params string[] possibleNames)
    {
        foreach (var name in possibleNames)
        {
            var func = TryName(definingType, name);
            if (func == null) continue;
            
            _get = func;
            return;
        }

        throw new Exception(
            $"Unable to find any field or property of type {typeof(T)} with name in \'{string.Join(",", possibleNames)}\' in type {definingType.FullName}");
    }

    private Func<object?, T?>? TryName(Type t, string name)
    {
        if (name.EndsWith("()")) //method
        {
            var method = t.GetMethod(name[..^2], BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            if (method == null) return null;
            
            if (method.GetParameters().Length > 0) throw new Exception("VariableReference only supports no-param methods; use VariableMethod instead");
            return obj => (T?) method.Invoke(obj, []);
        }
        
        var field = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        if (field != null)
        {
            return obj => (T?)field.GetValue(obj);
        }

        var prop = t.GetProperty(name);
        if (prop != null)
        {
            return obj => (T?)prop.GetValue(obj);
        }

        return null;
    }
}

public class VariableMethod
{
    private MethodInfo? _method;
    
    
    public void Invoke(object? instance, params object?[] args)
    {
        _method!.Invoke(instance, args);
    }
    
    public T? Invoke<T>(object? instance, params object?[] args)
    {
        return (T?) _method!.Invoke(instance, args);
    }

    private readonly Dictionary<Type, MethodInfo> _genericCalls = [];
    private readonly int[] _paramIndicies;

    public TReturn? InvokeGeneric<TReturn, TGeneric>(object? instance, params object?[] args)
    {
        if (!_genericCalls.TryGetValue(typeof(TGeneric), out var method))
        {
            method = _method!.MakeGenericMethod(typeof(TGeneric));
            _genericCalls[typeof(TGeneric)] = method;
        }

        var finalArgs = new object?[_paramIndicies.Length];
        for (int i = 0; i < _paramIndicies.Length; ++i)
            finalArgs[i] = args[_paramIndicies[i]];
        
        return (TReturn?) method.Invoke(instance, finalArgs);
    }

    public VariableMethod(params (string, string, Type?[], int[])[] possibleDefinitions) : this(possibleDefinitions.Select(def 
        => (TryGetType(def.Item1), def.Item2, def.Item3, def.Item4)).ToArray())
    {
    }
    public VariableMethod(params (Type?, string, Type?[], int[])[] possibleDefinitions)
    {
        _paramIndicies = [];
        foreach (var possible in possibleDefinitions)
        {
            if (possible.Item1 == null) continue;

            _method = possible.Item1.GetMethodExt(possible.Item2, possible.Item3);
            if (_method != null)
            {
                _paramIndicies = possible.Item4;
                break;
            }
        }

        if (_method == null)
            throw new Exception($"Failed to get VariableMethod {possibleDefinitions.Join(
                def => $"[{def.Item1?.Name ?? "UNKNOWN"}.{def.Item2}({def.Item3.Join(paramType => paramType?.Name ?? "ANY")})]")}");
    }

    private static Type? TryGetType(string typeName)
    {
        try
        {
            return Type.GetType($"{typeName}, sts2");
        }
        catch (Exception) { }
        return null;
    }
}

public class CombatStateWrapper(object combatState)
{
    static CombatStateWrapper()
    {
        Type? combatStateType = null;
        try
        {
            combatStateType = Type.GetType("MegaCrit.Sts2.Core.Combat.ICombatState, sts2");
        }
        catch (Exception) { }

        if (combatStateType == null) combatStateType = Type.GetType("MegaCrit.Sts2.Core.Combat.CombatState, sts2");
        //If this also fails BaseLib won't work anyways, so no try-catch
        if (combatStateType == null) throw new Exception("Failed to get combat state type in CombatStateWrapper for compatibility");

        RunStateRef = new(combatStateType, "RunState");
        AlliesRef = new(combatStateType, "Allies");
        EnemiesRef = new (combatStateType, "Enemies");
        ModifiersRef = new(combatStateType, "Modifiers");
        MultiplayerScalingModelRef = new(combatStateType, "MultiplayerScalingModel");
        RoundNumberRef = new(combatStateType, "RoundNumber");
        CurrentSideRef = new(combatStateType, "CurrentSide");
        EscapedCreaturesRef = new(combatStateType, "EscapedCreatures");
        HittableEnemiesRef = new(combatStateType, "HittableEnemies");
    }

    private static readonly VariableReference<IRunState> RunStateRef;
    private static readonly VariableReference<IReadOnlyList<Creature>> AlliesRef;
    private static readonly VariableReference<IReadOnlyList<Creature>> EnemiesRef;
    private static readonly VariableReference<IReadOnlyList<ModifierModel>> ModifiersRef;
    private static readonly VariableReference<MultiplayerScalingModel?> MultiplayerScalingModelRef;
    private static readonly VariableReference<int> RoundNumberRef;
    private static readonly VariableReference<CombatSide> CurrentSideRef;
    private static readonly VariableReference<List<Creature>> EscapedCreaturesRef;
    private static readonly VariableReference<IReadOnlyList<Creature>> HittableEnemiesRef;
    
    
    public IRunState RunState => RunStateRef.Get(combatState);

    public IReadOnlyList<Creature> Allies => AlliesRef.Get(combatState);
    public IReadOnlyList<Creature> Enemies => EnemiesRef.Get(combatState);
    public IReadOnlyList<Creature> Creatures => Allies.Concat(Enemies).ToList();
    public IReadOnlyList<Creature> PlayerCreatures => Creatures.Where(c => c.IsPlayer).ToList();
    public IReadOnlyList<Player> Players => PlayerCreatures.Select(c => c.Player!).ToList();
    
    public IReadOnlyList<ModifierModel> Modifiers => ModifiersRef.Get(combatState);
    public MultiplayerScalingModel? MultiplayerScalingModel => MultiplayerScalingModelRef.Get(combatState);

    public int RoundNumber => RoundNumberRef.Get(combatState);
    public CombatSide CurrentSide => CurrentSideRef.Get(combatState);
    public List<Creature> EscapedCreatures => EscapedCreaturesRef.Get(combatState);

    public IReadOnlyList<Creature> HittableEnemies => HittableEnemiesRef.Get(combatState);

    public IReadOnlyList<Creature> CreaturesOnCurrentSide => GetCreaturesOnSide(CurrentSide);
    public IReadOnlyList<Creature> GetCreaturesOnSide(CombatSide side)
    {
        if (side != CombatSide.Enemy)
        {
            return Allies;
        }
        return Enemies;
    }

    public IReadOnlyList<Creature> GetOpponentsOf(Creature creature)
    {
        return GetCreaturesOnSide(creature.Side.GetOppositeSide());
    }

    public IReadOnlyList<Creature> GetTeammatesOf(Creature creature)
    {
        return GetCreaturesOnSide(creature.Side);
    }

    public Player? GetPlayer(ulong playerId)
    {
        return Players.FirstOrDefault(p => p.NetId == playerId);
    }

    public bool HappenedThisTurn(CombatHistoryEntry entry)
    {
        return RoundNumber == entry.RoundNumber && CurrentSide == entry.CurrentSide;
    }
}