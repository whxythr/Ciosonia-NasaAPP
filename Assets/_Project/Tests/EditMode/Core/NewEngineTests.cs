using System;
using NUnit.Framework;
using SpaceSim.Economy;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    public class NewEngineTests
    {
        const double HomeGravity = 8.27;

        static RocketBlueprint Single(string engine, int count, params string[] payload)
        {
            var b = new RocketBlueprint();
            b.stages.Add(new StageBlueprint { name = "S1", engineId = engine, engineCount = count });
            b.payloadModules.AddRange(payload);
            return b;
        }

        static void Tank(RocketBlueprint b, string id, double kg, int stage = 0) =>
            b.stages[stage].tanks.Add(new TankLoad { moduleId = id, propellantLoaded = kg });

        // ---- Solid motors ------------------------------------------------------------------------------

        [Test]
        public void Solid_CarriesItsGrain_AndNeedsNoTanks()
        {
            CompiledRocket c = BlueprintCompiler.Compile(Single(ModuleCatalog.EngineSolidMedium, 2, ModuleCatalog.ProbeCore));
            RocketStage s = c.Configuration.stages[0];
            Assert.IsEmpty(c.Issues);
            Assert.AreEqual(98000.0, s.propellantMass, 1e-9, "grain of both motors");
            Assert.AreEqual(8200.0, s.dryMass, 1e-9, "only the casings");
            Assert.AreEqual(EngineKind.Solid, s.engineKind);
            Assert.AreEqual(98000.0, c.UsableFuel, 1e-9);
            Assert.AreEqual(98000.0, c.MaxFuelCapacity, 1e-9);
            Assert.AreEqual(3.3e6, s.vacuumThrust, 1e-6);
        }

        [Test]
        public void Solid_WithATank_CarriesItAsDeadWeight()
        {
            RocketBlueprint b = Single(ModuleCatalog.EngineSolidSmall, 1, ModuleCatalog.ProbeCore);
            Tank(b, ModuleCatalog.TankK10, 10000);
            CompiledRocket c = BlueprintCompiler.Compile(b);
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.IncompatiblePropellant));
            Assert.AreEqual(2010.0, c.Configuration.stages[0].propellantMass, 1e-9);
            Assert.AreEqual(10000.0, c.UnusableFuel, 1e-9);
        }

        [Test]
        public void Solid_OnceLit_IgnoresThrottleAndShutdown()
        {
            CompiledRocket c = BlueprintCompiler.Compile(Single(ModuleCatalog.EngineSolidSmall, 1, ModuleCatalog.ProbeCore));
            var r = new RocketState(c.Configuration);
            Assert.IsFalse(r.CanThrottle);
            r.Throttle = 0.2;
            Assert.AreEqual(1.0, r.Throttle, "there is no throttle");
            r.EngineOn = true;
            Assert.IsTrue(r.IsSolidBurning);
            Assert.AreEqual(RocketEquations.MassFlow(6.8e4, 292), r.MassFlow, 1e-9, "full thrust regardless of the command");
            r.Throttle = 0.0;
            Assert.IsTrue(r.IsBurning);
            r.EngineOn = false;
            Assert.IsTrue(r.EngineOn, "cannot be stopped");
            Assert.IsTrue(r.IsBurning);
        }

        [Test]
        public void Solid_BurnsToDepletion_ExactlyAsTheAnalysisSays()
        {
            CompiledRocket c = BlueprintCompiler.Compile(Single(ModuleCatalog.EngineSolidMedium, 1, ModuleCatalog.ProbeCore));
            RocketDesignReport report = RocketDesignAnalysis.Analyze(c.Configuration, HomeGravity);
            Assert.IsTrue(report.Stages[0].IsSolid);
            var r = new RocketState(c.Configuration);
            double dvBefore = r.DeltaVRemaining();
            Assert.AreEqual(report.TotalDeltaVVacuum, dvBefore, 1e-6, "flight and hangar agree on Δv");
            r.EngineOn = true;
            double t = 0.0;
            const double h = 0.01;
            while (r.IsBurning && t < 1000.0)
            {
                r.Burn(h);
                t += h;
            }
            Assert.AreEqual(report.Stages[0].BurnTime, t, 2.0 * h, "burn time");
            Assert.AreEqual(0.0, r.CurrentPropellant, 1e-3);
            Assert.IsFalse(r.EngineOn, "depleted: it stops by itself");
        }

        [Test]
        public void Solid_CanBeDroppedWhileBurning_AndExtinguishedOnlyByTheCrash()
        {
            var b = new RocketBlueprint();
            b.stages.Add(new StageBlueprint { name = "S1", engineId = ModuleCatalog.EngineSolidSmall });
            b.stages.Add(new StageBlueprint { name = "S2", engineId = ModuleCatalog.EngineIskra });
            Tank(b, ModuleCatalog.TankN2, 2000, 1);
            CompiledRocket c = BlueprintCompiler.Compile(b);

            var r = new RocketState(c.Configuration);
            r.EngineOn = true;
            r.Extinguish();
            Assert.IsFalse(r.IsBurning, "a crash stops everything");

            r.EngineOn = true;
            r.Burn(1.0);
            Assert.IsTrue(r.Separate());
            Assert.IsTrue(r.CanThrottle, "the liquid stage above can be throttled");
            r.Throttle = 0.5;
            Assert.AreEqual(0.5, r.Throttle, 1e-12);
            r.EngineOn = false;
            Assert.IsFalse(r.EngineOn, "and stopped");
        }

        // ---- Nuclear -------------------------------------------------------------------------------------

        [Test]
        public void Nuclear_BurnsLiquidHydrogen_WeakNearTheGround()
        {
            RocketBlueprint b = Single(ModuleCatalog.EngineNuclearSmall, 1, ModuleCatalog.ProbeCore);
            Tank(b, ModuleCatalog.TankHydrogen8, 8000);
            CompiledRocket c = BlueprintCompiler.Compile(b);
            RocketStage s = c.Configuration.stages[0];
            Assert.AreEqual(8000.0, s.propellantMass, 1e-9);
            Assert.AreEqual(EngineKind.Nuclear, s.engineKind);
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.VacuumEngineOnFirstStage), "850 s only in vacuum");
            var r = new RocketState(c.Configuration);
            Assert.AreEqual(850.0, r.Isp(0.0), 1e-9);
            Assert.AreEqual(360.0, r.Isp(1.0), 1e-9);
            RocketDesignReport report = RocketDesignAnalysis.Analyze(c.Configuration, HomeGravity);
            Assert.AreEqual(850.0 * RocketEquations.G0 * Math.Log(13600.0 / 5600.0), report.TotalDeltaVVacuum, 1e-6, "payload 1 t + engine 3.5 t + tank 1.1 t + 8 t of hydrogen");
        }

        [Test]
        public void Nuclear_CannotBurnKerosene()
        {
            RocketBlueprint b = Single(ModuleCatalog.EngineNuclearSmall, 1, ModuleCatalog.ProbeCore);
            Tank(b, ModuleCatalog.TankK10, 10000);
            CompiledRocket c = BlueprintCompiler.Compile(b);
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.IncompatiblePropellant));
            Assert.AreEqual(0.0, c.Configuration.stages[0].propellantMass);
        }

        // ---- Electric ------------------------------------------------------------------------------------

        /// <summary>Hall thruster, 1 t of xenon; net of everything in the payload.</summary>
        static RocketBlueprint Hall(params string[] payload)
        {
            RocketBlueprint b = Single(ModuleCatalog.EngineHall, 1, payload);
            Tank(b, ModuleCatalog.TankXenon1, 1000);
            return b;
        }

        const double HallPower = 0.6 * RocketEquations.G0 * 2800.0 / (2.0 * 0.6);

        [Test]
        public void ElectricPower_IsTheJetPowerOverEfficiency()
        {
            Assert.AreEqual(HallPower, RocketEquations.ElectricPower(0.6, 2800, 0.6), 1e-9);
            Assert.AreEqual(0.6, RocketEquations.ThrustFromElectricPower(HallPower, 2800, 0.6), 1e-12, "the inverse");
            Assert.AreEqual(0.0, RocketEquations.ElectricPower(0.6, 2800, 0.0));
            RocketStage s = BlueprintCompiler.Compile(Hall(ModuleCatalog.ProbeCore, ModuleCatalog.SolarMedium)).Configuration.stages[0];
            Assert.AreEqual(HallPower, s.electricPowerDemand, 1e-9);
        }

        [Test]
        public void Electric_WithEnoughPower_GivesFullThrust_NoIssue()
        {
            CompiledRocket c = BlueprintCompiler.Compile(Hall(ModuleCatalog.ProbeCore, ModuleCatalog.SolarMedium));
            Assert.IsFalse(c.Issues.Exists(i => i.Code == BlueprintIssueCode.NoElectricPower || i.Code == BlueprintIssueCode.InsufficientElectricPower));
            var r = new RocketState(c.Configuration);
            Assert.AreEqual(1.0, r.PowerFraction, 1e-12);
            r.EngineOn = true;
            Assert.AreEqual(0.6, r.Thrust(0.0), 1e-9);
            Assert.AreEqual(RocketEquations.MassFlow(0.6, 2800), r.MassFlow, 1e-15);
        }

        [Test]
        public void Electric_WithoutPower_HasZeroThrust_AndSaysSo()
        {
            // Only systems that the generators cannot cover: nothing is left for the thruster.
            CompiledRocket c = BlueprintCompiler.Compile(Hall(ModuleCatalog.ProbeSmall, ModuleCatalog.CargoPressurized20));
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.NoElectricPower && i.StageIndex == 0));
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.PowerDeficit));
            RocketDesignReport report = RocketDesignAnalysis.Analyze(c.Configuration, HomeGravity);
            Assert.AreEqual(0.0, report.Stages[0].VacuumThrust);
            Assert.AreEqual(0.0, report.Stages[0].MassFlow);
            Assert.AreEqual(0.0, report.Stages[0].DeltaVVacuum);
            Assert.AreEqual(0.0, report.Stages[0].BurnTime, "not infinity: it never starts");
            var r = new RocketState(c.Configuration);
            r.EngineOn = true;
            Assert.IsFalse(r.CanBurn);
            Assert.IsFalse(r.IsBurning);
            Assert.AreEqual(0.0, r.MassFlow);
            Assert.AreEqual(0.0, r.DeltaVRemaining(), 1e-12);
        }

        [Test]
        public void Electric_WithLittlePower_HasProportionalThrust_AndTheSameDeltaV()
        {
            // Probe: 600 W out, 150 W for itself; panel: 2 kW. 2450 W of the 13.7 kW needed.
            CompiledRocket weak = BlueprintCompiler.Compile(Hall(ModuleCatalog.ProbeCore, ModuleCatalog.SolarSmall));
            Assert.IsTrue(weak.Issues.Exists(i => i.Code == BlueprintIssueCode.InsufficientElectricPower));
            double fraction = 2450.0 / HallPower;

            RocketDesignReport report = RocketDesignAnalysis.Analyze(weak.Configuration, HomeGravity);
            Assert.AreEqual(fraction, report.Stages[0].PowerFraction, 1e-12);
            Assert.AreEqual(0.6 * fraction, report.Stages[0].VacuumThrust, 1e-12);
            Assert.AreEqual(0.6, report.Stages[0].NominalVacuumThrust, 1e-12);

            var r = new RocketState(weak.Configuration);
            r.EngineOn = true;
            Assert.AreEqual(0.6 * fraction, r.Thrust(0.0), 1e-12, "flight and hangar agree");

            CompiledRocket strong = BlueprintCompiler.Compile(Hall(ModuleCatalog.ProbeCore, ModuleCatalog.SolarSmall, ModuleCatalog.SolarMedium));
            RocketDesignReport full = RocketDesignAnalysis.Analyze(strong.Configuration, HomeGravity);
            double massPenalty = RocketEquations.DeltaV(2800, report.LaunchMass, report.LaunchMass - 1000.0) - report.TotalDeltaVVacuum;
            Assert.AreEqual(0.0, massPenalty, 1e-6, "Δv is Tsiolkovsky's, whatever the thrust");
            Assert.Greater(report.Stages[0].BurnTime, full.Stages[0].BurnTime * 5.0, "just much slower");
        }

        [Test]
        public void Electric_ThrottleBelowThePowerLimit_IsNotLimitedAgain()
        {
            CompiledRocket weak = BlueprintCompiler.Compile(Hall(ModuleCatalog.ProbeCore, ModuleCatalog.SolarSmall));
            var r = new RocketState(weak.Configuration);
            r.EngineOn = true;
            double limit = r.PowerFraction;
            r.Throttle = limit / 2.0;
            Assert.AreEqual(0.6 * limit / 2.0, r.Thrust(0.0), 1e-12);
            r.Throttle = 1.0;
            Assert.AreEqual(0.6 * limit, r.Thrust(0.0), 1e-12);
        }

        [Test]
        public void Electric_OnBatteriesAlone_StopsWhenTheyAreFlat_LikeTheAnalysisSays()
        {
            // No net generation: the probe's own panel is less than a pressurised bay draws.
            CompiledRocket c = BlueprintCompiler.Compile(Hall(ModuleCatalog.ProbeSmall, ModuleCatalog.CargoPressurized20, ModuleCatalog.BatterySmall));
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.InsufficientElectricPower));
            RocketDesignReport report = RocketDesignAnalysis.Analyze(c.Configuration, HomeGravity);
            Assert.Less(report.Stages[0].BurnablePropellant, 1000.0, "the xenon outlasts the batteries");
            Assert.Greater(report.Stages[0].BurnablePropellant, 0.0);

            var r = new RocketState(c.Configuration);
            Assert.AreEqual(report.TotalDeltaVVacuum, r.DeltaVRemaining(), 1e-9, "the same limit in flight");
            r.EngineOn = true;
            double t = 0.0;
            const double h = 1.0;
            while (r.IsBurning && t < 1e8)
            {
                r.Burn(h);
                t += h;
            }
            Assert.AreEqual(0.0, r.BatteryEnergy, 1e-9, "flat");
            Assert.AreEqual(report.Stages[0].BurnTime, t, 3.0 * h, "burn time");
            Assert.AreEqual(1000.0 - report.Stages[0].BurnablePropellant, r.CurrentPropellant, 0.01, "the rest stays in the tank");
            Assert.AreEqual(0.0, r.MassFlow);
        }

        [Test]
        public void Electric_BatteriesThenGenerators_MatchesTheAnalysis()
        {
            // Generators for a part of the demand, batteries for the rest of it until they run flat, generators alone after that.
            CompiledRocket c = BlueprintCompiler.Compile(Hall(ModuleCatalog.ProbeCore, ModuleCatalog.SolarSmall, ModuleCatalog.BatterySmall));
            RocketDesignReport report = RocketDesignAnalysis.Analyze(c.Configuration, HomeGravity);
            Assert.AreEqual(1000.0, report.Stages[0].BurnablePropellant, 1e-9);
            var r = new RocketState(c.Configuration);
            r.EngineOn = true;
            double t = 0.0;
            const double h = 10.0;
            while (r.IsBurning && t < 1e9)
            {
                r.Burn(h);
                t += h;
            }
            Assert.AreEqual(0.0, r.CurrentPropellant, 1e-3);
            Assert.AreEqual(report.Stages[0].BurnTime, t, 3.0 * h);
        }

        [Test]
        public void Electric_FarFromTheStar_LosesSolarPower()
        {
            CompiledRocket c = BlueprintCompiler.Compile(Hall(ModuleCatalog.ProbeCore, ModuleCatalog.SolarMedium));
            var r = new RocketState(c.Configuration);
            Assert.AreEqual(1.0, r.PowerFraction, 1e-12);
            r.SolarFluxRatio = PartPhysics.SolarFluxRatio(3.0, 1.0);
            double available = (600.0 + 20000.0) / 9.0 - 150.0;
            Assert.AreEqual(available / HallPower, r.PowerFraction, 1e-12);
        }

        [Test]
        public void Batteries_RechargeFromTheSurplus_UpToTheirCapacity()
        {
            CompiledRocket c = BlueprintCompiler.Compile(Hall(ModuleCatalog.ProbeCore, ModuleCatalog.SolarSmall, ModuleCatalog.BatterySmall));
            var r = new RocketState(c.Configuration);
            double full = r.BatteryEnergy;
            Assert.AreEqual(3.6e7, full, 1e-3);
            r.EngineOn = true;
            r.Burn(100.0);
            Assert.Less(r.BatteryEnergy, full);
            r.EngineOn = false;
            r.UpdateBatteries(1e7);
            Assert.AreEqual(full, r.BatteryEnergy, 1e-3);
        }

        [Test]
        public void Electric_AtSeaLevel_HasAlmostNoThrust()
        {
            CompiledRocket c = BlueprintCompiler.Compile(Hall(ModuleCatalog.ProbeCore, ModuleCatalog.SolarMedium));
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.VacuumEngineOnFirstStage));
            var r = new RocketState(c.Configuration);
            r.EngineOn = true;
            Assert.AreEqual(1.0, r.Isp(1.0), 1e-12);
            Assert.Less(r.Thrust(1.0), 0.6 / 1000.0);
        }

        [Test]
        public void ElectricThrust_InOrbit_SpiralsOut_ByItsDeltaV()
        {
            // A real integration of a low-thrust spiral: a slow tangential burn raises the orbit by exactly the Edelbaum amount.
            const double f = 0.6, isp = 2800.0, m0 = 2000.0;
            var config = new RocketConfiguration
            {
                stages = new[] { new RocketStage { name = "E", dryMass = 500.0, propellantMass = 500.0, vacuumThrust = f, vacuumIsp = isp, seaLevelIsp = 0.0,
                    engineKind = EngineKind.Electric, electricPowerDemand = 1.0 } },
                payloadMass = 1000.0,
                steadyPower = 1.0
            };
            PlanetModelFixture(out var sim, out double mu, out double r0);
            double v0 = Math.Sqrt(mu / r0);
            sim.SetLocalState(0.0, 0.0, r0 - TestPlanets.EarthRadius, v0, 0.0, 0.0, false, 0.0); // east along the equator: thrust stays prograde
            sim.Rocket = new RocketState(config) { PitchDegrees = 90.0, HeadingDegrees = 90.0, EngineOn = true };
            Assert.AreEqual(TimeWarpZone.LowThrustFlight, TimeWarpPolicy.Evaluate(sim.WarpSituation()).Zone, "a low-thrust burn on a safe orbit may be sped up");
            Assert.AreEqual(TimeWarpPolicy.LowThrustWarp, TimeWarpPolicy.Evaluate(sim.WarpSituation()).MaxWarp);
            Assert.AreEqual(0.0, sim.RailsHorizon(100.0), "no long steps without a star-system model");

            const double burn = 2.0e5, dt = 2.0;
            for (int i = 0; i < burn / dt; i++) sim.Step(dt);

            double m1 = sim.Rocket.Mass;
            double expectedBurned = RocketEquations.MassFlow(f, isp) * burn;
            Assert.AreEqual(m0 - expectedBurned, m1, 1e-6, "the mass flow of a tiny thruster is not lost to rounding");
            double dv = RocketEquations.DeltaV(isp, m0, m1);

            double r = sim.State.Position.Magnitude, v = sim.State.Velocity.Magnitude;
            double a = -mu / (2.0 * (0.5 * v * v - mu / r));
            double vCircular = Math.Sqrt(mu / a);
            Assert.AreEqual(v0 - dv, vCircular, 0.01 * dv, "spiral Δv = v₀ − v_f");
        }

        static void PlanetModelFixture(out FreeFlightSimulation sim, out double mu, out double r0)
        {
            var planet = TestPlanets.Earth(rotating: false, atmosphere: false);
            sim = TestPlanets.Simulation(planet);
            mu = TestPlanets.EarthMu;
            r0 = TestPlanets.EarthRadius + 500000.0;
        }

        // ---- Capabilities ---------------------------------------------------------------------------------

        [Test]
        public void AnyCabin_ReplacesTheProbe_AndCountsTheCrew()
        {
            CompiledRocket crewed = BlueprintCompiler.Compile(Single(ModuleCatalog.EngineIskra, 1, ModuleCatalog.CabinCrew3));
            Assert.IsFalse(crewed.Issues.Exists(i => i.Code == BlueprintIssueCode.MissingCore), "a crewed cabin is a cabin");
            Assert.AreEqual(5800.0, crewed.PayloadMass, 1e-9, "no probe was added");
            Assert.AreEqual(3, crewed.Capabilities.CrewCapacity);
            Assert.AreEqual(CabinKind.Crewed, crewed.Capabilities.ControlKind);
            Assert.AreEqual(1, crewed.Capabilities.CrewedCabins);

            CompiledRocket none = BlueprintCompiler.Compile(Single(ModuleCatalog.EngineIskra, 1));
            Assert.IsTrue(none.Issues.Exists(i => i.Code == BlueprintIssueCode.MissingCore));
            Assert.AreEqual(1000.0, none.PayloadMass, 1e-9);
            Assert.AreEqual(CabinKind.RemoteControl, none.Capabilities.ControlKind);
            Assert.AreEqual(1, none.Capabilities.RemoteCabins);
            Assert.Greater(none.Capabilities.SolarPower, 0.0, "the automatic probe brings its own panel");
        }

        [Test]
        public void Capabilities_AggregateEverything()
        {
            var b = RocketBlueprint.CreateDefault();
            b.stages[0].attachments.Add(ModuleCatalog.FinSideLarge);
            b.stages[1].attachments.Add(ModuleCatalog.FinTopSmall);
            b.stages[1].attachments.Add(ModuleCatalog.RcsLarge);
            b.stages[1].attachments.Add(ModuleCatalog.LegsLight);
            b.payloadModules.AddRange(new[]
            {
                ModuleCatalog.CabinCrew1, ModuleCatalog.ParachuteMedium, ModuleCatalog.ParachuteSmall, ModuleCatalog.HeatShieldHeavy, ModuleCatalog.HeatShieldLight,
                ModuleCatalog.LightSmall, ModuleCatalog.LightLarge, ModuleCatalog.DishSmall, ModuleCatalog.Relay, ModuleCatalog.SolarSmall, ModuleCatalog.Rtg, ModuleCatalog.BatterySmall,
                ModuleCatalog.RoverChassis, ModuleCatalog.RoverDrive, ModuleCatalog.WheelSmall, ModuleCatalog.WheelSmall, ModuleCatalog.WheelLarge, ModuleCatalog.WheelLarge,
                ModuleCatalog.DrillSmall, ModuleCatalog.DrillLarge, ModuleCatalog.Manipulator, ModuleCatalog.Cargo12, ModuleCatalog.CargoPressurized20,
                ModuleCatalog.Thermometer, ModuleCatalog.Seismometer
            });
            VehicleCapabilities caps = BlueprintCompiler.Compile(b).Capabilities;

            Assert.AreEqual(1, caps.CrewCapacity);
            Assert.AreEqual(1, caps.RemoteCabins);
            Assert.AreEqual(1, caps.CrewedCabins);
            Assert.AreEqual(CabinKind.Crewed, caps.ControlKind);

            Assert.AreEqual(7.2, caps.FinArea, 1e-9);
            Assert.AreEqual(6.0, caps.SideFinArea, 1e-9);
            Assert.AreEqual(1.2, caps.TopFinArea, 1e-9);
            Assert.AreEqual(1800.0, caps.RcsThrust, 1e-9);
            Assert.AreEqual(4, caps.LegCount);
            Assert.AreEqual(3.0, caps.LegTouchdownSpeed, 1e-9);

            Assert.AreEqual(2, caps.ParachuteCount);
            Assert.AreEqual(490.0 + 78.5, caps.ParachuteArea, 1e-9);
            Assert.AreEqual(0.8 * (490.0 + 78.5), caps.ParachuteDragArea, 1e-9);
            Assert.AreEqual(5e6, caps.HeatShieldFluxLimit, 1e-3, "the best shield");
            Assert.AreEqual(21500.0, caps.LightOutput, 1e-9);

            Assert.Greater(caps.CommRange, 2e11);
            Assert.IsTrue(caps.HasRelay);

            Assert.AreEqual(600.0 + 2000.0, caps.SolarPower, 1e-9);
            Assert.AreEqual(400.0 + 300.0, caps.SteadyPower, 1e-9);
            Assert.AreEqual(3.6e7, caps.BatteryEnergy, 1e-3);
            Assert.AreEqual(10000.0, caps.BatteryPower, 1e-9);
            Assert.Greater(caps.PowerConsumption, 0.0);

            Assert.AreEqual(4, caps.WheelCount);
            Assert.AreEqual(2.0 * 1200.0 + 2.0 * 8000.0, caps.WheelLoad, 1e-9);
            Assert.AreEqual(3.0, caps.WheelMaxSpeed, 1e-9, "the slowest wheel");
            Assert.IsTrue(caps.RoverAssembled);
            Assert.IsTrue(caps.RoverCanDrive(1000.0, 9.81));
            Assert.IsFalse(caps.RoverCanDrive(10000.0, 9.81), "too heavy for these wheels");

            Assert.AreEqual(2, caps.DrillCount);
            Assert.AreEqual(2.0, caps.DrillDepth, 1e-12);
            Assert.AreEqual(SampleKinds.Rock | SampleKinds.Soil, caps.DrillKinds);
            Assert.AreEqual(10.0, caps.SampleCapacity, 1e-12, "drills store their cores");
            Assert.AreEqual(1, caps.ManipulatorCount);

            Assert.AreEqual(32.0, caps.CargoVolume, 1e-9);
            Assert.AreEqual(20.0, caps.PressurizedVolume, 1e-9);
            Assert.IsTrue(caps.CanMeasure(ScienceMeasurement.Temperature | ScienceMeasurement.Seismic));
            Assert.IsFalse(caps.CanMeasure(ScienceMeasurement.Gravity));
        }

        [Test]
        public void Attachments_AddTheirMass_ToTheirStage()
        {
            var b = RocketBlueprint.CreateDefault();
            double before = BlueprintCompiler.Compile(b).Configuration.stages[0].dryMass;
            b.stages[0].attachments.Add(ModuleCatalog.FinSideLarge);
            b.stages[0].attachments.Add(ModuleCatalog.LegsHeavy);
            CompiledRocket c = BlueprintCompiler.Compile(b);
            Assert.IsEmpty(c.Issues);
            Assert.AreEqual(before + 320.0 + 2000.0, c.Configuration.stages[0].dryMass, 1e-9);
            Assert.AreEqual(36.0, c.Configuration.stages[0].length, 1e-9, "side hardware adds no length");
        }

        [Test]
        public void Hardware_LeavesWithItsStage()
        {
            var b = RocketBlueprint.CreateDefault();
            b.stages[0].attachments.Add(ModuleCatalog.LegsHeavy);
            b.stages[0].attachments.Add(ModuleCatalog.FinSideLarge);
            b.stages[1].attachments.Add(ModuleCatalog.LegsLight);
            VehicleCapabilities caps = BlueprintCompiler.Compile(b).Capabilities;
            Assert.AreEqual(8, caps.LegCount);
            Assert.AreEqual(3.0, caps.LegTouchdownSpeed, 1e-9, "the weakest leg decides");
            StageHardware afterStaging = caps.HardwareFrom(1);
            Assert.AreEqual(4, afterStaging.LegCount);
            Assert.AreEqual(0.0, afterStaging.FinArea);
            Assert.AreEqual(0, caps.HardwareFrom(2).LegCount, "only the payload is left");
        }

        [Test]
        public void WrongMount_IsReported_AndSkipped()
        {
            var b = RocketBlueprint.CreateDefault();
            b.stages[0].attachments.Add(ModuleCatalog.Thermometer);
            b.payloadModules.Add(ModuleCatalog.FinSideLarge);
            double dry = BlueprintCompiler.Compile(RocketBlueprint.CreateDefault()).Configuration.stages[0].dryMass;
            CompiledRocket c = BlueprintCompiler.Compile(b);
            Assert.AreEqual(2, c.Issues.FindAll(i => i.Code == BlueprintIssueCode.WrongMount).Count);
            Assert.AreEqual(dry, c.Configuration.stages[0].dryMass, 1e-9);
            Assert.AreEqual(1000.0, c.PayloadMass, 1e-9);
            Assert.AreEqual(0.0, c.Capabilities.FinArea);
        }

        [Test]
        public void IncompleteRover_IsReported()
        {
            var b = RocketBlueprint.CreateDefault();
            b.payloadModules.Add(ModuleCatalog.WheelSmall);
            Assert.IsTrue(BlueprintCompiler.Compile(b).Issues.Exists(i => i.Code == BlueprintIssueCode.RoverIncomplete));
            Assert.IsFalse(BlueprintCompiler.Compile(RocketBlueprint.CreateDefault()).Issues.Exists(i => i.Code == BlueprintIssueCode.RoverIncomplete));
        }

        [Test]
        public void PowerDeficit_IsReported_WithTheBatteryReserve()
        {
            var b = RocketBlueprint.CreateDefault();
            b.payloadModules.Add(ModuleCatalog.RoverDrive);
            b.payloadModules.Add(ModuleCatalog.DrillLarge);
            CompiledRocket c = BlueprintCompiler.Compile(b);
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.PowerDeficit));
            b.payloadModules.Add(ModuleCatalog.SolarSmall);
            Assert.IsFalse(BlueprintCompiler.Compile(b).Issues.Exists(i => i.Code == BlueprintIssueCode.PowerDeficit), "a panel covers it");
        }

        // ---- Old saves ---------------------------------------------------------------------------------

        [Test]
        public void OldSaves_WithoutAttachments_LoadWithAnEmptyList()
        {
            var b = RocketBlueprint.CreateDefault();
            b.stages[0].attachments = null; // an older file
            Assert.DoesNotThrow(() => BlueprintCompiler.Compile(b));
            Assert.DoesNotThrow(() => RocketLayout.Build(b));
            Assert.IsNotNull(b.Clone().stages[0].attachments, "the copy is clean");

            var profile = PlayerProfile.CreateNew("home");
            profile.blueprint = b;
            Assert.AreEqual(MigrationResult.Migrated, ProfileMigration.Migrate(profile));
            Assert.IsNotNull(profile.blueprint.stages[0].attachments);
            Assert.AreEqual(0, profile.blueprint.stages[0].attachments.Count);
            Assert.AreEqual(MigrationResult.UpToDate, ProfileMigration.Migrate(profile), "nothing more to fix");
        }

        [Test]
        public void Blueprint_CloneCopiesAttachments()
        {
            var a = RocketBlueprint.CreateDefault();
            a.stages[0].attachments.Add(ModuleCatalog.FinSideSmall);
            RocketBlueprint b = a.Clone();
            b.stages[0].attachments.Clear();
            Assert.AreEqual(1, a.stages[0].attachments.Count);
        }
    }
}
