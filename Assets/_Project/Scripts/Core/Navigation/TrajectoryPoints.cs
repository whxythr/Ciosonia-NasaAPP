using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Navigation
{
    /// <summary>A point chosen on a predicted trajectory: when the vehicle gets there and its exact state at that moment.</summary>
    public struct TrajectoryPoint
    {
        /// <summary>Simulation time of the point [s].</summary>
        public double Time;
        /// <summary>State relative to <see cref="CoastState.Primary"/> at <see cref="Time"/>.</summary>
        public CoastState State;
        /// <summary>Index of the patch of the prediction the point lies on.</summary>
        public int Patch;
        public bool Valid;
    }

    /// <summary>
    /// Time of flight to a point of a conic and exact states along a patched-conic prediction. Pure two-body / patched-conic math
    /// (<see cref="ConicPropagator"/>); used by the map to warp to a clicked point and to place manoeuvre nodes.
    /// </summary>
    public static class TrajectoryPoints
    {
        /// <summary>Below this eccentricity the true anomaly has no meaning: angles are measured from the current position instead.</summary>
        public const double CircularEccentricity = 1e-7;

        /// <summary>
        /// Forward time of flight [s] from the state (r, v) to the point of the same conic in the direction of <paramref name="pointDirection"/>
        /// (any vector from the primary's centre; only its direction in the orbit plane counts). Ellipses: the pass closest to
        /// <paramref name="approximateTime"/> (whole revolutions are added, never a negative time); hyperbolas: the only pass, +∞ if
        /// the direction lies outside the asymptotes or behind the vehicle.
        /// </summary>
        public static double TimeOfFlight(double mu, Vector3d r, Vector3d v, Vector3d pointDirection, double approximateTime = 0.0)
        {
            if (!(mu > 0.0)) throw new ArgumentOutOfRangeException(nameof(mu));
            Vector3d h = Vector3d.Cross(r, v);
            if (!(h.SqrMagnitude > 0.0)) return double.PositiveInfinity; // radial trajectory: no plane, no anomaly
            Vector3d n = h.Normalized;
            Vector3d d = Vector3d.ProjectOnPlane(pointDirection, n);
            if (!(d.SqrMagnitude > 0.0)) return double.PositiveInfinity;

            ConicPropagator.Shape(mu, r, v, out double e, out _, out _);
            double alpha = 2.0 / r.Magnitude - v.SqrMagnitude / mu;
            double t;
            if (e < CircularEccentricity && alpha > 0.0)
            {
                // Circle: uniform motion; the angle from the current position in the direction of motion.
                double angle = SignedAngle(r, d, n);
                if (angle < 0.0) angle += MathD.TwoPi;
                double nMotion = Math.Sqrt(mu * alpha * alpha * alpha);
                t = angle / nMotion;
            }
            else
            {
                Vector3d eDir = ConicPropagator.EccentricityVector(mu, r, v).Normalized;
                double theta = SignedAngle(eDir, d, n);
                t = ConicPropagator.TimeToTrueAnomaly(mu, r, v, theta);
            }
            if (double.IsInfinity(t) || double.IsNaN(t)) return double.PositiveInfinity;
            if (alpha > 0.0 && approximateTime > t)
            {
                double period = MathD.TwoPi / Math.Sqrt(mu * alpha * alpha * alpha);
                t += Math.Round((approximateTime - t) / period) * period;
            }
            return t;
        }

        /// <summary>Angle from a to b about the unit normal n, in (−π, π].</summary>
        public static double SignedAngle(Vector3d a, Vector3d b, Vector3d n) =>
            Math.Atan2(Vector3d.Dot(Vector3d.Cross(a, b), n), Vector3d.Dot(a, b));

        /// <summary>Exact state of the prediction at <paramref name="time"/> (false outside it).</summary>
        public static bool StateAt(PatchedConicSystem sys, TrajectoryPrediction prediction, double time, out CoastState state, out int patchIndex)
        {
            state = default;
            patchIndex = -1;
            if (prediction == null) return false;
            for (int i = 0; i < prediction.Patches.Count; i++)
            {
                PredictedPatch p = prediction.Patches[i];
                bool last = i == prediction.Patches.Count - 1;
                if (time < p.StartTime || (time > p.EndTime && !(last && time - p.EndTime < 1e-6))) continue;
                ConicPropagator.Propagate(sys.Mu(p.Primary), p.Start.Position, p.Start.Velocity, time - p.StartTime, out Vector3d r, out Vector3d v);
                state = new CoastState(p.Primary, r, v, time);
                patchIndex = i;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The point of a prediction between samples <paramref name="sample"/> and <paramref name="sample"/>+1 of a patch, at fraction
        /// <paramref name="fraction"/> of that interval. On the first patch around the vehicle's current primary the time is
        /// recomputed exactly from Kepler's equation (time of flight to the point's true anomaly) rather than interpolated.
        /// </summary>
        public static TrajectoryPoint PointOnPatch(PatchedConicSystem sys, TrajectoryPrediction prediction, int patchIndex, int sample, double fraction,
            CoastState vehicleNow)
        {
            var result = new TrajectoryPoint();
            if (prediction == null || patchIndex < 0 || patchIndex >= prediction.Patches.Count) return result;
            PredictedPatch p = prediction.Patches[patchIndex];
            if (p.Times.Count == 0) return result;
            sample = Math.Max(0, Math.Min(p.Times.Count - 1, sample));
            double t0 = p.Times[sample];
            double t1 = sample + 1 < p.Times.Count ? p.Times[sample + 1] : t0;
            double time = t0 + MathD.Clamp01(fraction) * (t1 - t0);
            double mu = sys.Mu(p.Primary);

            // The chord between samples is not the curve: snap to the conic through the interpolated direction (exact time).
            ConicPropagator.Propagate(mu, p.Start.Position, p.Start.Velocity, time - p.StartTime, out Vector3d approx, out _);
            if (patchIndex == 0 && vehicleNow.Primary == p.Primary && Math.Abs(vehicleNow.Time - p.StartTime) < 1.0)
            {
                double tof = TimeOfFlight(mu, vehicleNow.Position, vehicleNow.Velocity, approx, time - vehicleNow.Time);
                if (!double.IsInfinity(tof) && Math.Abs(vehicleNow.Time + tof - time) < 0.5 * Math.Max(1.0, t1 - t0) + 1.0)
                    time = vehicleNow.Time + tof;
            }
            ConicPropagator.Propagate(mu, p.Start.Position, p.Start.Velocity, time - p.StartTime, out Vector3d r, out Vector3d v);
            result.Time = time;
            result.State = new CoastState(p.Primary, r, v, time);
            result.Patch = patchIndex;
            result.Valid = true;
            return result;
        }
    }
}
