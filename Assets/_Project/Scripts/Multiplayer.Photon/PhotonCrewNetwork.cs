#if PHOTON_UNITY_NETWORKING
using System;
using System.Collections.Generic;
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
    /// Setup: import PUN 2, put the App ID into PhotonServerSettings. A room lives in one region, so all players must be in the
    /// same one: the lobby picks it (<see cref="CrewRegion"/>, EU or US; never «best region», which could put two players in
    /// different regions), and a code not found in the chosen region is looked up in the other one before giving up.
    /// A Fixed Region in the settings overrides the choice.
    /// </summary>
    public sealed class PhotonCrewNetwork : MonoBehaviourPunCallbacks, ICrewNetwork, IOnEventCallback
    {
        const string KeyCrew = "crew", KeyMission = "mission", KeyStart = "start", KeyRocket = "rocket", KeyHost = "host";
        /// <summary>Photon event code of a crew chat line (content: the text; sent only to the one recipient).</summary>
        const byte ChatEvent = 41;
        /// <summary>Photon event code of a flight snapshot from the pilot (content: <see cref="CrewFlightSnapshot.Encode"/>).</summary>
        const byte FlightEvent = 42;
        /// <summary>Photon event code of a robot snapshot from the robot operator (content: <see cref="CrewRobotSnapshot.Encode"/>).</summary>
        const byte RobotEvent = 43;

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
        string region = CrewRegion.Default;
        /// <summary>Set when the connection is dropped on purpose to move to another region: OnDisconnected connects again.</summary>
        bool reconnect;
        /// <summary>The code being looked up, and whether the other region was already tried for it.</summary>
        string searchCode;
        bool searchedOther;
        /// <summary>The pending room is to be listed for everyone.</summary>
        bool pendingCreatePublic;
        /// <summary>Every room the region's lobby has told us about, by name (Photon sends changes only).</summary>
        readonly Dictionary<string, CrewRoomInfo> listed = new Dictionary<string, CrewRoomInfo>();
        List<CrewRoomInfo> joinable = new List<CrewRoomInfo>();

        public IReadOnlyList<CrewRoomInfo> PublicRooms => joinable;
        public bool ListingRooms => PhotonNetwork.InLobby;
        public bool RoomIsPublic => PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom.IsVisible;

        public CrewLink Link => CrewLink.Online;
        public CrewNetStatus Status { get; private set; } = CrewNetStatus.Idle;
        public string StatusText => statusText;
        public string RoomCode => PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.Name : null;
        public string ScenarioId => RoomString(KeyMission);
        public int LocalMemberId => PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
        public bool IsHost => PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient;
        public CrewLobbyState State => state;
        public string Region => region;

        public void SetRegion(string newRegion)
        {
            string r = CrewRegion.Normalize(newRegion);
            if (r == region) return;
            if (PhotonNetwork.InRoom || Status == CrewNetStatus.JoiningRoom) return; // a room stays in its region
            region = r;
            searchedOther = false;
            if (!PhotonNetwork.IsConnected)
            {
                Changed?.Invoke();
                return;
            }
            SetStatus(CrewNetStatus.Connecting, Loc.T("Переход в регион ") + CrewRegion.Label(region) + "…");
            reconnect = true;
            PhotonNetwork.Disconnect();
        }

        public event Action Changed;
        public event Action<string> MissionStarted;
        public event Action<int, string> ChatReceived;
        public event Action<int, string> FlightReceived;
        public event Action<int, string> RobotReceived;

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
            // A copy of the settings with the lobby's region as the fixed region: ConnectUsingSettings takes the App ID from it
            // (the obsolete ConnectToRegion does not, and the name server answers «Invalid application id»). The copy keeps the
            // settings asset untouched (writing it from play mode would change the project file).
            bool ok;
            if (string.IsNullOrEmpty(settings.FixedRegion))
            {
                AppSettings chosen = settings.CopyTo(new AppSettings());
                chosen.FixedRegion = region;
                ok = PhotonNetwork.ConnectUsingSettings(chosen);
            }
            else ok = PhotonNetwork.ConnectUsingSettings();
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
                CreateRoom(s, pendingCreatePublic);
            }
            else if (pendingJoinCode != null)
            {
                string c = pendingJoinCode;
                pendingJoinCode = null;
                TryJoin(c);
            }
            // Nothing to do yet: listen to the region's lobby for the public rooms (also after leaving a room).
            else if (!PhotonNetwork.InLobby) PhotonNetwork.JoinLobby();
        }

        // ─────────────────────────────── public room list ───────────────────────────────

        public override void OnJoinedLobby()
        {
            ClearList();
            Changed?.Invoke();
            // A click that came while the lobby was still being joined (the client was not ready then) runs now.
            if (pendingCreateScenario != null)
            {
                string s = pendingCreateScenario;
                pendingCreateScenario = null;
                CreateRoom(s, pendingCreatePublic);
            }
            else if (pendingJoinCode != null)
            {
                string c = pendingJoinCode;
                pendingJoinCode = null;
                TryJoin(c);
            }
        }

        public override void OnLeftLobby()
        {
            ClearList();
            Changed?.Invoke();
        }

        /// <summary>Photon sends only what changed: rooms that are new, updated or gone (RemovedFromList).</summary>
        public override void OnRoomListUpdate(List<RoomInfo> roomList)
        {
            foreach (RoomInfo r in roomList)
            {
                if (r.RemovedFromList)
                {
                    listed.Remove(r.Name);
                    continue;
                }
                listed[r.Name] = new CrewRoomInfo
                {
                    Code = r.Name,
                    Players = r.PlayerCount,
                    MaxPlayers = r.MaxPlayers,
                    Open = r.IsOpen && r.IsVisible,
                    Host = r.CustomProperties.TryGetValue(KeyHost, out object h) ? h as string : null,
                    MissionId = r.CustomProperties.TryGetValue(KeyMission, out object m) ? m as string : null,
                };
            }
            joinable = CrewRoomList.Joinable(listed.Values);
            Changed?.Invoke();
        }

        void ClearList()
        {
            listed.Clear();
            joinable = new List<CrewRoomInfo>();
        }

        public void SetRoomPublic(bool isPublic)
        {
            if (!IsHost || started || PhotonNetwork.CurrentRoom.IsVisible == isPublic) return;
            PhotonNetwork.CurrentRoom.IsVisible = isPublic;
            Changed?.Invoke();
        }

        public override void OnDisconnected(DisconnectCause cause)
        {
            state = new CrewLobbyState();
            ClearList();
            if (disposed) return;
            if (reconnect)
            {
                // A region change (the lobby switch or a room search): straight on to the new region.
                reconnect = false;
                Connect(PhotonNetwork.NickName);
                return;
            }
            if (cause == DisconnectCause.InvalidAuthentication)
            {
                // The App ID is unknown to Photon or belongs to another product (Fusion, Chat, Voice…): PUN needs a «PUN» app.
                SetStatus(CrewNetStatus.Failed, Loc.T("Photon отклонил App ID: нужен App ID приложения типа PUN (dashboard.photonengine.com), скопированный целиком."));
                return;
            }
            if (cause == DisconnectCause.MaxCcuReached)
            {
                SetStatus(CrewNetStatus.Failed, Loc.T("Photon: достигнут лимит одновременных игроков бесплатного тарифа."));
                return;
            }
            SetStatus(cause == DisconnectCause.DisconnectByClientLogic ? CrewNetStatus.Idle : CrewNetStatus.Failed,
                cause == DisconnectCause.DisconnectByClientLogic ? Loc.T("Отключено") : Loc.T("Связь с Photon потеряна: ") + cause);
        }

        // ─────────────────────────────── rooms ───────────────────────────────

        public void CreateRoom(string scenarioId, bool isPublic = false)
        {
            pendingCreatePublic = isPublic;
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
                IsVisible = isPublic, // public: in the region's lobby list; private: joined by code only
                IsOpen = true,
                CustomRoomProperties = new Hashtable
                {
                    { KeyCrew, "" }, { KeyMission, scenarioId ?? "" }, { KeyStart, "" }, { KeyHost, PhotonNetwork.NickName ?? "" },
                },
                // What the lobby list shows of a public room (other properties stay inside the room).
                CustomRoomPropertiesForLobby = new[] { KeyMission, KeyHost },
            };
            SetStatus(CrewNetStatus.JoiningRoom, (isPublic ? Loc.T("Создание открытой комнаты ") : Loc.T("Создание комнаты ")) + code + "…");
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
                CreateRoom(s, pendingCreatePublic);
                return;
            }
            pendingCreateScenario = null;
            SetStatus(CrewNetStatus.Failed, Loc.T("Не удалось создать комнату: ") + message);
        }

        public void JoinRoom(string code)
        {
            searchedOther = false;
            TryJoin(code);
        }

        void TryJoin(string code)
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
            searchCode = room;
            SetStatus(CrewNetStatus.JoiningRoom, Loc.T("Вход в комнату ") + room + " · " + CrewRegion.Label(region) + "…");
            if (!PhotonNetwork.JoinRoom(room))
                SetStatus(CrewNetStatus.Failed, Loc.T("Photon: не удалось войти в комнату"));
        }

        public override void OnJoinRoomFailed(short returnCode, string message)
        {
            var settings = PhotonNetwork.PhotonServerSettings != null ? PhotonNetwork.PhotonServerSettings.AppSettings : null;
            bool fixedRegion = settings != null && !string.IsNullOrEmpty(settings.FixedRegion);
            if (returnCode == ErrorCode.GameDoesNotExist && !searchedOther && !fixedRegion && searchCode != null)
            {
                // The host may have made the room in the other region: look there once before giving up.
                searchedOther = true;
                string from = region;
                region = CrewRegion.Other(region);
                pendingJoinCode = searchCode;
                pendingCreateScenario = null;
                SetStatus(CrewNetStatus.JoiningRoom, Loc.T("Комната не найдена в ") + CrewRegion.Label(from) + Loc.T(" — ищем в ") +
                                                     CrewRegion.Label(region) + "…");
                reconnect = true;
                PhotonNetwork.Disconnect();
                return;
            }
            string why = returnCode == ErrorCode.GameFull ? Loc.T("в комнате уже 4 игрока")
                : returnCode == ErrorCode.GameClosed ? Loc.T("миссия уже началась")
                : returnCode == ErrorCode.GameDoesNotExist ? (searchedOther ? Loc.T("комнаты нет ни в EU, ни в US") : Loc.T("комната не найдена"))
                : message;
            SetStatus(CrewNetStatus.Connected, Loc.T("Не удалось войти: ") + why);
        }

        public override void OnJoinedRoom()
        {
            pendingCreateScenario = null;
            createAttempts = 0;
            SetStatus(CrewNetStatus.InRoom, Loc.T("В комнате ") + PhotonNetwork.CurrentRoom.Name + " · " + CrewRegion.Label(region));
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
        public override void OnMasterClientSwitched(Player newMasterClient)
        {
            // The new host's name is what the public list shows from now on.
            if (newMasterClient != null && newMasterClient.IsLocal && PhotonNetwork.InRoom)
                PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { KeyHost, PhotonNetwork.NickName ?? "" } });
            Rebuild();
        }
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
            PhotonNetwork.CurrentRoom.IsVisible = false; // and it leaves the public list
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
            if (disposed || photonEvent.Sender <= 0 || !(photonEvent.CustomData is string text)) return;
            if (photonEvent.Code == ChatEvent) ChatReceived?.Invoke(photonEvent.Sender, text);
            else if (photonEvent.Code == FlightEvent) FlightReceived?.Invoke(photonEvent.Sender, text);
            else if (photonEvent.Code == RobotEvent) RobotReceived?.Invoke(photonEvent.Sender, text);
        }

        public void SendRobot(string snapshot)
        {
            if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(snapshot)) return;
            var options = new RaiseEventOptions { Receivers = ReceiverGroup.Others };
            PhotonNetwork.RaiseEvent(RobotEvent, snapshot, options, SendOptions.SendUnreliable);
        }

        // ─────────────────────────────── mission sync ───────────────────────────────

        public void SendFlight(string snapshot)
        {
            if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(snapshot)) return;
            var options = new RaiseEventOptions { Receivers = ReceiverGroup.Others };
            PhotonNetwork.RaiseEvent(FlightEvent, snapshot, options, SendOptions.SendUnreliable);
        }

        public string SharedDesign
        {
            get
            {
                string s = RoomString(KeyRocket);
                return string.IsNullOrEmpty(s) ? null : s;
            }
        }

        public void PublishDesign(string blueprintJson)
        {
            if (!PhotonNetwork.InRoom || string.IsNullOrEmpty(blueprintJson)) return;
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { KeyRocket, blueprintJson } });
        }

        public double LatencySeconds => PhotonNetwork.IsConnected ? PhotonNetwork.GetPing() * 0.0005 : 0.0;

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
            FlightReceived = null;
            RobotReceived = null;
            if (PhotonNetwork.IsConnected) PhotonNetwork.Disconnect();
            if (this != null) Destroy(gameObject);
        }
    }
}
#endif
