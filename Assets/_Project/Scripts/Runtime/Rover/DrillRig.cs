using SpaceSim.Exploration;
using SpaceSim.Localization;
using SpaceSim.Rover;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The drills on board: a key starts a hole under the standing vehicle; it takes the time <see cref="DrillCores.DrillTime"/> gives
    /// (simulation time, so time warp shortens the wait) and the core then goes into the sample container through the same rules
    /// and the same profile record as a surface sample (<see cref="SurfaceExplorer.TryCollect"/>). Moving, launching or going outside
    /// stops the drill.
    /// </summary>
    [DefaultExecutionOrder(113)]
    public sealed class DrillRig : MonoBehaviour
    {
        FlightBody body;
        FlightStateController state;
        PlanetRuntime planet;
        SurfaceExplorer explorer;
        PlayerSession session;
        RoverController rover;

        DrillCore core;
        double started, duration;
        RocketModel startModel;
        SpaceSim.Mathematics.Vector3d startDirection;

        public bool Busy => core != null;
        /// <summary>0..1 while drilling.</summary>
        public double Progress => core == null || duration <= 0.0 ? 0.0 : MathD01((Now - started) / duration);
        public double Duration => duration;
        public DrillCore Current => core;

        double Now => body != null && body.Simulation != null ? body.Simulation.State.Time : 0.0;

        public void Initialize(FlightBody flightBody, FlightStateController flightState, PlanetRuntime planetRuntime, SurfaceExplorer surface,
            PlayerSession playerSession, RoverController roverController)
        {
            body = flightBody;
            state = flightState;
            planet = planetRuntime;
            explorer = surface;
            session = playerSession;
            rover = roverController;
        }

        /// <summary>The core a drill would take here now (null without a drill, over water, or when not landed).</summary>
        public DrillCore Preview()
        {
            var sim = body?.Simulation;
            VehicleCapabilities caps = body != null ? body.DesignCapabilities : null;
            if (sim == null || !sim.IsLanded || caps == null || caps.DrillCount <= 0) return null;
            return DrillCores.At(planet.Definition.planetId, planet.Model.Radius, sim.LandedFixedPosition, caps.DrillDepth, explorer != null ? explorer.WaterTest() : null);
        }

        /// <summary>Starts drilling; returns the message shown to the player.</summary>
        public string Begin()
        {
            if (!SpaceSim.Runtime.Multiplayer.CrewSession.Can(SpaceSim.Multiplayer.CrewAction.WorkOnSurface))
            {
                SpaceSim.Runtime.Multiplayer.CrewGate.Deny(SpaceSim.Multiplayer.CrewAction.WorkOnSurface, state);
                return SpaceSim.Runtime.Multiplayer.CrewSession.DeniedText(SpaceSim.Multiplayer.CrewAction.WorkOnSurface);
            }
            string msg = Start();
            state?.Notify(msg);
            return msg;
        }

        string Start()
        {
            var sim = body?.Simulation;
            if (Busy) return Loc.T("Бур уже работает: ") + UnitFormatter.Number(Progress * 100.0, 0) + " %.";
            if (sim == null || !sim.IsLanded || body.Destroyed) return Loc.T("Бурить можно только стоя на грунте.");
            if (explorer != null && explorer.Eva) return Loc.T("Бур управляется из аппарата: вернитесь внутрь.");
            if (body.Rocket != null && body.Rocket.IsBurning) return Loc.T("Двигатель работает — бурение невозможно.");
            if (rover != null && rover.Active && System.Math.Abs(rover.Motion.Speed) > 0.05) return Loc.T("Остановите ровер перед бурением.");
            CollectionCheck can = DrillCores.CanDrill(body.Capabilities);
            if (!can.Ok) return body.DesignCapabilities.DrillCount > 0 ? Loc.T("Приборы повреждены — бур не работает.") : can.Reason;
            DrillCore here = Preview();
            if (here == null) return Loc.T("Под аппаратом вода — бурить нечего.");
            if (session == null) return Loc.T("Нет профиля игрока.");
            if (session.Profile.collectedSites.Contains(here.Site.Id)) return Loc.T("Здесь уже бурили: отъедьте на ") + DrillCores.CellSize + Loc.T(" м.");
            CollectionCheck check = SampleCollection.Check(here.Site, body.Capabilities, session.Profile.CargoMass(), session.Profile.collectedSites);
            if (!check.Ok) return check.Reason;
            double supply = body.Rocket != null ? RoverKinematics.SupplyFraction(body.Rocket.Electrics) : 1.0;
            double power = DrillCores.DrillPower(body.Blueprint, body.OwnsModule);
            duration = DrillCores.DrillTime(here.Energy, power, supply);
            if (double.IsPositiveInfinity(duration)) return Loc.T("Нет питания для бура.");
            core = here;
            started = Now;
            startModel = body.Model;
            startDirection = sim.LandedFixedPosition;
            return Loc.T("Бурение до ") + UnitFormatter.Number(here.Depth, 2) + Loc.T(" м (грунт ") + UnitFormatter.Number(here.SoilDepth, 2) +
                   Loc.T(" м, ниже порода): энергия на долоте ") + UnitFormatter.Number(here.Energy / 1000.0, 1) + Loc.T(" кДж при ") +
                   UnitFormatter.Number(power * DrillCores.DrillEfficiency, 0) + Loc.T(" Вт — около ") + UnitFormatter.Number(duration, 0) + Loc.T(" с.");
        }

        public void Abort(string reason)
        {
            if (!Busy) return;
            core = null;
            state?.Notify(Loc.T("Бурение прервано: ") + reason);
        }

        void Update()
        {
            if (body == null || body.Simulation == null) return;
            if (!FlightInput.IsTyping && FlightInput.GetKeyDown(InputKey.Alpha5)) Begin();
            if (!Busy) return;
            var sim = body.Simulation;
            if (!sim.IsLanded || body.Destroyed) Abort(Loc.T("аппарат не на грунте."));
            else if (body.Rocket != null && body.Rocket.IsBurning) Abort(Loc.T("запущен двигатель."));
            else if (explorer != null && explorer.Eva) Abort(Loc.T("экипаж вышел наружу."));
            else if ((sim.LandedFixedPosition - startDirection).Magnitude > 0.5 || !ReferenceEquals(body.Model, startModel)) Abort(Loc.T("аппарат сдвинулся."));
            else if (Now - started >= duration)
            {
                DrillCore done = core;
                core = null;
                explorer.TryCollect(done.Site);
            }
        }

        static double MathD01(double x) => x < 0.0 ? 0.0 : x > 1.0 ? 1.0 : x;
    }
}
