using System.Buffers.Binary;
using System.Text;

namespace ProfitGraph.Venues.Steam.Enrollment;

/// <summary>
///     The few protocol buffer encodings Steam's authentication service needs (Profit Fox Designer <c>SteamProtobuf</c>):
///     varints, fixed 64-bit integers and length-delimited fields, read with bounds checked and at most 1024 fields.
/// </summary>
internal static class SteamProtobuf
{
    public static byte[] Integer(int number, ulong value)
    {
        using var stream = new MemoryStream();
        Varint(stream, (ulong)(number << 3));
        Varint(stream, value);

        return stream.ToArray();
    }

    public static byte[] Fixed64(int number, ulong value)
    {
        using var stream = new MemoryStream();
        Varint(stream, (ulong)((number << 3) | 1));
        var bytes = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        stream.Write(bytes);

        return stream.ToArray();
    }

    public static byte[] Text(int number, string value)
    {
        return Bytes(number, Encoding.UTF8.GetBytes(value));
    }

    public static byte[] Bytes(int number, byte[] value)
    {
        using var stream = new MemoryStream();
        Varint(stream, (ulong)((number << 3) | 2));
        Varint(stream, (ulong)value.Length);
        stream.Write(value);

        return stream.ToArray();
    }

    public static byte[] Message(params byte[][] fields)
    {
        return fields.SelectMany(field => field).ToArray();
    }

    public static IReadOnlyList<SteamProtobufField> Read(byte[] bytes)
    {
        var fields = new List<SteamProtobufField>();
        var offset = 0;

        while (offset < bytes.Length)
        {
            ulong key = ReadVarint(bytes, ref offset);
            var number = checked((int)(key >> 3));
            var wire = (int)(key & 7);

            if (number == 0 || fields.Count >= 1024)
            {
                throw new InvalidDataException("Invalid Steam protocol message.");
            }

            if (wire == 0)
            {
                fields.Add(new SteamProtobufField(number, [], ReadVarint(bytes, ref offset)));

                continue;
            }

            int length = wire switch { 1 => 8, 2 => checked((int)ReadVarint(bytes, ref offset)), 5 => 4, _ => -1 };

            if (length < 0 || length > bytes.Length - offset)
            {
                throw new InvalidDataException("Invalid Steam protocol message.");
            }

            byte[] data = bytes.AsSpan(offset, length).ToArray();
            offset += length;
            fields.Add(new SteamProtobufField(number, data, wire == 1 ? BinaryPrimitives.ReadUInt64LittleEndian(data) : 0));
        }

        return fields;
    }

    public static string String(IReadOnlyList<SteamProtobufField> fields, int number)
    {
        return Encoding.UTF8.GetString(fields.FirstOrDefault(field => field.Number == number)?.Data ?? []);
    }

    public static ulong Number(IReadOnlyList<SteamProtobufField> fields, int number)
    {
        return fields.FirstOrDefault(field => field.Number == number)?.Integer ?? 0;
    }

    private static void Varint(Stream stream, ulong value)
    {
        while (value > 127)
        {
            stream.WriteByte((byte)((value & 127) | 128));
            value >>= 7;
        }

        stream.WriteByte((byte)value);
    }

    private static ulong ReadVarint(byte[] bytes, ref int offset)
    {
        ulong value = 0;

        for (var shift = 0; shift < 64; shift += 7)
        {
            if (offset >= bytes.Length || (shift == 63 && bytes[offset] > 1))
            {
                throw new InvalidDataException("Invalid Steam protocol message.");
            }

            byte current = bytes[offset++];
            value |= (ulong)(current & 127) << shift;

            if ((current & 128) == 0)
            {
                return value;
            }
        }

        throw new InvalidDataException("Invalid Steam protocol message.");
    }
}