using System.Numerics;

namespace RaceImport;

/// <summary>
/// How the vertices a source joint carries move onto the game's skeleton: taken relative to the joint's rest position (after scaling the model),
/// optionally stretched along the bone, transformed linearly and placed at the target position.
/// </summary>
/// <param name="Source">The joint's rest position in the model.</param>
/// <param name="Target">Where that point lands on the game's skeleton.</param>
/// <param name="Linear">The rotation (or, for the feet, the squash) applied, as a row-vector matrix (<see cref="Vector3.TransformNormal(Vector3, Matrix4x4)"/>).</param>
/// <param name="Along">The unit direction the bone is stretched along, in the model's frame; null for no stretch.</param>
/// <param name="Ratio">The stretch: the game's bone length over the scaled model's.</param>
public sealed record BoneFit(Vector3 Source, Vector3 Target, Matrix4x4 Linear, Vector3? Along, float Ratio)
{
    /// <summary>
    /// A point (position or, with <paramref name="isNormal"/>, a normal) of the model moved by this fit; <paramref name="scale"/> is the
    /// model's scale to the game's units.
    /// </summary>
    public Vector3 Apply(Vector3 point, float scale, bool isNormal = false)
    {
        if (isNormal) return Vector3.TransformNormal(point, Linear);

        var local = scale * (point - Source);
        if (Along is { } along && Ratio != 1)
            local += (Ratio - 1) * MathF.Max(Vector3.Dot(local, along), 0) * along;

        return Vector3.TransformNormal(local, Linear) + Target;
    }
}
