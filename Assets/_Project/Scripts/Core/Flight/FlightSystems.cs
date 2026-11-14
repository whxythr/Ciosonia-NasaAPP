using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Physics;
using SpaceSim.Vehicles;

namespace SpaceSim.Flight
{
    /// <summary>State of one canopy in flight.</summary>
    public enum CanopyState
    {
        /// <summary>Packed, not yet released.</summary>
        Stowed,
        /// <summary>Released: inflates while there is enough air (<see cref="FlightSystems.ParachuteOpening"/>).</summary>
        Deployed,
        /// <summary>Torn by too high a load or speed: no drag any more.</summary>
        Torn
    }

    /// <summary>What happened when the pilot released the parachutes.</summary>
    public enum ParachuteCommand
    {
        NoParachutes,
        AlreadyReleased,
        Released
    }

    /// <summary>
    /// The non-propulsive equipment of a vehicle in flight (Level A): landing legs, parachutes, RCS, fins and the heat shield. Built from
    /// the design's <see cref="VehicleCapabilities"/>; side hardware counts only while its stage is attached
    /// (<see cref="VehicleCapabilities.HardwareFrom"/>). Every action here is the pilot's — nothing steers the vehicle; the fins and the
    /// air only act as physics does. Formulas live in <see cref="PartPhysics"/>.
    /// </summary>
    public sealed class FlightSystems
    {
        readonly VehicleCapabilities caps;
        readonly CanopyState[] canopies;
        readonly double[] opening;
        readonly List<string> events = new List<string>();
        StageHardware hardware;
        int hardwareStage = -1;

        public FlightSystems(VehicleCapabilities capabilities)
        {
            caps = capabilities ?? new VehicleCapabilities();
            canopies = new CanopyState[caps.Parachutes.Count];
            opening = new double[caps.Parachutes.Count];
            DesignHasLegs = caps.LegCount > 0;
            RcsEnabled = caps.RcsThrust > 0.0;
        }

        public VehicleCapabilities Capabilities => caps;

        /// <summary>Side hardware of the stages still attached when <paramref name="stageIndex"/> is the bottom one.</summary>
        public StageHardware Hardware(int stageIndex)
        {
            if (stageIndex != hardwareStage || hardware == null)
            {
                hardware = caps.HardwareFrom(stageIndex);
                hardwareStage = stageIndex;
            }
            return hardware;
        }

        /// <summary>Messages for the pilot raised by the physics (a torn canopy…); <see cref="DrainEvents"/> takes them.</summary>
        public IReadOnlyList<string> Events => events;

        public List<string> DrainEvents()
        {
            var list = new List<string>(events);
            events.Clear();
            return list;
        }

        // ---- Landing legs -------------------------------------------------------------------------------

        /// <summary>Any leg set is in the design (else the vehicle keeps its built-in landing limits).</summary>
        public bool DesignHasLegs { get; }
        /// <summary>The legs are extended (a pilot command; dropped stages take theirs with them).</summary>
        public bool LegsDeployed { get; set; }

        /// <summary>Touchdown verdict with the legs still attached and their state.</summary>
        public LandingOutcome AssessLanding(int stageIndex, double verticalSpeed, double horizontalSpeed, double tiltDegrees, double slopeDegrees)
        {
            StageHardware h = Hardware(stageIndex);
            return LandingPhysics.Assess(verticalSpeed, horizontalSpeed, tiltDegrees, slopeDegrees, DesignHasLegs, h.LegCount, h.LegTouchdownSpeed, LegsDeployed);
        }

        // ---- Parachutes ---------------------------------------------------------------------------------

        public int ParachuteCount => canopies.Length;
        public CanopyState Canopy(int i) => canopies[i];
        public int CanopiesIn(CanopyState state)
        {
            int n = 0;
            foreach (CanopyState c in canopies)
                if (c == state) n++;
            return n;
        }

        /// <summary>Mean inflation of the released, intact canopies 0..1 (for the view).</summary>
        public double ParachuteOpening
        {
            get
            {
                double sum = 0.0;
                int n = 0;
                for (int i = 0; i < canopies.Length; i++)
                {
                    if (canopies[i] != CanopyState.Deployed) continue;
                    sum += opening[i];
                    n++;
                }
                return n > 0 ? sum / n : 0.0;
            }
        }

        /// <summary>Releases every packed canopy. They fill once there is air and tear if the load or the speed is too high.</summary>
        public ParachuteCommand ReleaseParachutes()
        {
            if (canopies.Length == 0) return ParachuteCommand.NoParachutes;
            bool any = false;
            for (int i = 0; i < canopies.Length; i++)
            {
                if (canopies[i] != CanopyState.Stowed) continue;
                canopies[i] = CanopyState.Deployed;
                opening[i] = 0.0;
                any = true;
            }
            return any ? ParachuteCommand.Released : ParachuteCommand.AlreadyReleased;
        }

        /// <summary>Summed C_d·A of the released, intact canopies times their inflation [m²] — the drag they add.</summary>
        public double ParachuteDragArea
        {
            get
            {
                double sum = 0.0;
                for (int i = 0; i < canopies.Length; i++)
                    if (canopies[i] == CanopyState.Deployed) sum += caps.Parachutes[i].DragArea * opening[i];
                return sum;
            }
        }

        /// <summary>Inflates the released canopies and tears those over their limits, at density ρ [kg/m³] and air speed v [m/s].</summary>
        public void UpdateParachutes(double dt, double density, double airSpeed)
        {
            if (density <= 0.0) return; // no air: the canopies trail limp, nothing loads them
            double q = AtmosphericDrag.DynamicPressure(density, airSpeed);
            for (int i = 0; i < canopies.Length; i++)
            {
                if (canopies[i] != CanopyState.Deployed) continue;
                ParachuteSpec p = caps.Parachutes[i];
                if (PartPhysics.ParachuteTears(q, airSpeed, p.MaxDynamicPressure, p.MaxSpeed))
                {
                    canopies[i] = CanopyState.Torn;
                    opening[i] = 0.0;
                    ModuleDefinition m = ModuleCatalog.Find(p.ModuleId);
                    string name = m != null ? m.name : p.ModuleId;
                    events.Add(Loc.T("Купол ") + name + Loc.T(" порван: скоростной напор ") + Num(q) + Loc.T(" Па (предел ") + Num(p.MaxDynamicPressure) +
                               Loc.T("), скорость ") + Num(airSpeed) + Loc.T(" м/с (предел ") + Num(p.MaxSpeed) + ").");
                    continue;
                }
                opening[i] = PartPhysics.ParachuteInflation(opening[i], q, dt);
            }
        }

        // ---- Attitude: RCS and fins ---------------------------------------------------------------------

        /// <summary>The pilot lets the RCS fire when turning (on by default when the design has RCS).</summary>
        public bool RcsEnabled { get; set; }
        /// <summary>The RCS fired during the last turn command.</summary>
        public bool RcsFiring { get; private set; }
        /// <summary>Propellant the RCS has used this flight [kg].</summary>
        public double RcsPropellantUsed { get; private set; }

        /// <summary>RCS thrust still attached [N].</summary>
        public double RcsThrust(RocketState r) => r != null && r.HasStages ? Hardware(r.StageIndex).RcsThrust : 0.0;

        /// <summary>Moment of inertia of the vehicle now [kg·m²].</summary>
        public static double Inertia(RocketState r) => r == null ? 0.0 : PartPhysics.TransverseInertia(r.Mass, r.StackLength, r.Configuration.radius);

        /// <summary>Arm of the side hardware about the centre of mass [m] (Level A: half the stack).</summary>
        public static double Arm(RocketState r) => r == null ? 0.0 : 0.5 * r.StackLength;

        /// <summary>RCS can fire now: enabled, attached, propellant left.</summary>
        public bool RcsAvailable(RocketState r) => RcsEnabled && RcsThrust(r) > 0.0 && r.RcsPropellantAvailable > 0.0;

        /// <summary>Angular acceleration [rad/s²] from the RCS (when available) and the grid fins (above their dynamic pressure).</summary>
        public double ControlAngularAcceleration(RocketState r, double dynamicPressure, bool withRcs)
        {
            if (r == null || !r.HasStages) return 0.0;
            double inertia = Inertia(r), arm = Arm(r);
            double torque = 0.0;
            if (withRcs && RcsAvailable(r)) torque += PartPhysics.RcsTorque(RcsThrust(r), arm);
            StageHardware h = Hardware(r.StageIndex);
            torque += PartPhysics.GridFinControlTorque(dynamicPressure, h.TopFinArea, r.Configuration.ReferenceArea, arm);
            return PartPhysics.AngularAcceleration(torque, inertia);
        }

        /// <summary>Grid fins have enough air to steer right now.</summary>
        public bool GridFinsActive(RocketState r, double dynamicPressure) =>
            r != null && r.HasStages && Hardware(r.StageIndex).TopFinArea > 0.0 && dynamicPressure >= PartPhysics.GridFinMinDynamicPressure;

        /// <summary>
        /// The rotation rate [deg/s] the pilot gets for a turn command with base rate <paramref name="baseRate"/> (the rate every vehicle has).
        /// When <paramref name="turning"/> and the RCS can fire, it fires for <paramref name="dt"/> seconds and burns ṁ = F/(I_sp·g₀).
        /// </summary>
        public double CommandTurn(RocketState r, double baseRate, double dynamicPressure, bool turning, double dt)
        {
            bool rcs = turning && RcsAvailable(r);
            RcsFiring = rcs;
            if (rcs && dt > 0.0) RcsPropellantUsed += r.DrawRcsPropellant(PartPhysics.RcsMassFlow(RcsThrust(r)) * dt);
            return PartPhysics.AttitudeRate(baseRate, ControlAngularAcceleration(r, dynamicPressure, rcs));
        }

        /// <summary>Weathervane rate [rad/s] of the attached side fins for angle of attack α [rad] at dynamic pressure q [Pa].</summary>
        public double WeathervaneRate(RocketState r, double dynamicPressure, double angleOfAttack)
        {
            if (r == null || !r.HasStages) return 0.0;
            StageHardware h = Hardware(r.StageIndex);
            return PartPhysics.WeathervaneRate(dynamicPressure, h.SideFinArea, r.Configuration.ReferenceArea, Arm(r), Inertia(r), angleOfAttack);
        }

        /// <summary>Rate [rad/s] at which the open canopies turn the axis against the airflow, for the angle θ [rad] from that direction.</summary>
        public double CanopyRightingRate(RocketState r, double dynamicPressure, double angle)
        {
            if (r == null || !r.HasStages) return 0.0;
            return PartPhysics.CanopyRightingRate(dynamicPressure, ParachuteDragArea, Arm(r), Inertia(r), angle);
        }

        // ---- Heat ---------------------------------------------------------------------------------------

        /// <summary>Stagnation heat flux at the nose now [W/m²].</summary>
        public double HeatFlux { get; private set; }
        /// <summary>Flux the vehicle tolerates: the heat shield's, never below the bare hull's [W/m²].</summary>
        public double AllowedHeatFlux => PartPhysics.AllowedHeatFlux(caps.HeatShieldFluxLimit);
        public bool HasHeatShield => caps.HeatShieldFluxLimit > 0.0;
        /// <summary>Equipment integrity lost to heat this flight (0..1 summed).</summary>
        public double HeatDamageTaken { get; private set; }
        bool overheatReported;

        /// <summary>Heats the nose for dt seconds; returns the equipment integrity lost (0 below the allowed flux).</summary>
        public double UpdateHeat(double dt, double density, double airSpeed, double noseRadius)
        {
            HeatFlux = AtmosphericHeating.StagnationHeatFlux(density, airSpeed, noseRadius);
            double damage = PartPhysics.HeatDamagePerSecond(HeatFlux, AllowedHeatFlux) * Math.Max(0.0, dt);
            if (damage > 0.0)
            {
                HeatDamageTaken += damage;
                if (!overheatReported)
                {
                    overheatReported = true;
                    events.Add(Loc.T("Перегрев: тепловой поток ") + Num(HeatFlux / 1e6) + Loc.T(" МВт/м² выше допустимого ") + Num(AllowedHeatFlux / 1e6) +
                               Loc.T(" МВт/м²") + (HasHeatShield ? Loc.T(" (термощит)") : Loc.T(" (корпус без термощита)")) + Loc.T(" — оборудование повреждается."));
                }
            }
            else if (HeatFlux < 0.5 * AllowedHeatFlux) overheatReported = false;
            return damage;
        }

        /// <summary>Leaves the atmosphere state as it is on the ground (no flux).</summary>
        public void ClearHeat() => HeatFlux = 0.0;

        static string Num(double v) => Academy.AcademyNumber.Format(v);
    }
}
