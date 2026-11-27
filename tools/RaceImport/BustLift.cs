using System.Numerics;

namespace RaceImport;

/// <summary>
/// Raises a body's bust or chest to a human's height (docs/race-import.md "Bust"): the models sit it well below the human's, so the clothing's
/// chest and the part map's bust region ride above it. The bust is the most forward point of the chest between the waist
/// (<c>Spine1</c>) and the shoulders; the front of the torso moves up by the difference, fading out above, below and towards the back.
/// </summary>
public static class BustLift
{
    /// <summary>
    /// Bones whose vertices move with the bust (the torso's); arm and leg vertices stay.
    /// </summary>
    static readonly string[] TorsoBones = ["Pelvis", "Spine", "Spine1", "Spine2", "Neck", "L Clavicle", "R Clavicle"];

    /// <summary>
    /// How far the chest's sides reach from the middle, in game units (0.1 m): the bust is looked for between these.
    /// </summary>
    const float Inner = 0.3f, Outer = 1.8f;

    /// <summary>
    /// The height of the slices the chest's forward profile is taken in.
    /// </summary>
    const float Slice = 0.25f;

    /// <summary>
    /// The band that moves by the whole lift: from this far below the bust (its underside) to its top, at least <see cref="MinOver"/> above it.
    /// The top is where the chest's forward profile falls halfway from the bust's depth back to the collar's, so a tall bust (the Crevan woman's
    /// feathered one) moves whole instead of having its upper half squashed into a ledge.
    /// </summary>
    const float Under = 0.8f, MinOver = 0.2f;

    /// <summary>
    /// The fade below the band is this many times the lift long, and at least <see cref="MinFade"/>, so the chest below stretches by at most
    /// 1.75 (a narrow Gaussian stretched the bust into a cone: Matthew, 2026-11-23).
    /// </summary>
    const float FadePerLift = 2, MinFade = 1.5f;

    /// <summary>
    /// The fade above the band ends this far above the clavicles' joint (the neck's base), so the whole upper chest shares the squash evenly
    /// and the bust's top slopes gently down from the collar (a fade reaching into the neck, whose models' vertices ride the head, left a ledge
    /// above the bust: Matthew, 2026-11-24). It is at least <see cref="MinTopPerLift"/> times the lift long, so it never folds.
    /// </summary>
    const float TopOverClavicle = 0.4f, MinTopPerLift = 1.4f;

    /// <summary>
    /// The fitted positions with the bust raised to <paramref name="human"/>'s (the rig's own human, or the female's for a woman fitted to the male
    /// rig); returns them and the lift (0 when the bust is already as high).
    /// </summary>
    public static (Vector3[] Positions, float Lift) Apply(Vector3[] positions, Dictionary<ushort, float>[] weights, KenshiSkeleton kenshi,
        HumanBody human)
    {
        float low = kenshi.Position("Bip01 Spine1").Y, high = kenshi.Position("Bip01 L UpperArm").Y;
        var bust = Apex(positions, low, high);
        float lift = Apex(human.Positions, low, high).Height - bust.Height;
        if (lift <= 0) return (positions, 0);

        float back = kenshi.Position("Bip01 Spine2").Z;
        float below = MathF.Max(MinFade, FadePerLift * lift);
        float over = MathF.Max(MinOver, bust.Top - bust.Height);
        float above = MathF.Max(kenshi.Position("Bip01 L Clavicle").Y + TopOverClavicle - bust.Height - over, MinTopPerLift * lift);
        var torso = TorsoBones.Select(b => kenshi.Handle("Bip01 " + b)).ToHashSet();
        var lifted = new Vector3[positions.Length];
        for (int v = 0; v < positions.Length; v++)
        {
            var p = positions[v];
            float share = weights[v].Where(w => torso.Contains(w.Key)).Sum(w => w.Value);
            float around = Band(p.Y - bust.Height, over, below, above);
            float front = SmoothStep(back, bust.Depth, p.Z);
            lifted[v] = p with { Y = p.Y + lift * share * around * front };
        }

        return (lifted, lift);
    }

    /// <summary>
    /// The chest's most forward point between heights <paramref name="low"/> and <paramref name="high"/>: the middle of the slice whose
    /// frontmost vertex (beside the middle line, inside the chest's sides) is furthest forward, at that vertex's depth; and the bust's top, the
    /// bottom of the first slice above whose front is back halfway to the highest slice's (the collar's).
    /// </summary>
    static (float Height, float Depth, float Top) Apex(IReadOnlyList<Vector3> points, float low, float high)
    {
        int slices = (int)MathF.Ceiling((high - low) / Slice);
        var front = Enumerable.Repeat(float.MinValue, slices).ToArray();
        foreach (var p in points)
        {
            float side = MathF.Abs(p.X);
            if (side < Inner || side > Outer || p.Y < low || p.Y >= high) continue;

            int s = (int)((p.Y - low) / Slice);
            front[s] = MathF.Max(front[s], p.Z);
        }

        int best = Array.IndexOf(front, front.Max());
        int collar = Array.FindLastIndex(front, f => f > float.MinValue);
        float half = (front[best] + front[collar]) / 2;
        int top = best + 1;
        while (top < collar && front[top] > half) top++;

        return (low + (best + 0.5f) * Slice, front[best], low + top * Slice);
    }

    /// <summary>
    /// 0 below <paramref name="from"/>, 1 above <paramref name="to"/>, smooth between.
    /// </summary>
    static float SmoothStep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// The share of the lift at <paramref name="dy"/> from the bust: 1 in the band (up to <paramref name="over"/> above it), falling to 0 over <paramref name="below"/> under it (a
    /// smoothstep) and over <paramref name="above"/> over it (<see cref="Even"/>, which squashes the upper chest about evenly).
    /// </summary>
    static float Band(float dy, float over, float below, float above)
    {
        if (dy > over) return 1 - Even(Math.Clamp((dy - over) / above, 0, 1));
        if (dy < -Under) return SmoothStep(-Under - below, -Under, dy);

        return 1;
    }

    /// <summary>
    /// 0 to 1 as <paramref name="t"/> goes 0 to 1: mostly linear (an even squash), its ends rounded by blending a quarter of a smoothstep in so
    /// there is no crease where the fade starts and ends; steepest slope 1.125.
    /// </summary>
    static float Even(float t) => t + 0.25f * (t * t * (3 - 2 * t) - t);
}
