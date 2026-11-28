using System.Numerics;
using StructureImport;

namespace RaceImport;

/// <summary>
/// What a triangle of the model shows, read from its base colour texture (docs/race-import.md "Skin tone").
/// </summary>
public enum Texel
{
    /// <summary>
    /// Clothing, hooves, anything else.
    /// </summary>
    Other,

    /// <summary>
    /// Bare skin (coloured: its texels' chroma is high).
    /// </summary>
    Skin,

    /// <summary>
    /// The model's own hair (grey to white: low chroma, light).
    /// </summary>
    Hair,
}

/// <summary>
/// Sorts a model's triangles by what their texels show: each triangle's texels in uv, averaged, are skin when coloured (mean chroma, the largest
/// channel less the smallest, at least <see cref="SkinChroma"/>), else hair when light (mean largest channel at least <see cref="HairValue"/>),
/// else other. Per triangle, as a Meshy unwrap gives most triangles a chart of their own.
/// </summary>
public static class TextureClasses
{
    /// <summary>
    /// Side of the image the triangles are read at, in pixels.
    /// </summary>
    const int Side = 512;

    /// <summary>
    /// The least mean chroma (0 to 1) of a skin triangle. **Observed** (tiefling-female.glb): skin and horns 0.10 to 0.13, the top and shorts
    /// 0.03 to 0.06, the hair below 0.03.
    /// </summary>
    const float SkinChroma = 0.07f;

    /// <summary>
    /// The least mean largest channel (0 to 1) of a hair triangle. **Observed** (tiefling-female.glb): the hair 0.45 to 0.8, the clothing 0.15 to
    /// 0.35.
    /// </summary>
    const float HairValue = 0.4f;

    /// <summary>
    /// How many rounds <see cref="Smoothed"/> runs.
    /// </summary>
    const int SmoothRounds = 2;

    /// <summary>
    /// Each triangle's class (by triangle, <c>indices.Length / 3</c>), smoothed over the surface (<see cref="Smoothed"/>).
    /// </summary>
    public static Texel[] Classify(byte[] baseColourImage, Vector3[] positions, Vector2[] uvs, int[] indices) =>
        Smoothed(Classify(baseColourImage, uvs, indices), positions, indices);

    /// <summary>
    /// <paramref name="classes"/> with, for <see cref="SmoothRounds"/> rounds, each triangle that is skin with at least two of its three
    /// neighbours across an edge not, or not skin with at least two of them skin, turned to the neighbours' kind (a lone dirty-brown cloth
    /// triangle among cloth, a lone dark skin triangle among skin). **Observed** (tieflingmale-rigged.glb): the loincloth's chroma runs to 0.16
    /// at its 99th percentile and the red skin's down to 0.06 at its 1st.
    /// </summary>
    static Texel[] Smoothed(Texel[] classes, Vector3[] positions, int[] indices)
    {
        var weld = Surface.Weld(positions);
        var byEdge = new Dictionary<(int, int), List<int>>();
        for (int t = 0; t < classes.Length; t++)
            foreach (var edge in Edges(weld, indices, t))
                (byEdge.TryGetValue(edge, out var list) ? list : byEdge[edge] = []).Add(t);

        for (int round = 0; round < SmoothRounds; round++)
        {
            var next = (Texel[])classes.Clone();
            for (int t = 0; t < classes.Length; t++)
            {
                if (classes[t] == Texel.Hair) continue;

                var neighbours = Edges(weld, indices, t).SelectMany(e => byEdge[e]).Where(n => n != t).Distinct().ToList();
                int skin = neighbours.Count(n => classes[n] == Texel.Skin);
                int other = neighbours.Count(n => classes[n] == Texel.Other);
                if (classes[t] == Texel.Skin && other >= 2 && other > skin) next[t] = Texel.Other;
                else if (classes[t] == Texel.Other && skin >= 2 && skin > other) next[t] = Texel.Skin;
            }

            classes = next;
        }

        return classes;
    }

    /// <summary>
    /// A triangle's three edges as pairs of welded vertices, the lower first.
    /// </summary>
    static IEnumerable<(int, int)> Edges(int[] weld, int[] indices, int t)
    {
        for (int k = 0; k < 3; k++)
        {
            int a = weld[indices[3 * t + k]], b = weld[indices[3 * t + (k + 1) % 3]];
            yield return (Math.Min(a, b), Math.Max(a, b));
        }
    }

    /// <summary>
    /// Each triangle's class from its texels alone.
    /// </summary>
    static Texel[] Classify(byte[] baseColourImage, Vector2[] uvs, int[] indices)
    {
        var image = Rgba.Decode(baseColourImage).Resized(Side, Side);
        var result = new Texel[indices.Length / 3];
        for (int t = 0; t < result.Length; t++)
        {
            var (value, chroma) = Mean(image, uvs[indices[3 * t]], uvs[indices[3 * t + 1]], uvs[indices[3 * t + 2]]);
            result[t] = chroma >= SkinChroma ? Texel.Skin : value >= HairValue ? Texel.Hair : Texel.Other;
        }

        return result;
    }

    /// <summary>
    /// The mean largest channel and chroma (0 to 1) of the texels a uv triangle covers (its middle's texel when it covers none).
    /// </summary>
    static (float Value, float Chroma) Mean(Rgba image, Vector2 a, Vector2 b, Vector2 c)
    {
        float value = 0, chroma = 0;
        int count = 0;
        Fill(image.Width, a, b, c, (x, y) =>
        {
            var (v, ch) = Sample(image, x, y);
            value += v;
            chroma += ch;
            count++;
        });
        if (count > 0) return (value / count, chroma / count);

        var middle = (a + b + c) / 3;
        return Sample(image, Pixel(middle.X, image.Width), Pixel(middle.Y, image.Height));
    }

    /// <summary>
    /// One texel's largest channel and chroma (0 to 1).
    /// </summary>
    static (float Value, float Chroma) Sample(Rgba image, int x, int y)
    {
        int i = 4 * (y * image.Width + x);
        int r = image.Pixels[i], g = image.Pixels[i + 1], b = image.Pixels[i + 2];
        int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        return (max / 255f, (max - min) / 255f);
    }

    /// <summary>
    /// Calls <paramref name="texel"/> for each texel of a <paramref name="side"/>-square image whose middle lies in the uv triangle abc (uv 0
    /// to 1 across the image, wrapped into it).
    /// </summary>
    public static void Fill(int side, Vector2 a, Vector2 b, Vector2 c, Action<int, int> texel)
    {
        var offset = new Vector2(MathF.Floor(Math.Min(a.X, Math.Min(b.X, c.X))), MathF.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y))));
        Vector2 pa = (a - offset) * side, pb = (b - offset) * side, pc = (c - offset) * side;
        float area = Cross(pb - pa, pc - pa);
        if (MathF.Abs(area) < 1e-9f) return;

        int x0 = (int)MathF.Floor(Math.Min(pa.X, Math.Min(pb.X, pc.X))), x1 = (int)MathF.Ceiling(Math.Max(pa.X, Math.Max(pb.X, pc.X)));
        int y0 = (int)MathF.Floor(Math.Min(pa.Y, Math.Min(pb.Y, pc.Y))), y1 = (int)MathF.Ceiling(Math.Max(pa.Y, Math.Max(pb.Y, pc.Y)));
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float u = Cross(pc - pb, p - pb) / area, v = Cross(pa - pc, p - pc) / area, w = Cross(pb - pa, p - pa) / area;
                if (u >= 0 && v >= 0 && w >= 0) texel(Wrap(x, side), Wrap(y, side));
            }
    }

    /// <summary>
    /// The 2D cross product.
    /// </summary>
    static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>
    /// A pixel index wrapped into 0 to <paramref name="side"/> - 1.
    /// </summary>
    static int Wrap(int i, int side) => ((i % side) + side) % side;

    /// <summary>
    /// The pixel a texture coordinate falls in.
    /// </summary>
    static int Pixel(float t, int side) => Wrap((int)MathF.Floor(t * side), side);
}
