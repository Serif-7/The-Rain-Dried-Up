using System.Numerics;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;

namespace StructureImport;

/// <summary>
/// The three textures of a MATERIAL_SPEC, in the channels the game's building shader (<c>objects.hlsl</c>) reads (docs/structures.md "Textures"):
/// diffuse RGB colour and A gloss; normal RGB tangent-space normal (green along increasing V) and A the cut-out mask; metalness in R.
/// </summary>
public sealed record BakedTextures(Rgba Diffuse, Rgba Normal, Rgba Metal, bool CutOut);

/// <summary>
/// Converts a glTF material's images into the game's texture layout and writes them as compressed DDS files.
/// </summary>
public static class TextureBaker
{
    /// <summary>
    /// The textures for <paramref name="look"/>, none larger than <paramref name="maxSide"/> pixels a side.
    /// </summary>
    public static BakedTextures Bake(SurfaceLook look, int maxSide)
    {
        var baseColour = Decode(look.BaseColourImage);
        var metalRough = Decode(look.MetallicRoughnessImage);
        var normal = Decode(look.NormalImage);

        var diffuse = Diffuse(look, baseColour, metalRough, maxSide);
        var normalOut = Normal(look, normal, baseColour, maxSide);
        var metal = Metal(look, metalRough, maxSide);
        return new BakedTextures(diffuse, normalOut, metal, look.AlphaCutoff is not null);
    }

    /// <summary>
    /// Writes the three textures as <c>&lt;stem&gt;_d.dds</c> (BC3), <c>_n.dds</c> (BC1, or BC3 when cut out) and <c>_m.dds</c> (BC1) in
    /// <paramref name="folder"/>; returns their file names.
    /// </summary>
    public static (string Diffuse, string Normal, string Metal) Write(BakedTextures textures, string folder, string stem)
    {
        string diffuse = stem + "_d.dds", normal = stem + "_n.dds", metal = stem + "_m.dds";
        DdsWriter.Write(textures.Diffuse, CompressionFormat.Bc3, Path.Combine(folder, diffuse));
        DdsWriter.Write(textures.Normal, textures.CutOut ? CompressionFormat.Bc3 : CompressionFormat.Bc1, Path.Combine(folder, normal));
        DdsWriter.Write(textures.Metal, CompressionFormat.Bc1, Path.Combine(folder, metal));
        return (diffuse, normal, metal);
    }

    /// <summary>
    /// Diffuse: the base colour times its factor; alpha the gloss, 1 - roughness (the shader's <c>diffuse.a</c>, times <c>specular mult</c>).
    /// </summary>
    static Rgba Diffuse(SurfaceLook look, Rgba? baseColour, Rgba? metalRough, int maxSide)
    {
        var (w, h) = Size(maxSide, baseColour, metalRough);
        var colour = baseColour?.Resized(w, h);
        var rough = metalRough?.Resized(w, h);
        var tint = LinearToSrgb(look.BaseColour);
        var result = new Rgba(w, h);
        for (int i = 0; i < result.Pixels.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
                result.Pixels[i + c] = Scale(colour?.Pixels[i + c] ?? 255, tint[c]);

            float roughness = (rough?.Pixels[i + 1] ?? 255) / 255f * look.Roughness;
            result.Pixels[i + 3] = ToByte(1 - roughness);
        }

        return result;
    }

    /// <summary>
    /// Normal: the glTF normal map with green turned round (glTF's green points up the image, the game's along increasing V, down it), flat
    /// without one; alpha the base colour's alpha when the material is cut out, else opaque.
    /// </summary>
    static Rgba Normal(SurfaceLook look, Rgba? normal, Rgba? baseColour, int maxSide)
    {
        bool cut = look.AlphaCutoff is not null;
        var (w, h) = Size(maxSide, normal, cut ? baseColour : null);
        var map = normal?.Resized(w, h);
        var mask = cut ? baseColour?.Resized(w, h) : null;
        var result = new Rgba(w, h);
        for (int i = 0; i < result.Pixels.Length; i += 4)
        {
            result.Pixels[i] = map?.Pixels[i] ?? 128;
            result.Pixels[i + 1] = (byte)(255 - (map?.Pixels[i + 1] ?? 127));
            result.Pixels[i + 2] = map?.Pixels[i + 2] ?? 255;
            result.Pixels[i + 3] = cut ? Scale(mask?.Pixels[i + 3] ?? 255, look.BaseColour.W) : (byte)255;
        }

        return result;
    }

    /// <summary>
    /// Metalness: the metallic-roughness image's blue times the metallic factor, in red (the shader reads <c>.r</c>), grey in G and B.
    /// </summary>
    static Rgba Metal(SurfaceLook look, Rgba? metalRough, int maxSide)
    {
        var (w, h) = Size(maxSide, metalRough);
        var source = metalRough?.Resized(w, h);
        var result = new Rgba(w, h);
        for (int i = 0; i < result.Pixels.Length; i += 4)
        {
            byte metal = Scale(source?.Pixels[i + 2] ?? 255, look.Metallic);
            result.Pixels[i] = result.Pixels[i + 1] = result.Pixels[i + 2] = metal;
            result.Pixels[i + 3] = 255;
        }

        return result;
    }

    /// <summary>
    /// The output size: the largest of the given images, each side a power of two no larger than <paramref name="maxSide"/>; 4 × 4 with none.
    /// </summary>
    static (int W, int H) Size(int maxSide, params Rgba?[] images)
    {
        int w = images.Max(i => i?.Width ?? 4), h = images.Max(i => i?.Height ?? 4);
        return (Rgba.TextureSide(w, maxSide), Rgba.TextureSide(h, maxSide));
    }

    /// <summary>
    /// The decoded image, null for none.
    /// </summary>
    static Rgba? Decode(byte[]? encoded) => encoded is null ? null : Rgba.Decode(encoded);

    /// <summary>
    /// A linear glTF factor in the sRGB space of the colour texture it multiplies.
    /// </summary>
    static float[] LinearToSrgb(Vector4 linear) => [Srgb(linear.X), Srgb(linear.Y), Srgb(linear.Z)];

    /// <summary>
    /// One channel from linear to sRGB.
    /// </summary>
    static float Srgb(float c) => c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;

    /// <summary>
    /// A byte scaled by a factor.
    /// </summary>
    static byte Scale(byte value, float factor) => ToByte(value / 255f * factor);

    /// <summary>
    /// A 0..1 value as a rounded byte.
    /// </summary>
    static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255), 0, 255);
}
