using StbImageSharp;

namespace StructureImport;

/// <summary>
/// An RGBA image of bytes, rows top to bottom (glTF's and DDS's order).
/// </summary>
public sealed class Rgba
{
    /// <summary>
    /// The width in pixels.
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// The height in pixels.
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// Four bytes per pixel, R G B A.
    /// </summary>
    public byte[] Pixels { get; }

    /// <summary>
    /// An image of the given size, every pixel <paramref name="fill"/>.
    /// </summary>
    public Rgba(int width, int height, (byte R, byte G, byte B, byte A) fill = default)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];
        for (int i = 0; i < Pixels.Length; i += 4)
        {
            Pixels[i] = fill.R;
            Pixels[i + 1] = fill.G;
            Pixels[i + 2] = fill.B;
            Pixels[i + 3] = fill.A;
        }
    }

    /// <summary>
    /// An image over existing pixels.
    /// </summary>
    Rgba(int width, int height, byte[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    /// <summary>
    /// Decodes a PNG or JPEG. Throws when the bytes are neither.
    /// </summary>
    public static Rgba Decode(byte[] encoded)
    {
        var image = ImageResult.FromMemory(encoded, ColorComponents.RedGreenBlueAlpha);
        return new Rgba(image.Width, image.Height, image.Data);
    }

    /// <summary>
    /// This image at <paramref name="width"/> × <paramref name="height"/>: each new pixel the average of the source pixels it covers (or the
    /// nearest one when enlarging).
    /// </summary>
    public Rgba Resized(int width, int height)
    {
        if (width == Width && height == Height) return this;

        var result = new Rgba(width, height);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                Average(x, y, width, height, result.Pixels.AsSpan((y * width + x) * 4, 4));

        return result;
    }

    /// <summary>
    /// The average of the source pixels under target pixel (<paramref name="x"/>, <paramref name="y"/>) of a <paramref name="width"/> ×
    /// <paramref name="height"/> image, written to <paramref name="into"/>.
    /// </summary>
    void Average(int x, int y, int width, int height, Span<byte> into)
    {
        int x0 = x * Width / width, x1 = Math.Max(x0 + 1, (x + 1) * Width / width);
        int y0 = y * Height / height, y1 = Math.Max(y0 + 1, (y + 1) * Height / height);
        Span<int> sum = stackalloc int[4];
        for (int sy = y0; sy < y1; sy++)
            for (int sx = x0; sx < x1; sx++)
                for (int c = 0; c < 4; c++)
                    sum[c] += Pixels[(sy * Width + sx) * 4 + c];

        int n = (x1 - x0) * (y1 - y0);
        for (int c = 0; c < 4; c++) into[c] = (byte)((sum[c] + n / 2) / n);
    }

    /// <summary>
    /// The power of two nearest to <paramref name="size"/>, at least 4 (a compressed block) and at most <paramref name="max"/>.
    /// </summary>
    public static int TextureSide(int size, int max)
    {
        int side = 4;
        while (side < max && side * 3 / 2 < size) side *= 2;
        return side;
    }
}
