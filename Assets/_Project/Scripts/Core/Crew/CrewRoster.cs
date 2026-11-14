using System;
using System.Collections.Generic;
using SpaceSim.Economy;

namespace SpaceSim.Crew
{
    public enum CrewRole
    {
        Pilot,
        Engineer,
        Scientist
    }

    public enum CrewStatus
    {
        Available,
        /// <summary>Aboard the vehicle that is flying now.</summary>
        OnMission,
        /// <summary>Misses the next launch; comes back with a better main skill.</summary>
        Training,
        /// <summary>Not hired yet (can be hired for <see cref="CrewRoster.HireCost"/> credits).</summary>
        Applicant
    }

    /// <summary>
    /// One astronaut of the player's programme. Statistics (flights, time in space, EVAs, bodies) are recorded from real flights;
    /// the skills are a character sheet only — they do not change any physics (the simulation has one physics layer).
    /// </summary>
    [Serializable]
    public class CrewMember
    {
        public string id = "";
        /// <summary>Source-language (Russian) name; the UI passes it through Loc.T.</summary>
        public string name = "";
        public CrewRole role;
        public CrewStatus status;
        public int flights;
        /// <summary>Time above the space boundary of a body [s] (simulation time).</summary>
        public double secondsInSpace;
        public int evas;
        public List<string> bodiesVisited = new List<string>();
        /// <summary>Skills 0…100.</summary>
        public int piloting, engineering, composure, science;
        /// <summary>Completed training courses.</summary>
        public int trainings;
        /// <summary>Flight number this member is aboard (0 = none).</summary>
        public int missionFlight;

        /// <summary>The skill of the role (piloting / engineering / science).</summary>
        public int MainSkill => role == CrewRole.Pilot ? piloting : role == CrewRole.Engineer ? engineering : science;

        /// <summary>Level 0…5: experience from flights and training.</summary>
        public int Level => Math.Min(CrewRoster.MaxLevel, (flights + 2 * trainings + 1) / 2);
    }

    /// <summary>The roster saved in the player profile.</summary>
    [Serializable]
    public class CrewRosterData
    {
        public List<CrewMember> members = new List<CrewMember>();
        /// <summary>Number of the last launched flight (FLT n).</summary>
        public int flightCounter;
        /// <summary>Number of the flight that is in the air now (0 = none).</summary>
        public int activeFlight;
    }

    /// <summary>
    /// Rules of the crew roster (engine-free): the starting crew, who boards a launch, training, hiring and the per-flight
    /// statistics. Seats come from the crewed cabin of the design; a probe without one flies with nobody aboard.
    /// </summary>
    public static class CrewRoster
    {
        public const int MaxLevel = 5;
        public const int HireCost = 3000;

        /// <summary>What hiring costs this profile: <see cref="HireCost"/> in a Career, nothing in Sandbox.</summary>
        public static int HireCostFor(PlayerProfile profile) => profile != null && profile.IsCareer ? HireCost : 0;
        /// <summary>Main-skill gain of one finished training course.</summary>
        public const int TrainingGain = 6;

        static readonly (string name, CrewRole role, int p, int e, int c, int s, int flights)[] Starting =
        {
            ("Айгерим Садыкова", CrewRole.Pilot, 86, 42, 74, 30, 7),
            ("Данияр Омаров", CrewRole.Engineer, 38, 81, 60, 44, 4),
            ("Мира Ковальска", CrewRole.Scientist, 25, 46, 58, 84, 3),
            ("Тимур Беков", CrewRole.Pilot, 64, 30, 52, 22, 1),
            ("Лена Халворсен", CrewRole.Engineer, 31, 70, 66, 40, 2),
            ("Арман Исмаилов", CrewRole.Scientist, 18, 35, 49, 72, 0),
            ("София Рейес", CrewRole.Pilot, 91, 48, 80, 36, 9),
            ("Нурлан Абенов", CrewRole.Engineer, 42, 77, 55, 38, 2),
        };

        static readonly (string name, CrewRole role, int p, int e, int c, int s)[] Applicants =
        {
            ("Дана Жумабаева", CrewRole.Scientist, 20, 38, 61, 66),
            ("Ержан Касымов", CrewRole.Pilot, 58, 28, 47, 19),
            ("Ингрид Ланге", CrewRole.Engineer, 26, 63, 52, 33),
        };

        /// <summary>Fills an empty roster with the starting crew and applicants (an old profile gets them on load).</summary>
        public static bool EnsureRoster(CrewRosterData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.members == null) data.members = new List<CrewMember>();
            if (data.members.Count > 0) return false;
            int n = 0;
            foreach (var s in Starting)
                data.members.Add(new CrewMember
                {
                    id = "crew-" + (++n), name = s.name, role = s.role, status = CrewStatus.Available, flights = s.flights,
                    piloting = s.p, engineering = s.e, composure = s.c, science = s.s,
                });
            foreach (var a in Applicants)
                data.members.Add(new CrewMember
                {
                    id = "crew-" + (++n), name = a.name, role = a.role, status = CrewStatus.Applicant,
                    piloting = a.p, engineering = a.e, composure = a.c, science = a.s,
                });
            return true;
        }

        public static int Count(CrewRosterData data, CrewStatus status)
        {
            int n = 0;
            foreach (CrewMember m in data.members) if (m.status == status) n++;
            return n;
        }

        public static CrewMember Find(CrewRosterData data, string id) => data.members.Find(m => m.id == id);

        /// <summary>
        /// A new launch: training courses tick (a member in training misses this flight and finishes the course), then up to
        /// <paramref name="seats"/> available members board — pilots first, then engineers, then scientists, the most experienced
        /// first. Returns the flight number (also when nobody boards: a probe flight still counts).
        /// </summary>
        public static int BeginFlight(CrewRosterData data, int seats)
        {
            EndFlight(data); // a previous flight that never ended properly (crash, quit) releases its crew
            var graduates = new List<CrewMember>();
            foreach (CrewMember m in data.members)
            {
                if (m.status != CrewStatus.Training) continue;
                m.status = CrewStatus.Available;
                graduates.Add(m); // finishes the course during this launch: not aboard it
                m.trainings++;
                Raise(m, TrainingGain);
            }
            int flight = ++data.flightCounter;
            data.activeFlight = flight;
            if (seats <= 0) return flight;

            var candidates = data.members.FindAll(m => m.status == CrewStatus.Available && !graduates.Contains(m));
            candidates.Sort((a, b) =>
            {
                int r = ((int)a.role).CompareTo((int)b.role);
                if (r != 0) return r;
                int f = b.flights.CompareTo(a.flights);
                return f != 0 ? f : string.CompareOrdinal(a.id, b.id);
            });
            // One of each role first (a pilot, an engineer, a scientist), then the rest in the same order.
            var crew = new List<CrewMember>();
            foreach (CrewRole role in new[] { CrewRole.Pilot, CrewRole.Engineer, CrewRole.Scientist })
            {
                CrewMember pick = candidates.Find(m => m.role == role && !crew.Contains(m));
                if (pick != null && crew.Count < seats) crew.Add(pick);
            }
            foreach (CrewMember m in candidates)
                if (crew.Count < seats && !crew.Contains(m)) crew.Add(m);
            foreach (CrewMember m in crew)
            {
                m.status = CrewStatus.OnMission;
                m.missionFlight = flight;
                m.flights++;
            }
            return flight;
        }

        /// <summary>The flight is over (new rocket, recovery, loss): its crew is available again.</summary>
        public static void EndFlight(CrewRosterData data)
        {
            foreach (CrewMember m in data.members)
            {
                if (m.status != CrewStatus.OnMission) continue;
                m.status = CrewStatus.Available;
                m.missionFlight = 0;
            }
            data.activeFlight = 0;
        }

        public static List<CrewMember> Aboard(CrewRosterData data) => data.members.FindAll(m => m.status == CrewStatus.OnMission);

        /// <summary>Simulation time spent above the space boundary, added to everyone aboard.</summary>
        public static void AddTimeInSpace(CrewRosterData data, double seconds)
        {
            if (seconds <= 0.0 || double.IsNaN(seconds)) return;
            foreach (CrewMember m in data.members)
                if (m.status == CrewStatus.OnMission) m.secondsInSpace += seconds;
        }

        /// <summary>The crew aboard landed on (or walked on) a body.</summary>
        public static void VisitBody(CrewRosterData data, string bodyId)
        {
            if (string.IsNullOrEmpty(bodyId)) return;
            foreach (CrewMember m in data.members)
                if (m.status == CrewStatus.OnMission && !m.bodiesVisited.Contains(bodyId)) m.bodiesVisited.Add(bodyId);
        }

        /// <summary>One EVA (a walk outside) for the first crew member aboard; false with nobody aboard.</summary>
        public static bool RecordEva(CrewRosterData data)
        {
            CrewMember m = data.members.Find(x => x.status == CrewStatus.OnMission);
            if (m == null) return false;
            m.evas++;
            return true;
        }

        /// <summary>Only an available member can go to training (not while flying, not an applicant).</summary>
        public static bool SendToTraining(CrewRosterData data, string id)
        {
            CrewMember m = Find(data, id);
            if (m == null || m.status != CrewStatus.Available) return false;
            m.status = CrewStatus.Training;
            return true;
        }

        /// <summary>Hires an applicant for <see cref="HireCost"/> credits.</summary>
        public static bool Hire(PlayerProfile profile, CrewRosterData data, string id, out string reason)
        {
            CrewMember m = Find(data, id);
            if (m == null || m.status != CrewStatus.Applicant)
            {
                reason = "not an applicant";
                return false;
            }
            if (profile.IsCareer && profile.credits < HireCost)
            {
                reason = "not enough credits";
                return false;
            }
            if (profile.IsCareer) profile.credits -= HireCost; // Sandbox: no money checks
            m.status = CrewStatus.Available;
            reason = "";
            return true;
        }

        static void Raise(CrewMember m, int amount)
        {
            switch (m.role)
            {
                case CrewRole.Pilot: m.piloting = Math.Min(100, m.piloting + amount); break;
                case CrewRole.Engineer: m.engineering = Math.Min(100, m.engineering + amount); break;
                default: m.science = Math.Min(100, m.science + amount); break;
            }
            m.composure = Math.Min(100, m.composure + amount / 2);
        }
    }
}
