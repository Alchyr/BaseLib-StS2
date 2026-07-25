using System.Reflection;

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
}