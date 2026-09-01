using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace BaseLib.Utils.ModInterop.DynamicWrappers;

internal readonly record struct Lookup(string TargetModId, Type SourceType) { public override readonly string ToString() => $"{TargetModId}, {SourceType}"; }
internal readonly record struct InteropData(string SourceModId, Type TargetInterface, DynamicWrapperFactory Factory, IDictionary<string, Delegate> Delegates) { public override readonly string ToString() => $"{SourceModId}, {TargetInterface}, {Factory.Target}"; }
internal readonly record struct ReverseLookup(string SourceModId, Type TargetInterface) { public override readonly string ToString() => $"{SourceModId}, {TargetInterface}"; }
internal readonly record struct ReverseInteropData(string TargetModId, Type SourceInterface, DynamicWrapperFactory ReverseFactory, IDictionary<string, Delegate> ReverseDelegates) { public override readonly string ToString() => $"{TargetModId}, {SourceInterface}, {ReverseFactory.Target}"; }

/// <summary>
/// A factory method to create an <see cref="IDynamicWrapper"/> from the supplied arguments. 
/// </summary>
/// <param name="wrapperModId">The mod that the wrapper is for.</param>
/// <param name="instanceModId">The mod that the underlying instance object is from.</param>
/// <param name="instance">The underlying instance object.</param>
/// <param name="delegates">A collection of delegates that will be used to map the wrapper to the instance object.</param>
/// <returns>A dynamically generated wrapper class for the supplied object, ready for inter-mod communication.</returns>
internal delegate IDynamicWrapper DynamicWrapperFactory(string wrapperModId, string instanceModId, object instance, IDictionary<string, Delegate> delegates);

/// <summary>
/// Wraps objects for use by another mod using interfaces, and allowing two-way communication between them. The wrapped object must implement an identical/mirror interface to that of the other mod.
/// </summary>
/// <remarks>
/// For this to work, you need to first declare all such interfaces you require by calling DeclareInterfaces (or one of its's overloads).
/// Then, call RegisterType 
/// </remarks>
public class DynamicWrapper
{
    private static readonly Dictionary<string, HashSet<Type>> DeclaredInterfaces = [];
    private static readonly Dictionary<string, HashSet<Type>> MirroredInterfaces = []; // Used for reverse-wrapper's internal wrapping
    internal static readonly Dictionary<Lookup, InteropData> InteropLookup = [];
    private static readonly Dictionary<ReverseLookup, ReverseInteropData> ReverseInteropLookup = [];

    internal static Dictionary<string, List<Assembly>> loadedModAssemblies = null!; // keeping this alive allows a mod to declare their interfaces at their leisure. Otherwise would need to enforce the attribute.

    /// <summary>
    /// Fired whenever a type is registered.
    /// </summary>
    public static event TypeRegisteredEventHandler? TypeRegistered;

    /// <summary>
    /// The mod that will be receiving objects.
    /// </summary>
    public string TargetModId { get; }

    /// <summary>
    /// The mod that will be creating and sending objects.
    /// </summary>
    public string? SourceModId { get; }

    /// <summary>
    /// Creates a local instance with the specified <paramref name="targetModId"/>. All method calls pass through to the static methods, using the supplied modId. No additional functionality.
    /// </summary>
    /// <remarks>
    /// Since you are not specifiying a sourceModId, you cannot call the following methods (would result in error):
    /// <list type="bullet">
    /// <item>DeclareInterfaces</item>
    /// <item>RegisterType</item>
    /// <item>ReverseWrap</item>
    /// </list>
    /// </remarks>
    /// <param name="targetModId">The mod that you are wrapping objects for. If you are using this overload, it is probably your own, and you might be expecting to receive external objects.</param>
    public DynamicWrapper(string targetModId) : this(targetModId, null) { }

    /// <summary>
    /// Creates a local instance with the specified <paramref name="targetModId"/> and <paramref name="sourceModId"/>. All method calls pass through to the static methods, using the supplied modIds. No additional functionality.
    /// </summary>
    /// <param name="targetModId">The mod that you are wrapping objects for (usually the optional mod).</param>
    /// <param name="sourceModId">The mod that will be supplying the objects. Usually your own mod. Supply <see langword="null"/> if you are just providing support for your mod to be used as the optional mod (in this case, <paramref name="targetModId"/> should be your mod).</param>
    public DynamicWrapper(string targetModId, string? sourceModId)
    {
        TargetModId = targetModId;
        SourceModId = sourceModId;
    }

    /// <inheritdoc cref="DeclareInterfaceInternal(string, Type?, string?, Type?, string?)"/>
    public void DeclareInterface(Type sourceInterface, string? targetInterfaceName = null)
    {
        if (SourceModId == null)
            throw SourceModNullException(nameof(SourceModId));

        DeclareInterfaces(TargetModId, SourceModId, sourceInterface, targetInterfaceName);
    }

    /// <inheritdoc cref="DeclareInterfaces(string, string, IEnumerable{Type})"/>
    public void DeclareInterfaces(IEnumerable<Type> interfaces)
    {
        if (SourceModId == null)
            throw SourceModNullException(nameof(SourceModId));

        DeclareInterfaces(TargetModId, SourceModId, interfaces);
    }

    /// <inheritdoc cref="DeclareInterfaceInternal(string, Type?, string?, Type?, string?)"/>
    public static void DeclareInterfaces(string targetModId, Type targetInterface)
    {
        DeclareInterfaceInternal(targetModId, targetInterface, null, null);
    }

    /// <inheritdoc cref="DeclareInterfaceInternal(string, Type?, string?, Type?, string?)"/>
    /// <param name="targetModId">The mod that defines these interfaces, that you intend to use for optional mod interop.</param>
    /// <param name="targetInterfaces">The interfaces.</param>
    public static void DeclareInterfaces(string targetModId, IEnumerable<Type> targetInterfaces)
    {
        foreach (Type targetInterface in targetInterfaces)
        {
            DeclareInterfaceInternal(targetModId, targetInterface, null, null);
        }
    }

    /// <inheritdoc cref="DeclareInterfaceInternal(string, Type?, string?, Type?, string?)"/>
    public static void DeclareInterfaces(string targetModId, string sourceModId, Type sourceInterface, string? targetInterfaceName = null)
    {
        DeclareInterfaceInternal(targetModId, null, sourceModId, sourceInterface, targetInterfaceName);
    }

    /// <inheritdoc cref="DeclareInterfaceInternal(string, Type?, string?, Type?, string?)"/>
    /// <param name="targetModId">The mod that defines the original interfaces, that you intend to use for optional mod interop.</param>
    /// <param name="sourceModId">The mod that is defining the mirror interfaces.</param>
    /// <param name="sourceInterfaces">The mirror interfaces of ones belonging to <paramref name="targetModId"/>.</param>
    public static void DeclareInterfaces(string targetModId, string sourceModId, IEnumerable<Type> sourceInterfaces)
    {
        foreach (Type sourceInterface in sourceInterfaces)
        {
            DeclareInterfaceInternal(targetModId, null, sourceModId, sourceInterface);
        }
    }

    /// <summary>
    /// Marks a set of interfaces belonging to <paramref name="targetModId"/> (aka the optional mod) for use by dynamic mod interop.
    /// All such interfaces must be declared here before any sourceMods call RegisterType(), otherwise generated wrappers may be incomplete, or fail altogether.
    /// </summary>
    /// <remarks>
    /// It doesn't matter who actually registers these interfaces, as long as it is performed before any source mods register their own types against them.
    /// </remarks>
    /// <param name="targetModId">The mod that defines these interfaces, that you intend to use for optional mod interop.</param>
    /// <param name="targetInterface">The target interface to register. Duplicates will be discarded.</param>
    /// <param name="sourceModId">The mod that is defining the mirror interfaces.</param>
    /// <param name="sourceInterface">The mirror interface of <paramref name="targetInterface"/>.</param>
    /// <param name="targetInterfaceName">The namespace qualified name of <paramref name="sourceInterface"/> in targetMod's assembly. The type name must match exactly.
    /// If <see langword="null"/>, uses the type name of <paramref name="sourceInterface"/> (this would only be a problem if target mod has two types of the same name).</param>
    internal static void DeclareInterfaceInternal(string targetModId, Type? targetInterface, string? sourceModId, Type? sourceInterface, string? targetInterfaceName = null) // At least one of either targetInterface or sourceInterface should not be null
    {
        if (!DeclaredInterfaces.TryGetValue(targetModId, out HashSet<Type>? types))
        {
            types = [];
            DeclaredInterfaces[targetModId] = types;
        }

        if (targetInterface != null || TryGetTypeFromModId(targetModId, sourceInterface!.Name, targetInterfaceName, out targetInterface))
        {
            types.Add(targetInterface);

            if (sourceModId != null && sourceInterface != null)
            {
                DeclareMirrorInterface(sourceModId, sourceInterface);
                BaseLibMain.Logger.Info($"Declared mirror interfaces {targetInterface.FullName} and {sourceInterface.FullName} for dynamic interop");
            }
            else
            {
                BaseLibMain.Logger.Info($"{targetModId} declared {targetInterface.FullName} for use by dynamic interop.");
            }
        }
        else
        {
            BaseLibMain.Logger.Error($"Interface {sourceInterface!.Name} from {targetModId} could not be found");
        }
    }

    private static bool TryGetTypeFromModId(string modId, string typeName, string? namespaceQualitfiedName, [NotNullWhen(true)] out Type? type)
    {
        if (loadedModAssemblies.TryGetValue(modId, out var assemblies))
        {
            foreach (Assembly assembly in assemblies)
            {
                type = namespaceQualitfiedName != null
                    ? assembly.GetType(namespaceQualitfiedName)
                    : assembly.GetExportedTypes().FirstOrDefault(t => t.Name == typeName);
                if (type != null)
                    return true;
            }
        }
        type = null;
        return false;
    }

    internal static void ProcessType(Type sourceInterface)
    {
        InteropInterfaceAttribute? attr = sourceInterface.GetCustomAttribute<InteropInterfaceAttribute>();
        if (attr == null || !loadedModAssemblies.TryGetValue(attr.TargetModId, out List<Assembly>? assemblies))
            return;

        DeclareInterfaceInternal(attr.TargetModId, null, attr.SourceModId, sourceInterface, attr.TargetInterfaceName);
    }

    /// <summary>
    /// Marks the set of mirror interfaces belonging to <paramref name="sourceModId"/> (generally your own mod), that will allow generated reverse-wrappers to automatically wrap incoming and outgoing objects.
    /// </summary>
    /// <remarks>May not be required, but depends on use case. You can specify them here just to be safe.</remarks>
    /// <param name="sourceModId">The mod that defines this interface, that may be receiving data back from an optional mod.</param>
    /// <param name="sourceInterface">The interface to register. Duplicates will be discarded.</param>
    internal static void DeclareMirrorInterface(string sourceModId, Type sourceInterface)
    {
        if (MirroredInterfaces.TryGetValue(sourceModId, out HashSet<Type>? types))
        {
            types.Add(sourceInterface);
        }
        else
        {
            MirroredInterfaces[sourceModId] = [sourceInterface];
        }
    }



    /// <inheritdoc cref="RegisterTypeInternal(string, string, Type, Type, string?)"/>
    public void RegisterType(Type sourceInterfaceType, string? interfaceName = null)
    {
        RegisterTypeInternal(TargetModId, SourceModId, sourceInterfaceType, sourceInterfaceType, interfaceName);
    }

    /// <inheritdoc cref="RegisterTypeInternal(string, string, Type, Type, string?)"/>
    public void RegisterType(Type sourceType, Type sourceInterfaceType, string? interfaceName = null)
    {
        RegisterTypeInternal(TargetModId, SourceModId, sourceType, sourceInterfaceType, interfaceName);
    }

    /// <inheritdoc cref="RegisterTypeInternal(string, string, Type, Type, string?)"/>
    public static void RegisterType(string targetModId, string sourceModId, Type sourceInterfaceType, string? interfaceName = null)
    {
        RegisterTypeInternal(targetModId, sourceModId, sourceInterfaceType, sourceInterfaceType, interfaceName);
    }

    /// <inheritdoc cref="RegisterTypeInternal(string, string, Type, Type, string?)"/>
    public static void RegisterType(string targetModId, string sourceModId, Type sourceType, Type sourceInterfaceType, string? interfaceName = null)
    {
        RegisterTypeInternal(targetModId, sourceModId, sourceType, sourceInterfaceType, interfaceName);
    }

    /// <summary>
    /// Registers a type and/or interface as being a mirror for another mod's interface, and to generate dynamic wrappers that allow each others' interfaces to communicate.
    /// <para/>
    /// What exactly you need to register depends on the target mod's implementation. At minimum, you need to register your mirror interfaces that you are using.
    /// You may also need to register some concrete classes. For example a mod that iterates over the game hooks will receive models directly from the game, in this case you would need to
    /// register the model's type itself, so the correct wrapper can be found in the lookup table. Consider using the <see cref="IWrappable"/> interface to provide a hint as to which
    /// targetMod and interface the object is for. Check the logs for any error messages, they will indicate which types require registration.
    /// </summary>
    /// <remarks>
    /// Note that dynamic wrappers dont work with debugger step-through, since they have no source code.
    /// </remarks>
    /// <param name="targetModId">The target mod (usually the optional mod).</param>
    /// <param name="sourceModId">Your own mod, usually.</param>
    /// <param name="sourceType">The type that you are registering, that implements <paramref name="sourceInterfaceType"/>. Will implicity register <paramref name="sourceInterfaceType"/> if not already done.</param>
    /// <param name="sourceInterfaceType">The interface that is a mirror for one in <paramref name="targetModId"/>.
    /// Must have same name and implementation as targetMod's (except for any other interfaces from <paramref name="targetModId"/> - they need to be mirrored and registered as well).</param>
    /// <param name="interfaceName">If not <see langword="null"/>, will use this name instead of your interface's name when searching for target interface.</param>
    /// <exception cref="NotSupportedException"><paramref name="sourceInterfaceType"/> must be an interface.</exception>
    /// <exception cref="ArgumentException">Either:
    /// <list type="bullet">
    /// <item><paramref name="sourceType"/> or <paramref name="sourceInterfaceType"/> does not implement all members of the target interface <paramref name="interfaceName"/>.</item>
    /// <item>The interface '<paramref name="interfaceName"/>' has not been registered.</item>
    /// </list>
    /// </exception>
    private static InteropData RegisterTypeInternal(string targetModId, string? sourceModId, Type sourceType, Type sourceInterfaceType, string? interfaceName)
    {
        if (!sourceInterfaceType.IsInterface)
        {
            throw new NotSupportedException($"The provided {nameof(sourceInterfaceType)} of type '{sourceInterfaceType.GetType()}' is not an interface. DynamicWrapper currently only supports interfaces.");
        }

        if (sourceModId == null)
        {
            throw SourceModNullException(nameof(sourceModId));
        }

        if (InteropLookup.TryGetValue(new(targetModId, sourceInterfaceType), out InteropData value))
        {
            // Already generated for this interface, we can reuse the data for this type
            // Nothing to add to reverse lookups; they only require the interface, not concrete types, and it too will have already been generated
            InteropLookup[new(targetModId, sourceType)] = value;
            TypeRegistered?.Invoke(new(targetModId, sourceModId, sourceType, sourceInterfaceType, value.TargetInterface));
            return value;
        }
        else if (sourceInterfaceType != sourceType)
        {
            // Register the interface first, then the concrete type can copy that data
            InteropData data = RegisterTypeInternal(targetModId, sourceModId, sourceInterfaceType, sourceInterfaceType, interfaceName);
            InteropLookup[new(targetModId, sourceType)] = data;
            return data;
        }

        Type? referenceInterfaceType = null;
        interfaceName ??= sourceInterfaceType.Name;
        if (DeclaredInterfaces.TryGetValue(targetModId, out HashSet<Type>? declaredTypes))
        {
            foreach (Type refType in declaredTypes)
            {
                if (interfaceName == refType.Name)
                {
                    referenceInterfaceType = refType;
                    break;
                }
            }
        }

        // Generate the delegates that will map wrappers to their object instance
        // Could do this without delegates (they are from the original implementation). But at this point I dont want to rewrite it.
        if (referenceInterfaceType != null)
        {
            Dictionary<string, Delegate> delegates = [];
            Dictionary<string, Delegate> reverseDelegates = [];

            foreach (MethodInfo refMethod in referenceInterfaceType.GetMethods())
            {
                Type[] refParamTypes = [.. refMethod.GetParameters().Select(pInfo => pInfo.ParameterType)];
                Type[] sourceParamTypes = null!;
                MethodInfo? sourceMethod = sourceType.GetMethods().FirstOrDefault(method =>
                {
                    // Check for matching return and param types, unless the type is a declared type (then it will get wrapped instead)
                    sourceParamTypes = [.. method.GetParameters().Select(pInfo => pInfo.ParameterType)];
                    if (method.Name == refMethod.Name && sourceParamTypes.Length == refParamTypes.Length && MatchTypes(method.ReturnType, refMethod.ReturnType, declaredTypes!))
                    {
                        for (int i = 0; i < sourceParamTypes.Length; i++)
                        {
                            if (!MatchTypes(sourceParamTypes[i], refParamTypes[i], declaredTypes!))
                            {
                                return false;
                            }
                        }
                        return true;
                    }
                    return false;

                    static bool MatchTypes(Type checkType, Type refType, HashSet<Type> declaredTypes)
                    {
                        return checkType == refType || checkType.IsEnum && refType.IsEnum || declaredTypes.Contains(refType);
                    }
                });
                if (sourceMethod != null)
                {
                    Type delegateType = Expression.GetDelegateType([sourceType, .. sourceParamTypes, sourceMethod.ReturnType]); // Func<TParam1, ..., TReturn> -or- Func<TInstance, TParam1, ..., TReturn> if using an open (static) delegate (pass null for the target to Delegate.CreateDelegate(). Is an Action<> for void methods.
                    Delegate del = Delegate.CreateDelegate(delegateType, null, sourceMethod); // can load directly into fields of type Func<,>
                    delegates.Add(sourceMethod.Name, del);

                    Type reverseDelegateType = Expression.GetDelegateType([referenceInterfaceType, .. refParamTypes, refMethod.ReturnType]);
                    del = Delegate.CreateDelegate(reverseDelegateType, null, refMethod);
                    reverseDelegates.Add(refMethod.Name, del);
                }
                else
                {
                    throw new ArgumentException($"The provided type '{sourceType}' does not implement the required interface members (missing '{referenceInterfaceType.FullName}.{refMethod.Name}').");
                }
            }

            var _delegates = delegates.AsReadOnly();
            var _reverseDelegates = reverseDelegates.AsReadOnly();

            // While its possible to define compile-time wrappers using dynamic objects, you can only do this for your own side, and I think it only makes sense if both parties do this.
            // Adds a bunch of complexity, and doesnt gain a whole lot (functionally identically, and you'll suffer the DLR kicking in on first use), so not giving the option.
            DynamicWrapperFactory factory = CreateWrapperFactory(referenceInterfaceType, sourceInterfaceType, _delegates, targetModId, sourceModId, isReverse: false);
            DynamicWrapperFactory reverseFactory = CreateWrapperFactory(sourceInterfaceType, referenceInterfaceType, _reverseDelegates, targetModId, sourceModId, isReverse: true);

            InteropData data = new(sourceModId, referenceInterfaceType, factory, _delegates);
            ReverseInteropData reverseData = new(targetModId, sourceInterfaceType, reverseFactory, _reverseDelegates);

            InteropLookup[new(targetModId, sourceType)] = data;
            ReverseInteropLookup[new(sourceModId, referenceInterfaceType)] = reverseData;

            BaseLibMain.Logger.Info($"Built DynamicWrappers for interop between {targetModId} and {sourceModId} for interface {referenceInterfaceType}");

            TypeRegistered?.Invoke(new(targetModId, sourceModId, sourceType, sourceInterfaceType, referenceInterfaceType));

            return data;
        }
        else
        {
            throw new ArgumentException($"The provided type '{interfaceName}' was not found in {nameof(DeclaredInterfaces)}. Either wait for the target mod to add it, or add it yourself by calling {nameof(DeclareInterfaces)}.");
        }
    }

    /// <inheritdoc cref="Wrap{T}(string, object, Type?)"/>
    public object Wrap(object objectToWrap)
    {
        return Wrap(TargetModId, objectToWrap);
    }

    /// <inheritdoc cref="Wrap{T}(string, object, Type?)"/>
    public T Wrap<T>(object objectToWrap)
    {
        return Wrap<T>(TargetModId, objectToWrap, null);
    }

    /// <inheritdoc cref="Wrap{T}(string, object, Type?)"/>
    public T Wrap<T>(object objectToWrap, Type? implementingInterface)
    {
        return Wrap<T>(TargetModId, objectToWrap, implementingInterface);
    }

    /// <inheritdoc cref="Wrap{T}(string, object, Type?)"/>
    public static object Wrap(string targetModId, object objectToWrap)
    {
        Type type = objectToWrap.GetType();

        if (objectToWrap is IWrappable wrappable && wrappable.TargetModId == targetModId && InteropLookup.TryGetValue(new(targetModId, wrappable.InterfaceType), out InteropData data)
            || InteropLookup.TryGetValue(new(targetModId, type), out data))
        {
            if (type.IsAssignableTo(data.TargetInterface))
            {
                return objectToWrap; // No wrapper required
            }
            else if (objectToWrap is IDynamicWrapper wrapper && wrapper.Instance.GetType().IsAssignableTo(data.TargetInterface))
            {
                return wrapper.Instance; // Strip the current wrapper
            }
            return data.Factory(targetModId, data.SourceModId, objectToWrap, data.Delegates);
        }

        throw new InvalidOperationException($"The type '{objectToWrap.GetType()}' has not been registered with {targetModId}. You must call {nameof(DynamicWrapper)}.{nameof(RegisterType)}() first.");
    }

    /// <inheritdoc cref="Wrap{T}(string, object, Type?)"/>
    public static T Wrap<T>(string targetModId, object objectToWrap)
    {
        return Wrap<T>(targetModId, objectToWrap, null);
    }

    /// <summary>
    /// Wraps <paramref name="objectToWrap"/> for <paramref name="targetModId"/> consumption.
    /// </summary>
    /// <typeparam name="T">The interface to wrap <paramref name="objectToWrap"/> with. This interface must be native to <paramref name="targetModId"/>.</typeparam>
    /// <param name="targetModId">The modId that is the intended recipient of <paramref name="objectToWrap"/>.</param>
    /// <param name="objectToWrap">The object to wrap.</param>
    /// <param name="implementingInterface">The mirror interface that the object implements, if known. Used for lookup. Otherwise a value of <see langword="null"/> will use concrete type of the object.</param>
    /// <returns>Either an <see cref="IDynamicWrapper"/> implenting an interface that <paramref name="targetModId"/> can consume, or the object instance itself if it is native to <paramref name="targetModId"/>.</returns>
    /// <exception cref="InvalidOperationException">The interface <typeparamref name="T"/> has not been registered by <paramref name="targetModId"/>.</exception>
    public static T Wrap<T>(string targetModId, object objectToWrap, Type? implementingInterface) // Same as the non-generic overload, but defers the dict lookup and has one extra type check. Is it worth it?
    {
        // WARNING: If you change this method signature, need to update the Linq query and IL in CreateWrapperFactory (look for the matching comment)
        Type type = objectToWrap.GetType();

        if (type.IsAssignableTo(typeof(T)))
        {
            return (T)objectToWrap; // No wrapper required
        }
        else if (objectToWrap is IDynamicWrapper wrapper && wrapper.Instance.GetType().IsAssignableTo(typeof(T)))
        {
            return (T)wrapper.Instance; // Strip the current wrapper
        }

        // Try each type that we have, if one fails maybe the next hits
        if (implementingInterface != null && InteropLookup.TryGetValue(new(targetModId, implementingInterface), out InteropData data)
            || objectToWrap is IWrappable wrappable && wrappable.TargetModId == targetModId && InteropLookup.TryGetValue(new(targetModId, wrappable.InterfaceType), out data)
            || InteropLookup.TryGetValue(new(targetModId, type), out data))
        {
            return (T)data.Factory(targetModId, data.SourceModId, objectToWrap, data.Delegates);
        }

        throw new InvalidOperationException($"The type '{objectToWrap.GetType()}' has not been registered with {targetModId} and it's interface '{typeof(T)}'. You must call {nameof(DynamicWrapper)}.{nameof(RegisterType)}() first.");
    }

    /// <inheritdoc cref="ReverseWrap{T}(string, T)"/>
    public object? ReverseWrap<T>(T? objectToWrap)
    {
        if (SourceModId == null)
            throw SourceModNullException(nameof(SourceModId));

        return ReverseWrap<T>(SourceModId, objectToWrap);
    }

    /// <summary>
    /// Wraps <paramref name="objectToWrap"/> for <paramref name="sourceModId"/> consumption (in other words, optionalMod back to sourceMod).
    /// </summary>
    /// <remarks>Calling this explicity is generally not required; the generated wrappers will call this automatically as needed, so long as all relevant interfaces have been registered.</remarks>
    /// <typeparam name="T">The interface of <paramref name="objectToWrap"/>. This must match a registered mirror interface in <paramref name="sourceModId"/>.</typeparam>
    /// <param name="sourceModId">The modId that is the intended recipient of <paramref name="objectToWrap"/>, and has registered their mirror version of <typeparamref name="T"/>.</param>
    /// <param name="objectToWrap">The object to wrap.</param>
    /// <returns>Either an <see cref="IDynamicWrapper"/> implenting an interface that <paramref name="sourceModId"/> can consume, or the object instance itself if it is native to <paramref name="sourceModId"/>.</returns>
    /// <exception cref="InvalidOperationException">The interface <typeparamref name="T"/> has not been registered by <paramref name="sourceModId"/>.</exception>
    public static object? ReverseWrap<T>(string sourceModId, T? objectToWrap) // Return object because we dont know what type it will be (it will be the mirror of T)
    {
        // WARNING: If you change this method signature, need to update the Linq query and IL in CreateWrapperFactory (look for the matching comment)
        if (objectToWrap == null)
            return null;

        object obj = objectToWrap;

        if (obj is IDynamicWrapper wrapper)
        {
            obj = wrapper.Instance; // Strip the current wrapper
            if (wrapper.InstanceModId == sourceModId)
                return obj; // No wrapper required
        }

        if (ReverseInteropLookup.TryGetValue(new(sourceModId, typeof(T)), out ReverseInteropData data))
        {
            IDynamicWrapper reversewrapper = data.ReverseFactory(data.TargetModId, sourceModId, obj, data.ReverseDelegates);
            return reversewrapper;
        }

        throw new InvalidOperationException($"The type '{obj.GetType()}' with interface '{typeof(T).Name}' has not been registered by {sourceModId}. You must call RegisterType() first.");
    }



    private static ArgumentNullException SourceModNullException(string argumentName) => new ArgumentNullException(argumentName, $"{nameof(SourceModId)} not defined. Either set the {nameof(SourceModId)} property, or call the static overload and provide the name explicitly.");










    // Here is the meat

    private static readonly AssemblyBuilder _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("DynamicWrappers"), AssemblyBuilderAccess.Run);
    private static readonly ModuleBuilder _moduleBuilder = _assemblyBuilder.DefineDynamicModule("DynamicWrappers");

#if DEBUG
    private static readonly PersistedAssemblyBuilder _persistedAssemblyBuilder = new PersistedAssemblyBuilder(new AssemblyName("DynamicWrappers"), typeof(object).Assembly);
    private static readonly ModuleBuilder _persistedModuleBuilder = _persistedAssemblyBuilder.DefineDynamicModule("DynamicWrappers");
#endif

    /// <inheritdoc cref="CreateWrapperFactory(ModuleBuilder, Type, Type, IDictionary{string, Delegate}, string, string, bool)"/>
    private static DynamicWrapperFactory CreateWrapperFactory(Type wrapperInterfaceType, Type instanceInterfaceType, IDictionary<string, Delegate> delegates, string targetModId, string originModId, bool isReverse)
    {
        return CreateWrapperFactory(_moduleBuilder, wrapperInterfaceType, instanceInterfaceType, delegates, targetModId, originModId, isReverse);
    }

    /// <summary>
    /// Generates a dynamic type to be used as a wrapper between <paramref name="targetModId"/> and <paramref name="sourceModId"/>.
    /// </summary>
    /// <remarks>
    /// Wrappers come in pairs - the default or main one is the more visible one, that will wrap the sourceMod's objects for targetMod's consumption.
    /// The reverse wrappers wrap targetMod's objects for sourceMod consumption, and will primarily be used internally by the main wrapper (as mentioned).
    /// Both types will also handle any internal wrapping of objects passed in as arguments, and return values on the way back.
    /// This may produce some redundant wrapping attempts, but those should silently pass through the current wrapper as is (or it's underlying object instance).
    /// </remarks>
    /// <param name="moduleBuilder">Which module to use. For runtime types, the module from the standard <see cref="AssemblyBuilder"/> is required.
    /// Only use the module from <see cref="PersistedAssemblyBuilder"/> for debugging, which allows saving to disk for inspection in dnSpy (and has no runtime presence).</param>
    /// <param name="wrapperInterfaceType">The interface that this wrapper will implement.</param>
    /// <param name="instanceInterfaceType">The mirror interface that the underlying object implements.</param>
    /// <param name="delegates">The dictionary of delegates that map the wrapper's members to those of the underlying object.</param>
    /// <param name="targetModId">The modId that this wrapper is being used for (ie. the optional mod), regardless of the direction being wrapped.</param>
    /// <param name="sourceModId">The modId that made the request. Only used to create a unique namespace in the assembly.</param>
    /// <param name="isReverse">Whether this is the reverse wrapper (to wrap a targetMod object in sourceMod wrapper). Only used for naming diffrentiation in the assembly.</param>
    /// <returns>A <see cref="DynamicWrapperFactory"/> delegate that can be invoked to instantiate new instances of the generated dynamic type.</returns>
    private static DynamicWrapperFactory CreateWrapperFactory(ModuleBuilder moduleBuilder, Type wrapperInterfaceType, Type instanceInterfaceType, IDictionary<string, Delegate> delegates, string targetModId, string sourceModId, bool isReverse)
    {
        string typeName = $"{targetModId}.{sourceModId}.{wrapperInterfaceType.Name[1..]}Wrapper{(isReverse ? "_Reverse" : "")}"; // Remove the leading 'I' from the interface name
        TypeBuilder typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class, typeof(object), [wrapperInterfaceType, typeof(IDynamicWrapper)]);

        MethodAttributes backingFieldAttributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        // IDynamicWrapper properties
        FieldBuilder wrapperModIdField = typeBuilder.DefineField(nameof(IDynamicWrapper.WrapperModId).AsBackingFieldCamelCase, typeof(string), FieldAttributes.Private | FieldAttributes.InitOnly);
        FieldBuilder instanceModIdField = typeBuilder.DefineField(nameof(IDynamicWrapper.InstanceModId).AsBackingFieldCamelCase, typeof(string), FieldAttributes.Private | FieldAttributes.InitOnly);
        FieldBuilder instanceField = typeBuilder.DefineField(nameof(IDynamicWrapper.Instance).AsBackingFieldCamelCase, instanceInterfaceType, FieldAttributes.Private | FieldAttributes.InitOnly);

        DefineIDynamicWrapperProperty(typeBuilder, typeof(IDynamicWrapper).GetProperty(nameof(IDynamicWrapper.WrapperModId))!, wrapperModIdField, backingFieldAttributes, null, null);
        DefineIDynamicWrapperProperty(typeBuilder, typeof(IDynamicWrapper).GetProperty(nameof(IDynamicWrapper.InstanceModId))!, instanceModIdField, backingFieldAttributes, null, null);
        DefineIDynamicWrapperProperty(typeBuilder, typeof(IDynamicWrapper).GetProperty(nameof(IDynamicWrapper.Instance))!, instanceField, backingFieldAttributes, typeof(object), instanceInterfaceType);

        static void DefineIDynamicWrapperProperty(TypeBuilder typeBuilder, PropertyInfo propInfo, FieldBuilder backingField, MethodAttributes attr, Type? castGetTo, Type? castSetTo)
        {
            PropertyBuilder propBuilder = typeBuilder.DefineProperty(propInfo.Name, PropertyAttributes.None, propInfo.PropertyType, Type.EmptyTypes);
            if (propInfo.GetMethod != null)
            {
                MethodBuilder propGetter = typeBuilder.DefineMethod(propInfo.GetMethod.Name, attr, CallingConventions.HasThis, propBuilder.PropertyType, Type.EmptyTypes);
                propBuilder.SetGetMethod(propGetter);

                ILGenerator il = propGetter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, backingField);
                if (castGetTo != null)
                    il.Emit(OpCodes.Castclass, castGetTo);
                il.Emit(OpCodes.Ret);
            }
            if (propInfo.SetMethod != null)
            {
                MethodBuilder propSetter = typeBuilder.DefineMethod(propInfo.SetMethod.Name, attr, CallingConventions.HasThis, typeof(void), [propBuilder.PropertyType]);
                propBuilder.SetSetMethod(propSetter);
                propSetter.DefineParameter(1, ParameterAttributes.None, "value");

                ILGenerator il = propSetter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_1);
                if (castSetTo != null)
                    il.Emit(OpCodes.Castclass, castSetTo);
                il.Emit(OpCodes.Stfld, backingField);
                il.Emit(OpCodes.Ret);
            }
        }

        static bool RequireWrapping(Type type, string targetModId, string sourceModId)
        {
            // Cant use the lookups because they may not be filled out yet (ie if this is the first type registered, the lookups will be empty)
            return DeclaredInterfaces.TryGetValue(targetModId, out var targetTypes) && targetTypes.Contains(type) || MirroredInterfaces.TryGetValue(sourceModId, out var sourceTypes) && sourceTypes.Contains(type);
        }

        // Dont actually need to use the delegates here, since we already know the types of the wrapper and the underlying object, could just perform a direct pass through instead.
        // But they are more flexible (if the implementation should change), and using them doesnt hurt.
        // They are required though for writing compile-time wrappers, and since we have them, might as well use them. I also dont want to re-write the code.
        Dictionary<string, FieldInfo> delegateFields = [];

        // WARNING: If you change the signature of either Wrap() or ReverseWrap(), make sure the below queries can still find them (was having trouble finding them via a simple Type.GetMethod() due to the generics)
        // Also make sure to adjust IL accordingly
        MethodInfo wrapOpenGeneric = typeof(DynamicWrapper).GetMethods().Where(method => method.Name == nameof(Wrap) && method.ContainsGenericParameters && method.GetParameters().Length == 3).First()!;
        MethodInfo reverseWrapOpenGeneric = typeof(DynamicWrapper).GetMethods().Where(method => method.Name == nameof(ReverseWrap) && method.ContainsGenericParameters && method.GetParameters().Length == 2).First()!;

        // Get all properties and methods from the wrapperInterfaceType (methods exclude property getter and setters)
        IEnumerable<PropertyInfo> interfaceProps = wrapperInterfaceType.GetProperties();
        IEnumerable<MethodInfo> interfaceMethods = wrapperInterfaceType.GetMethods().Except(interfaceProps.SelectMany(prop => ((IEnumerable<MethodInfo>)[prop.GetMethod!, prop.SetMethod!]).Where(mInfo => mInfo != null)));

        foreach (PropertyInfo propInfo in interfaceProps)
        {
            PropertyBuilder propBuilder = typeBuilder.DefineProperty(propInfo.Name, PropertyAttributes.None, propInfo.PropertyType, Type.EmptyTypes);

            if (propInfo.GetMethod != null)
            {
                MethodBuilder propGetter = typeBuilder.DefineMethod(propInfo.GetMethod.Name, backingFieldAttributes, CallingConventions.HasThis, propInfo.PropertyType, Type.EmptyTypes);
                propBuilder.SetGetMethod(propGetter);

                string propName = propGetter.Name;
                Delegate del = delegates[propName];
                FieldBuilder delField = typeBuilder.DefineField(propName.AsBackingField, del.GetType(), FieldAttributes.Private | FieldAttributes.InitOnly);
                MethodInfo invoke = del.GetType().GetMethod("Invoke")!;
                delegateFields.Add(propName, delField);

                bool wrapReturn = RequireWrapping(propInfo.PropertyType, targetModId, sourceModId);
                MethodInfo? wrap = null;

                ILGenerator il = propGetter.GetILGenerator();
                if (wrapReturn)
                {
                    wrap = wrapOpenGeneric.MakeGenericMethod(propInfo.PropertyType);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, wrapperModIdField);
                }
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, delField);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, instanceField);

                il.Emit(OpCodes.Callvirt, invoke);
                if (wrapReturn)
                {
                    il.Emit(OpCodes.Castclass, typeof(object));
                    il.Emit(OpCodes.Ldnull);
                    il.Emit(OpCodes.Call, wrap!);
                }
                il.Emit(OpCodes.Ret);
            }
            if (propInfo.SetMethod != null)
            {
                MethodBuilder propSetter = typeBuilder.DefineMethod(propInfo.SetMethod.Name, backingFieldAttributes, CallingConventions.HasThis, typeof(void), [propInfo.PropertyType]);
                propBuilder.SetSetMethod(propSetter);
                propSetter.DefineParameter(1, ParameterAttributes.None, "value");

                string propName = propSetter.Name;
                Delegate del = delegates[propName];
                FieldBuilder delField = typeBuilder.DefineField(propName.AsBackingField, del.GetType(), FieldAttributes.Private | FieldAttributes.InitOnly);
                MethodInfo invoke = del.GetType().GetMethod("Invoke")!;
                delegateFields.Add(propName, delField);

                ILGenerator il = propSetter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, delField);
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, instanceField);

                if (RequireWrapping(propInfo.PropertyType, targetModId, sourceModId))
                {
                    MethodInfo reverseWrap = reverseWrapOpenGeneric.MakeGenericMethod(propInfo.PropertyType);
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, instanceModIdField);
                    il.Emit(OpCodes.Ldarg_1);
                    il.Emit(OpCodes.Call, reverseWrap);
                }
                else
                {
                    il.Emit(OpCodes.Ldarg_1);
                }

                il.Emit(OpCodes.Callvirt, invoke);
                il.Emit(OpCodes.Ret);
            }
        }

        foreach (MethodInfo method in interfaceMethods)
        {
            Delegate del = delegates[method.Name];
            FieldBuilder delField = typeBuilder.DefineField(method.Name.AsBackingField, del.GetType(), FieldAttributes.Private | FieldAttributes.InitOnly);
            MethodInfo invoke = del.GetType().GetMethod("Invoke")!;
            delegateFields.Add(method.Name, delField);

            ParameterInfo[] paramInfos = method.GetParameters();
            Type[]? paramTypes = [.. paramInfos.Select(p => p.ParameterType)];
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot, CallingConventions.HasThis, method.ReturnType, paramTypes);

            bool wrapReturn = method.ReturnType != typeof(void) && RequireWrapping(method.ReturnType, targetModId, sourceModId);
            MethodInfo? wrap = null;

            ILGenerator il = methodBuilder.GetILGenerator();
            if (wrapReturn)
            {
                // We need to wrap the return value so the caller can read it. Pre-stack some arguments now, but will perform the method call at the end.
                wrap = wrapOpenGeneric.MakeGenericMethod(method.ReturnType); // public static T Wrap<T>(string targetModId, object objectToWrap, Type? implementingInterface)
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, wrapperModIdField);
            }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, delField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, instanceField);

            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (RequireWrapping(paramInfos[i].ParameterType, targetModId, sourceModId))
                {
                    // Need to wrap the argument so the instance can read it
                    MethodInfo reverseWrap = reverseWrapOpenGeneric.MakeGenericMethod(paramInfos[i].ParameterType); // public static object? ReverseWrap<T>(string sourceModId, T? objectToWrap)
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, instanceModIdField);
                    il.Emit(OpCodes.Ldarg, i + 1);
                    il.Emit(OpCodes.Call, reverseWrap);
                }
                else
                {
                    il.Emit(OpCodes.Ldarg, i + 1);
                }
                ParameterAttributes attr = paramInfos[i].IsOptional ? ParameterAttributes.Optional | ParameterAttributes.HasDefault : ParameterAttributes.None;
                methodBuilder.DefineParameter(i + 1, attr, paramInfos[i].Name);
            }

            il.Emit(OpCodes.Callvirt, invoke); // Invoke the delegate
            if (wrapReturn)
            {
                il.Emit(OpCodes.Castclass, typeof(object));
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Call, wrap!);
            }
            il.Emit(OpCodes.Ret);
        }

        // Constructor (do this last because we need to define all the delegate fields)
        {
            MethodAttributes ctorAttr = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName;
            ParameterInfo[] paramInfos = typeof(DynamicWrapperFactory).GetMethod("Invoke")!.GetParameters();
            ConstructorBuilder ctorBuilder = typeBuilder.DefineConstructor(ctorAttr, CallingConventions.HasThis, [.. paramInfos.Select(p => p.ParameterType)]);

            for (int i = 0; i < paramInfos.Length; i++)
            {
                ctorBuilder.DefineParameter(i + 1, ParameterAttributes.None, paramInfos[i].Name);
            }

            ILGenerator il = ctorBuilder.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes)!);

            // Init IDynamicWrapper fields
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, wrapperModIdField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Stfld, instanceModIdField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Castclass, instanceInterfaceType);
            il.Emit(OpCodes.Stfld, instanceField);

            // Init all delegate fields. Delegates from the dictionary should map 1:1 with the fields
            MethodInfo dictIndexerGetter = typeof(IDictionary<string, Delegate>).IndexerGetter(null);
            foreach (var kvp in delegateFields)
            {
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldarg_S, 4); // load the delegate dict
                il.Emit(OpCodes.Ldstr, kvp.Key);
                il.Emit(OpCodes.Callvirt, dictIndexerGetter); // get the delegate itself
                il.Emit(OpCodes.Stfld, kvp.Value);
            }

            il.Emit(OpCodes.Ret);
        }

        TypeInfo createdType = typeBuilder.CreateTypeInfo(); // Woot!

#if DEBUG
        if (moduleBuilder != _persistedModuleBuilder) // dont stuck in loop!
        {
            // Run this through the persisted builder so we can save and inspect it in dnSpy if needed.
            CreateWrapperFactory(_persistedModuleBuilder, wrapperInterfaceType, instanceInterfaceType, delegates, targetModId, sourceModId, isReverse);
        }
        else
        {
            return null!;
        }
#endif

        return (wrapperModId, instanceModId, instance, delegates) => (IDynamicWrapper)Activator.CreateInstance(createdType, wrapperModId, instanceModId, instance, delegates)!;
    }

    /// <summary>
    /// Saves the generated assembly to disk. Call this to inspect the generated types with dnSpy.
    /// </summary>
    /// <remarks>Requires debug build. In a release build, perform the following to inspect instead: dnSpy -> Debug Menu -> Attach to Process -> SlayTheSpire2.exe -> break/pause -> Debug Menu -> Windows -> Modules -> DynamicWrappers</remarks>
    /// <param name="filename">The file name to save to. Root folder is StS executable folder.</param>
    /// <returns><see langword="false"/> if BaseLib is not running a debug build (and no file is generated).</returns>
    internal static bool SaveAssembly(string filename)
    {
        // I dont think this belongs on the public API, but can call this via reflection from another mod if needed
#if DEBUG
        _persistedAssemblyBuilder.Save(filename);
        return true;
#else
        // TODO: generate the persisted assembly on demand
        return false;
#endif
    }
}

file static class Extensions
{
    extension(string name)
    {
        public string AsBackingField => $"_{name}";
        public string AsBackingFieldCamelCase => $"_{Godot.StringExtensions.ToCamelCase(name)}";
        public string AsPropertyGetter => $"_get_{name}";
        public string AsPropertySetter => $"_set_{name}";
    }
}