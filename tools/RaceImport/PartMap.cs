using System.IO.Compression;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using StructureImport;

namespace RaceImport;

/// <summary>
/// A body's part map (engine docs/characters.md "Hidden body parts"): a PNG in the body's own uv layout whose colour at each vertex's uv names
/// the body part the vertex belongs to, so worn items' <c>hide parts</c> stop drawing the skin under them. Each fitted vertex takes the part of
/// the nearest human body vertex carried by the same bone, read from the game's human part map and grown by <see cref="Grow"/> (a bulkier body
/// clips over a wider band than the human's; docs/race-import.md "Part map").
/// </summary>
public static class PartMap
{
    /// <summary>
    /// Side of the written map in pixels: large enough that neighbouring vertices of different parts rarely share a texel.
    /// </summary>
    const int Side = 1024;

    /// <summary>
    /// How far each human part reaches beyond its own vertices, in game units (0.1 m): a human vertex without a part takes the part of the nearest
    /// vertex with one within this distance.
    /// </summary>
    const float Grow = 0.8f;

    /// <summary>
    /// The colours the game reads as parts; any other texel is no part.
    /// </summary>
    static readonly HashSet<uint> PartColours = [0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF, 0xFF8000, 0x8000FF, 0x008080];

    /// <summary>
    /// The part of wing membrane (<see cref="WingRig"/>): the human's upper-arm bands (white), which every sleeved item hides (jackets, coats,
    /// robes, plate jackets, skin suits; docs/race-import.md "Part map").
    /// </summary>
    const uint Membrane = 0xFFFFFF;

    /// <summary>
    /// Writes <paramref name="path"/> for a body of the given fitted positions, uvs and heaviest bones, with the vertices
    /// <paramref name="membrane"/> marks in <see cref="Membrane"/> and those <paramref name="unhidden"/> marks in none (attached parts); twice as
    /// wide when <paramref name="wide"/> (a body texture with parts beside it, <see cref="Attachments"/>). Returns how many vertices got a part.
    /// </summary>
    public static int Write(string path, Vector3[] positions, Vector2[] uvs, ushort[] bones, HumanBody human, Rgba humanMap, bool[] membrane,
        bool[]? unhidden = null, bool wide = false)
    {
        var map = new Rgba(wide ? 2 * Side : Side, Side);
        var parts = Grown(human, humanMap);
        var byBone = Enumerable.Range(0, human.Positions.Length).GroupBy(v => human.Bones[v]).ToDictionary(g => g.Key, g => g.ToArray());
        var all = Enumerable.Range(0, human.Positions.Length).ToArray();
        int marked = 0;
        for (int v = 0; v < positions.Length; v++)
        {
            if (uvs[v].Y < 0) continue; // the grafted human head's, coloured from the head texture
            if (unhidden?[v] == true) continue;

            int nearest = Nearest(positions[v], byBone.GetValueOrDefault(bones[v]) ?? all, human.Positions);
            uint? part = membrane[v] ? Membrane : parts[nearest];
            if (part is not { } colour) continue;

            Set(map, uvs[v], colour);
            marked++;
        }

        WritePng(path, map);
        return marked;
    }

    /// <summary>
    /// Each human vertex's part: its own from the map, else that of the nearest vertex with one, carried by the same bone, within
    /// <see cref="Grow"/> (a hanging hand or forearm beside the hip band takes none of it).
    /// </summary>
    static uint?[] Grown(HumanBody human, Rgba humanMap)
    {
        var own = human.Uvs.Select(uv => Colour(humanMap, uv)).ToArray();
        var marked = Enumerable.Range(0, own.Length)
            .Where(i => own[i] is not null)
            .GroupBy(i => human.Bones[i])
            .ToDictionary(g => g.Key, g => g.ToArray());
        var grown = new uint?[own.Length];
        for (int v = 0; v < own.Length; v++)
        {
            if (own[v] is not null || !marked.TryGetValue(human.Bones[v], out var sameBone))
            {
                grown[v] = own[v];
                continue;
            }

            int nearest = Nearest(human.Positions[v], sameBone, human.Positions);
            if (Vector3.Distance(human.Positions[v], human.Positions[nearest]) <= Grow) grown[v] = own[nearest];
        }

        return grown;
    }

    /// <summary>
    /// The candidate human vertex nearest <paramref name="point"/>.
    /// </summary>
    static int Nearest(Vector3 point, int[] candidates, Vector3[] positions) => candidates.MinBy(i => Vector3.DistanceSquared(point, positions[i]));

    /// <summary>
    /// The part colour at a uv of a map, sampled as the game samples it (the texel at the truncated uv, clamped); null when the texel is no part.
    /// </summary>
    static uint? Colour(Rgba map, Vector2 uv)
    {
        int i = Texel(map, uv);
        if (map.Pixels[i + 3] == 0) return null;

        uint rgb = (uint)(map.Pixels[i] << 16 | map.Pixels[i + 1] << 8 | map.Pixels[i + 2]);
        return PartColours.Contains(rgb) ? rgb : null;
    }

    /// <summary>
    /// Paints the texel at a uv opaque in a part colour.
    /// </summary>
    static void Set(Rgba map, Vector2 uv, uint rgb)
    {
        int i = Texel(map, uv);
        map.Pixels[i] = (byte)(rgb >> 16);
        map.Pixels[i + 1] = (byte)(rgb >> 8);
        map.Pixels[i + 2] = (byte)rgb;
        map.Pixels[i + 3] = 255;
    }

    /// <summary>
    /// The byte offset of the texel the game samples for a uv.
    /// </summary>
    static int Texel(Rgba map, Vector2 uv)
    {
        int x = Math.Clamp((int)(map.Width * uv.X), 0, map.Width - 1);
        int y = Math.Clamp((int)(map.Height * uv.Y), 0, map.Height - 1);
        return (y * map.Width + x) * 4;
    }

    #region Png

    /// <summary>
    /// An 8-bit RGBA PNG: signature, header, one zlib-compressed data chunk, end.
    /// </summary>
    static void WritePng(string path, Rgba image) => File.WriteAllBytes(path, Png(image));

    /// <summary>
    /// <paramref name="image"/> encoded as an 8-bit RGBA PNG (<see cref="WritePng"/>).
    /// </summary>
    internal static byte[] Png(Rgba image)
    {
        using var stream = new MemoryStream();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, image.Width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), image.Height);
        header[8] = 8; // bit depth
        header[9] = 6; // RGBA
        WriteChunk(stream, "IHDR", header);
        WriteChunk(stream, "IDAT", Compressed(image));
        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    /// <summary>
    /// The rows, each led by filter 0 (none), zlib-compressed.
    /// </summary>
    static byte[] Compressed(Rgba image)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            int row = image.Width * 4;
            for (int y = 0; y < image.Height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(image.Pixels, y * row, row);
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// A chunk: length, type, data, CRC of type and data.
    /// </summary>
    static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, data.Length);
        stream.Write(lengthBytes);

        byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
        stream.Write(typed);

        var crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, Crc(typed));
        stream.Write(crcBytes);
    }

    /// <summary>
    /// The PNG CRC-32 (polynomial 0xEDB88320).
    /// </summary>
    static uint Crc(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
        }

        return ~crc;
    }

    #endregion
}
