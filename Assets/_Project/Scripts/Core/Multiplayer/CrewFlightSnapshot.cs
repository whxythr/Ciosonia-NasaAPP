using System;
using System.Globalization;
using System.Text;
using SpaceSim.Mathematics;

namespace SpaceSim.Multiplayer
{
    /// <summary>
    /// One look at the pilot's rocket, sent to the other computers of a team mission several times a second.
    /// The pilot's computer is the authority over the flight and the clock; everybody else mirrors it
    /// (<see cref="CrewFlightSync"/> decides when to jump the clock and when to snap the rocket). Pure, no Unity.
    /// Wire form: one line «v1;…» of invariant-culture numbers (round-trip precision), see <see cref="Encode"/>.
    /// </summary>
    public sealed class CrewFlightSnapshot
    {
        public const string Version = "v1";

        /// <summary>Grows with every snapshot of one pilot (an older or repeated one is dropped).</summary>
        public int Sequence;
        /// <summary>Simulation time of the pilot when the snapshot was taken [s].</summary>
        public double Time;
        /// <summary>Time warp the pilot asked for (what the others set).</summary>
        public double Warp;
        /// <summary>Simulated seconds per real second the pilot's clock actually advanced (for predicting the clock).</summary>
        public double Rate;
        public bool Paused;

        /// <summary>Navigation index of the body whose frame <see cref="Position"/>/<see cref="Velocity"/> are in.</summary>
        public int Frame;
        public bool Landed;
        public Vector3d Position, Velocity;
        /// <summary>Landed only: the planet-fixed position of the rocket [m].</summary>
        public Vector3d LandedFixed;

        /// <summary>How many times the pilot's rocket was put back on the pad (new flight or new design).</summary>
        public int Resets;

        /// <summary>Stages of the pilot's rocket (the rocket part is applied only to a rocket with the same count).</summary>
        public int StageCount;
        public int StageIndex;
        public bool EngineOn;
        public double Throttle, Pitch, Heading;
        public double[] Propellant = new double[0];

        public bool Legs, Rcs, Parachutes;

        public string Encode()
        {
            var sb = new StringBuilder(256);
            sb.Append(Version);
            Add(sb, Sequence);
            Add(sb, Time);
            Add(sb, Warp);
            Add(sb, Rate);
            Add(sb, Paused);
            Add(sb, Frame);
            Add(sb, Landed);
            Add(sb, Position);
            Add(sb, Velocity);
            Add(sb, LandedFixed);
            Add(sb, Resets);
            Add(sb, StageCount);
            Add(sb, StageIndex);
            Add(sb, EngineOn);
            Add(sb, Throttle);
            Add(sb, Pitch);
            Add(sb, Heading);
            Add(sb, Legs);
            Add(sb, Rcs);
            Add(sb, Parachutes);
            double[] p = Propellant ?? new double[0];
            Add(sb, p.Length);
            foreach (double v in p) Add(sb, v);
            return sb.ToString();
        }

        /// <summary>Parses <see cref="Encode"/>; null for anything malformed, of another version or with non-finite numbers.</summary>
        public static CrewFlightSnapshot Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] f = text.Split(';');
            if (f.Length < 28 || f[0] != Version) return null;
            var r = new Reader(f);
            try
            {
                var s = new CrewFlightSnapshot
                {
                    Sequence = r.Int(),
                    Time = r.Double(),
                    Warp = r.Double(),
                    Rate = r.Double(),
                    Paused = r.Bool(),
                    Frame = r.Int(),
                    Landed = r.Bool(),
                    Position = r.Vector(),
                    Velocity = r.Vector(),
                    LandedFixed = r.Vector(),
                    Resets = r.Int(),
                    StageCount = r.Int(),
                    StageIndex = r.Int(),
                    EngineOn = r.Bool(),
                    Throttle = r.Double(),
                    Pitch = r.Double(),
                    Heading = r.Double(),
                    Legs = r.Bool(),
                    Rcs = r.Bool(),
                    Parachutes = r.Bool(),
                };
                int n = r.Int();
                if (n < 0 || n > 64 || r.Left != n) return null;
                s.Propellant = new double[n];
                for (int i = 0; i < n; i++) s.Propellant[i] = r.Double();
                if (s.StageCount < 0 || s.StageIndex < 0 || s.Warp < 0.0 || s.Rate < 0.0) return null;
                return s;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        static void Add(StringBuilder sb, int v) => sb.Append(';').Append(v.ToString(CultureInfo.InvariantCulture));
        static void Add(StringBuilder sb, bool v) => sb.Append(';').Append(v ? '1' : '0');
        static void Add(StringBuilder sb, double v) => sb.Append(';').Append(v.ToString("R", CultureInfo.InvariantCulture));

        static void Add(StringBuilder sb, Vector3d v)
        {
            Add(sb, v.x);
            Add(sb, v.y);
            Add(sb, v.z);
        }

        sealed class Reader
        {
            readonly string[] f;
            int i = 1;

            public Reader(string[] fields) => f = fields;

            public int Left => f.Length - i;

            string Next()
            {
                if (i >= f.Length) throw new FormatException();
                return f[i++];
            }

            public int Int()
            {
                if (!int.TryParse(Next(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) throw new FormatException();
                return v;
            }

            public double Double()
            {
                if (!double.TryParse(Next(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ||
                    double.IsNaN(v) || double.IsInfinity(v)) throw new FormatException();
                return v;
            }

            public bool Bool()
            {
                string s = Next();
                if (s == "1") return true;
                if (s == "0") return false;
                throw new FormatException();
            }

            public Vector3d Vector() => new Vector3d(Double(), Double(), Double());
        }
    }

    /// <summary>
    /// How a follower's computer keeps up with the pilot's snapshots: when to jump its clock, and when its own rocket has
    /// drifted far enough from the pilot's to be put where the pilot's is. Small differences are left alone, because both
    /// computers run the same physics with the same controls and a snap every snapshot would make the view stutter. Pure.
    /// </summary>
    public static class CrewFlightSync
    {
        /// <summary>Real seconds of clock difference tolerated before the follower jumps to the pilot's time.</summary>
        public const double ClockToleranceReal = 0.5;
        /// <summary>Always tolerated clock difference [simulated s] (network jitter at ×1).</summary>
        public const double ClockToleranceMin = 0.25;
        /// <summary>Position error always tolerated [m].</summary>
        public const double PositionToleranceMin = 2.0;
        /// <summary>Above this time difference [s] the straight-line extrapolation is not trusted: no snapping then.</summary>
        public const double MaxExtrapolation = 2.0;
        /// <summary>Planet-fixed distance a landed follower may be off before it is moved [m].</summary>
        public const double LandedTolerance = 3.0;

        /// <summary>The pilot's clock now: the snapshot time plus what has run since it was sent.</summary>
        public static double PredictedTime(CrewFlightSnapshot s, double latencySeconds) =>
            s.Paused ? s.Time : s.Time + Math.Max(0.0, latencySeconds) * Math.Max(0.0, s.Rate);

        /// <summary>True when the follower's clock is so far off that it must jump to <see cref="PredictedTime"/>.</summary>
        public static bool ClockNeedsJump(double localTime, double predictedRemoteTime, double remoteWarp)
        {
            double tolerance = Math.Max(ClockToleranceMin, ClockToleranceReal * Math.Max(1.0, remoteWarp));
            return Math.Abs(predictedRemoteTime - localTime) > tolerance;
        }

        /// <summary>
        /// Where the pilot's rocket is at the follower's time <paramref name="localTime"/> (straight line from the snapshot);
        /// false when the times are too far apart to say.
        /// </summary>
        public static bool Extrapolate(CrewFlightSnapshot s, double localTime, out Vector3d position)
        {
            double dt = localTime - s.Time;
            position = s.Position + s.Velocity * dt;
            return Math.Abs(dt) <= MaxExtrapolation;
        }

        /// <summary>
        /// Position error the follower lets pass at this speed and time difference: a minimum, plus 1 % of the distance flown
        /// in the gap and the curvature a 10 m/s² acceleration adds over it (the extrapolation is a straight line).
        /// </summary>
        public static double PositionTolerance(double speed, double dt)
        {
            dt = Math.Abs(dt);
            return PositionToleranceMin + 0.01 * Math.Abs(speed) * dt + 0.5 * 10.0 * dt * dt;
        }

        /// <summary>True when the follower's rocket in flight should be put on the pilot's state.</summary>
        public static bool FlightNeedsSnap(CrewFlightSnapshot s, Vector3d localPosition, double localTime)
        {
            if (!Extrapolate(s, localTime, out Vector3d expected)) return false;
            double error = (expected - localPosition).Magnitude;
            return error > PositionTolerance(s.Velocity.Magnitude, localTime - s.Time);
        }

        /// <summary>True when a landed follower stands too far from where the pilot's rocket stands.</summary>
        public static bool LandedNeedsMove(CrewFlightSnapshot s, Vector3d localFixed) =>
            (s.LandedFixed - localFixed).Magnitude > LandedTolerance;
    }
}
