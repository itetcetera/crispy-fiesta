namespace Mule.Engine.Core;

/// <summary>Source of random bytes, standing in for the Atari POKEY RANDOM register ($D20A).</summary>
public interface IRandomSource
{
    byte NextByte();
}

/// <summary>Default random source for real games.</summary>
public sealed class SystemRandomSource : IRandomSource
{
    readonly Random _rng;
    public SystemRandomSource(int? seed = null) => _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    public byte NextByte() => (byte)_rng.Next(256);
}

/// <summary>Ports of the original random helpers. Each call reads RANDOM exactly as the original does.</summary>
public static class Rng
{
    /// <summary>random_0_A ($1191): a random number in [0, n) by rejection; n &lt; 2 gives 0 with no read.</summary>
    public static byte Below(this IRandomSource r, byte n)
    {
        if (n < 2) return 0;
        byte v;
        do v = r.NextByte(); while (v >= n);
        return v;
    }

    /// <summary>One read used as a sign test (BIT RANDOM / BPL): true when bit 7 is set.</summary>
    public static bool Negative(this IRandomSource r) => (r.NextByte() & 0x80) != 0;

    /// <summary>
    /// calcBinomial ($1254): value + round(D * y / 2) where D is the sum of 12 random bytes minus 1530,
    /// in 1/256 units. Rounds up when the fractional byte is above 128. y = 0 returns value with no reads.
    /// The result wraps to a byte like the original.
    /// </summary>
    public static byte Binomial(this IRandomSource r, byte value, byte y)
    {
        if (y == 0) return value;
        int sum = 0;
        for (int i = 0; i < 12; i++) sum += r.NextByte();
        ushort d = (ushort)(sum - 0x05FA);
        ushort acc = d;
        if (y != 2)
        {
            // y == 1: d / 2; y >= 3: d * y / 2 (16-bit wrap, arithmetic shift).
            if (y != 1)
                for (int i = 0; i < y - 1; i++) acc = (ushort)(acc + d);
            acc = (ushort)((short)acc >> 1);
        }
        byte lo = (byte)acc, hi = (byte)(acc >> 8);
        if (lo >= 129) hi++;
        return (byte)(hi + value);
    }
}
