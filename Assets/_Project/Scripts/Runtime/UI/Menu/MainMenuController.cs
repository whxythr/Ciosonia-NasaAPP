using System;
using System.Collections.Generic;
using SpaceSim.Economy;
using SpaceSim.Localization;
using SpaceSim.Units;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>
    /// HORIZON main menu (UI Toolkit). Builds every screen in code from <see cref="MenuCatalog"/> and styles it with
    /// Resources/UI/MainMenu.uss: Main, New Mission, Team Mission lobby, Academy, Mission Archive, Settings and the quit
    /// dialog. Keyboard / gamepad navigation comes from UI Toolkit focus; Esc (polled through <see cref="FlightInput.EscapePressed"/>
    /// in Update, so it works whatever element has the focus) goes back one screen.
    /// Drop on an empty GameObject (SpaceSim ▸ Create Main Menu Scene).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        public const string MainScreen = "main", NewMissionScreen = "new-mission", TeamScreen = "team",
            AcademyScreen = "academy", ArchiveScreen = "archive", SettingsScreen = "settings", LoadScreen = "load", CrewScreen = "crew";

        [Tooltip("Optional. If empty a 1920×1080 scale-with-screen panel is created at runtime.")]
        [SerializeField] PanelSettings panelSettings;
        [Tooltip("Optional. If empty Resources/UI/MainMenu is used.")]
        [SerializeField] StyleSheet styleSheet;
        [Tooltip("Scene loaded by Begin Mission (until the mission-design scene exists).")]
        [SerializeField] string gameplayScene = "Phase1_Planet";

        UIDocument document;
        VisualElement root, modal;
        MenuBackdrop backdrop;
        readonly Dictionary<string, VisualElement> screens = new Dictionary<string, VisualElement>();
        readonly Stack<string> history = new Stack<string>();
        readonly List<VisualElement> menuItems = new List<VisualElement>();
        readonly List<VisualElement> scenarioCards = new List<VisualElement>();
        string current;
        int scenarioIndex = 2;
        /// <summary>The real settings (brightness, mouse, key bindings), shared with the in-game Esc menu.</summary>
        SettingsView settingsView;

        // New Mission detail widgets, refreshed when the scenario changes.
        Label detailType, detailTitle, detailTarget, detailSummary, detailFocus;
        // The mission's fixed difficulty (shown, not chosen).
        Label difficultyTitle, difficultyTag, difficultyText, difficultyScore, budgetValue;
        VisualElement detailGoals, difficultyPlate;
        readonly List<VisualElement> difficultySegments = new List<VisualElement>();

        public string CurrentScreen => current;
        public bool IsReady => backdrop != null && backdrop.IsReady;
        public PanelSettings Panel => document != null ? document.panelSettings : panelSettings;
        public SettingsView Settings => settingsView;

        void Awake()
        {
            GameSettings.ApplyAtStart();
            MusicDirector.Ensure()?.EnterMenu(); // main-menu soundtrack (persistent player, survives scene loads)
            document = GetComponent<UIDocument>();
            if (panelSettings == null) panelSettings = document.panelSettings;
            if (panelSettings == null) panelSettings = MenuKit.CreatePanel("MainMenuPanel (runtime)", opaque: true, sortingOrder: 0);
            document.panelSettings = panelSettings;
            if (styleSheet == null) styleSheet = Resources.Load<StyleSheet>(MenuKit.MenuStyleSheet);
            // A flight left through the Esc menu must not leave the input facade thinking a window is still open.
            FlightInput.ModalWindowOpen = false;
            FlightInput.PauseMenuOpen = false;
        }

        void OnEnable()
        {
            if (document == null) return;
            Build(document.rootVisualElement);
        }

        void OnDisable() => settingsView?.Detach();

        void Update()
        {
            // Settings first: an Esc that cancels a key prompt there marks BlocksBack, so Back() below ignores it.
            if (current == SettingsScreen) settingsView?.Tick();
            if (FlightInput.EscapePressed && Time.frameCount != builtFrame) Back();
            if (clockLabel != null && Time.frameCount % 60 == 0) clockLabel.text = ClockText();
        }

        void Build(VisualElement documentRoot)
        {
            documentRoot.Clear();
            screens.Clear();
            menuItems.Clear();
            scenarioCards.Clear();
            difficultySegments.Clear();
            modeTiles.Clear();
            history.Clear();
            current = null;

            // Normally done by the default runtime theme, which the runtime-created panel does not have.
            MenuKit.StretchRoot(documentRoot);

            if (styleSheet != null) documentRoot.styleSheets.Add(styleSheet);
            else Debug.LogWarning("[MainMenu] Resources/UI/MainMenu.uss not found — the menu will be unstyled.");

            root = new VisualElement();
            root.AddToClassList("menu-root");
            documentRoot.Add(root);
            ApplyFonts(root);

            if (backdrop == null) backdrop = new MenuBackdrop(); // kept when the language changes: its textures take a while
            root.Add(backdrop);

            BuildMain();
            BuildNewMission();
            BuildTeam();
            BuildAcademy();
            BuildArchive();
            BuildSettings();
            BuildLoad();
            BuildCrew();
            BuildHudBottom();
            BuildQuitModal();

            // Esc is polled in Update (UI Toolkit's NavigationCancelEvent needs a focused element and the UI input module, so it
            // did not fire reliably); listening to both would go back twice.
            builtFrame = Time.frameCount;
            Show(MainScreen, remember: false);
            root.schedule.Execute(() => FirstEnabledMenuItem()?.Focus());
        }

        /// <summary>Frame of the last rebuild: the Esc that closed the settings must not also leave the rebuilt screen.</summary>
        int builtFrame = -10;

        VisualElement FirstEnabledMenuItem()
        {
            foreach (VisualElement item in menuItems)
                if (!item.ClassListContains("menu-item--disabled")) return item;
            return menuItems.Count > 0 ? menuItems[0] : null;
        }

        // ─────────────────────────────── navigation ───────────────────────────────

        /// <summary>Switches to a screen with a fade/slide transition.</summary>
        public void Show(string id, bool remember = true)
        {
            if (!screens.TryGetValue(id, out var next) || id == current) return;
            if (current != null)
            {
                if (remember) history.Push(current);
                var previous = screens[current];
                previous.RemoveFromClassList("screen--active");
                previous.schedule.Execute(() =>
                {
                    if (!previous.ClassListContains("screen--active")) previous.RemoveFromClassList("screen--visible");
                }).StartingIn(450);
            }
            current = id;
            OnShow(id);
            next.AddToClassList("screen--visible");
            // One frame later so the transition starts from the hidden state.
            next.schedule.Execute(() => { if (current == id) next.AddToClassList("screen--active"); }).StartingIn(16);
            RefreshHints();
        }

        public void Back()
        {
            if (current == SettingsScreen && settingsView != null && settingsView.BlocksBack) return; // Esc cancelled a key prompt
            if (modal.ClassListContains("modal--open")) { modal.RemoveFromClassList("modal--open"); return; }
            if (current == LoadScreen && (renaming || deleteArmed != null))
            {
                // Esc first leaves the rename field / disarms «Точно удалить?», then the screen.
                renaming = false;
                deleteArmed = null;
                RefreshLoad();
                return;
            }
            if (current == SettingsScreen && settingsView != null)
            {
                settingsView.Apply(); // Esc keeps the changes (they are already in effect); the view calls PopScreen
                return;
            }
            PopScreen();
        }

        void PopScreen()
        {
            if (history.Count > 0) Show(history.Pop(), remember: false);
        }

        /// <summary>Screens that show live data re-read it when they open.</summary>
        void OnShow(string id)
        {
            if (id == LoadScreen)
            {
                renaming = false;
                deleteArmed = null;
                loadMessage = "";
                RefreshLoad();
            }
            else if (id == CrewScreen)
            {
                crewProfile = PlayerSession.ReadProfileFile();
                crewView?.Refresh();
            }
            else if (id == SettingsScreen && !rebuildingForLanguage) settingsView?.Open();
        }

        bool rebuildingForLanguage;

        /// <summary>A frame of the menu for previews (the backdrop planet, once generated).</summary>
        Texture2D backdropPreview => backdrop != null ? backdrop.PlanetTexture : null;

        public void SelectScenario(int index)
        {
            scenarioIndex = Mathf.Clamp(index, 0, MenuCatalog.Scenarios.Length - 1);
            for (int i = 0; i < scenarioCards.Count; i++)
                scenarioCards[i].EnableInClassList("scenario-card--selected", i == scenarioIndex);
            RefreshScenarioDetail();
        }

        /// <summary>The research mission selected on the New Mission screen.</summary>
        public MenuCatalog.Scenario SelectedScenario => MenuCatalog.Scenarios[scenarioIndex];

        public void SelectMenuItem(int index)
        {
            for (int i = 0; i < menuItems.Count; i++)
                menuItems[i].EnableInClassList("menu-item--selected", i == index);
        }

        GameMode newGameMode = GameMode.Career;
        readonly List<(GameMode mode, VisualElement tile)> modeTiles = new List<(GameMode, VisualElement)>();
        Label newGameNote;

        /// <summary>Mode of the game «НАЧАТЬ НОВУЮ ИГРУ» creates (New Mission screen).</summary>
        public GameMode NewGameMode => newGameMode;

        public void SelectGameMode(GameMode mode)
        {
            newGameMode = mode;
            foreach (var (m, tile) in modeTiles) tile.EnableInClassList("diff-tile--selected", m == mode);
            if (newGameNote != null)
                newGameNote.text = Loc.T("Новая игра: ") + GameModeRules.Name(mode) +
                                   (PlayerSession.ReadProfileFile() != null || System.IO.File.Exists(PlayerSession.SavePath)
                                       ? Loc.T(". Текущая игра останется в «Сохранениях».")
                                       : ".");
        }

        /// <summary>
        /// Starts a NEW game in the selected mode (the only place a mode is chosen): the current game is kept as a save slot first
        /// (<see cref="NewGame.Start"/>), then the flight scene loads the fresh profile with the chosen goal.
        /// </summary>
        public void StartNewGame()
        {
            try
            {
                NewGame.Start(newGameMode);
            }
            catch (Exception e)
            {
                if (newGameNote != null) newGameNote.text = Loc.T("Не удалось начать новую игру: ") + e.Message;
                return;
            }
            // Career plays the selected research mission; Sandbox is free play (its goal is the sandbox).
            StartScenario(newGameMode == GameMode.Sandbox ? MenuCatalog.Sandbox.Id : SelectedScenario.Id);
        }

        public void OpenQuitDialog() => modal.AddToClassList("modal--open");

        /// <summary>Continues the saved game: the flight scene starts from the live profile and keeps its goal.</summary>
        public void BeginMission() => LoadGameplay(null);

        /// <summary>«Песочница»: straight into the flight scene; the autosave records the sandbox as its goal.</summary>
        public void StartSandbox() => LoadGameplay(MenuCatalog.Sandbox.Id);

        /// <summary>Starts a research mission (or the sandbox) by id; the autosave records it as its goal.</summary>
        public void StartScenario(string scenarioId) => LoadGameplay(string.IsNullOrEmpty(scenarioId) ? MenuCatalog.Sandbox.Id : scenarioId);

        void LoadGameplay(string scenarioId)
        {
            if (!Application.CanStreamedLevelBeLoaded(gameplayScene))
            {
                Debug.LogWarning($"[MainMenu] Scene '{gameplayScene}' is not in Build Settings.");
                return;
            }
            if (LoadingScreen.IsShowing) return; // a second click while the scene is already loading
            PlayerSession.PendingScenarioId = scenarioId; // null = keep the goal of the save

            // The loading screen names what is being started: the mission (with its goals), the sandbox, or the continued save.
            bool resuming = scenarioId == null;
            MenuCatalog.Scenario s = MenuCatalog.FindScenario(resuming ? PlayerSession.ReadProfileFile()?.scenarioId : scenarioId);
            bool sandbox = s == MenuCatalog.Sandbox;
            string eyebrow = resuming ? Loc.T("ПРОДОЛЖЕНИЕ ИГРЫ")
                : sandbox ? Loc.T("ПЕСОЧНИЦА") : Loc.T("НОВАЯ МИССИЯ") + "  ·  " + Loc.T(s.Difficulty.Title);
            string goal = sandbox ? Loc.T(s.Summary) : string.Join("  ·  ", Array.ConvertAll(s.Goals, Loc.T));
            if (!LoadingScreen.Begin(gameplayScene, eyebrow, Loc.T(s.Title), goal))
                SceneManager.LoadScene(gameplayScene);
        }

        /// <summary>Localised name of a save's goal: «Песочница» or the research mission's title.</summary>
        static string GoalName(string scenarioId) => Loc.T(MenuCatalog.FindScenario(scenarioId).Title);

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ─────────────────────────────── main screen ───────────────────────────────

        void BuildMain()
        {
            var screen = AddScreen(MainScreen);
            PlayerProfile profile = PlayerSession.ReadProfileFile();

            // Top bar: programme · launch complex · local date and time.
            var top = Add(screen, El("hz-topbar"));
            top.Add(Text("HORIZON  ·  " + Loc.T("КОСМИЧЕСКАЯ ПРОГРАММА"), "hz-topbar__item", "t-mono"));
            top.Add(Text(Loc.T("ЧЕТВЁРТАЯ ПЛАНЕТА") + "  ·  " + Loc.T("СТАРТОВЫЙ КОМПЛЕКС 01"), "hz-topbar__item", "hz-topbar__item--dim", "t-mono"));
            clockLabel = Add(top, Text(ClockText(), "hz-topbar__item", "t-mono"));

            var nav = Add(screen, El("hz-nav"));
            NavItem(nav, "ЭКИПАЖ", () => Show(CrewScreen));
            NavItem(nav, "СОХРАНЕНИЯ", () => Show(LoadScreen));
            NavItem(nav, "АРХИВ МИССИЙ", () => Show(ArchiveScreen));
            NavItem(nav, "АКАДЕМИЯ", () => Show(AcademyScreen));

            var column = Add(screen, El("main-column"));
            column.Add(Text(Loc.T("ПРОФИЛЬ") + "  /  " + (profile != null
                ? Loc.T("АВТОСОХРАНЕНИЕ") + "  ·  " + GameModeRules.Name(profile.gameMode).ToUpperInvariant()
                : Loc.T("НОВАЯ ИГРА")), "eyebrow", "t-mono"));
            column.Add(Text("HORIZON", "title"));
            var status = Add(column, El("hz-status"));
            status.Add(El("hz-status__mark"));
            status.Add(Text("СЧЁТ ПРОГРАММЫ", "hz-status__label", "t-mono"));
            status.Add(Text(profile != null
                ? UnitFormatter.Number(profile.credits, 0) + Loc.T(" кр") + "  ·  " + UnitFormatter.Number(profile.researchPoints, 0) + Loc.T(" очк.")
                : UnitFormatter.Number(PlayerProfile.StartingCredits, 0) + Loc.T(" кр"), "hz-status__value", "t-mono"));

            var list = Add(screen, El("menu-list"));
            // «Продолжить» opens the autosave (the live profile) on the load screen; its caption names the goal of that game.
            // Without an autosave there is nothing to continue: the item is greyed out and ignores clicks.
            string saved = profile != null
                ? (GoalName(profile.scenarioId) + " · " + SaveSlots.Ago(DateTimeOf(profile.lastSavedUtc))).ToUpperInvariant()
                : Loc.T("НЕТ АВТОСОХРАНЕНИЯ");
            VisualElement resume = AddMenuItem(list, "ПРОДОЛЖИТЬ", saved, () =>
            {
                if (PlayerSession.ReadProfileFile() == null) return;
                loadSaveTab = false;
                loadSelected = SaveSlots.AutosaveId;
                Show(LoadScreen);
            });
            if (profile == null)
            {
                resume.AddToClassList("menu-item--disabled");
                resume.focusable = false;
            }
            AddMenuItem(list, "НОВАЯ МИССИЯ", null, () => { Show(NewMissionScreen); SelectGameMode(GameMode.Career); });
            AddMenuItem(list, "КОМАНДНАЯ МИССИЯ", null, () => Show(TeamScreen));
            AddMenuItem(list, "ПЕСОЧНИЦА", null, () => { Show(NewMissionScreen); SelectGameMode(GameMode.Sandbox); });
            AddMenuItem(list, "НАСТРОЙКИ", null, () => Show(SettingsScreen));
            SelectMenuItem(menuItems.IndexOf(FirstEnabledMenuItem()));

            var quit = new Button(OpenQuitDialog) { text = Loc.T("ВЫЙТИ НА РАБОЧИЙ СТОЛ") };
            quit.AddToClassList("hz-quit");
            quit.AddToClassList("t-mono");
            screen.Add(quit);
            screen.Add(Text(Loc.T("СБОРКА 0.3") + "  ·  " + Loc.T("ПРОТОТИП"), "hz-build", "t-mono"));
            screen.Add(Text("NASA SPACE APPS 2026  ·  HORIZON ENGINE", "hz-render", "t-mono"));

            BuildRecent(screen);
        }

        Label clockLabel;

        static string ClockText() => DateTime.Now.ToString("dd.MM.yyyy  ·  HH:mm", System.Globalization.CultureInfo.InvariantCulture);

        static DateTime DateTimeOf(string utc) =>
            DateTime.TryParse(utc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime t)
                ? t.ToLocalTime() : DateTime.MinValue;

        void NavItem(VisualElement nav, string label, Action onClick)
        {
            var b = new Button(onClick) { text = Loc.T(label) };
            b.AddToClassList("hz-nav__item");
            b.AddToClassList("t-mono");
            nav.Add(b);
        }

        /// <summary>«Недавние сохранения»: the three newest manual saves with their pictures (they open the load screen on them).</summary>
        void BuildRecent(VisualElement screen)
        {
            var recent = Add(screen, El("hz-recent"));
            recent.Add(Text("НЕДАВНИЕ СОХРАНЕНИЯ", "hz-recent__head", "t-mono"));
            var row = Add(recent, El("hz-recent__row"));
            int shown = 0;
            foreach (SaveSlotMeta m in SaveSlots.List())
            {
                if (m.IsAutosave) continue;
                if (shown++ >= 3) break;
                string id = m.id;
                var card = new Button(() => { loadSelected = id; Show(LoadScreen); });
                card.AddToClassList("hz-card");
                var image = Add(card, El("hz-card__image"));
                Texture2D thumb = Thumb(m.id);
                if (thumb != null) image.style.backgroundImage = new StyleBackground(thumb);
                var meta = Add(card, El("hz-card__meta"));
                meta.Add(Text("FLT " + m.flights, "hz-card__num", "t-mono"));
                meta.Add(Text(SaveSlots.Ago(m.SavedLocal).ToUpperInvariant(), "hz-card__tag", "t-mono"));
                card.Add(Text(m.name, "hz-card__name"));
                row.Add(card);
            }
            if (shown == 0)
                recent.Add(Text("Ручных сохранений пока нет. В полёте: Esc → «Сохранить игру» — снимок экрана и весь профиль.", "hz-card__empty"));
        }

        VisualElement AddMenuItem(VisualElement list, string label, string meta, Action onClick)
        {
            var item = new Button(onClick) { focusable = true };
            item.AddToClassList("menu-item");
            item.Add(Text(label, "menu-item__label"));
            if (meta != null) item.Add(Text(meta, "menu-item__meta", "t-mono"));
            int i = menuItems.Count;
            item.RegisterCallback<FocusInEvent>(_ => SelectMenuItem(i));
            item.RegisterCallback<PointerEnterEvent>(_ => { if (item.focusable) item.Focus(); });
            menuItems.Add(item);
            list.Add(item);
            return item;
        }

        // ─────────────────────────────── load game (photo 8) ───────────────────────────────

        readonly Dictionary<string, Texture2D> thumbs = new Dictionary<string, Texture2D>();
        VisualElement loadList, loadDetail;
        Label loadFoot;
        string loadSelected;
        bool loadSaveTab, renaming;
        string loadMessage = "";

        Texture2D Thumb(string id)
        {
            if (thumbs.TryGetValue(id, out Texture2D t)) return t;
            t = SaveSlots.Thumbnail(id);
            thumbs[id] = t;
            return t;
        }

        void BuildLoad()
        {
            var screen = AddScreen(LoadScreen);
            var head = Add(screen, El("screen-head"));
            head.Add(Text("ЗАГРУЗКА ИГРЫ", "screen-title"));

            var tabs = Add(screen, El("hz-tabs"));
            var seg = Add(tabs, El("hz-seg"));
            var load = new Button(() => { loadSaveTab = false; RefreshLoad(); }) { text = Loc.T("ЗАГРУЗКА") };
            var save = new Button(() => { loadSaveTab = true; RefreshLoad(); }) { text = Loc.T("СОХРАНЕНИЕ") };
            foreach (Button b in new[] { load, save })
            {
                b.AddToClassList("hz-seg__item");
                b.AddToClassList("t-mono");
                seg.Add(b);
            }
            load.name = "tab-load";
            save.name = "tab-save";

            loadList = Add(Add(screen, El("hz-list")), El("hz-list__content"));
            loadList.parent.RegisterCallback<WheelEvent>(e =>
            {
                float max = Mathf.Max(0f, loadList.layout.height - loadList.parent.layout.height);
                float top = Mathf.Clamp(-loadList.resolvedStyle.top + e.delta.y * 40f, 0f, max);
                loadList.style.top = -top;
                e.StopPropagation();
            });
            loadDetail = Add(screen, El("hz-detail"));
            loadFoot = Add(screen, Text("", "hz-foot-left", "t-mono"));
            var foot = Add(screen, El("hz-foot-right"));
            VisualElement back = KeyHint(foot, "ESC", Loc.T("НАЗАД"));
            back.AddToClassList("key-hint--button");
            back.RegisterCallback<ClickEvent>(_ => Back());
        }

        void RefreshLoad()
        {
            if (loadList == null) return;
            var screen = screens[LoadScreen];
            screen.Q<Button>("tab-load").EnableInClassList("hz-seg__item--on", !loadSaveTab);
            screen.Q<Button>("tab-save").EnableInClassList("hz-seg__item--on", loadSaveTab);
            loadList.Clear();
            loadDetail.Clear();
            loadList.style.top = 0;
            List<SaveSlotMeta> slots = SaveSlots.List();
            loadFoot.text = slots.Count + " " + Loc.T("СОХРАНЕНИЙ") + "  ·  " + Loc.T("ПАПКА") + ": " + SaveSlots.Directory.Replace('\\', '/');

            if (loadSaveTab)
            {
                BuildSaveForm(slots);
                ApplyFonts(screen);
                return;
            }
            if (slots.Count == 0)
            {
                loadList.Add(Text("Сохранений нет: игра ещё не начиналась. «Новая миссия» или «Песочница» создадут профиль.", "hz-note"));
                loadDetail.Add(Text("НОВАЯ ИГРА", "eyebrow", "t-mono"));
                loadDetail.Add(Text("Профиль игрока", "hz-detail__name", "t-strong"));
                Add(Add(loadDetail, El("hz-detail__buttons")), ButtonEl(Loc.T("НАЧАТЬ") + "  →", StartSandbox, primary: true));
                ApplyFonts(screen);
                return;
            }
            if (loadSelected == null || !slots.Exists(s => s.id == loadSelected)) loadSelected = slots[0].id;
            foreach (SaveSlotMeta m in slots) loadList.Add(SlotRow(m));
            BuildSlotDetail(slots.Find(s => s.id == loadSelected));
            ApplyFonts(screen);
        }

        VisualElement SlotRow(SaveSlotMeta m)
        {
            var row = new Button(() =>
            {
                loadSelected = m.id;
                renaming = false;
                loadMessage = "";
                RefreshLoad();
            });
            row.AddToClassList("hz-row");
            if (m.id == loadSelected) row.AddToClassList("hz-row--on");
            var thumb = Add(row, El("hz-row__thumb"));
            Texture2D t = Thumb(m.id);
            if (t != null) thumb.style.backgroundImage = new StyleBackground(t);
            else if (backdropPreview != null) thumb.style.backgroundImage = new StyleBackground(backdropPreview);
            var main = Add(row, El("hz-row__main"));
            var title = Add(main, El("hz-row__title"));
            title.Add(Text(m.name, "hz-row__name"));
            if (m.IsAutosave) title.Add(Text("АВТО", "hz-tag", "t-mono"));
            title.Add(Text(GameModeRules.Name(m.gameMode).ToUpperInvariant(), "hz-tag", "t-mono"));
            main.Add(Text(GoalName(m.scenarioId).ToUpperInvariant() + "  ·  " + m.SavedLocal.ToString("dd.MM  HH:mm"), "hz-row__sub", "t-mono"));
            Add(row, Text(UnitFormatter.Number(m.credits, 0) + Loc.T(" кр"), "hz-row__cell", "t-mono")).style.width = 170;
            Add(row, Text(UnitFormatter.Number(m.research, 0) + Loc.T(" очк."), "hz-row__cell", "hz-row__cell--green", "t-mono")).style.width = 140;
            Add(row, Text(SaveSlots.Ago(m.SavedLocal).ToUpperInvariant(), "hz-row__cell", "hz-row__cell--dim", "t-mono")).style.width = 170;
            return row;
        }

        void BuildSlotDetail(SaveSlotMeta m)
        {
            if (m == null) return;
            var image = Add(loadDetail, El("hz-detail__image"));
            Texture2D t = Thumb(m.id) ?? backdropPreview;
            if (t != null) image.style.backgroundImage = new StyleBackground(t);
            loadDetail.Add(Text((m.IsAutosave ? Loc.T("АВТОСОХРАНЕНИЕ") : Loc.T("РУЧНОЕ СОХРАНЕНИЕ")), "eyebrow", "t-mono"));
            if (renaming && !m.IsAutosave)
            {
                var field = new TextField { value = m.name, maxLength = SaveSlots.MaxNameLength };
                field.AddToClassList("hz-field");
                loadDetail.Add(field);
                field.schedule.Execute(() => field.Focus());
                var buttons = Add(loadDetail, El("hz-detail__buttons"));
                buttons.Add(ButtonEl("ОТМЕНА", () => { renaming = false; RefreshLoad(); }));
                buttons.Add(ButtonEl("СОХРАНИТЬ ИМЯ", () =>
                {
                    SaveSlots.Rename(m.id, field.value);
                    renaming = false;
                    RefreshLoad();
                }, primary: true));
                return;
            }
            loadDetail.Add(Text(m.name, "hz-detail__name", "t-strong"));
            Kv(loadDetail, "Сохранено", m.SavedLocal == DateTime.MinValue ? "—" : m.SavedLocal.ToString("dd.MM.yyyy  HH:mm"));
            Kv(loadDetail, "Режим", GameModeRules.Name(m.gameMode));
            Kv(loadDetail, "Полётов", m.flights.ToString());
            Kv(loadDetail, "Цель миссии", GoalName(m.scenarioId)); // «Песочница» or the research mission (old saves: sandbox)
            Kv(loadDetail, "Кредиты", UnitFormatter.Number(m.credits, 0) + Loc.T(" кр"));
            Kv(loadDetail, "Исследования", UnitFormatter.Number(m.research, 0) + Loc.T(" очк."));
            Kv(loadDetail, "Образцы", m.samples.ToString());
            Kv(loadDetail, "Посещено тел", m.visitedBodies.ToString());
            Kv(loadDetail, "Время в игре", SaveSlots.PlayTime(m.playSeconds));
            if (!string.IsNullOrEmpty(loadMessage)) loadDetail.Add(Text(loadMessage, "hz-note", "hz-note--warn"));

            var row = Add(loadDetail, El("hz-detail__buttons"));
            if (!m.IsAutosave)
            {
                Button del = ButtonEl(deleteArmed == m.id ? "ТОЧНО УДАЛИТЬ?" : "УДАЛИТЬ", () =>
                {
                    if (deleteArmed != m.id) { deleteArmed = m.id; RefreshLoad(); return; }
                    deleteArmed = null;
                    SaveSlots.Delete(m.id);
                    loadSelected = null;
                    RefreshLoad();
                });
                del.AddToClassList("btn--text");
                if (deleteArmed == m.id) del.AddToClassList("btn--danger");
                row.Add(del);
                row.Add(ButtonEl("ПЕРЕИМЕНОВАТЬ", () => { renaming = true; RefreshLoad(); }));
            }
            row.Add(ButtonEl(Loc.T("ЗАГРУЗИТЬ") + "  →", () => LoadSlot(m), primary: true));
        }

        string deleteArmed;

        /// <summary>Loads a slot (the autosave is already live) and continues it: the goal comes from the save itself.</summary>
        void LoadSlot(SaveSlotMeta m)
        {
            if (!m.IsAutosave)
            {
                if (!SaveSlots.Load(m.id, out string problem))
                {
                    loadMessage = problem;
                    RefreshLoad();
                    return;
                }
            }
            BeginMission();
        }

        /// <summary>«Сохранение» tab: copies the live profile into a new named slot (no picture in the menu).</summary>
        void BuildSaveForm(List<SaveSlotMeta> slots)
        {
            foreach (SaveSlotMeta m in slots) if (!m.IsAutosave) loadList.Add(SlotRow(m));
            if (slots.Count <= 1) loadList.Add(Text("Ручных сохранений пока нет.", "hz-note"));
            PlayerProfile live = PlayerSession.ReadProfileFile();
            loadDetail.Add(Text("НОВОЕ СОХРАНЕНИЕ", "eyebrow", "t-mono"));
            loadDetail.Add(Text("Сохранить текущий профиль", "hz-detail__name", "t-strong"));
            if (live == null)
            {
                loadDetail.Add(Text("Профиля ещё нет — сохранять нечего.", "hz-note"));
                return;
            }
            var field = new TextField { value = Loc.T("Сохранение") + " " + DateTime.Now.ToString("dd.MM HH:mm"), maxLength = SaveSlots.MaxNameLength };
            field.AddToClassList("hz-field");
            loadDetail.Add(field);
            loadDetail.Add(Text("В полёте (Esc → «Сохранить игру») к сохранению добавляется снимок экрана.", "hz-note"));
            if (!string.IsNullOrEmpty(loadMessage)) loadDetail.Add(Text(loadMessage, "hz-note", "hz-note--warn"));
            var row = Add(loadDetail, El("hz-detail__buttons"));
            row.Add(ButtonEl(Loc.T("СОХРАНИТЬ") + "  →", () =>
            {
                SaveSlotMeta made = SaveSlots.Save(live, field.value, "", null);
                loadMessage = Loc.T("Сохранено: ") + made.name;
                loadSaveTab = false;
                loadSelected = made.id;
                RefreshLoad();
            }, primary: true));
        }

        static void Kv(VisualElement parent, string k, string v)
        {
            var row = Add(parent, El("hz-kv"));
            row.Add(Text(k, "hz-kv__k"));
            row.Add(Text(v, "hz-kv__v", "t-mono"));
        }

        // ─────────────────────────────── crew (photo 7) ───────────────────────────────

        CrewView crewView;
        PlayerProfile crewProfile;

        void BuildCrew()
        {
            var screen = AddScreen(CrewScreen);
            crewView = new CrewView(screen, new MenuCrewHost(this));
        }

        /// <summary>The crew screen in the main menu works on the saved profile file directly.</summary>
        sealed class MenuCrewHost : CrewView.IHost
        {
            readonly MainMenuController menu;
            public MenuCrewHost(MainMenuController owner) => menu = owner;
            public PlayerProfile Profile => menu.crewProfile;
            public bool InFlight => false;
            public string FlightTitle => "";
            public string FlightClock => "";

            public bool SendToTraining(string id) => Change(p => SpaceSim.Crew.CrewRoster.SendToTraining(p.crew, id));
            public bool Hire(string id) => Change(p => SpaceSim.Crew.CrewRoster.Hire(p, p.crew, id, out _));
            public void Close() => menu.Back();

            bool Change(Func<PlayerProfile, bool> change)
            {
                PlayerProfile p = menu.crewProfile;
                if (p == null || !change(p)) return false;
                PlayerSession.WriteProfileFile(p);
                return true;
            }
        }

        void ResourceChip(VisualElement parent, MissionResource r, string value)
        {
            var chip = Add(parent, El("res-chip"));
            var swatch = Add(chip, El("res-chip__swatch"));
            swatch.style.backgroundColor = MenuCatalog.ResourceColors[(int)r];
            chip.Add(Text(MenuCatalog.ResourceNames[(int)r], "res-chip__label"));
            chip.Add(Text(value, "res-chip__value", "t-mono"));
        }

        void LeaderRow(VisualElement parent, string rank, string name, string crew, string score, bool you = false)
        {
            var row = Add(parent, El("leader-row"));
            if (you) row.AddToClassList("leader-row--you");
            row.Add(Text(rank, "leader-row__rank", "t-mono"));
            row.Add(Text(name, "leader-row__name"));
            row.Add(Text(crew, "leader-row__crew"));
            row.Add(Text(score, "leader-row__score", "t-mono"));
        }

        // ─────────────────────────────── new mission ───────────────────────────────

        void BuildNewMission()
        {
            var screen = AddScreen(NewMissionScreen);
            ScreenHeader(screen, "NEW MISSION", "CHOOSE YOUR OBJECTIVE",
                "Every mission starts with a goal. The difficulty is set by the mission itself.");

            var flow = Add(screen, El("flow"));
            string[] steps = { "OBJECTIVE", "REQUIREMENTS", "SPACECRAFT", "LAUNCH & ORBIT", "SIMULATE", "DEBRIEF" };
            for (int i = 0; i < steps.Length; i++)
            {
                var step = Add(flow, El("flow__step"));
                if (i == 0) step.AddToClassList("flow__step--active");
                step.Add(Text((i + 1).ToString(), "flow__num", "t-mono"));
                step.Add(Text(steps[i], "flow__label", "t-strong"));
                if (i < steps.Length - 1) flow.Add(El("flow__line"));
            }

            var body = Add(screen, El("screen-body"));
            body.style.top = 318;

            var list = Add(body, El("scenario-list"));
            for (int i = 0; i < MenuCatalog.Scenarios.Length; i++)
            {
                var s = MenuCatalog.Scenarios[i];
                int index = i;
                var card = new Button(() => SelectScenario(index));
                card.AddToClassList("scenario-card");
                var stripe = Add(card, El("scenario-card__stripe"));
                stripe.style.backgroundColor = s.Tint;
                var thumb = Add(card, El("scenario-card__thumb"));
                thumb.style.backgroundImage = new StyleBackground(PlanetThumb(s.Tint, i));
                var text = Add(card, El("scenario-card__body"));
                text.Add(Text(s.Type, "scenario-card__type", "t-strong"));
                text.Add(Text(s.Title, "scenario-card__title", "t-strong"));
                text.Add(Text(s.Target, "scenario-card__target"));
                // The mission's fixed difficulty (not chosen by the player).
                text.Add(Text(Loc.T("DIFFICULTY") + ": " + Loc.T(s.Difficulty.Title), "scenario-card__difficulty",
                    "scenario-card__difficulty--" + s.DifficultyIndex, "t-mono"));
                var pips = Add(card, El("scenario-card__pips"));
                for (int p = 0; p < 5; p++) pips.Add(El("pip", p < s.Stars ? "pip--on" : null));
                card.RegisterCallback<FocusInEvent>(_ => SelectScenario(index));
                scenarioCards.Add(card);
                list.Add(card);
            }

            var detail = Add(body, El("panel", "detail"));
            detailType = Add(detail, Text("", "eyebrow", "t-strong"));
            detailTitle = Add(detail, Text("", "detail__title", "t-strong"));
            detailTarget = Add(detail, Text("", "detail__target", "t-strong"));
            detailSummary = Add(detail, Text("", "detail__summary"));

            var columns = Add(detail, El("detail__columns"));
            detailGoals = Add(columns, El("grow"));
            var focus = Add(columns, El("focus-box"));
            Add(focus, Text("YOU WILL LEARN", "eyebrow", "t-strong")).style.color = (Color)new Color32(0xA9, 0x8B, 0xFF, 0xFF);
            detailFocus = Add(focus, Text("", "focus-box__text"));

            // Budget: the one starting resource still shown, as a compact value line.
            var budget = Add(detail, El("budget-line"));
            var budgetSwatch = Add(budget, El("budget-line__swatch"));
            budgetSwatch.style.backgroundColor = MenuCatalog.ResourceColors[(int)MissionResource.Budget];
            budget.Add(Text("STARTING BUDGET", "budget-line__label", "t-strong"));
            budget.Add(El("grow"));
            budgetValue = Add(budget, Text("", "budget-line__value", "t-mono"));
            budget.tooltip = Loc.T(MenuCatalog.ResourceHints[(int)MissionResource.Budget]);

            // The mission's fixed difficulty: a coloured plate (tier name, tagline, 1–5 segment meter, description), the one
            // detailed difficulty display of the screen. Tier colour comes from the diff-plate--0/1/2 class.
            difficultyPlate = Add(detail, El("diff-plate"));
            var plateTop = Add(difficultyPlate, El("diff-plate__top"));
            var plateName = Add(plateTop, El("grow"));
            plateName.Add(Text(Loc.T("MISSION DIFFICULTY") + "  ·  " + Loc.T("Set by the mission — it cannot be changed").ToUpperInvariant(),
                "diff-plate__eyebrow", "t-mono"));
            difficultyTitle = Add(plateName, Text("", "diff-plate__title", "t-strong"));
            difficultyTag = Add(plateName, Text("", "diff-plate__tag", "t-strong"));
            var meter = Add(plateTop, El("diff-plate__meter"));
            var segments = Add(meter, El("diff-plate__segments"));
            difficultySegments.Clear();
            for (int i = 0; i < 5; i++) difficultySegments.Add(Add(segments, El("diff-plate__seg")));
            difficultyScore = Add(meter, Text("", "diff-plate__score", "t-mono"));
            difficultyText = Add(difficultyPlate, Text("", "diff-plate__text"));

            var config = Add(body, El("config"));
            // Game mode of the new save: chosen only here, fixed for the save's whole life.
            Add(config, Text("РЕЖИМ ИГРЫ", "section-label", "t-strong")).style.marginTop = 0;
            foreach (GameMode mode in new[] { GameMode.Career, GameMode.Sandbox })
            {
                GameMode chosen = mode;
                var tile = new Button(() => SelectGameMode(chosen));
                tile.AddToClassList("diff-tile");
                var head = Add(tile, El("diff-tile__head"));
                head.Add(Text(GameModeRules.Name(mode).ToUpperInvariant(), "diff-tile__title", "t-strong"));
                head.Add(Text(mode == GameMode.Career
                    ? Loc.T("БЮДЖЕТ ") + UnitFormatter.Number(GameModeRules.CareerStartingCredits, 0) + Loc.T(" кр")
                    : Loc.T("ВСЁ ОТКРЫТО"), "diff-tile__tag", "t-strong"));
                tile.Add(Text(GameModeRules.Description(mode), "diff-tile__text"));
                modeTiles.Add((mode, tile));
                config.Add(tile);
            }
            Add(config, Text("OPTIONS", "section-label", "t-strong")).style.marginTop = 14;
            ToggleRow(config, "Random mission events", true);
            ToggleRow(config, "Advisor explains each decision", true);
            ToggleRow(config, "Show real-world equivalents", false);

            var cta = Add(config, El("cta-stack"));
            cta.Add(ButtonEl("НАЧАТЬ НОВУЮ ИГРУ", StartNewGame, primary: true));
            newGameNote = Add(cta, Text("", "hz-note"));
            cta.Add(ButtonEl("PLAY AS A TEAM", () => Show(TeamScreen)));

            SelectScenario(scenarioIndex);
            SelectGameMode(newGameMode);
        }

        void RefreshScenarioDetail()
        {
            if (detailTitle == null) return;
            var s = MenuCatalog.Scenarios[scenarioIndex];
            detailType.text = Loc.T(s.Type) + "   ·   " + DifficultyStars(s.Stars) + "   ·   " + Loc.T(s.Difficulty.Title);
            if (difficultyPlate != null)
            {
                for (int t = 0; t < MenuCatalog.Difficulties.Length; t++)
                    difficultyPlate.EnableInClassList("diff-plate--" + t, t == s.DifficultyIndex);
                difficultyTitle.text = Loc.T(s.Difficulty.Title);
                difficultyTag.text = Loc.T(s.Difficulty.Tagline).ToUpperInvariant();
                difficultyText.text = Loc.T(s.Difficulty.Description);
                for (int i = 0; i < difficultySegments.Count; i++)
                    difficultySegments[i].EnableInClassList("diff-plate__seg--on", i < s.Stars);
                difficultyScore.text = s.Stars + "/5";
            }
            if (budgetValue != null)
                budgetValue.text = FormatAllowance(s.Allowance[(int)MissionResource.Budget], MenuCatalog.ResourceUnits[(int)MissionResource.Budget]);
            detailTitle.text = Loc.T(s.Title);
            detailTarget.text = Loc.T(s.Target).ToUpperInvariant();
            detailSummary.text = Loc.T(s.Summary);
            detailFocus.text = Loc.T(s.LearningFocus);

            detailGoals.Clear();
            detailGoals.Add(Text("MISSION GOALS", "eyebrow", "eyebrow--cyan", "t-strong"));
            foreach (var g in s.Goals)
            {
                var goal = Add(detailGoals, El("goal"));
                goal.Add(El("goal__mark"));
                goal.Add(Text(g, "goal__text"));
            }
            ApplyFonts(detailGoals);
        }

        static string DifficultyStars(int stars) => stars == 0 ? Loc.T("NO LIMITS") : Loc.T("DIFFICULTY") + " " + stars + "/5";

        static string FormatAllowance(float v, string unit)
        {
            string number = v >= 1000f ? v.ToString("#,0").Replace(',', ' ') : v.ToString("0.##");
            return number + " " + Loc.T(unit);
        }

        void ToggleRow(VisualElement parent, string label, bool on)
        {
            var row = new Button();
            row.AddToClassList("toggle-row");
            row.Add(Text(label, "toggle-row__label"));
            var sw = Add(row, El("switch", on ? "switch--on" : null));
            sw.Add(El("switch__knob"));
            row.clicked += () => sw.ToggleInClassList("switch--on");
            parent.Add(row);
        }

        // ─────────────────────────────── team lobby ───────────────────────────────

        void BuildTeam()
        {
            var screen = AddScreen(TeamScreen);
            ScreenHeader(screen, "TEAM MISSION", "ASSEMBLE YOUR CREW",
                "Up to four players, one spacecraft. Each role owns a subsystem — and has to negotiate with the others.");

            var body = Add(screen, El("screen-body", "team-body"));
            body.style.top = 262;

            var bar = Add(body, El("team-bar"));
            var mode = Add(bar, El("team-bar__group"));
            mode.Add(Text("MODE", "team-bar__label", "t-strong"));
            Segmented(mode, new[] { "CO-OP", "VERSUS" }, 0);
            var link = Add(bar, El("team-bar__group"));
            link.Add(Text("PLAY", "team-bar__label", "t-strong"));
            Segmented(link, new[] { "SAME SCREEN", "ONLINE" }, 1);
            var code = Add(bar, El("team-bar__group"));
            code.Add(Text("ROOM CODE", "team-bar__label", "t-strong"));
            code.Add(Text("HZN-7Q4K", "room-code", "t-mono"));
            bar.Add(El("grow"));
            var mission = Add(bar, El("chip"));
            mission.Add(Text("MISSION", "chip__label"));
            mission.Add(Text("QUORIS POLAR MAPPER · ENGINEER", "chip__value", "t-strong"));

            var roles = Add(body, El("roles"));
            string[] players = { "Nova_7", "Kepler", "LunaByte", null };
            string[] meta = { "Host · Level 12", "Level 8", "Level 15", "" };
            bool[] ready = { true, true, false, false };
            string[,] stats = { { "3", "VETO" }, { "38%", "MASS MARGIN" }, { "4", "INSTRUMENTS" }, { "2", "STATIONS" } };
            for (int i = 0; i < MenuCatalog.Roles.Length; i++)
            {
                var role = MenuCatalog.Roles[i];
                bool open = players[i] == null;
                var card = Add(roles, El("role-card", open ? "role-card--open" : null, i == MenuCatalog.Roles.Length - 1 ? "role-card--last" : null));
                var band = Add(card, El("role-card__band"));
                band.style.backgroundColor = role.Tint;
                var inner = Add(card, El("role-card__inner"));
                var avatar = Add(inner, Text(open ? "+" : role.Initials, "role-card__avatar", "t-strong"));
                if (!open)
                {
                    avatar.style.borderTopColor = avatar.style.borderBottomColor = avatar.style.borderLeftColor = avatar.style.borderRightColor = role.Tint;
                    avatar.style.color = role.Tint;
                    avatar.style.backgroundColor = new Color(role.Tint.r, role.Tint.g, role.Tint.b, 0.1f);
                }
                inner.Add(Text(role.Title, "role-card__title", "t-strong"));
                Add(inner, Text(role.Owns, "role-card__owns", "t-strong")).style.color = role.Tint;
                inner.Add(Text(role.Description, "role-card__desc"));
                inner.Add(El("role-card__divider"));
                inner.Add(Text(open ? "Open seat" : players[i], "role-card__player", "t-strong"));
                inner.Add(Text(open ? "Share the room code or press ENTER on a second controller" : meta[i], "role-card__meta", "wrap"));
                if (!open)
                {
                    var st = Add(inner, El("role-card__stats"));
                    var stat = Add(st, El("stat"));
                    stat.Add(Text(stats[i, 0], "stat__value", "t-mono"));
                    stat.Add(Text(stats[i, 1], "stat__label", "t-strong"));
                    var vote = Add(st, El("stat"));
                    vote.Add(Text("1", "stat__value", "t-mono"));
                    vote.Add(Text("VOTE", "stat__label", "t-strong"));
                }
                card.Add(Text(open ? "WAITING FOR CREW" : ready[i] ? "READY" : "DESIGNING…", "ready",
                    open ? "ready--open" : ready[i] ? "ready--yes" : "ready--no", "t-strong"));
            }

            var foot = Add(body, El("team-foot"));
            Add(foot, Text("HOW IT WORKS", "eyebrow", "eyebrow--cyan", "t-strong")).style.marginRight = 8;
            foot.Add(Text("Each role signs off its own subsystem. Changes that cross roles — a heavier camera, a bigger antenna — go to a crew vote. Arguing costs schedule days, and the clock is a resource too.", "team-foot__text"));
            foot.Add(El("grow"));
            foot.Add(ButtonEl("INVITE CREW", () => { }));
            var start = Add(foot, ButtonEl("START MISSION", BeginMission, primary: false, disabled: true));
            start.style.marginLeft = 14;
            start.Add(Text("2/4 READY", "btn__count", "t-mono"));
        }

        void Segmented(VisualElement parent, string[] options, int selected)
        {
            var seg = Add(parent, El("segmented"));
            var items = new List<Button>();
            for (int i = 0; i < options.Length; i++)
            {
                var b = new Button { text = Loc.T(options[i]) };
                b.AddToClassList("segmented__item");
                b.AddToClassList("t-strong");
                if (i == selected) b.AddToClassList("segmented__item--on");
                items.Add(b);
                seg.Add(b);
            }
            foreach (var b in items)
                b.clicked += () => { foreach (var o in items) o.EnableInClassList("segmented__item--on", o == b); };
        }

        // ─────────────────────────────── academy ───────────────────────────────

        void BuildAcademy()
        {
            var screen = AddScreen(AcademyScreen);
            ScreenHeader(screen, "ACADEMY", "FLIGHT SCHOOL",
                "Short interactive lessons. Each one explains why a decision matters — and unlocks a part in the hangar.");

            var body = Add(screen, El("screen-body"));
            body.style.top = 268;

            var side = Add(body, El("academy-side"));
            var progress = Add(side, El("panel"));
            progress.Add(Text("YOUR PROGRESS", "eyebrow", "t-strong"));
            int done = 0;
            foreach (var l in MenuCatalog.Lessons) if (l.Progress >= 1f) done++;
            var big = Add(progress, El("row"));
            big.style.alignItems = Align.FlexEnd;
            big.Add(Text(done + "/" + MenuCatalog.Lessons.Length, "big-number", "t-strong"));
            big.Add(Text("lessons complete", "big-number__unit"));
            var bar = Add(progress, El("progress"));
            Add(bar, El("progress__fill")).style.width = Length.Percent(100f * done / MenuCatalog.Lessons.Length);
            progress.Add(Text("Finish \"Payload capacity\" to unlock the heavy-lift launcher.", "panel__text"));

            var next = Add(side, El("panel", "panel--spaced"));
            next.Add(Text("UP NEXT", "eyebrow", "eyebrow--cyan", "t-strong"));
            Add(next, Text("Payload capacity & choosing a rocket", "panel__title", "t-strong")).style.marginTop = 8;
            next.Add(Text("Why a rocket that can lift 20 t to low orbit can only carry 8 t to geostationary orbit.", "panel__text"));
            var resume = Add(next, ButtonEl("RESUME LESSON", () => { }, primary: true));
            resume.style.marginTop = 20;

            var gl = Add(side, El("panel", "panel--spaced"));
            gl.Add(Text("GLOSSARY", "eyebrow", "eyebrow--muted", "t-strong"));
            var terms = Add(gl, El("glossary"));
            foreach (var t in new[] { "Orbit", "Delta-v", "Payload", "Apoapsis", "Data rate", "Margin", "RTG", "Ground station" })
                terms.Add(Text(t, "term"));

            var grid = Add(body, El("lesson-grid"));
            foreach (var l in MenuCatalog.Lessons)
            {
                var card = Add(grid, El("lesson", l.Locked ? "lesson--locked" : null));
                var topic = Add(card, El("lesson__topic"));
                var color = TopicColor(l.Topic);
                var sw = Add(topic, El("res-chip__swatch"));
                sw.style.backgroundColor = color;
                Add(topic, Text(l.Topic, "eyebrow", "t-strong")).style.color = color;
                card.Add(Text(l.Title, "lesson__title", "t-strong"));
                var p = Add(card, El("progress"));
                p.style.marginBottom = 14;
                Add(p, El("progress__fill")).style.width = Length.Percent(l.Progress * 100f);
                var foot = Add(card, El("lesson__foot"));
                string status = l.Locked ? "LOCKED" : l.Progress >= 1f ? "COMPLETE" : l.Progress > 0f ? "CONTINUE" : "START";
                var st = Add(foot, Text(status, "lesson__status", "t-strong"));
                st.style.color = (Color)(l.Locked ? new Color32(0x6E, 0x7C, 0x92, 0xFF)
                    : l.Progress >= 1f ? new Color32(0x5B, 0xE4, 0x9B, 0xFF) : new Color32(0xFF, 0xB5, 0x47, 0xFF));
                foot.Add(Text(l.Minutes, "lesson__time", "t-mono"));
            }
        }

        static Color TopicColor(string topic)
        {
            switch (topic)
            {
                case "POWER": return MenuCatalog.ResourceColors[(int)MissionResource.Power];
                case "MASS": return MenuCatalog.ResourceColors[(int)MissionResource.Mass];
                case "COMMS": return MenuCatalog.ResourceColors[(int)MissionResource.Comms];
                case "SCIENCE": return MenuCatalog.ResourceColors[(int)MissionResource.Data];
                case "LIFETIME": return MenuCatalog.ResourceColors[(int)MissionResource.Lifetime];
                case "LAUNCH": return MenuCatalog.ResourceColors[(int)MissionResource.Budget];
                default: return new Color32(0x7F, 0xD4, 0xFF, 0xFF);
            }
        }

        // ─────────────────────────────── archive ───────────────────────────────

        void BuildArchive()
        {
            var screen = AddScreen(ArchiveScreen);
            ScreenHeader(screen, "MISSION ARCHIVE", "PAST MISSIONS",
                "Every design you simulate is kept with its debrief. Pick two to compare what changed — and what it cost.");

            var body = Add(screen, El("screen-body"));
            body.style.top = 268;

            var table = Add(body, El("panel", "table"));
            var head = Add(table, El("table__row", "table__row--head"));
            Add(head, El("table__check")).style.opacity = 0;
            head.Add(Text("DESIGN", "th", "col-name", "t-strong"));
            head.Add(Text("SCENARIO", "th", "col-scn", "t-strong"));
            head.Add(Text("CREW", "th", "col-crew", "t-strong"));
            head.Add(Text("SCORE", "th", "col-score", "t-strong"));
            head.Add(Text("GRADE", "th", "col-grade", "t-strong"));
            head.Add(Text("RESULT", "th", "col-result", "t-strong"));
            head.Add(Text("DATE", "th", "col-date", "t-strong"));

            ArchiveRow(table, "Ice Hunter III", "Quoris Polar Mapper", "4", "9 120", "A", "SUCCESS", "Oct 31", true);
            ArchiveRow(table, "Ice Hunter II", "Quoris Polar Mapper", "4", "6 410", "C", "PARTIAL", "Oct 29", true);
            ArchiveRow(table, "Featherweight", "Homeworld Weather Watch", "solo", "8 750", "A", "SUCCESS", "Oct 27", false);
            ArchiveRow(table, "Big Ears", "Relay Ring", "2", "7 980", "B", "SUCCESS", "Oct 24", false);
            ArchiveRow(table, "Icarus", "Teonan Sun-Grazer", "solo", "1 205", "F", "LOST", "Oct 22", false);
            ArchiveRow(table, "Dust Devil", "Mora Moon Lander", "3", "5 560", "C", "PARTIAL", "Oct 20", false);
            ArchiveRow(table, "Everything Bagel", "Sandbox", "solo", "—", "—", "SANDBOX", "Oct 18", false);

            var side = Add(body, El("archive-side"));
            var compare = Add(side, El("panel"));
            compare.Add(Text("COMPARE  ·  ICE HUNTER II → III", "eyebrow", "eyebrow--cyan", "t-strong"));
            compare.Add(Text("Swapping the radar for a lighter spectrometer saved 140 kg — enough for a cheaper rocket.", "panel__text"));
            CompareRow(compare, MissionResource.Mass, "1 340 → 1 200 kg", 0.92f, 0.82f);
            CompareRow(compare, MissionResource.Power, "860 → 640 W", 0.95f, 0.71f);
            CompareRow(compare, MissionResource.Budget, "455 → 398 M$", 1.0f, 0.88f);
            CompareRow(compare, MissionResource.Data, "1.6 → 2.3 Tb", 0.55f, 0.8f);
            var cmp = Add(compare, ButtonEl("OPEN FULL DEBRIEF", () => { }, primary: true));
            cmp.style.marginTop = 22;

            var share = Add(side, El("panel", "panel--spaced"));
            share.Add(Text("SHARE A DESIGN", "eyebrow", "t-strong"));
            share.Add(Text("Send a design code to another team so they can fly your spacecraft against the same objective.", "panel__text"));
            var code = Add(share, El("row"));
            code.style.marginTop = 16;
            code.Add(Text("ICE3-QPM-8F2A", "room-code", "t-mono"));
            code.Add(El("grow"));
            code.Add(ButtonEl("COPY", () => { }));
        }

        void ArchiveRow(VisualElement table, string name, string scenario, string crew, string score, string grade, string result, string date, bool selected)
        {
            var row = new Button();
            row.AddToClassList("table__row");
            if (selected) row.AddToClassList("table__row--selected");
            var check = Add(row, El("table__check", selected ? "table__check--on" : null));
            row.clicked += () =>
            {
                row.ToggleInClassList("table__row--selected");
                check.ToggleInClassList("table__check--on");
            };
            row.Add(Text(name, "td", "col-name", "t-strong"));
            row.Add(Text(scenario, "td--muted", "col-scn"));
            row.Add(Text(crew, "td--muted", "col-crew"));
            row.Add(Text(score, "td", "col-score", "t-mono"));
            var g = Add(row, Text(grade, "grade", "col-grade", "t-strong"));
            g.style.color = (Color)(grade == "A" ? new Color32(0x5B, 0xE4, 0x9B, 0xFF) : grade == "F" ? new Color32(0xFF, 0x6B, 0x5E, 0xFF)
                : grade == "—" ? new Color32(0x56, 0x63, 0x78, 0xFF) : new Color32(0xFF, 0xC8, 0x57, 0xFF));
            var cell = Add(row, El("col-result"));
            string kind = result == "SUCCESS" ? "result--success" : result == "LOST" ? "result--lost" : result == "PARTIAL" ? "result--partial" : null;
            var chip = Add(cell, Text(result, "result", kind, "t-strong"));
            if (kind == null) chip.style.color = (Color)new Color32(0xC6, 0xF2, 0x6B, 0xFF);
            row.Add(Text(date, "td--muted", "col-date"));
            table.Add(row);
        }

        void CompareRow(VisualElement parent, MissionResource r, string value, float before, float after)
        {
            var label = Add(parent, El("row"));
            label.style.marginTop = 16;
            var sw = Add(label, El("res-row__swatch"));
            sw.style.backgroundColor = MenuCatalog.ResourceColors[(int)r];
            label.Add(Text(MenuCatalog.ResourceNames[(int)r], "res-row__name", "t-strong"));
            label.Add(El("grow"));
            label.Add(Text(value, "res-chip__value", "t-mono"));
            var a = Add(parent, El("progress"));
            a.style.marginTop = 8;
            var fa = Add(a, El("progress__fill"));
            fa.style.width = Length.Percent(before * 100f);
            fa.style.backgroundColor = new Color(1f, 1f, 1f, 0.22f);
            var b = Add(parent, El("progress"));
            b.style.marginTop = 4;
            var fb = Add(b, El("progress__fill"));
            fb.style.width = Length.Percent(after * 100f);
            fb.style.backgroundColor = MenuCatalog.ResourceColors[(int)r];
        }

        // ─────────────────────────────── settings ───────────────────────────────


        /// <summary>The language changed: rebuilds every screen in the new language and returns to the language tab.</summary>
        public void RebuildForLanguage()
        {
            bool inSettings = current == SettingsScreen;
            document.rootVisualElement.schedule.Execute(() =>
            {
                Build(document.rootVisualElement);
                if (!inSettings) return; // «Отмена» restored the old language and left the screen
                rebuildingForLanguage = true;
                Show(SettingsScreen, remember: true);
                rebuildingForLanguage = false;
                settingsView.ShowPage(SettingsView.Page.Language);
            });
        }

        void BuildSettings()
        {
            var screen = AddScreen(SettingsScreen);
            var head = Add(screen, El("screen-head"));
            head.Add(Text("НАСТРОЙКИ", "screen-title"));
            // The real, saved settings (shared with the in-game pause menu).
            settingsView?.Detach();
            settingsView = new SettingsView(screen, _ => PopScreen(), RebuildForLanguage, () => backdropPreview);
        }

        // ─────────────────────────────── chrome ───────────────────────────────

        void BuildHudTop()
        {
            var top = Add(root, El("hud-top"));
            var mark = Add(top, El("brand-mark"));
            mark.Add(El("brand-mark__core"));
            top.Add(Text("HORIZON", "brand-name", "t-strong"));
            top.Add(Text("MISSION CONTROL  ·  HOME SYSTEM", "brand-sub"));
            top.Add(El("grow"));

            var link = Add(top, El("chip"));
            link.Add(El("status-dot"));
            link.Add(Text("UPLINK", "chip__label", "t-strong"));
            link.Add(Text("ONLINE", "chip__value", "t-strong"));

            var crew = Add(top, El("chip"));
            crew.Add(Text("CREW", "chip__label", "t-strong"));
            crew.Add(Text("3 / 4", "chip__value", "t-mono"));

            var profile = Add(top, El("chip"));
            profile.Add(Text("CM", "avatar-small", "t-strong"));
            Add(profile, Text("COMMANDER", "chip__value", "t-strong")).style.marginLeft = 0;
            Add(profile, Text("LV 7", "chip__label", "t-mono")).style.marginLeft = 10;
            var xp = Add(profile, El("xp-track"));
            xp.Add(El("xp-fill"));
        }

        Label hintBack, hintNavigate, hintSelect;

        void BuildHudBottom()
        {
            var bottom = Add(root, El("hud-bottom"));
            hintNavigate = KeyHint(bottom, "↑ ↓", "ВЫБОР");
            hintSelect = KeyHint(bottom, "ENTER", "ОТКРЫТЬ");
            hintBack = KeyHint(bottom, "ESC", "НАЗАД");
            hintBack.parent.AddToClassList("key-hint--button");
            hintBack.parent.RegisterCallback<ClickEvent>(_ => Back());
            bottom.Add(El("grow"));
            bottom.Add(Text("HORIZON  ·  NASA SPACE APPS 2026", "build-info", "t-mono"));
        }

        Label KeyHint(VisualElement parent, string key, string label)
        {
            var hint = Add(parent, El("key-hint"));
            hint.Add(Text(key, "key-cap", "t-strong"));
            return Add(hint, Text(label, "key-hint__label", "t-strong"));
        }

        void RefreshHints()
        {
            if (hintBack == null) return;
            hintBack.parent.style.display = current == MainScreen ? DisplayStyle.None : DisplayStyle.Flex;
            // Screens with their own bottom bar (settings, load, crew) and the main screen hide the shared one.
            hintBack.parent.parent.style.display = current == SettingsScreen || current == LoadScreen || current == CrewScreen || current == MainScreen
                ? DisplayStyle.None : DisplayStyle.Flex;
        }

        void BuildQuitModal()
        {
            modal = Add(root, El("modal"));
            var box = Add(modal, El("panel", "modal__box"));
            box.Add(Text("ЗАВЕРШИТЬ СЕАНС", "eyebrow", "t-mono"));
            Add(box, Text("Выйти из игры?", "panel__title", "t-strong")).style.fontSize = 34;
            box.Add(Text("Профиль игрока уже сохранён.", "panel__text"));
            var buttons = Add(box, El("modal__buttons"));
            buttons.Add(ButtonEl("ОСТАТЬСЯ", Back));
            buttons.Add(ButtonEl("ВЫЙТИ НА РАБОЧИЙ СТОЛ", Quit, primary: true));
        }

        void ScreenHeader(VisualElement screen, string crumb, string title, string subtitle)
        {
            var head = Add(screen, El("screen-head"));
            var back = new Button(Back);
            back.AddToClassList("back-btn");
            back.Add(Text("ESC", "key-cap", "t-strong"));
            back.Add(Text("BACK", "key-hint__label", "t-strong"));
            head.Add(back);
            var text = Add(head, El("grow"));
            text.Add(Text(Loc.T("MAIN MENU") + "  /  " + Loc.T(crumb), "eyebrow", "t-strong"));
            text.Add(Text(title, "screen-title", "t-strong"));
            if (subtitle != null) text.Add(Text(subtitle, "screen-sub"));
        }

        VisualElement AddScreen(string id)
        {
            var screen = Add(root, El("screen"));
            screen.name = id;
            screens[id] = screen;
            return screen;
        }

        // ─────────────────────────────── helpers ───────────────────────────────

        static T Add<T>(VisualElement parent, T child) where T : VisualElement
        {
            parent.Add(child);
            return child;
        }

        static VisualElement El(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
            return e;
        }

        static Label Text(string text, params string[] classes)
        {
            var l = new Label(Loc.T(text));
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) l.AddToClassList(c);
            return l;
        }

        Button ButtonEl(string label, Action onClick, bool primary = false, bool disabled = false)
        {
            var b = new Button(onClick);
            b.AddToClassList("btn");
            if (primary) b.AddToClassList("btn--primary");
            if (disabled) b.AddToClassList("btn--disabled");
            b.Add(Text(label, "btn__label", "t-strong"));
            return b;
        }

        /// <summary>Small shaded sphere for scenario cards.</summary>
        static Texture2D PlanetThumb(Color tint, int seed)
        {
            const int n = 96;
            var px = new Color32[n * n];
            var light = new Vector3(-0.6f, 0.55f, 0.58f).normalized;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                float r2 = u * u + v * v;
                if (r2 > 1f) { px[y * n + x] = new Color32(0, 0, 0, 0); continue; }
                var normal = new Vector3(u, v, Mathf.Sqrt(1f - r2));
                float band = 0.5f + 0.5f * Mathf.Sin(v * (5f + seed) + Mathf.Sin(u * 3f + seed) * 1.5f);
                float d = Mathf.Clamp01(Vector3.Dot(normal, light)) * 0.9f + 0.06f;
                var c = Color.Lerp(tint * 0.55f, tint, band) * d;
                c += new Color(0.5f, 0.6f, 1f) * Mathf.Pow(1f - normal.z, 3f) * 0.5f;
                c.a = Mathf.Clamp01((1f - Mathf.Sqrt(r2)) * n * 0.5f);
                px[y * n + x] = c;
            }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(px);
            t.Apply(false, true);
            return t;
        }

        // ─────────────────────────────── fonts ───────────────────────────────

        /// <summary>
        /// Bahnschrift (ships with Windows) for the DIN-style look and Consolas for numbers, shared with the in-game menu
        /// (<see cref="MenuKit"/>). TODO: bundle an OFL font asset so every platform looks the same.
        /// </summary>
        void ApplyFonts(VisualElement scope)
        {
            if (scope == root) MenuKit.ApplyRootFonts(root); // classes are applied after the screens are built
            else MenuKit.ApplyFonts(scope);
        }
    }
}
