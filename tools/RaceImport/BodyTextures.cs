using BCnEncoder.Shared;
using StructureImport;

namespace RaceImport;

/// <summary>
/// A body's textures in the game's character layout (docs/race-import.md "Textures"): <c>_d</c> = base colour with gloss in alpha, <c>_n</c> =
/// the normal map with X in alpha and Y (flipped) in green; both BC3 with mips.
/// </summary>
public static class BodyTextures
{
    /// <summary>
    /// Roughness taken for a model without a metallic-roughness image (0 to 255).
    /// </summary>
    const byte DefaultRoughness = 200;

    /// <summary>
    /// Writes <c>&lt;stem&gt;_d.dds</c> and, when the model has a normal map, <c>&lt;stem&gt;_n.dds</c> at <paramref name="side"/> pixels square;
    /// returns whether the normal map was written. Throws when the model has no base colour image.
    /// </summary>
    public static bool Write(SourceBody body, string folder, string stem, int side) => Write(body, [], folder, stem, side);

    /// <summary>
    /// As <see cref="Write(SourceBody, string, string, int)"/>, with <paramref name="parts"/> packed beside the body (<see cref="Attachments"/>):
    /// the body's images in the left half of a texture twice as wide, each part's in a tile of the right half. The normal map is written when
    /// the body has one (flat where a part has none).
    /// </summary>
    public static bool Write(SourceBody body, IReadOnlyList<Attachment> parts, string folder, string stem, int side)
    {
        if (parts.Count > 0) return WriteAtlas(body, parts, folder, stem, side);


        var colour = Rgba.Decode(body.BaseColourImage ?? throw new InvalidDataException("the model has no base colour texture")).Resized(side, side);
        var metalRough = body.MetallicRoughnessImage is { } mr ? Rgba.Decode(mr).Resized(side, side) : null;
        DdsWriter.Write(Diffuse(colour, metalRough), CompressionFormat.Bc3, Path.Combine(folder, stem + "_d.dds"));
        if (body.NormalImage is null) return false;

        DdsWriter.Write(Normal(Rgba.Decode(body.NormalImage).Resized(side, side)), CompressionFormat.Bc3, Path.Combine(folder, stem + "_n.dds"));
        return true;
    }

    /// <summary>
    /// The atlas textures of a body with parts.
    /// </summary>
    static bool WriteAtlas(SourceBody body, IReadOnlyList<Attachment> parts, string folder, string stem, int side)
    {
        var colour = Atlas(side, body.BaseColourImage ?? throw new InvalidDataException("the model has no base colour texture"),
            parts.Select(p => (byte[]?)p.BaseColourImage), default);
        bool roughness = body.MetallicRoughnessImage is not null || parts.Any(p => p.MetallicRoughnessImage is not null);
        var metalRough = roughness
            ? Atlas(side, body.MetallicRoughnessImage, parts.Select(p => p.MetallicRoughnessImage), (0, DefaultRoughness, 0, 255))
            : null;
        DdsWriter.Write(Diffuse(colour, metalRough), CompressionFormat.Bc3, Path.Combine(folder, stem + "_d.dds"));
        if (body.NormalImage is null) return false;

        var normal = Atlas(side, body.NormalImage, parts.Select(p => p.NormalImage), (128, 128, 255, 255));
        DdsWriter.Write(Normal(normal), CompressionFormat.Bc3, Path.Combine(folder, stem + "_n.dds"));
        return true;
    }

    /// <summary>
    /// A texture <paramref name="side"/> high and twice as wide: <paramref name="body"/> in the left half, each part's image in its tile of the
    /// right half (<see cref="Attachments.PartUv"/>), <paramref name="fill"/> where an image is missing.
    /// </summary>
    static Rgba Atlas(int side, byte[]? body, IEnumerable<byte[]?> parts, (byte, byte, byte, byte) fill)
    {
        var atlas = new Rgba(2 * side, side, fill);
        if (body is not null) Blit(atlas, Rgba.Decode(body).Resized(side, side), 0, 0);

        int tile = 0;
        foreach (var part in parts)
        {
            if (part is not null) Blit(atlas, Rgba.Decode(part).Resized(side / 2, side / 2), side + tile % 2 * side / 2, tile / 2 * side / 2);
            tile++;
        }

        return atlas;
    }

    /// <summary>
    /// Copies <paramref name="image"/> into <paramref name="into"/> with its top left at (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    static void Blit(Rgba into, Rgba image, int x, int y)
    {
        for (int row = 0; row < image.Height; row++)
            Array.Copy(image.Pixels, row * image.Width * 4, into.Pixels, ((y + row) * into.Width + x) * 4, image.Width * 4);
    }

    /// <summary>
    /// Base colour with gloss in alpha: half of (255 - roughness), roughness from the green channel.
    /// </summary>
    static Rgba Diffuse(Rgba colour, Rgba? metalRough)
    {
        var result = new Rgba(colour.Width, colour.Height);
        for (int i = 0; i < colour.Pixels.Length; i += 4)
        {
            int roughness = metalRough?.Pixels[i + 1] ?? DefaultRoughness;
            result.Pixels[i] = colour.Pixels[i];
            result.Pixels[i + 1] = colour.Pixels[i + 1];
            result.Pixels[i + 2] = colour.Pixels[i + 2];
            result.Pixels[i + 3] = (byte)((255 - roughness) / 2);
        }

        return result;
    }

    /// <summary>
    /// The game's character normal layout: X in alpha, Y in green, flipped from glTF's +Y up; red and blue zero.
    /// </summary>
    static Rgba Normal(Rgba normal)
    {
        var result = new Rgba(normal.Width, normal.Height);
        for (int i = 0; i < normal.Pixels.Length; i += 4)
        {
            result.Pixels[i + 1] = (byte)(255 - normal.Pixels[i + 1]);
            result.Pixels[i + 3] = normal.Pixels[i];
        }

        return result;
    }
}
