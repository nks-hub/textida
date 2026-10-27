using System.Buffers.Binary;

namespace NksHub.Textida.Core.Hex;

/// <summary>What an operation over the selected bytes does.</summary>
public enum HexOperation
{
    /// <summary>Each byte xor the operand bytes, which repeat from the first.</summary>
    Xor,
    And,
    Or,
    /// <summary>Each byte plus the operand byte, modulo 256.</summary>
    Add,
    Not,
    /// <summary>Each byte shifted by the count; bits that leave are lost.</summary>
    ShiftLeft,
    ShiftRight,
    /// <summary>Each byte rotated by the count; no bit is lost.</summary>
    RotateLeft,
    RotateRight,
    /// <summary>The bytes of every group of 2, 4 or 8 reversed; a rest shorter than a group stays.</summary>
    SwapEndian,
}

/// <summary>The byte arithmetic of the operations over a selection, and the sequences the number fill writes.</summary>
public static class HexOperations
{
    /// <summary>
    /// Applies <paramref name="operation"/> to <paramref name="data"/> in place. The operations that take an operand (<c>Xor</c>, <c>And</c>, <c>Or</c>,
    /// <c>Add</c>) read <paramref name="operand"/> as a pattern that starts again at its first byte; shifts and rotations read the count 1 to 7 from
    /// <paramref name="amount"/>, and <c>SwapEndian</c> the group size 2, 4 or 8.
    /// </summary>
    public static void Apply(HexOperation operation, Span<byte> data, ReadOnlySpan<byte> operand = default, int amount = 0)
    {
        switch (operation)
        {
            case HexOperation.Xor or HexOperation.And or HexOperation.Or or HexOperation.Add:
                if (operand.IsEmpty) throw new ArgumentException("The operation needs at least one operand byte.", nameof(operand));
                for (int i = 0, k = 0; i < data.Length; i++, k = k + 1 == operand.Length ? 0 : k + 1)
                    data[i] = operation switch
                    {
                        HexOperation.Xor => (byte)(data[i] ^ operand[k]),
                        HexOperation.And => (byte)(data[i] & operand[k]),
                        HexOperation.Or => (byte)(data[i] | operand[k]),
                        _ => (byte)(data[i] + operand[k]),
                    };
                break;
            case HexOperation.Not:
                for (int i = 0; i < data.Length; i++) data[i] = (byte)~data[i];
                break;
            case HexOperation.ShiftLeft or HexOperation.ShiftRight or HexOperation.RotateLeft or HexOperation.RotateRight:
                if (amount is < 1 or > 7) throw new ArgumentOutOfRangeException(nameof(amount), "A shift or rotation is by 1 to 7 bits.");
                for (int i = 0; i < data.Length; i++)
                    data[i] = operation switch
                    {
                        HexOperation.ShiftLeft => (byte)(data[i] << amount),
                        HexOperation.ShiftRight => (byte)(data[i] >> amount),
                        HexOperation.RotateLeft => (byte)(data[i] << amount | data[i] >> 8 - amount),
                        _ => (byte)(data[i] >> amount | data[i] << 8 - amount),
                    };
                break;
            case HexOperation.SwapEndian:
                if (amount is not (2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(amount), "The group is 2, 4 or 8 bytes.");
                for (int i = 0; i + amount <= data.Length; i += amount) data.Slice(i, amount).Reverse();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    /// <summary>
    /// Fills <paramref name="data"/> with the numbers <paramref name="start"/>, <paramref name="start"/> + <paramref name="step"/>, ... as words of
    /// <paramref name="width"/> bytes (1, 2, 4 or 8). A last word that does not fit keeps only its first bytes.
    /// </summary>
    public static void Sequence(Span<byte> data, long start, long step, int width, bool bigEndian)
    {
        if (width is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(width), "A number is 1, 2, 4 or 8 bytes.");
        Span<byte> word = stackalloc byte[8];
        ulong value = unchecked((ulong)start);
        for (int at = 0; at < data.Length; at += width)
        {
            if (bigEndian) BinaryPrimitives.WriteUInt64BigEndian(word, value << (64 - width * 8));
            else BinaryPrimitives.WriteUInt64LittleEndian(word, value);
            word[..Math.Min(width, data.Length - at)].CopyTo(data[at..]);
            value = unchecked(value + (ulong)step);
        }
    }
}
