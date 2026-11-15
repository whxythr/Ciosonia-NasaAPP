#if PHOTON_UNITY_NETWORKING
using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using SpaceSim.Localization;
using SpaceSim.Multiplayer;
using SpaceSim.Runtime.Multiplayer;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

namespace SpaceSim.Networking
{
    /// <summary>
    /// Team-mission lobby over Photon PUN 2. One private room per crew, named by its <see cref="CrewRoomCode"/>.
    /// The room keeps three custom properties:
    ///   «crew»    — roles and ready flags (<see cref="CrewLobbyState.Encode"/>), edited with check-and-swap so two players
    ///               grabbing the same role at once cannot both get it (the loser just sees the role taken);
    ///   «mission» — the research mission the team picked (host);
    ///   «start»   — set once by the host when the mission starts; every client loads the flight scene on seeing it.
    /// The member list is the room's player list (actor number = <see cref="CrewMember.Id"/>, master client = host).
    /// Setup: import PUN 2, put the App ID into PhotonServerSettings. All players must be in one region (<see cref="DefaultRegion"/>
    /// is used when the settings leave it to «best region», otherwise two players could land in different regions and miss
    /// each other's room).
    /// </summary>
    public sealed class PhotonCrewNetwork : MonoBehaviourPunCallbacks, ICrewNetwork, IOnEventCallback
    {
        const string KeyCrew = "crew", KeyMission = "mission", KeyStart = "start";
        /// <summary>Photon event code of a crew chat line (content: the text; sent only to the one recipient).</summary>
        const byte ChatEvent = 41;
        public const string DefaultRegion = "eu";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register() => CrewNetwork.RegisterOnline(Create);

        static ICrewNetwork Create()
        {
            var go = new GameObject("PhotonCrewNetwork");
            DontDestroyOnLoad(go);
            return go.AddComponent<PhotonCrewNetwork>();
        }

        CrewLobbyState state = new CrewLobbyState();
        readonly System.Random random = new System.Random();
        string statusText = "";
        string pendingCreateScenario, pendingJoinCode;
        bool started, disposed;
        int createAttempts;

        public CrewLink Link => CrewLink.Online;
        public CrewNetStatus Status { get; private set; } = CrewNetStatus.Idle;
        public string StatusText => statusText;
        public string RoomCode => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : null;
        public string ScenarioId => RoomString(KeyMission);
        public int LocalMemberId => PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
        public bool IsHost => PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;
        public CrewLobbyState State => state;

        public event Action Changed;
        public event Action<string> MissionStarted;
        public event Action<int, string> ChatReceived;

        // ─────────────────────────────── connection ───────────────────────────────

        public void Connect(string nickname)
        {
            PhotonNetwork.NickName = string.IsNullOrWhiteSpace(nickname) ? Loc.T("Игрок") : nickname.Trim();
            PhotonNetwork.AutomaticallySyncScene = false; // every client loads the flight scene itself, with its loading screen
            if (PhotonNetwork.IsConnectedAndReady)
            {
                SetStatus(CrewNetStatus.Connected, Loc.T("Подключено к Photon"));
                return;
            }
            if (PhotonNetwork.IsConnected) return; // still connecting
            var settings = PhotonNetwork.PhotonServerSettings != null ? PhotonNetwork.PhotonServerSettings.AppSettings : null;
            if (settings == null || string.IsNullOrEmpty(settings.AppIdRealtime))
            {
                SetStatus(CrewNetStatus.Failed, Loc.T("Photon: не задан App ID (Window ▸ Photon Unity Networking ▸ Highlight Server Settings)"));
                return;
            }
            SetStatus(CrewNetStatus.Connecting, Loc.T("Подключение к Photon…"));
            // Not written into the settings asset (that would change the project file from play mode).
            bool ok = string.IsNullOrEmpty(settings.FixedRegion)
                ? PhotonNetwork.ConnectToRegion(DefaultRegion)
                : PhotonNetwork.ConnectUsingSettings();
            if (!ok)
                SetStatus(CrewNetStatus.Failed, Loc.T("Photon: не удалось начать подключение"));
        }

        public override void OnConnectedToMaster()
        {
            SetStatus(CrewNetStatus.Connected, Loc.T("Подключено к Photon") + " · " + PhotonNetwork.CloudRegion);
            if (pendingCreateScenario != null)
            {
                string s = pendingCreateScenario;
                pendingCreateScenario = null;
                CreateRoom(s);
            }
            else if (pendingJoinCode != null)
            {
                string c = pendingJoinCode;
                pendingJoinCode = null;
                JoinRoom(c);
            }
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            state = new CrewLobbyState();
            if (disposed) return;
            SetStatus(cause == DisconnectCause.DisconnectByClientLogic ? CrewNetStatus.Idle : CrewNetStatus.Failed,
                cause == DisconnectCause.DisconnectByClientLogic ? Loc.T("Отключено") : Loc.T("Связь с Photon потеряна: ") + cause);
        }

        // ─────────────────────────────── rooms ───────────────────────────────

        public void CreateRoom(string scenarioId)
        {
            if (!PhotonNetwork.IsConnectedAndReady)
            {
                pendingCreateScenario = scenarioId;
                pendingJoinCode = null;
                if (Status != CrewNetStatus.Connecting) Connect(PhotonNetwork.NickName);
                return;
            }
            if (PhotonNetwork.InRoom) return;
            started = false;
            string code = CrewRoomCode.Generate(random);
            var options = new RoomOptions
            {
                MaxPlayers = CrewLobbyState.MaxPlayers,
                IsVisible = false, // joined by code only
                IsOpen = true,
                CustomRoomProperties = new Hashtable { { KeyCrew, "" }, { KeyMission, scenarioId ?? "" }, { KeyStart, "" } },
            };
            SetStatus(CrewNetStatus.JoiningRoom, Loc.T("Создание комнаты ") + code + "…");
            pendingCreateScenario = scenarioId;
            if (!PhotonNetwork.CreateRoom(code, options))
                SetStatus(CrewNetStatus.Failed, Loc.T("Photon: не удалось создать комнату"));
        }

        public override void OnCreateRoomFailed(short returnCode, string message)
        {
            // A code collision is retried with a new code a few times.
            if (returnCode == ErrorCode.GameIdAlreadyExists && createAttempts++ < 3 && pendingCreateScenario != null)
            {
                string s = pendingCreateScenario;
                pendingCreateScenario = null;
                CreateRoom(s);
                return;
            }
            pendingCreateScenario = null;
            SetStatus(CrewNetStatus.Failed, Loc.T("Не удалось создать комнату: ") + message);
        }

        public void JoinRoom(string code)
        {
            string room = CrewRoomCode.Normalize(code);
            if (room == null)
            {
                SetStatus(Status == CrewNetStatus.Failed ? CrewNetStatus.Idle : Status, Loc.T("Неверный код комнаты"));
                return;
            }
            if (!PhotonNetwork.IsConnectedAndReady)
            {
                pendingJoinCode = room;
                pendingCreateScenario = null;
                if (Status != CrewNetStatus.Connecting) Connect(PhotonNetwork.NickName);
                return;
            }
            if (PhotonNetwork.InRoom) return;
            started = false;
            SetStatus(CrewNetStatus.JoiningRoom, Loc.T("Вход в комнату ") + room + "…");
            if (!PhotonNetwork.JoinRoom(room))
                SetStatus(CrewNetStatus.Failed, Loc.T("Photon: не удалось войти в комнату"));
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            string why = returnCode == ErrorCode.GameFull ? Loc.T("в комнате уже 4 игрока")
                : returnCode == ErrorCode.GameClosed ? Loc.T("миссия уже началась")
                : returnCode == ErrorCode.GameDoesNotExist ? Loc.T("комната не найдена")
                : message;
            SetStatus(CrewNetStatus.Connected, Loc.T("Не удалось войти: ") + why);
        }

        public override void OnJoinedRoom()
        {
            pendingCreateScenario = null;
            createAttempts = 0;
            SetStatus(CrewNetStatus.InRoom, Loc.T("В комнате ") + PhotonNetwork.CurrentRoom.Name);
            Rebuild();
        }

        public void Leave()
        {
            pendingCreateScenario = pendingJoinCode = null;
            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
        }

        public override void OnLeftRoom()
        {
            state = new CrewLobbyState();
            started = false;
            if (disposed) return;
            SetStatus(PhotonNetwork.IsConnected ? CrewNetStatus.Connected : CrewNetStatus.Idle, Loc.T("Вы вышли из комнаты"));
        }

        public override void OnPlayerEnteredRoom(Player newPlayer) => Rebuild();
        public override void OnPlayerLeftRoom(Player otherPlayer) => Rebuild();
        public override void OnMasterClientSwitched(Player newMasterClient) => Rebuild();
        public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps) => Rebuild();

        public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
        {
            Rebuild();
            string start = RoomString(KeyStart);
            if (!started && !string.IsNullOrEmpty(start))
            {
                started = true;
                MissionStarted?.Invoke(start);
            }
        }

        // ─────────────────────────────── lobby actions ───────────────────────────────

        public void ToggleRole(CrewRole role) => Publish(s => s.Toggle(LocalMemberId, role));

        public void SetReady(bool ready) => Publish(s =>
        {
            s.SetReady(LocalMemberId, ready);
            return true;
        });

        public void AssignUncovered()
        {
            if (IsHost) Publish(s => s.AssignUncovered() > 0);
        }

        public void SetScenario(string scenarioId)
        {
            if (!IsHost || started) return;
            CrewLobbyState copy = state.Clone();
            foreach (CrewMember m in copy.Members) copy.SetReady(m.Id, false); // a new mission needs a new «Готов»
            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new Hashtable { { KeyMission, scenarioId ?? "" }, { KeyCrew, copy.Encode() } },
                new Hashtable { { KeyCrew, RoomString(KeyCrew) } });
        }

        public void StartMission()
        {
            if (!IsHost || started || !state.CanStart) return;
            string mission = ScenarioId;
            if (string.IsNullOrEmpty(mission)) return;
            PhotonNetwork.CurrentRoom.IsOpen = false; // nobody joins a mission in flight
            // Check-and-swap on «crew»: if somebody changed a role in the same instant, the start is refused and the lobby updates.
            PhotonNetwork.CurrentRoom.SetCustomProperties(
                new Hashtable { { KeyStart, mission } },
                new Hashtable { { KeyCrew, RoomString(KeyCrew) } });
        }

        /// <summary>Edits a copy of the lobby and publishes it with check-and-swap against the version it was made from.</summary>
        void Publish(Func<CrewLobbyState, bool> edit)
        {
            if (!PhotonNetwork.InRoom || started) return;
            CrewLobbyState copy = state.Clone();
            if (!edit(copy)) return;
            string before = RoomString(KeyCrew), after = copy.Encode();
            if (before == after) return;
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { KeyCrew, after } }, new Hashtable { { KeyCrew, before } });
        }

        // ─────────────────────────────── chat ───────────────────────────────

        public void SendChat(int toMemberId, string text)
        {
            if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(text) || toMemberId == LocalMemberId) return;
            // Addressed to one actor only, so the other players never receive the line at all.
            var options = new RaiseEventOptions { TargetActors = new[] { toMemberId } };
            PhotonNetwork.RaiseEvent(ChatEvent, text, options, SendOptions.SendReliable);
        }

        /// <summary>Photon events (registered by MonoBehaviourPunCallbacks.OnEnable). The sender is Photon's own, not trusted from the content.</summary>
        public void OnEvent(EventData photonEvent)
        {
            if (photonEvent.Code != ChatEvent || disposed) return;
            if (photonEvent.CustomData is string text && photonEvent.Sender > 0)
                ChatReceived?.Invoke(photonEvent.Sender, text);
        }

        // ─────────────────────────────── state ───────────────────────────────

        /// <summary>Re-reads the lobby from the room: players in actor order, master client as host, roles from «crew».</summary>
        void Rebuild()
        {
            var s = new CrewLobbyState();
            if (PhotonNetwork.InRoom)
            {
                Player[] players = PhotonNetwork.PlayerList; // sorted by actor number
                foreach (Player p in players) s.AddMember(p.ActorNumber, p.NickName, p.IsMasterClient);
                if (PhotonNetwork.MasterClient != null) s.SetHost(PhotonNetwork.MasterClient.ActorNumber);
                s.ApplyEncoded(RoomString(KeyCrew));
            }
            state = s;
            Changed?.Invoke();
        }

        static string RoomString(string key)
        {
            if (!PhotonNetwork.InRoom) return null;
            return PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(key, out object v) ? v as string : null;
        }

        void SetStatus(CrewNetStatus status, string text)
        {
            Status = status;
            statusText = text;
            Changed?.Invoke();
        }

        public void Dispose()
        {
            disposed = true;
            Changed = null;
            MissionStarted = null;
            ChatReceived = null;
            if (PhotonNetwork.IsConnected) PhotonNetwork.Disconnect();
            if (this != null) Destroy(gameObject);
        }
    }
}
#endif
