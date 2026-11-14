using System;
using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    /// <summary>
    /// Designs made of the original modules (their ids are in player saves) must fly exactly as before the catalog grew.
    /// The numbers below were printed by the code as it was before the new parts were added.
    /// </summary>
    public class BlueprintRegressionTests
    {
        const double HomeGravity = 8.27;

        static void Same(double expected, double actual, string what) =>
            Assert.AreEqual(expected, actual, 1e-9 + 1e-12 * Math.Abs(expected), what);

        struct StageNumbers
        {
            public double Dry, Propellant, Thrust, VacuumIsp, SeaIsp, Length, DvVacuum, DvSurface, MassFlow, BurnTime, Twr;

            public StageNumbers(double dry, double propellant, double thrust, double vacuumIsp, double seaIsp, double length,
                double dvVacuum, double dvSurface, double massFlow, double burnTime, double twr)
            {
                Dry = dry; Propellant = propellant; Thrust = thrust; VacuumIsp = vacuumIsp; SeaIsp = seaIsp; Length = length;
                DvVacuum = dvVacuum; DvSurface = dvSurface; MassFlow = massFlow; BurnTime = burnTime; Twr = twr;
            }
        }

        static void CheckStages(CompiledRocket c, RocketDesignReport report, StageNumbers[] expected)
        {
            Assert.AreEqual(expected.Length, c.Configuration.stages.Length);
            for (int i = 0; i < expected.Length; i++)
            {
                StageNumbers e = expected[i];
                RocketStage s = c.Configuration.stages[i];
                StageReport r = report.Stages[i];
                string n = "stage " + i + " ";
                Same(e.Dry, s.dryMass, n + "dry mass");
                Same(e.Propellant, s.propellantMass, n + "propellant");
                Same(e.Thrust, s.vacuumThrust, n + "thrust");
                Same(e.VacuumIsp, s.vacuumIsp, n + "vacuum Isp");
                Same(e.SeaIsp, s.seaLevelIsp, n + "sea-level Isp");
                Same(e.Length, s.length, n + "length");
                Same(e.DvVacuum, r.DeltaVVacuum, n + "Δv vacuum");
                Same(e.DvSurface, r.DeltaVSurface, n + "Δv surface");
                Same(e.MassFlow, r.MassFlow, n + "mass flow");
                Same(e.BurnTime, r.BurnTime, n + "burn time");
                Same(e.Twr, r.IgnitionTwr, n + "TWR");
            }
        }

        static RocketBlueprint KitchenSink()
        {
            var b = new RocketBlueprint { name = "Kitchen" };
            var s1 = new StageBlueprint { name = "S1", engineId = ModuleCatalog.EngineSokolM, engineCount = 7 };
            s1.tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK140, propellantLoaded = 140000 });
            s1.tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK100, propellantLoaded = 80000 });
            var s2 = new StageBlueprint { name = "S2", engineId = ModuleCatalog.EngineVega, engineCount = 2 };
            s2.tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK40, propellantLoaded = 40000 });
            s2.tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK10, propellantLoaded = 5000 });
            var s3 = new StageBlueprint { name = "S3", engineId = ModuleCatalog.EngineHydra, engineCount = 3 };
            s3.tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankH20, propellantLoaded = 20000 });
            var s4 = new StageBlueprint { name = "S4", engineId = ModuleCatalog.EngineIskra, engineCount = 1 };
            s4.tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankN2, propellantLoaded = 2000 });
            s4.tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK10, propellantLoaded = 10000 }); // wrong propellant: dead weight
            var s5 = new StageBlueprint { name = "S5", engineId = ModuleCatalog.EngineSokol, engineCount = 1 };
            s5.tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK10, propellantLoaded = 3000 });
            b.stages.Add(s1);
            b.stages.Add(s2);
            b.stages.Add(s3);
            b.stages.Add(s4);
            b.stages.Add(s5);
            b.payloadModules.AddRange(new[]
            {
                ModuleCatalog.ProbeCore, ModuleCatalog.NavComputer, ModuleCatalog.StarTracker, ModuleCatalog.RadiationShield,
                ModuleCatalog.RadiationShieldHeavy, ModuleCatalog.AcidCoating, ModuleCatalog.Spectrometer, ModuleCatalog.MeteoKit,
                ModuleCatalog.CollectorBasic, ModuleCatalog.CollectorAdvanced
            });
            return b;
        }

        [Test]
        public void DefaultDesign_HasTheSameNumbers()
        {
            CompiledRocket c = BlueprintCompiler.Compile(RocketBlueprint.CreateDefault(), id => true);
            RocketDesignReport report = RocketDesignAnalysis.Analyze(c.Configuration, HomeGravity);
            Assert.IsEmpty(c.Issues);
            Same(1000, c.PayloadMass, "payload");
            Same(340000, c.UsableFuel, "usable");
            Same(0, c.UnusableFuel, "unusable");
            Same(340000, c.MaxFuelCapacity, "capacity");
            Same(367000, report.LaunchMass, "launch");
            Same(27000, report.DryMass, "dry");
            Same(4648484.848484849, report.MaxSurfaceThrust, "surface thrust");
            Same(5200000, report.MaxVacuumThrust, "vacuum thrust");
            Same(1.5315805621859149, report.LiftoffTwr, "TWR");
            Same(13045.290744587379, report.TotalDeltaVVacuum, "Δv");
            CheckStages(c, report, new[]
            {
                new StageNumbers(22000, 300000, 5200000, 330, 295, 36, 5503.696404120397, 4919.97102792581, 1606.8255477227963, 186.70352884615383, 1.5315805621859149),
                new StageNumbers(4000, 40000, 700000, 350, 240, 12, 7541.594340466982, 5171.378976320217, 203.94324259558564, 196.133, 1.880961977697165)
            });

            var state = new RocketState(c.Configuration);
            Same(367000, state.Mass, "state mass");
            Same(13045.290744587379, state.DeltaVRemaining(), "state Δv");
            Same(1606.8255477227963, state.FullThrottleMassFlow, "full throttle flow");
            Same(1.5315805621859149, state.AvailableThrustToWeight(1.0, HomeGravity), "state TWR");
            state.EngineOn = true;
            state.Throttle = 0.7;
            state.Burn(12.5);
            Same(285940.27645742556, state.Propellant[0], "propellant after a burn");
            Same(1124.7778834059573, state.MassFlow, "throttled flow");
            Same(3446969.696969697, state.Thrust(0.5), "throttled thrust");
        }

        [Test]
        public void DesignWithEveryOriginalModule_HasTheSameNumbers()
        {
            CompiledRocket c = BlueprintCompiler.Compile(KitchenSink(), id => true);
            RocketDesignReport report = RocketDesignAnalysis.Analyze(c.Configuration, HomeGravity);
            Assert.AreEqual(1, c.Issues.Count, "only the wrong propellant");
            Assert.AreEqual(BlueprintIssueCode.IncompatiblePropellant, c.Issues[0].Code);
            Same(3345, c.PayloadMass, "payload");
            Same(290000, c.UsableFuel, "usable");
            Same(10000, c.UnusableFuel, "unusable");
            Same(332000, c.MaxFuelCapacity, "capacity");
            Same(343045, report.LaunchMass, "launch");
            Same(53045, report.DryMass, "dry");
            Same(9474852.071005916, report.MaxSurfaceThrust, "surface thrust");
            Same(10500000, report.MaxVacuumThrust, "vacuum thrust");
            Same(3.3397644292566016, report.LiftoffTwr, "TWR");
            Same(9571.449468094183, report.TotalDeltaVVacuum, "Δv");
            CheckStages(c, report, new[]
            {
                new StageNumbers(26000, 220000, 10500000, 338, 305, 29, 3398.5463779890715, 3066.7356369428007, 3167.7574663515525, 69.44976133333333, 3.3397644292566016),
                new StageNumbers(6400, 45000, 1400000, 350, 240, 15, 2138.566770287889, 1466.445785340267, 407.8864851911713, 110.32481250000001, 1.7444131896825688),
                new StageNumbers(2900, 20000, 330000, 450, 280, 14.5, 2544.290751947976, 1583.1142456565183, 74.77918895171474, 267.4540909090909, 0.8742088906249442),
                new StageNumbers(11100, 2000, 30000, 315, 250, 5.5, 284.3205502737791, 225.65123037601512, 9.711582980742175, 205.93964999999997, 0.15948865809698665),
                new StageNumbers(3300, 3000, 1300000, 330, 295, 6, 1205.7250175954675, 1077.8450914868574, 401.70638693069907, 7.468141153846153, 16.298048684778816)
            });

            var state = new RocketState(c.Configuration);
            Same(9571.449468094183, state.DeltaVRemaining(), "state Δv");
            state.EngineOn = true;
            state.Throttle = 0.7;
            state.Burn(12.5);
            Same(192282.1221694239, state.Propellant[0], "propellant after a burn");
            Same(6991198.224852071, state.Thrust(0.5), "throttled thrust");
        }

        [Test]
        public void DesignWithoutCabin_StillGetsTheProbeCore()
        {
            var b = new RocketBlueprint();
            b.stages.Add(new StageBlueprint { name = "X", engineId = ModuleCatalog.EngineIskra, engineCount = 2 });
            b.stages[0].tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankN2, propellantLoaded = 1234.5 });
            CompiledRocket c = BlueprintCompiler.Compile(b, id => true);
            RocketDesignReport report = RocketDesignAnalysis.Analyze(c.Configuration, HomeGravity);
            Same(1000, c.PayloadMass, "the core added automatically");
            Same(2634.5, report.LaunchMass, "launch");
            Same(1952.9911095843054, report.TotalDeltaVVacuum, "Δv");
            Assert.AreEqual(1, c.Issues.Count);
            Assert.AreEqual(BlueprintIssueCode.MissingCore, c.Issues[0].Code);
        }

        [Test]
        public void OriginalModules_KeepTheirIds()
        {
            string[] ids =
            {
                "eng-sokol-1", "eng-sokol-1m", "eng-vega-v", "eng-hydra", "eng-iskra", "tank-k100", "tank-k140", "tank-k40", "tank-k10", "tank-h20",
                "tank-n2", "core-probe", "nav-nk1", "nav-sd2", "prot-rad1", "prot-rad2", "prot-acid1", "sci-sp1", "sci-meteo", "col-pb1", "col-pb2"
            };
            foreach (string id in ids) Assert.IsNotNull(ModuleCatalog.Find(id), id);
        }
    }
}
