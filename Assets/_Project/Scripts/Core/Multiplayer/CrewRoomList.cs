using System;
using System.Collections.Generic;

namespace SpaceSim.Multiplayer
{
    /// <summary>One open (public) crew room as the lobby list shows it.</summary>
    public sealed class CrewRoomInfo
    {
        /// <summary>The room code (= Photon room name), joined like a typed code.</summary>
        public string Code;
        /// <summary>Nickname of the player who made the room.</summary>
        public string Host;
        /// <summary>The research mission the room has picked (scenario id).</summary>
        public string MissionId;
        public int Players;
        public int MaxPlayers = CrewLobbyState.MaxPlayers;
        /// <summary>Still taking players (false once the mission started).</summary>
        public bool Open = true;
    }

    /// <summary>
    /// The public rooms a player may join from the lobby list: open, not full, with a valid code; the fullest first (they start
    /// soonest), then by code so the list does not jump around between updates. Pure, no Unity.
    /// </summary>
    public static class CrewRoomList
    {
        /// <summary>Rows the lobby shows at most.</summary>
        public const int MaxShown = 8;

        public static List<CrewRoomInfo> Joinable(IEnumerable<CrewRoomInfo> rooms, int max = MaxShown)
        {
            var list = new List<CrewRoomInfo>();
            if (rooms == null) return list;
            foreach (CrewRoomInfo r in rooms)
            {
                if (r == null || !r.Open || r.Players <= 0) continue;
                int cap = r.MaxPlayers > 0 ? Math.Min(r.MaxPlayers, CrewLobbyState.MaxPlayers) : CrewLobbyState.MaxPlayers;
                if (r.Players >= cap) continue;
                if (CrewRoomCode.Normalize(r.Code) != r.Code) continue; // not one of ours
                list.Add(r);
            }
            list.Sort((a, b) =>
            {
                int byPlayers = b.Players.CompareTo(a.Players);
                return byPlayers != 0 ? byPlayers : string.CompareOrdinal(a.Code, b.Code);
            });
            if (max >= 0 && list.Count > max) list.RemoveRange(max, list.Count - max);
            return list;
        }

        /// <summary>«2/4» for the list row.</summary>
        public static string Seats(CrewRoomInfo r) =>
            r.Players + "/" + (r.MaxPlayers > 0 ? Math.Min(r.MaxPlayers, CrewLobbyState.MaxPlayers) : CrewLobbyState.MaxPlayers);
    }
}
