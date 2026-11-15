using System;
using SpaceSim.Multiplayer;

namespace SpaceSim.Runtime.Multiplayer
{
    /// <summary>How the crew plays together.</summary>
    public enum CrewLink
    {
        /// <summary>Photon room, one player per computer, joined by a room code.</summary>
        Online
    }

    public enum CrewNetStatus
    {
        Idle,
        Connecting,
        /// <summary>Connected to the Photon master server, not in a room yet.</summary>
        Connected,
        JoiningRoom,
        InRoom,
        Failed
    }

    /// <summary>
    /// The team-mission lobby as the menu sees it, carried by Photon.
    /// Every change arrives through <see cref="Changed"/> (on the main thread); <see cref="MissionStarted"/> fires on every
    /// computer of the room when the host starts the mission.
    /// </summary>
    public interface ICrewNetwork : IDisposable
    {
        CrewLink Link { get; }
        CrewNetStatus Status { get; }
        /// <summary>Human-readable status for the lobby (already localised).</summary>
        string StatusText { get; }
        string RoomCode { get; }
        /// <summary>The research mission the team chose (<see cref="Menu.MenuCatalog.Scenario.Id"/>).</summary>
        string ScenarioId { get; }
        /// <summary>The player of this computer.</summary>
        int LocalMemberId { get; }
        bool IsHost { get; }
        CrewLobbyState State { get; }

        event Action Changed;
        event Action<string> MissionStarted;
        /// <summary>A chat line arrived from another player: (sender id, text). Raised only for lines addressed to this computer.</summary>
        event Action<int, string> ChatReceived;

        void Connect(string nickname);
        void CreateRoom(string scenarioId);
        void JoinRoom(string code);
        void Leave();

        /// <summary>Take the role when free, give it up when it is ours.</summary>
        void ToggleRole(CrewRole role);
        void SetReady(bool ready);
        /// <summary>Host: hand every free role to the players with the fewest roles.</summary>
        void AssignUncovered();
        /// <summary>Host: pick the mission (clears everyone's «Готов»).</summary>
        void SetScenario(string scenarioId);
        /// <summary>Host: start when <see cref="CrewLobbyState.CanStart"/>.</summary>
        void StartMission();
        /// <summary>Sends a chat line to one player (the rule «only with the captain» is checked by <see cref="CrewChat"/>).</summary>
        void SendChat(int toMemberId, string text);
    }

    /// <summary>
    /// Owns the one lobby connection of the game. The Photon layer (assembly SpaceSim.Multiplayer.Photon, compiled only when
    /// PUN 2 is imported) registers itself here at start-up; without it the team mission is unavailable.
    /// </summary>
    public static class CrewNetwork
    {
        static Func<ICrewNetwork> onlineFactory;

        /// <summary>Called by the Photon assembly at start-up.</summary>
        public static void RegisterOnline(Func<ICrewNetwork> factory) => onlineFactory = factory;

        public static bool OnlineAvailable => onlineFactory != null;

        /// <summary>The open lobby, or null. Survives scene loads (the Photon room stays open during the mission).</summary>
        public static ICrewNetwork Current { get; private set; }

        /// <summary>Opens the Photon lobby; null when Photon PUN 2 is not installed.</summary>
        public static ICrewNetwork Open()
        {
            Close();
            Current = onlineFactory?.Invoke();
            return Current;
        }

        public static void Close()
        {
            ICrewNetwork n = Current;
            Current = null;
            n?.Dispose();
        }
    }
}
