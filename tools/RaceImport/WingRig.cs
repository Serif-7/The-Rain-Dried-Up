using System.Numerics;

namespace RaceImport;

/// <summary>
/// Arm wings (docs/race-import.md "Wings"): Meshy binds a wing's membrane to helper bones and the spine, which pins it to fixed points. Before the
/// fit, every vertex past the shoulder on an arm's side (above the spine joint) is bound by its place along the arm instead: to the upper arm,
/// forearm or hand, blended over the elbow and the wrist. The membrane is what hangs below the arm further from its bones than the arm's skin above
/// them reaches; after the fit <see cref="WingSpread"/> places it and shares its weight with the flank.
/// </summary>
public static class WingRig
{
    /// <summary>
    /// How far past the hand the arm's last segment reaches, as a share of the forearm (claws and wing tips beyond the hand).
    /// </summary>
    const float TipReach = 0.8f;

    /// <summary>
    /// Half the width of the blend over the elbow and the wrist, as a share of the forearm.
    /// </summary>
    const float Blend = 0.15f;

    /// <summary>
    /// How many stretches the chain is cut into to measure the arm's thickness (<see cref="Arm.Radius"/>).
    /// </summary>
    const int Bands = 8;

    /// <summary>
    /// A vertex below the arm is the membrane's past this many times the reach of the arm's skin above it at that point of the chain.
    /// </summary>
    const float MembraneStart = 0.7f;

    /// <summary>
    /// Rebinds the arm wings of <paramref name="body"/> in place (its influences); returns which vertices are the membrane's and which of those a
    /// sleeve covers (up to the wrist). The model faces +Z (glTF's front), so behind is -Z.
    /// </summary>
    public static (bool[] Membrane, bool[] Sleeved) Apply(SourceBody body)
    {
        var membrane = new bool[body.Positions.Length];
        var sleeved = new bool[body.Positions.Length];
        float spine = Joint(body, "Bip001 Spine").Y;
        foreach (string side in new[] { "L", "R" })
        {
            var arm = Arm.Of(body, side);
            var vertices = Enumerable.Range(0, body.Positions.Length).Where(v => arm.Carries(body.Positions[v], spine)).ToArray();
            ApplySide(body, arm.Measured(body.Positions, vertices), vertices, membrane, sleeved);
        }

        return (membrane, sleeved);
    }

    /// <summary>
    /// One arm: each of its <paramref name="vertices"/> is bound along the arm, and marked when it is the membrane's.
    /// </summary>
    static void ApplySide(SourceBody body, Arm arm, int[] vertices, bool[] membrane, bool[] sleeved)
    {
        var below = arm.Below();
        foreach (int v in vertices)
        {
            var p = body.Positions[v];
            var (along, closest, distance) = arm.Nearest(p);
            body.Influences[v] = arm.Weights(along);

            float skin = arm.Radius(along);
            float start = skin * MembraneStart;
            if (distance <= start || Vector3.Dot(p - closest, below) <= 0) continue;

            membrane[v] = true;
            sleeved[v] = along < arm.ToWrist;
        }
    }

    /// <summary>
    /// A joint's rest position by name. Throws when the model lacks it.
    /// </summary>
    static Vector3 Joint(SourceBody body, string name)
    {
        int i = Array.IndexOf(body.JointNames, name);
        return i >= 0 ? body.JointPositions[i] : throw new InvalidDataException($"the model has no joint {name} (needed for wings)");
    }

    /// <summary>
    /// One arm's chain: shoulder, elbow, wrist and the tip past the hand, with the joint indices that carry each segment.
    /// </summary>
    sealed record Arm(Vector3 Shoulder, Vector3 Elbow, Vector3 Wrist, Vector3 Tip, int UpperArm, int Forearm, int Hand)
    {
        /// <summary>
        /// The reach of the arm's skin above the chain in each of its <see cref="Bands"/> (<see cref="Measured"/>); empty until measured.
        /// </summary>
        public float[] Radii { get; init; } = [];

        /// <summary>
        /// The arm of a side (<c>L</c> or <c>R</c>).
        /// </summary>
        public static Arm Of(SourceBody body, string side)
        {
            int upper = Index(body, $"Bip001 {side} UpperArm");
            int fore = Index(body, $"Bip001 {side} Forearm");
            int hand = Index(body, $"Bip001 {side} Hand");
            var wrist = body.JointPositions[hand];
            var elbow = body.JointPositions[fore];
            return new Arm(body.JointPositions[upper], elbow, wrist, wrist + (wrist - elbow) * TipReach, upper, fore, hand);
        }

        /// <summary>
        /// Whether a point is the arm's: past the shoulder on the arm's side and not below the spine joint.
        /// </summary>
        public bool Carries(Vector3 p, float spine) => p.Y >= spine && Vector3.Dot(p - Shoulder, Elbow - Shoulder) > 0;

        /// <summary>
        /// This arm with <see cref="Radii"/> measured from its <paramref name="vertices"/>: in each band, the furthest the vertices above the
        /// chain (only skin, since the membrane hangs below) lie from it; a band without any takes its neighbour's.
        /// </summary>
        public Arm Measured(Vector3[] positions, int[] vertices)
        {
            var radii = new float[Bands];
            foreach (int v in vertices)
            {
                var (along, closest, distance) = Nearest(positions[v]);
                if (positions[v].Y <= closest.Y) continue;

                int band = Band(along);
                radii[band] = Math.Max(radii[band], distance);
            }

            for (int b = 1; b < Bands; b++) if (radii[b] == 0) radii[b] = radii[b - 1];
            for (int b = Bands - 2; b >= 0; b--) if (radii[b] == 0) radii[b] = radii[b + 1];
            return this with { Radii = radii };
        }

        /// <summary>
        /// The reach of the arm's skin this far along the chain.
        /// </summary>
        public float Radius(float along) => Radii[Band(along)];

        /// <summary>
        /// The band of a point this far along the chain.
        /// </summary>
        int Band(float along) => Math.Clamp((int)(along / Length * Bands), 0, Bands - 1);

        /// <summary>
        /// The chain's length up to the tip.
        /// </summary>
        float Length => ToWrist + Vector3.Distance(Wrist, Tip);

        /// <summary>
        /// The direction below the arm, square to it.
        /// </summary>
        public Vector3 Below() => Vector3.Normalize(Flatten(-Vector3.UnitY, Axis));

        /// <summary>
        /// The direction of the arm from shoulder to wrist.
        /// </summary>
        Vector3 Axis => Vector3.Normalize(Wrist - Shoulder);

        /// <summary>
        /// The arm's length up to the elbow.
        /// </summary>
        float ToElbow => Vector3.Distance(Shoulder, Elbow);

        /// <summary>
        /// The arm's length up to the wrist.
        /// </summary>
        public float ToWrist => ToElbow + Vector3.Distance(Elbow, Wrist);

        /// <summary>
        /// The nearest point of the chain to <paramref name="p"/>: how far along the chain it lies, the point and its distance.
        /// </summary>
        public (float Along, Vector3 Closest, float Distance) Nearest(Vector3 p)
        {
            (Vector3 From, Vector3 To, float Start)[] segments = [(Shoulder, Elbow, 0), (Elbow, Wrist, ToElbow), (Wrist, Tip, ToWrist)];
            var best = (Along: 0f, Closest: Shoulder, Distance: float.MaxValue);
            foreach (var (from, to, at) in segments)
            {
                var d = to - from;
                float u = Math.Clamp(Vector3.Dot(p - from, d) / d.LengthSquared(), 0, 1);
                var q = from + u * d;
                float distance = Vector3.Distance(p, q);
                if (distance < best.Distance) best = (at + u * d.Length(), q, distance);
            }

            return best;
        }

        /// <summary>
        /// The influences of a point this far along the chain: the upper arm, then the forearm, then the hand, blended over elbow and wrist.
        /// </summary>
        public (int Joint, float Weight)[] Weights(float along)
        {
            float band = Blend * Vector3.Distance(Elbow, Wrist);
            float elbow = Smooth(ToElbow - band, ToElbow + band, along);
            float wrist = Smooth(ToWrist - band, ToWrist + band, along);
            (int Joint, float Weight)[] all = [(UpperArm, 1 - elbow), (Forearm, elbow * (1 - wrist)), (Hand, wrist)];
            return [.. all.Where(p => p.Weight > 1e-4f)];
        }

            /// <summary>
        /// A direction with its part along <paramref name="axis"/> removed.
        /// </summary>
        static Vector3 Flatten(Vector3 direction, Vector3 axis) => direction - axis * Vector3.Dot(direction, axis);

        /// <summary>
        /// Hermite smoothstep of <paramref name="x"/> between <paramref name="a"/> and <paramref name="b"/>.
        /// </summary>
        static float Smooth(float a, float b, float x)
        {
            float t = Math.Clamp((x - a) / (b - a), 0, 1);
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// A joint's index by name. Throws when the model lacks it.
        /// </summary>
        static int Index(SourceBody body, string name)
        {
            int i = Array.IndexOf(body.JointNames, name);
            return i >= 0 ? i : throw new InvalidDataException($"the model has no joint {name} (needed for wings)");
        }
    }
}
