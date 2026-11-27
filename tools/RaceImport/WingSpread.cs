using System.Numerics;

namespace RaceImport;

/// <summary>
/// Arm wings that spread and fold with the arms (docs/race-import.md "Wings"). The model shows its membrane spread, the arms out to the sides; the
/// game's bind pose has them down. Each membrane vertex is weighted between its arm (<see cref="WingRig"/>'s binding along the arm) and the upper
/// spine, and placed in the bind pose where that blend of the two takes it back to the model's spread wing when the game raises the arm to the
/// model's pose: so the membrane opens between arm and flank as the arm rises and closes up as it falls. Of the blends that do, each vertex takes
/// the one that lays it nearest the arm in the bind pose, so a lowered arm hides its membrane along itself.
/// </summary>
public sealed class WingSpread
{
    #region Spread

    /// <summary>
    /// The torso bone the membrane's flank side follows.
    /// </summary>
    const string Flank = "Bip01 Spine2";

    /// <summary>
    /// How many blends between arm and flank are tried per vertex.
    /// </summary>
    const int Steps = 100;

    /// <summary>
    /// How far from the arm's bones a membrane vertex may lie in the bind pose, in game units: well inside the arm's skin, so a lowered arm hides
    /// its folded membrane.
    /// </summary>
    const float Inside = 0.25f;

    /// <summary>
    /// How far past the wrist the chain reaches, as a share of the forearm (wing tips), as in <see cref="WingRig"/>.
    /// </summary>
    const float TipReach = 0.8f;

    /// <summary>
    /// The arm bones of each side, shoulder to hand (short names, as <see cref="BodyFit.Fits"/> keys them).
    /// </summary>
    static readonly string[] ArmBones = ["UpperArm", "Forearm", "Hand"];

    /// <summary>
    /// The fit the membrane is placed from.
    /// </summary>
    readonly BodyFit _fit;

    /// <summary>
    /// The game's skeleton.
    /// </summary>
    readonly KenshiSkeleton _kenshi;

    /// <summary>
    /// The rotation of the model's torso fit: the spread pose is the model turned by it, its torso unmoved from the bind pose.
    /// </summary>
    readonly Matrix4x4 _torso;

    /// <summary>
    /// Each arm bone's move from the bind pose to the model's spread pose (row-vector affine), by game bone handle.
    /// </summary>
    readonly Dictionary<ushort, Matrix4x4> _spread = [];

    /// <summary>
    /// Each side's arm chain in the bind pose: shoulder, elbow, wrist and a tip past the hand.
    /// </summary>
    readonly Dictionary<string, Vector3[]> _chains = [];

    /// <summary>
    /// The spread of <paramref name="fit"/> on <paramref name="kenshi"/>.
    /// </summary>
    public WingSpread(BodyFit fit, KenshiSkeleton kenshi)
    {
        _fit = fit;
        _kenshi = kenshi;
        var torso = fit.Fits["Spine2"];
        _torso = torso.Linear;
        foreach (var side in new[] { "L", "R" }) AddArm(side);
    }

    /// <summary>
    /// Places and weights the membrane vertices (<paramref name="membrane"/>) of the fitted mesh in place; <paramref name="body"/> is the model,
    /// its influences already bound along the arm. Returns the share of each vertex's weight left on the arm (1 off the membrane).
    /// </summary>
    public float[] Apply(SourceBody body, bool[] membrane, Vector3[] positions, Vector3[] normals, Dictionary<ushort, float>[] weights)
    {
        var shares = Enumerable.Repeat(1f, positions.Length).ToArray();
        ushort flank = _kenshi.Handle(Flank);
        for (int v = 0; v < positions.Length; v++)
        {
            if (!membrane[v]) continue;

            var arm = weights[v];
            var spread = SpreadPosition(arm, positions[v]);
            var chain = _chains[Side(arm)];
            var (share, placed, matrix) = Nearest(arm, flank, spread, chain);
            positions[v] = Tucked(placed, chain);
            normals[v] = SpreadNormal(body.Normals[v], matrix);
            weights[v] = Weighted(arm, flank, share);
            shares[v] = share;
        }

        return shares;
    }

    /// <summary>
    /// Where the fitted vertex at <paramref name="fitted"/>, carried by the arm bones <paramref name="arm"/>, lies in the spread pose.
    /// </summary>
    Vector3 SpreadPosition(Dictionary<ushort, float> arm, Vector3 fitted)
    {
        var sum = Vector3.Zero;
        foreach (var (bone, w) in arm) sum += w * Vector3.Transform(fitted, _spread[bone]);
        return sum;
    }

    /// <summary>
    /// Of the blends of arm and flank, the one whose bind-pose place for the spread point lies nearest the arm chain: its arm share, the place,
    /// and the blended move from bind to spread pose.
    /// </summary>
    (float Share, Vector3 Placed, Matrix4x4 Move) Nearest(Dictionary<ushort, float> arm, ushort flank, Vector3 spread, Vector3[] chain)
    {
        var best = (Share: 1f, Placed: spread, Move: Matrix4x4.Identity, Distance: float.MaxValue);
        for (int i = 0; i <= Steps; i++)
        {
            float share = (float)i / Steps;
            var move = Blend(arm, share);
            if (!Matrix4x4.Invert(move, out var back)) continue;

            var placed = Vector3.Transform(spread, back);
            float distance = Distance(placed, chain);
            if (distance < best.Distance) best = (share, placed, move, distance);
        }

        return (best.Share, best.Placed, best.Move);
    }

    /// <summary>
    /// The move from bind to spread pose of a vertex with <paramref name="share"/> of its weight on the arm bones (spread as
    /// <paramref name="arm"/>) and the rest on the flank, which does not move.
    /// </summary>
    Matrix4x4 Blend(Dictionary<ushort, float> arm, float share)
    {
        var move = Matrix4x4.Identity * (1 - share);
        foreach (var (bone, w) in arm) move += _spread[bone] * (w * share);
        return move;
    }

    /// <summary>
    /// The model's normal turned into the spread pose by the torso fit, then back to the bind pose through the blended move.
    /// </summary>
    Vector3 SpreadNormal(Vector3 normal, Matrix4x4 move)
    {
        var spread = Vector3.TransformNormal(normal, _torso);
        var bind = Vector3.TransformNormal(spread, Matrix4x4.Transpose(move));
        return bind.LengthSquared() > 1e-12f ? Vector3.Normalize(bind) : Vector3.UnitY;
    }

    /// <summary>
    /// The weights of a membrane vertex: <paramref name="share"/> on its arm bones as bound, the rest on the flank.
    /// </summary>
    static Dictionary<ushort, float> Weighted(Dictionary<ushort, float> arm, ushort flank, float share)
    {
        var weights = arm.ToDictionary(p => p.Key, p => p.Value * share);
        if (share < 1) weights[flank] = 1 - share;
        return weights;
    }

    #endregion

    #region Arms

    /// <summary>
    /// One side's arm moves and chain: each arm bone turned from the game's direction back to the model's, the joints following down the chain.
    /// </summary>
    void AddArm(string side)
    {
        var joints = ArmBones.Select(b => _kenshi.Position($"Bip01 {side} {b}")).ToArray();
        var spreadJoint = joints[0];
        for (int i = 0; i < ArmBones.Length; i++)
        {
            var fit = _fit.Fits[$"{side} {ArmBones[i]}"];
            Matrix4x4.Invert(fit.Linear, out var undo);
            var turn = undo * _torso;
            if (i > 0) spreadJoint += Vector3.TransformNormal(joints[i] - joints[i - 1], PreviousTurn(side, i));
            _spread[_kenshi.Handle($"Bip01 {side} {ArmBones[i]}")] =
                Matrix4x4.CreateTranslation(-joints[i]) * turn * Matrix4x4.CreateTranslation(spreadJoint);
        }

        var tip = joints[2] + (joints[2] - joints[1]) * TipReach;
        _chains[side] = [joints[0], joints[1], joints[2], tip];
    }

    /// <summary>
    /// The turn of the arm bone before the <paramref name="i"/>th of a side.
    /// </summary>
    Matrix4x4 PreviousTurn(string side, int i)
    {
        Matrix4x4.Invert(_fit.Fits[$"{side} {ArmBones[i - 1]}"].Linear, out var undo);
        return undo * _torso;
    }

    /// <summary>
    /// The side (<c>L</c> or <c>R</c>) whose arm bones carry a vertex.
    /// </summary>
    string Side(Dictionary<ushort, float> arm)
    {
        string name = _kenshi.Name(arm.MaxBy(p => p.Value).Key) ?? "";
        return name.StartsWith("Bip01 L", StringComparison.Ordinal) ? "L" : "R";
    }

    /// <summary>
    /// The distance from a point to a polyline.
    /// </summary>
    static float Distance(Vector3 p, Vector3[] chain) => Vector3.Distance(p, Closest(p, chain));

    /// <summary>
    /// The point of a polyline closest to a point.
    /// </summary>
    static Vector3 Closest(Vector3 p, Vector3[] chain)
    {
        var best = chain[0];
        for (int i = 0; i + 1 < chain.Length; i++)
        {
            var d = chain[i + 1] - chain[i];
            float u = Math.Clamp(Vector3.Dot(p - chain[i], d) / d.LengthSquared(), 0, 1);
            var q = chain[i] + u * d;
            if (Vector3.DistanceSquared(p, q) < Vector3.DistanceSquared(p, best)) best = q;
        }

        return best;
    }

    /// <summary>
    /// A point drawn towards a polyline until it lies within <see cref="Inside"/> of it.
    /// </summary>
    static Vector3 Tucked(Vector3 p, Vector3[] chain)
    {
        var closest = Closest(p, chain);
        float distance = Vector3.Distance(p, closest);
        return distance <= Inside ? p : closest + (p - closest) * (Inside / distance);
    }

    #endregion
}
