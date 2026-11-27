using System.Numerics;
using Meitou.Data.Ogre;

namespace RaceImport;

/// <summary>
/// The game's own human body of a rig (<see cref="HumanRig.Body"/>, bound to the same skeleton) as the reference a fitted body is measured against: each
/// vertex's bind position, uv and heaviest bone. The game's clothing is modelled around it.
/// </summary>
public sealed class HumanBody
{
    /// <summary>
    /// Bind positions.
    /// </summary>
    public required Vector3[] Positions { get; init; }

    /// <summary>
    /// Texture coordinates.
    /// </summary>
    public required Vector2[] Uvs { get; init; }

    /// <summary>
    /// Each vertex's heaviest bone handle.
    /// </summary>
    public required ushort[] Bones { get; init; }

    /// <summary>
    /// Each vertex's bone weights by handle.
    /// </summary>
    public required Dictionary<ushort, float>[] Weights { get; init; }

    /// <summary>
    /// The skinned vertices' triangles, three indices each.
    /// </summary>
    public required int[] Indices { get; init; }

    /// <summary>
    /// The body of <paramref name="rig"/> in the game in <paramref name="kenshiFolder"/>. Throws when it is missing or has no skinned geometry.
    /// </summary>
    public static HumanBody Load(string kenshiFolder, HumanRig rig)
    {
        var mesh = OgreMeshReader.Read(File.ReadAllBytes(KenshiSkeleton.GameFile(kenshiFolder, rig.Body)));
        var positions = new List<Vector3>();
        var uvs = new List<Vector2>();
        var weights = new List<Dictionary<ushort, float>>();
        var indices = new List<int>();
        var added = new Dictionary<OgreVertexData, Dictionary<uint, int>>();
        foreach (var sub in mesh.SubMeshes) AddSubMesh(mesh, sub, positions, uvs, weights, added, indices);

        if (positions.Count == 0) throw new InvalidDataException($"{rig.Body}: no vertices");

        return new HumanBody
        {
            Positions = [.. positions],
            Uvs = [.. uvs],
            Bones = [.. weights.Select(w => w.MaxBy(p => p.Value).Key)],
            Weights = [.. weights],
            Indices = [.. indices],
        };
    }

    /// <summary>
    /// Adds a submesh's skinned vertices with their bone weights (a vertex buffer shared by submeshes is read once) and its triangles whose
    /// three vertices are skinned.
    /// </summary>
    static void AddSubMesh(OgreMesh mesh, OgreSubMesh sub, List<Vector3> positions, List<Vector2> uvs, List<Dictionary<ushort, float>> weights,
        Dictionary<OgreVertexData, Dictionary<uint, int>> added, List<int> indices)
    {
        var data = sub.UseSharedVertices ? mesh.SharedVertexData : sub.VertexData;
        if (data is null) return;

        if (!added.TryGetValue(data, out var map))
        {
            var assignments = sub.UseSharedVertices ? mesh.BoneAssignments : sub.BoneAssignments;
            added[data] = map = AddVertices(data, assignments, positions, uvs, weights);
        }

        var source = sub.Indices.Indices;
        for (int i = 0; i + 2 < source.Length; i += 3)
        {
            if (!map.TryGetValue(source[i], out int a) || !map.TryGetValue(source[i + 1], out int b) || !map.TryGetValue(source[i + 2], out int c)) continue;

            indices.Add(a);
            indices.Add(b);
            indices.Add(c);
        }
    }

    /// <summary>
    /// Adds a vertex buffer's skinned vertices; returns each added vertex's new index by its index in the buffer.
    /// </summary>
    static Dictionary<uint, int> AddVertices(OgreVertexData data, List<OgreBoneAssignment> assignments, List<Vector3> positions, List<Vector2> uvs,
        List<Dictionary<ushort, float>> weights)
    {
        var byVertex = assignments.GroupBy(a => a.VertexIndex).ToDictionary(g => g.Key, g => g.GroupBy(a => a.BoneIndex).ToDictionary(b => b.Key, b => b.Sum(a => a.Weight)));
        var points = data.ReadPositions() ?? [];
        var uvElement = data.Find(OgreVertexSemantic.TextureCoordinates);
        var uvFloats = uvElement is null ? null : data.ReadFloats(uvElement);
        int stride = uvElement?.Type.ComponentCount() ?? 0;
        var map = new Dictionary<uint, int>();
        for (uint v = 0; v < points.Length; v++)
        {
            if (!byVertex.TryGetValue(v, out var own)) continue;

            map[v] = positions.Count;
            positions.Add(points[v]);
            uvs.Add(uvFloats is null ? Vector2.Zero : new Vector2(uvFloats[v * stride], uvFloats[v * stride + 1]));
            weights.Add(own);
        }

        return map;
    }
}
