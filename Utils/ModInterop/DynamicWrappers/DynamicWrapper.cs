using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace BaseLib.Utils.ModInterop.DynamicWrappers;

/// <summary>
/// Wraps objects for use by another mod using interfaces, and allowing two-way communication between them. The wrapped object must implement an identical/mirror interface to that of the other mod.
/// <br/>Declaring/registering interfaces is not generally required, however you can perform this explicitly by calling <see cref="DeclareAndRegisterInterfaces(string, string, IEnumerable{Type})"/>, or by applying the <see cref="DynamicInteropAttribute"/> to them.
/// </summary>
/// <remarks>
/// Requires <see cref="ModManager"/> to be initialized before declaring interfaces, and requires <see cref="MegaCrit.Sts2.Core.Modding.AssemblyInfo"/> to be initialized in order to register and wrap objects.
/// </remarks>
public class DynamicWrapper
{
    private static Dictionary<string, List<Assembly>> LoadedModAssemblies
    {
        get
        {
            if (field == null)
            {
                // Could wait until AssemblyInfo.Init() instead, consistent with TryGetModIdFromType(), to give time for multi-assembly mods to register all their assemblies.
                if (ModManager.State != ModManagerState.Initialized)
                    throw new InvalidOperationException(ErrorMsg($"{nameof(ModManager)} has not initialized yet (current state is '{ModManager.State}'). Please wait until all mods have been loaded."));

                field = ModManager.GetLoadedMods().Where(mod => mod.manifest?.id != null).ToDictionary(mod => mod.manifest!.id!, mod => mod.assemblies);
            }
            return field;
        }
    }

    private readonly record struct InteropKey(Type TargetInterface, Type SourceType) { public override readonly string ToString() => $"{{Target = \"{ModIdFromTypeOrDefault(TargetInterface)}\", {TargetInterface.Name}}}, {{Source = \"{ModIdFromTypeOrDefault(SourceType)}\", {SourceType.Name}}}"; }
    private readonly record struct AltKey(string TargetModId, Type SourceType) { public override readonly string ToString() => $"Target = \"{TargetModId}\", Source = \"{ModIdFromTypeOrDefault(SourceType)}\", {SourceType.Name}, {SourceType}"; }
    private record class InteropData(string SourceModId, Type SourceInterface, Type TargetInterface, DynamicWrapperFactory Wrap) { public override string ToString() => $"\"{SourceModId}\", {SourceInterface.Name}, {TargetInterface}"; }

    private readonly record struct Declaration(string TargetModId, string SourceModId, Type SourceInterface) { public override readonly string ToString() => $"Target = \"{TargetModId}\", Source = \"{SourceModId}\", {SourceInterface.Name}"; }
    private readonly record struct RegistrationKey(Type TargetInterface, Type SourceInterface) { public override readonly string ToString() => $"\"{SourceInterface.Name}\", Target = {TargetInterface}, Source = {SourceInterface}"; }
    private readonly record struct RegistrationData(Dictionary<string, MethodInfo> TargetMethods, Dictionary<string, MethodInfo> SourceMethods) { public override readonly string ToString() => $"MethodCount = {TargetMethods?.Count.ToString() ?? "null"}"; }
    private readonly record struct RegistrationLog(LogLevel LogLevel, string Log, string Result);

    private static readonly HashSet<Declaration> AttributeDeclarations = [];
    private static readonly HashSet<Declaration> InterfaceDeclarations = [];

#if DEBUG && false // These sorted dicts are awfully slow. Dont risk using them unless specifically debugging, even for the tiny chance that something actually makes heavy use of the system - you'll never know why performance is tanking.
#warning SortedLists are in use - please remember to disable them once finished debugging.
    private static readonly SortedDictionary<Declaration, Type> MirroredInterfaces = new(Comparer<Declaration>.Create((x, y) => (x.TargetModId + x.SourceModId + x.SourceInterface.Name).CompareTo(y.TargetModId + y.SourceModId + y.SourceInterface.Name)));
    private static readonly SortedDictionary<Type, string> ModIdsByRegisteredInterface = new(Comparer<Type>.Create((x, y) => (x.Name + x.FullName).CompareTo(y.Name + y.FullName)));
    private static readonly SortedDictionary<InteropKey, InteropData?> InteropLookup = new(Comparer<InteropKey>.Create((x, y) => (ModIdFromTypeOrDefault(x.TargetInterface, "zzzzz") + x.TargetInterface.Name + x.TargetInterface.FullName + ModIdFromTypeOrDefault(x.SourceType, "zzzzz") + x.SourceType.Name + x.SourceType.FullName).CompareTo(ModIdFromTypeOrDefault(y.TargetInterface, "zzzzz") + y.TargetInterface.Name + y.TargetInterface.FullName + ModIdFromTypeOrDefault(y.SourceType, "zzzzz") + y.SourceType.Name + y.SourceType.FullName)));
    private static readonly SortedDictionary<AltKey, InteropData?> AltLookup = new(Comparer<AltKey>.Create((x, y) => (x.TargetModId + ModIdFromTypeOrDefault(x.SourceType, "zzzzz") + x.SourceType.Name + x.SourceType.FullName).CompareTo(y.TargetModId + ModIdFromTypeOrDefault(y.SourceType, "zzzzz") + y.SourceType.Name + y.SourceType.FullName)));
#else
    private static readonly Dictionary<Declaration, Type> MirroredInterfaces = [];
    private static readonly Dictionary<Type, string> ModIdsByRegisteredInterface = [];
    private static readonly Dictionary<InteropKey, InteropData?> InteropLookup = [];
    private static readonly Dictionary<AltKey, InteropData?> AltLookup = [];
#endif

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
    /// <remarks>Since you are not providing a sourceModId, you can only make calls to Wrap() (anything else will result in an <see cref="InvalidOperationException"/>).</remarks>
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

    /// <inheritdoc cref="DeclareInterface(string, string, Type, string?)"/>
    public void DeclareInterface(Type sourceInterface, string? targetInterfaceFullName = null)
    {
        if (SourceModId == null)
            throw SourceModNullException();

        DeclareInterface(TargetModId, SourceModId, sourceInterface, targetInterfaceFullName);
    }

    /// <inheritdoc cref="DeclareInterfaces(string, string, IEnumerable{Type})"/>
    public void DeclareInterfaces(IEnumerable<Type> sourceInterfaces)
    {
        if (SourceModId == null)
            throw SourceModNullException();

        DeclareInterfaces(TargetModId, SourceModId, sourceInterfaces);
    }

    /// <inheritdoc cref="DeclareAndRegisterInterfaces(string, string, IEnumerable{Type})"/>
    public void DeclareAndRegisterInterfaces(IEnumerable<Type> sourceInterfaces)
    {
        if (SourceModId == null)
            throw SourceModNullException();

        DeclareAndRegisterInterfaces(TargetModId, SourceModId, sourceInterfaces);
    }

    /// <summary>
    /// Registers all previously declared interfaces.
    /// </summary>
    public void RegisterAllInterfaces()
    {
        if (SourceModId == null)
            throw SourceModNullException();

        RegisterAllInterfaces(TargetModId, SourceModId);
    }

    /// <summary>
    /// Marks a set of interfaces as mirrors of ones belonging to <paramref name="targetModId"/> (aka the optional mod), for use by dynamic mod interop.
    /// </summary>
    /// <remarks>
    /// Manually declaring interfaces is usually not required, unless you need to explicitly specify the namespace qualified target name.
    /// </remarks>
    /// <param name="targetModId">The mod that defines these interfaces, that you intend to use for optional mod interop.</param>
    /// <param name="sourceModId">The mod that is defining the mirror interfaces.</param>
    /// <param name="sourceInterface">The mirror interface of that of the same name in <paramref name="targetModId"/>.</param>
    /// <param name="targetInterfaceFullName">The namespace qualified name of <paramref name="sourceInterface"/> in targetMod's assembly. This may have a different name to your own.
    /// If <see langword="null"/>, uses the type name of <paramref name="sourceInterface"/>.
    /// <br/> Use this in the rare case that the target assembly defines multiple interfaces of the same name.</param>
    public static void DeclareInterface(string targetModId, string sourceModId, Type sourceInterface, string? targetInterfaceFullName = null)
    {
        DeclareInterfaceInternal(targetModId, null, sourceModId, sourceInterface, targetInterfaceFullName);
    }

    /// <inheritdoc cref="DeclareInterface(string, string, Type, string?)"/>
    /// <param name="sourceInterfaces">The mirror interfaces of those of the same name belonging to <paramref name="targetModId"/>.</param>
    public static void DeclareInterfaces(string targetModId, string sourceModId, IEnumerable<Type> sourceInterfaces)
    {
        foreach (Type sourceInterface in sourceInterfaces)
        {
            DeclareInterfaceInternal(targetModId, null, sourceModId, sourceInterface);
        }
    }

    /// <summary>
    /// Declares then registers all the supplied interfaces.
    /// </summary>
    /// <remarks>
    /// Manually declaring interfaces is usually not required, as types will be processed automatically when attempting to wrap them.
    /// </remarks>
    /// <inheritdoc cref="DeclareInterfaces(string, string, IEnumerable{Type})"/>
    public static void DeclareAndRegisterInterfaces(string targetModId, string sourceModId, IEnumerable<Type> sourceInterfaces)
    {
        DeclareInterfaces(targetModId, sourceModId, sourceInterfaces);
        RegisterAllInterfaces(targetModId, sourceModId);
    }

    /// <summary>
    /// Registers all previously declared interfaces from <paramref name="sourceModId"/> against <paramref name="targetModId"/>.
    /// </summary>
    /// <param name="targetModId">The mod to register against.</param>
    /// <param name="sourceModId">The mod whose previously decalared interfaces you are now registering.</param>
    public static void RegisterAllInterfaces(string targetModId, string sourceModId)
    {
        foreach (Declaration decl in InterfaceDeclarations.Where(dec => dec.TargetModId == targetModId && dec.SourceModId == sourceModId).ToArray())
        {
            RegisterType(decl.TargetModId, decl.SourceModId, null, decl.SourceInterface);
            InterfaceDeclarations.Remove(decl);
        }
    }

    /// <inheritdoc cref="DeclareInterface(string, string, Type, string?)"/>
    private static void DeclareInterfaceInternal(string targetModId, Type? targetInterface, string sourceModId, Type sourceInterface, string? targetInterfaceFullName = null)
    {
        Declaration declaration = new(targetModId, sourceModId, sourceInterface);

        if (InterfaceDeclarations.Contains(declaration) && MirroredInterfaces.TryGetValue(declaration, out targetInterface))
        {
            BaseLibMain.Logger.Warn($"[DynamicWrappers] Interface '{sourceInterface}' has already been declared with target interface '{targetInterface}'");
        }
        else if (targetInterface != null || TryGetTypeFromModId(targetModId, sourceInterface.Name, targetInterfaceFullName, out targetInterface))
        {
            InterfaceDeclarations.Add(declaration);
            MirroredInterfaces[declaration] = targetInterface;
            MirroredInterfaces[new(sourceModId, targetModId, targetInterface)] = sourceInterface;
            BaseLibMain.Logger.Debug($"[DynamicWrappers] Declared mirror interfaces '{targetInterface}' and '{sourceInterface}' for dynamic interop");
        }
        else
        {
            throw new InvalidOperationException(ErrorMsg($"Interface '{targetInterfaceFullName ?? sourceInterface.Name}' from '{targetModId}' could not be found"));
        }
    }

    internal static void ProcessType(Type sourceInterface)
    {
        if (!sourceInterface.IsInterface)
            return;

        DynamicInteropAttribute? attr = sourceInterface.GetCustomAttribute<DynamicInteropAttribute>();
        if (attr == null)
            return;

        DeclareInterfaceInternal(attr.TargetModId, null, attr.SourceModId, sourceInterface, attr.TargetInterfaceFullName);
        AttributeDeclarations.Add(new(attr.TargetModId, attr.SourceModId, sourceInterface));
    }

    internal static void FinalizeTypeProcessing()
    {
        foreach (Declaration decl in AttributeDeclarations)
        {
            RegisterType(decl.TargetModId, decl.SourceModId, null, decl.SourceInterface);
            InterfaceDeclarations.Remove(decl);
        }

        // Never used again
        AttributeDeclarations.Clear();
        AttributeDeclarations.TrimExcess();
    }





    private static bool TryGetTypeFromModId(string modId, string typeName, string? namespaceQualifiedName, [NotNullWhen(true)] out Type? type)
    {
        if (LoadedModAssemblies.TryGetValue(modId, out var assemblies))
        {
            foreach (Assembly assembly in assemblies)
            {
                type = namespaceQualifiedName != null
                    ? assembly.GetType(namespaceQualifiedName)
                    : assembly.GetExportedTypes().FirstOrDefault(t => t.Name == typeName);
                if (type != null)
                    return true;
            }
        }
        type = null;
        return false;
    }

    static bool TryGetModIdFromType(Type type, [NotNullWhen(true)] out string? modId)
    {
        if (AssemblyInfo.ModMap == null)
            throw new InvalidOperationException(ErrorMsg($"'{nameof(AssemblyInfo)}' has not been initialized yet"));

        modId = AssemblyInfo.ModForType(type, out _)?.manifest?.id;
        return modId != null;
    }

    static string? ModIdFromTypeOrDefault(Type type, string? defaultValue = null)
    {
        return TryGetModIdFromType(type, out string? modId) ? modId : defaultValue;
    }

    /// <summary>
    /// Clears all lookup data.
    /// </summary>
    /// <param name="fullClear">Whether to clear type mappings as well. This may not be recoverable.</param>
    internal static void ClearData(bool fullClear)
    {
        AttributeDeclarations.Clear();
        InterfaceDeclarations.Clear();

        if (fullClear)
            MirroredInterfaces.Clear();

        ModIdsByRegisteredInterface.Clear();
        InteropLookup.Clear();
        AltLookup.Clear();
    }





    /// <inheritdoc cref="RegisterTypeInternal(string, Type?, string, Type?, Type, RegistrationType, OrderedDictionary{RegistrationKey, RegistrationData}?, List{RegistrationLog}?, out InteropData?)"/>
    public bool RegisterType(Type? sourceType, Type sourceInterface)
    {
        if (SourceModId == null)
            throw SourceModNullException();

        return RegisterTypeInternal(TargetModId, null, SourceModId, sourceType, sourceInterface, RegistrationType.Explicit, null, null, out _);
    }

    /// <inheritdoc cref="RegisterTypeInternal(string, Type?, string, Type?, Type, RegistrationType, OrderedDictionary{RegistrationKey, RegistrationData}?, List{RegistrationLog}?, out InteropData?)"/>
    public static bool RegisterType(string targetModId, string sourceModId, Type? sourceType, Type sourceInterface)
    {
        return RegisterTypeInternal(targetModId, null, sourceModId, sourceType, sourceInterface, RegistrationType.Explicit, null, null, out _);
    }

    /// <summary>
    /// Registers a type and/or interface as being a mirror for another mod's interface, and to generate dynamic wrappers that allow each others' interfaces to communicate.
    /// </summary>
    /// <remarks>
    /// Note that dynamic wrappers dont work with debugger step-through, since they have no source code (dissassembly only).
    /// </remarks>
    /// <param name="targetModId">The target mod (usually the optional mod).</param>
    /// <param name="targetInterface">The interface to register against. If <see langword="null"/>, will search declared interfaces and the target mod's assembly.</param>
    /// <param name="sourceModId">Your own mod (usually).</param>
    /// <param name="sourceType">The type that you are registering, that implements <paramref name="sourceInterface"/>. Set to <see langword="null"/> if registering <paramref name="sourceInterface"/> itself.
    /// Will automatically register <paramref name="sourceInterface"/> if not already done.</param>
    /// <param name="sourceInterface">The interface that is a mirror for one in <paramref name="targetModId"/>.
    /// Must have same name and implementation as targetMod's (any other interfaces from <paramref name="targetModId"/> that are used as parameters or return types must be mirrored and registered as well).</param>
    /// <param name="regType">The reason for this registration. Supplying <see cref="RegistrationType.Dependency"/> will perform the validation/dependency check without actually registering anything.</param>
    /// <param name="currentlyRegistering">Stores data related to the types that are currently being registered. Used when recursively registering interface dependencies.</param>
    /// <param name="logs">Stores in-progress logging during recursion, to be logged in order of first started (not first-finished).</param>
    /// <param name="data">The generated data. Will be <see langword="null"/> if registration failed.
    /// Will also be <see langword="null"/> if this was a successful dependency check that is awaiting registration (when using <see cref="RegistrationType.Dependency"/>).</param>
    /// <returns><see langword="true"/> if registration was successful, or this was a dependency that passed it's validation check. Otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="sourceInterface"/> is not an interface, or it does not implement all members of the target interface.</exception>
    /// <exception cref="InvalidOperationException">The interface '<paramref name="sourceInterface"/>' has not been registered.</exception>
    private static bool RegisterTypeInternal(string targetModId, Type? targetInterface, string sourceModId, Type? sourceType, Type sourceInterface, RegistrationType regType,
        OrderedDictionary<RegistrationKey, RegistrationData>? currentlyRegistering, List<RegistrationLog>? logs, out InteropData? data)
    {
        if (!sourceInterface.IsInterface)
        {
            PrintLogs(logs);
            throw new ArgumentException(ErrorMsg($"The provided argument '{nameof(sourceInterface)}' of type '{sourceInterface.GetType()}' is not an interface. DynamicWrappers require an interface."), nameof(sourceInterface));
        }

        // The logs are a bit hard to read, but this at least keeps them in order during recursion (while allowing to append 'success' or 'fail' to each log entry)
        logs ??= [];
        int logIndex = logs.Count;
        logs.Add(default); // reserve the index
        string log = $"[DynamicWrappers] Registering: targetModId='{targetModId}', sourceModId='{sourceModId}', sourceType='{sourceType?.Name ?? ""}', sourceInterface='{sourceInterface.Name}', regType='{regType}', result: ";

        static void LogResult(List<RegistrationLog> logs, int index, LogLevel logLevel, string log, string result, bool forcePrint = false)
        {
            logs[index] = new(logLevel, log, result);

            if (index == 0 || forcePrint)
            {
                PrintLogs(logs);
            }
        }

        static void PrintLogs(List<RegistrationLog>? logs)
        {
            if (logs == null)
                return;

            foreach (var (_logLevel, _log, _result) in logs)
            {
                BaseLibMain.Logger.LogMessage(_logLevel, $"{_log}{_result}", 1);
            }
        }

        if (targetInterface != null || MirroredInterfaces.TryGetValue(new(targetModId, sourceModId, sourceInterface), out targetInterface) || TryGetTypeFromModId(targetModId, sourceInterface.Name, null, out targetInterface))
        {
            // WARNING: Do not add concrete types to AltLookup here (this will be done separately); types may implement multiple interfaces, but AltLookup only supports one per target mod
            if (InteropLookup.TryGetValue(new(targetInterface, sourceInterface), out data))
            {
                if (data == null)
                {
                    LogResult(logs, logIndex, LogLevel.VeryDebug, log, "Failed (internal logic error)", forcePrint: true);
                    throw new InvalidOperationException(ErrorMsg($"Internal logic error: Trying to register '{sourceInterface}' with '{targetModId}', but it has already been marked as not wrappable."));
                }

                InteropKey interopKey;
                if (sourceType != null && sourceType != sourceInterface && !InteropLookup.ContainsKey(interopKey = new(targetInterface, sourceType)))
                {
                    // Already generated for this sourceInterface, we can reuse the data for this concrete type
                    // Nothing to add to reverse lookups; they only require the interface, not concrete types, and it too will have already been generated
                    RegisterClassWithInterface(targetModId, targetInterface, sourceModId, sourceType, sourceInterface, data);
                    LogResult(logs, logIndex, LogLevel.VeryDebug, log, "Success");
                }
                else
                {
                    LogResult(logs, logIndex, LogLevel.VeryDebug, log, "Already Registered");
                }
                return true;
            }
            else if (sourceType != null && sourceType != sourceInterface)
            {
                // Register the interface first, then the concrete type can copy that data
                if (!RegisterTypeInternal(targetModId, targetInterface, sourceModId, null, sourceInterface, regType, null, logs, out data) || data == null)
                {
                    LogResult(logs, logIndex, LogLevel.VeryDebug, log, "Failed");
                    return false; // Should only hit this code during auto-registration or a pure validation check, so is safe to return false again here
                }
                RegisterClassWithInterface(targetModId, targetInterface, sourceModId, sourceType, sourceInterface, data);
                LogResult(logs, logIndex, LogLevel.VeryDebug, log, "Success");
                return true;
            }

            Dictionary<string, MethodInfo> targetMethods = [];
            Dictionary<string, MethodInfo> sourceMethods = [];

            currentlyRegistering ??= [];
            currentlyRegistering.Add(new(targetInterface, sourceInterface), new(targetMethods, sourceMethods));

            // Generate the delegates that will map wrappers to their object instance
            foreach (MethodInfo targetMethod in targetInterface.GetMethods())
            {
                Type[] targetParamTypes = [.. targetMethod.GetParameters().Select(pInfo => pInfo.ParameterType)];
                Type[] sourceParamTypes;
                MethodInfo? sourceMethod = sourceInterface.GetMethods().FirstOrDefault(method =>
                {
                    // Check for matching return and param types (or that are mirrored interfaces, which will get wrapped accordingly in the emitted IL)
                    sourceParamTypes = [.. method.GetParameters().Select(pInfo => pInfo.ParameterType)];
                    if (method.Name == targetMethod.Name && sourceParamTypes.Length == targetParamTypes.Length && MatchTypes(targetModId, targetMethod.ReturnType, sourceModId, method.ReturnType, currentlyRegistering, logs))
                    {
                        for (int i = 0; i < sourceParamTypes.Length; i++)
                        {
                            if (!MatchTypes(targetModId, targetParamTypes[i], sourceModId, sourceParamTypes[i], currentlyRegistering, logs))
                            {
                                return false;
                            }
                        }
                        return true;
                    }
                    return false;

                    static bool MatchTypes(string targetModId, Type targetType, string sourceModId, Type sourceType, OrderedDictionary<RegistrationKey, RegistrationData> currentlyRegistering, List<RegistrationLog> logs)
                    {
                        if (sourceType == targetType || sourceType.IsEnum && targetType.IsEnum && sourceType.GetEnumUnderlyingType() == targetType.GetEnumUnderlyingType())
                        {
                            return true;
                        }
                        else if (sourceType.IsInterface && targetType.IsInterface
                                && (sourceType.Name == targetType.Name || MirroredInterfaces.TryGetValue(new(targetModId, sourceModId, sourceType), out Type? targetInterface) && targetInterface == targetType))
                        {
                            if (currentlyRegistering.ContainsKey(new(targetType, sourceType)))
                                return true;
                            else if (ModIdsByRegisteredInterface.TryGetValue(targetType, out string? targetId) && targetId == targetModId // Not guaranteed that these are mirrors of each other, but very probable
                                    && ModIdsByRegisteredInterface.TryGetValue(sourceType, out string? sourceId) && sourceId == sourceModId)
                                return true;
                            else if (RegisterTypeInternal(targetModId, targetType, sourceModId, null, sourceType, RegistrationType.Dependency, currentlyRegistering, logs, out _))
                                return true;
                        }

                        return false;
                    }
                });

                if (sourceMethod != null)
                {
                    sourceMethods.Add(sourceMethod.Name, sourceMethod);
                    targetMethods.Add(targetMethod.Name, targetMethod);
                }
                else
                {
                    string msg = $"missing/incorrect signature of '{targetInterface}.{targetMethod.Name}'";
                    LogResult(logs, logIndex, LogLevel.VeryDebug, log, $"Failed Validation: {msg}");

                    if (regType == RegistrationType.Explicit)
                    {
                        PrintLogs(logs);
                        throw new ArgumentException(ErrorMsg($"The type '{sourceType ?? sourceInterface}' does not implement the required interface members ({msg})."));
                    }
                    else if (regType == RegistrationType.Dependency)
                    {
                        currentlyRegistering.RemoveAt(currentlyRegistering.Count - 1);
                    }

                    return false;
                }
            }

            if (regType == RegistrationType.Dependency)
            {
                // Wait for all dependencies to resolve before registering anything. Data is stored in 'currentlyRegistering'.
                LogResult(logs, logIndex, LogLevel.VeryDebug, log, "Success");
                return true;
            }

            foreach (RegistrationKey key in currentlyRegistering.Keys)
            {
                // Need these populated before calling CreateWrapperFactory()
                ModIdsByRegisteredInterface[key.TargetInterface] = targetModId;
                ModIdsByRegisteredInterface[key.SourceInterface] = sourceModId;
            }

            for (int i = 0; i < currentlyRegistering.Count; i++)
            {
                var reg = currentlyRegistering.GetAt(i);
                (targetInterface, sourceInterface) = reg.Key;
                (targetMethods, sourceMethods) = reg.Value;

                // While its possible to define compile-time wrappers using dynamic objects, you can only do this for your own side, and I think it only makes sense if both parties do this.
                // Adds a bunch of complexity, and doesnt gain a whole lot (functionally identically, and you'll suffer the DLR kicking in on first use), so not giving the option.
                DynamicWrapperFactory wrapperFactory = CreateWrapperFactory(targetInterface, sourceInterface, sourceMethods, targetModId, sourceModId);
                DynamicWrapperFactory reverseFactory = CreateWrapperFactory(sourceInterface, targetInterface, targetMethods, sourceModId, targetModId);

                InteropData interopData = new(sourceModId, sourceInterface, targetInterface, wrapperFactory);
                InteropData reverseData = new(targetModId, targetInterface, sourceInterface, reverseFactory);

                InteropLookup[new(targetInterface, sourceInterface)] = interopData;
                InteropLookup[new(sourceInterface, targetInterface)] = reverseData;

                AltLookup[new(targetModId, sourceInterface)] = interopData;
                AltLookup[new(sourceModId, targetInterface)] = reverseData;

                logs.Add(new(LogLevel.Info, $"[DynamicWrappers] Built DynamicWrappers for interop between '{sourceModId}' and '{targetModId}' with interface '{targetInterface.Name}'", ""));

                TypeRegistered?.Invoke(new TypeRegisteredEventArgs(targetModId, targetInterface, sourceModId, sourceType, sourceInterface));

                if (regType != RegistrationType.Auto) // Dont recurse on auto rego
                {
                    // Register with all other mods that have already registered with the same targetInterface. These mods may come into contact with each other via targetMod.
                    // This does not register concrete types.
                    // Note: If a lot of mods are registering against the same target interface, this can lead to a lot of combinations, though I dont imagine it will be an issue.

                    // Find all targets of our target
                    var otherModKvps = AltLookup.Where(kvp => kvp.Key.SourceType == targetInterface && kvp.Key.TargetModId != sourceModId).ToArray();

                    if (otherModKvps.Length > 0)
                    {
                        logs.Add(new(LogLevel.Debug, $"[DynamicWrappers] Auto-registering interop between '{sourceModId}' and {otherModKvps.Length} other mod(s) registered with '{targetInterface}'", ""));

                        foreach (var altKvp in otherModKvps)
                        {
                            if (altKvp.Value == null)
                                continue;

                            string otherModId = altKvp.Key.TargetModId;

                            if (i == 0) // Ensure any explicit type mappings translate to the other mods. Do this all upfront because they may be needed for the first validation check.
                            {
                                foreach (RegistrationKey key in currentlyRegistering.Keys)
                                {
                                    if (AltLookup.TryGetValue(new(otherModId, key.TargetInterface), out InteropData? otherData) && otherData != null)
                                    {
                                        MirroredInterfaces[new(otherModId, sourceModId, key.SourceInterface)] = otherData.TargetInterface;
                                    }
                                }
                            }
                            RegisterTypeInternal(otherModId, altKvp.Value.TargetInterface, sourceModId, null, sourceInterface, RegistrationType.Auto, null, logs, out _);
                        }
                    }
                }

                if (i == 0)
                    data = interopData;
            }

            if (data == null)
            {
                LogResult(logs, logIndex, LogLevel.VeryDebug, log, $"Failed (internal logic error)", forcePrint: true);
                throw new InvalidOperationException(ErrorMsg($"Internal logic error: '{nameof(data)}' should not be null"));
            }

            LogResult(logs, logIndex, LogLevel.VeryDebug, log, "Success");
            return true;
        }
        else if (regType != RegistrationType.Explicit)
        {
            LogResult(logs, logIndex, LogLevel.VeryDebug, log, "Failed");
            data = null;
            return false;
        }
        else
        {
            LogResult(logs, logIndex, LogLevel.VeryDebug, log, "Failed", forcePrint: true);
            throw new InvalidOperationException(ErrorMsg($"The interface '{sourceInterface.Name}' could not be found in '{targetModId}'."));
        }
    }

    private static void RegisterClassWithInterface(string targetModId, Type targetInterface, string sourceModId, Type sourceType, Type sourceInterface, InteropData interopData)
    {
        InteropLookup[new(targetInterface, sourceType)] = interopData;
        TypeRegistered?.Invoke(new TypeRegisteredEventArgs(targetModId, targetInterface, sourceModId, sourceType, sourceInterface));
    }

    private enum RegistrationType
    {
        Explicit,
        OnDemand,
        Auto,
        Dependency,
        Sync
    }





    /// <inheritdoc cref="Wrap{T}(string, object?, Type?)"/>
    [return: NotNullIfNotNull(nameof(objectToWrap))]
    public object? Wrap(object? objectToWrap, Type? sourceInterface = null)
    {
        return WrapInternal(TargetModId, null, objectToWrap, sourceInterface);
    }

    /// <inheritdoc cref="Wrap{T}(string, object?, Type?)"/>
    [return: NotNullIfNotNull(nameof(objectToWrap))]
    public T? Wrap<T>(object? objectToWrap, Type? sourceInterface = null) where T : class
    {
        return (T?)WrapInternal(TargetModId, typeof(T), objectToWrap, sourceInterface);
    }

    /// <inheritdoc cref="Wrap{T}(string, object?, Type?)"/>
    [return: NotNullIfNotNull(nameof(objectToWrap))]
    public static object? Wrap(string targetModId, object? objectToWrap, Type? sourceInterface = null)
    {
        // WARNING: If you change this method signature, need to update the MethodInfo fields (look for the matching WARNING comment) and associated IL in CreateWrapperFactory
        return WrapInternal(targetModId, null, objectToWrap, sourceInterface);
    }

    /// <summary>
    /// Wraps <paramref name="objectToWrap"/> for <paramref name="targetModId"/> consumption.
    /// </summary>
    /// <typeparam name="T">The target interface to wrap <paramref name="objectToWrap"/> with. This interface must be native to <paramref name="targetModId"/>.</typeparam>
    /// <param name="targetModId">The modId that is the intended recipient of <paramref name="objectToWrap"/>.</param>
    /// <param name="objectToWrap">The object to wrap.</param>
    /// <param name="sourceInterface">The source interface to wrap by. Only required when the target interface is unknown and <paramref name="objectToWrap"/> implements multiple wrappable interfaces.</param>
    /// <returns>Either an <see cref="IDynamicWrapper"/> implenting an interface that <paramref name="targetModId"/> can consume, or the object instance itself if it is native to <paramref name="targetModId"/>.
    /// Returns <see langword="null"/> if <paramref name="objectToWrap"/> is <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">The interface <typeparamref name="T"/> has not been registered by <paramref name="targetModId"/>.</exception>
    [return: NotNullIfNotNull(nameof(objectToWrap))]
    public static T? Wrap<T>(string? targetModId, object? objectToWrap, Type? sourceInterface = null)
    {
        // WARNING: If you change this method signature, need to update the MethodInfo fields (look for the matching WARNING comment) and associated IL in CreateWrapperFactory
        return (T?)WrapInternal(targetModId, typeof(T), objectToWrap, sourceInterface);
    }

    /// <inheritdoc cref="Wrap{T}(string, object?, Type?)"/>
    /// <param name="targetInterface">The target interface to wrap <paramref name="objectToWrap"/> with. This interface must be native to <paramref name="targetModId"/>. If <see langword="null"/>, <paramref name="targetModId"/> must be supplied.</param>
    /// <param name="returnNullIfNotWrapped">If <see langword="true"/>, returns <see langword="null"/> when the object is not able to be wrapped. Otherwise, throws an <see cref="InvalidOperationException"/>.</param>
    [return: NotNullIfNotNull(nameof(objectToWrap))]
    private static object? WrapInternal(string? targetModId, Type? targetInterface, object? objectToWrap, Type? sourceInterface, bool returnNullIfNotWrapped = false)
    {
        if (objectToWrap == null)
            return null;

        Type sourceType = objectToWrap.GetType();

        if (targetInterface != null && sourceType.IsAssignableTo(targetInterface))
        {
            return objectToWrap; // No wrapper required
        }
        else if (objectToWrap is IDynamicWrapper wrapper)
        {
            if (wrapper.WrapperModId == targetModId)
            {
                return wrapper;
            }
            else if (wrapper.InstanceModId == targetModId)
            {
                return wrapper.Instance;
            }

            objectToWrap = wrapper.Instance; // Strip the current wrapper
            sourceType = objectToWrap.GetType();

            if (targetInterface != null && sourceType.IsAssignableTo(targetInterface))
            {
                return objectToWrap;
            }

            sourceInterface = wrapper.InstanceInterface;
        }

        if (TryFindWrapper(targetModId, targetInterface, sourceType, sourceInterface, returnNullIfNotWrapped, out InteropData? data))
        {
            if (targetInterface == null && sourceType.IsAssignableTo(data.TargetInterface)) // If (targetInterface != null), then this has already been checked
            {
                return objectToWrap;
            }
            return data.Wrap(objectToWrap); // Apply wrapper
        }

        if (returnNullIfNotWrapped)
            return null!; // internal use only (TryWrap)

        if (targetModId == null)
            throw new InvalidOperationException(ErrorMsg($"The type '{objectToWrap.GetType()}' has not been registered with interface {targetInterface?.FullName ?? "<NotProvided>"}."));

        throw new InvalidOperationException(ErrorMsg($"Unable to wrap '{objectToWrap.GetType()}' for target mod '{targetModId}'{(targetInterface == null ? "" : $" with target interface '{targetInterface}'")}."));



        static bool TryFindWrapper(string? targetModId, Type? targetInterface, Type sourceType, Type? sourceInterface, bool returnNullIfNotWrapped, [NotNullWhen(true)] out InteropData? data)
        {
            // A lookup value of 'null' indicates not wrappable for the provided target. A missing entry altogether means the type requires processing, which should guarantee an entry into it's respective lookup for next time.

            if (targetInterface != null)
            {
                if (InteropLookup.TryGetValue(new(targetInterface, sourceInterface ?? sourceType), out data))
                    return data != null;
                else if (targetModId != null || TryGetModIdFromType(targetInterface, out targetModId))
                    return TryRegisterType(targetModId, targetInterface, sourceType, sourceInterface, returnNullIfNotWrapped, out data);

                if (returnNullIfNotWrapped)
                {
                    if (sourceInterface != null)
                    {
                        InteropLookup[new(targetInterface, sourceInterface)] = null;
                    }
                    InteropLookup[new(targetInterface, sourceType)] = null;
                    return false;
                }

                throw new InvalidOperationException(ErrorMsg($"Could not find the mod that the target interface '{targetInterface}' belongs to."));
            }
            else if (targetModId != null)
            {
                if (AltLookup.TryGetValue(new(targetModId, sourceInterface ?? sourceType), out data))
                    return data != null;
                else
                    return TryRegisterType(targetModId, null, sourceType, sourceInterface, returnNullIfNotWrapped, out data);
            }
            else if (returnNullIfNotWrapped)
            {
                data = null;
                return false;
            }

            throw new InvalidOperationException(ErrorMsg($"No {nameof(targetModId)} or {nameof(targetInterface)} supplied. At least one of these is required in order to wrap an object."));



            static bool TryRegisterType(string targetModId, Type? targetInterface, Type sourceType, Type? sourceInterface, bool returnNullIfNotWrapped, [NotNullWhen(true)] out InteropData? data)
            {
                data = null;
                InteropData? interopData = null;
                int numWrappableInterfaces = 0;

                foreach (Type srcInterface in sourceType.GetInterfaces())
                {
                    AltKey srcKey = new(targetModId, srcInterface);
                    if (!AltLookup.TryGetValue(srcKey, out InteropData? tempData))
                    {
                        if (!TryGetModIdFromType(srcInterface, out string? srcModId)
                            || !RegisterTypeInternal(targetModId, null, srcModId, sourceType, srcInterface, RegistrationType.OnDemand, null, null, out tempData)) // Do not supply targetInterface here
                        {
                            // srcInterface is not wrappable for the target mod
                            if (targetInterface != null)
                            {
                                InteropLookup[new(targetInterface, srcInterface)] = null;
                            }
                            AltLookup[new(targetModId, srcInterface)] = null;
                        }
                    }
                    else if (tempData != null)
                    {
                        RegisterClassWithInterface(targetModId, tempData.TargetInterface, tempData.SourceModId, sourceType, srcInterface, tempData);
                    }

                    if (tempData != null)
                    {
                        numWrappableInterfaces++;
                        interopData = tempData;
                        if (targetInterface == null && sourceInterface == null || interopData.TargetInterface == targetInterface || srcInterface == sourceInterface)
                        {
                            data = tempData;
                        }
                    }
                }

                if (numWrappableInterfaces == 0)
                {
                    AltLookup[new(targetModId, sourceType)] = null;
                }
                else if (numWrappableInterfaces == 1)
                {
                    if (interopData == null)
                        throw new InvalidOperationException(ErrorMsg($"Internal logic error: '{nameof(interopData)}' is null, even though a wrappable interface was found."));

                    AltLookup[new(targetModId, sourceType)] = interopData; // Registration does not automatically add concrete types to this lookup. Can only do this when the type implements only a single wrappable interface.
                }
                else if (targetInterface == null && sourceInterface == null)
                {
                    if (!returnNullIfNotWrapped)
                        throw new AmbiguousMatchException(ErrorMsg($"'{sourceType}' implements multiple interfaces for mod '{targetModId}'. Please specify an interface explicitly."));

                    AltLookup[new(targetModId, sourceType)] = null; // Can still be wrapped by specifying one of it's interfaces
                    data = null;
                }

                if (targetInterface != null && data == null)
                {
                    InteropLookup[new(targetInterface, sourceType)] = null;
                }

                return data != null;
            }
        }
    }

    /// <inheritdoc cref="TryWrap(string, Type?, object?, Type?, out object?)"/>
    public bool TryWrap(object? objectToWrap, Type? sourceInterface, [NotNullWhen(true)] out object? value)
    {
        return TryWrap(TargetModId, objectToWrap, sourceInterface, out value);
    }

    /// <inheritdoc cref="TryWrap(string, Type?, object?, Type?, out object?)"/>
    public bool TryWrap(Type? targetInterface, object? objectToWrap, Type? sourceInterface, [NotNullWhen(true)] out object? value)
    {
        return TryWrap(TargetModId, targetInterface, objectToWrap, sourceInterface, out value);
    }

    /// <inheritdoc cref="TryWrap{T}(string, object?, out T)"/>
    public bool TryWrap<T>(object? objectToWrap, [NotNullWhen(true)] out T? value)
    {
        return TryWrap(TargetModId, objectToWrap, out value);
    }

    /// <inheritdoc cref="TryWrap(string, Type?, object?, Type?, out object?)"/>
    public static bool TryWrap(string targetModId, object? objectToWrap, Type? sourceInterface, [NotNullWhen(true)] out object? wrapper)
    {
        return TryWrap(targetModId, null, objectToWrap, sourceInterface, out wrapper);
    }

    /// <summary>
    /// Attempts to wrap an object for <paramref name="targetModId"/>.
    /// </summary>
    /// <param name="targetModId">The modId that is the intended recipient of <paramref name="objectToWrap"/>.</param>
    /// <param name="targetInterface">The interface to wrap <paramref name="objectToWrap"/> with. Supply <see langword="null"/> if not known.
    /// <para/>Note that if both <paramref name="targetInterface"/> and <paramref name="sourceInterface"/> are <see langword="null"/>, 
    /// the correct wrapper needs to be inferred and may produce errors, depending on <paramref name="objectToWrap"/>'s implemented interfaces.</param>
    /// <param name="objectToWrap">The object to wrap.</param>
    /// <param name="sourceInterface">The source interface to wrap by. Only required when the target interface is unknown or <paramref name="objectToWrap"/> implements multiple wrappable interfaces.</param>
    /// <param name="wrapper">If the method returned <see langword="true"/>, then this is either an <see cref="IDynamicWrapper"/> implenting an interface that <paramref name="targetModId"/> can consume, or the object instance itself if it is native to <paramref name="targetModId"/>.</param>
    /// <returns><see langword="true"/> if the object was successfully wrapped (or wrapping wasn't required), otherwise <see langword="false"/>. Returns <see langword="false"/> if <paramref name="objectToWrap"/> is <see langword="null"/>.</returns>
    public static bool TryWrap(string targetModId, Type? targetInterface, object? objectToWrap, Type? sourceInterface, [NotNullWhen(true)] out object? wrapper)
    {
        wrapper = WrapInternal(targetModId, targetInterface, objectToWrap, sourceInterface, returnNullIfNotWrapped: true);
        return wrapper != null;
    }

    /// <typeparam name="T">The interface to wrap <paramref name="objectToWrap"/> with. This interface must be native to <paramref name="targetModId"/>.</typeparam>
    /// <param name="targetModId">The modId that is the intended recipient of <paramref name="objectToWrap"/>. If not known, supply <see langword="null"/>, and it will seek the target mod using type <typeparamref name="T"/>.</param>
    /// <inheritdoc cref="TryWrap(string, object?, Type?, out object?)"/>
    public static bool TryWrap<T>(string? targetModId, object? objectToWrap, [NotNullWhen(true)] out T? wrapper)
    {
        wrapper = (T?)WrapInternal(targetModId, typeof(T), objectToWrap, null, returnNullIfNotWrapped: true);
        return wrapper != null;
    }










    private static InvalidOperationException SourceModNullException([CallerLineNumber] int line = 0) => new InvalidOperationException(ErrorMsg($"{nameof(SourceModId)} not defined. Either set the {nameof(SourceModId)} property, or call the static overload and provide the name explicitly.", line));

    /// <summary>
    /// A factory method to create an <see cref="IDynamicWrapper"/> for wrapping a supplied instance object.
    /// </summary>
    /// <param name="instance">The underlying instance object.</param>
    /// <returns>A dynamically generated wrapper class for the supplied <paramref name="instance"/>.</returns>
    private delegate IDynamicWrapper DynamicWrapperFactory(object instance);










    internal static void RegisterDynamicAssembly()
    {
        // Unsure about this...if any caller wants to know, I think "BaseLib" is the wrong answer...
        ModManager.AssociateAssemblyWithMod(BaseLibMain.ModId, _assemblyBuilder);
    }

    /// <summary>
    /// If <see cref="DynamicWrapper"/> is defined in a another mod (and that mod is loaded first), call this to sync both implementations.
    /// </summary>
    /// <remarks>Prevents lookup tables being split across different mods, which may break interopability in some cases.</remarks>
    /// <param name="otherModId">The other mod that defines <see cref="DynamicWrapper"/>.</param>
    internal static void SyncWithAnotherImplementation(string otherModId)
    {
        // Untested
        if (TryGetTypeFromModId(otherModId, nameof(DynamicWrapper), null, out Type? other))
        {
            other.GetEvent(nameof(TypeRegistered), BindingFlags.Static)!.AddEventHandler(null, (TypeRegisteredEventHandler)Sync);
            TypeRegistered += other.GetMethod(nameof(Sync), BindingFlags.Static | BindingFlags.NonPublic, [typeof(TypeRegisteredEventArgs)])!.CreateDelegate<TypeRegisteredEventHandler>();
        }
        else
        {
            BaseLibMain.Logger.Warn($"[DynamicWrappers] Could not sync with mod {otherModId}");
        }
    }

    private static void Sync(TypeRegisteredEventArgs e)
    {
        DeclareInterfaceInternal(e.TargetModId, e.TargetInterface, e.SourceModId, e.SourceInterface, e.TargetInterface.FullName);
        RegisterTypeInternal(e.TargetModId, e.TargetInterface, e.SourceModId, e.SourceType, e.SourceInterface, RegistrationType.Sync, null, null, out _);
    }








    // Here is the meat

    const string AssemblyName = "DynamicWrappers";

    private static readonly AssemblyBuilder _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(AssemblyName), AssemblyBuilderAccess.Run);
    private static readonly ModuleBuilder _moduleBuilder = _assemblyBuilder.DefineDynamicModule(AssemblyName);

#if DEBUG
    private static readonly PersistedAssemblyBuilder _persistedAssemblyBuilder = new PersistedAssemblyBuilder(new AssemblyName(AssemblyName), typeof(object).Assembly);
    private static readonly ModuleBuilder _persistedModuleBuilder = _persistedAssemblyBuilder.DefineDynamicModule(AssemblyName);
#endif

    // WARNING: If you change the signature of either Wrap<T>(string, object?, Type?) or Wrap(string, object?, Type?), make sure the below two fields can still find them
    // Also make sure to adjust the IL in CreateWrapperFactory() accordingly
    private static readonly MethodInfo wrapNonGeneric = ((Func<string, object?, Type?, object?>)Wrap).Method;
    private static readonly MethodInfo wrapOpenGeneric = ((Func<string, object?, Type?, object?>)Wrap<object>).Method.GetGenericMethodDefinition();
    private static readonly MethodInfo getTypeFromHandle = ((Func<RuntimeTypeHandle, Type?>)Type.GetTypeFromHandle).Method;


    /// <inheritdoc cref="CreateWrapperFactory(ModuleBuilder, Type, Type, Dictionary{string, MethodInfo}, string, string)"/>
    private static DynamicWrapperFactory CreateWrapperFactory(Type wrapperInterface, Type instanceInterface, Dictionary<string, MethodInfo> instanceMethods, string targetModId, string originModId)
    {
        return CreateWrapperFactory(_moduleBuilder, wrapperInterface, instanceInterface, instanceMethods, targetModId, originModId);
    }

    /// <summary>
    /// Generates a dynamic type to be used as a wrapper between <paramref name="wrapperModId"/> and <paramref name="instanceModId"/>.
    /// </summary>
    /// <remarks>
    /// Wrappers come in pairs - the default or main one is the more visible one, that will wrap the sourceMod's objects for targetMod's consumption.
    /// The reverse wrappers wrap targetMod's objects for sourceMod consumption, and will primarily be used internally by the main wrapper (as mentioned).
    /// Both types will also handle any internal wrapping of objects passed in as arguments, and return values on the way back.
    /// This may produce some redundant wrapping attempts, but those should silently pass through the current wrapper as is (or it's underlying object instance).
    /// </remarks>
    /// <param name="moduleBuilder">Which module to use. For runtime types, the module from the standard <see cref="AssemblyBuilder"/> is required.
    /// Only use the module from <see cref="PersistedAssemblyBuilder"/> for debugging, which allows saving to disk for inspection in dnSpy (and has no runtime presence).</param>
    /// <param name="wrapperInterface">The interface that this wrapper will implement.</param>
    /// <param name="instanceInterface">The mirror interface that the underlying object implements.</param>
    /// <param name="instanceMethods">The methods defined by the underlying object.</param>
    /// <param name="wrapperModId">The modId that this wrapper is being used for (ie. the optional mod), regardless of the direction being wrapped.</param>
    /// <param name="instanceModId">The modId that the instance object belongs to. Only used to create a unique namespace in the assembly.</param>
    /// <returns>A <see cref="DynamicWrapperFactory"/> delegate that can be invoked to instantiate new instances of the generated dynamic type.</returns>
    private static DynamicWrapperFactory CreateWrapperFactory(ModuleBuilder moduleBuilder, Type wrapperInterface, Type instanceInterface, Dictionary<string, MethodInfo> instanceMethods, string wrapperModId, string instanceModId)
    {
        string typeName = $"{wrapperModId}.{instanceModId}.{wrapperInterface.Name[1..]}Wrapper"; // Remove the leading 'I' from the interface name. Will be awkward if it doesnt start with I
        TypeBuilder typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class, typeof(object), [wrapperInterface, typeof(IDynamicWrapper)]);

        MethodAttributes getterSetterAttributes = MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.SpecialName | MethodAttributes.HideBySig;

        // IDynamicWrapper properties
        FieldBuilder wrapperModIdField = typeBuilder.DefineField(nameof(IDynamicWrapper.WrapperModId).AsBackingFieldCamelCase, typeof(string), FieldAttributes.Private | FieldAttributes.InitOnly);
        FieldBuilder instanceModIdField = typeBuilder.DefineField(nameof(IDynamicWrapper.InstanceModId).AsBackingFieldCamelCase, typeof(string), FieldAttributes.Private | FieldAttributes.InitOnly);
        FieldBuilder instanceField = typeBuilder.DefineField(nameof(IDynamicWrapper.Instance).AsBackingFieldCamelCase, instanceInterface, FieldAttributes.Private | FieldAttributes.InitOnly);

        DefineIDynamicWrapperProperty(typeBuilder, typeof(IDynamicWrapper).GetProperty(nameof(IDynamicWrapper.WrapperModId))!, wrapperModIdField, getterSetterAttributes, null, null);
        DefineIDynamicWrapperProperty(typeBuilder, typeof(IDynamicWrapper).GetProperty(nameof(IDynamicWrapper.InstanceModId))!, instanceModIdField, getterSetterAttributes, null, null);
        DefineIDynamicWrapperProperty(typeBuilder, typeof(IDynamicWrapper).GetProperty(nameof(IDynamicWrapper.Instance))!, instanceField, getterSetterAttributes, castGetTo: typeof(object), castSetTo: instanceInterface);
        DefineIDynamicWrapperProperty_InterfaceType(typeBuilder, typeof(IDynamicWrapper).GetProperty(nameof(IDynamicWrapper.WrapperInterface))!, wrapperInterface, getterSetterAttributes); // readonly, no backing field
        DefineIDynamicWrapperProperty_InterfaceType(typeBuilder, typeof(IDynamicWrapper).GetProperty(nameof(IDynamicWrapper.InstanceInterface))!, instanceInterface, getterSetterAttributes);

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

        static void DefineIDynamicWrapperProperty_InterfaceType(TypeBuilder typeBuilder, PropertyInfo propInfo, Type interfaceType, MethodAttributes attr)
        {
            PropertyBuilder propBuilder = typeBuilder.DefineProperty(propInfo.Name, PropertyAttributes.None, propInfo.PropertyType, Type.EmptyTypes);
            if (propInfo.GetMethod != null)
            {
                MethodBuilder propGetter = typeBuilder.DefineMethod(propInfo.GetMethod.Name, attr, CallingConventions.HasThis, propBuilder.PropertyType, Type.EmptyTypes);
                propBuilder.SetGetMethod(propGetter);

                ILGenerator il = propGetter.GetILGenerator();
                il.Emit(OpCodes.Ldtoken, interfaceType);
                il.Emit(OpCodes.Call, getTypeFromHandle);
                il.Emit(OpCodes.Ret);
            }
        }

        static bool RequireWrapping(Type type, string wrapperModId)
        {
            // Cant use the lookups because they may not be filled out yet (ie if this is the first type registered, the lookups will be empty)
            return ModIdsByRegisteredInterface.TryGetValue(type, out string? modId) && modId == wrapperModId;
        }

        // Get all properties and methods from the wrapperInterfaceType (methods exclude property getter and setters)
        IEnumerable<PropertyInfo> interfaceProps = wrapperInterface.GetProperties();
        IEnumerable<MethodInfo> interfaceMethods = wrapperInterface.GetMethods().Except(interfaceProps.SelectMany(prop => ((IEnumerable<MethodInfo>)[prop.GetMethod!, prop.SetMethod!]).Where(mInfo => mInfo != null)));

        foreach (PropertyInfo propInfo in interfaceProps)
        {
            PropertyBuilder propBuilder = typeBuilder.DefineProperty(propInfo.Name, PropertyAttributes.None, propInfo.PropertyType, Type.EmptyTypes);
            bool requireWrapping = RequireWrapping(propInfo.PropertyType, wrapperModId);

            if (propInfo.GetMethod != null)
            {
                MethodBuilder propGetter = typeBuilder.DefineMethod(propInfo.GetMethod.Name, getterSetterAttributes, CallingConventions.HasThis, propInfo.PropertyType, Type.EmptyTypes);
                propBuilder.SetGetMethod(propGetter);

                MethodInfo wrap = null!;

                ILGenerator il = propGetter.GetILGenerator();
                if (requireWrapping)
                {
                    wrap = wrapOpenGeneric.MakeGenericMethod(propInfo.PropertyType); // T
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, wrapperModIdField); // targetModId
                }
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, instanceField);

                il.Emit(OpCodes.Callvirt, instanceMethods[propGetter.Name]);
                if (requireWrapping)
                {
                    il.Emit(OpCodes.Castclass, typeof(object)); // objectToWrap
                    il.Emit(OpCodes.Ldnull); // sourceInterface
                    il.Emit(OpCodes.Call, wrap); // public static T? Wrap<T>(string targetModId, object? objectToWrap, Type? sourceInterface = null)
                }
                il.Emit(OpCodes.Ret);
            }
            if (propInfo.SetMethod != null)
            {
                MethodBuilder propSetter = typeBuilder.DefineMethod(propInfo.SetMethod.Name, getterSetterAttributes, CallingConventions.HasThis, typeof(void), [propInfo.PropertyType]);
                propBuilder.SetSetMethod(propSetter);
                propSetter.DefineParameter(1, ParameterAttributes.None, "value");

                ILGenerator il = propSetter.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, instanceField);

                if (requireWrapping)
                {
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, instanceModIdField); // targetModId
                    il.Emit(OpCodes.Ldarg_1); // objectToWrap
                    il.Emit(OpCodes.Ldtoken, propInfo.PropertyType);
                    il.Emit(OpCodes.Call, getTypeFromHandle); // sourceInterface
                    il.Emit(OpCodes.Call, wrapNonGeneric); // public static object? Wrap(string targetModId, object? objectToWrap, Type? sourceInterface = null)
                }
                else
                {
                    il.Emit(OpCodes.Ldarg_1);
                }

                il.Emit(OpCodes.Callvirt, instanceMethods[propSetter.Name]);
                il.Emit(OpCodes.Ret);
            }
        }

        foreach (MethodInfo method in interfaceMethods)
        {
            ParameterInfo[] paramInfos = method.GetParameters();
            Type[]? paramTypes = [.. paramInfos.Select(p => p.ParameterType)];
            MethodBuilder methodBuilder = typeBuilder.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot, CallingConventions.HasThis, method.ReturnType, paramTypes);

            bool wrapReturn = method.ReturnType != typeof(void) && RequireWrapping(method.ReturnType, wrapperModId);
            MethodInfo wrap = null!;

            ILGenerator il = methodBuilder.GetILGenerator();
            if (wrapReturn)
            {
                // Need to wrap the return value so the caller can read it. Pre-stack some arguments now, but will perform the method call at the end.
                wrap = wrapOpenGeneric.MakeGenericMethod(method.ReturnType); // T
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldfld, wrapperModIdField); // targetModId
            }
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, instanceField);

            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (RequireWrapping(paramInfos[i].ParameterType, wrapperModId))
                {
                    // Need to wrap the argument so the instance can read it
                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, instanceModIdField); // targetModId
                    il.Emit(OpCodes.Ldarg, i + 1); // objectToWrap
                    il.Emit(OpCodes.Ldtoken, paramInfos[i].ParameterType);
                    il.Emit(OpCodes.Call, getTypeFromHandle); // sourceInterface
                    il.Emit(OpCodes.Call, wrapNonGeneric); // public static object? Wrap(string targetModId, object? objectToWrap, Type? sourceInterface = null)
                }
                else
                {
                    il.Emit(OpCodes.Ldarg, i + 1);
                }
                ParameterAttributes attr = paramInfos[i].IsOptional ? ParameterAttributes.Optional | ParameterAttributes.HasDefault : ParameterAttributes.None;
                methodBuilder.DefineParameter(i + 1, attr, paramInfos[i].Name);
            }

            il.Emit(OpCodes.Callvirt, instanceMethods[method.Name]);
            if (wrapReturn)
            {
                il.Emit(OpCodes.Castclass, typeof(object)); // objectToWrap
                il.Emit(OpCodes.Ldnull); // sourceInterface
                il.Emit(OpCodes.Call, wrap); // public static T? Wrap<T>(string targetModId, object? objectToWrap, Type? sourceInterface = null)
            }
            il.Emit(OpCodes.Ret);
        }

        // Constructor
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
            il.Emit(OpCodes.Ldstr, wrapperModId);
            il.Emit(OpCodes.Stfld, wrapperModIdField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, instanceModId);
            il.Emit(OpCodes.Stfld, instanceModIdField);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Castclass, instanceInterface);
            il.Emit(OpCodes.Stfld, instanceField);

            il.Emit(OpCodes.Ret);
        }

        TypeInfo createdType = typeBuilder.CreateTypeInfo(); // Woot!

#if DEBUG
        if (moduleBuilder != _persistedModuleBuilder) // dont stuck in loop
        {
            if (!_assemblySaved)
            {
                // Run this through the persisted builder so we can save and inspect it in dnSpy if needed.
                CreateWrapperFactory(_persistedModuleBuilder, wrapperInterface, instanceInterface, instanceMethods, wrapperModId, instanceModId);
            }
        }
        else
        {
            return null!;
        }
#endif

        // Expression delegate ~20ns vs ~350ns using Activator.CreateInstance()
        ConstructorInfo ctor = createdType.DeclaredConstructors.First();
        ParameterExpression[] args = [.. typeof(DynamicWrapperFactory).GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType))];
        DynamicWrapperFactory factory = Expression.Lambda<DynamicWrapperFactory>(Expression.Convert(Expression.New(ctor, args), typeof(IDynamicWrapper)), args).Compile();
        return factory;
    }

    private static bool _assemblySaved = false;

    /// <summary>
    /// Saves the generated assembly to disk. Call this to inspect the generated types with dnSpy. Can only be saved once.
    /// </summary>
    /// <remarks>Requires debug build. In a release build, perform the following to inspect instead: dnSpy -> Debug Menu -> Attach to Process -> SlayTheSpire2.exe -> break/pause -> Debug Menu -> Windows -> Modules -> DynamicWrappers</remarks>
    /// <param name="filename">The file name to save to. Root directory is StS executable folder.</param>
    /// <returns><see langword="false"/> if BaseLib is not running a debug build (and no file is generated), or if the assembly has already been saved.</returns>
    internal static bool SaveAssembly(string filename)
    {
        // I dont think this belongs on the public API, but can call this via reflection from another mod if needed
#if DEBUG
        if (_assemblySaved) // Can only be saved once
            return false;

        _persistedAssemblyBuilder.Save(filename);
        _assemblySaved = true;
        BaseLibMain.Logger.Warn($"[DynamicWrappers] Saving persisted assembly to file; no further types can be created in this assembly (does not affect the runtime assembly)");
        return true;
#else
        // TODO: generate the persisted assembly on demand
        BaseLibMain.Logger.Warn($"[DynamicWrappers] Cannot save persisted assembly as it has not been generated (running release build)");
        return false;
#endif
    }

    private static string ErrorMsg(string message, [CallerLineNumber] int line = 0)
    {
        return $"<DynamicWrapper.cs Line:{line}> {message}";
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