using System.Text;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace StructureImport;

/// <summary>
/// Writes a block-compressed DDS with a full mip chain and the legacy header (FourCC <c>DXT1</c> / <c>DXT5</c>), as the game's building textures
/// are stored.
/// </summary>
public static class DdsWriter
{
    /// <summary>
    /// Header flags: CAPS, HEIGHT, WIDTH, PIXELFORMAT, MIPMAPCOUNT, LINEARSIZE.
    /// </summary>
    const uint HeaderFlags = 0x1 | 0x2 | 0x4 | 0x1000 | 0x20000 | 0x80000;

    /// <summary>
    /// Caps: COMPLEX, TEXTURE, MIPMAP.
    /// </summary>
    const uint Caps = 0x8 | 0x1000 | 0x400000;

    /// <summary>
    /// Compresses <paramref name="image"/> as <paramref name="format"/> (BC1 or BC3) and writes it to <paramref name="path"/>.
    /// </summary>
    public static void Write(Rgba image, CompressionFormat format, string path)
    {
        var encoder = new BcEncoder(format);
        encoder.OutputOptions.GenerateMipMaps = true;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        byte[][] levels = encoder.EncodeToRawBytes(image.Pixels, image.Width, image.Height, PixelFormat.Rgba32);

        using var stream = File.Create(path);
        using var w = new BinaryWriter(stream);
        WriteHeader(w, image, format, levels);
        foreach (var level in levels) w.Write(level);
    }

    /// <summary>
    /// The magic and the 124-byte header.
    /// </summary>
    static void WriteHeader(BinaryWriter w, Rgba image, CompressionFormat format, byte[][] levels)
    {
        w.Write(Encoding.ASCII.GetBytes("DDS "));
        w.Write(124u);
        w.Write(HeaderFlags);
        w.Write((uint)image.Height);
        w.Write((uint)image.Width);
        w.Write((uint)levels[0].Length);
        w.Write(0u); // depth
        w.Write((uint)levels.Length);
        for (int i = 0; i < 11; i++) w.Write(0u);

        // Pixel format: size, FOURCC flag, the FourCC, then unused bit counts and masks.
        w.Write(32u);
        w.Write(0x4u);
        w.Write(Encoding.ASCII.GetBytes(format == CompressionFormat.Bc1 ? "DXT1" : "DXT5"));
        for (int i = 0; i < 5; i++) w.Write(0u);

        w.Write(Caps);
        for (int i = 0; i < 4; i++) w.Write(0u);
    }
}
