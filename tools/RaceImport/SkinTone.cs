using System.Numerics;
using BCnEncoder.Shared;
using StructureImport;

namespace RaceImport;

/// <summary>
/// Makes a body take the character editor's skin tone like the human head it wears (docs/race-import.md "Skin tone"). The game's body shader
/// multiplies the colour texture by the <c>Skin Tone</c> colour where the race's body mask (red) and the HEAD's mask say skin (engine
/// docs/characters.md "Shading, poses and LOD"). So the model's skin texels are repainted to the Greenlander head's bare skin colour
/// (<see cref="HumanSkin"/>), keeping their shading, and the mask marks them: one slider then tints head and body alike.
/// </summary>
public static class SkinTone
{
    /// <summary>
    /// The Greenlander heads' bare skin, the mean of their <c>texture map</c> where their <c>mask map</c> is skin (sRGB bytes). **Observed**
    /// (measured over the game's Greenlander heads, docs/race-import.md "Skin tone").
    /// </summary>
    public static readonly Vector3 HumanSkin = new(204, 143, 112);

    /// <summary>
    /// How far each texel of skin is grown into its neighbours, in pixels at <see cref="BodyImporter.TextureSide"/>, so that filtering and
    /// mipmaps at a chart's edge read skin, not the gutter.
    /// </summary>
    const int Grow = 4;

    /// <summary>
    /// Side of the written mask in pixels.
    /// </summary>
    const int MaskSide = 1024;

    #region Repaint

    /// <summary>
    /// <paramref name="body"/> with the texels of its <see cref="Texel.Skin"/> triangles repainted to <see cref="HumanSkin"/> times their
    /// brightness against the skin's median brightness, at <paramref name="side"/> pixels square.
    /// </summary>
    public static SourceBody Repainted(SourceBody body, Texel[] classes, int side)
    {
        var image = Rgba.Decode(body.BaseColourImage ?? throw new InvalidDataException("the model has no base colour texture")).Resized(side, side);
        var skin = Grown(Covered(side, body.Uvs, body.Indices, classes), side, Grow);
        float reference = Median(image, skin);
        for (int i = 0; i < skin.Length; i++)
            if (skin[i]) Repaint(image.Pixels, 4 * i, reference);

        return new SourceBody
        {
            Positions = body.Positions,
            Normals = body.Normals,
            Uvs = body.Uvs,
            Indices = body.Indices,
            Influences = body.Influences,
            JointNames = body.JointNames,
            JointParents = body.JointParents,
            JointPositions = body.JointPositions,
            BaseColourImage = PartMap.Png(image),
            MetallicRoughnessImage = body.MetallicRoughnessImage,
            NormalImage = body.NormalImage,
        };
    }

    /// <summary>
    /// The brightest a repainted texel gets, as a share of <see cref="HumanSkin"/>: its red at 255, so highlights (a model's lighter scales)
    /// keep the skin's hue instead of clipping towards yellow.
    /// </summary>
    static readonly float MaxShare = 255 / HumanSkin.X;

    /// <summary>
    /// One texel repainted: <see cref="HumanSkin"/> times its brightness over <paramref name="reference"/>, at most <see cref="MaxShare"/>.
    /// </summary>
    static void Repaint(byte[] pixels, int i, float reference)
    {
        float share = Math.Min(Brightness(pixels, i) / reference, MaxShare);
        pixels[i] = Byte(HumanSkin.X * share);
        pixels[i + 1] = Byte(HumanSkin.Y * share);
        pixels[i + 2] = Byte(HumanSkin.Z * share);
    }

    /// <summary>
    /// The median brightness of the skin texels (1 without any).
    /// </summary>
    static float Median(Rgba image, bool[] skin)
    {
        var values = Enumerable.Range(0, skin.Length).Where(i => skin[i]).Select(i => Brightness(image.Pixels, 4 * i)).Order().ToArray();
        return values.Length == 0 ? 1 : Math.Max(values[values.Length / 2], 1);
    }

    /// <summary>
    /// A texel's luma (Rec. 601) in bytes.
    /// </summary>
    static float Brightness(byte[] pixels, int i) => 0.299f * pixels[i] + 0.587f * pixels[i + 1] + 0.114f * pixels[i + 2];

    /// <summary>
    /// A value clamped to a byte.
    /// </summary>
    static byte Byte(float value) => (byte)Math.Clamp(MathF.Round(value), 0, 255);

    #endregion

    #region Mask

    /// <summary>
    /// Writes the body mask <c>&lt;stem&gt;_mask.dds</c>: white where <paramref name="mesh"/>'s triangles that are the model's
    /// <see cref="Texel.Skin"/> triangles (by their vertices' <paramref name="source"/>, -1 for vertices not the model's), or the skin patched
    /// under its hair (<see cref="HairHolePatch.Patched"/>), lie in uv 0 to 1 (the
    /// body texture; the head's uvs below 0 take the HEAD's own mask), grown a texel, black elsewhere. Returns how many triangles it covers.
    /// </summary>
    public static int WriteMask(string folder, string stem, SkinnedMesh mesh, int[] source, int[] modelIndices, Texel[] modelClasses)
    {
        var skinTriangles = new HashSet<(int, int, int)>();
        for (int t = 0; t < modelClasses.Length; t++)
            if (modelClasses[t] == Texel.Skin) skinTriangles.Add(Key(modelIndices[3 * t], modelIndices[3 * t + 1], modelIndices[3 * t + 2]));

        var classes = new Texel[mesh.Indices.Length / 3];
        for (int t = 0; t < classes.Length; t++)
        {
            int a = mesh.Indices[3 * t], b = mesh.Indices[3 * t + 1], c = mesh.Indices[3 * t + 2];
            bool body = mesh.Uvs[a].Y >= 0 && mesh.Uvs[b].Y >= 0 && mesh.Uvs[c].Y >= 0;
            bool patched = source[a] == HairHolePatch.Patched && source[b] == HairHolePatch.Patched && source[c] == HairHolePatch.Patched;
            classes[t] = body && (patched || skinTriangles.Contains(Key(source[a], source[b], source[c]))) ? Texel.Skin : Texel.Other;
        }

        var covered = Grown(Covered(MaskSide, mesh.Uvs, mesh.Indices, classes), MaskSide, 1);
        var image = new Rgba(MaskSide, MaskSide, (0, 0, 0, 255));
        for (int i = 0; i < covered.Length; i++)
            if (covered[i]) (image.Pixels[4 * i], image.Pixels[4 * i + 1], image.Pixels[4 * i + 2]) = ((byte)255, (byte)255, (byte)255);

        DdsWriter.Write(image, CompressionFormat.Bc1, Path.Combine(folder, stem + "_mask.dds"));
        return classes.Count(c => c == Texel.Skin);
    }

    /// <summary>
    /// A triangle's three vertices in rising order.
    /// </summary>
    static (int, int, int) Key(int a, int b, int c)
    {
        int[] sorted = [a, b, c];
        Array.Sort(sorted);
        return (sorted[0], sorted[1], sorted[2]);
    }

    #endregion

    #region Coverage

    /// <summary>
    /// The texels of a <paramref name="side"/>-square image that the <see cref="Texel.Skin"/> triangles cover.
    /// </summary>
    static bool[] Covered(int side, Vector2[] uvs, int[] indices, Texel[] classes)
    {
        var covered = new bool[side * side];
        for (int t = 0; t < classes.Length; t++)
        {
            if (classes[t] != Texel.Skin) continue;

            TextureClasses.Fill(side, uvs[indices[3 * t]], uvs[indices[3 * t + 1]], uvs[indices[3 * t + 2]], (x, y) => covered[y * side + x] = true);
        }

        return covered;
    }

    /// <summary>
    /// <paramref name="covered"/> grown by <paramref name="steps"/> texels (each step adds the four neighbours).
    /// </summary>
    static bool[] Grown(bool[] covered, int side, int steps)
    {
        for (int s = 0; s < steps; s++)
        {
            var next = (bool[])covered.Clone();
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                {
                    if (!covered[y * side + x]) continue;

                    if (x > 0) next[y * side + x - 1] = true;
                    if (x < side - 1) next[y * side + x + 1] = true;
                    if (y > 0) next[(y - 1) * side + x] = true;
                    if (y < side - 1) next[(y + 1) * side + x] = true;
                }

            covered = next;
        }

        return covered;
    }

    #endregion
}
