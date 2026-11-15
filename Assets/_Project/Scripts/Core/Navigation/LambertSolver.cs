using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Navigation
{
    /// <summary>
    /// Lambert's problem: the conic that goes from r₁ to r₂ in a given time of flight around a body with μ (single revolution).
    /// Universal-variable formulation (Curtis, "Orbital Mechanics for Engineering Students", alg. 5.2), solved for z by a
    /// bracketed bisection on the monotonic time equation, so it never diverges; elliptic, parabolic and hyperbolic transfers.
    /// </summary>
    public static class LambertSolver
    {
        /// <summary>
        /// Velocities at departure (<paramref name="v1"/>) and arrival (<paramref name="v2"/>). <paramref name="referenceNormal"/>
        /// picks the direction of motion: the transfer goes around it counter-clockwise (prograde) or clockwise when
        /// <paramref name="prograde"/> is false. Returns false for degenerate geometry (r₁ ∥ r₂ — the plane is undefined) or no solution.
        /// </summary>
        public static bool Solve(double mu, Vector3d r1, Vector3d r2, double timeOfFlight, Vector3d referenceNormal, bool prograde,
            out Vector3d v1, out Vector3d v2)
        {
            v1 = v2 = Vector3d.Zero;
            if (!(mu > 0.0) || !(timeOfFlight > 0.0)) return false;
            double r1m = r1.Magnitude, r2m = r2.Magnitude;
            if (!(r1m > 0.0) || !(r2m > 0.0)) return false;

            Vector3d c12 = Vector3d.Cross(r1, r2);
            double cosDtheta = MathD.Clamp(Vector3d.Dot(r1, r2) / (r1m * r2m), -1.0, 1.0);
            if (c12.Magnitude < 1e-10 * r1m * r2m) return false; // 0° or 180°: infinitely many planes
            double dtheta = Math.Acos(cosDtheta);
            bool counterClockwise = Vector3d.Dot(c12, referenceNormal) >= 0.0;
            if (counterClockwise != prograde) dtheta = MathD.TwoPi - dtheta;

            double A = Math.Sin(dtheta) * Math.Sqrt(r1m * r2m / (1.0 - Math.Cos(dtheta)));
            if (Math.Abs(A) < 1e-12) return false;
            double sqrtMu = Math.Sqrt(mu);

            double Y(double z) => r1m + r2m + A * (z * ConicPropagator.StumpffS(z) - 1.0) / Math.Sqrt(ConicPropagator.StumpffC(z));

            // F(z) = time(z) − Δt, monotonic increasing where y(z) > 0; y ≤ 0 is unphysical and counts as "too short".
            double F(double z)
            {
                double y = Y(z);
                if (!(y > 0.0)) return double.NegativeInfinity;
                double c = ConicPropagator.StumpffC(z), s = ConicPropagator.StumpffS(z);
                double t = (Math.Pow(y / c, 1.5) * s + A * Math.Sqrt(y)) / sqrtMu;
                return t - timeOfFlight;
            }

            const double zMax = 4.0 * Math.PI * Math.PI; // one full revolution: time → ∞
            double hi = zMax * (1.0 - 1e-9);
            double lo = 0.0;
            if (F(lo) > 0.0)
            {
                // Hyperbolic (or short) transfer: search downwards until the time is too short.
                double step = 1.0;
                lo = -step;
                int guard = 0;
                while (F(lo) > 0.0 && guard++ < 200)
                {
                    step *= 2.0;
                    lo = -step;
                    if (lo < -1e7) return false;
                }
            }
            if (F(hi) < 0.0) return false;

            double z = 0.5 * (lo + hi);
            for (int i = 0; i < 300; i++)
            {
                z = 0.5 * (lo + hi);
                double f = F(z);
                if (f > 0.0) hi = z;
                else lo = z;
                if (hi - lo < 1e-14 * Math.Max(1.0, Math.Abs(z))) break;
            }
            double yz = Y(z);
            if (!(yz > 0.0)) return false;

            double fLag = 1.0 - yz / r1m;
            double g = A * Math.Sqrt(yz / mu);
            double gDot = 1.0 - yz / r2m;
            if (Math.Abs(g) < 1e-12) return false;
            v1 = (r2 - r1 * fLag) / g;
            v2 = (r2 * gDot - r1) / g;
            return v1.IsFinite && v2.IsFinite;
        }
    }
}
