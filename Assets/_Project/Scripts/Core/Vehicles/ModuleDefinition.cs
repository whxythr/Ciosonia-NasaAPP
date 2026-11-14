using System;

namespace SpaceSim.Vehicles
{
    /// <summary>Top-level catalog tabs of the hangar.</summary>
    public enum ModuleCategory
    {
        Cabin,
        Engine,
        FuelTank,
        Aerodynamics,
        Science,
        Auxiliary,
        Communication,
        Mechanics,
        Cargo
    }

    /// <summary>Who controls the vehicle from a cabin.</summary>
    public enum CabinKind
    {
        /// <summary>Probe core: commanded remotely, no one on board.</summary>
        RemoteControl,
        /// <summary>Pressurised cabin with seats: <see cref="ModuleDefinition.crewCapacity"/> people on board.</summary>
        Crewed
    }

    /// <summary>Kinds of aerodynamic and attitude-control hardware.</summary>
    public enum AeroKind
    {
        /// <summary>Tail fins low on the stage: move the centre of pressure aft.</summary>
        SideFin,
        /// <summary>Grid fins high on the stage: control surfaces for a descent with the engines first.</summary>
        TopFin,
        /// <summary>Reaction control thrusters.</summary>
        Rcs
    }

    /// <summary>The one list of sub-tabs the catalog UI shows (see <see cref="ModuleDefinition.Subgroup"/>).</summary>
    public enum ModuleSubgroup
    {
        /// <summary>The category has no sub-tabs.</summary>
        None,
        RemoteControl,
        Crewed,
        LiquidEngine,
        SolidEngine,
        NuclearEngine,
        ElectricEngine,
        LiquidFuel,
        SideFin,
        TopFin,
        Rcs
    }

    /// <summary>Where a module is installed on the rocket.</summary>
    public enum ModuleMount
    {
        /// <summary>The stage's engine slot (<see cref="StageBlueprint.engineId"/>).</summary>
        StageEngine,
        /// <summary>The stage's tank stack (<see cref="StageBlueprint.tanks"/>).</summary>
        StageTank,
        /// <summary>Outside of a stage (<see cref="StageBlueprint.attachments"/>): leaves with that stage.</summary>
        StageSide,
        /// <summary>The payload section on top (<see cref="RocketBlueprint.payloadModules"/>): stays to the end.</summary>
        Payload
    }

    /// <summary>What an auxiliary module is (the category holds very different hardware).</summary>
    public enum AuxiliaryKind
    {
        None,
        Parachute,
        LandingLegs,
        /// <summary>Radiation shield or acid coating wrapped around the bus.</summary>
        Shield,
        HeatShield,
        Light,
        PowerSource
    }

    /// <summary>How a power source produces electricity.</summary>
    public enum PowerSourceKind
    {
        None,
        /// <summary>Output falls with the square of the distance from the star.</summary>
        Solar,
        /// <summary>RTG, reactor, fuel cells: constant output.</summary>
        Steady,
        /// <summary>Stores <see cref="ModuleDefinition.batteryEnergy"/>; <see cref="ModuleDefinition.powerOutput"/> is its maximum discharge.</summary>
        Battery
    }

    /// <summary>What a mechanics module is.</summary>
    public enum MechanismKind
    {
        None,
        Wheel,
        RoverChassis,
        RoverDrive,
        Drill,
        Manipulator,
        SampleCollector
    }

    /// <summary>Propellant. An engine can only burn propellant of its own type.</summary>
    public enum PropellantType
    {
        None,
        /// <summary>Kerosene + liquid oxygen: dense, moderate Isp.</summary>
        Kerolox,
        /// <summary>Liquid hydrogen + oxygen: highest chemical Isp, bulky heavy tanks.</summary>
        Hydrolox,
        /// <summary>Storable hypergolic: lower Isp, reliable restarts — landers and manoeuvring.</summary>
        Hypergolic,
        /// <summary>Cast grain inside a solid motor: there is no tank for it.</summary>
        Solid,
        /// <summary>Liquid hydrogen alone: the working fluid of a nuclear thermal engine.</summary>
        LiquidHydrogen,
        /// <summary>Xenon stored as a dense fluid: the propellant of electric thrusters.</summary>
        Xenon
    }

    /// <summary>How an engine produces thrust. Decides its physics (throttling, power demand) and its exhaust look.</summary>
    public enum EngineKind
    {
        /// <summary>Liquid propellant from the stage tanks: throttleable, restartable.</summary>
        LiquidChemical,
        /// <summary>Solid grain inside the engine itself: once lit it burns to depletion at full thrust.</summary>
        Solid,
        /// <summary>Nuclear thermal: a reactor heats liquid hydrogen. Very high Isp, heavy, modest thrust.</summary>
        Nuclear,
        /// <summary>Electric (Hall, ion, magnetoplasma): extreme Isp, tiny thrust, limited by the available electric power.</summary>
        Electric
    }

    /// <summary>What a module lets the vehicle do (beyond adding mass).</summary>
    [Flags]
    public enum ModuleCapability
    {
        None = 0,
        /// <summary>Predicts the current orbit (periapsis, apoapsis, period) — enables the orbit-analysis instrument.</summary>
        OrbitPrediction = 1,
        /// <summary>Star tracker: required for interplanetary navigation / assisted flight planning.</summary>
        InterplanetaryNavigation = 2,
        /// <summary>Measures and records atmosphere composition, pressure and temperature on site.</summary>
        AtmosphereAnalysis = 4,
        /// <summary>Spectrometer: identifies minerals, raises the research value of samples.</summary>
        SampleAnalysis = 8
    }

    /// <summary>What a science instrument measures.</summary>
    [Flags]
    public enum ScienceMeasurement
    {
        None = 0,
        Temperature = 1,
        Pressure = 2,
        Gravity = 4,
        Seismic = 8,
        Radiation = 16,
        MagneticField = 32,
        Imaging = 64,
        AtmosphereComposition = 128,
        Mineralogy = 256
    }

    /// <summary>Which kinds of samples a collector can take.</summary>
    [Flags]
    public enum SampleKinds
    {
        None = 0,
        Rock = 1,
        Soil = 2,
        Liquid = 4,
        Acid = 8,
        Rare = 16
    }

    /// <summary>
    /// One rocket component. All values SI (kg, N, s, m, W, J). Only the fields relevant to its category are used.
    /// Data, not behaviour: the physics comes from <see cref="SpaceSim.Flight.RocketEquations"/> and
    /// <see cref="SpaceSim.Physics.PartPhysics"/> via the compiler.
    /// </summary>
    [Serializable]
    public class ModuleDefinition
    {
        public string id = "";
        public string name = "";
        public ModuleCategory category;
        public ModuleMount mount = ModuleMount.Payload;
        public string description = "";
        /// <summary>Mass without propellant [kg].</summary>
        public double dryMass;
        /// <summary>Visual/structural length contribution [m].</summary>
        public double length;
        /// <summary>
        /// Visual size [m]: an engine's nozzle-exit diameter, the probe core's bus width. 0 = chosen by <see cref="RocketLayout"/>.
        /// Purely geometric (where the 3D model goes in the hangar and in flight) — no physics uses it.
        /// </summary>
        public double diameter;
        /// <summary>Price in credits (used by the shop, M3).</summary>
        public int price;
        /// <summary>Not available until bought in the shop.</summary>
        public bool requiresPurchase;
        /// <summary>
        /// Career technology tier 0..4 (see SpaceSim.Economy.TechTree). −1 = not set here: <see cref="ModuleCatalog"/> fills it
        /// from its career table or derives it from the module's data (ModuleCatalog.Career.cs).
        /// </summary>
        public int techTier = -1;

        // Cabin
        public CabinKind cabinKind;
        /// <summary>Seats [people]. 0 = remote control.</summary>
        public int crewCapacity;

        // Engine
        public EngineKind engineKind;
        public PropellantType propellant;
        public double vacuumThrust;
        public double vacuumIsp;
        public double seaLevelIsp;
        /// <summary>Solid motor: grain cast into the engine [kg].</summary>
        public double solidPropellantMass;
        /// <summary>Electric thruster: thrust efficiency η in P = F·g₀·Isp/(2·η), 0..1.</summary>
        public double electricEfficiency;

        // Fuel tank
        /// <summary>Maximum propellant mass the tank holds [kg].</summary>
        public double propellantCapacity;

        // Aerodynamics (one catalog item is a set for the whole stage)
        public AeroKind aeroKind;
        /// <summary>Planform area of the whole fin set [m²].</summary>
        public double finArea;
        /// <summary>RCS thrust of the whole block set along one direction [N].</summary>
        public double rcsThrust;

        // Auxiliary
        public AuxiliaryKind auxiliaryKind;
        /// <summary>Canopy reference area [m²].</summary>
        public double parachuteArea;
        /// <summary>Canopy drag coefficient (on <see cref="parachuteArea"/>).</summary>
        public double parachuteCd;
        /// <summary>Highest dynamic pressure the canopy survives while it is out [Pa]; above it the canopy tears.</summary>
        public double parachuteMaxDynamicPressure;
        /// <summary>Highest air speed of a deployed canopy [m/s]: faster it flutters apart, even in thin air.</summary>
        public double parachuteMaxSpeed;
        /// <summary>Legs in the set [pieces].</summary>
        public int legCount;
        /// <summary>Highest vertical speed the legs absorb without damage [m/s].</summary>
        public double legTouchdownSpeed;
        /// <summary>Heat shield: highest heat flux it withstands [W/m²].</summary>
        public double heatFluxLimit;
        /// <summary>Light: luminous flux [lm].</summary>
        public double lightOutput;

        // Electric power
        public PowerSourceKind powerSource;
        /// <summary>Electric output [W]: generation at the home planet's distance from the star, or a battery's maximum discharge.</summary>
        public double powerOutput;
        /// <summary>Electric consumption of the module's own systems [W] (thrusters are computed from their thrust instead).</summary>
        public double powerDraw;
        /// <summary>Stored electric energy [J].</summary>
        public double batteryEnergy;

        // Communication
        /// <summary>Largest distance the antenna reaches at <see cref="antennaPower"/> [m].</summary>
        public double antennaRange;
        /// <summary>Transmitter power [W].</summary>
        public double antennaPower;
        /// <summary>Passes on the signal of other vehicles.</summary>
        public bool relay;

        // Mechanics
        public MechanismKind mechanismKind;
        /// <summary>Greatest weight one wheel carries [N].</summary>
        public double wheelLoad;
        /// <summary>Greatest speed of the wheel [m/s].</summary>
        public double wheelMaxSpeed;
        /// <summary>Greatest drilling depth [m] (the sample kinds are in <see cref="sampleKinds"/>).</summary>
        public double drillDepth;

        // Cargo
        /// <summary>Usable volume of a cargo bay [m³].</summary>
        public double cargoVolume;
        /// <summary>The bay holds air at cabin pressure.</summary>
        public bool pressurized;

        // Science and other payload-section modules
        public ModuleCapability capabilities;
        public ScienceMeasurement measurements;
        /// <summary>0 = none; higher = safer in stronger radiation (see M6 environment hazards).</summary>
        public int radiationShielding;
        /// <summary>0 = none; higher = resists stronger corrosive (acidic) atmospheres and surfaces.</summary>
        public int acidResistance;
        /// <summary>Sample storage [kg].</summary>
        public double sampleCapacity;
        public SampleKinds sampleKinds;
        /// <summary>Multiplier on the research value of samples analysed with this instrument (1 = none).</summary>
        public double researchMultiplier = 1.0;

        public bool IsCabin => category == ModuleCategory.Cabin;
        public bool IsEngine => category == ModuleCategory.Engine;
        public bool IsTank => category == ModuleCategory.FuelTank;
        public bool IsPayload => mount == ModuleMount.Payload;
        public bool IsStageAttachment => mount == ModuleMount.StageSide;
        /// <summary>Wraps the bus as a shell (radiation shield, acid coating).</summary>
        public bool IsWrap => auxiliaryKind == AuxiliaryKind.Shield;
        public bool IsSampleCollector => mechanismKind == MechanismKind.SampleCollector;

        /// <summary>
        /// The sub-tab of its category: the single switch the catalog UI uses (<see cref="ModuleSubgroup.None"/> = no sub-tabs).
        /// <see cref="ModuleCatalog.SubgroupsOf"/> gives the tabs of a category in display order.
        /// </summary>
        public ModuleSubgroup Subgroup
        {
            get
            {
                switch (category)
                {
                    case ModuleCategory.Cabin:
                        return cabinKind == CabinKind.Crewed ? ModuleSubgroup.Crewed : ModuleSubgroup.RemoteControl;
                    case ModuleCategory.Engine:
                        switch (engineKind)
                        {
                            case EngineKind.Solid: return ModuleSubgroup.SolidEngine;
                            case EngineKind.Nuclear: return ModuleSubgroup.NuclearEngine;
                            case EngineKind.Electric: return ModuleSubgroup.ElectricEngine;
                            default: return ModuleSubgroup.LiquidEngine;
                        }
                    case ModuleCategory.FuelTank:
                        return ModuleSubgroup.LiquidFuel;
                    case ModuleCategory.Aerodynamics:
                        switch (aeroKind)
                        {
                            case AeroKind.TopFin: return ModuleSubgroup.TopFin;
                            case AeroKind.Rcs: return ModuleSubgroup.Rcs;
                            default: return ModuleSubgroup.SideFin;
                        }
                    default:
                        return ModuleSubgroup.None;
                }
            }
        }

        public ModuleDefinition Clone() => (ModuleDefinition)MemberwiseClone();
    }
}
