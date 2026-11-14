using SpaceSim.Localization;
using System.Collections.Generic;

namespace SpaceSim.Vehicles
{
    /// <summary>
    /// Built-in rocket components. Numbers are game-balanced but physically plausible (Isp, thrust and mass of real engine classes;
    /// tank dry fractions 4–15 %; real power densities of panels and RTGs). Modules marked requiresPurchase are sold in the shop (M3).
    /// Ids are saved in player designs: never change one.
    /// </summary>
    public static class ModuleCatalog
    {
        // Cabins
        public const string ProbeCore = "core-probe";
        public const string ProbeSmall = "cab-rc-small";
        public const string CabinCrew1 = "cab-crew-1";
        public const string CabinCrew3 = "cab-crew-3";
        public const string CabinCrew7 = "cab-crew-7";

        // Engines
        public const string EngineSokol = "eng-sokol-1";
        public const string EngineSokolM = "eng-sokol-1m";
        public const string EngineVega = "eng-vega-v";
        public const string EngineHydra = "eng-hydra";
        public const string EngineIskra = "eng-iskra";
        public const string EngineSolidSmall = "eng-solid-s";
        public const string EngineSolidMedium = "eng-solid-m";
        public const string EngineSolidLarge = "eng-solid-l";
        public const string EngineNuclearSmall = "eng-nuc-70";
        public const string EngineNuclearLarge = "eng-nuc-250";
        public const string EngineHall = "eng-el-hall";
        public const string EngineIon = "eng-el-ion";
        public const string EngineMagnetoplasma = "eng-el-mpd";

        // Tanks
        public const string TankK100 = "tank-k100";
        public const string TankK140 = "tank-k140";
        public const string TankK40 = "tank-k40";
        public const string TankK10 = "tank-k10";
        public const string TankH20 = "tank-h20";
        public const string TankN2 = "tank-n2";
        public const string TankHydrogen8 = "tank-lh8";
        public const string TankHydrogen25 = "tank-lh25";
        public const string TankXenon1 = "tank-xe1";
        public const string TankXenon8 = "tank-xe8";

        // Aerodynamics
        public const string FinSideSmall = "fin-side-s";
        public const string FinSideLarge = "fin-side-l";
        public const string FinTopSmall = "fin-top-s";
        public const string FinTopLarge = "fin-top-l";
        public const string RcsSmall = "rcs-100";
        public const string RcsLarge = "rcs-1800";

        // Science
        public const string Spectrometer = "sci-sp1";
        public const string MeteoKit = "sci-meteo";
        public const string Thermometer = "sci-therm";
        public const string Barometer = "sci-baro";
        public const string Gravimeter = "sci-grav";
        public const string Seismometer = "sci-seis";
        public const string Dosimeter = "sci-dosi";
        public const string Magnetometer = "sci-magn";
        public const string Camera = "sci-cam";

        // Auxiliary
        public const string RadiationShield = "prot-rad1";
        public const string RadiationShieldHeavy = "prot-rad2";
        public const string AcidCoating = "prot-acid1";
        public const string ParachuteSmall = "chute-10";
        public const string ParachuteMedium = "chute-25";
        public const string ParachuteLarge = "chute-45";
        public const string LegsLight = "leg-light";
        public const string LegsHeavy = "leg-heavy";
        public const string HeatShieldLight = "hs-light";
        public const string HeatShieldHeavy = "hs-heavy";
        public const string LightSmall = "light-s";
        public const string LightLarge = "light-l";
        public const string SolarSmall = "pwr-sol-2";
        public const string SolarMedium = "pwr-sol-20";
        public const string SolarLarge = "pwr-sol-200";
        public const string Rtg = "pwr-rtg";
        public const string Reactor = "pwr-reactor";
        public const string BatterySmall = "pwr-bat-10";
        public const string BatteryLarge = "pwr-bat-100";

        // Communication
        public const string NavComputer = "nav-nk1";
        public const string StarTracker = "nav-sd2";
        public const string AntennaOmni = "ant-omni";
        public const string DishSmall = "ant-dish-s";
        public const string DishLarge = "ant-dish-l";
        public const string Relay = "ant-relay";

        // Mechanics
        public const string CollectorBasic = "col-pb1";
        public const string CollectorAdvanced = "col-pb2";
        public const string WheelSmall = "wheel-s";
        public const string WheelLarge = "wheel-l";
        public const string RoverChassis = "rover-chassis";
        public const string RoverDrive = "rover-drive";
        public const string DrillSmall = "drill-s";
        public const string DrillLarge = "drill-l";
        public const string Manipulator = "arm-mn1";

        // Cargo
        public const string Cargo2 = "cargo-2";
        public const string Cargo12 = "cargo-12";
        public const string Cargo40 = "cargo-40";
        public const string CargoPressurized20 = "cargo-press-20";

        /// <summary>Hangar tabs in display order.</summary>
        public static readonly ModuleCategory[] Categories =
        {
            ModuleCategory.Cabin, ModuleCategory.Engine, ModuleCategory.FuelTank, ModuleCategory.Aerodynamics, ModuleCategory.Science,
            ModuleCategory.Auxiliary, ModuleCategory.Communication, ModuleCategory.Mechanics, ModuleCategory.Cargo
        };

        static readonly ModuleSubgroup[] NoSubgroups = new ModuleSubgroup[0];
        static readonly ModuleSubgroup[] CabinSubgroups = { ModuleSubgroup.RemoteControl, ModuleSubgroup.Crewed };
        static readonly ModuleSubgroup[] EngineSubgroups = { ModuleSubgroup.LiquidEngine, ModuleSubgroup.SolidEngine, ModuleSubgroup.NuclearEngine, ModuleSubgroup.ElectricEngine };
        static readonly ModuleSubgroup[] TankSubgroups = { ModuleSubgroup.LiquidFuel };
        static readonly ModuleSubgroup[] AeroSubgroups = { ModuleSubgroup.SideFin, ModuleSubgroup.TopFin, ModuleSubgroup.Rcs };

        static List<ModuleDefinition> all;
        static int allLanguage = -1;

        public static IReadOnlyList<ModuleDefinition> All
        {
            get
            {
                if (all == null || allLanguage != Loc.Version)
                {
                    all = Build(); // names and descriptions are translated when built
                    allLanguage = Loc.Version;
                }
                return all;
            }
        }

        public static ModuleDefinition Find(string id)
        {
            foreach (var m in All)
                if (m.id == id) return m;
            return null;
        }

        public static List<ModuleDefinition> InCategory(ModuleCategory c)
        {
            var list = new List<ModuleDefinition>();
            foreach (var m in All)
                if (m.category == c) list.Add(m);
            return list;
        }

        /// <summary>Sub-tabs of a category in display order; empty when the category has none.</summary>
        public static IReadOnlyList<ModuleSubgroup> SubgroupsOf(ModuleCategory c)
        {
            switch (c)
            {
                case ModuleCategory.Cabin: return CabinSubgroups;
                case ModuleCategory.Engine: return EngineSubgroups;
                case ModuleCategory.FuelTank: return TankSubgroups;
                case ModuleCategory.Aerodynamics: return AeroSubgroups;
                default: return NoSubgroups;
            }
        }

        public static List<ModuleDefinition> InSubgroup(ModuleCategory c, ModuleSubgroup subgroup)
        {
            var list = new List<ModuleDefinition>();
            foreach (var m in All)
                if (m.category == c && m.Subgroup == subgroup) list.Add(m);
            return list;
        }

        public static string CategoryName(ModuleCategory c)
        {
            switch (c)
            {
                case ModuleCategory.Cabin: return Loc.T("Кабины");
                case ModuleCategory.Engine: return Loc.T("Двигатели");
                case ModuleCategory.FuelTank: return Loc.T("Топливные баки");
                case ModuleCategory.Aerodynamics: return Loc.T("Аэродинамика");
                case ModuleCategory.Science: return Loc.T("Наука");
                case ModuleCategory.Auxiliary: return Loc.T("Вспомогательное оборудование");
                case ModuleCategory.Communication: return Loc.T("Коммуникация");
                case ModuleCategory.Mechanics: return Loc.T("Механика");
                case ModuleCategory.Cargo: return Loc.T("Грузовые модули");
                default: return c.ToString();
            }
        }

        public static string SubgroupName(ModuleSubgroup s)
        {
            switch (s)
            {
                case ModuleSubgroup.RemoteControl: return Loc.T("Дистанционного управления");
                case ModuleSubgroup.Crewed: return Loc.T("С экипажем");
                case ModuleSubgroup.LiquidEngine: return Loc.T("Жидкостные");
                case ModuleSubgroup.SolidEngine: return Loc.T("Твердотопливные");
                case ModuleSubgroup.NuclearEngine: return Loc.T("Ядерные");
                case ModuleSubgroup.ElectricEngine: return Loc.T("Электрические");
                case ModuleSubgroup.LiquidFuel: return Loc.T("Жидкое топливо");
                case ModuleSubgroup.SideFin: return Loc.T("Боковые стабилизаторы");
                case ModuleSubgroup.TopFin: return Loc.T("Верхние стабилизаторы");
                case ModuleSubgroup.Rcs: return Loc.T("РСУ");
                default: return "";
            }
        }

        const double KilowattHour = 3.6e6;

        static List<ModuleDefinition> Build() => new List<ModuleDefinition>
        {
            // ---- Cabins ----
            new ModuleDefinition { id = ProbeCore, name = Loc.T("Зонд (ядро)"), category = ModuleCategory.Cabin, cabinKind = CabinKind.RemoteControl,
                dryMass = 1000, length = 2.5, diameter = 1.8, powerSource = PowerSourceKind.Solar, powerOutput = 600, powerDraw = 150,
                antennaRange = 2e6, antennaPower = 1,
                description = Loc.T("Научный зонд: связь, питание, управление по радио. На борту нет людей.") },
            new ModuleDefinition { id = ProbeSmall, name = Loc.T("Зонд Ласточка"), category = ModuleCategory.Cabin, cabinKind = CabinKind.RemoteControl,
                dryMass = 150, length = 0.8, diameter = 0.9, powerSource = PowerSourceKind.Solar, powerOutput = 120, powerDraw = 40,
                antennaRange = 2e6, antennaPower = 1,
                description = Loc.T("Малый блок управления для лёгких аппаратов. Дёшев по массе, но для приборов и антенн почти нет запаса мощности.") },
            new ModuleDefinition { id = CabinCrew1, name = Loc.T("Капсула Чайка-1"), category = ModuleCategory.Cabin, cabinKind = CabinKind.Crewed, crewCapacity = 1,
                dryMass = 1800, length = 2.6, diameter = 2.2, powerSource = PowerSourceKind.Steady, powerOutput = 400, powerDraw = 300,
                antennaRange = 1e7, antennaPower = 5,
                description = Loc.T("Одноместная капсула: кресло, скафандр, система жизнеобеспечения и питание от топливных элементов.") },
            new ModuleDefinition { id = CabinCrew3, name = Loc.T("Капсула Чайка-3"), category = ModuleCategory.Cabin, cabinKind = CabinKind.Crewed, crewCapacity = 3,
                dryMass = 5800, length = 3.6, diameter = 3.4, powerSource = PowerSourceKind.Steady, powerOutput = 1200, powerDraw = 900,
                antennaRange = 1e7, antennaPower = 5,
                description = Loc.T("Трёхместная капсула для полётов к другим мирам: вес настоящих лунных кораблей.") },
            new ModuleDefinition { id = CabinCrew7, name = Loc.T("Кабина Альбатрос-7"), category = ModuleCategory.Cabin, cabinKind = CabinKind.Crewed, crewCapacity = 7,
                dryMass = 11000, length = 4.6, diameter = 3.6, powerSource = PowerSourceKind.Steady, powerOutput = 3000, powerDraw = 2200,
                antennaRange = 1e7, antennaPower = 5,
                description = Loc.T("Семиместная кабина: большой экипаж стоит многих тонн на всех ступенях.") },

            // ---- Engines: liquid ----
            new ModuleDefinition { id = EngineSokol, name = Loc.T("Сокол-1"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, propellant = PropellantType.Kerolox,
                dryMass = 2500, length = 3, diameter = 1.1, price = 0, vacuumThrust = 1.30e6, vacuumIsp = 330, seaLevelIsp = 295,
                description = Loc.T("Керосин-кислородный двигатель первой ступени. Хорошая тяга у поверхности.") },
            new ModuleDefinition { id = EngineSokolM, name = Loc.T("Сокол-1М"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, propellant = PropellantType.Kerolox,
                dryMass = 2400, length = 3, diameter = 1.15, price = 9000, requiresPurchase = true, vacuumThrust = 1.50e6, vacuumIsp = 338, seaLevelIsp = 305,
                description = Loc.T("Модернизированный Сокол: +15 % тяги, выше Isp, легче.") },
            new ModuleDefinition { id = EngineVega, name = Loc.T("Вега-В"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, propellant = PropellantType.Kerolox,
                dryMass = 1600, length = 2, diameter = 2.4, price = 0, vacuumThrust = 7.0e5, vacuumIsp = 350, seaLevelIsp = 240,
                description = Loc.T("Вакуумный двигатель верхней ступени: большое сопло, у поверхности тяга сильно падает.") },
            new ModuleDefinition { id = EngineHydra, name = Loc.T("Гидра"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, propellant = PropellantType.Hydrolox,
                dryMass = 300, length = 2.5, diameter = 1.4, price = 0, vacuumThrust = 1.1e5, vacuumIsp = 450, seaLevelIsp = 280,
                description = Loc.T("Водородный вакуумный двигатель: лучший Isp, но малая тяга — долгие разгоны.") },
            new ModuleDefinition { id = EngineIskra, name = Loc.T("Искра"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, propellant = PropellantType.Hypergolic,
                dryMass = 100, length = 1, diameter = 0.5, price = 0, vacuumThrust = 3.0e4, vacuumIsp = 315, seaLevelIsp = 250,
                description = Loc.T("Маленький двигатель на самовоспламеняющемся топливе: манёвры и посадка.") },

            // ---- Engines: solid (the grain is inside, the stage needs no tanks) ----
            new ModuleDefinition { id = EngineSolidSmall, name = Loc.T("Стриж-Т"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, engineKind = EngineKind.Solid, propellant = PropellantType.Solid,
                dryMass = 140, length = 1.8, diameter = 0.75, vacuumThrust = 6.8e4, vacuumIsp = 292, seaLevelIsp = 250, solidPropellantMass = 2010,
                description = Loc.T("Малый твердотопливный двигатель, 2 т заряда внутри. После зажигания не выключается и не дросселируется: горит около 85 с на полной тяге.") },
            new ModuleDefinition { id = EngineSolidMedium, name = Loc.T("Кречет-Т"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, engineKind = EngineKind.Solid, propellant = PropellantType.Solid,
                dryMass = 4100, length = 9, diameter = 1.7, vacuumThrust = 1.65e6, vacuumIsp = 280, seaLevelIsp = 250, solidPropellantMass = 49000,
                description = Loc.T("Средний твердотопливный ускоритель, 49 т заряда. Просто и мощно, но управлять тягой нельзя: горит около 80 с.") },
            new ModuleDefinition { id = EngineSolidLarge, name = Loc.T("Беркут-Т"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, engineKind = EngineKind.Solid, propellant = PropellantType.Solid,
                dryMass = 11000, length = 13.5, diameter = 2, vacuumThrust = 4.5e6, vacuumIsp = 279, seaLevelIsp = 245, solidPropellantMass = 142000,
                description = Loc.T("Большой твердотопливный ускоритель первой ступени, 142 т заряда. Огромная стартовая тяга на 85 с — и никакого способа остановить её.") },

            // ---- Engines: nuclear thermal ----
            new ModuleDefinition { id = EngineNuclearSmall, name = Loc.T("Атом-70"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, engineKind = EngineKind.Nuclear, propellant = PropellantType.LiquidHydrogen,
                dryMass = 3500, length = 4, diameter = 1.3, vacuumThrust = 7.3e4, vacuumIsp = 850, seaLevelIsp = 360,
                description = Loc.T("Ядерный тепловой двигатель малой тяги: реактор нагревает жидкий водород. Isp 850 с в вакууме, у поверхности меньше половины.") },
            new ModuleDefinition { id = EngineNuclearLarge, name = Loc.T("Атом-250"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, engineKind = EngineKind.Nuclear, propellant = PropellantType.LiquidHydrogen,
                dryMass = 18000, length = 7, diameter = 2.5, vacuumThrust = 2.46e5, vacuumIsp = 850, seaLevelIsp = 380,
                description = Loc.T("Тяжёлый ядерный тепловой двигатель: 18 т реактора и защиты. Для больших межпланетных кораблей.") },

            // ---- Engines: electric (F·g₀·Isp/(2·η) of electric power) ----
            new ModuleDefinition { id = EngineHall, name = Loc.T("Луч-Х"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, engineKind = EngineKind.Electric, propellant = PropellantType.Xenon,
                dryMass = 110, length = 0.5, diameter = 0.45, vacuumThrust = 0.6, vacuumIsp = 2800, seaLevelIsp = 0, electricEfficiency = 0.6,
                description = Loc.T("Холловский двигатель на ксеноне: 0,6 Н при 13,7 кВт. Работает только в вакууме и только с питанием.") },
            new ModuleDefinition { id = EngineIon, name = Loc.T("Луч-И"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, engineKind = EngineKind.Electric, propellant = PropellantType.Xenon,
                dryMass = 100, length = 0.6, diameter = 0.6, vacuumThrust = 0.236, vacuumIsp = 4150, seaLevelIsp = 0, electricEfficiency = 0.7,
                description = Loc.T("Ионный двигатель: 0,24 Н при 6,9 кВт, Isp 4150 с. Разгоняется месяцами, зато экономит топливо лучше всех.") },
            new ModuleDefinition { id = EngineMagnetoplasma, name = Loc.T("Луч-М"), category = ModuleCategory.Engine, mount = ModuleMount.StageEngine, engineKind = EngineKind.Electric, propellant = PropellantType.Xenon,
                dryMass = 620, length = 2.2, diameter = 1, vacuumThrust = 5.7, vacuumIsp = 4900, seaLevelIsp = 0, electricEfficiency = 0.7,
                description = Loc.T("Магнитоплазменный двигатель: 5,7 Н при 196 кВт. Нужна большая солнечная батарея или реактор.") },

            // ---- Fuel tanks ----
            new ModuleDefinition { id = TankK100, name = Loc.T("Бак K-100"), category = ModuleCategory.FuelTank, mount = ModuleMount.StageTank, propellant = PropellantType.Kerolox,
                dryMass = 4000, length = 11, price = 0, propellantCapacity = 100000, description = Loc.T("Большой керосин-кислородный бак, 100 т.") },
            new ModuleDefinition { id = TankK140, name = Loc.T("Расширенный бак K-140"), category = ModuleCategory.FuelTank, mount = ModuleMount.StageTank, propellant = PropellantType.Kerolox,
                dryMass = 5200, length = 15, price = 6000, requiresPurchase = true, propellantCapacity = 140000,
                description = Loc.T("Расширенный бак: +40 % топлива при +30 % сухой массы. Больше Δv — но и тяжелее старт.") },
            new ModuleDefinition { id = TankK40, name = Loc.T("Бак K-40"), category = ModuleCategory.FuelTank, mount = ModuleMount.StageTank, propellant = PropellantType.Kerolox,
                dryMass = 2400, length = 10, price = 0, propellantCapacity = 40000, description = Loc.T("Средний керосин-кислородный бак, 40 т.") },
            new ModuleDefinition { id = TankK10, name = Loc.T("Бак K-10"), category = ModuleCategory.FuelTank, mount = ModuleMount.StageTank, propellant = PropellantType.Kerolox,
                dryMass = 800, length = 3, price = 0, propellantCapacity = 10000, description = Loc.T("Малый керосин-кислородный бак, 10 т.") },
            new ModuleDefinition { id = TankH20, name = Loc.T("Бак H-20"), category = ModuleCategory.FuelTank, mount = ModuleMount.StageTank, propellant = PropellantType.Hydrolox,
                dryMass = 2000, length = 12, price = 0, propellantCapacity = 20000,
                description = Loc.T("Водород-кислородный бак, 20 т. Водород лёгкий и объёмный — бак тяжёлый и длинный.") },
            new ModuleDefinition { id = TankN2, name = Loc.T("Бак N-2"), category = ModuleCategory.FuelTank, mount = ModuleMount.StageTank, propellant = PropellantType.Hypergolic,
                dryMass = 200, length = 1.5, price = 0, propellantCapacity = 2000, description = Loc.T("Малый бак самовоспламеняющегося топлива, 2 т.") },
            new ModuleDefinition { id = TankHydrogen8, name = Loc.T("Бак LH-8"), category = ModuleCategory.FuelTank, mount = ModuleMount.StageTank, propellant = PropellantType.LiquidHydrogen,
                dryMass = 1100, length = 11, propellantCapacity = 8000,
                description = Loc.T("Жидкий водород для ядерных двигателей, 8 т. Плотность водорода 71 кг/м³: бак огромный для своей массы.") },
            new ModuleDefinition { id = TankHydrogen25, name = Loc.T("Бак LH-25"), category = ModuleCategory.FuelTank, mount = ModuleMount.StageTank, propellant = PropellantType.LiquidHydrogen,
                dryMass = 3200, length = 33, propellantCapacity = 25000,
                description = Loc.T("Жидкий водород для ядерных двигателей, 25 т. Длиной почти с первую ступень обычной ракеты.") },
            new ModuleDefinition { id = TankXenon1, name = Loc.T("Бак Xe-1"), category = ModuleCategory.FuelTank, mount = ModuleMount.StageTank, propellant = PropellantType.Xenon,
                dryMass = 120, length = 1, propellantCapacity = 1000,
                description = Loc.T("Жидкий ксенон для электрических двигателей, 1 т. Плотный и компактный.") },
            new ModuleDefinition { id = TankXenon8, name = Loc.T("Бак Xe-8"), category = ModuleCategory.FuelTank, mount = ModuleMount.StageTank, propellant = PropellantType.Xenon,
                dryMass = 800, length = 2.5, propellantCapacity = 8000,
                description = Loc.T("Жидкий ксенон для электрических двигателей, 8 т: запас на дальний перелёт.") },

            // ---- Aerodynamics (one item is a set around the stage) ----
            new ModuleDefinition { id = FinSideSmall, name = Loc.T("Стабилизаторы КС-1"), category = ModuleCategory.Aerodynamics, mount = ModuleMount.StageSide, aeroKind = AeroKind.SideFin,
                dryMass = 60, length = 1.2, finArea = 1.6, description = Loc.T("Четыре боковых киля, 1,6 м² в сумме: смещают центр давления назад на лёгкой ракете.") },
            new ModuleDefinition { id = FinSideLarge, name = Loc.T("Стабилизаторы КС-2"), category = ModuleCategory.Aerodynamics, mount = ModuleMount.StageSide, aeroKind = AeroKind.SideFin,
                dryMass = 320, length = 2.5, finArea = 6, description = Loc.T("Четыре больших боковых киля, 6 м² в сумме: для тяжёлых ступеней.") },
            new ModuleDefinition { id = FinTopSmall, name = Loc.T("Решётчатые рули РР-1"), category = ModuleCategory.Aerodynamics, mount = ModuleMount.StageSide, aeroKind = AeroKind.TopFin,
                dryMass = 90, length = 0.8, finArea = 1.2, description = Loc.T("Четыре решётчатых руля наверху ступени, 1,2 м²: управление на спуске двигателями вперёд.") },
            new ModuleDefinition { id = FinTopLarge, name = Loc.T("Решётчатые рули РР-2"), category = ModuleCategory.Aerodynamics, mount = ModuleMount.StageSide, aeroKind = AeroKind.TopFin,
                dryMass = 900, length = 1.5, finArea = 6.4, description = Loc.T("Четыре больших титановых решётчатых руля, 6,4 м²: возвращаемая первая ступень.") },
            new ModuleDefinition { id = RcsSmall, name = Loc.T("Блок РСУ-100"), category = ModuleCategory.Aerodynamics, mount = ModuleMount.StageSide, aeroKind = AeroKind.Rcs,
                dryMass = 16, length = 0.3, rcsThrust = 100, description = Loc.T("Набор малых двигателей ориентации: 100 Н по каждой оси. Для зондов и лёгких кораблей.") },
            new ModuleDefinition { id = RcsLarge, name = Loc.T("Блок РСУ-1800"), category = ModuleCategory.Aerodynamics, mount = ModuleMount.StageSide, aeroKind = AeroKind.Rcs,
                dryMass = 90, length = 0.5, rcsThrust = 1800, description = Loc.T("Набор мощных двигателей ориентации: 1800 Н по каждой оси. Для тяжёлых кораблей и стыковки.") },

            // ---- Science ----
            new ModuleDefinition { id = Spectrometer, name = Loc.T("Спектрометр СП-1"), category = ModuleCategory.Science, dryMass = 80, length = 0.3, price = 0, powerDraw = 40,
                capabilities = ModuleCapability.SampleAnalysis, measurements = ScienceMeasurement.Mineralogy, researchMultiplier = 1.5,
                description = Loc.T("Определяет минералы на месте: научная ценность образцов ×1.5.") },
            new ModuleDefinition { id = MeteoKit, name = Loc.T("Метеокомплекс МЕТЕО"), category = ModuleCategory.Science, dryMass = 25, length = 0.1, price = 0, powerDraw = 10,
                capabilities = ModuleCapability.AtmosphereAnalysis, measurements = ScienceMeasurement.Temperature | ScienceMeasurement.Pressure | ScienceMeasurement.AtmosphereComposition,
                description = Loc.T("Давление, температура и состав атмосферы в точке посадки.") },
            new ModuleDefinition { id = Thermometer, name = Loc.T("Термометр ТМ-1"), category = ModuleCategory.Science, dryMass = 2, length = 0.1, powerDraw = 2, measurements = ScienceMeasurement.Temperature,
                description = Loc.T("Температура среды за бортом.") },
            new ModuleDefinition { id = Barometer, name = Loc.T("Барометр БМ-1"), category = ModuleCategory.Science, dryMass = 2, length = 0.1, powerDraw = 2, measurements = ScienceMeasurement.Pressure,
                description = Loc.T("Давление атмосферы.") },
            new ModuleDefinition { id = Gravimeter, name = Loc.T("Гравиметр ГР-1"), category = ModuleCategory.Science, dryMass = 30, length = 0.4, powerDraw = 15, measurements = ScienceMeasurement.Gravity,
                description = Loc.T("Точное измерение ускорения свободного падения: внутреннее строение тела.") },
            new ModuleDefinition { id = Seismometer, name = Loc.T("Сейсмометр СМ-1"), category = ModuleCategory.Science, dryMass = 25, length = 0.3, powerDraw = 8, measurements = ScienceMeasurement.Seismic,
                description = Loc.T("Слушает толчки недр. Работает только на поверхности.") },
            new ModuleDefinition { id = Dosimeter, name = Loc.T("Дозиметр ДЗ-1"), category = ModuleCategory.Science, dryMass = 5, length = 0.1, powerDraw = 4, measurements = ScienceMeasurement.Radiation,
                description = Loc.T("Измеряет дозу излучения на борту и за бортом.") },
            new ModuleDefinition { id = Magnetometer, name = Loc.T("Магнитометр МГ-1"), category = ModuleCategory.Science, dryMass = 5, length = 0.8, powerDraw = 6, measurements = ScienceMeasurement.MagneticField,
                description = Loc.T("Магнитное поле тела. Датчик вынесен на штанге, подальше от электроники.") },
            new ModuleDefinition { id = Camera, name = Loc.T("Камера КМ-1"), category = ModuleCategory.Science, dryMass = 20, length = 0.4, powerDraw = 20, measurements = ScienceMeasurement.Imaging,
                description = Loc.T("Съёмка поверхности и неба высокого разрешения.") },

            // ---- Auxiliary: protection ----
            new ModuleDefinition { id = RadiationShield, name = Loc.T("Радиационная защита Р-1"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.Shield, dryMass = 450, length = 0.4,
                price = 7000, requiresPurchase = true, radiationShielding = 1,
                description = Loc.T("Экранирует оборудование и экипаж: открывает исследование районов с сильным излучением.") },
            new ModuleDefinition { id = RadiationShieldHeavy, name = Loc.T("Радиационная защита Р-2"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.Shield, dryMass = 900, length = 0.6,
                price = 14000, requiresPurchase = true, radiationShielding = 2,
                description = Loc.T("Тяжёлая защита уровня 2: работа в самых сильных полях излучения (Теонан). Вдвое тяжелее Р-1.") },
            new ModuleDefinition { id = AcidCoating, name = Loc.T("Кислотостойкое покрытие К-1"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.Shield, dryMass = 300, length = 0.1,
                price = 6500, requiresPurchase = true, acidResistance = 1,
                description = Loc.T("Защищает от коррозии: нужно для посадки в кислотной атмосфере (Гермеус Мора).") },

            // ---- Auxiliary: parachutes ----
            new ModuleDefinition { id = ParachuteSmall, name = Loc.T("Парашют ПС-10"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.Parachute, dryMass = 15, length = 0.3,
                parachuteArea = 78.5, parachuteCd = 0.8, parachuteMaxDynamicPressure = 6000, parachuteMaxSpeed = 350, description = Loc.T("Купол 10 м для малых зондов. Бесполезен там, где нет атмосферы.") },
            new ModuleDefinition { id = ParachuteMedium, name = Loc.T("Парашют ПС-25"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.Parachute, dryMass = 120, length = 0.6,
                parachuteArea = 490, parachuteCd = 0.8, parachuteMaxDynamicPressure = 4000, parachuteMaxSpeed = 250, description = Loc.T("Купол 25 м для капсул на несколько тонн.") },
            new ModuleDefinition { id = ParachuteLarge, name = Loc.T("Парашют ПС-45"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.Parachute, dryMass = 400, length = 1,
                parachuteArea = 1590, parachuteCd = 0.8, parachuteMaxDynamicPressure = 2500, parachuteMaxSpeed = 180, description = Loc.T("Купол 45 м для тяжёлых кабин и ступеней.") },

            // ---- Auxiliary: landing ----
            new ModuleDefinition { id = LegsLight, name = Loc.T("Посадочные опоры ОП-1"), category = ModuleCategory.Auxiliary, mount = ModuleMount.StageSide, auxiliaryKind = AuxiliaryKind.LandingLegs, dryMass = 80, length = 1.2,
                legCount = 4, legTouchdownSpeed = 3, description = Loc.T("Четыре лёгкие опоры, мягкая посадка до 3 м/с. Крепятся к посадочной ступени.") },
            new ModuleDefinition { id = LegsHeavy, name = Loc.T("Посадочные опоры ОП-2"), category = ModuleCategory.Auxiliary, mount = ModuleMount.StageSide, auxiliaryKind = AuxiliaryKind.LandingLegs, dryMass = 2000, length = 4,
                legCount = 4, legTouchdownSpeed = 6, description = Loc.T("Четыре тяжёлые опоры для посадки целой ступени, мягкая посадка до 6 м/с.") },
            new ModuleDefinition { id = HeatShieldLight, name = Loc.T("Термощит ТЩ-1"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.HeatShield, dryMass = 200, length = 0.2,
                heatFluxLimit = 1.5e6, description = Loc.T("Абляционный щит на 1,5 МВт/м²: возвращение с низкой орбиты.") },
            new ModuleDefinition { id = HeatShieldHeavy, name = Loc.T("Термощит ТЩ-5"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.HeatShield, dryMass = 850, length = 0.4,
                heatFluxLimit = 5e6, description = Loc.T("Абляционный щит на 5 МВт/м²: вход в атмосферу на межпланетной скорости.") },

            // ---- Auxiliary: lights ----
            new ModuleDefinition { id = LightSmall, name = Loc.T("Фонарь ФН-1"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.Light, dryMass = 1.5, length = 0.1,
                lightOutput = 1500, powerDraw = 15, description = Loc.T("Светодиодный фонарь, 1500 лм.") },
            new ModuleDefinition { id = LightLarge, name = Loc.T("Прожектор ПЖ-20"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.Light, dryMass = 8, length = 0.3,
                lightOutput = 20000, powerDraw = 200, description = Loc.T("Мощный прожектор, 20000 лм: ночная посадка и работа в тени.") },

            // ---- Auxiliary: electric power ----
            new ModuleDefinition { id = SolarSmall, name = Loc.T("Солнечная панель СБ-2"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.PowerSource, powerSource = PowerSourceKind.Solar,
                dryMass = 25, length = 0.3, powerOutput = 2000, description = Loc.T("Даёт 2 кВт у родной планеты. Дальше от звезды мощность падает как квадрат расстояния.") },
            new ModuleDefinition { id = SolarMedium, name = Loc.T("Солнечная панель СБ-20"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.PowerSource, powerSource = PowerSourceKind.Solar,
                dryMass = 220, length = 0.8, powerOutput = 20000, description = Loc.T("Большая складная батарея, 20 кВт: хватит на холловский двигатель.") },
            new ModuleDefinition { id = SolarLarge, name = Loc.T("Солнечная панель СБ-200"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.PowerSource, powerSource = PowerSourceKind.Solar,
                dryMass = 2000, length = 2, powerOutput = 200000, description = Loc.T("Огромная батарея площадью 700 м², 200 кВт: для магнитоплазменного двигателя.") },
            new ModuleDefinition { id = Rtg, name = Loc.T("РИТЭГ-300"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.PowerSource, powerSource = PowerSourceKind.Steady,
                dryMass = 57, length = 1.1, powerOutput = 300, description = Loc.T("Радиоизотопный генератор: 300 Вт в любом месте системы, но для электродвигателей слишком мало.") },
            new ModuleDefinition { id = Reactor, name = Loc.T("Реактор РК-40"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.PowerSource, powerSource = PowerSourceKind.Steady,
                dryMass = 2500, length = 3.5, powerOutput = 40000, description = Loc.T("Компактный ядерный реактор с радиаторами, 40 кВт: питание вдали от звезды.") },
            new ModuleDefinition { id = BatterySmall, name = Loc.T("Батарея АБ-10"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.PowerSource, powerSource = PowerSourceKind.Battery,
                dryMass = 65, length = 0.3, powerOutput = 10000, batteryEnergy = 10 * KilowattHour, description = Loc.T("Литий-ионная батарея: 10 кВт·ч, разряд до 10 кВт.") },
            new ModuleDefinition { id = BatteryLarge, name = Loc.T("Батарея АБ-100"), category = ModuleCategory.Auxiliary, auxiliaryKind = AuxiliaryKind.PowerSource, powerSource = PowerSourceKind.Battery,
                dryMass = 600, length = 0.8, powerOutput = 100000, batteryEnergy = 100 * KilowattHour, description = Loc.T("Большая батарея: 100 кВт·ч, разряд до 100 кВт.") },

            // ---- Communication ----
            new ModuleDefinition { id = NavComputer, name = Loc.T("Навигационный компьютер НК-1"), category = ModuleCategory.Communication, dryMass = 120, length = 0.3, price = 0, powerDraw = 30,
                capabilities = ModuleCapability.OrbitPrediction,
                description = Loc.T("Рассчитывает текущую орбиту: перицентр, апоцентр, период, наклонение (прибор «Анализ орбиты»).") },
            new ModuleDefinition { id = StarTracker, name = Loc.T("Звёздный датчик СД-2"), category = ModuleCategory.Communication, dryMass = 60, length = 0.2, price = 0, powerDraw = 15,
                capabilities = ModuleCapability.InterplanetaryNavigation,
                description = Loc.T("Ориентация по звёздам — нужна для межпланетной навигации и режима подсказок планировщика.") },
            new ModuleDefinition { id = AntennaOmni, name = Loc.T("Антенна АН-1"), category = ModuleCategory.Communication, dryMass = 3, length = 0.5, powerDraw = 10, antennaRange = 1e7, antennaPower = 5,
                description = Loc.T("Всенаправленная антенна: связь без наведения, до 10 тыс. км при 5 Вт.") },
            new ModuleDefinition { id = DishSmall, name = Loc.T("Тарелка ТР-1"), category = ModuleCategory.Communication, dryMass = 12, length = 0.4, diameter = 1, powerDraw = 40, antennaRange = 5e10, antennaPower = 15,
                description = Loc.T("Метровая направленная антенна: 50 млн км при 15 Вт. Соседние планеты.") },
            new ModuleDefinition { id = DishLarge, name = Loc.T("Тарелка ТР-4"), category = ModuleCategory.Communication, dryMass = 55, length = 0.8, diameter = 3.7, powerDraw = 80, antennaRange = 5e12, antennaPower = 25,
                description = Loc.T("Большая антенна 3,7 м: 5 млрд км при 25 Вт. Край системы.") },
            new ModuleDefinition { id = Relay, name = Loc.T("Ретранслятор РТ-2"), category = ModuleCategory.Communication, dryMass = 35, length = 0.6, diameter = 1.5, powerDraw = 110, antennaRange = 2e11, antennaPower = 30, relay = true,
                description = Loc.T("Принимает сигнал других аппаратов и передаёт дальше: 200 млн км при 30 Вт.") },

            // ---- Mechanics ----
            new ModuleDefinition { id = CollectorBasic, name = Loc.T("Пробоотборник ПБ-1"), category = ModuleCategory.Mechanics, mechanismKind = MechanismKind.SampleCollector, dryMass = 150, length = 0.5, price = 0, powerDraw = 40,
                sampleCapacity = 10, sampleKinds = SampleKinds.Rock | SampleKinds.Soil,
                description = Loc.T("Камни и грунт, до 10 кг.") },
            new ModuleDefinition { id = CollectorAdvanced, name = Loc.T("Пробоотборник ПБ-2"), category = ModuleCategory.Mechanics, mechanismKind = MechanismKind.SampleCollector, dryMass = 260, length = 0.7, powerDraw = 60,
                price = 8000, requiresPurchase = true, sampleCapacity = 30,
                sampleKinds = SampleKinds.Rock | SampleKinds.Soil | SampleKinds.Liquid | SampleKinds.Acid | SampleKinds.Rare,
                description = Loc.T("До 30 кг; жидкости, кислоты и редкие материалы.") },
            new ModuleDefinition { id = WheelSmall, name = Loc.T("Колесо КЛ-40"), category = ModuleCategory.Mechanics, mechanismKind = MechanismKind.Wheel, dryMass = 9, length = 0.4, diameter = 0.4,
                wheelLoad = 1200, wheelMaxSpeed = 3, description = Loc.T("Лёгкое колесо 40 см: нагрузка до 1,2 кН, скорость до 3 м/с. Ровер собирается минимум на четырёх.") },
            new ModuleDefinition { id = WheelLarge, name = Loc.T("Колесо КЛ-100"), category = ModuleCategory.Mechanics, mechanismKind = MechanismKind.Wheel, dryMass = 45, length = 1, diameter = 1,
                wheelLoad = 8000, wheelMaxSpeed = 5, description = Loc.T("Большое колесо метрового диаметра: нагрузка до 8 кН, скорость до 5 м/с.") },
            new ModuleDefinition { id = RoverChassis, name = Loc.T("Шасси ШС-1"), category = ModuleCategory.Mechanics, mechanismKind = MechanismKind.RoverChassis, dryMass = 180, length = 2,
                description = Loc.T("Рама ровера: на неё ставятся колёса, приборы и бур.") },
            new ModuleDefinition { id = RoverDrive, name = Loc.T("Привод ПД-1"), category = ModuleCategory.Mechanics, mechanismKind = MechanismKind.RoverDrive, dryMass = 30, length = 0.3, powerDraw = 300,
                description = Loc.T("Электроприводы колёс ровера с редукторами, 300 Вт.") },
            new ModuleDefinition { id = DrillSmall, name = Loc.T("Бур БУ-1"), category = ModuleCategory.Mechanics, mechanismKind = MechanismKind.Drill, dryMass = 20, length = 0.5, powerDraw = 150,
                drillDepth = 0.15, sampleCapacity = 2, sampleKinds = SampleKinds.Soil, description = Loc.T("Лёгкий бур: керн грунта с глубины 15 см, до 2 кг.") },
            new ModuleDefinition { id = DrillLarge, name = Loc.T("Бур БУ-2"), category = ModuleCategory.Mechanics, mechanismKind = MechanismKind.Drill, dryMass = 90, length = 2, powerDraw = 500,
                drillDepth = 2, sampleCapacity = 8, sampleKinds = SampleKinds.Rock | SampleKinds.Soil, description = Loc.T("Глубокий бур: керн грунта и пород с глубины 2 м, до 8 кг.") },
            new ModuleDefinition { id = Manipulator, name = Loc.T("Манипулятор МН-1"), category = ModuleCategory.Mechanics, mechanismKind = MechanismKind.Manipulator, dryMass = 45, length = 1.5, powerDraw = 60,
                description = Loc.T("Рука с тремя суставами, вылет 1,5 м: подвести прибор к образцу или взять груз.") },

            // ---- Cargo ----
            new ModuleDefinition { id = Cargo2, name = Loc.T("Контейнер КГ-2"), category = ModuleCategory.Cargo, dryMass = 80, length = 1, cargoVolume = 2,
                description = Loc.T("Пустой цилиндр на 2 м³ для приборов и малых аппаратов.") },
            new ModuleDefinition { id = Cargo12, name = Loc.T("Отсек КГ-12"), category = ModuleCategory.Cargo, dryMass = 450, length = 2.5, cargoVolume = 12,
                description = Loc.T("Негерметичный грузовой отсек на 12 м³.") },
            new ModuleDefinition { id = Cargo40, name = Loc.T("Отсек КГ-40"), category = ModuleCategory.Cargo, dryMass = 1200, length = 4, cargoVolume = 40,
                description = Loc.T("Большой негерметичный отсек на 40 м³.") },
            new ModuleDefinition { id = CargoPressurized20, name = Loc.T("Гермоотсек ГО-20"), category = ModuleCategory.Cargo, dryMass = 2500, length = 4, cargoVolume = 20, pressurized = true, powerDraw = 300,
                description = Loc.T("Герметичный отсек на 20 м³ с атмосферой: для людей, образцов и живых экспериментов.") },
        };
    }
}
