using System;
using SpaceSim.Multiplayer;

namespace SpaceSim.Runtime.Multiplayer
{
    /// <summary>How the crew plays together.</summary>
    public enum CrewLink
    {
        /// <summary>Everybody at one computer: seats are added by hand, no network.</summary>
        SameScreen,
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
    /// The team-mission lobby as the menu sees it, whatever carries it (Photon or one shared screen).
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
        /// <summary>The player whose clicks this computer makes (the active seat on a shared screen).</summary>
        int LocalMemberId { get; }
        bool IsHost { get; }
        CrewLobbyState State { get; }

        event Action Changed;
        event Action<string> MissionStarted;

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
    }

    /// <summary>
    /// Owns the one lobby connection of the game. The Photon layer (assembly SpaceSim.Multiplayer.Photon, compiled only when
    /// PUN 2 is imported) registers itself here at start-up; without it only <see cref="CrewLink.SameScreen"/> is offered.
    /// </summary>
    public static class CrewNetwork
    {
        static Func<ICrewNetwork> onlineFactory;

        /// <summary>Called by the Photon assembly at start-up.</summary>
        public static void RegisterOnline(Func<ICrewNetwork> factory) => onlineFactory = factory;

        public static bool OnlineAvailable => onlineFactory != null;

        /// <summary>The open lobby, or null. Survives scene loads (the Photon room stays open during the mission).</summary>
        public static ICrewNetwork Current { get; private set; }

        public static ICrewNetwork Open(CrewLink link)
        {
            Close();
            Current = link == CrewLink.Online && onlineFactory != null ? onlineFactory() : new LocalCrewNetwork();
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
