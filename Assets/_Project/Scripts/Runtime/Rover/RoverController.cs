using System;
using System.Collections.Generic;
using SpaceSim.Exploration;
using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Rover;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Driving the landed vehicle as a rover. Only on the ground, only with a complete rover (chassis, drive, at least
    /// <see cref="PartPhysics.MinRoverWheels"/> wheels) whose wheels carry the whole remaining vehicle at the local gravity. The
    /// player drives with the keys; there is no autopilot. Motion comes from <see cref="RoverKinematics"/> (Core) on the planet's
    /// own terrain heights, in simulation time; the new place goes back into the flight physics through
    /// <see cref="FlightBody.DriveTo"/>, which keeps the vehicle landed (co-rotating with the planet, floating origin as usual).
    /// The model is turned to the surface normal and the heading, and the wheels spin and steer — purely visual.
    /// Igniting the engine ends the drive.
    /// </summary>
    [DefaultExecutionOrder(112)]
    public sealed class RoverController : MonoBehaviour
    {
        const double SubStep = 0.02;
        const int MaxSubsteps = 250;

        FlightBody body;
        FlightStateController state;
        PlanetRuntime planet;
        SurfaceExplorer explorer;
        RocketControls controls;
        SimulationController simulation;

        public VehicleLights Lights { get; private set; }
        public DrillRig Drill { get; private set; }
        public RoverHUD Hud { get; private set; }

        public bool Active { get; private set; }
        /// <summary>The crew is outside on foot.</summary>
        public bool Eva => explorer != null && explorer.Eva;
        public RoverMotion Motion { get; } = new RoverMotion();
        public RoverStep LastStep { get; private set; }
        public RoverSpec Spec { get; private set; }
        /// <summary>Grade along the heading [deg, + uphill] and the terrain slope [deg] under the rover.</summary>
        public double GradeDegrees { get; private set; }
        public double SlopeDegrees { get; private set; }
        public double Gravity { get; private set; }
        /// <summary>Share of the drive power the electrics give, 0..1.</summary>
        public double Supply { get; private set; } = 1.0;
        /// <summary>Last command: throttle and steering, −1..1.</summary>
        public double Throttle { get; private set; }
        public double Steer { get; private set; }
        public string Blocked { get; private set; } = "";

        /// <summary>The vehicle has been driven since it last landed: keep it on its wheels, facing its heading.</summary>
        bool driven;
        double lastSimTime;
        double scriptThrottle, scriptSteer;
        float scriptUntil = -1f;

        sealed class Wheel
        {
            public Transform Transform;
            public Quaternion Rest;
            public bool Front;
            public float Radius, Angle;
        }

        RocketModel wheelModel;
        readonly List<Wheel> wheels = new List<Wheel>();

        public int WheelVisuals => wheels.Count;

        public void Initialize(FlightBody flightBody, FlightStateController flightState, PlanetRuntime planetRuntime, SurfaceExplorer surface,
            RocketControls rocketControls, PlayerSession session, SimulationController sim)
        {
            body = flightBody;
            state = flightState;
            planet = planetRuntime;
            explorer = surface;
            controls = rocketControls;
            simulation = sim;
            if (explorer != null) explorer.Rover = this;
            Lights = gameObject.AddComponent<VehicleLights>();
            Lights.Initialize(body, state);
            Drill = gameObject.AddComponent<DrillRig>();
            Drill.Initialize(body, state, planet, explorer, session, this);
            Hud = gameObject.AddComponent<RoverHUD>();
            Hud.Initialize(this, body, planet);
        }

        void OnDestroy()
        {
            if (Active && controls != null) controls.Suspended = false;
        }

        // ---- Mode -------------------------------------------------------------------------------------------

        /// <summary>The rover of the current design carrying the vehicle as it stands now.</summary>
        RoverSpec CurrentSpec() => RoverSpec.From(body.DesignCapabilities, body.Simulation.CurrentMass);

        double LocalGravity() => PlanetaryGravity.AccelerationMagnitude(planet.Model.GravitationalParameter, body.Simulation.LandedFixedPosition.Magnitude);

        /// <summary>Whether the vehicle can drive now; the reason (with the numbers) when it cannot.</summary>
        public string CanDrive()
        {
            var sim = body?.Simulation;
            if (sim == null || !sim.IsLanded || body.Destroyed) return Loc.T("Режим ровера — только на грунте.");
            if (explorer != null && explorer.Eva) return Loc.T("Сначала вернитесь в аппарат.");
            if (body.Rocket != null && body.Rocket.IsBurning) return Loc.T("Работает двигатель.");
            VehicleCapabilities caps = body.DesignCapabilities;
            if (caps.WheelCount == 0 && !caps.HasRoverChassis && !caps.HasRoverDrive)
                return Loc.T("Это не ровер: нужны шасси ШС-1, привод ПД-1 и не меньше ") + PartPhysics.MinRoverWheels + Loc.T(" колёс (вкладка «Механика» в ангаре).");
            if (!caps.RoverAssembled)
                return Loc.T("Ровер не собран: шасси ") + (caps.HasRoverChassis ? Loc.T("есть") : Loc.T("нет")) + Loc.T(", привод ") +
                       (caps.HasRoverDrive ? Loc.T("есть") : Loc.T("нет")) + Loc.T(", колёс ") + caps.WheelCount + Loc.T(" из ") + PartPhysics.MinRoverWheels + ".";
            RoverSpec spec = CurrentSpec();
            double g = LocalGravity();
            if (!PartPhysics.WheelsCarry(spec.WheelLoad, spec.Mass, g))
                return Loc.T("Колёса не выдержат: вес m·g = ") + UnitFormatter.Number(spec.Mass * g / 1000.0, 1) + Loc.T(" кН (масса ") +
                       UnitFormatter.Number(spec.Mass / 1000.0, 2) + Loc.T(" т), колёса держат ") + UnitFormatter.Number(spec.WheelLoad / 1000.0, 1) +
                       Loc.T(" кН. Отделите пустые ступени или поставьте колёса КЛ-100.");
            if (Drill != null && Drill.Busy) return Loc.T("Идёт бурение.");
            return "";
        }

        /// <summary>Team mission: driving and the manipulator belong to the robot operator.</summary>
        string Deny()
        {
            SpaceSim.Runtime.Multiplayer.CrewGate.Deny(SpaceSim.Multiplayer.CrewAction.WorkOnSurface, state);
            return SpaceSim.Runtime.Multiplayer.CrewSession.DeniedText(SpaceSim.Multiplayer.CrewAction.WorkOnSurface);
        }

        /// <summary>Q: drive mode on/off; returns the message shown to the player.</summary>
        public string Toggle() => Active ? Disable(Loc.T("Режим ровера выключен.")) : Enable();

        public string Enable()
        {
            if (Active) return "";
            if (!SpaceSim.Runtime.Multiplayer.CrewSession.Can(SpaceSim.Multiplayer.CrewAction.WorkOnSurface)) return Deny();
            string reason = CanDrive();
            if (reason.Length > 0)
            {
                state?.Notify(reason);
                return reason;
            }
            Active = true;
            driven = true;
            Motion.Speed = 0.0;
            Blocked = "";
            lastSimTime = body.Simulation.State.Time;
            if (controls != null) controls.Suspended = true;
            Spec = CurrentSpec();
            Gravity = LocalGravity();
            double flat = RoverKinematics.MaxSpeed(Spec, Gravity, 0.0);
            string msg = Loc.T("Режим ровера: ") + MoveKeys() + Loc.T(" — газ/руль, ") + FlightInput.KeyLabel(InputKey.Q) + Loc.T(" — выйти. g = ") +
                         UnitFormatter.Number(Gravity, 2) + Loc.T(" м/с², масса ") + UnitFormatter.Number(Spec.Mass, 0) + Loc.T(" кг, по ровному до ") +
                         UnitFormatter.Number(flat, 2) + Loc.T(" м/с (колёса — до ") + UnitFormatter.Number(Spec.WheelMaxSpeed, 1) + Loc.T(" м/с), подъём до ") +
                         UnitFormatter.Number(RoverKinematics.ClimbLimitDegrees, 0) + "°.";
            state?.Notify(msg);
            return msg;
        }

        public string Disable(string message)
        {
            if (!Active) return "";
            Active = false;
            Motion.Speed = 0.0;
            Throttle = Steer = 0.0;
            if (controls != null) controls.Suspended = false;
            if (!string.IsNullOrEmpty(message)) state?.Notify(message);
            return message ?? "";
        }

        /// <summary>Scripted keys for tests and the development bridge: throttle and steering for some real seconds.</summary>
        public void Drive(double throttle, double steer, float seconds)
        {
            scriptThrottle = MathD.Clamp(throttle, -1.0, 1.0);
            scriptSteer = MathD.Clamp(steer, -1.0, 1.0);
            scriptUntil = Time.unscaledTime + seconds;
        }

        // ---- Frame loop --------------------------------------------------------------------------------------

        void Update()
        {
            if (body == null || body.Simulation == null || planet == null || planet.Model == null) return;
            var sim = body.Simulation;
            if (!ReferenceEquals(body.Model, wheelModel))
            {
                // A new vehicle was built (new rocket on the pad, hangar): it starts upright and not driving.
                bool first = wheelModel == null;
                BindWheels();
                driven = false;
                if (!first && Active) Disable(Loc.T("Режим ровера выключен."));
            }
            if (!sim.IsLanded || body.Destroyed) driven = false;
            if (Active)
            {
                string stop = !sim.IsLanded || body.Destroyed ? Loc.T("Режим ровера выключен: аппарат не на грунте.")
                    : explorer != null && explorer.Eva ? Loc.T("Режим ровера выключен: экипаж снаружи.")
                    : body.Rocket != null && body.Rocket.IsBurning ? Loc.T("Режим ровера выключен: запущен двигатель.")
                    : null;
                if (stop != null) Disable(stop);
            }

            bool typing = FlightInput.IsTyping;
            if (!typing && FlightInput.GetKeyDown(InputKey.Q)) Toggle();
            if (!Active)
            {
                lastSimTime = sim.State.Time;
                return;
            }
            if (!typing && FlightInput.GetKeyDown(InputKey.Return))
            {
                // Igniting ends the drive; the rocket's own controls take over.
                Disable(Loc.T("Режим ровера выключен: зажигание."));
                controls?.Ignite();
                return;
            }
            if (!typing && FlightInput.GetKeyDown(InputKey.E)) UseManipulator();

            double throttle = 0.0, steer = 0.0;
            if (!typing)
            {
                throttle = (FlightInput.GetKey(InputKey.W) ? 1 : 0) - (FlightInput.GetKey(InputKey.S) ? 1 : 0);
                steer = (FlightInput.GetKey(InputKey.D) ? 1 : 0) - (FlightInput.GetKey(InputKey.A) ? 1 : 0);
            }
            if (Time.unscaledTime < scriptUntil)
            {
                throttle = scriptThrottle;
                steer = scriptSteer;
            }
            if (Drill != null && Drill.Busy) throttle = 0.0; // the drill is in the ground: the brakes hold
            Throttle = throttle;
            Steer = steer;
            StepMotion(sim);
        }

        void StepMotion(SpaceSim.Flight.FreeFlightSimulation sim)
        {
            double now = sim.State.Time;
            double elapsed = Math.Min(now - lastSimTime, SubStep * MaxSubsteps); // simulation time: pause stops, warp speeds up
            lastSimTime = now;
            Spec = CurrentSpec();
            Gravity = LocalGravity();
            Supply = body.Rocket != null ? RoverKinematics.SupplyFraction(body.Rocket.Electrics) : 1.0;
            ISurfaceHeightProvider surface = planet.Model.Surface;
            double radius = planet.Model.Radius;
            Vector3d dir = sim.LandedFixedPosition.Normalized;
            if (elapsed > 0.0)
            {
                int n = Math.Max(1, (int)Math.Ceiling(elapsed / SubStep));
                double h = elapsed / n;
                Func<Vector3d, bool> water = explorer != null ? explorer.WaterTest() : null;
                bool moved = false;
                Blocked = "";
                for (int i = 0; i < n; i++)
                {
                    double grade = RoverTerrain.GradeAlong(surface, radius, dir, Motion.HeadingDegrees);
                    double v0 = Motion.Speed;
                    LastStep = RoverKinematics.Step(Motion, Spec, Gravity, grade, Throttle, Steer, Supply, h);
                    double ds = 0.5 * (v0 + Motion.Speed) * h;
                    if (Math.Abs(ds) < 1e-9) continue;
                    Vector3d next = RoverTerrain.Advance(dir, Motion.HeadingDegrees, ds, radius);
                    if (water != null && water(next))
                    {
                        Motion.Speed = 0.0;
                        Blocked = Loc.T("впереди вода");
                        break;
                    }
                    dir = next;
                    moved = true;
                }
                if (moved) body.DriveTo(dir);
            }
            GradeDegrees = RoverTerrain.GradeAlong(surface, radius, dir, Motion.HeadingDegrees) * MathD.RadToDeg;
            SlopeDegrees = LandingPhysics.TerrainSlope(surface, radius, dir);
        }

        /// <summary>E while driving: the manipulator takes the nearest surface sample in its reach (no one goes outside).</summary>
        public string UseManipulator()
        {
            if (!SpaceSim.Runtime.Multiplayer.CrewSession.Can(SpaceSim.Multiplayer.CrewAction.WorkOnSurface)) return Deny();
            string msg = Manipulate();
            if (msg.Length > 0) state?.Notify(msg);
            return msg;
        }

        /// <summary>Reach of the manipulator from the vehicle's reference point [m]: arm length + hull radius + the point's height.</summary>
        public double ManipulatorReach
        {
            get
            {
                ModuleDefinition arm = ModuleCatalog.Find(ModuleCatalog.Manipulator);
                double length = arm != null ? arm.length : 1.5;
                double hull = body != null && body.Compiled != null && body.Compiled.Configuration != null ? body.Compiled.Configuration.radius : 1.0;
                return length + hull + 1.0;
            }
        }

        string Manipulate()
        {
            var sim = body.Simulation;
            if (body.Capabilities.ManipulatorCount <= 0)
                return body.DesignCapabilities.ManipulatorCount > 0 ? Loc.T("Приборы повреждены — манипулятор не работает.")
                    : Loc.T("Из ровера образцы берёт манипулятор МН-1 — его нет. Выйти наружу: ") + FlightInput.KeyLabel(InputKey.Q) + Loc.T(", затем ") +
                      FlightInput.KeyLabel(InputKey.E) + ".";
            explorer.FindNearest(sim.LandedFixedPosition);
            double reach = ManipulatorReach;
            if (explorer.Nearest == null) return Loc.T("Рядом нет образцов.");
            if (explorer.NearestDistance > reach)
                return Loc.T("Манипулятор достаёт на ") + UnitFormatter.Number(reach, 1) + Loc.T(" м, до образца «") + explorer.Nearest.Name + "» " +
                       UnitFormatter.Number(explorer.NearestDistance, 1) + Loc.T(" м — подъедьте ближе.");
            return explorer.TryCollect(explorer.Nearest);
        }

        // ---- Visuals ------------------------------------------------------------------------------------------

        void BindWheels()
        {
            wheels.Clear();
            wheelModel = body.Model;
            if (wheelModel == null) return;
            foreach (Transform t in wheelModel.WheelTransforms)
            {
                if (t == null) continue;
                ModuleDefinition m = VehicleLights.ModuleOf(wheelModel, t);
                float r = m != null && m.diameter > 0.0 ? (float)(0.5 * m.diameter) : m != null && m.id == ModuleCatalog.WheelLarge ? 0.5f : 0.2f;
                wheels.Add(new Wheel
                {
                    Transform = t, Rest = t.localRotation, Radius = Mathf.Max(0.05f, r),
                    Front = body.transform.InverseTransformPoint(t.position).z > 0f
                });
            }
        }

        void LateUpdate()
        {
            if (body == null || body.Simulation == null || planet == null || planet.Model == null || body.VisualHidden) return;
            var sim = body.Simulation;
            bool upright = !driven || !sim.IsLanded || body.Destroyed || (body.Rocket != null && body.Rocket.IsBurning);
            if (!upright)
            {
                // On its wheels: up = the terrain normal, nose along the heading (after FlightBody placed it this frame).
                double t = simulation.RenderTime;
                ISurfaceHeightProvider surface = planet.Model.Surface;
                Vector3d dir = sim.LandedFixedPosition.Normalized;
                Vector3d n = RoverTerrain.Normal(surface, planet.Model.Radius, dir);
                Vector3d f = Vector3d.ProjectOnPlane(RoverTerrain.Heading(dir, Motion.HeadingDegrees), n).Normalized;
                Vector3 nU = CoordinateSystem.ToUnityVector(planet.Model.Rotation.FixedToInertial(n, t));
                Vector3 fU = CoordinateSystem.ToUnityVector(planet.Model.Rotation.FixedToInertial(f, t));
                body.transform.rotation = Quaternion.LookRotation(fU, nU);
                if (body.IsOnLaunchPad && explorer != null)
                {
                    // FlightBody stands the model on the pad's deck anywhere near the pad; off the deck the ground is lower.
                    double lift = explorer.WalkSurface().SurfaceHeight(dir) - surface.SurfaceHeight(dir);
                    Vector3 radial = CoordinateSystem.ToUnityVector(planet.Model.Rotation.FixedToInertial(dir, t));
                    body.transform.position += radial * (float)(lift - LaunchSite.DeckHeight);
                }
            }
            AnimateWheels();
        }

        void AnimateWheels()
        {
            if (wheels.Count == 0) return;
            float dt = simulation.Paused ? 0f : Time.unscaledDeltaTime * (float)Math.Min(simulation.TimeWarp, 50.0);
            float steerDeg = Active ? (float)(Steer * RoverKinematics.MaxSteerDegrees) : 0f;
            foreach (Wheel w in wheels)
            {
                if (w.Transform == null) continue;
                w.Angle = Mathf.Repeat(w.Angle + (float)(Motion.Speed * dt / w.Radius) * Mathf.Rad2Deg, 360f);
                Quaternion steering = w.Front ? Quaternion.AngleAxis(steerDeg, Vector3.up) : Quaternion.identity;
                w.Transform.localRotation = w.Rest * steering * Quaternion.AngleAxis(w.Angle, Vector3.right);
            }
        }

        /// <summary>"WASD" with the default keys.</summary>
        internal static string MoveKeys()
        {
            string[] k = { FlightInput.KeyLabel(InputKey.W), FlightInput.KeyLabel(InputKey.A), FlightInput.KeyLabel(InputKey.S), FlightInput.KeyLabel(InputKey.D) };
            bool letters = k[0].Length == 1 && k[1].Length == 1 && k[2].Length == 1 && k[3].Length == 1;
            return string.Join(letters ? "" : "/", k);
        }

        /// <summary>One line for the development bridge.</summary>
        public string StatusLine()
        {
            var sim = body?.Simulation;
            if (sim == null) return "no body";
            PlanetRotation.LatLonFromDirection(sim.LandedFixedPosition.Normalized, out double lat, out double lon);
            return "rover active=" + Active + " can=\"" + CanDrive() + "\" landed=" + sim.IsLanded + " v=" + UnitFormatter.Number(Motion.Speed, 3) +
                   " heading=" + UnitFormatter.Number(Motion.HeadingDegrees, 1) + " grade=" + UnitFormatter.Number(GradeDegrees, 1) + " slope=" +
                   UnitFormatter.Number(SlopeDegrees, 1) + " mode=" + LastStep.Mode + " power=" + UnitFormatter.Number(LastStep.Power, 0) + "W supply=" +
                   UnitFormatter.Number(Supply, 2) + " dist=" + UnitFormatter.Number(Motion.Distance, 1) + " energy=" + UnitFormatter.Number(Motion.EnergyUsed / 3600.0, 2) +
                   "Wh mass=" + UnitFormatter.Number(Spec.Mass, 0) + " g=" + UnitFormatter.Number(Gravity, 3) + " wheels=" + body.DesignCapabilities.WheelCount +
                   " wheelVisuals=" + wheels.Count + " lat=" + UnitFormatter.Number(lat * MathD.RadToDeg, 5) + " lon=" + UnitFormatter.Number(lon * MathD.RadToDeg, 5) +
                   " drill=" + (Drill != null && Drill.Busy ? UnitFormatter.Number(Drill.Progress * 100.0, 0) + "%" : "idle") +
                   " lights=" + (Lights != null ? Lights.On + "/" + Lights.LampCount + "of" + Lights.AnchorCount : "-");
        }
    }
}
