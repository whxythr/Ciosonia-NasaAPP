using System.Collections.Generic;
using SpaceSim.Localization;
using SpaceSim.Multiplayer;

namespace SpaceSim.Runtime.Multiplayer
{
    /// <summary>
    /// The team mission in progress, fixed when the host pressed «Начать миссию»: which mission, who holds which role and which
    /// roles are this computer's. Gameplay asks <see cref="Has"/> to decide what the local player may do; outside a team
    /// mission (<see cref="Active"/> false) the single player holds every role.
    /// </summary>
    public static class CrewSession
    {
        public sealed class Seat
        {
            public int Id;
            public string Name;
            public List<CrewRole> Roles = new List<CrewRole>();
        }

        public static bool Active { get; private set; }
        public static CrewLink Link { get; private set; }
        public static string ScenarioId { get; private set; }
        public static string RoomCode { get; private set; }
        public static IReadOnlyList<Seat> Crew => crew;
        /// <summary>Roles this computer plays (on a shared screen: all of them).</summary>
        public static IReadOnlyList<CrewRole> LocalRoles => localRoles;

        static readonly List<Seat> crew = new List<Seat>();
        static readonly List<CrewRole> localRoles = new List<CrewRole>();

        /// <summary>Freezes the lobby as the mission's crew.</summary>
        public static void Begin(ICrewNetwork network, string scenarioId)
        {
            crew.Clear();
            localRoles.Clear();
            CrewLobbyState s = network.State;
            foreach (CrewMember m in s.Members)
                crew.Add(new Seat { Id = m.Id, Name = m.Name, Roles = s.RolesOf(m.Id) });
            Link = network.Link;
            ScenarioId = scenarioId;
            RoomCode = network.RoomCode;
            if (network.Link == CrewLink.SameScreen) localRoles.AddRange(CrewRoles.All);
            else localRoles.AddRange(s.RolesOf(network.LocalMemberId));
            Active = true;
        }

        public static void End()
        {
            Active = false;
            crew.Clear();
            localRoles.Clear();
            ScenarioId = RoomCode = null;
        }

        /// <summary>True when this computer plays the role (always true outside a team mission).</summary>
        public static bool Has(CrewRole role) => !Active || localRoles.Contains(role);

        /// <summary>Who holds the role, or null.</summary>
        public static string HolderOf(CrewRole role)
        {
            foreach (Seat seat in crew)
                if (seat.Roles.Contains(role)) return seat.Name;
            return null;
        }

        /// <summary>«КАПИТАН + ИНЖЕНЕР» — the local roles for the HUD and the loading screen.</summary>
        public static string LocalRolesText()
        {
            var names = new List<string>();
            foreach (CrewRole r in localRoles) names.Add(CrewRoles.Info(r).Title);
            return names.Count == 0 ? Loc.T("НАБЛЮДАТЕЛЬ") : string.Join(" + ", names);
        }
    }
}
