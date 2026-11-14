using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Physics;

namespace SpaceSim.Vehicles
{
    public enum BlueprintIssueCode
    {
        FuelCapacityExceeded,
        IncompatiblePropellant,
        TooManyEngines,
        VacuumEngineOnFirstStage,
        UnknownModule,
        /// <summary>No cabin of any kind: a probe core was added.</summary>
        MissingCore,
        LockedModule,
        NoStages,
        /// <summary>A module is installed where it cannot go (a fin in the payload section, an engine on the side…) and is skipped.</summary>
        WrongMount,
        /// <summary>An electric stage has no electric power at all: its thrust is zero.</summary>
        NoElectricPower,
        /// <summary>An electric stage gets less power than full thrust needs: its thrust is proportionally lower.</summary>
        InsufficientElectricPower,
        /// <summary>The systems draw more than the generators give: they live on batteries, or go dark without them.</summary>
        PowerDeficit,
        /// <summary>Rover parts are on board, but not a complete rover.</summary>
        RoverIncomplete
    }

    /// <summary>A construction problem and its physical consequence (designs are never refused outright).</summary>
    public readonly struct BlueprintIssue
    {
        public BlueprintIssue(BlueprintIssueCode code, int stageIndex, string message)
        {
            Code = code;
            StageIndex = stageIndex;
            Message = message;
        }

        public BlueprintIssueCode Code { get; }
        /// <summary>Stage index, or −1 for the payload section.</summary>
        public int StageIndex { get; }
        public string Message { get; }
    }

    /// <summary>One canopy: its C_d·A [m²] and the loads it survives (0 = not limited).</summary>
    public readonly struct ParachuteSpec
    {
        public ParachuteSpec(string moduleId, double dragArea, double maxDynamicPressure, double maxSpeed)
        {
            ModuleId = moduleId;
            DragArea = dragArea;
            MaxDynamicPressure = maxDynamicPressure;
            MaxSpeed = maxSpeed;
        }

        public string ModuleId { get; }
        public double DragArea { get; }
        public double MaxDynamicPressure { get; }
        public double MaxSpeed { get; }
    }

    /// <summary>What is mounted on one stage's side (or, summed, on the stages still attached).</summary>
    public sealed class StageHardware
    {
        /// <summary>Planform area of all fin sets [m²].</summary>
        public double FinArea;
        public double SideFinArea, TopFinArea;
        /// <summary>RCS thrust along one direction [N].</summary>
        public double RcsThrust;
        public int LegCount;
        /// <summary>Weakest leg's limit for a soft touchdown [m/s] (0 = no legs).</summary>
        public double LegTouchdownSpeed;

        internal void Add(StageHardware h)
        {
            FinArea += h.FinArea;
            SideFinArea += h.SideFinArea;
            TopFinArea += h.TopFinArea;
            RcsThrust += h.RcsThrust;
            AddLegs(h.LegCount, h.LegTouchdownSpeed);
        }

        internal void AddLegs(int count, double touchdownSpeed)
        {
            if (count <= 0) return;
            LegTouchdownSpeed = LegCount > 0 ? Math.Min(LegTouchdownSpeed, touchdownSpeed) : touchdownSpeed;
            LegCount += count;
        }
    }

    /// <summary>
    /// Aggregated non-propulsive abilities of a vehicle. Stage-side hardware counts the whole stack at launch;
    /// use <see cref="HardwareFrom"/> for what is left after staging.
    /// </summary>
    public sealed class VehicleCapabilities
    {
        public ModuleCapability Flags;
        public int RadiationShielding;
        public int AcidResistance;
        public double SampleCapacity;
        public SampleKinds SampleKinds;
        public double ResearchMultiplier = 1.0;

        // Control
        /// <summary>Seats on board [people].</summary>
        public int CrewCapacity;
        public int RemoteCabins, CrewedCabins;

        // Aerodynamics and landing (whole stack)
        /// <summary>Hardware of each stage, same order as the stages.</summary>
        public readonly List<StageHardware> Stages = new List<StageHardware>();
        /// <summary>Planform area of all fin sets [m²].</summary>
        public double FinArea => HardwareFrom(0).FinArea;
        public double SideFinArea => HardwareFrom(0).SideFinArea;
        public double TopFinArea => HardwareFrom(0).TopFinArea;
        /// <summary>RCS thrust along one direction [N].</summary>
        public double RcsThrust => HardwareFrom(0).RcsThrust;
        public int LegCount => HardwareFrom(0).LegCount;
        /// <summary>Weakest leg's limit for a soft touchdown [m/s] (0 = no legs).</summary>
        public double LegTouchdownSpeed => HardwareFrom(0).LegTouchdownSpeed;

        // Recovery
        public int ParachuteCount;
        /// <summary>Sum of the canopy areas [m²].</summary>
        public double ParachuteArea;
        /// <summary>Sum of C_d·A of the canopies [m²].</summary>
        public double ParachuteDragArea;
        /// <summary>Best heat shield's limit [W/m²] (0 = no shield).</summary>
        public double HeatShieldFluxLimit;
        /// <summary>Every canopy on board, for the flight (each one inflates and tears on its own).</summary>
        public readonly List<ParachuteSpec> Parachutes = new List<ParachuteSpec>();

        // Lights and communication
        /// <summary>Total luminous flux [lm].</summary>
        public double LightOutput;
        /// <summary>Reach of all antennas together [m].</summary>
        public double CommRange;
        /// <summary>Total transmitter power [W].</summary>
        public double AntennaPower;
        public bool HasRelay;

        // Electric power
        /// <summary>Solar panels at the home planet's distance [W].</summary>
        public double SolarPower;
        /// <summary>RTGs, reactors and fuel cells [W].</summary>
        public double SteadyPower;
        /// <summary>All systems except the thrusters [W].</summary>
        public double PowerConsumption;
        /// <summary>Greatest battery discharge [W].</summary>
        public double BatteryPower;
        /// <summary>Energy in the full batteries [J].</summary>
        public double BatteryEnergy;
        /// <summary>The largest thruster demand of any stage [W].</summary>
        public double EnginePowerDemand;

        // Rover and drills
        public int WheelCount;
        /// <summary>Weight all the wheels carry together [N].</summary>
        public double WheelLoad;
        /// <summary>Speed of the slowest wheel [m/s] (0 = no wheels).</summary>
        public double WheelMaxSpeed;
        public bool HasRoverChassis, HasRoverDrive;
        public int DrillCount;
        /// <summary>Deepest drill [m].</summary>
        public double DrillDepth;
        public SampleKinds DrillKinds;
        public int ManipulatorCount;

        // Cargo and science
        /// <summary>All cargo bays [m³].</summary>
        public double CargoVolume;
        /// <summary>Of that, the bays held at cabin pressure [m³].</summary>
        public double PressurizedVolume;
        public ScienceMeasurement Measurements;

        public bool Has(ModuleCapability c) => (Flags & c) == c;
        public bool CanCollect(SampleKinds k) => (SampleKinds & k) == k;
        public bool CanMeasure(ScienceMeasurement m) => (Measurements & m) == m;

        public bool HasCabin => RemoteCabins + CrewedCabins > 0;
        /// <summary>The kind of control: crewed when anyone can be on board.</summary>
        public CabinKind ControlKind => CrewedCabins > 0 ? CabinKind.Crewed : CabinKind.RemoteControl;
        public double GeneratedPower => SolarPower + SteadyPower;
        /// <summary>Generators plus batteries against systems and the largest thruster demand.</summary>
        public PowerBalance Electrics => PartPhysics.ElectricBalance(GeneratedPower, PowerConsumption, EnginePowerDemand, BatteryPower, BatteryEnergy);
        /// <summary>Chassis, drive and at least <see cref="PartPhysics.MinRoverWheels"/> wheels are on board.</summary>
        public bool RoverAssembled => HasRoverChassis && HasRoverDrive && WheelCount >= PartPhysics.MinRoverWheels;
        /// <summary>A complete rover, and its wheels carry <paramref name="mass"/> [kg] at local gravity.</summary>
        public bool RoverCanDrive(double mass, double gravity) => RoverAssembled && PartPhysics.WheelsCarry(WheelLoad, mass, gravity);

        /// <summary>Side hardware still attached when stage <paramref name="stageIndex"/> is the bottom one (the rest has been dropped).</summary>
        public StageHardware HardwareFrom(int stageIndex)
        {
            var sum = new StageHardware();
            for (int i = Math.Max(0, stageIndex); i < Stages.Count; i++) sum.Add(Stages[i]);
            return sum;
        }
    }

    /// <summary>Result of compiling a blueprint: the physics configuration plus capabilities and issues.</summary>
    public sealed class CompiledRocket
    {
        public RocketConfiguration Configuration;
        public VehicleCapabilities Capabilities = new VehicleCapabilities();
        public readonly List<BlueprintIssue> Issues = new List<BlueprintIssue>();
        /// <summary>Sum of the tank capacities and solid grain of all stages [kg].</summary>
        public double MaxFuelCapacity;
        /// <summary>Propellant the engines can actually burn [kg].</summary>
        public double UsableFuel;
        /// <summary>Propellant loaded into tanks no engine of that stage can burn — carried as dead weight [kg].</summary>
        public double UnusableFuel;
        public double PayloadMass;
    }

    /// <summary>
    /// Turns a <see cref="RocketBlueprint"/> into the <see cref="RocketConfiguration"/> the flight physics uses.
    /// Mass, thrust and Isp come straight from module data; performance figures are then computed by
    /// <see cref="RocketDesignAnalysis"/> (the same Level A code the flight uses).
    /// </summary>
    public static class BlueprintCompiler
    {
        public const int MaxEnginesPerStage = 9;
        /// <summary>Isp_sea/Isp_vac below this marks a vacuum-optimised engine.</summary>
        const double VacuumEngineRatio = 0.75;

        /// <param name="isUnlocked">Ownership check for purchasable modules (M3 shop). Null = everything available.</param>
        public static CompiledRocket Compile(RocketBlueprint blueprint, Func<string, bool> isUnlocked = null)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
            var result = new CompiledRocket();
            var config = new RocketConfiguration { stages = new RocketStage[blueprint.stages.Count] };

            if (blueprint.stages.Count == 0)
                result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.NoStages, -1, Loc.T("Нет ни одной ступени: аппарат не сможет взлететь.")));

            for (int i = 0; i < blueprint.stages.Count; i++)
                config.stages[i] = CompileStage(blueprint.stages[i], i, result, isUnlocked);

            // ---- payload section ----
            bool hasCabin = false;
            double payload = 0.0;
            VehicleCapabilities caps = result.Capabilities;
            foreach (string id in blueprint.payloadModules)
            {
                ModuleDefinition m = Resolve(id, -1, result, isUnlocked);
                if (m == null) continue;
                if (!m.IsPayload)
                {
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.WrongMount, -1, m.name + Loc.T(" нельзя ставить в отсек полезной нагрузки.")));
                    continue;
                }
                hasCabin |= m.IsCabin;
                payload += m.dryMass;
                AddPayloadCapabilities(m, caps);
            }
            if (!hasCabin)
            {
                ModuleDefinition core = ModuleCatalog.Find(ModuleCatalog.ProbeCore);
                payload += core.dryMass;
                AddPayloadCapabilities(core, caps);
                result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.MissingCore, -1, Loc.T("Нет кабины — добавлен зонд с дистанционным управлением.")));
            }
            config.payloadMass = payload;
            result.PayloadMass = payload;

            config.solarPower = caps.SolarPower;
            config.steadyPower = caps.SteadyPower;
            config.systemsPower = caps.PowerConsumption;
            config.batteryPower = caps.BatteryPower;
            config.batteryEnergy = caps.BatteryEnergy;
            CheckElectricPower(config, result);
            CheckRover(caps, result);

            result.Configuration = config;
            return result;
        }

        static RocketStage CompileStage(StageBlueprint sb, int index, CompiledRocket result, Func<string, bool> isUnlocked)
        {
            var stage = new RocketStage { name = string.IsNullOrEmpty(sb.name) ? Loc.T("Ступень ") + (index + 1) : sb.name, dryMass = 0, propellantMass = 0,
                vacuumThrust = 0, vacuumIsp = 0, seaLevelIsp = 0, length = 0 };

            ModuleDefinition engine = string.IsNullOrEmpty(sb.engineId) ? null : Resolve(sb.engineId, index, result, isUnlocked);
            if (engine != null && !engine.IsEngine)
            {
                result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.UnknownModule, index, engine.name + Loc.T(" — не двигатель.")));
                engine = null;
            }
            if (engine != null)
            {
                int count = Math.Max(1, sb.engineCount);
                if (count > MaxEnginesPerStage)
                {
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.TooManyEngines, index,
                        stage.name + Loc.T(": помещается не более ") + MaxEnginesPerStage + Loc.T(" двигателей — установлено ") + MaxEnginesPerStage + "."));
                    count = MaxEnginesPerStage;
                }
                stage.dryMass += engine.dryMass * count;
                stage.vacuumThrust = engine.vacuumThrust * count;
                stage.vacuumIsp = engine.vacuumIsp;
                stage.seaLevelIsp = engine.seaLevelIsp;
                stage.length += engine.length;
                stage.engineKind = engine.engineKind;
                if (engine.engineKind == EngineKind.Solid)
                {
                    // The grain is cast into the motor: it counts as this stage's propellant, no tank needed.
                    double grain = engine.solidPropellantMass * count;
                    stage.propellantMass += grain;
                    result.UsableFuel += grain;
                    result.MaxFuelCapacity += grain;
                }
                if (engine.engineKind == EngineKind.Electric)
                {
                    stage.electricPowerDemand = RocketEquations.ElectricPower(engine.vacuumThrust, engine.vacuumIsp, engine.electricEfficiency) * count;
                    result.Capabilities.EnginePowerDemand = Math.Max(result.Capabilities.EnginePowerDemand, stage.electricPowerDemand);
                }
                if (index == 0 && engine.vacuumIsp > 0.0 && engine.seaLevelIsp / engine.vacuumIsp < VacuumEngineRatio)
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.VacuumEngineOnFirstStage, index,
                        engine.name + Loc.T(" — вакуумный двигатель: у поверхности его Isp ") + engine.seaLevelIsp.ToString("0") + Loc.T(" с вместо ") +
                        engine.vacuumIsp.ToString("0") + Loc.T(" с, тяга на старте заметно ниже.")));
            }

            foreach (TankLoad t in sb.tanks)
            {
                ModuleDefinition tank = Resolve(t.moduleId, index, result, isUnlocked);
                if (tank == null) continue;
                if (!tank.IsTank)
                {
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.UnknownModule, index, tank.name + Loc.T(" — не топливный бак.")));
                    continue;
                }
                double loaded = Math.Max(0.0, t.propellantLoaded);
                if (loaded > tank.propellantCapacity)
                {
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.FuelCapacityExceeded, index,
                        tank.name + Loc.T(": запрошено ") + (loaded / 1000.0).ToString("0.0") + Loc.T(" т, вмещается ") + (tank.propellantCapacity / 1000.0).ToString("0.0") +
                        Loc.T(" т — залито только то, что помещается.")));
                    loaded = tank.propellantCapacity;
                }
                result.MaxFuelCapacity += tank.propellantCapacity;
                stage.dryMass += tank.dryMass;
                stage.length += tank.length;
                if (engine != null && tank.propellant != engine.propellant)
                {
                    stage.dryMass += loaded; // carried, but nothing can burn it
                    result.UnusableFuel += loaded;
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.IncompatiblePropellant, index,
                        tank.name + " (" + Name(tank.propellant) + Loc.T(") не подходит двигателю ") + engine.name + " (" + Name(engine.propellant) +
                        "): " + (loaded / 1000.0).ToString("0.0") + Loc.T(" т топлива — мёртвый груз.")));
                }
                else
                {
                    stage.propellantMass += loaded;
                    result.UsableFuel += loaded;
                }
            }

            var hardware = new StageHardware();
            if (sb.attachments != null)
            {
                foreach (string id in sb.attachments)
                {
                    ModuleDefinition m = Resolve(id, index, result, isUnlocked);
                    if (m == null) continue;
                    if (!m.IsStageAttachment)
                    {
                        result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.WrongMount, index, m.name + Loc.T(" нельзя крепить на борт ступени.")));
                        continue;
                    }
                    stage.dryMass += m.dryMass;
                    AddStageHardware(m, hardware);
                }
            }
            result.Capabilities.Stages.Add(hardware);

            stage.length = Math.Max(stage.length, 1.0);
            return stage;
        }

        static void AddStageHardware(ModuleDefinition m, StageHardware h)
        {
            h.FinArea += m.finArea;
            if (m.category == ModuleCategory.Aerodynamics && m.aeroKind == AeroKind.SideFin) h.SideFinArea += m.finArea;
            if (m.category == ModuleCategory.Aerodynamics && m.aeroKind == AeroKind.TopFin) h.TopFinArea += m.finArea;
            h.RcsThrust += m.rcsThrust;
            h.AddLegs(m.legCount, m.legTouchdownSpeed);
        }

        static void AddPayloadCapabilities(ModuleDefinition m, VehicleCapabilities caps)
        {
            caps.Flags |= m.capabilities;
            caps.Measurements |= m.measurements;
            caps.RadiationShielding = Math.Max(caps.RadiationShielding, m.radiationShielding);
            caps.AcidResistance = Math.Max(caps.AcidResistance, m.acidResistance);
            caps.SampleCapacity += m.sampleCapacity;
            caps.SampleKinds |= m.sampleKinds;
            caps.ResearchMultiplier = Math.Max(caps.ResearchMultiplier, m.researchMultiplier);

            if (m.IsCabin)
            {
                if (m.cabinKind == CabinKind.Crewed) caps.CrewedCabins++;
                else caps.RemoteCabins++;
                caps.CrewCapacity += m.crewCapacity;
            }

            if (m.parachuteArea > 0.0)
            {
                caps.ParachuteCount++;
                caps.ParachuteArea += m.parachuteArea;
                caps.ParachuteDragArea += m.parachuteCd * m.parachuteArea;
                caps.Parachutes.Add(new ParachuteSpec(m.id, m.parachuteCd * m.parachuteArea, m.parachuteMaxDynamicPressure, m.parachuteMaxSpeed));
            }
            caps.HeatShieldFluxLimit = Math.Max(caps.HeatShieldFluxLimit, m.heatFluxLimit);
            caps.LightOutput += m.lightOutput;

            if (m.antennaRange > 0.0) caps.CommRange = PartPhysics.CombineRange(caps.CommRange, m.antennaRange);
            caps.AntennaPower += m.antennaPower;
            caps.HasRelay |= m.relay;

            switch (m.powerSource)
            {
                case PowerSourceKind.Solar: caps.SolarPower += m.powerOutput; break;
                case PowerSourceKind.Steady: caps.SteadyPower += m.powerOutput; break;
                case PowerSourceKind.Battery: caps.BatteryPower += m.powerOutput; break;
            }
            caps.BatteryEnergy += m.batteryEnergy;
            caps.PowerConsumption += m.powerDraw;

            switch (m.mechanismKind)
            {
                case MechanismKind.Wheel:
                    caps.WheelMaxSpeed = caps.WheelCount > 0 ? Math.Min(caps.WheelMaxSpeed, m.wheelMaxSpeed) : m.wheelMaxSpeed;
                    caps.WheelCount++;
                    caps.WheelLoad += m.wheelLoad;
                    break;
                case MechanismKind.RoverChassis: caps.HasRoverChassis = true; break;
                case MechanismKind.RoverDrive: caps.HasRoverDrive = true; break;
                case MechanismKind.Drill:
                    caps.DrillCount++;
                    caps.DrillDepth = Math.Max(caps.DrillDepth, m.drillDepth);
                    caps.DrillKinds |= m.sampleKinds;
                    break;
                case MechanismKind.Manipulator: caps.ManipulatorCount++; break;
            }

            caps.CargoVolume += m.cargoVolume;
            if (m.pressurized) caps.PressurizedVolume += m.cargoVolume;
        }

        /// <summary>The electric supply of every thruster stage and of the systems, in numbers (at the home planet's distance, batteries full).</summary>
        static void CheckElectricPower(RocketConfiguration config, CompiledRocket result)
        {
            VehicleCapabilities caps = result.Capabilities;
            if (caps.PowerConsumption > caps.GeneratedPower)
            {
                string reserve = caps.BatteryEnergy > 0.0
                    ? Loc.T(" Батарей хватит примерно на ") + (caps.BatteryEnergy / Math.Max(1.0, caps.PowerConsumption - caps.GeneratedPower) / 3600.0).ToString("0.#") + Loc.T(" ч.")
                    : Loc.T(" Батарей нет: системы останутся без питания.");
                result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.PowerDeficit, -1,
                    Loc.T("Системы потребляют ") + caps.PowerConsumption.ToString("0") + Loc.T(" Вт, генераторы дают ") + caps.GeneratedPower.ToString("0") + Loc.T(" Вт.") + reserve));
            }

            for (int i = 0; i < config.stages.Length; i++)
            {
                RocketStage s = config.stages[i];
                if (!s.IsElectric || !(s.electricPowerDemand > 0.0)) continue;
                PowerBalance balance = config.ElectricBalance(s, config.batteryEnergy);
                string numbers = Loc.T("нужно ") + (s.electricPowerDemand / 1000.0).ToString("0.0") + Loc.T(" кВт, доступно ") + (balance.AvailableForEngines / 1000.0).ToString("0.0") + Loc.T(" кВт");
                if (balance.EngineFraction <= 0.0)
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.NoElectricPower, i,
                        s.name + Loc.T(": нет питания для электродвигателя (") + numbers + Loc.T(") — тяга равна нулю.")));
                else if (balance.EngineFraction < 1.0)
                    result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.InsufficientElectricPower, i,
                        s.name + Loc.T(": не хватает питания (") + numbers + Loc.T(") — тяга только ") + (balance.EngineFraction * 100.0).ToString("0") + Loc.T(" % от полной.")));
            }
        }

        static void CheckRover(VehicleCapabilities caps, CompiledRocket result)
        {
            bool anyPart = caps.WheelCount > 0 || caps.HasRoverChassis || caps.HasRoverDrive;
            if (anyPart && !caps.RoverAssembled)
                result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.RoverIncomplete, -1,
                    Loc.T("Ровер не соберётся: нужны шасси, привод и не меньше ") + PartPhysics.MinRoverWheels + Loc.T(" колёс (сейчас колёс: ") + caps.WheelCount + ")."));
        }

        static ModuleDefinition Resolve(string id, int stageIndex, CompiledRocket result, Func<string, bool> isUnlocked)
        {
            ModuleDefinition m = ModuleCatalog.Find(id);
            if (m == null)
            {
                result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.UnknownModule, stageIndex, Loc.T("Неизвестный модуль «") + id + Loc.T("» пропущен.")));
                return null;
            }
            if (m.requiresPurchase && isUnlocked != null && !isUnlocked(m.id))
            {
                result.Issues.Add(new BlueprintIssue(BlueprintIssueCode.LockedModule, stageIndex, m.name + Loc.T(" не куплен — не установлен.")));
                return null;
            }
            return m;
        }

        public static string Name(PropellantType p)
        {
            switch (p)
            {
                case PropellantType.Kerolox: return Loc.T("керосин + O₂");
                case PropellantType.Hydrolox: return Loc.T("водород + O₂");
                case PropellantType.Hypergolic: return Loc.T("гиперголь");
                case PropellantType.Solid: return Loc.T("твёрдое топливо");
                case PropellantType.LiquidHydrogen: return Loc.T("жидкий водород");
                case PropellantType.Xenon: return Loc.T("ксенон");
                default: return "—";
            }
        }
    }
}
