using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SpaceSim.Mathematics;

namespace SpaceSim.Multiplayer
{
    /// <summary>
    /// The robot operator's robot and rover, sent to the other computers of a team mission several times a second. The robot
    /// operator's computer is the authority over them (the pilot's over the flight, <see cref="CrewFlightSnapshot"/>).
    /// The others walk a copy of the robot with the same input and put it on the sent point when it drifts
    /// (<see cref="CrewRobotSync"/>); while the rover drives, the shared rocket stands where the rover is. Pure, no Unity.
    /// Wire form: «r1;…» invariant-culture numbers, the collected sample ids last, separated by «|».
    /// </summary>
    public sealed class CrewRobotSnapshot
    {
        public const string Version = "r1";

        public int Sequence;
        /// <summary>Navigation index of the body the robot is on.</summary>
        public int Frame;
        /// <summary>The robot is outside (otherwise only the rover part counts).</summary>
        public bool Eva;
        /// <summary>Planet-fixed feet position and velocity of the robot [m, m/s].</summary>
        public Vector3d Position, Velocity;
        /// <summary>Heading of the robot [degrees, 0 = north, 90 = east].</summary>
        public double Yaw;
        /// <summary>Walking input of the last step: forward and right in −1…1, sprint.</summary>
        public double Forward, Right;
        public bool Sprint;
        /// <summary>Jumps started so far (a new value makes the copy jump).</summary>
        public int Jumps;
        /// <summary>The rocket drives as a rover on the robot operator's computer.</summary>
        public bool RoverActive;
        /// <summary>Planet-fixed position of the landed rocket on the robot operator's computer [m].</summary>
        public Vector3d RoverFixed;
        /// <summary>Sample sites the robot operator has taken (ids).</summary>
        public string[] Collected = new string[0];

        public string Encode()
        {
            var sb = new StringBuilder(256);
            sb.Append(Version);
            Add(sb, Sequence);
            Add(sb, Frame);
            Add(sb, Eva);
            Add(sb, Position);
            Add(sb, Velocity);
            Add(sb, Yaw);
            Add(sb, Forward);
            Add(sb, Right);
            Add(sb, Sprint);
            Add(sb, Jumps);
            Add(sb, RoverActive);
            Add(sb, RoverFixed);
            var ids = new List<string>();
            if (Collected != null)
                foreach (string id in Collected)
                    if (ValidId(id)) ids.Add(id);
            sb.Append(';').Append(string.Join("|", ids));
            return sb.ToString();
        }

        /// <summary>An id that can travel in the line (no separators, not empty, not huge).</summary>
        public static bool ValidId(string id) =>
            !string.IsNullOrEmpty(id) && id.Length <= 128 && id.IndexOf(';') < 0 && id.IndexOf('|') < 0;

        public static CrewRobotSnapshot Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            string[] f = text.Split(';');
            if (f.Length != 20 || f[0] != Version) return null; // version + 18 numbers + the id list
            int i = 1;
            try
            {
                var s = new CrewRobotSnapshot
                {
                    Sequence = Int(f[i++]),
                    Frame = Int(f[i++]),
                    Eva = Bool(f[i++]),
                    Position = new Vector3d(Double(f[i++]), Double(f[i++]), Double(f[i++])),
                    Velocity = new Vector3d(Double(f[i++]), Double(f[i++]), Double(f[i++])),
                    Yaw = Double(f[i++]),
                    Forward = Clamp1(Double(f[i++])),
                    Right = Clamp1(Double(f[i++])),
                    Sprint = Bool(f[i++]),
                    Jumps = Int(f[i++]),
                    RoverActive = Bool(f[i++]),
                    RoverFixed = new Vector3d(Double(f[i++]), Double(f[i++]), Double(f[i++])),
                };
                string list = f[i];
                s.Collected = list.Length == 0 ? new string[0] : list.Split('|');
                foreach (string id in s.Collected)
                    if (!ValidId(id)) return null;
                return s;
            }
            catch (FormatException)
            {
                return null;
            }
        }

        static double Clamp1(double v) => Math.Max(-1.0, Math.Min(1.0, v));

        static int Int(string s)
        {
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) throw new FormatException();
            return v;
        }

        static double Double(string s)
        {
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) || double.IsNaN(v) || double.IsInfinity(v))
                throw new FormatException();
            return v;
        }

        static bool Bool(string s)
        {
            if (s == "1") return true;
            if (s == "0") return false;
            throw new FormatException();
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
    }

    /// <summary>How the others keep their copy of the robot and the rover in step with the robot operator's. Pure.</summary>
    public static class CrewRobotSync
    {
        /// <summary>Position error of the walking copy always tolerated [m].</summary>
        public const double RobotToleranceMin = 0.5;
        /// <summary>A robot snapshot older than this [real s] no longer says what the robot does (the operator dropped out).</summary>
        public const double StaleAfter = 1.5;
        /// <summary>The rover's landed point may be this far off before the rocket is moved [m].</summary>
        public const double RoverTolerance = 1.0;

        /// <summary>
        /// Error the copy lets pass: a minimum plus the way the robot covers during the network delay (its input already walks the
        /// copy that way, so only the rest is drift).
        /// </summary>
        public static double RobotTolerance(double speed, double latencySeconds) =>
            RobotToleranceMin + Math.Abs(speed) * Math.Max(0.0, latencySeconds);

        /// <summary>True when the copy of the robot should be put on the sent point.</summary>
        public static bool RobotNeedsPlace(CrewRobotSnapshot s, Vector3d copyPosition, double latencySeconds) =>
            (s.Position - copyPosition).Magnitude > RobotTolerance(s.Velocity.Magnitude, latencySeconds);

        /// <summary>True when the shared rocket should be moved to where the operator's rover stands.</summary>
        public static bool RoverNeedsMove(CrewRobotSnapshot s, Vector3d localLandedFixed) =>
            s.RoverActive && (s.RoverFixed - localLandedFixed).Magnitude > RoverTolerance;

        /// <summary>True when the copy should start a jump (the operator's jump counter moved on).</summary>
        public static bool StartsJump(int seenJumps, CrewRobotSnapshot s) => s.Jumps != seenJumps;
    }
}
