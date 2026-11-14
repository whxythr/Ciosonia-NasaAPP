using System;
using System.Collections.Generic;

namespace SpaceSim.Vehicles
{
    /// <summary>
    /// Career data of the catalog: the technology tier and the price of every module. Data-driven and kept next to the catalog
    /// (never in the UI): an explicit <see cref="ModuleDefinition.techTier"/> / <see cref="ModuleDefinition.price"/> in a definition wins;
    /// otherwise the tier comes from <see cref="TierTable"/>, and for modules missing there (new parts added later) it is derived
    /// from the module's kind; the price is derived from mass, thrust/capacity, category and tier (<see cref="DerivePrice"/>).
    /// Sandbox ignores all of it (everything is available, nothing is bought).
    /// </summary>
    public static partial class ModuleCatalog
    {
        /// <summary>Highest career technology tier (tiers are 0..MaxTechTier).</summary>
        public const int MaxTechTier = 4;

        /// <summary>
        /// Modules a new career starts with (owned, free): exactly the parts of <see cref="RocketBlueprint.CreateDefault"/>, so the
        /// starting rocket on the pad is always buildable.
        /// </summary>
        public static readonly string[] CareerStarterKit = { ProbeCore, EngineSokol, EngineVega, TankK100, TankK40 };

        /// <summary>
        /// Tier of each built-in module. 0 = basic rocketry (available from the start), 1 = suborbital, 2 = orbital,
        /// 3 = interplanetary, 4 = deep space. The six former shop upgrades keep their old milestone gates (tier = milestone).
        /// </summary>
        static readonly Dictionary<string, int> TierTable = new Dictionary<string, int>
        {
            // Tier 0 — basic rocketry
            { ProbeCore, 0 }, { ProbeSmall, 0 }, { EngineSokol, 0 }, { EngineVega, 0 }, { EngineIskra, 0 }, { EngineSolidSmall, 0 },
            { EngineSolidMedium, 0 }, { TankK100, 0 }, { TankK140, 0 }, { TankK40, 0 }, { TankK10, 0 }, { TankN2, 0 },
            { FinSideSmall, 0 }, { FinSideLarge, 0 }, { ParachuteSmall, 0 }, { ParachuteMedium, 0 }, { Thermometer, 0 },
            { Barometer, 0 }, { AntennaOmni, 0 }, { BatterySmall, 0 }, { SolarSmall, 0 }, { NavComputer, 0 }, { LightSmall, 0 },
            { CollectorBasic, 0 }, { Cargo2, 0 },
            // Tier 1 — suborbital (milestone: reach space)
            { CabinCrew1, 1 }, { EngineSokolM, 1 }, { EngineSolidLarge, 1 }, { FinTopSmall, 1 }, { RcsSmall, 1 }, { MeteoKit, 1 },
            { Spectrometer, 1 }, { Camera, 1 }, { Dosimeter, 1 }, { Magnetometer, 1 }, { RadiationShield, 1 }, { LegsLight, 1 },
            { HeatShieldLight, 1 }, { SolarMedium, 1 }, { DishSmall, 1 }, { LightLarge, 1 }, { ParachuteLarge, 1 }, { Cargo12, 1 },
            // Tier 2 — orbital (milestone: first orbit)
            { CabinCrew3, 2 }, { EngineHydra, 2 }, { TankH20, 2 }, { FinTopLarge, 2 }, { RcsLarge, 2 }, { Gravimeter, 2 },
            { Seismometer, 2 }, { AcidCoating, 2 }, { CollectorAdvanced, 2 }, { LegsHeavy, 2 }, { HeatShieldHeavy, 2 },
            { BatteryLarge, 2 }, { StarTracker, 2 }, { DishLarge, 2 }, { Relay, 2 }, { WheelSmall, 2 }, { RoverChassis, 2 },
            { RoverDrive, 2 }, { DrillSmall, 2 }, { Manipulator, 2 }, { Cargo40, 2 },
            { TemperatureRadar, 2 }, { PressureRadar, 2 }, { TerrainScanner, 2 }, // orbital mapping needs an orbit first
            // Tier 3 — interplanetary (milestone: escape the home planet)
            { EngineNuclearSmall, 3 }, { TankHydrogen8, 3 }, { EngineHall, 3 }, { EngineIon, 3 }, { TankXenon1, 3 }, { TankXenon8, 3 },
            { RadiationShieldHeavy, 3 }, { Rtg, 3 }, { SolarLarge, 3 }, { WheelLarge, 3 }, { DrillLarge, 3 }, { CargoPressurized20, 3 },
            // Tier 4 — deep space (milestone: first interplanetary flight)
            { CabinCrew7, 4 }, { EngineNuclearLarge, 4 }, { TankHydrogen25, 4 }, { EngineMagnetoplasma, 4 }, { Reactor, 4 },
        };

        /// <summary>Price factor per tier: newer technology costs more.</summary>
        static readonly double[] TierPriceFactor = { 1.0, 1.5, 2.0, 2.5, 3.0 };

        /// <summary>Career tier of a module (0 when unknown).</summary>
        public static int TierOf(string id)
        {
            ModuleDefinition m = Find(id);
            return m != null ? Math.Max(0, m.techTier) : 0;
        }

        /// <summary>True if the module is part of the free starting kit of a career.</summary>
        public static bool IsStarterModule(string id) => Array.IndexOf(CareerStarterKit, id) >= 0;

        static void ApplyCareerData(List<ModuleDefinition> modules)
        {
            foreach (ModuleDefinition m in modules)
            {
                if (m.techTier < 0) m.techTier = TierTable.TryGetValue(m.id, out int t) ? t : DeriveTier(m);
                if (m.techTier > MaxTechTier) m.techTier = MaxTechTier;
                if (m.price <= 0) m.price = DerivePrice(m);
            }
        }

        /// <summary>Tier for a module that is not in <see cref="TierTable"/>: by its technology and size.</summary>
        public static int DeriveTier(ModuleDefinition m)
        {
            switch (m.category)
            {
                case ModuleCategory.Engine:
                    switch (m.engineKind)
                    {
                        case EngineKind.Nuclear: return m.dryMass > 10000.0 ? 4 : 3;
                        case EngineKind.Electric: return m.vacuumThrust > 2.0 ? 4 : 3;
                        case EngineKind.Solid: return m.dryMass > 8000.0 ? 1 : 0;
                        default: return m.propellant == PropellantType.Hydrolox ? 2 : 0;
                    }
                case ModuleCategory.FuelTank:
                    if (m.propellant == PropellantType.LiquidHydrogen || m.propellant == PropellantType.Xenon) return 3;
                    return m.propellant == PropellantType.Hydrolox ? 2 : 0;
                case ModuleCategory.Cabin:
                    return m.cabinKind == CabinKind.Crewed ? Math.Min(MaxTechTier, 1 + m.crewCapacity / 3) : 0;
                case ModuleCategory.Science:
                    return m.dryMass > 20.0 ? 2 : 1;
                case ModuleCategory.Mechanics:
                case ModuleCategory.Communication:
                    return 2;
                case ModuleCategory.Auxiliary:
                    if (m.powerSource == PowerSourceKind.Steady) return 3;
                    return m.radiationShielding > 1 ? 3 : 1;
                default:
                    return m.dryMass > 1000.0 ? 2 : 1;
            }
        }

        /// <summary>
        /// Career price [credits] from the module's data: a base per category plus what makes it valuable (thrust, propellant capacity,
        /// seats, mass), times the tier factor; rounded to 50, at least 100.
        /// </summary>
        public static int DerivePrice(ModuleDefinition m)
        {
            double basePrice;
            switch (m.category)
            {
                case ModuleCategory.Cabin:
                    basePrice = m.cabinKind == CabinKind.Crewed ? 2500.0 * Math.Max(1, m.crewCapacity) + m.dryMass : 1500.0 + 2.0 * m.dryMass;
                    break;
                case ModuleCategory.Engine:
                    switch (m.engineKind)
                    {
                        case EngineKind.Solid: basePrice = 400.0 + m.vacuumThrust / 1000.0 + 0.2 * m.dryMass; break;
                        case EngineKind.Nuclear: basePrice = 6000.0 + 20.0 * m.vacuumThrust / 1000.0 + 0.5 * m.dryMass; break;
                        case EngineKind.Electric: basePrice = 2000.0 + 1000.0 * m.vacuumThrust + 5.0 * m.dryMass; break;
                        default: basePrice = 800.0 + 2.0 * m.vacuumThrust / 1000.0 + 0.3 * m.dryMass; break;
                    }
                    break;
                case ModuleCategory.FuelTank:
                    basePrice = 200.0 + 0.02 * m.propellantCapacity + 0.2 * m.dryMass;
                    break;
                case ModuleCategory.Science:
                    basePrice = 600.0 + 60.0 * Math.Sqrt(Math.Max(0.0, m.dryMass));
                    break;
                default:
                    basePrice = 300.0 + 60.0 * Math.Sqrt(Math.Max(0.0, m.dryMass));
                    if (m.powerSource == PowerSourceKind.Steady) basePrice *= 3.0;
                    break;
            }
            int tier = Math.Max(0, Math.Min(MaxTechTier, m.techTier));
            double price = basePrice * TierPriceFactor[tier];
            return Math.Max(100, (int)(Math.Round(price / 50.0) * 50.0));
        }
    }
}
