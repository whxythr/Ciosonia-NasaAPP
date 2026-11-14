using SpaceSim.Economy;
using SpaceSim.Localization;
using UnityEngine;
using UnityEngine.UIElements;
using static SpaceSim.Runtime.Menu.MenuKit;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>
    /// Tab in flight: the crew roster (<see cref="CrewView"/>) over the 3D view. The simulation pauses while it is open (like the
    /// pause screen) and the flight hotkeys are ignored; Tab or Esc closes it.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class CrewScreen : MonoBehaviour, CrewView.IHost
    {
        UIDocument document;
        PanelSettings panel;
        VisualElement root;
        CrewView view;
        SpaceSimBootstrap boot;
        bool open, pausedByMe;
        int openedFrame = -10;

        public bool IsOpen => open;

        static bool anyOpen;
        static int closedFrame = -10;
        /// <summary>The crew screen has the keyboard this frame (the pause screen must not also react to that Esc).</summary>
        public static bool OwnsEscape => anyOpen || Time.frameCount == closedFrame;

        public void Initialize(SpaceSimBootstrap bootstrap) => boot = bootstrap;

        void Awake()
        {
            document = GetComponent<UIDocument>();
            panel = CreatePanel("CrewPanel (runtime)", opaque: false, sortingOrder: 90);
            document.panelSettings = panel;
        }

        void OnEnable()
        {
            if (document == null) return;
            VisualElement d = document.rootVisualElement;
            d.Clear();
            StretchRoot(d);
            AddStyleSheet(d, MenuStyleSheet);
            AddStyleSheet(d, GameStyleSheet);
            root = El("game-menu");
            root.style.display = DisplayStyle.None;
            d.Add(root);
            ApplyRootFonts(root);
            view = new CrewView(root, this);
        }

        void OnDestroy()
        {
            if (open) Close();
            if (panel != null) Destroy(panel);
        }

        void Update()
        {
            if (FlightInput.PauseMenuOpen && !open) return;
            if (open)
            {
                if (Time.frameCount == openedFrame) return;
                if (FlightInput.EscapePressed || FlightInput.GetKeyDown(InputKey.Tab)) Close();
                return;
            }
            // Tab opens it when the keyboard is free (no text field, no modal window such as the hangar).
            if (!FlightInput.IsTyping && FlightInput.GetKeyDown(InputKey.Tab)) Open();
        }

        public void Toggle()
        {
            if (open) Close();
            else Open();
        }

        public void Open()
        {
            if (open || root == null) return;
            open = true;
            anyOpen = true;
            openedFrame = Time.frameCount;
            // Owns the keyboard like the pause screen: the HUD hides, flight keys pause, Esc does not reach the pause screen.
            FlightInput.PauseMenuOpen = true;
            if (boot != null && boot.Simulation != null && !boot.Simulation.Paused)
            {
                boot.Simulation.SetPaused(true);
                pausedByMe = true;
            }
            view.Refresh();
            root.style.display = DisplayStyle.Flex;
            root.schedule.Execute(() => { if (open) root.AddToClassList("game-menu--open"); }).StartingIn(16);
        }

        public void Close()
        {
            if (!open) return;
            open = false;
            anyOpen = false;
            closedFrame = Time.frameCount;
            FlightInput.PauseMenuOpen = false;
            if (pausedByMe && boot != null && boot.Simulation != null) boot.Simulation.SetPaused(false);
            pausedByMe = false;
            root.RemoveFromClassList("game-menu--open");
            root.schedule.Execute(() => { if (!open) root.style.display = DisplayStyle.None; }).StartingIn(200);
        }

        // ─────────────────────────────── CrewView.IHost ───────────────────────────────

        public PlayerProfile Profile => boot != null && boot.Session != null ? boot.Session.Profile : null;
        public bool InFlight => boot != null && boot.Hud != null && boot.Hud.FlightNumber > 0;
        public string FlightTitle => boot != null && boot.Hud != null ? boot.Hud.FlightTitle : "";
        public string FlightClock => boot != null && boot.Hud != null ? boot.Hud.MissionClock : "";
        public bool SendToTraining(string id) => boot != null && boot.Session != null && boot.Session.SendCrewToTraining(id);
        public bool Hire(string id) => boot != null && boot.Session != null && boot.Session.HireCrew(id);
        void CrewView.IHost.Close() => Close();
    }
}
