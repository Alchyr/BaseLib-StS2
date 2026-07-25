using System.Runtime.CompilerServices;
using BaseLib.Patches.Saves;
using BaseLib.Patches.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Utils;

/// <summary>
/// Marks a SpireField whose value will be copied if it has been set.
/// SpireFields utilizing this interface add themselves to <see cref="CloneFields"/>
/// if their Clone method should be called.
/// </summary>
public interface ICloneableField
{
    private static NotNullSpireField<AbstractModel, HashSet<ICloneableField>> CloneFields = new(() => []);

    /// <summary>
    /// Adds an ICloneableField to the set of fields whose Clone method will be called when the model is cloned.
    /// </summary>
    public static void AddClonedField(AbstractModel model, ICloneableField field)
    {
        CloneFields[model].Add(field);
    }
    
    /// <summary>
    /// Copies this field's data from a source model to a destination model when the model is cloned.
    /// </summary>
    public void Clone(AbstractModel src, AbstractModel dst);
    
    [HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.MutableClone))]
    private static class CloneSpireFields {
        [HarmonyPostfix]
        static void ModifyResult(AbstractModel __instance, AbstractModel __result)
        {
            var cloneFields = CloneFields[__instance];
            foreach (var cloneableField in cloneFields)
            {
                cloneableField.Clone(__instance, __result);
            }
        }
    }
}

/// <summary>
/// A basic wrapper around <seealso cref="ConditionalWeakTable{TKey, TValue}"/> for convenience.
/// While this can be used to store value types, they will be boxed and thus is somewhat inefficient.
/// </summary>
public class SpireField<TKey, TVal> : ICloneableField where TKey : class
{
    private readonly ConditionalWeakTable<TKey, object?> _table = [];
    private readonly Func<TKey, TVal?> _defaultVal;

    public SpireField(Func<TVal?> defaultVal)
    {
        _defaultVal = _ => defaultVal();
    }

    public SpireField(Func<TKey, TVal?> defaultVal)
    {
        _defaultVal = defaultVal;
    }

    /// <summary>
    /// Causes this SpireField's value to be copied when the model it is attached to is cloned. Only valid for
    /// SpireFields attached to types inheriting from AbstractModel.
    /// The value will only be copied over if it has been set at least once outside of the default value, or if the
    /// default value is a reference type and its value has been retrieved at least once.
    /// Note that this is a shallow clone; reference types will be assigned directly to the new instance, not copied.
    /// Optional cloneVal parameter will change how the value is copied to the new instance.
    /// </summary>
    /// <param name="cloneVal">A function to copy the value to the new model. Receives the source model,
    /// destination model, and the field's value for the source model. Should call Set on this SpireField with the
    /// destination model or otherwise copy values over.</param>
    public SpireField<TKey, TVal> CopyOnClone(Action<TKey, TKey, TVal?>? cloneVal = null)
    {
        if (!typeof(TKey).IsAssignableTo(typeof(AbstractModel)))
        {
            throw new InvalidOperationException(
                $"Cannot enable CopyOnClone for SpireField on type {typeof(TKey).Name}; only valid for SpireFields attached to AbstractModel types.");
        }
        _cloneFunc = cloneVal ?? ((_, dst, val) => Set(dst, val));
        return this;
    }

    private Action<TKey, TKey, TVal?>? _cloneFunc;
    /// <summary>
    /// Returns true if this SpireField's value should be copied over when a model it is attached to is cloned.
    /// </summary>
    public bool ShouldClone => _cloneFunc != null;

    /// <summary>
    /// Copies this SpireField's value from one AbstractModel to another.
    /// Only usable if this SpireField is attached to a model type.
    /// </summary>
    public void Clone(AbstractModel src, AbstractModel dst)
    {
        if (src is not TKey srcKey || dst is not TKey dstKey)
            throw new ArgumentException(
                $"Unable to clone SpireField on type {typeof(TKey).Name} from {src.GetType().Name} to {dst.GetType().Name}.");
        if (!ShouldClone) return;
        _cloneFunc!(srcKey, dstKey, Get(srcKey));
    }

    public TVal? this[TKey obj]
    {
        get => Get(obj);
        set => Set(obj, value);
    }

    public TVal? Get(TKey obj) {
        if (_table.TryGetValue(obj, out var result)) return (TVal?)result;

        _table.Add(obj, result = _defaultVal(obj));
        if (ShouldClone && !typeof(TVal).IsValueType && obj is AbstractModel model)
        {
            ICloneableField.AddClonedField(model, this);
        }
        return (TVal?)result;
    }

    public void Set(TKey obj, TVal? val)
    {
        _table.AddOrUpdate(obj, val);
        if (ShouldClone && obj is AbstractModel model)
        {
            ICloneableField.AddClonedField(model, this);
        }
    }
}

/// <summary>
/// A SpireField containing an object whose value is guaranteed to not be null.
/// </summary>
public class NotNullSpireField<TKey, TVal> : ICloneableField where TKey : class where TVal : class
{
    private readonly ConditionalWeakTable<TKey, TVal> _table = [];
    private readonly Func<TKey, TVal> _defaultVal;
    
    public NotNullSpireField(Func<TVal> defaultVal)
    {
        _defaultVal = _ => defaultVal();
    }

    public NotNullSpireField(Func<TKey, TVal> defaultVal)
    {
        _defaultVal = defaultVal;
    }

    /// <summary>
    /// Causes this SpireField's value to be copied when the model it is attached to is cloned. Only valid for
    /// SpireFields attached to types inheriting from AbstractModel.
    /// The value will only be copied over if it has been set at least once outside of the default value, or if the
    /// default value is a reference type.
    /// Note that this is a shallow clone; reference types will be assigned directly to the new instance, not copied.
    /// Optional cloneVal parameter will change how the value is copied to the new instance.
    /// </summary>
    public NotNullSpireField<TKey, TVal> CopyOnClone(Action<TKey, TKey, TVal>? cloneVal = null)
    {
        if (!typeof(TKey).IsAssignableTo(typeof(AbstractModel)))
        {
            throw new InvalidOperationException(
                $"Cannot enable CopyOnClone for SpireField on type {typeof(TKey).Name}; only valid for SpireFields attached to AbstractModel types.");
        }
        _cloneFunc = cloneVal ?? ((_, dst, val) => Set(dst, val));
        return this;
    }

    private Action<TKey, TKey, TVal>? _cloneFunc;
    /// <summary>
    /// Returns true if this SpireField's value should be copied over when a model it is attached to is cloned.
    /// </summary>
    public bool ShouldClone => _cloneFunc != null;

    /// <summary>
    /// Copies this SpireField's value from one AbstractModel to another.
    /// Only usable if this SpireField is attached to a model type.
    /// </summary>
    public void Clone(AbstractModel src, AbstractModel dst)
    {
        if (src is not TKey srcKey || dst is not TKey dstKey)
            throw new ArgumentException(
                $"Unable to clone SpireField on type {typeof(TKey).Name} from {src.GetType().Name} to {dst.GetType().Name}.");
        if (!ShouldClone) return;

        _cloneFunc!(srcKey, dstKey, Get(srcKey));
    }
    
    public TVal this[TKey obj]
    {
        get => Get(obj);
        set => Set(obj, value);
    }

    public TVal Get(TKey obj) {
        if (_table.TryGetValue(obj, out var result)) return result;

        var defaultVal = _defaultVal(obj);
        
        _table.Add(obj, defaultVal);
        if (ShouldClone && !typeof(TVal).IsValueType && obj is AbstractModel model)
        {
            ICloneableField.AddClonedField(model, this);
        }
        return defaultVal;
    }

    public void Set(TKey obj, TVal val)
    {
        _table.AddOrUpdate(obj, val);
        if (ShouldClone && obj is AbstractModel model)
        {
            ICloneableField.AddClonedField(model, this);
        }
    }
}

public class ReadonlySpireField<TKey, TVal> : NotNullSpireField<TKey, TVal> where TKey : class where TVal : class
{
    /// <inheritdoc />
    public ReadonlySpireField(Func<TVal> defaultVal) : base(defaultVal)
    {
        
    }

    /// <inheritdoc />
    public ReadonlySpireField(Func<TKey, TVal> defaultVal) : base(defaultVal)
    {
        
    }
    
    /// <summary>
    /// Throws an exception if called.
    /// </summary>
    [Obsolete("ReadonlySpireField cannot be set; exception will be thrown.")]
    public new void Set(TKey obj, TVal? val)
    {
        throw new InvalidOperationException("The value of a ReadonlySpireField should not be set. If possible, modify its current value instead.");
    }
}

internal interface IAddedNodes<TParentType> where TParentType : Node
{
    protected static List<IAddedNodes<TParentType>> _addedNodes = [];
    private static bool _patched = false;

    Node? GetNode(TParentType obj);
        
    protected static void PatchNodeReady()
    {
        if (_patched) return;
        _patched = true;
        
        BaseLibMain.Logger.Info($"Patching type {typeof(TParentType).Name} to add nodes.");
        
        var harmony = BaseLibMain.MainHarmony;
        var method = AccessTools.DeclaredMethod(typeof(TParentType), "_Ready", []);

        if (method != null)
        {
            var unconditionalMethod = typeof(IAddedNodes<TParentType>).DeclaredMethod(nameof(UnconditionalAdd));
            BaseLibMain.Logger.Info($"Adding postfix {unconditionalMethod.FullDescription()}");
            harmony.Patch(method, postfix: unconditionalMethod);
            return;
        }
        
        method = AccessTools.Method(typeof(TParentType), "_Ready", []);

        if (method == null)
        {
            BaseLibMain.Logger.Error($"Failed to patch _Ready method for type {typeof(TParentType).Name} to add nodes; _Ready method not found.");
            return;
        }

        var conditionalMethod = typeof(IAddedNodes<TParentType>).DeclaredMethod(nameof(ConditionalAdd));
        BaseLibMain.Logger.Info($"Adding postfix {conditionalMethod.FullDescription()}");
        harmony.Patch(method, postfix: conditionalMethod);
    }

    private static void UnconditionalAdd(TParentType __instance)
    {
        foreach (var add in _addedNodes)
        {
            var child = add.GetNode(__instance);
            if (__instance.IsAncestorOf(child)) return;
            __instance.AddChild(child);
        }
    }

    private static void ConditionalAdd(object __instance)
    {
        if (__instance is not TParentType parent) return;
        UnconditionalAdd(parent);
    }
}

/// <summary>
/// Adds a node as a child to all instances of the specified parent node type.
/// </summary>
public class AddedNode<TParentType, TNode> : ReadonlySpireField<TParentType, TNode>, IAddedNodes<TParentType> where TParentType : Node where TNode : Node
{
    
    public AddedNode(Func<TParentType, TNode> defaultVal) : base(defaultVal)
    {
        IAddedNodes<TParentType>._addedNodes.Add(this);
        IAddedNodes<TParentType>.PatchNodeReady();
    }

    /// <summary>
    /// An AddedNode that adds a specific scene as a child.
    /// </summary>
    /// <param name="scenePath">.tscn resource file path of the scene to instantiate.</param>
    /// <param name="extraSetup">If additional properties of the scene need to be set up before it is added as a child,
    /// or to add it as a child in a specific way.</param>
    public AddedNode(string scenePath, Action<TParentType, TNode>? extraSetup = null) :
        this(parent =>
        {
            var scene = ResourceLoader.Load<PackedScene>(scenePath).Instantiate<TNode>();
            extraSetup?.Invoke(parent, scene);
            return scene;
        })
    { }

    public Node? GetNode(TParentType obj)
    {
        return Get(obj);
    }
}

internal interface ISavedSpireField
{
    public bool IsBasegameSupported { get; }

    string Name { get; }
    Type TargetType { get; }
    void Export(object model, SavedProperties props);
    void Import(object model, SavedProperties props);
    bool RegisterCustomSave();
}

/// <summary>
/// A SpireField whose value will automatically be saved and loaded.
/// Only functions on model types that support SavedProperty, so mainly just cards and relics.
/// </summary>
public class SavedSpireField<TKey, TVal> : SpireField<TKey, TVal>, ISavedSpireField where TKey : class
{
    public SavedSpireField(Func<TVal?> defaultVal, string name) : this(_ => defaultVal(), name) { }

    public SavedSpireField(Func<TKey, TVal?> defaultVal, string name) : base(defaultVal)
    {
        string typeName = typeof(TKey).Name;
        Name = $"{typeName}_{name}";
        
        if (!SavePatchUtils.IsStoreTypeBaseSupported(typeof(TVal)) || !SavePatchUtils.IsHolderTypeBaseSupported(typeof(TKey)))
        {
            IsBasegameSupported = false;
        }
        else
        {
            IsBasegameSupported = true;
        }
        
        SavedSpireFieldPatch.Register(this);
    }

    public bool IsBasegameSupported { get; init; }

    public string Name { get; }
    public Type TargetType { get; } = typeof(TKey);

    /// <summary>
    /// Used to serialize value over net when custom save is used (value/target type not compatible with SavedProperty)
    /// </summary>
    public Action<TVal, PacketWriter>? Serializer { get; set; } = null;
    /// <summary>
    /// Used to deserialize value over net when custom save is used (value/target type not compatible with SavedProperty)
    /// </summary>
    public Func<PacketReader, TVal>? Deserializer { get; set; } = null;

    /// <summary>
    /// Store value in a SavedProperties instance.
    /// </summary>
    public void Export(object model, SavedProperties props)
    {
        AddToProperties(props, Name, Get((TKey)model));
    } 

    /// <summary>
    /// Load value from a SavedProperties instance.
    /// </summary>
    public void Import(object model, SavedProperties props)
    {
        if (TryGetFromProperties<TVal>(props, Name, out var val))
            Set((TKey)model, val);
    }

    public bool RegisterCustomSave()
    {
        Action<TVal, PacketWriter>? serializer = Serializer;
        Func<PacketReader, TVal>? deserializer = Deserializer;
        if (serializer == null || deserializer == null)
        {
            if (typeof(TVal).IsAssignableTo(typeof(IPacketSerializable)))
            {
                serializer = (val, writer) => ((IPacketSerializable)val!).Serialize(writer);
                deserializer = (reader) =>
                {
                    var val = (TVal)typeof(TVal).CreateInstance();
                    ((IPacketSerializable)val).Deserialize(reader);
                    return val;
                };
            }
            else if (!SavePatchUtils.TryGetSerializerDeserializer(out serializer, out deserializer))
            {
                BaseLibMain.Logger.Error($"Unable to register custom save for SavedSpireField {Name}; no serialization defined for " +
                                         $"type {typeof(TVal).Name}. Set Serializer/Deserializer properties of SavedSpireField.");
                return false;
            }
        }

        if (!ExtendedSaveTypes.IsSaveTypeSupported(typeof(TVal)))
        {
            throw new ArgumentException($"Type {typeof(TVal).Name} is not registered for saving; register the type with ExtendedSaveTypes in your mod's initializer.");
        }
        
        return ExtendedSaveTypes.RegisterSavedValue<TKey, TVal>($"spirefield_{Name}", Get, Set, serializer, deserializer);
    }

    private static void AddToProperties(SavedProperties props, string name, object? value)
    {
        switch (value)
        {
            case null:
                return;
            case int i:
                (props.ints ??= []).Add(new(name, i));
                break;
            case bool b:
                (props.bools ??= []).Add(new(name, b));
                break;
            case string s:
                (props.strings ??= []).Add(new(name, s));
                break;
            case Enum e:
                (props.ints ??= []).Add(new(name, Convert.ToInt32(e)));
                break;
            case ModelId mid:
                (props.modelIds ??= []).Add(new(name, mid));
                break;
            case SerializableCard card:
                (props.cards ??= []).Add(new(name, card));
                break;
            case int[] iArr:
                (props.intArrays ??= []).Add(new(name, iArr));
                break;
            case Enum[] eArr:
                (props.intArrays ??= []).Add(new(name, eArr.Select(Convert.ToInt32).ToArray()));
                break;
            case SerializableCard[] cArr:
                (props.cardArrays ??= []).Add(new(name, cArr));
                break;
            case List<SerializableCard> cList:
                (props.cardArrays ??= []).Add(new(name, cList.ToArray()));
                break;
        }
    }

    private static bool TryGetFromProperties<T>(SavedProperties props, string name, out T? value)
    {
        value = default;

        if (typeof(T) == typeof(int) || typeof(T).IsEnum)
        {
            var found = props.ints?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            value = typeof(T).IsEnum
                ? (T)Enum.ToObject(typeof(T), found.Value.value)
                : (T)(object)found.Value.value;
            return true;
        }
        else if (typeof(T) == typeof(bool))
        {
            var found = props.bools?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            value = (T)(object)found.Value.value;
            return true;
        }
        else if (typeof(T) == typeof(string))
        {
            var found = props.strings?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            value = (T)(object)found.Value.value;
            return true;
        }
        else if (typeof(T) == typeof(ModelId))
        {
            var found = props.modelIds?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            value = (T)(object)found.Value.value;
            return true;
        }
        else if (
            typeof(T) == typeof(int[])
            || (typeof(T).IsArray && typeof(T).GetElementType()!.IsEnum)
        )
        {
            var found = props.intArrays?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            if (typeof(T).IsArray && typeof(T).GetElementType()!.IsEnum)
            {
                Type enumType = typeof(T).GetElementType()!;
                Array enumArr = Array.CreateInstance(enumType, found.Value.value.Length);
                for (int i = 0; i < found.Value.value.Length; i++)
                    enumArr.SetValue(Enum.ToObject(enumType, found.Value.value[i]), i);
                value = (T)(object)enumArr;
            }
            else
            {
                value = (T)(object)found.Value.value;
            }
            return true;
        }
        else if (typeof(T) == typeof(SerializableCard))
        {
            var found = props.cards?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            value = (T)(object)found.Value.value;
            return true;
        }
        else if (
            typeof(T) == typeof(SerializableCard[])
            || typeof(T) == typeof(List<SerializableCard>)
        )
        {
            var found = props.cardArrays?.FirstOrDefault(p => p.name == name);
            if (found == null) return false;
            
            value =
                typeof(T) == typeof(List<SerializableCard>)
                    ? (T)(object)found.Value.value.ToList()
                    : (T)(object)found.Value.value;
            return true;
        }
        return false;
    }
}