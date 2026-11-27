using System.Globalization;
using System.Numerics;
using System.Text;
using System.Xml;

namespace StructureImport;

/// <summary>
/// Writes a building part's <c>xml collision</c>: a PhysX NxuStream2 file (engine docs/formats/collision.md) with one static actor holding a
/// triangle mesh as plain points and triangles (the game cooks it itself when it loads) or a box. PhysX is Z up: the part's (x, y, z) is written
/// as (x, -z, y), the inverse of the game's mapping (x, y, z) -> (x, z, -y).
/// </summary>
public static class CollisionWriter
{
    /// <summary>
    /// Writes a triangle-mesh collision of <paramref name="shape"/> (in the part's space) to <paramref name="path"/>.
    /// </summary>
    public static void WriteMesh(TriangleShape shape, string path) => Write(path, w =>
    {
        w.WriteStartElement("NxTriangleMeshDesc");
        w.WriteAttributeString("id", "Mesh_0");
        w.WriteStartElement("NxSimpleTriangleMesh");
        w.WriteElementString("numVertices", Number(shape.Points.Length));
        w.WriteElementString("numTriangles", Number(shape.Triangles.Length / 3));
        w.WriteElementString("pointStrideBytes", "12");
        w.WriteElementString("triangleStrideBytes", "12");
        w.WriteElementString("points", string.Join("  ", shape.Points.Select(p => Vector(ToPhysX(p)))));
        w.WriteElementString("triangles", string.Join("  ", shape.Triangles.Chunk(3).Select(t => string.Join(' ', t.Select(Number)))));
        w.WriteElementString("flags", "0");
        w.WriteEndElement();
        w.WriteElementString("cookedDataSize", "0");
        w.WriteElementString("cookedData", "");
        w.WriteEndElement();
    }, w =>
    {
        w.WriteStartElement("NxTriangleMeshShapeDesc");
        w.WriteAttributeString("meshData", "Mesh_0");
    });

    /// <summary>
    /// Writes a box collision filling <paramref name="min"/> .. <paramref name="max"/> (in the part's space) to <paramref name="path"/>.
    /// </summary>
    public static void WriteBox(Vector3 min, Vector3 max, string path) => Write(path, _ => { }, w =>
    {
        var half = (max - min) / 2;
        var centre = ToPhysX((min + max) / 2);
        w.WriteStartElement("NxBoxShapeDesc");
        w.WriteAttributeString("dimensions", Vector(new Vector3(half.X, half.Z, half.Y)));
        w.WriteStartElement("NxShapeDesc");
        w.WriteAttributeString("name", "box");
        w.WriteElementString("localPose", "1 0 0  0 1 0  0 0 1  " + Vector(centre));
        w.WriteElementString("group", "0");
        w.WriteEndElement();
    });

    /// <summary>
    /// The file: collection, the mesh descriptions <paramref name="meshes"/> writes, then a scene with one static actor whose shape element
    /// <paramref name="startShape"/> opens (and may fill); the shape is closed here.
    /// </summary>
    static void Write(string path, Action<XmlWriter> meshes, Action<XmlWriter> startShape)
    {
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) };
        using var w = XmlWriter.Create(path, settings);
        w.WriteStartDocument();
        w.WriteStartElement("NXUSTREAM2");
        w.WriteStartElement("NxuPhysicsCollection");
        w.WriteAttributeString("id", Path.GetFileName(path));
        w.WriteAttributeString("sdkVersion", "284");
        w.WriteAttributeString("nxuVersion", "103");
        meshes(w);
        w.WriteStartElement("NxSceneDesc");
        w.WriteAttributeString("id", "collision");
        w.WriteAttributeString("hasFilter", "false");
        w.WriteAttributeString("hasLimits", "false");
        w.WriteAttributeString("hasMaxBounds", "false");
        w.WriteStartElement("NxActorDesc");
        w.WriteAttributeString("id", "Actor_0");
        w.WriteAttributeString("hasBody", "false");
        w.WriteAttributeString("name", Path.GetFileNameWithoutExtension(path));
        w.WriteElementString("globalPose", "1 0 0  0 1 0  0 0 1  0 0 0");
        startShape(w);
        w.WriteEndElement();
        w.WriteEndDocument();
    }

    /// <summary>
    /// A point of the part (Y up) in PhysX's Z-up space.
    /// </summary>
    public static Vector3 ToPhysX(Vector3 p) => new(p.X, -p.Z, p.Y);

    /// <summary>
    /// Three invariant numbers.
    /// </summary>
    static string Vector(Vector3 v) => $"{Number(v.X)} {Number(v.Y)} {Number(v.Z)}";

    /// <summary>
    /// An invariant number, shortest round-trip form.
    /// </summary>
    static string Number(float value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// An invariant integer.
    /// </summary>
    static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
