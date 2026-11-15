using System;
using SpaceSim.Localization;
using SpaceSim.Multiplayer;

namespace SpaceSim.Runtime.Multiplayer
{
    /// <summary>
    /// «На одном экране»: the whole crew sits at one computer. Seats are added with <see cref="AddSeat"/>; clicks on a role go to
    /// the <see cref="ActiveSeat"/>. Everyone at the screen counts as ready (they can see the same lobby), so the start only
    /// waits for the roles.
    /// </summary>
    public sealed class LocalCrewNetwork : ICrewNetwork
    {
        readonly CrewLobbyState state = new CrewLobbyState();
        int nextId = 1;
        string scenarioId;

        public CrewLink Link => CrewLink.SameScreen;
        public CrewNetStatus Status { get; private set; } = CrewNetStatus.Idle;
        public string StatusText => Status == CrewNetStatus.InRoom ? Loc.T("Игроки за одним компьютером") : Loc.T("Не подключено");
        public string RoomCode => null;
        public string ScenarioId => scenarioId;
        public int LocalMemberId => ActiveSeat;
        public bool IsHost => true;
        public CrewLobbyState State => state;

        /// <summary>The seat whose turn it is to pick roles.</summary>
        public int ActiveSeat { get; private set; }

        public event Action Changed;
        public event Action<string> MissionStarted;

        string hostName;

        /// <summary>Only names the first seat (the others are «Игрок 2…4»).</summary>
        public void Connect(string nickname) => hostName = nickname;

        public void CreateRoom(string scenario)
        {
            scenarioId = scenario;
            if (state.Count == 0)
            {
                ActiveSeat = nextId;
                state.AddMember(nextId++, string.IsNullOrWhiteSpace(hostName) ? Loc.T("Игрок") + " 1" : hostName, true);
                AddSeat();
                ActiveSeat = 1;
            }
            Status = CrewNetStatus.InRoom;
            Raise();
        }

        public void JoinRoom(string code) => CreateRoom(scenarioId);

        public void Leave()
        {
            Status = CrewNetStatus.Idle;
            Raise();
        }

        /// <summary>Adds a seat («Игрок N») and makes it active. False when four players already sit at the screen.</summary>
        public bool AddSeat()
        {
            int id = nextId;
            if (!state.AddMember(id, Loc.T("Игрок") + " " + (state.Count + 1))) return false;
            nextId++;
            ActiveSeat = id;
            Raise();
            return true;
        }

        public void RemoveSeat(int id)
        {
            if (state.Count <= 1 || !state.RemoveMember(id)) return;
            if (ActiveSeat == id) ActiveSeat = state.Members[0].Id;
            Raise();
        }

        public void SelectSeat(int id)
        {
            if (state.Find(id) == null) return;
            ActiveSeat = id;
            Raise();
        }

        public void ToggleRole(CrewRole role)
        {
            if (state.Toggle(ActiveSeat, role)) Raise();
        }

        public void SetReady(bool ready) => Raise();

        public void AssignUncovered()
        {
            if (state.AssignUncovered() > 0) Raise();
        }

        public void SetScenario(string scenario)
        {
            scenarioId = scenario;
            Raise();
        }

        public void StartMission()
        {
            MarkAllReady();
            if (!state.CanStart) return;
            MissionStarted?.Invoke(scenarioId);
        }

        void MarkAllReady()
        {
            foreach (CrewMember m in state.Members) state.SetReady(m.Id, true);
        }

        void Raise()
        {
            MarkAllReady();
            Changed?.Invoke();
        }

        public void Dispose()
        {
            Changed = null;
            MissionStarted = null;
        }
    }
}
