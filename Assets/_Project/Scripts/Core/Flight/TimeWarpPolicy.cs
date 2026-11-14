using System;

namespace SpaceSim.Flight
{
    /// <summary>Where the vehicle is, for the purpose of the allowed time warp.</summary>
    public enum TimeWarpZone
    {
        /// <summary>On the ground or in the lower half of the drag/terrain zone: take-off and landing.</summary>
        Surface,
        /// <summary>Upper half of the drag/terrain zone.</summary>
        UpperAtmosphere,
        /// <summary>Above the zone with the engine running.</summary>
        PoweredFlight,
        /// <summary>Above the zone, engine off, the trajectory comes back into the zone, closer than one body radius.</summary>
        Suborbital,
        /// <summary>The same trajectory, farther than one body radius.</summary>
        HighArc,
        /// <summary>Safe trajectory closer than one body radius.</summary>
        LowOrbit,
        /// <summary>Safe trajectory farther than one body radius.</summary>
        HighOrbit,
        /// <summary>Coasting around the star, outside every sphere of influence.</summary>
        Interplanetary,
        /// <summary>A low-thrust engine (electric) runs on a safe trajectory: the orbit changes slowly, warp up to <see cref="TimeWarpPolicy.LowThrustWarp"/>.</summary>
        LowThrustFlight
    }

    /// <summary>The flight state the time-warp limit depends on, measured from the body the vehicle currently orbits.</summary>
    public struct TimeWarpSituation
    {
        public bool Landed;
        public bool EngineBurning;
        /// <summary>The primary is the star: the distances below are not used.</summary>
        public bool Interplanetary;
        /// <summary>Height above the body's reference radius [m].</summary>
        public double Altitude;
        /// <summary>Radius of the body [m].</summary>
        public double BodyRadius;
        /// <summary>Height below which drag and terrain matter (<see cref="SpaceSim.Physics.NavBody.ZoneRadius"/> − radius) [m].</summary>
        public double ZoneAltitude;
        /// <summary>The coasting trajectory never enters the zone again.</summary>
        public bool SafeTrajectory;
        /// <summary>Acceleration of the running engine [m/s²] (0 = off or not known: never counted as low thrust).</summary>
        public double ThrustAcceleration;
    }

    public readonly struct TimeWarpLimit
    {
        public TimeWarpLimit(TimeWarpZone zone, double maxWarp)
        {
            Zone = zone;
            MaxWarp = maxWarp;
        }

        public TimeWarpZone Zone { get; }
        public double MaxWarp { get; }
    }

    /// <summary>
    /// How much time may be accelerated at a given place: the closer to the ground, the less. The limits are a game rule and do
    /// not change the physics (up to ×1000 motion is integrated step by step, above it the vehicle follows exact Kepler arcs).
    /// </summary>
    public static class TimeWarpPolicy
    {
        public const double SurfaceWarp = 4.0;
        public const double UpperAtmosphereWarp = 16.0;
        public const double PoweredWarp = 100.0;
        public const double SuborbitalWarp = 100.0;
        public const double HighArcWarp = 1000.0;
        public const double LowOrbitWarp = 100000.0;
        public const double HighOrbitWarp = 1000000.0;
        public const double InterplanetaryWarp = 1000000.0;
        /// <summary>Warp for a low-thrust burn on a safe trajectory or around the star (the integrator keeps sub-steps on the orbit's time scale).</summary>
        public const double LowThrustWarp = 10000.0;
        /// <summary>An engine at or below this acceleration counts as low thrust [m/s²] (≈ 1 mg₀: electric thrusters).</summary>
        public const double LowThrustAcceleration = 0.01;

        /// <summary>The running engine is weak enough for <see cref="LowThrustWarp"/>.</summary>
        public static bool IsLowThrust(in TimeWarpSituation s) =>
            s.EngineBurning && s.ThrustAcceleration > 0.0 && s.ThrustAcceleration <= LowThrustAcceleration;

        /// <summary>Fraction of the zone height that separates «взлёт/посадка» from the upper atmosphere.</summary>
        public const double LowerZoneFraction = 0.5;

        public static TimeWarpLimit Evaluate(in TimeWarpSituation s)
        {
            if (s.Landed) return new TimeWarpLimit(TimeWarpZone.Surface, SurfaceWarp);
            if (s.Interplanetary)
            {
                if (IsLowThrust(s)) return new TimeWarpLimit(TimeWarpZone.LowThrustFlight, LowThrustWarp);
                return s.EngineBurning
                    ? new TimeWarpLimit(TimeWarpZone.PoweredFlight, PoweredWarp)
                    : new TimeWarpLimit(TimeWarpZone.Interplanetary, InterplanetaryWarp);
            }
            if (s.Altitude < s.ZoneAltitude * LowerZoneFraction) return new TimeWarpLimit(TimeWarpZone.Surface, SurfaceWarp);
            if (s.Altitude < s.ZoneAltitude) return new TimeWarpLimit(TimeWarpZone.UpperAtmosphere, UpperAtmosphereWarp);
            if (IsLowThrust(s) && s.SafeTrajectory) return new TimeWarpLimit(TimeWarpZone.LowThrustFlight, LowThrustWarp);
            if (s.EngineBurning) return new TimeWarpLimit(TimeWarpZone.PoweredFlight, PoweredWarp);
            bool close = s.Altitude < s.BodyRadius;
            if (s.SafeTrajectory)
            {
                return close
                    ? new TimeWarpLimit(TimeWarpZone.LowOrbit, LowOrbitWarp)
                    : new TimeWarpLimit(TimeWarpZone.HighOrbit, HighOrbitWarp);
            }
            return close
                ? new TimeWarpLimit(TimeWarpZone.Suborbital, SuborbitalWarp)
                : new TimeWarpLimit(TimeWarpZone.HighArc, HighArcWarp);
        }

        /// <summary>
        /// Slider position (0..1) → factor, on a logarithmic scale from ×1 to <paramref name="max"/>. Values are rounded to whole
        /// numbers up to ×20 and to two significant digits above; the right end is exactly <paramref name="max"/>.
        /// </summary>
        public static double SliderToWarp(double position, double max)
        {
            if (!(max > 1.0) || position <= 0.0) return 1.0;
            if (position >= 1.0) return max;
            return Math.Max(1.0, Math.Min(max, RoundNice(Math.Pow(max, position))));
        }

        /// <summary>Factor → slider position (0..1); the inverse of <see cref="SliderToWarp"/> before rounding.</summary>
        public static double WarpToSlider(double warp, double max)
        {
            if (!(max > 1.0) || warp <= 1.0) return 0.0;
            return Math.Min(1.0, Math.Log(warp) / Math.Log(max));
        }

        static double RoundNice(double x)
        {
            if (x < 20.0) return Math.Round(x);
            double unit = Math.Pow(10.0, Math.Floor(Math.Log10(x)) - 1.0);
            return Math.Round(x / unit) * unit;
        }
    }
}
