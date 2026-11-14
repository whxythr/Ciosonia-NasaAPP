using SpaceSim.Localization;
using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Units;
using SpaceSim.Vehicles;

namespace SpaceSim.Flight
{
    /// <summary>One rocket stage (SI units).</summary>
    [Serializable]
    public class RocketStage
    {
        public string name = "Stage";
        /// <summary>Structure + engines without propellant [kg].</summary>
        public double dryMass = 20000.0;
        /// <summary>Usable propellant [kg].</summary>
        public double propellantMass = 200000.0;
        /// <summary>Thrust in vacuum at full throttle [N].</summary>
        public double vacuumThrust = 4.0e6;
        /// <summary>Specific impulse in vacuum [s].</summary>
        public double vacuumIsp = 330.0;
        /// <summary>Specific impulse at the planet's surface pressure [s] (nozzle back-pressure loss).</summary>
        public double seaLevelIsp = 295.0;
        /// <summary>Visual length of the stage [m].</summary>
        public double length = 30.0;
        /// <summary>How the stage's engines work. Default: throttleable liquid engines.</summary>
        public EngineKind engineKind = EngineKind.LiquidChemical;
        /// <summary>Electric thrusters only: power all the engines of the stage draw at <see cref="vacuumThrust"/> [W].</summary>
        public double electricPowerDemand;

        /// <summary>A solid motor: full thrust from ignition to depletion, no throttle, no shut-down.</summary>
        public bool IsSolid => engineKind == EngineKind.Solid;
        /// <summary>Thrust is limited by the electric power on board.</summary>
        public bool IsElectric => engineKind == EngineKind.Electric;

        public RocketStage Clone() => (RocketStage)MemberwiseClone();
    }

    /// <summary>A stacked multi-stage rocket. Stage 0 burns first.</summary>
    [Serializable]
    public class RocketConfiguration
    {
        public RocketStage[] stages =
        {
            new RocketStage { name = Loc.T("Ступень 1"), dryMass = 22000, propellantMass = 300000, vacuumThrust = 5.2e6, vacuumIsp = 330, seaLevelIsp = 295, length = 36 },
            new RocketStage { name = Loc.T("Ступень 2"), dryMass = 4000, propellantMass = 40000, vacuumThrust = 7.0e5, vacuumIsp = 350, seaLevelIsp = 240, length = 12 }
        };
        /// <summary>Payload on top (the probe) [kg].</summary>
        public double payloadMass = 1000.0;
        /// <summary>Radius of the stack [m] (visual and reference area).</summary>
        public double radius = 1.85;
        public double dragCoefficient = 0.3;

        // Electric system of the payload section (it stays with the vehicle through every staging).
        /// <summary>Solar panels at the home planet's distance from the star [W].</summary>
        public double solarPower;
        /// <summary>Generators that do not depend on the star: RTGs, reactors, fuel cells [W].</summary>
        public double steadyPower;
        /// <summary>Consumption of all systems except the thrusters [W].</summary>
        public double systemsPower;
        /// <summary>Greatest discharge of the batteries [W].</summary>
        public double batteryPower;
        /// <summary>Energy in the full batteries [J].</summary>
        public double batteryEnergy;

        public double ReferenceArea => Math.PI * radius * radius;

        /// <summary>
        /// Electric balance while <paramref name="stage"/> is the one attached, with <paramref name="storedEnergy"/> [J] in the batteries and
        /// the solar flux at <paramref name="solarFluxRatio"/> of the home planet's. The one source of the thrust limit for flight and analysis.
        /// </summary>
        public PowerBalance ElectricBalance(RocketStage stage, double storedEnergy, double solarFluxRatio = 1.0) =>
            PartPhysics.ElectricBalance(solarPower * solarFluxRatio + steadyPower, systemsPower, stage != null ? stage.electricPowerDemand : 0.0, batteryPower, storedEnergy);

        /// <summary>
        /// How much of <paramref name="propellant"/> [kg] a stage burns at full command and in how many seconds, taking the battery energy
        /// that needs from <paramref name="storedEnergy"/> [J]. Everything, except for electric thrusters: they run on generators plus batteries
        /// until the batteries are flat, then on the generators alone; with neither left the burn stops and the rest of the propellant stays
        /// in the tanks. A weak supply only makes the burn longer — Δv does not depend on thrust.
        /// </summary>
        public double BurnablePropellant(RocketStage stage, double propellant, ref double storedEnergy, out double burnTime, double solarFluxRatio = 1.0)
        {
            burnTime = 0.0;
            if (stage == null || !(stage.vacuumIsp > 0.0) || !(stage.vacuumThrust > 0.0)) return propellant;
            double fullFlow = RocketEquations.MassFlow(stage.vacuumThrust, stage.vacuumIsp);
            if (!stage.IsElectric || !(stage.electricPowerDemand > 0.0))
            {
                burnTime = RocketEquations.BurnTime(propellant, fullFlow);
                return propellant;
            }

            PowerBalance balance = ElectricBalance(stage, storedEnergy, solarFluxRatio);
            double fraction = balance.EngineFraction;
            if (fraction <= 0.0) return 0.0;
            double massFlow = fraction * fullFlow;
            double batteryDraw = fraction * stage.electricPowerDemand - balance.NetGeneration;
            if (batteryDraw <= 0.0 || propellant / massFlow <= storedEnergy / batteryDraw)
            {
                burnTime = propellant / massFlow;
                if (batteryDraw > 0.0) storedEnergy = Math.Max(0.0, storedEnergy - batteryDraw * burnTime);
                return propellant;
            }

            // The batteries run flat first.
            double onBatteries = storedEnergy / batteryDraw;
            double burned = massFlow * onBatteries;
            storedEnergy = 0.0;
            burnTime = onBatteries;
            double sustained = Math.Min(1.0, Math.Max(0.0, balance.NetGeneration) / stage.electricPowerDemand);
            if (sustained <= 0.0) return burned;
            burnTime += (propellant - burned) / (sustained * fullFlow);
            return propellant;
        }

        public RocketConfiguration Clone()
        {
            var c = (RocketConfiguration)MemberwiseClone();
            c.stages = new RocketStage[stages.Length];
            for (int i = 0; i < stages.Length; i++) c.stages[i] = stages[i].Clone();
            return c;
        }
    }

    /// <summary>
    /// Live state of a rocket: remaining propellant per stage, engine, throttle and attitude. The player sets
    /// throttle and attitude; nothing here steers towards an orbit.
    ///
    /// Engine model: constant mass flow ṁ = throttle·F_vac/(I_sp,vac·g₀); thrust F = ṁ·g₀·I_sp(p) with
    /// I_sp(p) = I_sp,vac + (I_sp,SL − I_sp,vac)·p/p₀ — so thrust grows as the air thins.
    /// Solid motors ignore the throttle and cannot be shut down before their grain is gone. Electric thrusters get
    /// min(throttle, share of the full thrust the electric power sustains).
    /// Attitude: pitch from the local vertical (0° = straight up, 90° = horizontal) and heading from north
    /// (90° = east), relative to the local horizon of the current position.
    /// </summary>
    public sealed class RocketState
    {
        readonly RocketConfiguration config;

        public RocketState(RocketConfiguration configuration)
        {
            config = (configuration ?? new RocketConfiguration()).Clone();
            Propellant = new double[config.stages.Length];
            for (int i = 0; i < Propellant.Length; i++) Propellant[i] = Math.Max(0.0, config.stages[i].propellantMass);
            HeadingDegrees = 90.0;
            throttle = 1.0;
            BatteryEnergy = Math.Max(0.0, config.batteryEnergy);
        }

        public RocketConfiguration Configuration => config;
        public double[] Propellant { get; }
        /// <summary>Index of the stage currently attached at the bottom; = stage count when only the payload is left.</summary>
        public int StageIndex { get; private set; }

        bool engineOn;
        /// <summary>The engine is commanded on. Switching a burning solid motor off is ignored: it runs until the grain is gone (see <see cref="Extinguish"/>).</summary>
        public bool EngineOn
        {
            get => engineOn;
            set
            {
                if (!value && IsSolidBurning) return;
                engineOn = value;
            }
        }

        /// <summary>A solid motor of the current stage has been lit and still has grain.</summary>
        public bool IsSolidBurning => engineOn && HasStages && config.stages[StageIndex].IsSolid && Propellant[StageIndex] > 0.0;
        /// <summary>The player can throttle and stop the current stage (false for a solid motor).</summary>
        public bool CanThrottle => !HasStages || !config.stages[StageIndex].IsSolid;

        /// <summary>Stops any engine at once, whatever its kind: the vehicle is gone (crash, destruction) — not something a pilot can do.</summary>
        public void Extinguish() => engineOn = false;

        double throttle;
        /// <summary>Commanded throttle 0..1. While a solid motor is the current stage this reads 1 and writes are ignored.</summary>
        public double Throttle
        {
            get => CanThrottle ? throttle : 1.0;
            set
            {
                if (CanThrottle) throttle = MathD.Clamp01(value);
            }
        }

        double pitch;
        public double PitchDegrees
        {
            get => pitch;
            set => pitch = MathD.Clamp(value, 0.0, 180.0);
        }

        double heading;
        public double HeadingDegrees
        {
            get => heading;
            set => heading = ((value % 360.0) + 360.0) % 360.0;
        }

        public int StageCount => config.stages.Length;
        public bool HasStages => StageIndex < config.stages.Length;
        public RocketStage CurrentStage => HasStages ? config.stages[StageIndex] : null;
        public double CurrentPropellant => HasStages ? Propellant[StageIndex] : 0.0;
        public bool CanBurn => HasStages && Propellant[StageIndex] > 0.0 && config.stages[StageIndex].vacuumThrust > 0.0 && PowerFraction > 0.0;
        public bool IsBurning => EngineOn && CanBurn && Throttle > 0.0;

        // ---- Electric power ----------------------------------------------------------------------------

        /// <summary>Energy left in the batteries [J].</summary>
        public double BatteryEnergy { get; private set; }

        double solarFlux = 1.0;
        /// <summary>Solar flux relative to the home planet (<see cref="PartPhysics.SolarFluxRatio"/>; 1 until the flight sets it).</summary>
        public double SolarFluxRatio
        {
            get => solarFlux;
            set => solarFlux = Math.Max(0.0, value);
        }

        /// <summary>Electric balance of the current stage now.</summary>
        public PowerBalance Electrics => config.ElectricBalance(CurrentStage, BatteryEnergy, solarFlux);

        /// <summary>Share of the full thrust the electric power sustains: 1 for engines that need none.</summary>
        public double PowerFraction => HasStages && config.stages[StageIndex].IsElectric ? Electrics.EngineFraction : 1.0;

        /// <summary>Throttle the engine really runs at: the command, limited by the electric power.</summary>
        double EffectiveThrottle => Math.Min(Throttle, PowerFraction);

        /// <summary>
        /// Advances the batteries by dt seconds: a deficit discharges them, a surplus charges them up to their capacity. Done by
        /// <see cref="Burn"/> during a burn of an electric stage; call it for the rest of the flight to count the batteries there too.
        /// </summary>
        public void UpdateBatteries(double dt)
        {
            if (dt <= 0.0) return;
            PowerBalance b = Electrics;
            double used = IsBurning && config.stages[StageIndex].IsElectric ? EffectiveThrottle * config.stages[StageIndex].electricPowerDemand : 0.0;
            BatteryEnergy = MathD.Clamp(BatteryEnergy + (b.NetGeneration - used) * dt, 0.0, Math.Max(0.0, config.batteryEnergy));
        }

        /// <summary>Current total mass [kg].</summary>
        public double Mass
        {
            get
            {
                double m = config.payloadMass;
                for (int i = StageIndex; i < config.stages.Length; i++) m += config.stages[i].dryMass + Propellant[i];
                return m;
            }
        }

        /// <summary>Propellant mass flow of the current stage at the current throttle [kg/s].</summary>
        public double MassFlow
        {
            get
            {
                if (!IsBurning) return 0.0;
                RocketStage s = CurrentStage;
                return EffectiveThrottle * RocketEquations.MassFlow(s.vacuumThrust, s.vacuumIsp);
            }
        }

        /// <summary>Mass flow at full throttle of the current stage, whether or not the engine is running [kg/s].</summary>
        public double FullThrottleMassFlow => HasStages && CurrentStage.vacuumIsp > 0.0
            ? PowerFraction * RocketEquations.MassFlow(CurrentStage.vacuumThrust, CurrentStage.vacuumIsp)
            : 0.0;

        /// <summary>
        /// Thrust-to-weight ratio of the current stage at full throttle, at ambient pressure ratio p/p₀ and local gravity g —
        /// meaningful also while the engine is off (what the rocket COULD do).
        /// </summary>
        public double AvailableThrustToWeight(double pressureRatio, double localGravity) =>
            RocketEquations.ThrustToWeight(RocketEquations.ThrustFromMassFlow(FullThrottleMassFlow, Isp(pressureRatio)), Mass, localGravity);

        /// <summary>Specific impulse at ambient pressure ratio p/p₀ (0 = vacuum) [s].</summary>
        public double Isp(double pressureRatio)
        {
            RocketStage s = CurrentStage;
            if (s == null) return 0.0;
            return Math.Max(1.0, s.vacuumIsp + (s.seaLevelIsp - s.vacuumIsp) * MathD.Clamp01(pressureRatio));
        }

        /// <summary>Thrust [N] at ambient pressure ratio p/p₀.</summary>
        public double Thrust(double pressureRatio) => RocketEquations.ThrustFromMassFlow(MassFlow, Isp(pressureRatio));

        /// <summary>Burns propellant for dt seconds. Flames out (engine off) when the stage is empty.</summary>
        public void Burn(double dt)
        {
            double mdot = MassFlow;
            if (HasStages && config.stages[StageIndex].IsElectric) UpdateBatteries(dt);
            if (mdot <= 0.0) return;
            double left = Propellant[StageIndex] - mdot * dt;
            // Treat a rounding residue (< 1 mg) as empty, otherwise the engine would stay "on" for one more tick.
            if (left < 1e-6) left = 0.0;
            Propellant[StageIndex] = left;
            if (left <= 0.0) engineOn = false;
        }

        /// <summary>
        /// Takes up to <paramref name="mass"/> [kg] of liquid propellant for the RCS from the lowest attached stage that has some (a solid
        /// grain cannot feed thrusters). Returns how much was taken; less means the tanks are dry. A stage emptied this way flames out.
        /// </summary>
        public double DrawRcsPropellant(double mass)
        {
            if (!(mass > 0.0)) return 0.0;
            double taken = 0.0;
            for (int i = StageIndex; i < config.stages.Length && taken < mass; i++)
            {
                if (config.stages[i].IsSolid || Propellant[i] <= 0.0) continue;
                double d = Math.Min(Propellant[i], mass - taken);
                Propellant[i] -= d;
                if (Propellant[i] < 1e-6) Propellant[i] = 0.0;
                taken += d;
                if (i == StageIndex && Propellant[i] <= 0.0) engineOn = false;
            }
            return taken;
        }

        /// <summary>Liquid propellant the RCS could still draw from the attached stages [kg].</summary>
        public double RcsPropellantAvailable
        {
            get
            {
                double sum = 0.0;
                for (int i = StageIndex; i < config.stages.Length; i++)
                    if (!config.stages[i].IsSolid) sum += Propellant[i];
                return sum;
            }
        }

        /// <summary>Length of the stages still attached plus the payload section [m] (Level A, for the moment of inertia).</summary>
        public double StackLength
        {
            get
            {
                double l = PayloadLength;
                for (int i = StageIndex; i < config.stages.Length; i++) l += Math.Max(0.0, config.stages[i].length);
                return l;
            }
        }

        /// <summary>Assumed length of the payload section [m].</summary>
        public const double PayloadLength = 3.0;

        /// <summary>Drops the current stage. Returns false if only the payload is left.</summary>
        public bool Separate()
        {
            if (!HasStages) return false;
            StageIndex++;
            return true;
        }

        /// <summary>Remaining ideal (vacuum) Δv over all remaining stages, Tsiolkovsky: Σ I_sp·g₀·ln(m₀/m₁) [m/s].</summary>
        public double DeltaVRemaining()
        {
            double dv = 0.0;
            double energy = BatteryEnergy;
            for (int i = StageIndex; i < config.stages.Length; i++)
            {
                double above = config.payloadMass;
                for (int j = i + 1; j < config.stages.Length; j++) above += config.stages[j].dryMass + Propellant[j];
                double m0 = above + config.stages[i].dryMass + Propellant[i];
                double burnable = config.BurnablePropellant(config.stages[i], Propellant[i], ref energy, out _, solarFlux);
                double m1 = above + config.stages[i].dryMass + (Propellant[i] - burnable);
                if (config.stages[i].vacuumIsp > 0.0 && m1 > 0.0) dv += RocketEquations.DeltaV(config.stages[i].vacuumIsp, m0, m1);
            }
            return dv;
        }

        /// <summary>Thrust direction in the local frame (up, north, east components).</summary>
        public void LocalDirection(out double up, out double north, out double east)
        {
            double p = pitch * MathD.DegToRad, h = heading * MathD.DegToRad;
            up = Math.Cos(p);
            north = Math.Sin(p) * Math.Cos(h);
            east = Math.Sin(p) * Math.Sin(h);
        }
    }
}
