using SpaceSim.Localization;
using System;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using SpaceSim.Economy;
using SpaceSim.Planets;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Instrument panel. Shows real SI values from <see cref="FlightTelemetry"/> (rounded for display only).
    /// Derived orbital elements are hidden by default (training mode): the player is expected to compute them
    /// from r, v, v_r and ε using the Flight Manual; press O to reveal them for checking.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed partial class FlightHUD : MonoBehaviour, SpaceSim.Runtime.Menu.IFlightPauseHost
    {
        [SerializeField] DisplayUnitSystem units = DisplayUnitSystem.SI;
        [SerializeField] bool showOrbitAnalysis;

        FlightStateController state;
        FlightBody body;
        PlanetRuntime planet;
        SimulationController simulation;
        SpaceCameraController cameraController;
        StarfieldController stars;
        FlightManualUI manual;
        InitialConditionsPanel initialConditions;
        StarSystemRuntime system;
        SolarLightingController sun;
        FloatingOrigin origin;
        Action switchToNextBody;
        bool visible = true;
        bool showMarkers = true;
        bool showMap;
        RocketControls rocketControls;
        HangarUI hangar;
        PlayerSession session;
        ShopUI shop;
        AcademyUI academy;
        AcademyService academyService;
        NavigationUI navigationUi;
        NavigationService navigation;
        OrbitalMapView map;
        SpaceSim.Academy.AcademyHint banner;
        float bannerUntil;
        bool pausedByAcademy;
        Action newRocket;

        /// <summary>What R does (a new rocket at the start planet's pad; set by the bootstrap).</summary>
        public void SetNewRocket(Action action) => newRocket = action;

        CockpitView cockpit;
        // Inside the crewed cabin the cabin's own screens are the instruments: the panel stays hidden until H asks for it.
        bool visibleInCockpit;
        bool InCockpit => cockpit != null && cockpit.Inside;

        /// <summary>
        /// True while the player sits in the crewed cabin with the instrument panel hidden: the other ground panels (landing,
        /// rover) stay off too, so nothing covers the cabin's own screens. H brings them all back.
        /// </summary>
        public static bool CabinViewClear { get; private set; }

        /// <summary>The first-person cabin view (set by the bootstrap).</summary>
        public void SetCockpit(CockpitView view) => cockpit = view;

        public void Initialize(FlightStateController flightState, FlightBody flightBody, PlanetRuntime planetRuntime,
            SimulationController sim, SpaceCameraController cam, StarfieldController starfield,
            FlightManualUI manualUi, InitialConditionsPanel icPanel,
            StarSystemRuntime starSystem, SolarLightingController starLight, FloatingOrigin floatingOrigin, Action nextBody,
            RocketControls controls)
        {
            state = flightState;
            body = flightBody;
            planet = planetRuntime;
            simulation = sim;
            cameraController = cam;
            stars = starfield;
            manual = manualUi;
            initialConditions = icPanel;
            system = starSystem;
            sun = starLight;
            origin = floatingOrigin;
            switchToNextBody = nextBody;
            rocketControls = controls;
            warpClampsSeen = sim.WarpClampCount;
        }

        int warpClampsSeen;

        void Update()
        {
            CabinViewClear = InCockpit && !visibleInCockpit;
            if (state == null) return;
            if (simulation.WarpClampCount != warpClampsSeen)
            {
                warpClampsSeen = simulation.WarpClampCount;
                state.Notify(Loc.T("Ускорение снижено до ×") + UnitFormatter.Number(simulation.WarpLimit, 0) + ": " + WarpZoneName(simulation.WarpZone) + ".");
            }
            if (!system.SpaceUnlocked && (state.Telemetry.AboveSpaceBoundary || (session != null && session.Profile.HasMilestone(ResearchMilestones.ReachSpace))))
            {
                system.UnlockSpace();
                if (state.Telemetry.AboveSpaceBoundary)
                    state.Notify(Loc.T("Вы в космосе! Открыта звёздная система: ") + FlightInput.KeyLabel(InputKey.Y) + Loc.T(" — карта системы, ") +
                                 FlightInput.KeyLabel(InputKey.N) + Loc.T(" — метки тел, ") + FlightInput.KeyLabel(InputKey.J) + Loc.T(" — перелёт к другому телу."));
            }
            CheckMilestones();
            TrackFlight();
            // Hangar and shop are modal: while one is open, camera and flight hotkeys pause (FlightInput.IsTyping).
            FlightInput.ModalWindowOpen = AnyModalOpen;
            if (FlightInput.PauseMenuOpen) return; // the Esc menu owns the keyboard
            if (GUIUtility.keyboardControl == 0)
            {
                if (hangar != null && FlightInput.GetKeyDown(InputKey.B) && (hangar.IsOpen || body.IsOnLaunchPad) && !(shop != null && shop.IsOpen) &&
                    !(academy != null && academy.IsOpen))
                {
                    hangar.Toggle();
                    FlightInput.ModalWindowOpen = AnyModalOpen;
                    return;
                }
                // The shop also opens inside the hangar (buy a module, install it right away).
                if (shop != null && FlightInput.GetKeyDown(InputKey.K) && !(hangar != null && hangar.Busy) && !(academy != null && academy.IsOpen))
                {
                    shop.Toggle();
                    FlightInput.ModalWindowOpen = AnyModalOpen;
                    return;
                }
                if (navigationUi != null && FlightInput.GetKeyDown(InputKey.G) && !AnyModalOpen)
                {
                    navigationUi.Toggle();
                    return;
                }
                if (map != null && FlightInput.GetKeyDown(InputKey.M) && !AnyModalOpen)
                {
                    map.Toggle();
                    return;
                }
                if (academy != null && FlightInput.GetKeyDown(InputKey.L) && !(hangar != null && hangar.IsOpen) && !(shop != null && shop.IsOpen))
                {
                    if (!academy.IsOpen && banner != null && Time.unscaledTime < bannerUntil) academy.OpenHint(banner);
                    else academy.Toggle();
                    banner = null;
                    FlightInput.ModalWindowOpen = AnyModalOpen;
                    return;
                }
            }
            if (FlightInput.IsTyping) return;
            if (FlightInput.GetKeyDown(InputKey.H))
            {
                if (InCockpit) visibleInCockpit = !visibleInCockpit;
                else visible = !visible;
            }
            if (system.SpaceUnlocked)
            {
                if (FlightInput.GetKeyDown(InputKey.N)) showMarkers = !showMarkers;
                if (FlightInput.GetKeyDown(InputKey.Y)) showMap = !showMap;
                if (FlightInput.GetKeyDown(InputKey.J))
                {
                    switchToNextBody?.Invoke();
                    return;
                }
            }
            if (FlightInput.GetKeyDown(InputKey.U))
            {
                units = units == DisplayUnitSystem.SI ? DisplayUnitSystem.Kilometric : DisplayUnitSystem.SI;
                session?.SetUnits(units);
            }
            if (FlightInput.GetKeyDown(InputKey.O)) showOrbitAnalysis = !showOrbitAnalysis;
            if (FlightInput.GetKeyDown(InputKey.F1)) manual.Toggle();
            if (FlightInput.GetKeyDown(InputKey.I)) initialConditions.Toggle();
            if (FlightInput.GetKeyDown(InputKey.R))
            {
                if (newRocket != null) newRocket();
                else
                {
                    body.ResetToLaunchSite();
                    state.Notify(Loc.T("Новая заправленная ракета на стартовой площадке."));
                }
            }
        }


        /// <summary>
        /// Esc: closes the window on top — Academy, shop, hangar, navigation, Flight Manual, initial conditions, then the system
        /// map. Returns false when nothing was open (the Esc menu opens then).
        /// </summary>
        public bool CloseTopWindow()
        {
            if (academy != null && academy.IsOpen) academy.Toggle();
            else if (shop != null && shop.IsOpen) shop.Toggle();
            else if (hangar != null && hangar.IsOpen) hangar.Toggle();
            else if (navigationUi != null && navigationUi.IsOpen) navigationUi.Toggle();
            else if (scienceMap != null && scienceMap.IsOpen) scienceMap.Close();
            else if (manual != null && manual.IsOpen) manual.Toggle();
            else if (initialConditions != null && initialConditions.IsOpen) initialConditions.Toggle();
            else if (map != null && map.IsOpen) map.Close();
            else if (showMap) showMap = false;
            else return false;
            FlightInput.ModalWindowOpen = AnyModalOpen;
            return true;
        }

        ScienceMapUI scienceMap;

        /// <summary>Connects the science map window (F2; it lives outside the flight stack).</summary>
        public void SetScienceMap(ScienceMapUI window) => scienceMap = window;

        /// <summary>Opens or closes the Flight Manual (same as F1).</summary>
        public void ToggleManual() => manual?.Toggle();

        /// <summary>Any window or overlay that Esc would close first.</summary>
        public bool AnyWindowOpen => AnyModalOpen || (navigationUi != null && navigationUi.IsOpen) || (manual != null && manual.IsOpen) ||
                                     (initialConditions != null && initialConditions.IsOpen) || (map != null && map.IsOpen) || showMap ||
                                     (scienceMap != null && scienceMap.IsOpen);

        /// <summary>Whether a window opened over the 3D map is under this point (HUD coordinates): the mouse then belongs to it.</summary>
        public bool WindowCovers(Vector2 point) => (manual != null && manual.IsOpen) || (navigationUi != null && navigationUi.Covers(point));

        /// <summary>Connects the full-screen 3D map (it lives outside the flight stack).</summary>
        public void SetMap(OrbitalMapView mapView) => map = mapView;


        bool ShowPower(SpaceSim.Flight.RocketState r)
        {
            var c = r.Configuration;
            return c.solarPower + c.steadyPower + c.systemsPower + c.batteryEnergy > 0.0 || (r.HasStages && r.CurrentStage.IsElectric);
        }

        /// <summary>Force in the unit that fits it: мН, Н, кН, МН.</summary>
        static string Force(double newtons)
        {
            double a = Math.Abs(newtons);
            if (a <= 0.0) return Loc.T("0 Н");
            if (a < 1.0) return UnitFormatter.Number(newtons * 1000.0, a < 0.01 ? 2 : 0) + Loc.T(" мН");
            if (a < 1000.0) return UnitFormatter.Number(newtons, a < 10.0 ? 2 : 0) + Loc.T(" Н");
            if (a < 1e6) return UnitFormatter.Number(newtons / 1000.0, a < 1e4 ? 2 : 0) + Loc.T(" кН");
            return UnitFormatter.Number(newtons / 1e6, 2) + Loc.T(" МН");
        }

        /// <summary>Mass flow in the unit that fits it: мг/с, г/с, кг/с.</summary>
        static string Flow(double kgPerSecond)
        {
            double a = Math.Abs(kgPerSecond);
            if (a <= 0.0) return Loc.T("0 кг/с");
            if (a < 1e-3) return UnitFormatter.Number(kgPerSecond * 1e6, 2) + Loc.T(" мг/с");
            if (a < 1.0) return UnitFormatter.Number(kgPerSecond * 1000.0, 1) + Loc.T(" г/с");
            return UnitFormatter.Number(kgPerSecond, a < 10.0 ? 2 : 0) + Loc.T(" кг/с");
        }

        static string Power(double watts) =>
            Math.Abs(watts) < 1000.0 ? UnitFormatter.Number(watts, 0) + Loc.T(" Вт") : UnitFormatter.Number(watts / 1000.0, 1) + Loc.T(" кВт");

        /// <summary>
        /// A contextual Academy lesson just raised by the flight: one compact line under the status panel. It never covers the
        /// view centre, fades on its own, and disappears once the condition is gone. L or the button opens the full lesson.
        /// </summary>
        void DrawAcademyBanner(Vector2 screen)
        {
            if (banner == null || academy == null || AnyModalOpen) return;
            bool stillActive = academyService != null && academyService.Advisor.IsActive(banner.Id);
            if (Time.unscaledTime > bannerUntil || (!stillActive && Time.unscaledTime > bannerUntil - 17f))
            {
                banner = null;
                return;
            }
            const float w = 620f, h = 78f;
            var r = new Rect((screen.x - w) * 0.5f, BannerTop, w, h); // below the heading tape (and the manoeuvre card)
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            Color c = banner.Severity == SpaceSim.Academy.HintSeverity.Warning ? HudStyle.Warning
                : banner.Severity == SpaceSim.Academy.HintSeverity.Info ? HudStyle.Accent : Color.white;
            var old = GUI.color;
            GUI.color = c;
            GUI.Label(new Rect(r.x + 10, r.y + 6, w - 200, 18), Loc.T("АКАДЕМИЯ · ") + banner.Title, HudStyle.Title);
            GUI.color = old;
            GUI.Label(new Rect(r.x + 10, r.y + 24, w - 200, h - 28), banner.Message, HudStyle.Small);
            if (GUI.Button(new Rect(r.xMax - 180, r.y + 22, 140, 34), Loc.T("Подробнее (") + FlightInput.KeyLabel(InputKey.L) + ")", HudStyle.Button))
            {
                academy.OpenHint(banner);
                banner = null;
            }
            if (banner != null && GUI.Button(new Rect(r.xMax - 34, r.y + 22, 26, 34), "×", HudStyle.Button)) banner = null;
        }

        bool AnyModalOpen => (hangar != null && hangar.IsOpen) || (shop != null && shop.IsOpen) || (academy != null && academy.IsOpen);

        /// <summary>Connects the navigation window and the flight-plan service (burn guidance, rails warp notices).</summary>
        public void SetNavigation(NavigationUI window, NavigationService service)
        {
            navigationUi = window;
            navigation = service;
        }

        /// <summary>Connects the Flight Academy window and the advisor that raises contextual lessons.</summary>
        public void SetAcademy(AcademyUI academyUi, AcademyService service)
        {
            if (academy != null) academy.OpenChanged -= OnAcademyOpenChanged;
            if (academyService != null) academyService.HintRaised -= OnHintRaised;
            academy = academyUi;
            academyService = service;
            if (academy != null) academy.OpenChanged += OnAcademyOpenChanged;
            if (academyService != null) academyService.HintRaised += OnHintRaised;
        }

        void OnHintRaised(SpaceSim.Academy.AcademyHint hint)
        {
            banner = hint;
            bannerUntil = Time.unscaledTime + 25f;
        }

        /// <summary>Reading a lesson mid-flight should not cost the flight: the simulation pauses while the Academy is open.</summary>
        void OnAcademyOpenChanged(bool isOpen)
        {
            FlightInput.ModalWindowOpen = AnyModalOpen;
            if (isOpen)
            {
                if (!simulation.Paused && !body.Simulation.IsLanded)
                {
                    simulation.SetPaused(true);
                    pausedByAcademy = true;
                    state.Notify(Loc.T("Пауза на время Академии — закройте её (") + FlightInput.KeyLabel(InputKey.L) + Loc.T(" или Esc), чтобы продолжить полёт."));
                }
            }
            else if (pausedByAcademy)
            {
                pausedByAcademy = false;
                simulation.SetPaused(false);
            }
        }

        /// <summary>Connects the hangar window (built with the flight stack).</summary>
        public void SetHangar(HangarUI hangarUi) => hangar = hangarUi;

        /// <summary>Connects the shop / market / research window.</summary>
        public void SetShop(ShopUI shopUi) => shop = shopUi;

        /// <summary>Connects the persistent player session (credits, rewards, saved settings).</summary>
        public void SetSession(PlayerSession playerSession)
        {
            session = playerSession;
            if (session != null) units = session.Profile.settings.units;
        }

        /// <summary>
        /// Research rewards for physical achievements of a real flight (not after the initial-conditions test tool).
        /// "Space" and "escape" count only from the start planet.
        /// </summary>
        void CheckMilestones()
        {
            if (session == null || body.UsedTestTool || body.Simulation.IsLanded) return;
            bool home = system.ActiveIndex == system.IndexOf(homeBodyId);
            FlightTelemetry t = state.Telemetry;
            if (home && t.AboveSpaceBoundary) Reward(ResearchMilestones.ReachSpace);
            if (state.Phase == FlightPhase.OrbitalFlight) Reward(ResearchMilestones.FirstOrbit);
            if (home && state.Phase == FlightPhase.EscapeTrajectory) Reward(ResearchMilestones.EscapeHome);
        }

        void Reward(string id)
        {
            if (session.Profile.HasMilestone(id)) return;
            MilestoneDefinition m = session.Award(id);
            if (m != null)
                state.Notify(Loc.T("ДОСТИЖЕНИЕ: «") + m.Name + "» — +" + m.CreditReward + Loc.T(" кр, +") + UnitFormatter.Number(m.ResearchReward, 0) +
                             Loc.T(" очк. исследований. Новые товары в магазине."));
        }

        string homeBodyId => session != null && session.Profile.visitedBodies.Count > 0 ? session.Profile.visitedBodies[0] : "";

        void OnDestroy()
        {
            FlightInput.ModalWindowOpen = false;
            CabinViewClear = false;
            SetAcademy(null, null);
            // The flight stack is rebuilt on a body switch; never leave the shared simulation paused by a window that is gone.
            if (pausedByAcademy && simulation != null) simulation.SetPaused(false);
            pausedByAcademy = false;
        }

        /// <summary>Top-down map of the system (true scale of orbit radii): the star, orbits and current positions.</summary>
        void DrawSystemMap(Vector2 screen)
        {
            float size = Mathf.Min(screen.x, screen.y) - 200f;
            var rect = new Rect((screen.x - size) * 0.5f, (screen.y - size) * 0.5f, size, size);
            GUI.Box(rect, GUIContent.none, HudStyle.Panel);
            GUI.Label(new Rect(rect.x + 10, rect.y + 6, size - 20, 20), Loc.T("КАРТА СИСТЕМЫ · ") + Loc.T(system.SystemName) + Loc.T(" (вид сверху, ") + FlightInput.KeyLabel(InputKey.Y) + Loc.T(" или Esc — закрыть)"), HudStyle.Title);
            Vector2 c = rect.center;
            double maxR = 1.0;
            var heliocentric = new Vector3d[system.Bodies.Count];
            for (int i = 0; i < system.Bodies.Count; i++)
            {
                heliocentric[i] = system.Bodies[i].Runtime.Model.Position - system.StarPosition;
                if (system.Bodies[i].ParentIndex < 0) maxR = System.Math.Max(maxR, heliocentric[i].Magnitude);
            }
            float scale = (size * 0.45f) / (float)maxR;
            HudStyle.Dot(c, 10f, HudStyle.Warning);
            GUI.Label(new Rect(c.x + 8, c.y - 9, 200, 18), Loc.T(system.Star.name), HudStyle.Label);
            for (int i = 0; i < system.Bodies.Count; i++)
            {
                var b = system.Bodies[i];
                Vector3d hp = heliocentric[i];
                if (b.ParentIndex < 0)
                {
                    float orbitR = (float)hp.Magnitude * scale;
                    for (int k = 0; k < 96; k++)
                    {
                        float a = k / 96f * Mathf.PI * 2f;
                        HudStyle.Dot(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * orbitR, 2f, new Color(1f, 1f, 1f, 0.25f));
                    }
                }
                var p = c + new Vector2((float)hp.x, -(float)hp.y) * scale;
                if (b.ParentIndex >= 0) p += new Vector2(10f, 10f); // moons: offset so they don't hide the parent
                bool active = i == system.ActiveIndex;
                HudStyle.Dot(p, active ? 9f : 7f, active ? HudStyle.Danger : HudStyle.Accent);
                double au = hp.Magnitude / PhysicalConstants.AstronomicalUnit;
                GUI.Label(new Rect(p.x + 8, p.y - 9, 280, 18), b.Name + (active ? Loc.T("  ◄ вы здесь") : "") + (b.ParentIndex < 0 ? "  " + UnitFormatter.Number(au, 3) + Loc.T(" а.е.") : ""), HudStyle.Label);
            }
        }

        /// <summary>Name and distance labels over the star and every other body (direction-only projection).</summary>
        void DrawBodyMarkers()
        {
            Camera cam = cameraController.Camera;
            if (cam == null) return;
            Vector3d viewer = origin.ViewerPosition;
            Marker(cam, system.StarPosition - viewer, system.Star.radius, Loc.T(system.Star.name), HudStyle.Warning);
            for (int i = 0; i < system.Bodies.Count; i++)
            {
                if (i == system.ActiveIndex) continue;
                var b = system.Bodies[i];
                if (b.Runtime.Model == null) continue;
                Marker(cam, b.Runtime.Model.Position - viewer, b.Runtime.Model.Radius, b.Name, HudStyle.Accent);
            }
        }

        static void Marker(Camera cam, Vector3d relative, double radius, string name, Color color)
        {
            double distance = relative.Magnitude;
            if (distance <= 0.0) return;
            Vector3 dir = CoordinateSystem.ToUnityVector(relative / distance);
            Vector3 screenPoint = cam.WorldToScreenPoint(cam.transform.position + dir * 1000f);
            if (screenPoint.z <= 0f) return; // behind the camera
            float s = HudStyle.Scale;
            var p = new Vector2(screenPoint.x / s, (Screen.height - screenPoint.y) / s);
            string dist = distance - radius > 1.0e9
                ? UnitFormatter.Number((distance - radius) / PhysicalConstants.AstronomicalUnit, 3) + Loc.T(" а.е.")
                : UnitFormatter.Number((distance - radius) / 1000.0, 0) + Loc.T(" км");
            var c = GUI.color;
            GUI.color = color;
            GUI.Label(new Rect(p.x - 4, p.y - 9, 20, 18), "◇", HudStyle.Label);
            GUI.Label(new Rect(p.x + 10, p.y - 9, 320, 18), name + " · " + dist, HudStyle.Label);
            GUI.color = c;
        }

        static string WarpZoneName(TimeWarpZone zone)
        {
            switch (zone)
            {
                case TimeWarpZone.Surface: return Loc.T("взлёт и посадка");
                case TimeWarpZone.UpperAtmosphere: return Loc.T("верхняя атмосфера");
                case TimeWarpZone.PoweredFlight: return Loc.T("работает двигатель");
                case TimeWarpZone.Suborbital: return Loc.T("ещё не орбита");
                case TimeWarpZone.HighArc: return Loc.T("дуга с возвратом");
                case TimeWarpZone.LowOrbit: return Loc.T("низкая орбита");
                case TimeWarpZone.HighOrbit: return Loc.T("высокая орбита");
                case TimeWarpZone.LowThrustFlight: return Loc.T("малая тяга на безопасной траектории");
                default: return Loc.T("межпланетный полёт");
            }
        }

        double Conv(double metersPerSecond) => units == DisplayUnitSystem.SI ? metersPerSecond : metersPerSecond / 1000.0;
        int Dec => units == DisplayUnitSystem.SI ? 1 : 4;

        public static string PhaseName(FlightPhase p)
        {
            switch (p)
            {
                case FlightPhase.Ground: return Loc.T("НА ПОВЕРХНОСТИ");
                case FlightPhase.AtmosphericAscent: return Loc.T("ПОДЪЁМ В АТМОСФЕРЕ");
                case FlightPhase.UpperAtmosphere: return Loc.T("ВЕРХНИЕ СЛОИ АТМОСФЕРЫ");
                case FlightPhase.Spaceflight: return Loc.T("КОСМИЧЕСКИЙ ПОЛЁТ (НЕ ОРБИТА)");
                case FlightPhase.OrbitalFlight: return Loc.T("ОРБИТАЛЬНЫЙ ПОЛЁТ");
                case FlightPhase.EscapeTrajectory: return Loc.T("ТРАЕКТОРИЯ УХОДА");
                case FlightPhase.AtmosphericDescent: return Loc.T("СНИЖЕНИЕ В АТМОСФЕРЕ");
                case FlightPhase.Landing: return Loc.T("ЗАХОД НА ПОСАДКУ");
                default: return p.ToString();
            }
        }

        public static string ReasonText(FlightPhaseReason r)
        {
            switch (r)
            {
                case FlightPhaseReason.RestingOnSurface: return Loc.T("Аппарат покоится на грунте и вращается вместе с планетой.");
                case FlightPhaseReason.ClimbingInDenseAir: return Loc.T("Вертикальная скорость > 0, плотный воздух.");
                case FlightPhaseReason.DescendingInDenseAir: return Loc.T("Вертикальная скорость < 0, плотный воздух.");
                case FlightPhaseReason.ThinAirAboveThreshold: return Loc.T("ρ/ρ₀ ниже порога верхней атмосферы; сопротивление ещё действует.");
                case FlightPhaseReason.CloseToSurfaceDescending: return Loc.T("Снижение у поверхности: следите за вертикальной скоростью.");
                case FlightPhaseReason.AboveBoundarySuborbital: return Loc.T("Суборбитальная траектория.");
                case FlightPhaseReason.AboveBoundaryPeriapsisBelowSurface: return Loc.T("Выше условной границы, но перицентр под поверхностью — траектория пересекает планету.");
                case FlightPhaseReason.AboveBoundaryPeriapsisInAtmosphere: return Loc.T("ε < 0, но перицентр в атмосфере — сопротивление разрушит орбиту.");
                case FlightPhaseReason.BoundOrbitPeriapsisClear: return Loc.T("ε < 0 и перицентр выше плотной атмосферы.");
                case FlightPhaseReason.UnboundEnergy: return Loc.T("ε ≥ 0 — траектория не замкнута, аппарат уходит от планеты.");
                default: return r.ToString();
            }
        }
    }
}
