using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SpaceSim.Multiplayer
{
    /// <summary>One player in the team-mission lobby. <see cref="Id"/> is the network id (Photon actor number, or the local seat).</summary>
    public sealed class CrewMember
    {
        public CrewMember(int id, string name, bool isHost)
        {
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? "#" + id : name.Trim();
            IsHost = isHost;
        }

        public int Id { get; }
        public string Name { get; }
        public bool IsHost { get; internal set; }
        public bool Ready { get; internal set; }
    }

    /// <summary>Why the lobby cannot start yet, in the order the players have to fix it.</summary>
    public enum CrewLobbyIssue
    {
        None,
        /// <summary>Fewer than <see cref="CrewLobbyState.MinPlayers"/> players in the room.</summary>
        NotEnoughPlayers,
        /// <summary>A role has no owner: with fewer than four players somebody must take two (or more) roles.</summary>
        RolesUncovered,
        /// <summary>A player has no role at all (the others took everything).</summary>
        MemberWithoutRole,
        /// <summary>Somebody has not pressed «Готов».</summary>
        NotReady
    }

    /// <summary>
    /// The team-mission lobby, independent of the network layer: who is in the room, which role each player holds and who is
    /// ready. Rules: 2–4 players; each role has at most one owner; one player may hold several roles; the mission starts only
    /// when all four roles are covered, every player holds at least one role and everybody is ready.
    /// Changing a player's roles clears that player's «Готов», so nobody is signed up for duties they have not seen.
    /// The roles and ready flags travel as one compact string (<see cref="Encode"/> / <see cref="ApplyEncoded"/>), which the
    /// Photon room keeps as a custom property; the member list itself comes from the room's players.
    /// </summary>
    public sealed class CrewLobbyState
    {
        public const int MinPlayers = 2, MaxPlayers = 4;
        public const int NoOwner = -1;

        readonly List<CrewMember> members = new List<CrewMember>();
        readonly int[] owners = { NoOwner, NoOwner, NoOwner, NoOwner };

        public IReadOnlyList<CrewMember> Members => members;
        public int Count => members.Count;
        public bool IsFull => members.Count >= MaxPlayers;

        public CrewMember Find(int id) => members.Find(m => m.Id == id);

        public CrewMember Host => members.Find(m => m.IsHost);

        /// <summary>Owner's id or <see cref="NoOwner"/>.</summary>
        public int OwnerOf(CrewRole role) => owners[(int)role];

        public bool IsCovered(CrewRole role) => owners[(int)role] != NoOwner;

        public List<CrewRole> RolesOf(int memberId)
        {
            var list = new List<CrewRole>();
            foreach (CrewRole r in CrewRoles.All)
                if (owners[(int)r] == memberId) list.Add(r);
            return list;
        }

        public List<CrewRole> UncoveredRoles()
        {
            var list = new List<CrewRole>();
            foreach (CrewRole r in CrewRoles.All)
                if (owners[(int)r] == NoOwner) list.Add(r);
            return list;
        }

        // ─────────────────────────────── membership ───────────────────────────────

        /// <summary>Adds a player; false when the room is full or the id is taken. The first member becomes host if none is set.</summary>
        public bool AddMember(int id, string name, bool isHost = false)
        {
            if (IsFull || Find(id) != null) return false;
            if (isHost) foreach (CrewMember m in members) m.IsHost = false;
            members.Add(new CrewMember(id, name, isHost || members.Count == 0 && Host == null));
            return true;
        }

        /// <summary>Removes a player and frees their roles. If the host left, the next player in join order hosts.</summary>
        public bool RemoveMember(int id)
        {
            CrewMember m = Find(id);
            if (m == null) return false;
            members.Remove(m);
            for (int i = 0; i < owners.Length; i++)
                if (owners[i] == id) owners[i] = NoOwner;
            if (m.IsHost && members.Count > 0) members[0].IsHost = true;
            return true;
        }

        public void SetHost(int id)
        {
            if (Find(id) == null) return;
            foreach (CrewMember m in members) m.IsHost = m.Id == id;
        }

        // ─────────────────────────────── roles ───────────────────────────────

        /// <summary>Takes a free role (or keeps one already held). False when another player owns it or the member is unknown.</summary>
        public bool Claim(int memberId, CrewRole role)
        {
            CrewMember m = Find(memberId);
            if (m == null) return false;
            int owner = owners[(int)role];
            if (owner == memberId) return true;
            if (owner != NoOwner) return false;
            owners[(int)role] = memberId;
            m.Ready = false;
            return true;
        }

        /// <summary>Gives up a role the member holds.</summary>
        public bool Release(int memberId, CrewRole role)
        {
            if (owners[(int)role] != memberId) return false;
            owners[(int)role] = NoOwner;
            CrewMember m = Find(memberId);
            if (m != null) m.Ready = false;
            return true;
        }

        /// <summary>Claim when free, release when held by this member; false when another player owns the role.</summary>
        public bool Toggle(int memberId, CrewRole role) =>
            owners[(int)role] == memberId ? Release(memberId, role) : Claim(memberId, role);

        /// <summary>
        /// Hands every free role to the players: first to anyone without a role, then to whoever holds the fewest (ties: join
        /// order). Returns how many roles were assigned. Does nothing with an empty room.
        /// </summary>
        public int AssignUncovered()
        {
            if (members.Count == 0) return 0;
            int assigned = 0;
            foreach (CrewRole r in CrewRoles.All)
            {
                if (owners[(int)r] != NoOwner) continue;
                CrewMember best = null;
                int bestCount = int.MaxValue;
                foreach (CrewMember m in members)
                {
                    int c = RolesOf(m.Id).Count;
                    if (c < bestCount)
                    {
                        best = m;
                        bestCount = c;
                    }
                }
                owners[(int)r] = best.Id;
                best.Ready = false;
                assigned++;
            }
            return assigned;
        }

        // ─────────────────────────────── ready / start ───────────────────────────────

        public void SetReady(int memberId, bool ready)
        {
            CrewMember m = Find(memberId);
            if (m != null) m.Ready = ready;
        }

        public bool AllReady
        {
            get
            {
                if (members.Count == 0) return false;
                foreach (CrewMember m in members)
                    if (!m.Ready) return false;
                return true;
            }
        }

        public int ReadyCount
        {
            get
            {
                int n = 0;
                foreach (CrewMember m in members)
                    if (m.Ready) n++;
                return n;
            }
        }

        /// <summary>The first thing that keeps the mission from starting, or <see cref="CrewLobbyIssue.None"/>.</summary>
        public CrewLobbyIssue Check()
        {
            if (members.Count < MinPlayers) return CrewLobbyIssue.NotEnoughPlayers;
            if (UncoveredRoles().Count > 0) return CrewLobbyIssue.RolesUncovered;
            foreach (CrewMember m in members)
                if (RolesOf(m.Id).Count == 0) return CrewLobbyIssue.MemberWithoutRole;
            if (!AllReady) return CrewLobbyIssue.NotReady;
            return CrewLobbyIssue.None;
        }

        public bool CanStart => Check() == CrewLobbyIssue.None;

        // ─────────────────────────────── wire format ───────────────────────────────

        /// <summary>
        /// Roles and ready flags as text: <c>r=OWNER,OWNER,OWNER,OWNER;ok=ID,ID</c> (owners in <see cref="CrewRole"/> order,
        /// −1 = free). Names and the member list are not included: the network layer owns those.
        /// </summary>
        public string Encode()
        {
            var sb = new StringBuilder("r=");
            for (int i = 0; i < owners.Length; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(owners[i].ToString(CultureInfo.InvariantCulture));
            }
            sb.Append(";ok=");
            bool first = true;
            foreach (CrewMember m in members)
            {
                if (!m.Ready) continue;
                if (!first) sb.Append(',');
                sb.Append(m.Id.ToString(CultureInfo.InvariantCulture));
                first = false;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Replaces roles and ready flags with an <see cref="Encode"/>d string. Ids of players no longer in the room are dropped
        /// (their roles become free). Malformed parts are ignored; null or empty clears everything.
        /// </summary>
        public void ApplyEncoded(string encoded)
        {
            for (int i = 0; i < owners.Length; i++) owners[i] = NoOwner;
            foreach (CrewMember m in members) m.Ready = false;
            if (string.IsNullOrEmpty(encoded)) return;
            foreach (string part in encoded.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;
                string key = part.Substring(0, eq), value = part.Substring(eq + 1);
                string[] items = value.Length == 0 ? Array.Empty<string>() : value.Split(',');
                if (key == "r")
                {
                    for (int i = 0; i < owners.Length && i < items.Length; i++)
                        if (int.TryParse(items[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) && Find(id) != null)
                            owners[i] = id;
                }
                else if (key == "ok")
                {
                    foreach (string item in items)
                        if (int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                            SetReady(id, true);
                }
            }
        }

        /// <summary>Deep copy (members, roles, ready flags): the network layer edits a copy and publishes it.</summary>
        public CrewLobbyState Clone()
        {
            var c = new CrewLobbyState();
            foreach (CrewMember m in members)
            {
                var copy = new CrewMember(m.Id, m.Name, m.IsHost) { Ready = m.Ready };
                c.members.Add(copy);
            }
            Array.Copy(owners, c.owners, owners.Length);
            return c;
        }
    }
}
