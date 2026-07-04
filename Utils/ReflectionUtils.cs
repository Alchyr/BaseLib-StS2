using System.Reflection;
using System.Runtime.CompilerServices;

namespace BaseLib.Utils;

public static class ReflectionUtils
{
    private const BindingFlags DeclaredOnlyLookup = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
    public static Action<T?, TValue> GetSetterForProperty<T, TValue>(string propName) where T : class
    {
        var propertyInfo = typeof(T).GetProperty(propName, DeclaredOnlyLookup);

        if (propertyInfo is null)
        {
            throw new InvalidOperationException($"Property {propName} not found in type {typeof(T).FullName}");
        }

        return GetPropertySetter(propertyInfo);

        static Action<T?, TValue> GetPropertySetter(PropertyInfo prop)
        {
            var setter = prop.GetSetMethod(nonPublic: true);
            if (setter is not null)
            {
                return (obj, value) => setter.Invoke(obj, [value]);
            }

            var backingField = prop.DeclaringType?.GetField($"<{prop.Name}>k__BackingField", DeclaredOnlyLookup);
            if (backingField is null)
            {
                throw new InvalidOperationException($"Could not find a way to set {prop.DeclaringType?.FullName}.{prop.Name}. Try adding a private setter.");
            }

            return (obj, value) => backingField.SetValue(obj, value);
        }
    }
    public static Action<object?, TValue> GetSetterForProperty<TValue>(Type type, string propName)
    {
        var propertyInfo = type.GetProperty(propName, DeclaredOnlyLookup);

        if (propertyInfo is null)
        {
            throw new InvalidOperationException($"Property {propName} not found in type {type.FullName}");
        }

        return GetPropertySetter(propertyInfo);

        static Action<object?, TValue> GetPropertySetter(PropertyInfo prop)
        {
            var setter = prop.GetSetMethod(nonPublic: true);
            if (setter is not null)
            {
                return (obj, value) => setter.Invoke(obj, [value]);
            }

            var backingField = prop.DeclaringType?.GetField($"<{prop.Name}>k__BackingField", DeclaredOnlyLookup);
            if (backingField is null)
            {
                throw new InvalidOperationException($"Could not find a way to set {prop.DeclaringType?.FullName}.{prop.Name}. Try adding a private setter.");
            }

            return (obj, value) => backingField.SetValue(obj, value);
        }
    }
    
    /// <summary>
    /// Returns a list of instantiated objects. One for (and of) each class that inherits from the specified superclass. <br/>
    /// Classes require a parameterless constructor.
    /// </summary>
    /// <typeparam name="T">The Type of the Superclass</typeparam>
    /// <returns></returns>
    public static List<T> GetListOfInstantiatedSubclassesFromAllAssemblies<T>() where T : class
    {
        var baseType = typeof(T);
        var instances = new List<T>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var assembly in assemblies)
        {
            if (assembly.IsDynamic) continue;

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray()!;
            }
            catch (Exception)
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type is not { IsClass: true, IsAbstract: false } || !baseType.IsAssignableFrom(type)) continue;
                try
                {
                    if (type.GetConstructor(Type.EmptyTypes) != null && Activator.CreateInstance(type) is T instance)
                        instances.Add(instance);
                }
                catch (Exception ex)
                {
                    BaseLibMain.Logger.Error($"Mod assembly {assembly.GetName().Name} failed to instantiate type {type.FullName} for base {baseType.Name}. Error: {ex.Message}");
                }
            }
        }

        return instances;
    }
    
    /// <summary>
    /// Returns a list of instantiated objects. One for (and of) each class that inherits from the specified superclass within the calling assembly. <br/>
    /// Classes require a parameterless constructor.
    /// </summary>
    /// <typeparam name="T">The Type of the Superclass</typeparam>
    /// <returns></returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static List<T> GetListOfInstantiatedSubclassesFromCurrentAssemblies<T>() where T : class
    {
        var baseType = typeof(T);
        var instances = new List<T>();
        var assembly = Assembly.GetCallingAssembly();

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.Where(t => t != null).ToArray()!;
        }

        foreach (var type in types)
        {
            if (type is not { IsClass: true, IsAbstract: false } || !baseType.IsAssignableFrom(type)) continue;
            try
            {
                if (type.GetConstructor(Type.EmptyTypes) != null && Activator.CreateInstance(type) is T instance)
                    instances.Add(instance);
            }
            catch (Exception ex)
            {
                BaseLibMain.Logger.Error($"Mod assembly {assembly.GetName().Name} failed to instantiate type {type.FullName} for base {baseType.Name}. Error: {ex.Message}");
            }
        }

        return instances;
    }
}