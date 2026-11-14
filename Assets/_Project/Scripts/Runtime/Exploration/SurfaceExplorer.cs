using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Economy;
using SpaceSim.Exploration;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;
using SpaceSim.Units;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Surface exploration after landing: the sample sites around the lander, the lander's collector arm, and the astronaut
    /// on foot (first-person, <see cref="SurfaceWalker"/> physics with the planet's own gravity). E — go out / take the
    /// nearest sample / come back in; Shift — sprint (speed and endurance from <see cref="Locomotion"/>); T (the same
    /// rebindable key as the cabin view) — first / third person: the camera follows behind and above the body. The suited body (<see cref="AstronautModel"/>) is drawn and casts shadows; in first person the camera sits in
    /// the helmet and moves with the body's lean and stride. All rules come from Core (<see cref="SampleCollection"/>,
    /// <see cref="SampleSiteGenerator"/>, <see cref="Locomotion"/>).
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class SurfaceExplorer : MonoBehaviour, SpaceCameraController.IFirstPersonView
    {
        const float MouseSensitivity = 0.15f;
        const double BoardingDistance = 8.0;
        /// <summary>Distance of the camera behind the astronaut in the third-person view [m].</summary>
        const double ThirdPersonDistance = 3.4;
        /// <summary>Highest point the third-person camera looks at above the feet [m] (lower for a short robot).</summary>
        const double ThirdPersonPivotMax = 1.25;
        /// <summary>Clearance of the third-person camera above the ground [m].</summary>
        const double ThirdPersonClearance = 0.4;
        /// <summary>Points checked on the line from the body to the third-person camera (a hill in between pulls it in).</summary>
        const int ThirdPersonGroundSamples = 6;
        /// <summary>Extra field of view at full sprint [degrees].</summary>
        const float SprintFovKick = 5f;

        FlightBody body;
        FlightStateController state;
        PlanetRuntime planet;
        SpaceCameraController cameraController;
        RocketControls controls;
        PlayerSession session;
        SimulationController simulation;
        FloatingOrigin origin;
        Shader lit;

        SurfaceWalker walker;
        List<SampleSite> sites;
        readonly Dictionary<string, Transform> siteVisuals = new Dictionary<string, Transform>();
        Transform siteRoot;
        Vector3d sitesAnchor;
        float yaw, lookPitch;
        GUIStyle note;

        IExplorerBody model;
        RobotVisorOverlay visor;
        RobotStatusPanel statusPanel;
        readonly RobotStatus robot = new RobotStatus();
        int seenTouchdowns, seenImpacts;
        bool warnedBlind;
        bool thirdPerson;
        Vector3 eyeLocal = new Vector3(0f, (float)SurfaceWalker.EyeHeight, 0f);
        /// <summary>Height above the feet the third-person camera looks at [m]: just above the body's eye at rest.</summary>
        double pivotHeight = ThirdPersonPivotMax;
        float baseFov = -1f, fovKick;

        public bool Eva => walker != null;
        /// <summary>The rover of this flight stack (set by <see cref="RoverController"/>): while it drives, E belongs to it.</summary>
        public RoverController Rover { get; set; }
        public float Yaw => yaw;
        public float LookPitch => lookPitch;
        /// <summary>
        /// The body while outside (null inside the rocket): the robot from the imported model when it is set up
        /// (<see cref="RobotModelSettings"/>), else the procedural astronaut.
        /// </summary>
        public IExplorerBody Model => model;
        public bool ThirdPerson => thirdPerson;
        /// <summary>The robot's condition (kept aboard between exits, reset with every new rocket).</summary>
        public RobotStatus Robot => robot;
        public RobotVisorOverlay VisorOverlay => visor;
        public RobotStatusPanel StatusPanel => statusPanel;
        /// <summary>The surroundings on the ground here, as the robot feels them.</summary>
        public RobotEnvironment SurfaceEnvironment => RobotEnvironment.AtSurface(planet.Model.Properties, planet.Definition.hazards);

        // Scripted input for tests and the development bridge (the player uses the keyboard).
        double driveForward, driveRight;
        float driveUntil;
        bool driveRun, driveJump;

        public void Drive(double forward, double right, float seconds, bool run)
        {
            driveForward = forward;
            driveRight = right;
            driveRun = run;
            driveUntil = Time.unscaledTime + seconds;
        }

        public void Jump() => driveJump = true;
        public void Face(float yawDegrees) => yaw = Mathf.Repeat(yawDegrees, 360f);
        /// <summary>Head pitch [degrees, + up], as the right mouse button would set it.</summary>
        public void Look(float pitchDegrees) => lookPitch = Mathf.Clamp(pitchDegrees, -85f, 85f);

        /// <summary>T: first person ↔ a view from behind and above the robot (or the astronaut).</summary>
        public void ToggleThirdPerson() => SetThirdPerson(!thirdPerson);

        /// <summary>Third person draws the whole body (the head too) and hides the visor overlay; first person puts the camera in the head.</summary>
        public void SetThirdPerson(bool value)
        {
            thirdPerson = value;
            if (model != null) model.SetFirstPerson(!thirdPerson);
            if (visor != null && Eva) ApplyVisor();
        }
        public SurfaceWalker Walker => walker;
        public IReadOnlyList<SampleSite> Sites => sites;
        /// <summary>Nearest uncollected site and its distance [m] from the astronaut (EVA) or the lander.</summary>
        public SampleSite Nearest { get; private set; }
        public double NearestDistance { get; private set; } = double.PositiveInfinity;

        public void Initialize(FlightBody flightBody, FlightStateController flightState, PlanetRuntime planetRuntime, SpaceCameraController cam,
            RocketControls rocketControls, PlayerSession playerSession, SimulationController sim, FloatingOrigin floatingOrigin, Shader litShader)
        {
            body = flightBody;
            state = flightState;
            planet = planetRuntime;
            cameraController = cam;
            controls = rocketControls;
            session = playerSession;
            simulation = sim;
            origin = floatingOrigin;
            lit = litShader;
            visor = gameObject.AddComponent<RobotVisorOverlay>();
            statusPanel = gameObject.AddComponent<RobotStatusPanel>();
            statusPanel.Status = robot;
            if (body != null)
            {
                body.Touchdown += OnVehicleTouchdown;
                body.RocketReset += robot.Reset;
            }
        }

        /// <summary>The robot rides inside: a hard landing shakes it, a crash wrecks it.</summary>
        void OnVehicleTouchdown(LandingOutcome outcome)
        {
            if (Eva) return;
            if (outcome.Result == LandingResult.Hard) robot.VehicleDamage(LandingPhysics.HardLandingDamage);
            else if (outcome.Result == LandingResult.Crash) robot.VehicleDamage(1.0);
        }

        void OnDestroy()
        {
            if (body != null)
            {
                body.Touchdown -= OnVehicleTouchdown;
                body.RocketReset -= robot.Reset;
            }
            if (cameraController != null && ReferenceEquals(cameraController.FirstPerson, this)) cameraController.FirstPerson = null;
            if (controls != null) controls.Suspended = false;
            RemoveBody();
        }

        void RemoveBody()
        {
            if (model != null) model.Destroy();
            model = null;
            if (visor != null) visor.Visible = false;
            if (statusPanel != null) statusPanel.Visible = false;
            if (baseFov > 0f && cameraController != null && cameraController.Camera != null) cameraController.Camera.fieldOfView = baseFov;
            baseFov = -1f;
            fovKick = 0f;
        }

        /// <summary>The ground the astronaut walks on: the planet surface, raised to the launch pad's deck over the pad (ramp on the apron).</summary>
        internal sealed class PadSurface : ISurfaceHeightProvider
        {
            readonly ISurfaceHeightProvider ground;
            readonly Vector3d pad;
            readonly double radius, padRadius, deck;

            public PadSurface(ISurfaceHeightProvider inner, Vector3d padDirection, double planetRadius, double deckRadius)
            {
                ground = inner;
                pad = padDirection.Normalized;
                radius = planetRadius;
                padRadius = deckRadius;
                deck = inner.SurfaceHeight(pad) + LaunchSite.DeckHeight;
            }

            public double SurfaceHeight(Vector3d d)
            {
                double h = ground.SurfaceHeight(d);
                double distance = (d - pad).Magnitude * radius;
                if (distance <= padRadius) return Math.Max(h, deck);
                if (distance <= padRadius + 6.0) return Math.Max(h, deck - (distance - padRadius) / 6.0 * (LaunchSite.DeckHeight + 1.5));
                return h;
            }

            public double MinSurfaceHeight => ground.MinSurfaceHeight;
            public double MaxSurfaceHeight => ground.MaxSurfaceHeight + LaunchSite.DeckHeight;
        }

        internal ISurfaceHeightProvider WalkSurface() =>
            new PadSurface(planet.Model.Surface, planet.LaunchSiteDirection(), planet.Model.Radius, planet.Definition.launchSite.padRadius);

        PlanetSurfaceSampler waterTestSampler;
        Func<Vector3d, bool> waterTest;

        /// <summary>
        /// "Is this direction open sea?" for the walker and the rover, or null on a body without an ocean. Same answer as
        /// <c>Sample(d).IsWater</c> but only evaluates the height (no biome shading); cached until the surface changes.
        /// </summary>
        internal Func<Vector3d, bool> WaterTest()
        {
            if (!(planet.Model.Surface is PlanetSurfaceSampler sampler))
            {
                waterTestSampler = null;
                waterTest = null;
                return null;
            }
            if (ReferenceEquals(sampler, waterTestSampler)) return waterTest;
            PlanetGenerationSettings g = sampler.Settings; // a copy: read once per surface
            double seaLevel = g.seaLevel;
            waterTestSampler = sampler;
            waterTest = g.hasOcean ? d => sampler.TerrainHeight(d) < seaLevel : (Func<Vector3d, bool>)null;
            return waterTest;
        }

        // ---- Sites -------------------------------------------------------------------------------------------

        void RefreshSites()
        {
            var sim = body.Simulation;
            bool landed = sim != null && sim.IsLanded && !body.Destroyed;
            if (!landed && !Eva)
            {
                if (sites != null) ClearSites();
                return;
            }
            Vector3d dir = sim.LandedFixedPosition.Normalized;
            if (sites != null && (dir - sitesAnchor).Magnitude * planet.Model.Radius < 50.0) return;
            ClearSites();
            sitesAnchor = dir;
            sites = SampleSiteGenerator.Around(planet.Definition.planetId, planet.Model.Radius, dir, WaterTest());
            siteRoot = new GameObject("SampleSites").transform;
            siteRoot.SetParent(transform, false);
            foreach (SampleSite s in sites)
            {
                if (session != null && session.Profile.collectedSites.Contains(s.Id)) continue;
                var go = ProceduralMeshes.Primitive(s.IsLiquid ? PrimitiveType.Cylinder : PrimitiveType.Sphere, s.Name, siteRoot, Material(ColorOf(s)));
                float size = (float)(0.35 + 0.15 * Math.Min(3.0, s.Mass));
                go.transform.localScale = s.IsLiquid ? new Vector3(size * 2f, 0.04f, size * 2f) : Vector3.one * size;
                siteVisuals[s.Id] = go.transform;
            }
        }

        void ClearSites()
        {
            if (siteRoot != null) Destroy(siteRoot.gameObject);
            siteRoot = null;
            siteVisuals.Clear();
            sites = null;
            Nearest = null;
        }

        static Color ColorOf(SampleSite s)
        {
            switch (s.Category)
            {
                case SampleCategory.Rock: return new Color(0.45f, 0.40f, 0.36f);
                case SampleCategory.Mineral: return new Color(0.75f, 0.72f, 0.65f);
                case SampleCategory.RareGeology: return new Color(0.55f, 0.85f, 1.0f);
                case SampleCategory.Soil: return new Color(0.70f, 0.58f, 0.40f);
                case SampleCategory.MineralSoil: return new Color(0.62f, 0.32f, 0.20f);
                case SampleCategory.UnusualCompound: return new Color(0.30f, 0.75f, 0.45f);
                case SampleCategory.Water: return new Color(0.55f, 0.35f, 0.85f);
                case SampleCategory.OtherLiquid: return new Color(0.95f, 0.75f, 0.20f);
                default: return new Color(0.80f, 0.95f, 0.25f);
            }
        }

        Material Material(Color c) => ProceduralMeshes.LitMaterial(lit, c, 0.2f);

        Vector3d SitePoint(SampleSite s) => s.FixedDirection * (planet.Model.Radius + planet.Model.Surface.SurfaceHeight(s.FixedDirection));

        internal void FindNearest(Vector3d from)
        {
            Nearest = null;
            NearestDistance = double.PositiveInfinity;
            if (sites == null) return;
            foreach (SampleSite s in sites)
            {
                if (session != null && session.Profile.collectedSites.Contains(s.Id)) continue;
                double d = (SitePoint(s) - from).Magnitude;
                if (d < NearestDistance)
                {
                    NearestDistance = d;
                    Nearest = s;
                }
            }
        }

        // ---- EVA ---------------------------------------------------------------------------------------------

        /// <summary>Goes outside if a suit survives here; returns the reason when not.</summary>
        public string TryStartEva()
        {
            var sim = body.Simulation;
            if (sim == null || !sim.IsLanded || body.Destroyed) return Loc.T("Выйти можно только из стоящей на грунте ракеты.");
            CollectionCheck exit = RobotEva.CanExit(planet.Model.Properties, planet.Definition.hazards, robot);
            if (!exit.Ok) return exit.Reason;
            Vector3d landed = sim.LandedFixedPosition;
            walker = new SurfaceWalker(planet.Model.GravitationalParameter, planet.Model.Radius, WalkSurface(), landed, WaterTest());
            PlanetRotation.LocalBasis(landed.Normalized, out Vector3d east, out _);
            walker.PlaceAt(landed + east * 5.0);
            if (session != null && !body.UsedTestTool)
            {
                session.CrewEva(); // the crew statistics of the Tab screen
                session.CrewVisited(planet.Definition.planetId);
            }
            yaw = 270f; // face the rocket (west)
            lookPitch = 0f;
            controls.Suspended = true;
            cameraController.FirstPerson = this;
            simulation.ResetWarp();
            RemoveBody();
            model = CreateBody();
            if (model != null) model.SetFirstPerson(!thirdPerson);
            seenTouchdowns = walker.Touchdowns;
            seenImpacts = walker.Impacts;
            warnedBlind = robot.Blind;
            eyeLocal = model != null ? model.EyeLocal : new Vector3(0f, (float)SurfaceWalker.EyeHeight, 0f);
            pivotHeight = Math.Min(ThirdPersonPivotMax, Math.Max(0.5, eyeLocal.y + 0.2));
            if (cameraController.Camera != null) baseFov = cameraController.Camera.fieldOfView;
            double g = walker.Gravity;
            string sprint = walker.CanSprint
                ? Loc.T("спринт ") + UnitFormatter.Number(walker.SprintSpeed, 2) + Loc.T(" м/с") +
                  (double.IsPositiveInfinity(Locomotion.SprintEndurance(g)) ? Loc.T(" без усталости") : Loc.T(" на ≈ ") + UnitFormatter.Number(Locomotion.SprintEndurance(g), 0) + Loc.T(" с"))
                : Loc.T("бег невозможен — слишком тяжело");
            state.Notify(Loc.T("Выход на поверхность: g = ") + UnitFormatter.Number(g, 2) + Loc.T(" м/с², шаг ") + UnitFormatter.Number(walker.WalkSpeed, 2) +
                         Loc.T(" м/с, ") + sprint + Loc.T(", прыжок ≈ ") + UnitFormatter.Number(walker.JumpHeight, 2) + Loc.T(" м. ") + FlightInput.KeyLabel(InputKey.E) + Loc.T(" — образец/вернуться, ") + FlightInput.KeyLabel(InputKey.T) + Loc.T(" — вид от третьего лица.") +
                         Wear());
            return "";
        }

        /// <summary>" Износ: …" when the surroundings wear the robot, else "".</summary>
        string Wear()
        {
            string w = RobotEva.Warning(SurfaceEnvironment, robot.Rates);
            return w.Length > 0 ? Loc.T(" Износ робота: ") + w + "." : "";
        }

        /// <summary>The robot from the imported model when it is set up, else the procedural astronaut (also on any error).</summary>
        IExplorerBody CreateBody()
        {
            RobotModelSettings settings = RobotModelSettings.Load();
            if (settings != null && settings.Usable)
            {
                try
                {
                    return new RobotBodyModel(settings, lit);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("SpaceSim: robot model failed (" + e.Message + "), using the procedural astronaut.");
                }
            }
            return lit != null ? new AstronautModel(null, lit) : null;
        }

        /// <summary>Wears the robot for one EVA step: the surroundings, touchdowns after a fall and collisions.</summary>
        void UpdateRobot(float dt)
        {
            robot.Step(SurfaceEnvironment, dt);
            string hit = "";
            if (walker.Touchdowns != seenTouchdowns)
            {
                seenTouchdowns = walker.Touchdowns;
                if (robot.Impact(walker.LastTouchdownSpeed) > 0.0) hit = Loc.T("Жёсткое приземление ");
            }
            if (walker.Impacts != seenImpacts)
            {
                seenImpacts = walker.Impacts;
                if (robot.Impact(walker.LastImpactSpeed) > 0.0) hit = Loc.T("Удар ");
            }
            if (hit.Length > 0)
                state.Notify(hit + UnitFormatter.Number(robot.LastImpactSpeed, 1) + Loc.T(" м/с: корпус −") + UnitFormatter.Number(robot.LastImpactDamage * 100.0, 0) +
                             Loc.T(" % (без вреда до ") + UnitFormatter.Number(robot.Rates.safeImpactSpeed, 1) + Loc.T(" м/с)."));
            if (robot.Blind && !warnedBlind)
            {
                warnedBlind = true;
                state.Notify(Loc.T("Визор робота разрушен: изображения нет. Вернитесь к ракете по памяти (E — у ракеты)."));
            }
            if (robot.Disabled)
            {
                walker = null;
                RemoveBody();
                controls.Suspended = false;
                cameraController.FirstPerson = null;
                state.Notify(Loc.T("Робот вышел из строя — трос втянул его в ракету. Выход невозможен до новой ракеты."));
            }
        }

        public void EndEva()
        {
            if (!Eva) return;
            walker = null;
            RemoveBody();
            controls.Suspended = false;
            cameraController.FirstPerson = null;
            state.Notify(Loc.T("Робот в ракете."));
        }

        /// <summary>Takes a sample through the core rules; returns the message shown to the player.</summary>
        public string TryCollect(SampleSite site)
        {
            if (site == null) return Loc.T("Рядом нет образцов.");
            CollectionCheck check = SampleCollection.Check(site, body.Capabilities, session.Profile.CargoMass(), session.Profile.collectedSites);
            if (!check.Ok) return check.Reason;
            SampleRecord record = SampleCollection.ToRecord(site, body.Capabilities);
            if (!session.AddCargo(record)) return Loc.T("Профиль только для чтения — образец не сохранится.");
            if (siteVisuals.TryGetValue(site.Id, out Transform t) && t != null) Destroy(t.gameObject);
            siteVisuals.Remove(site.Id);
            string message = Loc.T("Взят образец: «") + site.Name + "» — " + site.Description + (record.analysed ? Loc.T(", проанализирован спектрометром") : "") +
                             Loc.T(". Груз ") + UnitFormatter.Number(session.Profile.CargoMass(), 2) + Loc.T(" из ") + UnitFormatter.Number(body.Capabilities.SampleCapacity, 0) + Loc.T(" кг.");
            if (planet.Definition.planetId == HomeSystemCatalog.HomeWorldId)
            {
                session.DeliverCargo();
                message += Loc.T(" Вы дома — образец сразу сдан (можно продать).");
            }
            else message += Loc.T(" Продать — после возвращения на Четвёртую планету.");
            if (!session.Profile.HasMilestone(ResearchMilestones.FirstSample) && !body.UsedTestTool)
            {
                MilestoneDefinition m = session.Award(ResearchMilestones.FirstSample);
                if (m != null) message += Loc.T(" ДОСТИЖЕНИЕ «") + m.Name + "» +" + m.CreditReward + Loc.T(" кр.");
            }
            state.Notify(message);
            return message;
        }

        /// <summary>E: outside — take the nearest sample in reach or board the rocket; inside — go out, or use the arm.</summary>
        public string Interact()
        {
            var sim = body.Simulation;
            if (Eva)
            {
                if (Nearest != null && NearestDistance <= SampleCollection.HandReach) return TryCollect(Nearest);
                double toRocket = (walker.Position - sim.LandedFixedPosition).Magnitude;
                if (toRocket <= BoardingDistance)
                {
                    EndEva();
                    return Loc.T("Робот в ракете.");
                }
                string far = Loc.T("Ничего в пределах ") + SampleCollection.HandReach + Loc.T(" м; до ракеты ") + UnitFormatter.Number(toRocket, 0) + Loc.T(" м.");
                state.Notify(far);
                return far;
            }
            if (sim == null || !sim.IsLanded || body.Destroyed) return Loc.T("Нужна посадка.");
            string blocked = TryStartEva();
            if (blocked.Length == 0) return "";
            // A suit would not survive: the lander's arm still reaches the closest samples.
            FindNearest(sim.LandedFixedPosition);
            string reply = blocked + " " + (Nearest != null && NearestDistance <= SampleCollection.ArmReach
                ? TryCollect(Nearest)
                : Loc.T("Манипулятор достаёт только до ") + SampleCollection.ArmReach + Loc.T(" м — образцов в зоне нет."));
            state.Notify(reply);
            return reply;
        }

        // ---- Frame loop --------------------------------------------------------------------------------------

        void Update()
        {
            if (body == null || body.Simulation == null || planet == null || planet.Model == null) return;
            if (Eva && (!body.Simulation.IsLanded || body.Destroyed)) EndEva();
            RefreshSites();
            if (FlightInput.IsTyping) return;
            if (FlightInput.GetKeyDown(InputKey.E) && (Rover == null || !Rover.Active)) Interact();
            if (!Eva) return;

            if (FlightInput.RightMouseHeld)
            {
                Vector2 d = FlightInput.LookDelta; // x player sensitivity, optional inverted Y
                yaw = Mathf.Repeat(yaw + d.x * MouseSensitivity, 360f);
                lookPitch = Mathf.Clamp(lookPitch - d.y * MouseSensitivity, -85f, 85f);
            }
            // The cabin view's key: CockpitView leaves it to the robot while the camera is out here.
            if (!FlightInput.MapOpen && FlightInput.GetKeyDown(InputKey.T)) ToggleThirdPerson();
            double forward = (FlightInput.GetKey(InputKey.W) ? 1 : 0) - (FlightInput.GetKey(InputKey.S) ? 1 : 0);
            double right = (FlightInput.GetKey(InputKey.D) ? 1 : 0) - (FlightInput.GetKey(InputKey.A) ? 1 : 0);
            bool sprint = FlightInput.GetKey(InputKey.LeftShift), jump = FlightInput.GetKeyDown(InputKey.Space) || driveJump;
            driveJump = false;
            if (Time.unscaledTime < driveUntil)
            {
                forward = driveForward;
                right = driveRight;
                sprint |= driveRun;
            }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            walker.Step(dt, forward, right, yaw, sprint, jump);
            UpdateRobot(dt);
            if (!Eva) return; // the robot failed this step
            FindNearest(walker.Position);
            AnimateBody(dt);
        }

        /// <summary>The body's horizontal axes in the planet-fixed frame: the way the astronaut (and the camera) faces.</summary>
        void BodyAxes(out Vector3d up, out Vector3d forward, out Vector3d right)
        {
            up = walker.Up;
            PlanetRotation.LocalBasis(up, out Vector3d east, out Vector3d north);
            double y = yaw * MathD.DegToRad;
            forward = north * Math.Cos(y) + east * Math.Sin(y);
            right = east * Math.Cos(y) - north * Math.Sin(y);
        }

        void AnimateBody(float dt)
        {
            BodyAxes(out _, out Vector3d forward, out Vector3d right);
            if (model != null)
            {
                model.Animate(walker, forward, right, dt);
                // The camera rides with the body, low-passed so the stride reads as a sway, not a jolt.
                float k = 1f - Mathf.Exp(-14f * dt);
                Vector3 target = model.EyeLocal;
                eyeLocal = new Vector3(Mathf.Lerp(eyeLocal.x, target.x, k), Mathf.Lerp(eyeLocal.y, target.y, k), Mathf.Lerp(eyeLocal.z, target.z, k));
            }
            // A little wider view at full sprint (the sense of speed), none in a slow lope on a small world.
            float speedShare = walker.Sprinting ? Mathf.Clamp01((float)((walker.HorizontalSpeed - walker.WalkSpeed) / Math.Max(0.5, 4.5 - walker.WalkSpeed))) : 0f;
            fovKick = Mathf.Lerp(fovKick, SprintFovKick * speedShare, 1f - Mathf.Exp(-4f * dt));
            if (baseFov > 0f && cameraController.Camera != null) cameraController.Camera.fieldOfView = baseFov + fovKick;
            if (visor != null) ApplyVisor();
            if (statusPanel != null)
            {
                statusPanel.Visible = true;
                statusPanel.Environment = SurfaceEnvironment;
            }
        }

        /// <summary>
        /// The visor overlay is the robot's own eye: drawn in first person only. From behind it is hidden, except a destroyed
        /// visor, which stays black in either view (the picture is still rebuilt from that camera).
        /// </summary>
        void ApplyVisor()
        {
            visor.Visor = (float)robot.Visor;
            visor.Visible = !thirdPerson || robot.Visor <= 0.0;
            visor.Rim = !thirdPerson;
            visor.Strain = thirdPerson || walker == null ? 0f : (float)MathD.Clamp(1.0 - walker.Stamina.Fraction / 0.5, 0.0, 1.0) * 0.8f;
        }

        void LateUpdate()
        {
            if (Eva && model != null && planet != null && planet.Model != null && origin != null)
            {
                // Feet on the walker's ground point, facing the camera's heading (after the camera moved the floating origin).
                double tr = simulation.RenderTime;
                BodyAxes(out Vector3d up, out Vector3d forward, out _);
                Vector3d feet = planet.Model.ToInertial(walker.Position, tr);
                Vector3 f = CoordinateSystem.ToUnityVector(planet.Model.Rotation.FixedToInertial(forward, tr));
                Vector3 u = CoordinateSystem.ToUnityVector(planet.Model.Rotation.FixedToInertial(up, tr));
                model.Root.SetPositionAndRotation(origin.ToUnityPosition(feet), Quaternion.LookRotation(f, u));
            }
            if (siteRoot == null || planet == null || planet.Model == null || origin == null) return;
            double t = simulation.RenderTime;
            foreach (SampleSite s in sites)
            {
                if (!siteVisuals.TryGetValue(s.Id, out Transform tr) || tr == null) continue;
                Vector3d p = planet.Model.ToInertial(SitePoint(s), t);
                Vector3d up = (p - planet.Model.Position).Normalized;
                tr.SetPositionAndRotation(origin.ToUnityPosition(p + up * 0.15), Quaternion.FromToRotation(Vector3.up, CoordinateSystem.ToUnityVector(up)));
            }
        }

        public void GetView(double renderTime, out Vector3d eye, out Vector3d forward, out Vector3d up)
        {
            BodyAxes(out Vector3d fixedUp, out Vector3d heading, out Vector3d side);
            double p = lookPitch * MathD.DegToRad;
            Vector3d look = heading * Math.Cos(p) + fixedUp * Math.Sin(p);
            Vector3d fixedEye, fixedForward;
            if (!thirdPerson)
            {
                // Inside the helmet: the eye follows the hips, the lean and the stride of the body below it.
                fixedEye = walker.Position + fixedUp * eyeLocal.y + heading * eyeLocal.z + side * eyeLocal.x;
                fixedForward = look;
            }
            else
            {
                // Behind and above the body, looking at it along the head's pitch; never below the ground.
                Vector3d target = walker.Position + fixedUp * pivotHeight;
                fixedEye = AboveGround(target - look * ThirdPersonDistance);
                // A hill between the body and the camera: come in to the last clear point on the line.
                Vector3d toEye = fixedEye - target;
                for (int i = 1; i < ThirdPersonGroundSamples; i++)
                {
                    Vector3d onLine = target + toEye * ((double)i / ThirdPersonGroundSamples);
                    if (onLine.Magnitude >= walker.PointOnGround(onLine.Normalized).Magnitude + 0.1) continue;
                    fixedEye = AboveGround(target + toEye * (Math.Max(1, i - 1) / (double)ThirdPersonGroundSamples));
                    break;
                }
                fixedForward = (target - fixedEye).Normalized;
            }
            eye = planet.Model.ToInertial(fixedEye, renderTime);
            forward = planet.Model.Rotation.FixedToInertial(fixedForward, renderTime);
            up = planet.Model.Rotation.FixedToInertial(fixedUp, renderTime);
        }

        /// <summary>A planet-fixed point raised, along its own vertical, to at least the camera clearance above the ground.</summary>
        Vector3d AboveGround(Vector3d point)
        {
            Vector3d dir = point.Normalized;
            double floor = walker.PointOnGround(dir).Magnitude + ThirdPersonClearance;
            return point.Magnitude < floor ? dir * floor : point;
        }

        // ---- HUD ---------------------------------------------------------------------------------------------

        void OnGUI()
        {
            var sim = body != null ? body.Simulation : null;
            if (sim == null || !sim.IsLanded || body.Destroyed || session == null || FlightInput.PauseMenuOpen || FlightInput.MapOpen) return;
            if (body.VisualHidden) return; // the rocket is in the hangar: nothing to land or walk out of here
            if (!Eva && FlightHUD.CabinViewClear) return; // in the crewed cabin the panel would cover its screens (H shows it)
            HudStyle.Begin();
            if (note == null) note = new GUIStyle(HudStyle.Small) { wordWrap = true };
            Vector2 screen = HudStyle.VirtualScreen;
            var r = Eva ? new Rect(screen.x * 0.5f - 320f, screen.y - 300f, 640f, 170f) : new Rect(screen.x * 0.5f - 290f, screen.y - 250f, 580f, 120f);
            GUILayout.BeginArea(r);
            GUILayout.BeginVertical(HudStyle.Panel);
            double cargo = session.Profile.CargoMass(), capacity = body.Capabilities.SampleCapacity;
            if (Eva)
            {
                double g = walker.Gravity;
                GUILayout.Label(Loc.T("НА ПОВЕРХНОСТИ · ") + Loc.T(planet.Definition.displayName), HudStyle.Title);
                GUILayout.Label("g = " + UnitFormatter.Number(g, 2) + Loc.T(" м/с² · шаг ") + UnitFormatter.Number(walker.WalkSpeed, 2) + Loc.T(" м/с (√(Fr·g·L)) · ") + SprintLine(g) +
                                Loc.T(" · разгон ≤ μg = ") + UnitFormatter.Number(Locomotion.MaxAcceleration(g), 2) + Loc.T(" м/с² · прыжок v²/2g ≈ ") + UnitFormatter.Number(walker.JumpHeight, 2) +
                                Loc.T(" м · до ракеты ") + UnitFormatter.Number(walker.DistanceToAnchor, 0) + Loc.T(" м (трос ") + SurfaceWalker.TetherLength + Loc.T(" м)"), note);
                StaminaRow(g);
                GUILayout.Label(Loc.T("Сейчас ") + UnitFormatter.Number(walker.HorizontalSpeed, 2) + Loc.T(" м/с · ") + GaitName(walker.Gait) +
                                (walker.Gait == Gait.Walking || walker.Gait == Gait.Running
                                    ? " (Fr = " + UnitFormatter.Number(Locomotion.FroudeNumber(walker.HorizontalSpeed, g), 2) + ", " +
                                      UnitFormatter.Number(Locomotion.StepFrequency(walker.HorizontalSpeed, g), 1) + Loc.T(" шаг/с)")
                                    : "") +
                                (model != null && Mathf.Abs(model.LeanDegrees) > 3f ? Loc.T(" · наклон ") + UnitFormatter.Number(model.LeanDegrees, 0) + "° (tg θ = a/g)" : "") +
                                " · " + FlightInput.KeyLabel(InputKey.LeftShift) + Loc.T(" — спринт, ") + FlightInput.KeyLabel(InputKey.T) + " — " + (thirdPerson ? Loc.T("от первого лица") : Loc.T("вид от третьего лица")), note);
                if (walker.Blocked.Length > 0) GUILayout.Label(Loc.T("Стоп: ") + walker.Blocked, note);
            }
            else
            {
                CollectionCheck exit = RobotEva.CanExit(planet.Model.Properties, planet.Definition.hazards, robot);
                GUILayout.Label(Loc.T("ПОСАДКА · ") + Loc.T(planet.Definition.displayName), HudStyle.Title);
                GUILayout.Label(exit.Ok ? FlightInput.KeyLabel(InputKey.E) + Loc.T(" — выйти на поверхность (") + MoveKeys() + Loc.T(" — идти, ") + FlightInput.KeyLabel(InputKey.LeftShift) + Loc.T(" — спринт, ") + FlightInput.KeyLabel(InputKey.Space) + Loc.T(" — прыжок, ПКМ — осмотреться, ") + FlightInput.KeyLabel(InputKey.T) + Loc.T(" — вид от третьего лица).") + Wear()
                    : Loc.T("Выход невозможен: ") + exit.Reason + " " + FlightInput.KeyLabel(InputKey.E) + Loc.T(" — манипулятор (до ") + SampleCollection.ArmReach + Loc.T(" м)."), note);
                FindNearest(sim.LandedFixedPosition);
            }
            string nearest = Nearest == null ? Loc.T("образцов рядом нет")
                : Loc.T("ближайший: «") + Nearest.Name + "» (" + Nearest.Description + ") — " + UnitFormatter.Number(NearestDistance, 0) + Loc.T(" м") +
                  (Eva && NearestDistance <= SampleCollection.HandReach ? " · " + FlightInput.KeyLabel(InputKey.E) + Loc.T(" — взять") : "");
            GUILayout.Label(Loc.T("Груз ") + UnitFormatter.Number(cargo, 2) + " / " + UnitFormatter.Number(capacity, 0) + Loc.T(" кг · ") + nearest, note);
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>"WASD" with the default keys, "Вверх/Влево/Вниз/Вправо" style when rebound to longer names.</summary>
        static string MoveKeys()
        {
            string[] k = { FlightInput.KeyLabel(InputKey.W), FlightInput.KeyLabel(InputKey.A), FlightInput.KeyLabel(InputKey.S), FlightInput.KeyLabel(InputKey.D) };
            bool letters = k[0].Length == 1 && k[1].Length == 1 && k[2].Length == 1 && k[3].Length == 1;
            return string.Join(letters ? "" : "/", k);
        }

        /// <summary>Top running speed here and what limits it.</summary>
        static string SprintLine(double g)
        {
            switch (Locomotion.SprintLimitedBy(g))
            {
                case SprintLimit.Weight:
                    return Loc.T("бег невозможен: ноги давят ") + UnitFormatter.Number(Locomotion.LegForce, 1) + Loc.T(" веса тела, а вес с костюмом здесь ") +
                           UnitFormatter.Number(Locomotion.EffectiveLoad(g), 0) + Loc.T(" кг (k = ") + UnitFormatter.Number(Locomotion.ForceRatio(g), 2) + " < 1)";
                case SprintLimit.Strength:
                    return Loc.T("спринт ") + UnitFormatter.Number(Locomotion.SprintSpeed(g), 2) + Loc.T(" м/с — предел силы ног L(2k−1)/t, k = ") +
                           UnitFormatter.Number(Locomotion.ForceRatio(g), 2);
                default:
                    return Loc.T("спринт ") + UnitFormatter.Number(Locomotion.SprintSpeed(g), 2) + Loc.T(" м/с — предел Fr = ") + UnitFormatter.Number(Locomotion.SprintFroude, 0) +
                           Loc.T(" (сильнее толчок — долгий неуправляемый полёт)");
            }
        }

        /// <summary>The anaerobic reserve W′: a bar and what the current effort costs.</summary>
        void StaminaRow(double g)
        {
            WorkCapacity stamina = walker.Stamina;
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("Запас сил"), note, GUILayout.Width(72f));
            Rect bar = GUILayoutUtility.GetRect(150f, 12f, GUILayout.Width(150f), GUILayout.Height(12f));
            bar = new Rect(bar.x, bar.y + 4f, bar.width, 9f);
            Color back = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = stamina.Exhausted ? HudStyle.Danger : stamina.Fraction < 0.35 ? HudStyle.Warning : HudStyle.Accent;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * (float)stamina.Fraction, bar.height), Texture2D.whiteTexture);
            GUI.color = back;

            double p = walker.MetabolicPower, cp = stamina.CriticalPower;
            string effort = UnitFormatter.Number(stamina.Fraction * 100.0, 0) + " % · " + UnitFormatter.Number(p, 0) + Loc.T(" Вт (порог ") + UnitFormatter.Number(cp, 0) + Loc.T(" Вт)");
            if (!walker.CanSprint) effort += Loc.T(" · только шаг");
            else if (walker.SprintBlocked.Length > 0) effort += " · " + walker.SprintBlocked;
            else if (p > cp) effort += Loc.T(" · хватит на ") + UnitFormatter.Number(stamina.Balance / (p - cp), 0) + Loc.T(" с");
            else if (double.IsPositiveInfinity(Locomotion.SprintEndurance(g))) effort += Loc.T(" · спринт здесь не утомляет: ") +
                                                                                    UnitFormatter.Number(Locomotion.MetabolicPower(Locomotion.SprintSpeed(g), g), 0) + Loc.T(" Вт < порога");
            else if (stamina.Fraction < 0.999) effort += Loc.T(" · восстановление");
            else effort += Loc.T(" · спринт ≈ ") + UnitFormatter.Number(Locomotion.SprintEndurance(g), 0) + Loc.T(" с");
            GUILayout.Label(effort, note);
            GUILayout.EndHorizontal();
        }

        static string GaitName(Gait gait)
        {
            switch (gait)
            {
                case Gait.Walking: return Loc.T("шаг");
                case Gait.Running: return Loc.T("бег");
                case Gait.Airborne: return Loc.T("в прыжке");
                default: return Loc.T("стоит");
            }
        }
    }
}
