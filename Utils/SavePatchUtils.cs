using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace BaseLib.Utils;

//Utilities to help with adding properties to mod saves.
public class SavePatchUtils
{
    protected static readonly HashSet<Type> SupportedTypes =
    [
        typeof(int),
        typeof(bool),
        typeof(string),
        typeof(ModelId),
        typeof(int[]),
        typeof(SerializableCard),
        typeof(SerializableCard[]),
        typeof(List<SerializableCard>),
    ];

    /// <summary>
    /// Returns true if the given type can be stored by basegame SavedProperty.
    /// </summary>
    public static bool IsStoreTypeBaseSupported(Type t) =>
        SupportedTypes.Contains(t) || t.IsEnum || (t.IsArray && t.GetElementType()!.IsEnum);
    
    /// <summary>
    /// Returns true if the given type supports holding basegame SavedProperty values.
    /// </summary>
    /// <param name="t"></param>
    /// <returns></returns>
    public static bool IsHolderTypeBaseSupported(Type t)
    {
        return t.IsAssignableTo(typeof(RelicModel))
               || t.IsAssignableTo(typeof(CardModel))
               || t.IsAssignableTo(typeof(EnchantmentModel)) 
               || t.IsAssignableTo(typeof(ModifierModel));
    }
    
    /// <summary>
    /// Quickly sets up the properties of a JsonPropertyInfoValues object for an actual property.
    /// </summary>
    /// <typeparam name="ModifyingType">The type whose serialization is being modified, from which values must be obtained.</typeparam>
    /// <typeparam name="DeclaringType">The type in which the extra property is defined.</typeparam>
    /// <typeparam name="PropType">The property's type.</typeparam>
    /// <returns></returns>
    public static JsonPropertyInfoValues<PropType> QuickProps<ModifyingType, DeclaringType, PropType>(string propName,
        Func<ModifyingType, PropType?> getter, Action<ModifyingType, PropType?> setter)
    {
        return new JsonPropertyInfoValues<PropType>
        {
            IsProperty = true,
            IsPublic = true,
            IsVirtual = false,
            DeclaringType = typeof(ModifyingType),
            Converter = null,
            Getter = (obj) => getter((ModifyingType) obj),
            Setter = (obj, val) => setter((ModifyingType) obj, val),
            IgnoreCondition = null,
            HasJsonInclude = false,
            IsExtensionData = false,
            NumberHandling = null,
            PropertyName = propName,
            JsonPropertyName = propName,
            AttributeProviderFactory = () => typeof(DeclaringType)
                .GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, typeof(PropType), [], null)!
        };
    }
    
    /// <summary>
    /// Quickly sets up the properties of a JsonPropertyInfoValues object with a custom name for a fake property.
    /// </summary>
    /// <typeparam name="ModifyingType">The type whose serialization is being modified, from which values must be obtained.</typeparam>
    /// <typeparam name="DeclaringType">The type in which the extra property is defined.</typeparam>
    /// <typeparam name="PropType">The property's type.</typeparam>
    /// <returns></returns>
    public static JsonPropertyInfoValues<PropType> QuickProps<ModifyingType, PropType>(string propName,
        Func<ModifyingType, PropType?> getter, Action<ModifyingType, PropType?> setter)
    {
        return new JsonPropertyInfoValues<PropType>
        {
            IsProperty = true,
            IsPublic = true,
            IsVirtual = false,
            DeclaringType = typeof(ModifyingType),
            Converter = null,
            Getter = (obj) => getter((ModifyingType) obj),
            Setter = (obj, val) => setter((ModifyingType) obj, val),
            IgnoreCondition = null,
            HasJsonInclude = false,
            IsExtensionData = false,
            NumberHandling = null,
            PropertyName = propName,
            JsonPropertyName = propName
        };
    }

    public static bool TryGetSerializerDeserializer<T>(
        [NotNullWhen(true)] out Action<T, PacketWriter>? serializer,
        [NotNullWhen(true)] out Func<PacketReader, T>? deserializer)
    {
        serializer = SerializerDeserializerInfo<T>.Serializer!;
        deserializer = SerializerDeserializerInfo<T>.Deserializer!;
        return serializer != null && deserializer != null;
    }

    private static class SerializerDeserializerInfo<T>
    {
        public static Action<T, PacketWriter>? Serializer;
        public static Func<PacketReader, T>? Deserializer;
    }

    static SavePatchUtils()
    {
        SerializerDeserializerInfo<bool>.Serializer = (val, writer) => writer.WriteBool(val);
        SerializerDeserializerInfo<bool>.Deserializer = reader => reader.ReadBool();
        
        SerializerDeserializerInfo<byte>.Serializer = (val, writer) => writer.WriteByte(val);
        SerializerDeserializerInfo<byte>.Deserializer = reader => reader.ReadByte();
        SerializerDeserializerInfo<short>.Serializer = (val, writer) => writer.WriteShort(val);
        SerializerDeserializerInfo<short>.Deserializer = reader => reader.ReadShort();
        SerializerDeserializerInfo<int>.Serializer = (val, writer) => writer.WriteInt(val);
        SerializerDeserializerInfo<int>.Deserializer = reader => reader.ReadInt();
        SerializerDeserializerInfo<long>.Serializer = (val, writer) => writer.WriteLong(val);
        SerializerDeserializerInfo<long>.Deserializer = reader => reader.ReadLong();
        
        SerializerDeserializerInfo<ushort>.Serializer = (val, writer) => writer.WriteUShort(val);
        SerializerDeserializerInfo<ushort>.Deserializer = reader => reader.ReadUShort();
        SerializerDeserializerInfo<uint>.Serializer = (val, writer) => writer.WriteUInt(val);
        SerializerDeserializerInfo<uint>.Deserializer = reader => reader.ReadUInt();
        SerializerDeserializerInfo<ulong>.Serializer = (val, writer) => writer.WriteULong(val);
        SerializerDeserializerInfo<ulong>.Deserializer = reader => reader.ReadULong();
        
        SerializerDeserializerInfo<float>.Serializer = (val, writer) => writer.WriteFloat(val);
        SerializerDeserializerInfo<float>.Deserializer = reader => reader.ReadFloat();
        SerializerDeserializerInfo<double>.Serializer = (val, writer) => writer.WriteDouble(val);
        SerializerDeserializerInfo<double>.Deserializer = reader => reader.ReadDouble();
        //Slight loss of precision, but generally shouldn't be used for precise value communication.
        SerializerDeserializerInfo<decimal>.Serializer = (val, writer) => writer.WriteDouble((double)val);
        SerializerDeserializerInfo<decimal>.Deserializer = reader => (decimal)reader.ReadDouble();
        
        SerializerDeserializerInfo<string>.Serializer = (val, writer) => writer.WriteString(val);
        SerializerDeserializerInfo<string>.Deserializer = reader => reader.ReadString();
        
        SerializerDeserializerInfo<ModelId>.Serializer = (val, writer) => writer.WriteFullModelId(val);
        SerializerDeserializerInfo<ModelId>.Deserializer = reader => reader.ReadFullModelId();
        
        //Basic arrays
        SerializerDeserializerInfo<int[]>.Serializer = (arr, writer) =>
        {
            writer.WriteInt(arr.Length);
            foreach (var val in arr)
            {
                writer.WriteInt(val);
            }
        };
        SerializerDeserializerInfo<int[]>.Deserializer = reader =>
        {
            var length = reader.ReadInt();
            var arr = new int[length];
            for (var i = 0; i < length; ++i)
                arr[i] = reader.ReadInt();
            return arr;
        };
        SerializerDeserializerInfo<float[]>.Serializer = (arr, writer) =>
        {
            writer.WriteInt(arr.Length);
            foreach (var val in arr)
            {
                writer.WriteFloat(val);
            }
        };
        SerializerDeserializerInfo<float[]>.Deserializer = reader =>
        {
            var length = reader.ReadInt();
            var arr = new float[length];
            for (var i = 0; i < length; ++i)
                arr[i] = reader.ReadFloat();
            return arr;
        };
        SerializerDeserializerInfo<double[]>.Serializer = (arr, writer) =>
        {
            writer.WriteInt(arr.Length);
            foreach (var val in arr)
            {
                writer.WriteDouble(val);
            }
        };
        SerializerDeserializerInfo<double[]>.Deserializer = reader =>
        {
            var length = reader.ReadInt();
            var arr = new double[length];
            for (var i = 0; i < length; ++i)
                arr[i] = reader.ReadDouble();
            return arr;
        };
        SerializerDeserializerInfo<bool[]>.Serializer = (arr, writer) =>
        {
            writer.WriteInt(arr.Length);
            foreach (var val in arr)
            {
                writer.WriteBool(val);
            }
        };
        SerializerDeserializerInfo<bool[]>.Deserializer = reader =>
        {
            var length = reader.ReadInt();
            var arr = new bool[length];
            for (var i = 0; i < length; ++i)
                arr[i] = reader.ReadBool();
            return arr;
        };
        SerializerDeserializerInfo<SerializableCard[]>.Serializer = (arr, writer) =>
        {
            writer.WriteInt(arr.Length);
            foreach (var val in arr)
            {
                writer.Write(val);
            }
        };
        SerializerDeserializerInfo<SerializableCard[]>.Deserializer = reader =>
        {
            var length = reader.ReadInt();
            var arr = new SerializableCard[length];
            for (var i = 0; i < length; ++i)
            {
                arr[i] = reader.Read<SerializableCard>();
            }
            
            return arr;
        };
    }
}

public sealed record ExtendedSaveInfo<DataSourceType, DataHolderType>(string Id, 
    Action<DataSourceType, DataHolderType> Getter, Action<DataSourceType, DataHolderType> Setter,
    Action<DataHolderType, PacketWriter> Serializer, Action<DataHolderType, PacketReader> Deserializer)
    : IComparable<ExtendedSaveInfo<DataSourceType, DataHolderType>>
{
    /// <inheritdoc />
    public int CompareTo(ExtendedSaveInfo<DataSourceType, DataHolderType>? other)
    {
        return string.Compare(Id, other?.Id, StringComparison.Ordinal);
    }
}