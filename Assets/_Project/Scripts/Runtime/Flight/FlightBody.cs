using SpaceSim.Localization;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Simulation;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The player's vehicle: a multi-stage rocket carrying the probe. Wraps <see cref="FreeFlightSimulation"/>; its
    /// state changes only through physics — gravity, drag and the thrust of the engine the player controls — or the
    /// explicit initial-conditions test tool. No autopilot. The visual model is interpolated between physics steps
    /// and never feeds back into the simulation.
    /// </summary>
    [DefaultExecutionOrder(105)]
    public sealed class FlightBody : MonoBehaviour, ISimulatedBody, IRailsBody, IWarpLimitedBody
    {
        /// <summary>A flight in progress moved into another body's frame (sphere-of-influence handoff).</summary>
        public sealed class VehicleCarry
        {
            public RocketState Rocket;
            /// <summary>State in the NEW frame [m, m/s] at <see cref="Time"/>.</summary>
            public Vector3d Position, Velocity;
            public double Time;
            public bool UsedTestTool;
            /// <summary>Legs, canopies, RCS and heat of the same vehicle.</summary>
            public FlightSystems Systems;
        }

        [Header("Payload / probe (SI)")]
        [Tooltip("Probe mass [kg] when flown without a rocket.")]
        [SerializeField] double mass = 1000.0;
        [Tooltip("Height of the reference point above the ground when resting [m].")]
        [SerializeField] double contactRadius = 1.0;
        [SerializeField] DragProperties drag = new DragProperties(true, 1.05, 3.14);

        [Header("Rocket")]
        [SerializeField] bool startWithRocket = true;
        [Tooltip("The rocket design built in the hangar (compiled into the physics configuration).")]
        [SerializeField] RocketBlueprint blueprint = RocketBlueprint.CreateDefault();
        RocketConfiguration rocket = new RocketConfiguration();

        [Header("Integration")]
        [SerializeField] IntegrationMethod integrator = IntegrationMethod.VelocityVerlet;
        [Tooltip("η in Δt ≤ η·√(r³/μ). Sub-steps are added automatically if the fixed step is too coarse.")]
        [SerializeField] double accuracyFactor = 0.002;

        FreeFlightSimulation sim;
        PlanetRuntime planet;
        SimulationController controller;
        FloatingOrigin origin;
        Transform visual;
        RocketPalette palette;
        RocketModel model;
        RocketExhaustEffects exhaust;
        int laidOutStage = -1;
        Shader lit;

        public FreeFlightSimulation Simulation => sim;
        public RocketState Rocket => sim?.Rocket;
        public double Mass => mass;
        public DragProperties Drag => drag;
        public PlanetRuntime Planet => planet;

        /// <summary>Current design (a copy is kept; edit a clone and pass it to <see cref="ApplyBlueprint"/>).</summary>
        public RocketBlueprint Blueprint => blueprint;
        /// <summary>The design compiled into physics data, with capabilities and construction issues.</summary>
        public CompiledRocket Compiled { get; private set; }
        /// <summary>Capabilities as designed (independent of damage).</summary>
        public VehicleCapabilities DesignCapabilities => Compiled != null ? Compiled.Capabilities : new VehicleCapabilities();
        /// <summary>Effective capabilities: instruments go offline when hazards have degraded the equipment below 50 %.</summary>
        public VehicleCapabilities Capabilities
        {
            get
            {
                VehicleCapabilities design = DesignCapabilities;
                if (Condition.InstrumentsOnline) return design;
                return new VehicleCapabilities { RadiationShielding = design.RadiationShielding, AcidResistance = design.AcidResistance };
            }
        }
        /// <summary>Payload equipment integrity under environmental hazards (Level B, reset with every new rocket).</summary>
        public EquipmentCondition Condition { get; } = new EquipmentCondition();
        /// <summary>True once the initial-conditions test tool moved the vehicle (until a new rocket is placed on the pad).</summary>
        public bool UsedTestTool { get; private set; }

        System.Func<string, bool> ownsModule = id => true;
        /// <summary>Whether the player may install a module (profile ownership; free modules always).</summary>
        public bool OwnsModule(string moduleId) => ownsModule(moduleId);
        /// <summary>Raised after a new design was applied (new rocket on the pad).</summary>
        public event System.Action<RocketBlueprint> BlueprintApplied;

        /// <summary>True while resting on the launch pad — the only place the rocket can be rebuilt.</summary>
        public bool IsOnLaunchPad
        {
            get
            {
                if (sim == null || !sim.IsLanded) return false;
                Vector3d padDir = planet.LaunchSiteDirection();
                double chord = (sim.LandedFixedPosition.Normalized - padDir).Magnitude * planet.Model.Radius;
                return chord < 250.0;
            }
        }

        public void Initialize(PlanetRuntime planetRuntime, GravityField gravity, SimulationController simulation,
            FloatingOrigin floatingOrigin, Shader litShader, RocketBlueprint design = null, System.Func<string, bool> owns = null,
            PatchedConicSystem navigation = null, int frameIndex = -1, VehicleCarry carry = null)
        {
            planet = planetRuntime;
            controller = simulation;
            origin = floatingOrigin;
            lit = litShader;
            if (owns != null) ownsModule = owns;
            if (design != null) blueprint = design.Clone();
            CompileBlueprint();
            sim = new FreeFlightSimulation(planet.Model, gravity);
            if (navigation != null && frameIndex >= 0) sim.SetNavigation(navigation, frameIndex);
            ApplyParameters();
            planet.ModelChanged += OnModelChanged;
            controller.Register(this);
            Current = this;
            visual = new GameObject("VehicleVisual").transform;
            visual.SetParent(transform, false);
            if (carry == null) ResetToLaunchSite();
            else
            {
                // Continue the same flight in this body's frame: same rocket, same propellant, same trajectory.
                sim.Rocket = carry.Rocket;
                sim.Systems = carry.Systems ?? new FlightSystems(DesignCapabilities);
                Condition.Reset();
                sim.SetInertialState(carry.Position, carry.Velocity, carry.Time);
                UsedTestTool = carry.UsedTestTool;
                RebuildVisual();
            }
        }

        /// <summary>Everything needed to continue this flight in another body's frame.</summary>
        public VehicleCarry CarryTo(int newFrameIndex)
        {
            Vector3d r = sim.State.Position, v = sim.State.Velocity;
            sim.Navigation.Transform(sim.FrameBody, newFrameIndex, sim.State.Time, ref r, ref v);
            return new VehicleCarry { Rocket = sim.Rocket, Position = r, Velocity = v, Time = sim.State.Time, UsedTestTool = UsedTestTool, Systems = sim.Systems };
        }

        /// <summary>The vehicle is inside another body's sphere of influence: the frame should move to that body.</summary>
        public bool NeedsHandoff(out int bodyIndex)
        {
            bodyIndex = sim != null ? sim.Primary : -1;
            return sim != null && sim.Navigation != null && !sim.IsLanded && bodyIndex >= 0 && bodyIndex != sim.FrameBody;
        }

        // ---- IRailsBody: coasting at high time warp -----------------------------------------------------

        public double RailsHorizon(double maxDt) => sim.RailsHorizon(maxDt);

        public SpaceSim.Flight.TimeWarpLimit WarpLimit() => SpaceSim.Flight.TimeWarpPolicy.Evaluate(sim.WarpSituation());

        public void RailsStep(double time, double dt, double renderLag)
        {
            UpdateSolarFlux();
            sim.RailsStep(dt, renderLag);
            ApplyHazards(dt);
            CheckTouchdown();
        }

        /// <summary>Why the vehicle cannot coast on rails right now (empty when it can).</summary>
        public string RailsBlockedReason()
        {
            if (sim == null || sim.Navigation == null) return Loc.T("нет модели навигации");
            if (sim.Rocket != null && sim.Rocket.IsBurning && !sim.LowThrustBurnOnSafeTrajectory())
                return sim.Rocket.CurrentStage.IsElectric ? Loc.T("работает двигатель (малая тяга ускоряется только на безопасной орбите)") : Loc.T("работает двигатель");
            if (sim.IsLanded) return "";
            if (sim.RailsHorizon(1.0) <= 0.0)
            {
                int p = sim.Primary >= 0 ? sim.Primary : sim.FrameBody;
                NavBody b = sim.Navigation[p];
                return Loc.T("ниже ") + SpaceSim.Units.UnitFormatter.Number((b.ZoneRadius - b.Radius) / 1000.0, 0) + Loc.T(" км над ") + b.Name + Loc.T(" (атмосфера/рельеф — только точное интегрирование)");
            }
            return "";
        }

        void OnModelChanged(SpaceSim.Planets.PlanetModel model) => sim.Planet = model;

        void OnDestroy()
        {
            if (controller != null) controller.Unregister(this);
            if (Current == this) Current = null;
            if (planet != null) planet.ModelChanged -= OnModelChanged;
            model?.Destroy();
            palette?.Dispose();
        }

        /// <summary>The 3D rocket as built from the applied design (same builder as the hangar preview).</summary>
        public RocketModel Model => model;

        /// <summary>Hides the rocket on the pad while it is in the hangar (physics untouched: it stays landed where it was).</summary>
        public bool VisualHidden
        {
            get => visual != null && !visual.gameObject.activeSelf;
            set
            {
                if (visual != null) visual.gameObject.SetActive(!value);
            }
        }

        void OnValidate()
        {
            if (sim != null) ApplyParameters();
        }

        void ApplyParameters()
        {
            sim.Mass = System.Math.Max(1e-6, mass);
            sim.ContactRadius = System.Math.Max(0.0, contactRadius);
            sim.Drag = drag;
            sim.Method = integrator;
            sim.AccuracyFactor = System.Math.Max(1e-5, accuracyFactor);
        }

        public void SimulationStep(double time, double dt)
        {
            UpdateSolarFlux();
            sim.Step(dt);
            ApplyHazards(dt);
            CheckTouchdown();
        }

        // ---- Touchdown ---------------------------------------------------------------------------------

        /// <summary>The vehicle was destroyed in a crash: no engine, no controls (R — a new rocket at the start planet).</summary>
        public bool Destroyed { get; private set; }
        /// <summary>Verdict of the last touchdown (null before the first one of this rocket).</summary>
        public LandingOutcome? LastLanding { get; private set; }
        /// <summary>Raised on every touchdown with its verdict.</summary>
        public event System.Action<LandingOutcome> Touchdown;
        int seenImpacts;

        /// <summary>Judges a new contact with the ground: speeds from the simulation's impact record, tilt, terrain slope.</summary>
        void CheckTouchdown()
        {
            if (sim.ImpactCount == seenImpacts) return;
            seenImpacts = sim.ImpactCount;
            ImpactRecord hit = sim.LastImpact;
            double horizontal = System.Math.Sqrt(System.Math.Max(0.0, hit.SurfaceRelativeSpeed * hit.SurfaceRelativeSpeed - hit.VerticalSpeed * hit.VerticalSpeed));
            double tilt = sim.Rocket != null && sim.Rocket.HasStages ? sim.Rocket.PitchDegrees : 0.0;
            double slope = LandingPhysics.TerrainSlope(planet.Model.Surface, planet.Model.Radius, sim.LandedFixedPosition);
            int stageIndex = sim.Rocket != null ? sim.Rocket.StageIndex : int.MaxValue;
            LandingOutcome outcome = sim.Systems != null
                ? sim.Systems.AssessLanding(stageIndex, hit.VerticalSpeed, horizontal, tilt, slope)
                : LandingPhysics.Assess(hit.VerticalSpeed, horizontal, tilt, slope);
            LastLanding = outcome;
            if (outcome.Result == LandingResult.Hard) Condition.Damage(LandingPhysics.HardLandingDamage);
            if (outcome.Result == LandingResult.Crash)
            {
                Destroyed = true;
                if (sim.Rocket != null) sim.Rocket.Extinguish(); // a burning solid motor stops too: the vehicle is gone
                Condition.Damage(1.0);
            }
            Touchdown?.Invoke(outcome);
        }

        void ApplyHazards(double dt)
        {
            // Environmental hazards act on the equipment where the vehicle actually is (Level B).
            var model = sim.Planet;
            double rho0 = model.Atmosphere.ReferenceDensity;
            double ratio = rho0 > 0.0 ? model.DensityAt(sim.State.Position) / rho0 : 0.0;
            VehicleCapabilities caps = DesignCapabilities;
            // A body's radiation and acid act only inside its own sphere of influence, not across interplanetary space.
            EnvironmentHazards hazards = sim.Navigation != null && sim.Primary != sim.FrameBody ? new EnvironmentHazards() : planet.Definition.hazards;
            Condition.Apply(HazardModel.Evaluate(hazards, ratio, caps.RadiationShielding, caps.AcidResistance), dt);
            // Aerodynamic heating above what the heat shield (or the bare hull) tolerates (Core: FlightSystems.UpdateHeat).
            double heat = sim.TakeHeatDamage();
            if (heat > 0.0) Condition.Damage(heat);
            if (sim.Systems != null && sim.Systems.Events.Count > 0)
                foreach (string message in sim.Systems.DrainEvents()) SystemMessage?.Invoke(message);
        }

        // ---- Flight systems: legs, parachutes, RCS, power (pilot commands; the physics is in Core) ------------

        /// <summary>A message from the vehicle's systems for the pilot (a torn canopy, overheating…).</summary>
        public event System.Action<string> SystemMessage;

        /// <summary>The flight body of the current flight stack (for windows that only read it).</summary>
        public static FlightBody Current { get; private set; }

        /// <summary>Extends or retracts the landing legs. Returns false when no legs are attached any more.</summary>
        public bool SetLegsDeployed(bool deployed)
        {
            if (sim?.Systems == null || Destroyed) return false;
            var r = sim.Rocket;
            int stage = r != null ? r.StageIndex : int.MaxValue;
            if (sim.Systems.Hardware(stage).LegCount <= 0) return false;
            sim.Systems.LegsDeployed = deployed;
            return true;
        }

        /// <summary>Releases the parachutes (they cannot be packed again in flight).</summary>
        public ParachuteCommand ReleaseParachutes() =>
            sim?.Systems == null || Destroyed ? ParachuteCommand.NoParachutes : sim.Systems.ReleaseParachutes();

        /// <summary>Solar flux at the vehicle relative to the home planet's distance from the star (1 without a star system).</summary>
        void UpdateSolarFlux()
        {
            var r = sim.Rocket;
            if (r == null || sim.Navigation == null) return;
            if (homeNavIndex == int.MinValue)
            {
                homeNavIndex = sim.Navigation.IndexOf(SpaceSim.Planets.HomeSystemCatalog.HomeWorldId);
                while (homeNavIndex >= 0 && sim.Navigation[homeNavIndex].ParentIndex >= 0) homeNavIndex = sim.Navigation[homeNavIndex].ParentIndex; // a moon: its planet's distance
            }
            if (homeNavIndex < 0) return;
            double t = sim.State.Time;
            Vector3d star = sim.State.Position + sim.Navigation.Position(sim.FrameBody, t);
            r.SolarFluxRatio = PartPhysics.SolarFluxRatio(star.Magnitude, sim.Navigation.Position(homeNavIndex, t).Magnitude);
        }

        int homeNavIndex = int.MinValue;
        int shownLegs = -1, shownChute = -1;

        /// <summary>Keeps the model's legs and canopies in step with the systems (visual only, on change).</summary>
        void Update()
        {
            if (model == null || sim?.Systems == null) return;
            int legs = sim.Systems.LegsDeployed ? 1 : 0;
            if (legs != shownLegs)
            {
                shownLegs = legs;
                model.SetLegsDeployed(legs == 1);
            }
            bool released = sim.Systems.CanopiesIn(CanopyState.Deployed) > 0;
            float opening = (float)sim.Systems.ParachuteOpening;
            int chute = released ? 1 + Mathf.RoundToInt(opening * 50f) : 0;
            if (chute != shownChute)
            {
                shownChute = chute;
                model.SetParachutesDeployed(released, opening);
            }
        }

        void CompileBlueprint()
        {
            Compiled = BlueprintCompiler.Compile(blueprint ?? RocketBlueprint.CreateDefault(), ownsModule);
            rocket = Compiled.Configuration;
            mass = Compiled.PayloadMass;
        }

        /// <summary>
        /// Builds a new rocket from <paramref name="design"/> and puts it, fully loaded as designed, on the launch pad.
        /// Only allowed while on the pad (returns false otherwise).
        /// </summary>
        public bool ApplyBlueprint(RocketBlueprint design)
        {
            if (design == null || !IsOnLaunchPad) return false;
            blueprint = design.Clone();
            CompileBlueprint();
            ApplyParameters();
            ResetToLaunchSite();
            BlueprintApplied?.Invoke(blueprint.Clone());
            return true;
        }

        /// <summary>Puts a fresh, fully fuelled rocket (or the bare probe) on the launch pad, engine off, vertical.</summary>
        public void ResetToLaunchSite()
        {
            sim.Rocket = startWithRocket ? new RocketState(rocket) : null;
            sim.Systems = new FlightSystems(DesignCapabilities);
            sim.TakeHeatDamage();
            Condition.Reset();
            UsedTestTool = false;
            Destroyed = false;
            LastLanding = null;
            sim.PlaceOnSurface(planet.LaunchSiteDirection(), controller.SimulationTime);
            RebuildVisual();
            RocketReset?.Invoke();
        }

        /// <summary>A fresh rocket stands on the pad (<see cref="ResetToLaunchSite"/>): everything aboard starts anew.</summary>
        public event System.Action RocketReset;

        /// <summary>Rover hook: moves the landed vehicle along the ground to a planet-fixed direction (stays landed, no touchdown).</summary>
        public bool DriveTo(Vector3d fixedDirection)
        {
            if (sim == null || !sim.IsLanded || Destroyed) return false;
            sim.PlaceOnSurface(fixedDirection, sim.State.Time);
            return true;
        }

        /// <summary>Ignites the current stage at full throttle. Attitude is whatever the player has set (default vertical).</summary>
        public bool Launch()
        {
            var r = sim.Rocket;
            if (r == null || !r.CanBurn || Destroyed) return false;
            r.Throttle = r.Throttle > 0.0 ? r.Throttle : 1.0;
            r.EngineOn = true;
            return true;
        }

        /// <summary>Drops the current (usually empty) stage; the next one must be ignited again.</summary>
        public bool SeparateStage()
        {
            var r = sim.Rocket;
            if (r == null || !r.HasStages) return false;
            bool wasOn = r.EngineOn;
            r.Separate();
            r.EngineOn = wasOn && r.CanBurn;
            return true;
        }

        /// <summary>
        /// Test tool: sets position (lat/lon [deg], altitude above the reference radius [m]) and velocity in the
        /// local East-North-Up frame [m/s]. Nothing is computed for the player — the numbers are used as given.
        /// </summary>
        public void ApplyInitialConditions(double latitudeDeg, double longitudeDeg, double altitude,
            double vEast, double vNorth, double vUp, bool relativeToSurface)
        {
            UsedTestTool = true; // research rewards only count for real flights
            sim.SetLocalState(latitudeDeg * MathD.DegToRad, longitudeDeg * MathD.DegToRad, altitude,
                vEast, vNorth, vUp, relativeToSurface, controller.SimulationTime);
        }

        /// <summary>
        /// Test tool: adds an instantaneous Δv [m/s] (simulation frame). Like the initial-conditions tool it is not a flight —
        /// research rewards and visits do not count afterwards.
        /// </summary>
        public void ApplyTestImpulse(Vector3d deltaV)
        {
            UsedTestTool = true;
            sim.SetInertialState(sim.State.Position, sim.State.Velocity + deltaV, sim.State.Time);
        }

        /// <summary>Position for rendering: interpolated between the last two physics steps.</summary>
        public Vector3d RenderPosition(double alpha, double renderTime)
        {
            if (sim.IsLanded) return sim.LandedPositionAt(renderTime);
            return Vector3d.Lerp(sim.PreviousState.Position, sim.State.Position, alpha);
        }

        /// <summary>The vehicle's long axis for rendering: vertical on the ground, the commanded attitude in flight.</summary>
        Vector3d RenderAxis(Vector3d p, Vector3d up)
        {
            if (Destroyed) return Vector3d.Cross(planet.Model.Rotation.SpinAxis, up).Normalized; // the wreck lies on its side
            return sim.IsLanded || sim.Rocket == null ? up : sim.ThrustDirection(p);
        }

        /// <summary>
        /// Height of the middle of the visible stack above the reference point, along the vehicle axis [m]: halfway between the
        /// nozzle exits and the nose. The flight camera orbits and zooms towards this point, not the bottom of the rocket.
        /// </summary>
        public double VisualCenterHeight => model != null && !VisualHidden ? 0.5 * (nozzleY + stackTop) : 0.0;

        /// <summary>Render position of the middle of the visible stack (<see cref="VisualCenterHeight"/> along the axis).</summary>
        public Vector3d RenderCenter(double alpha, double renderTime, double centerHeight)
        {
            Vector3d p = RenderPosition(alpha, renderTime);
            if (centerHeight == 0.0 || planet == null || planet.Model == null) return p;
            Vector3d up = (p - planet.Model.Position).Normalized;
            return p + RenderAxis(p, up) * centerHeight;
        }

        // ---- Visuals -------------------------------------------------------------------------

        /// <summary>
        /// Builds the 3D rocket from the applied design with <see cref="RocketModelBuilder"/> — the same parts, sizes and module
        /// models the hangar shows — plus the plume and the heating glow. Purely visual.
        /// </summary>
        void RebuildVisual()
        {
            model?.Destroy();
            model = null;
            foreach (Transform child in visual) Destroy(child.gameObject);
            if (heatMaterial != null) Destroy(heatMaterial);
            heatMaterial = null;
            laidOutStage = -1;
            shownLegs = shownChute = -1; // the new model starts folded: show the systems' state again
            heatGlow = null;
            if (palette == null) palette = new RocketPalette(lit);
            model = RocketModelBuilder.Build(blueprint, rocket.radius, ownsModule, visual, palette, ModuleVisualLibrary.Active);
            if (exhaust == null)
            {
                exhaust = gameObject.AddComponent<RocketExhaustEffects>();
                exhaust.Initialize(this, planet, controller, origin);
            }
            exhaust.Attach(visual, model);

            if (sim.Rocket != null)
            {
                heatMaterial = ProceduralMeshes.EffectMaterial("SpaceSim/HeatGlow");
                if (heatMaterial != null)
                {
                    // A plasma sheath around the whole stack, pushed towards the oncoming air (placed every frame).
                    heatGlow = ProceduralMeshes.Primitive(PrimitiveType.Sphere, "HeatGlow", visual, heatMaterial).transform;
                }
            }
        }

        Transform heatGlow;
        Material heatMaterial;
        float nozzleY, stackTop;

        /// <summary>
        /// Shows the stages still attached: the bottom of the lowest one (its nozzle exits) stands on the ground under the reference
        /// point — on the pad, on its raised deck. The fairing covers the payload while any stage remains; then the probe flies
        /// free with its solar wings open.
        /// </summary>
        void LayoutStack()
        {
            if (model == null) return;
            var r = sim.Rocket;
            RocketLayout layout = model.Layout;
            int first = r != null ? r.StageIndex : int.MaxValue;
            bool onPad = IsOnLaunchPad;
            int key = (first == int.MaxValue ? 1000 : first) * 2 + (onPad ? 1 : 0);
            if (key == laidOutStage) return;
            laidOutStage = key;
            bool stack = first < layout.StageCount;
            float ground = -(float)contactRadius + (onPad ? LaunchSite.DeckHeight : 0f);
            float bottom = stack ? (float)layout.StageBottom[first] : (float)layout.PayloadBottom;
            model.Root.localPosition = new Vector3(0f, ground - bottom, 0f);
            for (int i = 0; i < model.Stages.Length; i++) model.Stages[i].SetActive(stack && i >= first);
            model.Fairing.SetActive(stack);
            model.SetWingsDeployed(!stack);
            nozzleY = ground;
            stackTop = ground - bottom + (float)(stack ? layout.FairingTop : layout.PayloadTop);
        }

        /// <summary>Stagnation heat flux at the nose now [W/m²] (Sutton–Graves, <see cref="AtmosphericHeating"/>).</summary>
        public double HeatFlux { get; private set; }

        void LateUpdate()
        {
            if (sim == null || origin == null || planet == null || planet.Model == null) return;
            double t = controller.RenderTime;
            Vector3d p = RenderPosition(controller.InterpolationAlpha, t);
            Vector3d up = (p - planet.Model.Position).Normalized;
            // Vehicle axis: vertical on the ground, the commanded attitude in flight.
            Vector3d axis = RenderAxis(p, up);
            Vector3 axisUnity = CoordinateSystem.ToUnityVector(axis);
            Vector3 fwd = Vector3.ProjectOnPlane(CoordinateSystem.ToUnityVector(planet.Model.Rotation.SpinAxis), axisUnity);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.Cross(axisUnity, Vector3.right);
            transform.SetPositionAndRotation(origin.ToUnityPosition(p), Quaternion.LookRotation(fwd.normalized, axisUnity));

            LayoutStack();
            var model = planet.Model;
            double h = model.AltitudeAboveReference(p);
            double p0 = model.Atmosphere.Pressure(0.0);
            double vacuum = p0 > 0.0 ? 1.0 - System.Math.Min(1.0, model.Atmosphere.Pressure(h) / p0) : 1.0;
            if (exhaust != null) exhaust.Tick(new RocketExhaustEffects.Frame { Position = p, Axis = axis, Vacuum = vacuum, NozzleY = nozzleY });
            // Aerodynamic heating at the nose (Level A estimate) → glow colour and brightness.
            Vector3d airVelocity = sim.State.Velocity - model.AirVelocityAt(sim.State.Position);
            HeatFlux = sim.IsLanded ? 0.0 : AtmosphericHeating.StagnationHeatFlux(model.DensityAt(sim.State.Position), airVelocity.Magnitude, rocket.radius);
            if (heatGlow != null)
            {
                float glow = HeatFlux > 2e4 ? Mathf.Clamp01((float)System.Math.Log10(HeatFlux / 2e4) / 2f) : 0f;
                heatGlow.gameObject.SetActive(glow > 0.01f);
                if (glow > 0.01f)
                {
                    float length = Mathf.Max(1f, stackTop - nozzleY), width = 2.6f * (float)rocket.radius;
                    heatGlow.localScale = new Vector3(width, length * 1.08f, width);
                    heatGlow.localPosition = new Vector3(0f, nozzleY + 0.5f * length, 0f);
                    Vector3 flow = CoordinateSystem.ToUnityVector(airVelocity.Normalized);
                    heatGlow.position += flow * (0.35f * width); // the windward side (facing the motion) glows
                    double temperature = AtmosphericHeating.EquilibriumTemperature(HeatFlux);
                    float warm = Mathf.Clamp01((float)(temperature - 800.0) / 1600f);
                    heatMaterial.SetColor("_Color", Color.Lerp(new Color(0.7f, 0.12f, 0.05f), new Color(1f, 0.85f, 0.55f), warm));
                    heatMaterial.SetFloat("_Intensity", glow * 1.5f);
                }
            }
        }
    }
}
