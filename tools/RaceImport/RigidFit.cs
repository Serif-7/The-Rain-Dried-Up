using System.Numerics;

namespace RaceImport;

/// <summary>
/// The rotation and translation (no scale) that take one set of points closest to another in the least-squares sense (Horn's closed form:
/// the rotation is the eigenvector of the largest eigenvalue of a symmetric 4×4 built from the points' cross-covariance).
/// </summary>
public static class RigidFit
{
    /// <summary>
    /// The proper rotation <c>R</c> and translation <c>t</c> minimising the summed squared distance of <c>R a + t</c> to <c>b</c> over the
    /// pairs; the rotation as a row-vector matrix.
    /// </summary>
    public static (Matrix4x4 Rotation, Vector3 Translation) Fit(IReadOnlyList<Vector3> from, IReadOnlyList<Vector3> to)
    {
        var centreFrom = Centre(from);
        var centreTo = Centre(to);
        var s = CrossCovariance(from, to, centreFrom, centreTo);

        var q = LargestEigenvector(HornMatrix(s));
        var rotation = Quaternion.Normalize(new Quaternion((float)q[1], (float)q[2], (float)q[3], (float)q[0]));
        var matrix = Matrix4x4.CreateFromQuaternion(rotation);

        return (matrix, centreTo - Vector3.TransformNormal(centreFrom, matrix));
    }

    /// <summary>
    /// The mean of the points.
    /// </summary>
    static Vector3 Centre(IReadOnlyList<Vector3> points) => points.Aggregate(Vector3.Zero, (a, p) => a + p) / points.Count;

    /// <summary>
    /// <c>S[i, j]</c> = the sum over the pairs of the centred <c>from[i] × to[j]</c>.
    /// </summary>
    static double[,] CrossCovariance(IReadOnlyList<Vector3> from, IReadOnlyList<Vector3> to, Vector3 centreFrom, Vector3 centreTo)
    {
        var s = new double[3, 3];
        for (int n = 0; n < from.Count; n++)
        {
            var a = from[n] - centreFrom;
            var b = to[n] - centreTo;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    s[i, j] += a[i] * b[j];
        }

        return s;
    }

    /// <summary>
    /// Horn's symmetric matrix, whose top eigenvector is the rotation quaternion (w, x, y, z).
    /// </summary>
    static double[,] HornMatrix(double[,] s)
    {
        double xx = s[0, 0], xy = s[0, 1], xz = s[0, 2];
        double yx = s[1, 0], yy = s[1, 1], yz = s[1, 2];
        double zx = s[2, 0], zy = s[2, 1], zz = s[2, 2];
        return new double[,]
        {
            { xx + yy + zz, yz - zy, zx - xz, xy - yx },
            { yz - zy, xx - yy - zz, xy + yx, zx + xz },
            { zx - xz, xy + yx, -xx + yy - zz, yz + zy },
            { xy - yx, zx + xz, yz + zy, -xx - yy + zz },
        };
    }

    /// <summary>
    /// The unit eigenvector of a symmetric 4×4 matrix's largest eigenvalue (cyclic Jacobi rotations).
    /// </summary>
    static double[] LargestEigenvector(double[,] a)
    {
        var v = new double[4, 4];
        for (int i = 0; i < 4; i++) v[i, i] = 1;

        for (int sweep = 0; sweep < 64 && OffDiagonal(a) > 1e-18; sweep++)
            for (int p = 0; p < 3; p++)
                for (int q = p + 1; q < 4; q++)
                    Rotate(a, v, p, q);

        int best = Enumerable.Range(0, 4).MaxBy(i => a[i, i]);
        return [v[0, best], v[1, best], v[2, best], v[3, best]];
    }

    /// <summary>
    /// The sum of the squared off-diagonal entries.
    /// </summary>
    static double OffDiagonal(double[,] a)
    {
        double sum = 0;
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                if (i != j) sum += a[i, j] * a[i, j];

        return sum;
    }

    /// <summary>
    /// One Jacobi rotation zeroing <c>a[p, q]</c>, accumulated into the eigenvectors <paramref name="v"/>.
    /// </summary>
    static void Rotate(double[,] a, double[,] v, int p, int q)
    {
        if (Math.Abs(a[p, q]) < 1e-300) return;

        double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
        double t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
        double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
        for (int k = 0; k < 4; k++)
        {
            double akp = a[k, p], akq = a[k, q];
            a[k, p] = c * akp - s * akq;
            a[k, q] = s * akp + c * akq;
        }

        for (int k = 0; k < 4; k++)
        {
            double apk = a[p, k], aqk = a[q, k];
            a[p, k] = c * apk - s * aqk;
            a[q, k] = s * apk + c * aqk;
        }

        for (int k = 0; k < 4; k++)
        {
            double vkp = v[k, p], vkq = v[k, q];
            v[k, p] = c * vkp - s * vkq;
            v[k, q] = s * vkp + c * vkq;
        }
    }
}
