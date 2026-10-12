using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;

namespace Styles.Tooling.Protobuf;

/// <summary>
/// The number of a field in the Protobuf message: IT is what travels, never the name. A field can be renamed without
/// breaking anything; changing its number, or reusing it, breaks all the clients already deployed.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class FieldAttribute(int number) : Attribute
{
    public int Number => number;

    /// <summary>"Zigzag" encoding (type <c>sint64</c>): small negative numbers fit in few bytes.</summary>
    public bool ZigZag { get; init; }
}

/// <summary>
/// A Protobuf encoder (proto3) reduced to what the demo needs, by reflection on the <see cref="FieldAttribute"/> attributes:
/// integers (varint, zigzag), booleans, enumerations, strings, nested messages and repeated fields.
/// For real, <c>protoc</c> generates this code from the .proto file (Google.Protobuf, Grpc.Tools).
/// </summary>
public static class Protobuf
{
    private const int Varint = 0, Fixed64 = 1, Delimited = 2, Fixed32 = 5;

    private sealed record Field(PropertyInfo Property, int Number, bool ZigZag, Type Type, bool Repeated);

    private static readonly ConcurrentDictionary<Type, Field[]> Fields = new();

    private static Field[] FieldsOf(Type type) => Fields.GetOrAdd(type, t => t.GetProperties()
        .Select(p => (Property: p, Attribute: p.GetCustomAttribute<FieldAttribute>()))
        .Where(x => x.Attribute is not null)
        .Select(x =>
        {
            var repeated = x.Property.PropertyType.IsGenericType && x.Property.PropertyType.GetGenericTypeDefinition() == typeof(List<>);
            var type = repeated ? x.Property.PropertyType.GetGenericArguments()[0] : x.Property.PropertyType;
            return new Field(x.Property, x.Attribute!.Number, x.Attribute.ZigZag, type, repeated);
        })
        .OrderBy(c => c.Number)
        .ToArray());

    // ─── Writing ───

    public static byte[] Serialize(object message)
    {
        var buffer = new MemoryStream();
        Write(buffer, message);
        return buffer.ToArray();
    }

    private static void Write(MemoryStream output, object message)
    {
        foreach (var field in FieldsOf(message.GetType()))
        {
            var value = field.Property.GetValue(message);
            if (field.Repeated)
            {
                foreach (var element in (IList)value!)
                    WriteValue(output, field, element!);
            }
            else if (!IsDefault(value))   // proto3: a default value does not travel (0 bytes)
                WriteValue(output, field, value!);
        }
    }

    private static bool IsDefault(object? value) => value switch
    {
        null => true,
        string s => s.Length == 0,
        long l => l == 0,
        int i => i == 0,
        bool b => !b,
        Enum e => Convert.ToInt32(e) == 0,
        _ => false
    };

    private static void WriteValue(MemoryStream output, Field field, object value)
    {
        switch (value)
        {
            case string text:
                WriteHeader(output, field.Number, Delimited);
                var bytes = Encoding.UTF8.GetBytes(text);
                WriteVarint(output, (ulong)bytes.Length);
                output.Write(bytes);
                break;
            case long integer:
                WriteHeader(output, field.Number, Varint);
                WriteVarint(output, field.ZigZag ? (ulong)((integer << 1) ^ (integer >> 63)) : (ulong)integer);
                break;
            case int integer:
                WriteHeader(output, field.Number, Varint);
                WriteVarint(output, (ulong)(long)integer);   // a negative int32 extends over 64 bits: 10 bytes
                break;
            case bool boolean:
                WriteHeader(output, field.Number, Varint);
                WriteVarint(output, boolean ? 1UL : 0UL);
                break;
            case Enum enumeration:
                WriteHeader(output, field.Number, Varint);
                WriteVarint(output, (ulong)(long)Convert.ToInt32(enumeration));
                break;
            default:   // a nested message: its length, then its content
                WriteHeader(output, field.Number, Delimited);
                var inner = Serialize(value);
                WriteVarint(output, (ulong)inner.Length);
                output.Write(inner);
                break;
        }
    }

    private static void WriteHeader(MemoryStream output, int number, int wireType) =>
        WriteVarint(output, (ulong)((number << 3) | wireType));

    /// <summary>7 bits per byte, the most significant bit says "more to come": 1 fits in 1 byte, 300 in 2.</summary>
    private static void WriteVarint(MemoryStream output, ulong value)
    {
        while (value >= 0x80)
        {
            output.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        output.WriteByte((byte)value);
    }

    // ─── Reading ───

    public static T Deserialize<T>(ReadOnlySpan<byte> bytes) where T : new() => (T)Deserialize(typeof(T), bytes);

    private static object Deserialize(Type type, ReadOnlySpan<byte> bytes)
    {
        var message = Activator.CreateInstance(type)!;
        var fields = FieldsOf(type);
        var position = 0;
        while (position < bytes.Length)
        {
            var header = ReadVarint(bytes, ref position);
            var number = (int)(header >> 3);
            var wireType = (int)(header & 7);
            var field = Array.Find(fields, c => c.Number == number);
            if (field is null)
            {
                // An unknown field (added by a more recent version on the other side): it is skipped, without error.
                // That is what allows a contract to evolve without redeploying all the clients at the same time.
                Skip(bytes, ref position, wireType);
                continue;
            }
            var value = ReadValue(field, bytes, ref position, wireType);
            if (field.Repeated)
                ((IList)field.Property.GetValue(message)!).Add(value);
            else
                field.Property.SetValue(message, value);
        }
        return message;
    }

    private static object ReadValue(Field field, ReadOnlySpan<byte> bytes, ref int position, int wireType)
    {
        var expected = field.Type == typeof(string) || field.Type.IsClass ? Delimited : Varint;
        if (wireType != expected)
            throw new FormatException($"Field {field.Number}: wire type {wireType} received, {expected} expected.");

        if (expected == Delimited)
        {
            var length = checked((int)ReadVarint(bytes, ref position));
            if (position + length > bytes.Length)
                throw new FormatException($"Field {field.Number}: truncated message.");
            var content = bytes.Slice(position, length);
            position += length;
            return field.Type == typeof(string) ? Encoding.UTF8.GetString(content) : Deserialize(field.Type, content);
        }

        var raw = ReadVarint(bytes, ref position);
        if (field.Type == typeof(long))
            return field.ZigZag ? (long)(raw >> 1) ^ -(long)(raw & 1) : (long)raw;
        if (field.Type == typeof(int))
            return (int)(long)raw;
        if (field.Type == typeof(bool))
            return raw != 0;
        if (field.Type.IsEnum)
            return Enum.ToObject(field.Type, (int)(long)raw);   // an unknown value is kept as is (proto3)
        throw new NotSupportedException($"Type {field.Type.Name} not supported.");
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> bytes, ref int position)
    {
        ulong value = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (position >= bytes.Length)
                throw new FormatException("Truncated varint.");
            var b = bytes[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
                return value;
        }
        throw new FormatException("Varint too long.");
    }

    private static void Skip(ReadOnlySpan<byte> bytes, ref int position, int wireType)
    {
        switch (wireType)
        {
            case Varint: ReadVarint(bytes, ref position); break;
            case Fixed64: position += 8; break;
            case Delimited:
                // Not "position += ReadVarint(ref position)": the old value of position would be read BEFORE the call.
                var length = checked((int)ReadVarint(bytes, ref position));
                position += length;
                break;
            case Fixed32: position += 4; break;
            default: throw new FormatException($"Unknown wire type {wireType}.");
        }
        if (position > bytes.Length)
            throw new FormatException("Truncated message.");
    }
}
