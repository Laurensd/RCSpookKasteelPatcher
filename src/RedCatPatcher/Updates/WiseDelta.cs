using System.Buffers.Binary;

namespace RedCatPatcher.Updates;

/// <summary>
/// Het delta-formaat van de Wise-installer (WISE0232.DLL) waarmee Davilex de officiële updates bouwde.
/// Achtergrond: zie docs/TECHNISCH.md, "Officiële updates".
/// </summary>
/// <remarks>
/// Header (little-endian): <c>01 91 2A</c>, 2 bytes, CRC-32 en grootte van het nieuwe bestand, headerlengte
/// (25, of 0 als het hele nieuwe bestand erachter staat), CRC-32 en grootte van het oude bestand.
/// Daarna: lengte van de commando's, de commando's en de literals. Elk commando zet eerst literals in de uitvoer
/// (de leeskop in het oude bestand schuift even ver mee), verschuift dan eventueel de leeskop en kopieert een stuk
/// uit het oude bestand.
/// </remarks>
public static class WiseDelta
{
    private const int HeaderLength = 25;

    public static bool IsDelta(ReadOnlySpan<byte> data) =>
        data.Length >= HeaderLength && data[0] == 0x01 && data[1] == 0x91 && data[2] == 0x2A;

    /// <summary>CRC-32 en grootte van het bestand waarop de delta past.</summary>
    public static (uint Crc, int Size) Source(ReadOnlySpan<byte> delta) => (U32(delta, 17), (int)U32(delta, 21));

    /// <summary>CRC-32 en grootte van het resultaat.</summary>
    public static (uint Crc, int Size) Target(ReadOnlySpan<byte> delta) => (U32(delta, 5), (int)U32(delta, 9));

    public static bool Matches(ReadOnlySpan<byte> file, (uint Crc, int Size) expected) =>
        file.Length == expected.Size && Crc32.Compute(file) == expected.Crc;

    /// <summary>Past de delta toe op <paramref name="old"/>; dat moet het bronbestand zijn (zie <see cref="Source"/>).</summary>
    public static byte[] Apply(ReadOnlySpan<byte> old, ReadOnlySpan<byte> delta)
    {
        if (!IsDelta(delta)) throw new InvalidDataException("Geen Wise-delta.");
        var target = Target(delta);
        byte[] result = U32(delta, 13) == 0
            ? delta[HeaderLength..].ToArray()
            : Decode(old, delta, target.Size);
        if (!Matches(result, target))
            throw new InvalidDataException("Wise-delta levert niet het verwachte bestand op.");
        return result;
    }

    private static byte[] Decode(ReadOnlySpan<byte> old, ReadOnlySpan<byte> delta, int size)
    {
        int opsLength = (int)U32(delta, HeaderLength);
        var ops = delta.Slice(HeaderLength + 4, opsLength);
        var literals = delta[(HeaderLength + 4 + opsLength)..];
        var output = new byte[size];
        int i = 0, lit = 0, src = 0, dst = 0;

        while (i < ops.Length && dst < size)
        {
            byte b = ops[i++];
            int literalCount, copyCount, skip = 0;
            if ((b & 0x80) == 0)
            {
                // Kort commando: 0LLLCCCC = L literals, C+3 bytes kopiëren
                literalCount = b >> 4;
                copyCount = (b & 0x0F) + 3;
            }
            else
            {
                // 1ELLLLLL: aantal literals (E = er volgen extra bytes)
                literalCount = b & 0x3F;
                if ((b & 0x40) != 0) literalCount = Extend(ops, ref i, literalCount);
                // EKCCCCCC: aantal te kopiëren bytes - 3 (E = extra bytes, K = kopiëren zonder de leeskop te verschuiven)
                byte c = ops[i++];
                copyCount = c & 0x3F;
                if ((c & 0x80) != 0) copyCount = Extend(ops, ref i, copyCount);
                copyCount += 3;
                if ((c & 0x40) == 0)
                {
                    // Verschuiving van de leeskop, met teken (bit 6 van de eerste byte)
                    byte x = ops[i++];
                    skip = x & 0x7F;
                    if (skip > 0x3F) skip -= 0x80;
                    if ((x & 0x80) != 0) skip = Extend(ops, ref i, skip);
                }
            }

            literals.Slice(lit, literalCount).CopyTo(output.AsSpan(dst));
            lit += literalCount;
            dst += literalCount;
            src += literalCount;
            if (dst >= size) break;

            src += skip;
            old.Slice(src, copyCount).CopyTo(output.AsSpan(dst));
            src += copyCount;
            dst += copyCount;
        }
        if (dst != size) throw new InvalidDataException("Wise-delta is onvolledig.");
        return output;
    }

    // Getallen lopen door in maximaal twee bytes van 7 bits (bit 7 = er volgt meer) en een laatste byte van 8 bits.
    private static int Extend(ReadOnlySpan<byte> ops, ref int i, int value)
    {
        for (int n = 0; n < 2; n++)
        {
            byte x = ops[i++];
            value = (value << 7) | (x & 0x7F);
            if ((x & 0x80) == 0) return value;
        }
        return (value << 8) | ops[i++];
    }

    private static uint U32(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
