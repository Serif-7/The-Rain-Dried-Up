using System.Numerics; using Meitou.Data.Ogre;
// Poser <mesh> <skeleton> <anim skeleton|-> <clip|-> <time 0..1> <out.obj>
var mesh = OgreMeshReader.Read(File.ReadAllBytes(args[0]));
var skel = OgreSkeletonReader.Read(File.ReadAllBytes(args[1]));
var sub = mesh.SubMeshes[0]; var vd = sub.VertexData ?? mesh.SharedVertexData!;
var pos = vd.ReadPositions()!;
var uvEl = vd.Find(OgreVertexSemantic.TextureCoordinates); var uv = uvEl is null ? null : vd.ReadFloats(uvEl);
int n = skel.Bones.Max(b => b.Handle) + 1;
var bind = new Matrix4x4[n]; var anim = new Matrix4x4[n];
var byHandle = skel.Bones.ToDictionary(b => b.Handle);
var lp = new Dictionary<ushort,(Vector3 p, Quaternion q)>();
foreach (var b in skel.Bones) lp[b.Handle] = (Vector3.Zero, Quaternion.Identity);
if (args[2] != "-") {
    var a = OgreSkeletonReader.Read(File.ReadAllBytes(args[2]));
    var clip = a.Animations.First(c => c.Name == args[3]);
    float t = float.Parse(args[4]) * clip.Length;
    var names = a.Bones.ToDictionary(b => b.Handle, b => b.Name);
    var mine = skel.Bones.ToDictionary(b => b.Name, b => b.Handle);
    foreach (var tr in clip.Tracks) {
        if (!mine.TryGetValue(names[tr.Bone], out var h) || tr.KeyFrames.Count == 0) continue;
        var k = tr.KeyFrames; int i = k.FindLastIndex(f => f.Time <= t); if (i < 0) i = 0;
        var f0 = k[i]; var f1 = k[Math.Min(i + 1, k.Count - 1)];
        float s = f1.Time > f0.Time ? (t - f0.Time) / (f1.Time - f0.Time) : 0;
        lp[h] = (Vector3.Lerp(f0.Translation, f1.Translation, s), Quaternion.Slerp(f0.Rotation, f1.Rotation, s));
    }
}
Matrix4x4 World(ushort h, bool animated) {
    var b = byHandle[h]; var q = b.Orientation; var p = b.Position;
    if (animated) { p += lp[h].p; q = q * lp[h].q; }
    var m = Matrix4x4.CreateFromQuaternion(q) * Matrix4x4.CreateTranslation(p);
    return b.Parent is ushort par ? m * World(par, animated) : m;
}
foreach (var b in skel.Bones) { bind[b.Handle] = World(b.Handle, false); anim[b.Handle] = World(b.Handle, true); }
var skin = new Matrix4x4[n];
for (int i = 0; i < n; i++) { Matrix4x4.Invert(bind[i], out var inv); skin[i] = inv * anim[i]; }
var outp = new Vector3[pos.Length]; var wsum = new float[pos.Length];
foreach (var ba in sub.BoneAssignments.Concat(mesh.BoneAssignments)) {
    outp[ba.VertexIndex] += Vector3.Transform(pos[ba.VertexIndex], skin[ba.BoneIndex]) * ba.Weight; wsum[ba.VertexIndex] += ba.Weight; }
using var w = new StreamWriter(args[5]);
for (int i = 0; i < pos.Length; i++) { var p = wsum[i] > 0 ? outp[i] / wsum[i] : pos[i]; w.WriteLine($"v {p.X * 0.1f} {-p.Z * 0.1f} {p.Y * 0.1f}"); }
if (uv != null) for (int i = 0; i < pos.Length; i++) w.WriteLine($"vt {uv[i*2]} {1 - uv[i*2+1]}");
var idx = sub.Indices.Indices;
for (int i = 0; i < idx.Length; i += 3) { uint a0 = idx[i]+1, b0 = idx[i+1]+1, c0 = idx[i+2]+1; w.WriteLine(uv != null ? $"f {a0}/{a0} {b0}/{b0} {c0}/{c0}" : $"f {a0} {b0} {c0}"); }
