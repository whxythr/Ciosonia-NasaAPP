using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Navigation
{
    /// <summary>
    /// Warp to a moment ("warp to the clicked point", "warp to the manoeuvre"): each frame the warp is chosen so that time slows
    /// smoothly as the moment approaches and the last frames run at ×1, so the vehicle stops at the point instead of skipping it.
    /// The caller still clamps the result to the situation limit of <see cref="Flight.TimeWarpPolicy"/> (atmosphere, engine).
    /// </summary>
    public static class WarpApproach
    {
        /// <summary>Real seconds the remaining time would take at the commanded warp (the exponential approach constant).</summary>
        public const double ApproachTime = 0.75;
        /// <summary>One frame may cover at most this share of the time left (protection against a long frame).</summary>
        public const double MaxFrameShare = 0.5;
        /// <summary>The point counts as reached this close [s of simulation time].</summary>
        public const double ArrivalTolerance = 0.05;

        /// <summary>
        /// Warp for the next frame. <paramref name="remaining"/> — simulation seconds left to the point; <paramref name="warpLimit"/> —
        /// the highest warp allowed now; <paramref name="maxFrameSeconds"/> — the longest real frame the clock can take.
        /// <paramref name="arrived"/> is true when the point is reached (or passed): the caller then drops to ×1 and stops.
        /// </summary>
        public static double Command(double remaining, double warpLimit, double maxFrameSeconds, out bool arrived)
        {
            arrived = !(remaining > ArrivalTolerance);
            if (arrived) return 1.0;
            double frame = Math.Max(1.0 / 240.0, maxFrameSeconds);
            double warp = Math.Min(remaining / ApproachTime, MaxFrameShare * remaining / frame);
            return MathD.Clamp(warp, 1.0, Math.Max(1.0, warpLimit));
        }

        /// <summary>
        /// Warp-to-node lead: the warp stops this long before the node so the burn can start on time — half the burn (it is centred
        /// on the node) plus a margin for turning to the burn attitude [s].
        /// </summary>
        public static double NodeLead(double burnDuration, double margin = 30.0) =>
            0.5 * Math.Max(0.0, double.IsInfinity(burnDuration) || double.IsNaN(burnDuration) ? 0.0 : burnDuration) + Math.Max(0.0, margin);
    }

    /// <summary>
    /// Assisted burn (the engine and the attitude are commanded by the flight computer through the real propulsion model): when
    /// to throttle down, when to cut off, how fast time may run while burning.
    /// </summary>
    public static class BurnControl
    {
        /// <summary>The burn is complete when the remaining Δv is below this [m/s].</summary>
        public const double CutoffDeltaV = 0.1;
        /// <summary>The engine runs only while the thrust points within this angle of the remaining Δv [deg].</summary>
        public const double AlignTolerance = 4.0;
        /// <summary>Throttle-down constant: the last <c>a·t</c> of Δv is flown with a proportionally smaller throttle [s].</summary>
        public const double ThrottleTime = 1.0;
        public const double MinThrottle = 0.05;

        /// <summary>Throttle 0..1 for the remaining Δv with a full-thrust acceleration <paramref name="maxAcceleration"/>.</summary>
        public static double Throttle(double remaining, double maxAcceleration)
        {
            if (!(remaining > CutoffDeltaV)) return 0.0;
            if (!(maxAcceleration > 0.0)) return 1.0;
            return MathD.Clamp(remaining / (maxAcceleration * ThrottleTime), MinThrottle, 1.0);
        }

        /// <summary>
        /// True when the burn is over: the remaining Δv is small, or it now points against the planned direction (the target was
        /// passed: any more thrust would make it worse).
        /// </summary>
        public static bool Finished(Mathematics.Vector3d remaining, Mathematics.Vector3d plannedDirection) =>
            remaining.Magnitude <= CutoffDeltaV || Mathematics.Vector3d.Dot(remaining, plannedDirection) < 0.0;

        /// <summary>Highest warp at which one frame changes the velocity by at most a quarter of what is left.</summary>
        public static double MaxWarp(double remaining, double acceleration, double maxFrameSeconds)
        {
            if (!(acceleration > 0.0)) return double.PositiveInfinity;
            double frame = Math.Max(1.0 / 240.0, maxFrameSeconds);
            return Math.Max(1.0, 0.25 * remaining / (acceleration * frame));
        }

        /// <summary>Moves an attitude angle towards a target at most <paramref name="maxStep"/> degrees (shortest way for headings).</summary>
        public static double Approach(double current, double target, double maxStep, bool wrap)
        {
            double d = target - current;
            if (wrap)
            {
                d = ((d % 360.0) + 540.0) % 360.0 - 180.0;
            }
            double next = Math.Abs(d) <= maxStep ? current + d : current + Math.Sign(d) * maxStep;
            return wrap ? ((next % 360.0) + 360.0) % 360.0 : next;
        }

        /// <summary>Angle [deg] between two attitudes given as pitch from the local vertical and heading from north.</summary>
        public static double AttitudeError(double pitch1, double heading1, double pitch2, double heading2)
        {
            Mathematics.Vector3d a = Direction(pitch1, heading1), b = Direction(pitch2, heading2);
            return Math.Acos(MathD.Clamp(Mathematics.Vector3d.Dot(a, b), -1.0, 1.0)) * MathD.RadToDeg;
        }

        static Mathematics.Vector3d Direction(double pitch, double heading)
        {
            double p = pitch * MathD.DegToRad, h = heading * MathD.DegToRad;
            return new Mathematics.Vector3d(Math.Cos(p), Math.Sin(p) * Math.Cos(h), Math.Sin(p) * Math.Sin(h));
        }
    }
}
