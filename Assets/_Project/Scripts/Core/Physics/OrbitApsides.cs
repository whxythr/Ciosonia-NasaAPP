using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Physics
{
    /// <summary>
    /// Periapsis and apoapsis of the two-body conic through a state (r, v) around a body with gravitational parameter μ:
    /// their distances, where they are relative to the body, and when the vehicle gets there.
    /// </summary>
    public readonly struct OrbitApsides
    {
        /// <summary>Below this eccentricity the orbit is treated as a circle: the line of apsides has no meaningful direction.</summary>
        public const double CircularEccentricity = 1e-6;

        OrbitApsides(double eccentricity, bool isBound, double periapsisRadius, double apoapsisRadius, double period, bool hasLine,
            Vector3d periapsisPosition, Vector3d apoapsisPosition, double timeToPeriapsis, double timeToApoapsis)
        {
            Eccentricity = eccentricity;
            IsBound = isBound;
            PeriapsisRadius = periapsisRadius;
            ApoapsisRadius = apoapsisRadius;
            Period = period;
            HasLine = hasLine;
            PeriapsisPosition = periapsisPosition;
            ApoapsisPosition = apoapsisPosition;
            TimeToPeriapsis = timeToPeriapsis;
            TimeToApoapsis = timeToApoapsis;
        }

        public double Eccentricity { get; }
        /// <summary>True for a closed orbit (specific energy &lt; 0).</summary>
        public bool IsBound { get; }
        /// <summary>Distance of the periapsis from the body's centre [m].</summary>
        public double PeriapsisRadius { get; }
        /// <summary>Distance of the apoapsis from the body's centre [m]; +∞ on an open trajectory.</summary>
        public double ApoapsisRadius { get; }
        /// <summary>Orbital period [s]; +∞ on an open trajectory.</summary>
        public double Period { get; }
        /// <summary>False for a circular orbit: the positions and times of the apsides are then undefined.</summary>
        public bool HasLine { get; }
        /// <summary>Periapsis point relative to the body's centre [m] (zero when <see cref="HasLine"/> is false).</summary>
        public Vector3d PeriapsisPosition { get; }
        /// <summary>Apoapsis point relative to the body's centre [m] (zero when open or circular).</summary>
        public Vector3d ApoapsisPosition { get; }
        /// <summary>Seconds until the next periapsis; +∞ if an open trajectory has already passed it, NaN for a circle.</summary>
        public double TimeToPeriapsis { get; }
        /// <summary>Seconds until the next apoapsis; +∞ on an open trajectory, NaN for a circle.</summary>
        public double TimeToApoapsis { get; }

        public static OrbitApsides FromState(double mu, Vector3d r, Vector3d v)
        {
            if (!(mu > 0.0)) throw new ArgumentOutOfRangeException(nameof(mu));
            double distance = r.Magnitude;
            if (!(distance > 0.0)) throw new ArgumentException("The state must not be at the body's centre.", nameof(r));

            ConicPropagator.Shape(mu, r, v, out double e, out double p, out double rp);
            bool bound = 0.5 * v.SqrMagnitude - mu / distance < 0.0 && e < 1.0;
            double ra = bound ? p / (1.0 - e) : double.PositiveInfinity;
            double period = double.PositiveInfinity;
            if (bound)
            {
                double a = 0.5 * (rp + ra);
                period = MathD.TwoPi * Math.Sqrt(a * a * a / mu);
            }

            if (!(e > CircularEccentricity))
                return new OrbitApsides(e, bound, rp, ra, period, false, Vector3d.Zero, Vector3d.Zero, double.NaN, double.NaN);

            Vector3d toPeriapsis = ConicPropagator.EccentricityVector(mu, r, v).Normalized;
            return new OrbitApsides(e, bound, rp, ra, period, true, toPeriapsis * rp, bound ? toPeriapsis * -ra : Vector3d.Zero,
                ConicPropagator.TimeToTrueAnomaly(mu, r, v, 0.0),
                bound ? ConicPropagator.TimeToTrueAnomaly(mu, r, v, Math.PI) : double.PositiveInfinity);
        }
    }
}
