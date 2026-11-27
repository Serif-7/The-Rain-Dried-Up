using System.Numerics;

namespace RaceImport;

/// <summary>
/// Pulls a fitted body in where it is bulkier than the game's human body, so the game's clothing (modelled around the human) covers it
/// (docs/race-import.md "Slimming"). Around each torso and limb bone the human body's widest extent is measured per slice along the bone and per
/// direction around it; a fitted vertex beyond that (plus a margin for the cloth) is moved most of the way back. Vertices far beyond it (tails,
/// claws, horns) are left alone, being parts no clothing is meant to cover.
/// </summary>
public sealed class BodyConform
{
    /// <summary>
    /// The bones measured, each with the bone its axis points to.
    /// </summary>
    static readonly (string Bone, string Towards)[] Axes =
    [
        ("Pelvis", "Spine"), ("Spine", "Spine1"), ("Spine1", "Spine2"), ("Spine2", "Neck"),
        ("L Clavicle", "L UpperArm"), ("L UpperArm", "L Forearm"), ("L Forearm", "L Hand"),
        ("R Clavicle", "R UpperArm"), ("R UpperArm", "R Forearm"), ("R Forearm", "R Hand"),
        ("L Thigh", "L Calf"), ("L Calf", "L Foot"), ("R Thigh", "R Calf"), ("R Calf", "R Foot"),
    ];

    /// <summary>
    /// Slices along a bone, covering a quarter of its length beyond each end.
    /// </summary>
    const int Slices = 6;

    /// <summary>
    /// Directions around a bone.
    /// </summary>
    const int Sectors = 12;

    /// <summary>
    /// Room left for the cloth beyond the human skin, in game units (0.1 m).
    /// </summary>
    const float Margin = 0.15f;

    /// <summary>
    /// The share of the excess a pulled-in vertex keeps.
    /// </summary>
    const float Keep = 0.2f;

    /// <summary>
    /// Between these multiples of the limit the correction fades out: a vertex further out is an appendage and is not pulled in.
    /// </summary>
    const float FadeFrom = 1.5f, Appendage = 2.5f;

    /// <summary>
    /// Each measured bone's axis and the human body's extent around it, by bone handle.
    /// </summary>
    readonly Dictionary<ushort, (BoneAxis Axis, float[,] Extent)> _bones = [];

    /// <summary>
    /// Measures <paramref name="human"/> around the bones of <paramref name="kenshi"/>.
    /// </summary>
    public BodyConform(KenshiSkeleton kenshi, HumanBody human)
    {
        foreach (var (bone, towards) in Axes)
        {
            string name = "Bip01 " + bone, child = "Bip01 " + towards;
            if (!kenshi.Has(name) || !kenshi.Has(child)) continue;

            var axis = new BoneAxis(kenshi.Position(name), kenshi.Position(child));
            ushort handle = kenshi.Handle(name);
            _bones[handle] = (axis, Measure(axis, human, handle));
        }
    }

    /// <summary>
    /// Within this distance of a kept vertex (a tail's) the correction fades to nothing, so the body around a tail's root is not pulled in under
    /// it (a notch: Matthew, male Leoma, 2026-11-23).
    /// </summary>
    const float KeptReach = 1.5f;

    /// <summary>
    /// Rounds of smoothing of the corrections over the surface, so neighbouring vertices move alike (no creases where bones or the fade change).
    /// </summary>
    const int SmoothingRounds = 12;

    /// <summary>
    /// The positions pulled in, each vertex moved by the weighted sum of its bones' corrections, smoothed over the mesh's surface
    /// (<paramref name="indices"/>) and scaled by <paramref name="strength"/> (0 none, 1 full); vertices marked in <paramref name="kept"/> (a
    /// tail's) and those near them are not moved.
    /// </summary>
    public Vector3[] Apply(Vector3[] positions, Dictionary<ushort, float>[] weights, int[] indices, float strength, bool[]? kept = null)
    {
        var offsets = new Vector3[positions.Length];
        for (int v = 0; v < positions.Length; v++)
        {
            foreach (var (handle, weight) in weights[v])
                if (_bones.TryGetValue(handle, out var bone)) offsets[v] += weight * Correction(positions[v], bone.Axis, bone.Extent);
        }

        if (kept is not null) Spare(offsets, positions, kept);
        offsets = Smoothed(offsets, positions, indices);
        if (kept is not null) Spare(offsets, positions, kept);
        return [.. positions.Select((p, v) => p + strength * offsets[v])];
    }

    /// <summary>
    /// Scales each offset by a smoothstep of its vertex's distance to the nearest kept vertex, from 0 (at it) to 1 (<see cref="KeptReach"/> away).
    /// </summary>
    internal static void Spare(Vector3[] offsets, Vector3[] positions, bool[] kept)
    {
        var keptPoints = positions.Where((_, v) => kept[v]).ToArray();
        if (keptPoints.Length == 0) return;

        for (int v = 0; v < offsets.Length; v++)
        {
            float distance = MathF.Sqrt(keptPoints.Min(k => Vector3.DistanceSquared(k, positions[v])));
            float t = Math.Clamp(distance / KeptReach, 0, 1);
            offsets[v] *= t * t * (3 - 2 * t);
        }
    }

    /// <summary>
    /// The offsets averaged with their neighbours' <see cref="SmoothingRounds"/> times; vertices at one position (uv seams) are welded first, so
    /// a seam moves as one.
    /// </summary>
    internal static Vector3[] Smoothed(Vector3[] offsets, Vector3[] positions, int[] indices, int rounds = SmoothingRounds)
    {
        var weld = Surface.Weld(positions);
        int count = weld.Max() + 1;
        var neighbours = Surface.Neighbours(weld, indices);

        var welded = new Vector3[count];
        var members = new int[count];
        for (int v = 0; v < offsets.Length; v++)
        {
            welded[weld[v]] += offsets[v];
            members[weld[v]]++;
        }

        for (int w = 0; w < count; w++) welded[w] /= members[w];
        for (int round = 0; round < rounds; round++)
            welded = [.. Enumerable.Range(0, count).Select(w => (welded[w] + neighbours[w].Aggregate(Vector3.Zero, (s, n) => s + welded[n])) / (1 + neighbours[w].Count))];

        return [.. weld.Select(w => welded[w])];
    }

    /// <summary>
    /// The human vertices carried mostly by the bone: their furthest distance from its axis per slice and direction, empty cells filled from
    /// their slice (or the whole bone).
    /// </summary>
    static float[,] Measure(BoneAxis axis, HumanBody human, ushort handle)
    {
        var extent = new float[Slices, Sectors];
        for (int v = 0; v < human.Positions.Length; v++)
        {
            if (human.Bones[v] != handle) continue;

            var (slice, sector, radius, _) = axis.Locate(human.Positions[v], Slices, Sectors);
            if (slice < 0) continue;

            extent[slice, sector] = MathF.Max(extent[slice, sector], radius);
        }

        Fill(extent);
        return extent;
    }

    /// <summary>
    /// Gives each empty cell its slice's largest extent, or the bone's largest when the slice is empty too.
    /// </summary>
    static void Fill(float[,] extent)
    {
        float all = extent.Cast<float>().Max();
        for (int slice = 0; slice < Slices; slice++)
        {
            float row = Enumerable.Range(0, Sectors).Max(s => extent[slice, s]);
            for (int s = 0; s < Sectors; s++)
                if (extent[slice, s] == 0) extent[slice, s] = row > 0 ? row : all;
        }
    }

    /// <summary>
    /// How far one bone moves a vertex: back towards the axis when it lies beyond the human extent plus the margin, fading out towards an
    /// appendage's distance; nothing for a vertex beside the bone's slices (a tail hanging past the pelvis).
    /// </summary>
    static Vector3 Correction(Vector3 position, BoneAxis axis, float[,] extent)
    {
        var (slice, sector, radius, outward) = axis.Locate(position, Slices, Sectors);
        if (slice < 0) return Vector3.Zero;

        float limit = extent[slice, sector] + Margin;
        if (radius <= limit || radius >= Appendage * limit) return Vector3.Zero;

        float fade = Math.Clamp((Appendage * limit - radius) / ((Appendage - FadeFrom) * limit), 0, 1);
        float target = limit + (radius - limit) * Keep;
        return fade * (target - radius) * outward;
    }

    /// <summary>
    /// A bone's axis from its position towards its child, with a frame around it for directions.
    /// </summary>
    sealed class BoneAxis
    {
        /// <summary>
        /// The bone's position.
        /// </summary>
        readonly Vector3 _start;

        /// <summary>
        /// The vector to the child.
        /// </summary>
        readonly Vector3 _along;

        /// <summary>
        /// Two unit directions perpendicular to the axis and each other.
        /// </summary>
        readonly Vector3 _side, _front;

        /// <summary>
        /// The axis from <paramref name="start"/> to <paramref name="end"/>.
        /// </summary>
        public BoneAxis(Vector3 start, Vector3 end)
        {
            _start = start;
            _along = end - start;
            var unit = Vector3.Normalize(_along);
            var side = Vector3.Cross(unit, Vector3.UnitZ);
            if (side.Length() < 1e-3f) side = Vector3.Cross(unit, Vector3.UnitX);

            _side = Vector3.Normalize(side);
            _front = Vector3.Cross(unit, _side);
        }

        /// <summary>
        /// Where a point lies around the axis: its slice along it (-1 beyond the slices), its direction sector, its distance from it and the unit
        /// direction out.
        /// </summary>
        public (int Slice, int Sector, float Radius, Vector3 Outward) Locate(Vector3 point, int slices, int sectors)
        {
            float t = Vector3.Dot(point - _start, _along) / _along.LengthSquared();
            var radial = point - (_start + t * _along);
            float radius = radial.Length();
            float angle = MathF.Atan2(Vector3.Dot(radial, _front), Vector3.Dot(radial, _side));

            int slice = (int)MathF.Floor((t + 0.25f) / 1.5f * slices);
            if (slice < 0 || slice >= slices) slice = -1;
            int sector = ((int)MathF.Floor((angle + MathF.PI) / (2 * MathF.PI) * sectors) % sectors + sectors) % sectors;
            return (slice, sector, radius, radius > 1e-6f ? radial / radius : Vector3.Zero);
        }
    }
}
