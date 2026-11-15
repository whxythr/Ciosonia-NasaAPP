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
        /// <summary>Roles this computer plays.</summary>
        public static IReadOnlyList<CrewRole> LocalRoles => localRoles;

        /// <summary>The player of this computer.</summary>
        public static int LocalId { get; private set; } = -1;
        /// <summary>Online team mission only: the chat with the captain (see <see cref="CrewChat"/>); null otherwise.</summary>
        public static CrewChat Chat { get; private set; }

        static readonly List<Seat> crew = new List<Seat>();
        static readonly List<CrewRole> localRoles = new List<CrewRole>();
        static ICrewNetwork network;

        /// <summary>Freezes the lobby as the mission's crew.</summary>
        public static void Begin(ICrewNetwork net, string scenarioId)
        {
            End();
            network = net;
            localRoles.Clear();
            CrewLobbyState s = network.State;
            foreach (CrewMember m in s.Members)
                crew.Add(new Seat { Id = m.Id, Name = m.Name, Roles = s.RolesOf(m.Id) });
            LocalId = network.LocalMemberId;
            Link = network.Link;
            ScenarioId = scenarioId;
            RoomCode = network.RoomCode;
            localRoles.AddRange(s.RolesOf(network.LocalMemberId));
            Active = true;

            // Crew chat: everyone talks only with the captain.
            int captain = HolderIdOf(CrewRole.Captain);
            if (network.Link == CrewLink.Online && captain >= 0)
            {
                var ids = new List<int>();
                foreach (Seat seat in crew) ids.Add(seat.Id);
                Chat = new CrewChat(LocalId, captain, ids);
                network.ChatReceived += OnChatReceived;
                CrewChatPanel.Ensure();
            }
        }

        public static void End()
        {
            if (network != null) network.ChatReceived -= OnChatReceived;
            network = null;
            Chat = null;
            LocalId = -1;
            CrewChatPanel.Remove();
            Active = false;
            crew.Clear();
            localRoles.Clear();
            ScenarioId = RoomCode = null;
        }

        /// <summary>True when this computer plays the role (always true outside a team mission).</summary>
        public static bool Has(CrewRole role) => !Active || localRoles.Contains(role);

        static void OnChatReceived(int fromId, string text) => Chat?.Post(fromId, LocalId, text);

        /// <summary>
        /// Writes a line into a chat thread (the captain: to that thread's player; anybody else: to the captain).
        /// False when there is nothing to send or the rule forbids it.
        /// </summary>
        public static bool SendChat(int channel, string text)
        {
            if (Chat == null || network == null) return false;
            string clean = CrewChat.Clean(text);
            int to = Chat.RecipientOf(channel);
            if (clean == null || !Chat.Allowed(LocalId, to)) return false;
            network.SendChat(to, clean);
            Chat.Post(LocalId, to, clean);
            return true;
        }

        /// <summary>Still in the room (an online player can drop out during the mission).</summary>
        public static bool IsConnected(int id) => network == null || network.State.Find(id) != null;

        public static Seat SeatOf(int id)
        {
            foreach (Seat seat in crew)
                if (seat.Id == id) return seat;
            return null;
        }

        /// <summary>«ИНЖЕНЕР, ПИЛОТ» — the roles of one player, for the chat headers.</summary>
        public static string RolesText(int id)
        {
            Seat seat = SeatOf(id);
            if (seat == null || seat.Roles.Count == 0) return Loc.T("НАБЛЮДАТЕЛЬ");
            var names = new List<string>();
            foreach (CrewRole r in seat.Roles) names.Add(CrewRoles.Info(r).Title);
            return string.Join(", ", names);
        }

        /// <summary>Id of the player holding the role, or −1.</summary>
        public static int HolderIdOf(CrewRole role)
        {
            foreach (Seat seat in crew)
                if (seat.Roles.Contains(role)) return seat.Id;
            return -1;
        }

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
