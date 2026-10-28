using System.IO.Compression;

namespace Mule.Engine.Tests.Original;

/// <summary>A minimal PNG writer (8-bit RGB, no filtering) for reference shots of the original.</summary>
static class Png
{
    public static void Write(string path, uint[] rgb, int width, int height, int scale = 2)
    {
        int w = width * scale, h = height * scale;
        var raw = new byte[h * (w * 3 + 1)];
        for (int y = 0; y < h; y++)
        {
            int o = y * (w * 3 + 1);
            for (int x = 0; x < w; x++)
            {
                uint c = rgb[(y / scale) * width + x / scale];
                raw[o + 1 + x * 3] = (byte)(c >> 16);
                raw[o + 2 + x * 3] = (byte)(c >> 8);
                raw[o + 3 + x * 3] = (byte)c;
            }
        }
        using var f = File.Create(path);
        f.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var hdr = new byte[13];
        Be(hdr, 0, (uint)w); Be(hdr, 4, (uint)h);
        hdr[8] = 8; hdr[9] = 2;
        Chunk(f, "IHDR", hdr);
        var z = new MemoryStream();
        using (var zs = new ZLibStream(z, CompressionLevel.Optimal, true)) zs.Write(raw);
        Chunk(f, "IDAT", z.ToArray());
        Chunk(f, "IEND", Array.Empty<byte>());
    }

    static void Be(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        Be(len, 0, (uint)data.Length);
        s.Write(len);
        var td = new byte[4 + data.Length];
        for (int i = 0; i < 4; i++) td[i] = (byte)type[i];
        data.CopyTo(td, 4);
        s.Write(td);
        var crc = new byte[4];
        Be(crc, 0, Crc(td));
        s.Write(crc);
    }

    static uint Crc(byte[] d)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in d)
        {
            c ^= b;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
        }
        return ~c;
    }
}
