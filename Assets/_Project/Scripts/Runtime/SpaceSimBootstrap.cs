using SpaceSim.Localization;
using SpaceSim.Physics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Builds the game world at runtime: simulation clock, floating origin, the whole star system (every planet and
    /// moon with physics, procedural surface and atmosphere, moving on its orbit), the star, the star field, and a
    /// "flight stack" on the active body (unpowered test probe on the launch pad, camera, HUD, Flight Manual).
    /// Jumping to another body (key J) rebuilds only the flight stack. Drop on an empty GameObject in any scene.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SpaceSimBootstrap : MonoBehaviour
    {
        [Header("Star system")]
        [Tooltip("System asset (SpaceSim ▸ Create Home System Assets). If empty, the built-in home system is used: " +
                 "Teonan, Quoris, Hermeous Mora and its moon.")]
        [SerializeField] StarSystemDefinition starSystem;

        [Header("Shaders (assigned by SpaceSim ▸ Setup; looked up by name if empty)")]
        [SerializeField] Shader planetSurfaceShader;
        [SerializeField] Shader atmosphereShader;
        [SerializeField] Shader starfieldShader;
        [SerializeField] Shader sunDiskShader;
        [Tooltip("Lit shader for the probe and the launch pad (URP/Lit or Standard).")]
        [SerializeField] Shader litShader;

        Camera viewCamera;
        StarfieldController stars;
        GameObject flightStack;
        /// <summary>The planet the game starts on: home of the launch pad, the market and sample delivery.</summary>
        string startBodyId = SpaceSim.Planets.HomeSystemCatalog.HomeWorldId;
        /// <summary>The player's current rocket design; survives jumps between bodies (persisted by the save system in M3).</summary>
        SpaceSim.Vehicles.RocketBlueprint design = SpaceSim.Vehicles.RocketBlueprint.CreateDefault();

        public SimulationController Simulation { get; private set; }
        public FloatingOrigin Origin { get; private set; }
        public StarSystemRuntime System { get; private set; }
        public PlanetRuntime Planet => System.Active;
        public PlanetVisualController PlanetVisual => System.ActiveBody.Visual;
        public FlightBody Body { get; private set; }
        public FlightStateController FlightState { get; private set; }
        public SpaceCameraController CameraController { get; private set; }
        public SolarLightingController Sun { get; private set; }
        public GravityField Gravity { get; private set; }
        public PlayerSession Session { get; private set; }
        /// <summary>Flight Academy: lessons, calculators and the contextual advisor for the current flight.</summary>
        public AcademyService Academy { get; private set; }
        /// <summary>Flight plan, trajectory prediction and burn guidance (survives frame handoffs).</summary>
        public NavigationService Navigation { get; private set; }
        /// <summary>Surface exploration of the current flight stack (sample sites, arm, astronaut on foot).</summary>
        public SurfaceExplorer Explorer { get; private set; }
        /// <summary>Rover drive, drills and lamps of the current flight stack.</summary>
        public RoverController Rover { get; private set; }
        /// <summary>The view from inside the crewed cabin of the current flight stack.</summary>
        public CockpitView Cockpit { get; private set; }
        /// <summary>Radio-link indicator of a remote-controlled vehicle.</summary>
        public CommLinkOverlay CommLink { get; private set; }
        /// <summary>The in-game Esc menu (settings, main menu, quit).</summary>
        public SpaceSim.Runtime.Menu.GameMenu EscMenu { get; private set; }
        /// <summary>The crew roster opened with Tab.</summary>
        public SpaceSim.Runtime.Menu.CrewScreen Crew { get; private set; }
        /// <summary>Applies the player's brightness setting to the camera.</summary>
        public ScreenBrightness Brightness { get; private set; }
        /// <summary>HUD of the current flight stack.</summary>
        public FlightHUD Hud { get; private set; }
        /// <summary>The assembly hangar of the current flight stack (window + the 3D hall next to the pad).</summary>
        public HangarUI Hangar { get; private set; }
        /// <summary>The full-screen 3D map of the star system (survives frame handoffs).</summary>
        public OrbitalMapView Map { get; private set; }

        void Awake()
        {
            // Every SpaceSim object sets world poses from double precision; the root must be an identity transform.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            GameSettings.ApplyAtStart();
            MusicDirector.Ensure()?.AttachGame(this); // soundtrack by location (persistent player, survives scene loads)
            ResolveShaders();
            Gravity = new GravityField();

            Simulation = Create<SimulationController>("Simulation");
            Origin = Create<FloatingOrigin>("FloatingOrigin");

            StarSystemDefinition def = starSystem != null ? starSystem : StarSystemDefinition.CreateHomeSystem();
            startBodyId = def.startBodyId;
            System = Create<StarSystemRuntime>("StarSystem");
            System.Initialize(def, Simulation, Origin, planetSurfaceShader, atmosphereShader);
            int start = System.IndexOf(def.startBodyId);

            // Persistent player profile: currency, purchases, rocket design, samples, progress.
            Session = Create<PlayerSession>("PlayerSession");
            Session.Initialize(def.startBodyId);
            design = Session.Profile.blueprint.Clone();
            System.SetActive(start >= 0 ? start : 0, Gravity);

            var environment = new GameObject("Environment").transform;
            environment.SetParent(transform, false);
            var sunGo = new GameObject("StarLight");
            sunGo.transform.SetParent(environment, false);
            Sun = sunGo.AddComponent<SolarLightingController>();
            Sun.Initialize(System, Origin, sunDiskShader);

            stars = new GameObject("Starfield").AddComponent<StarfieldController>();
            stars.transform.SetParent(environment, false);
            stars.Initialize(System, Origin, Sun, starfieldShader);

            viewCamera = Camera.main;
            if (viewCamera == null)
            {
                var go = new GameObject("SpaceCamera") { tag = "MainCamera" };
                viewCamera = go.AddComponent<Camera>();
            }
            CameraController = viewCamera.GetComponent<SpaceCameraController>();
            if (CameraController == null) CameraController = viewCamera.gameObject.AddComponent<SpaceCameraController>();
            System.SetCamera(viewCamera);
            // Player brightness (Settings): post-exposure on this camera, off at 100 %.
            Brightness = viewCamera.GetComponent<ScreenBrightness>();
            if (Brightness == null) Brightness = viewCamera.gameObject.AddComponent<ScreenBrightness>();
            Brightness.Initialize(viewCamera);
            // Tone mapping, bloom and the grade of the whole picture.
            var look = viewCamera.GetComponent<GraphicsPostProcessing>();
            if (look == null) look = viewCamera.gameObject.AddComponent<GraphicsPostProcessing>();
            look.Initialize(viewCamera);

            Navigation = Create<NavigationService>("Navigation");
            Navigation.Initialize(this);
            Map = Create<OrbitalMapView>("OrbitalMap");
            Map.Initialize(this, viewCamera, stars);

            // The Esc menu lives outside the flight stack (it survives body switches); the HUD of each stack plugs into it.
            EscMenu = Create<SpaceSim.Runtime.Menu.GameMenu>("GameMenu");
            EscMenu.Initialize(Simulation);
            // Tab: the crew roster (also outside the flight stack).
            Crew = Create<SpaceSim.Runtime.Menu.CrewScreen>("CrewScreen");
            Crew.Initialize(this);

            // Science instruments and the science map (outside the flight stack: maps survive body switches).
            Science = Create<ScienceSession>("Science");
            Science.Initialize(Session, System);
            ScienceMap = Science.gameObject.AddComponent<ScienceMapUI>();
            ScienceMap.Initialize(Science, System);

            BuildFlightStack();
        }

        /// <summary>Orbital scanning and the saved science maps.</summary>
        public ScienceSession Science { get; private set; }
        /// <summary>The science map window (F2).</summary>
        public ScienceMapUI ScienceMap { get; private set; }

        void Update()
        {
            // Sphere-of-influence handoff: the vehicle entered another body's SOI — continue the same flight in that body's frame.
            if (Body != null && Body.NeedsHandoff(out int index)) HandoffTo(index);
        }

        /// <summary>
        /// Moves the simulation frame to body <paramref name="index"/> without touching the flight: same rocket, propellant and
        /// trajectory, re-expressed relative to the new centre. A real arrival counts as a visit (not after the test tools).
        /// </summary>
        void HandoffTo(int index)
        {
            FlightBody.VehicleCarry carry = Body.CarryTo(index);
            string from = System.ActiveBody.Name;
            Simulation.Unregister(Body);
            if (flightStack != null) Destroy(flightStack);
            System.SetActive(index, Gravity);
            BuildFlightStack(carry);
            // An encounter can be over in a fraction of a second at ×10⁶: stop the warp at every sphere change, the player decides.
            bool warped = Simulation.TimeWarp > 1.0;
            Simulation.ResetWarp();
            if (warped) FlightState.Notify(Loc.T("Ускорение времени сброшено: вход в новую сферу влияния."));
            string name = System.ActiveBody.Name;
            FlightState.Notify(Loc.T("Сфера влияния: ") + name + Loc.T(" (из ") + from + Loc.T("). Теперь притягивает ") + name + Loc.T("; система отсчёта — центр ") + name + ".");
            if (!carry.UsedTestTool && Session != null)
            {
                bool first = !Session.Profile.visitedBodies.Contains(System.ActiveBody.Id);
                Session.MarkVisited(System.ActiveBody.Id);
                if (first) FlightState.Notify(Loc.T("Открытие: ") + name + Loc.T(" — первое посещение."));
                if (!Session.Profile.HasMilestone(SpaceSim.Economy.ResearchMilestones.Interplanetary))
                {
                    var m = Session.Award(SpaceSim.Economy.ResearchMilestones.Interplanetary);
                    if (m != null) FlightState.Notify(Loc.T("ДОСТИЖЕНИЕ: «") + m.Name + "» — +" + m.CreditReward + Loc.T(" кр."));
                }
            }
        }

        /// <summary>Moves the probe to another body's launch pad (that body becomes the simulation frame's centre).</summary>
        public void SwitchToBody(int index)
        {
            if (Body != null) Simulation.Unregister(Body); // stop stepping the old probe immediately
            if (flightStack != null) Destroy(flightStack);
            System.SetActive(index, Gravity);
            BuildFlightStack();
            FlightState.Notify(Loc.T("Активное тело: ") + System.ActiveBody.Name + Loc.T(". Зонд на стартовой площадке."));
        }

        public void SwitchToNextBody() => SwitchToBody((System.ActiveIndex + 1) % System.Bodies.Count);

        void BuildFlightStack(FlightBody.VehicleCarry carry = null)
        {
            PlanetRuntime planet = System.Active;
            flightStack = new GameObject("FlightStack · " + planet.Definition.displayName);
            flightStack.transform.SetParent(transform, false);

            var site = new GameObject("LaunchSite").AddComponent<LaunchSite>();
            site.transform.SetParent(flightStack.transform, false);
            site.Initialize(planet, Origin, Simulation, litShader);
            // The assembly hangar next to the pad (part of the launch site, so it rides on the planet like the pad).
            var hangarBuilding = new GameObject("Hangar").AddComponent<HangarBuilding>();
            hangarBuilding.transform.SetParent(site.transform, false);
            hangarBuilding.Initialize(site, litShader, ModuleVisualLibrary.Active);

            var bodyGo = new GameObject("TestProbe");
            bodyGo.transform.SetParent(flightStack.transform, false);
            Body = bodyGo.AddComponent<FlightBody>();
            Body.Initialize(planet, Gravity, Simulation, Origin, litShader, design, Session.Owns, System.Navigation, System.ActiveIndex, carry);
            Body.BlueprintApplied += applied =>
            {
                design = applied;
                Session.SetBlueprint(applied);
            };

            var stateGo = new GameObject("FlightState");
            stateGo.transform.SetParent(flightStack.transform, false);
            FlightState = stateGo.AddComponent<FlightStateController>();
            FlightState.Initialize(Body, planet);

            CameraController.Initialize(viewCamera, planet, Body, Origin, Simulation);

            var ui = new GameObject("UI");
            ui.transform.SetParent(flightStack.transform, false);
            var manual = ui.AddComponent<FlightManualUI>();
            manual.Initialize(planet);
            var ic = ui.AddComponent<InitialConditionsPanel>();
            ic.Initialize(Body, planet, FlightState);
            var hud = ui.AddComponent<FlightHUD>();
            var controls = ui.AddComponent<RocketControls>();
            controls.Initialize(Body, FlightState);
            hud.Initialize(FlightState, Body, planet, Simulation, CameraController, stars, manual, ic, System, Sun, Origin, SwitchToNextBody, controls);
            var hangarFacility = ui.AddComponent<HangarFacility>();
            hangarFacility.Initialize(Body, hangarBuilding, CameraController, litShader);
            var hangar = ui.AddComponent<HangarUI>();
            hangar.Initialize(Body, FlightState, hangarFacility);
            hud.SetHangar(hangar);
            Hangar = hangar;
            hud.SetSession(Session);
            var shop = ui.AddComponent<ShopUI>();
            shop.Initialize(Session, FlightState, System);
            hud.SetShop(shop);
            hangar.SetShop(shop);
            hangar.SetSession(Session);
            Academy = ui.AddComponent<AcademyService>();
            Academy.Initialize(Body, FlightState, System, Simulation);
            var academyUi = ui.AddComponent<AcademyUI>();
            academyUi.Initialize(Academy, Session, FlightState);
            hud.SetAcademy(academyUi, Academy);
            var navigationUi = ui.AddComponent<NavigationUI>();
            navigationUi.Initialize(Navigation, Session, FlightState, System);
            hud.SetNavigation(navigationUi, Navigation);
            hud.SetMap(Map);
            hud.SetScienceMap(ScienceMap);
            hud.SetNewRocket(NewRocketAtHome);
            Hud = hud;
            if (EscMenu != null)
            {
                EscMenu.SetWindowCloser(hud.CloseTopWindow);
                EscMenu.SetFlight(hud);
            }
            Explorer = ui.AddComponent<SurfaceExplorer>();
            Explorer.Initialize(Body, FlightState, planet, CameraController, controls, Session, Simulation, Origin, litShader);
            Rover = ui.AddComponent<RoverController>();
            Rover.Initialize(Body, FlightState, planet, Explorer, controls, Session, Simulation);
            // The view from the crewed cabin (zoom in past the closest distance) and the radio-link indicator of a probe.
            Cockpit = ui.AddComponent<CockpitView>();
            Cockpit.Initialize(Body, FlightState, CameraController, Simulation, planet, litShader);
            hud.SetCockpit(Cockpit);
            CommLink = ui.AddComponent<CommLinkOverlay>();
            CommLink.Initialize(Body, FlightState, planet);
            Body.Touchdown += OnTouchdown;
        }

        /// <summary>
        /// Consequences of a touchdown: the verdict, delivery of the cargo on the start planet, the cargo lost in a crash,
        /// and the research milestone for the first landing on another body (real flights only).
        /// </summary>
        void OnTouchdown(SpaceSim.Physics.LandingOutcome outcome)
        {
            bool home = System.ActiveBody.Id == startBodyId;
            if (Body.IsOnLaunchPad && home && outcome.Result == SpaceSim.Physics.LandingResult.Soft && outcome.VerticalSpeed < 0.5) return; // settling on the pad
            FlightState.Notify(outcome.Reason + (outcome.Result == SpaceSim.Physics.LandingResult.Crash ? " " + FlightInput.KeyLabel(InputKey.R) + Loc.T(" — новая ракета на старте.") : ""));
            if (outcome.Result == SpaceSim.Physics.LandingResult.Crash)
            {
                int lost = Session.LoseCargo();
                if (lost > 0) FlightState.Notify(Loc.T("Потеряно образцов: ") + lost + ".");
                return;
            }
            if (home)
            {
                int delivered = Session.DeliverCargo();
                if (delivered > 0) FlightState.Notify(Loc.T("Доставлено образцов: ") + delivered + Loc.T(" — их можно продать на рынке (") + FlightInput.KeyLabel(InputKey.K) + ").");
            }
            else if (!Body.UsedTestTool) Session.CrewVisited(System.ActiveBody.Id);
            if (!home && !Body.UsedTestTool && !Session.Profile.HasMilestone(SpaceSim.Economy.ResearchMilestones.OtherworldLanding))
            {
                var m = Session.Award(SpaceSim.Economy.ResearchMilestones.OtherworldLanding);
                if (m != null) FlightState.Notify(Loc.T("ДОСТИЖЕНИЕ: «") + m.Name + "» — +" + m.CreditReward + Loc.T(" кр."));
            }
        }

        /// <summary>R: a fresh rocket on the start planet's pad. Whatever was on board is lost.</summary>
        public void NewRocketAtHome()
        {
            Session?.EndCrewFlight(); // the old flight is over: its crew is free again
            int lost = Session != null ? Session.LoseCargo() : 0;
            int home = System.IndexOf(startBodyId);
            if (home >= 0 && home != System.ActiveIndex) SwitchToBody(home);
            else
            {
                Explorer?.EndEva();
                Body.ResetToLaunchSite();
                FlightState.Notify(Loc.T("Новая заправленная ракета на стартовой площадке."));
            }
            if (lost > 0) FlightState.Notify(Loc.T("Образцы на борту старой ракеты потеряны: ") + lost + ".");
        }

        /// <summary>«В ангар» of the pause menu: a fresh rocket on the start pad, then the hangar opens (next frame: the stack may be rebuilt).</summary>
        public void RevertToAssembly()
        {
            NewRocketAtHome();
            StartCoroutine(OpenHangarSoon());
        }

        System.Collections.IEnumerator OpenHangarSoon()
        {
            yield return null;
            yield return null;
            if (Hangar != null && !Hangar.IsOpen && Body != null && Body.IsOnLaunchPad) Hangar.Toggle();
        }

        T Create<T>(string objectName) where T : Component
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        void ResolveShaders()
        {
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            if (planetSurfaceShader == null) planetSurfaceShader = Shader.Find("SpaceSim/PlanetSurface");
            if (atmosphereShader == null) atmosphereShader = Shader.Find("SpaceSim/Atmosphere");
            if (starfieldShader == null) starfieldShader = Shader.Find("SpaceSim/Starfield");
            if (sunDiskShader == null) sunDiskShader = Shader.Find("SpaceSim/SunDisk");
            if (litShader == null) litShader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
            if (litShader == null) litShader = Shader.Find("Standard");
            if (planetSurfaceShader == null || atmosphereShader == null || starfieldShader == null)
                Debug.LogError("[SpaceSim] SpaceSim shaders not found. Run 'SpaceSim ▸ Setup Project' or assign them on the bootstrap.", this);
        }
    }
}
