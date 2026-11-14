using System;
using System.Collections.Generic;

namespace SpaceSim.Vehicles
{
    /// <summary>How the inside of a crewed cabin is fitted out (chosen by its number of seats).</summary>
    public enum CockpitInterior
    {
        /// <summary>No interior: a remote-control cabin (probe core) has no one inside.</summary>
        None,
        /// <summary>One reclined seat in front of the panel.</summary>
        Single,
        /// <summary>Three seats side by side, the commander in the middle.</summary>
        Triple,
        /// <summary>Seven seats: three in the front row, four on a raised row behind.</summary>
        Seven
    }

    /// <summary>
    /// Rules of the cabin view: which cabin the player can sit in, how its interior is fitted out, and when zooming the outside
    /// camera carries the player inside. Engine-free (the Runtime only draws what these say). Visual rules only — they never touch
    /// the physics.
    /// </summary>
    public static class CabinRules
    {
        /// <summary>Wheel notches pushed "into" the rocket at the closest camera distance before the view goes inside.</summary>
        public const double PushNotchesToEnter = 1.5;
        /// <summary>A pause longer than this [s] between pushes starts the count again (one stray notch never carries you inside).</summary>
        public const double PushMemorySeconds = 1.2;
        /// <summary>The outside camera counts as "at its closest" within this factor of its minimum distance.</summary>
        public const double MinimumDistanceTolerance = 1.08;
        /// <summary>Leaving the cabin: the outside camera starts this many times its minimum distance away.</summary>
        public const double ExitDistanceFactor = 2.5;

        /// <summary>Head turn limits inside the helmet [deg]: yaw left/right, pitch down/up.</summary>
        public const double MaxHeadYaw = 110.0, MinHeadPitch = -55.0, MaxHeadPitch = 70.0;

        /// <summary>The cabin has seats someone can sit in (a crewed cabin with room for at least one person).</summary>
        public static bool CanEnter(ModuleDefinition cabin) =>
            cabin != null && cabin.IsCabin && cabin.cabinKind == CabinKind.Crewed && cabin.crewCapacity > 0;

        /// <summary>The interior fit-out for a cabin: by seat count (1 → Single, 2–3 → Triple, 4+ → Seven).</summary>
        public static CockpitInterior InteriorOf(ModuleDefinition cabin)
        {
            if (!CanEnter(cabin)) return CockpitInterior.None;
            if (cabin.crewCapacity <= 1) return CockpitInterior.Single;
            if (cabin.crewCapacity <= 3) return CockpitInterior.Triple;
            return CockpitInterior.Seven;
        }

        /// <summary>Seats per row, front row first (the pilot sits in the middle of the front row).</summary>
        public static int[] SeatRows(CockpitInterior interior)
        {
            switch (interior)
            {
                case CockpitInterior.Single: return new[] { 1 };
                case CockpitInterior.Triple: return new[] { 3 };
                case CockpitInterior.Seven: return new[] { 3, 4 };
                default: return new int[0];
            }
        }

        public static int SeatCount(CockpitInterior interior)
        {
            int n = 0;
            foreach (int row in SeatRows(interior)) n += row;
            return n;
        }

        /// <summary>
        /// The cabin the player sits in: the crewed one with the most seats (the first of equals). Null when the vehicle has only
        /// remote-control cabins — then the camera stops outside.
        /// </summary>
        public static PartPlacement CrewedCabin(IEnumerable<PartPlacement> parts)
        {
            if (parts == null) return null;
            PartPlacement best = null;
            int bestSeats = 0;
            foreach (PartPlacement p in parts)
            {
                if (p == null || !p.IsModule) continue;
                ModuleDefinition m = ModuleCatalog.Find(p.ModuleId);
                if (!CanEnter(m) || m.crewCapacity <= bestSeats) continue;
                best = p;
                bestSeats = m.crewCapacity;
            }
            return best;
        }

        /// <summary>
        /// Parts that would block the windows from inside the chosen cabin and are hidden while the player sits there: the cabin's
        /// own hull, the shields wrapped round it and the nose fairing over it.
        /// </summary>
        public static bool HiddenFromInside(PartPlacement part, PartPlacement cabin)
        {
            if (part == null || cabin == null) return false;
            if (ReferenceEquals(part, cabin)) return true;
            if (part.Role == PartRole.Fairing) return true;
            return part.Role == PartRole.Wrap && part.Bottom < cabin.Top && part.Top > cabin.Bottom;
        }

        /// <summary>Clamps a head turn to what a seated, suited person can do [deg].</summary>
        public static void ClampHead(ref double yaw, ref double pitch)
        {
            yaw = Math.Max(-MaxHeadYaw, Math.Min(MaxHeadYaw, yaw));
            pitch = Math.Max(MinHeadPitch, Math.Min(MaxHeadPitch, pitch));
        }

        /// <summary>
        /// Counts wheel pushes towards the rocket while the outside camera is already at its closest. Returns true once the pushes
        /// add up to <see cref="PushNotchesToEnter"/>; any push outwards, a move away from the minimum or a long pause resets it.
        /// </summary>
        public sealed class ZoomGate
        {
            double pushed, lastPush = double.NegativeInfinity;

            public double Pushed => pushed;

            /// <param name="atMinimum">The camera is (within tolerance) at its minimum distance.</param>
            /// <param name="notches">Wheel notches this frame, + = towards the rocket.</param>
            /// <param name="time">Real time [s].</param>
            public bool Update(bool atMinimum, double notches, double time)
            {
                if (!atMinimum || notches < 0.0)
                {
                    Reset();
                    return false;
                }
                if (notches <= 0.0) return false;
                if (time - lastPush > PushMemorySeconds) pushed = 0.0;
                lastPush = time;
                pushed += notches;
                if (pushed + 1e-9 < PushNotchesToEnter) return false;
                Reset();
                return true;
            }

            public void Reset()
            {
                pushed = 0.0;
                lastPush = double.NegativeInfinity;
            }
        }

        /// <summary>True when the camera distance counts as "at the minimum".</summary>
        public static bool AtMinimum(double distance, double minDistance) => distance <= minDistance * MinimumDistanceTolerance;

        // ---- Head motion from the real acceleration (visual only) ---------------------------------------------

        /// <summary>
        /// Load along the seat axis in g₀: what an accelerometer in the seat reads, + = pressed into the seat. On the ground it is the
        /// local gravity; in free fall 0; under thrust (thrust − drag)/g₀.
        /// </summary>
        public static double SeatLoad(bool landed, double localGravity, double thrustAcceleration, double dragAcceleration)
        {
            const double g0 = 9.80665;
            double a = landed ? localGravity : thrustAcceleration - dragAcceleration;
            return a / g0;
        }

        /// <summary>How far the head sinks into the seat cushion under load [m]: 1.2 cm per g, at most 6 cm; ±1 cm float in free fall.</summary>
        public static double HeadSag(double seatLoad)
        {
            if (double.IsNaN(seatLoad)) return 0.0;
            return Math.Max(-0.01, Math.Min(0.06, 0.012 * seatLoad));
        }

        /// <summary>
        /// Amplitude of the vibration felt in the seat [deg]: engine roughness grows with the thrust acceleration (√ of it, so a weak
        /// thruster barely hums), buffeting with the dynamic pressure q (strongest near max-q). Capped at 1.2°.
        /// </summary>
        public static double ShakeAmplitude(bool engineBurning, double thrustAcceleration, double dynamicPressure)
        {
            double engine = engineBurning ? 0.12 * Math.Sqrt(Math.Max(0.0, thrustAcceleration) / 9.80665) : 0.0;
            double buffet = 0.35 * Math.Max(0.0, dynamicPressure) / 30000.0;
            return Math.Min(1.2, engine + buffet);
        }
    }

    /// <summary>Radio link between the vehicle and the home ground station (visual and warnings only; control is never cut).</summary>
    public readonly struct CommLinkStatus
    {
        /// <summary>Speed of light in vacuum [m/s] (exact, SI definition).</summary>
        public const double SpeedOfLight = 299792458.0;
        /// <summary>Below this link margin (received / weakest usable power) the indicator warns that the link is weak.</summary>
        public const double WeakMargin = 2.0;

        /// <summary>Received signal relative to the weakest usable one, capped at 1 (<see cref="SpaceSim.Physics.PartPhysics.SignalStrength"/>): below 1 out of range.</summary>
        public readonly double Strength;
        /// <summary>Link margin: received power over the weakest usable one, (R/d)², not capped (+∞ at the station).</summary>
        public readonly double Margin;
        /// <summary>Distance to the ground station [m] and the vehicle's reach [m].</summary>
        public readonly double Distance, Range;
        /// <summary>One-way light time to the ground station [s] (a command and its reply take twice as long).</summary>
        public readonly double OneWayDelay;

        public CommLinkStatus(double range, double distance)
        {
            Range = Math.Max(0.0, range);
            Distance = Math.Max(0.0, distance);
            Strength = SpaceSim.Physics.PartPhysics.SignalStrength(Range, Distance);
            Margin = Range <= 0.0 ? 0.0 : Distance <= 0.0 ? double.PositiveInfinity : (Range / Distance) * (Range / Distance);
            OneWayDelay = Distance / SpeedOfLight;
        }

        public double RoundTripDelay => 2.0 * OneWayDelay;
        /// <summary>The station is within reach (signal at or above the weakest usable level).</summary>
        public bool Connected => Range > 0.0 && Distance <= Range;
        public bool Weak => Connected && Margin < WeakMargin;
        /// <summary>Signal bars 0..4 from the link margin: ≥100 → 4, ≥10 → 3, ≥2 → 2, ≥1 → 1, out of range → 0.</summary>
        public int Bars => !Connected ? 0 : Margin >= 100.0 ? 4 : Margin >= 10.0 ? 3 : Margin >= WeakMargin ? 2 : 1;

        /// <summary>
        /// Distance from the vehicle to a ground station on the surface of the home body [m]: centre distance minus the home radius,
        /// never below zero (on the home world the station is "here").
        /// </summary>
        public static double StationDistance(double distanceToHomeCentre, double homeRadius) => Math.Max(0.0, distanceToHomeCentre - Math.Max(0.0, homeRadius));
    }
}
