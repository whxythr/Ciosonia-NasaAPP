using System;
using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    /// <summary>The new parts in flight: parachutes, legs, RCS, fins, heat shield, power and the low-thrust time warp.</summary>
    public class FlightSystemsTests
    {
        const double G0 = 9.80665;

        static CompiledRocket Compile(Action<RocketBlueprint> edit)
        {
            RocketBlueprint b = RocketBlueprint.CreateDefault();
            edit?.Invoke(b);
            return BlueprintCompiler.Compile(b);
        }

        /// <summary>A bare payload (no stages) falling through Earth's air at <paramref name="altitude"/> with <paramref name="speed"/> down.</summary>
        static FreeFlightSimulation Capsule(CompiledRocket c, double altitude, double speed, out RocketState rocket)
        {
            var sim = TestPlanets.Simulation(TestPlanets.Earth(rotating: false, atmosphere: true), drag: true);
            rocket = new RocketState(c.Configuration);
            while (rocket.HasStages) rocket.Separate();
            sim.Rocket = rocket;
            sim.Systems = new FlightSystems(c.Capabilities);
            sim.SetLocalState(0.0, 0.0, altitude, 0.0, 0.0, -speed, false, 0.0);
            return sim;
        }

        // ---- Parachutes ------------------------------------------------------------------------------

        [Test]
        public void Parachute_Inflated_AddsDrag_HalfRhoV2CdA()
        {
            CompiledRocket c = Compile(b => b.payloadModules.Add(ModuleCatalog.ParachuteMedium));
            FreeFlightSimulation sim = Capsule(c, 3000.0, 60.0, out RocketState r);
            Vector3d before = sim.DragAcceleration(sim.State.Position, sim.State.Velocity);
            Assert.AreEqual(ParachuteCommand.Released, sim.Systems.ReleaseParachutes());
            Assert.AreEqual(ParachuteCommand.AlreadyReleased, sim.Systems.ReleaseParachutes());
            Assert.AreEqual(0.0, sim.Systems.ParachuteDragArea, 1e-12, "a released canopy starts empty");
            for (int i = 0; i < 200; i++) sim.Step(0.02); // 4 s > inflation time
            Assert.AreEqual(1.0, sim.Systems.ParachuteOpening, 1e-9);
            Assert.AreEqual(0.8 * 490.0, sim.Systems.ParachuteDragArea, 1e-9);

            Vector3d p = sim.State.Position, v = sim.State.Velocity;
            double rho = sim.Planet.DensityAt(p);
            double speed = (v - sim.Planet.AirVelocityAt(p)).Magnitude;
            double bare = 0.5 * rho * speed * speed * sim.Drag.dragCoefficient * sim.Drag.referenceArea;
            double chute = 0.5 * rho * speed * speed * 0.8 * 490.0;
            Assert.AreEqual((bare + chute) / sim.CurrentMass, sim.DragAcceleration(p, v).Magnitude, 1e-9 * (bare + chute));
            Assert.Greater(chute, 10.0 * bare, "the canopy dominates the drag");
            Assert.Greater(before.Magnitude, 0.0);
        }

        [Test]
        public void Parachute_SettlesAtItsDescentSpeed()
        {
            CompiledRocket c = Compile(b => b.payloadModules.Add(ModuleCatalog.ParachuteMedium));
            FreeFlightSimulation sim = Capsule(c, 2500.0, 50.0, out _);
            sim.Systems.ReleaseParachutes();
            for (int i = 0; i < 3000; i++) sim.Step(0.02); // 60 s
            Vector3d p = sim.State.Position;
            double g = sim.Gravity.AccelerationAt(p).Magnitude;
            double cdA = 0.8 * 490.0 + sim.Drag.dragCoefficient * sim.Drag.referenceArea;
            double expected = PartPhysics.ParachuteDescentSpeed(sim.CurrentMass, g, sim.Planet.DensityAt(p), cdA);
            Assert.AreEqual(expected, sim.State.Velocity.Magnitude, 0.03 * expected, "steady descent v = √(2mg/(ρ·Cd·A))");
            Assert.AreEqual(0, sim.Systems.CanopiesIn(CanopyState.Torn));
        }

        [Test]
        public void Parachute_ReleasedTooFast_Tears_AndGivesNoDrag()
        {
            CompiledRocket c = Compile(b => b.payloadModules.Add(ModuleCatalog.ParachuteLarge));
            FreeFlightSimulation sim = Capsule(c, 10000.0, 400.0, out _); // 400 m/s > 180 m/s limit, q ≈ 33 kPa > 2.5 kPa
            sim.Systems.ReleaseParachutes();
            sim.Step(0.02);
            Assert.AreEqual(1, sim.Systems.CanopiesIn(CanopyState.Torn));
            Assert.AreEqual(0.0, sim.Systems.ParachuteDragArea);
            Assert.AreEqual(1, sim.Systems.Events.Count, "the pilot is told why");
            Assert.AreEqual(ParachuteCommand.AlreadyReleased, sim.Systems.ReleaseParachutes(), "a torn canopy cannot be packed again");
        }

        [Test]
        public void Parachute_ThinAirHighSpeed_Tears_VacuumWaitsLimp()
        {
            ModuleDefinition m = ModuleCatalog.Find(ModuleCatalog.ParachuteSmall);
            Assert.IsTrue(PartPhysics.ParachuteTears(10.0, 2000.0, m.parachuteMaxDynamicPressure, m.parachuteMaxSpeed), "thin air, orbital speed: flutters apart");
            Assert.IsFalse(PartPhysics.ParachuteTears(1000.0, 100.0, m.parachuteMaxDynamicPressure, m.parachuteMaxSpeed));

            CompiledRocket c = Compile(b => b.payloadModules.Add(ModuleCatalog.ParachuteSmall));
            var sim = TestPlanets.Simulation(TestPlanets.Earth(rotating: false, atmosphere: false), drag: true);
            var r = new RocketState(c.Configuration);
            while (r.HasStages) r.Separate();
            sim.Rocket = r;
            sim.Systems = new FlightSystems(c.Capabilities);
            sim.SetLocalState(0.0, 0.0, 200000.0, 7700.0, 0.0, 0.0, false, 0.0);
            sim.Systems.ReleaseParachutes();
            for (int i = 0; i < 100; i++) sim.Step(0.02);
            Assert.AreEqual(1, sim.Systems.CanopiesIn(CanopyState.Deployed), "no air: nothing loads it");
            Assert.AreEqual(0.0, sim.Systems.ParachuteOpening);
        }

        [Test]
        public void Parachute_EveryCatalogCanopy_HasStrengthLimits()
        {
            foreach (string id in new[] { ModuleCatalog.ParachuteSmall, ModuleCatalog.ParachuteMedium, ModuleCatalog.ParachuteLarge })
            {
                ModuleDefinition m = ModuleCatalog.Find(id);
                Assert.Greater(m.parachuteMaxDynamicPressure, 0.0, id);
                Assert.Greater(m.parachuteMaxSpeed, 0.0, id);
            }
        }

        // ---- Landing legs -----------------------------------------------------------------------------

        [Test]
        public void Legs_DesignWithoutLegs_KeepsTheOriginalLimits()
        {
            var systems = new FlightSystems(Compile(null).Capabilities);
            Assert.IsFalse(systems.DesignHasLegs);
            LandingOutcome old = LandingPhysics.Assess(-5.0, 1.0, 2.0, 3.0);
            LandingOutcome now = systems.AssessLanding(0, -5.0, 1.0, 2.0, 3.0);
            Assert.AreEqual(old.Result, now.Result);
            Assert.AreEqual(old.Reason, now.Reason);
            Assert.AreEqual(LandingResult.Soft, now.Result);
        }

        [Test]
        public void Legs_Deployed_Retracted_Dropped()
        {
            CompiledRocket c = Compile(b => b.stages[1].attachments.Add(ModuleCatalog.LegsLight)); // 3 m/s legs on stage 2
            var s = new FlightSystems(c.Capabilities);
            Assert.IsTrue(s.DesignHasLegs);
            Assert.AreEqual(4, s.Hardware(1).LegCount);

            s.LegsDeployed = true;
            Assert.AreEqual(LandingResult.Soft, s.AssessLanding(1, -2.5, 0.5, 2.0, 2.0).Result, "on the legs: soft up to 3 m/s");
            Assert.AreEqual(LandingResult.Hard, s.AssessLanding(1, -5.0, 0.5, 2.0, 2.0).Result);
            Assert.AreEqual(LandingResult.Crash, s.AssessLanding(1, -8.0, 0.5, 2.0, 2.0).Result, "crash above 3 × 2.5");

            s.LegsDeployed = false;
            Assert.AreEqual(LandingResult.Hard, s.AssessLanding(1, -2.5, 0.5, 2.0, 2.0).Result, "retracted: the hull takes it (1.5 m/s)");

            s.LegsDeployed = true;
            Assert.AreEqual(0, s.Hardware(2).LegCount, "stage 2 dropped");
            Assert.AreEqual(LandingResult.Hard, s.AssessLanding(2, -2.5, 0.5, 2.0, 2.0).Result, "the legs went with their stage");
            Assert.AreEqual(LandingResult.Crash, s.AssessLanding(2, -4.0, 0.5, 2.0, 2.0).Result, "hull crash above 1.5 × 2.5");
            Assert.AreEqual(LandingResult.Crash, s.AssessLanding(1, -1.0, LandingPhysics.CrashHorizontal + 1.0, 2.0, 2.0).Result, "sideways limits stay");
        }

        // ---- RCS and fins -----------------------------------------------------------------------------

        [Test]
        public void Rcs_None_KeepsTheBaseRate_AndBurnsNothing()
        {
            CompiledRocket c = Compile(null);
            var r = new RocketState(c.Configuration);
            var s = new FlightSystems(c.Capabilities);
            double fuel = r.CurrentPropellant;
            Assert.AreEqual(12.0, s.CommandTurn(r, 12.0, 30000.0, true, 1.0), 1e-12);
            Assert.AreEqual(fuel, r.CurrentPropellant);
            Assert.IsFalse(s.RcsFiring);
        }

        [Test]
        public void Rcs_TurnsFaster_AndBurnsPropellant_AtIsp220()
        {
            CompiledRocket c = Compile(b => b.stages[1].attachments.Add(ModuleCatalog.RcsLarge));
            var r = new RocketState(c.Configuration);
            r.Separate(); // upper stage only: the RCS is on it
            var s = new FlightSystems(c.Capabilities);
            Assert.IsTrue(s.RcsEnabled, "on by default when installed");
            double fuel = r.CurrentPropellant;
            double rate = s.CommandTurn(r, 12.0, 0.0, true, 2.0);
            double inertia = PartPhysics.TransverseInertia(r.Mass, r.StackLength, r.Configuration.radius);
            double eps = 1800.0 * 0.5 * r.StackLength / inertia;
            Assert.AreEqual(12.0 + eps * PartPhysics.ControlRampTime * 180.0 / Math.PI, rate, 1e-9);
            Assert.Greater(rate, 12.0);
            Assert.AreEqual(1800.0 / (220.0 * G0) * 2.0, fuel - r.CurrentPropellant, 1e-9, "ṁ = F/(Isp·g₀)");
            Assert.IsTrue(s.RcsFiring);

            Assert.AreEqual(12.0, s.CommandTurn(r, 12.0, 0.0, false, 2.0), 1e-12, "no turn command: no firing");
            Assert.IsFalse(s.RcsFiring);
            s.RcsEnabled = false;
            double left = r.CurrentPropellant;
            Assert.AreEqual(12.0, s.CommandTurn(r, 12.0, 0.0, true, 2.0), 1e-12);
            Assert.AreEqual(left, r.CurrentPropellant, "switched off: burns nothing");
        }

        [Test]
        public void Rcs_NeverDrawsFromASolidGrain_AndStopsWhenDry()
        {
            var config = new RocketConfiguration
            {
                stages = new[]
                {
                    new RocketStage { name = "SRB", dryMass = 100, propellantMass = 1000, vacuumThrust = 5e4, vacuumIsp = 280, seaLevelIsp = 250, engineKind = EngineKind.Solid, length = 5 },
                    new RocketStage { name = "L", dryMass = 100, propellantMass = 0.5, vacuumThrust = 1e3, vacuumIsp = 300, seaLevelIsp = 250, length = 2 }
                },
                payloadMass = 100
            };
            var r = new RocketState(config);
            Assert.AreEqual(0.5, r.RcsPropellantAvailable, 1e-12);
            Assert.AreEqual(0.5, r.DrawRcsPropellant(2.0), 1e-12, "only the liquid stage feeds the RCS");
            Assert.AreEqual(1000.0, r.Propellant[0]);
            Assert.AreEqual(0.0, r.RcsPropellantAvailable);
        }

        [Test]
        public void GridFins_NeedDynamicPressure()
        {
            CompiledRocket c = Compile(b => b.stages[0].attachments.Add(ModuleCatalog.FinTopLarge));
            var r = new RocketState(c.Configuration);
            var s = new FlightSystems(c.Capabilities);
            Assert.AreEqual(0.0, s.ControlAngularAcceleration(r, PartPhysics.GridFinMinDynamicPressure * 0.5, false));
            Assert.IsFalse(s.GridFinsActive(r, 500.0));
            double eps = s.ControlAngularAcceleration(r, 20000.0, false);
            Assert.Greater(eps, 0.0);
            Assert.IsTrue(s.GridFinsActive(r, 20000.0));
            Assert.Greater(s.CommandTurn(r, 12.0, 20000.0, true, 0.1), 12.0);
        }

        [Test]
        public void SideFins_TurnTheNoseIntoTheAirflow_WithoutFinsNothingTurns()
        {
            double Final(Action<RocketBlueprint> edit)
            {
                CompiledRocket c = Compile(edit);
                var sim = TestPlanets.Simulation(TestPlanets.Earth(rotating: false, atmosphere: true), drag: true);
                sim.Rocket = new RocketState(c.Configuration) { PitchDegrees = 20.0, HeadingDegrees = 90.0 };
                sim.Systems = new FlightSystems(c.Capabilities);
                sim.SetLocalState(0.0, 0.0, 8000.0, 0.0, 0.0, 400.0, false, 0.0); // straight up, nose 20° off the flow
                for (int i = 0; i < 100; i++) sim.Step(0.02);
                return sim.Rocket.PitchDegrees;
            }
            Assert.AreEqual(20.0, Final(null), 1e-9, "no fins: the attitude is what the pilot holds");
            double finned = Final(b => b.stages[0].attachments.Add(ModuleCatalog.FinSideLarge));
            Assert.Less(finned, 19.0, "stable fins turn the nose towards the oncoming air");
            Assert.GreaterOrEqual(finned, 0.0);
        }

        [Test]
        public void OpenCanopy_HangsTheFinnedStageNoseUp()
        {
            // A finned stage falling nose first (pitch 170°): the open canopy at the top turns it nose up, against the fins.
            // A small spent stage (Sokol-1 on an empty 10 t tank), so the canopy holds it.
            CompiledRocket c = Compile(b =>
            {
                b.stages.Clear();
                b.stages.Add(new StageBlueprint
                {
                    name = "Ступень 1", engineId = ModuleCatalog.EngineSokol, engineCount = 1,
                    tanks = { new TankLoad { moduleId = ModuleCatalog.TankK10, propellantLoaded = 0 } },
                    attachments = { ModuleCatalog.FinSideLarge }
                });
                b.payloadModules.Add(ModuleCatalog.ParachuteLarge);
            });
            var sim = TestPlanets.Simulation(TestPlanets.Earth(rotating: false, atmosphere: true), drag: true);
            sim.Rocket = new RocketState(c.Configuration) { PitchDegrees = 170.0, HeadingDegrees = 90.0 };
            sim.Systems = new FlightSystems(c.Capabilities);
            sim.SetLocalState(0.0, 0.0, 3000.0, 0.0, 0.0, -20.0, false, 0.0);
            sim.Systems.ReleaseParachutes();
            for (int i = 0; i < 500; i++) sim.Step(0.02); // 10 s
            Assert.Greater(sim.Systems.ParachuteDragArea, 0.0, "the canopy holds: torn=" + sim.Systems.CanopiesIn(CanopyState.Torn));
            Assert.Less(sim.Rocket.PitchDegrees, 20.0, "under the canopy the vehicle hangs nose up");
        }

        [Test]
        public void Weathervane_RateFormula()
        {
            double q = 20000.0, s = 6.0, a = Math.PI * 1.85 * 1.85, arm = 20.0, inertia = 5e7, alpha = 0.1;
            double k = q * a * PartPhysics.FinNormalForceSlope(s, a) * arm;
            Assert.AreEqual(alpha * Math.Sqrt(k / inertia), PartPhysics.WeathervaneRate(q, s, a, arm, inertia, alpha), 1e-12);
            Assert.AreEqual(0.0, PartPhysics.WeathervaneRate(q, 0.0, a, arm, inertia, alpha));
        }

        // ---- Heat shield ------------------------------------------------------------------------------

        [Test]
        public void Heat_BelowTheHullLimit_HarmsNothing_AboveIt_TheShieldDecides()
        {
            var bare = new FlightSystems(Compile(null).Capabilities);
            var shielded = new FlightSystems(Compile(b => b.payloadModules.Add(ModuleCatalog.HeatShieldLight)).Capabilities);
            Assert.AreEqual(PartPhysics.HullHeatFluxLimit, bare.AllowedHeatFlux);
            Assert.AreEqual(1.5e6, shielded.AllowedHeatFlux);

            // A density/speed pair for a chosen flux: q̇ = k·√(ρ/r)·v³.
            double Speed(double flux, double rho, double r) => Math.Pow(flux / (AtmosphericHeating.SuttonGravesAir * Math.Sqrt(rho / r)), 1.0 / 3.0);
            const double rho0 = 2e-4, nose = 1.85;
            Assert.AreEqual(0.0, bare.UpdateHeat(1.0, rho0, Speed(0.9e6, rho0, nose), nose), "shallow return from low orbit: unchanged");
            double hurt = bare.UpdateHeat(1.0, rho0, Speed(1.3e6, rho0, nose), nose);
            Assert.AreEqual(PartPhysics.HeatDamageRate * 0.3, hurt, 1e-6);
            Assert.AreEqual(1, bare.Events.Count, "overheating is reported once");
            Assert.AreEqual(0.0, shielded.UpdateHeat(1.0, rho0, Speed(1.3e6, rho0, nose), nose), "the shield takes 1.5 MW/m²");
            Assert.Greater(shielded.UpdateHeat(1.0, rho0, Speed(3e6, rho0, nose), nose), 0.0, "beyond its limit the shield is not enough");
        }

        [Test]
        public void Heat_IsAccumulatedByTheSimulation()
        {
            CompiledRocket c = Compile(null);
            FreeFlightSimulation sim = Capsule(c, 60000.0, 11000.0, out _); // interplanetary return, straight down
            sim.Step(0.02);
            Assert.Greater(sim.Systems.HeatFlux, PartPhysics.HullHeatFluxLimit);
            double d = sim.TakeHeatDamage();
            Assert.Greater(d, 0.0);
            Assert.AreEqual(0.0, sim.TakeHeatDamage(), "taken once");
        }

        // ---- Power -----------------------------------------------------------------------------------

        [Test]
        public void Batteries_RunDown_OutsideBurns_AndSolarFluxScalesThePanels()
        {
            var config = new RocketConfiguration
            {
                stages = new[] { new RocketStage { name = "L", dryMass = 1000, propellantMass = 1000, vacuumThrust = 3e4, vacuumIsp = 300, seaLevelIsp = 250 } },
                payloadMass = 500, solarPower = 100.0, systemsPower = 300.0, batteryPower = 1000.0, batteryEnergy = 1e6
            };
            var sim = TestPlanets.Simulation(TestPlanets.Earth(rotating: false, atmosphere: false));
            sim.Rocket = new RocketState(config);
            sim.SetLocalState(0.0, 0.0, 500000.0, Math.Sqrt(TestPlanets.EarthMu / (TestPlanets.EarthRadius + 500000.0)), 0.0, 0.0, false, 0.0);
            for (int i = 0; i < 50; i++) sim.Step(2.0); // 100 s coasting
            Assert.AreEqual(1e6 - 200.0 * 100.0, sim.Rocket.BatteryEnergy, 1e-3, "300 W systems − 100 W panels for 100 s");

            sim.Rocket.SolarFluxRatio = PartPhysics.SolarFluxRatio(2.0, 1.0); // twice as far from the star: ¼ of the light
            Assert.AreEqual(25.0, sim.Rocket.Electrics.Generation, 1e-9);
        }

        // ---- Regression: old designs fly exactly as before ----------------------------------------------

        [Test]
        public void Systems_OfADesignWithoutNewParts_ChangeNothingInFlight()
        {
            CompiledRocket c = Compile(null);
            FreeFlightSimulation Fly(bool systems)
            {
                var sim = TestPlanets.Simulation(TestPlanets.Earth(rotating: true, atmosphere: true), drag: true);
                sim.Rocket = new RocketState(c.Configuration) { PitchDegrees = 30.0, HeadingDegrees = 90.0, EngineOn = true };
                if (systems) sim.Systems = new FlightSystems(c.Capabilities);
                sim.SetLocalState(0.3, 0.2, 2000.0, 50.0, 10.0, 150.0, true, 0.0);
                for (int i = 0; i < 2000; i++) sim.Step(0.02);
                return sim;
            }
            FreeFlightSimulation a = Fly(false), b = Fly(true);
            Assert.AreEqual(a.State.Position.x, b.State.Position.x);
            Assert.AreEqual(a.State.Position.y, b.State.Position.y);
            Assert.AreEqual(a.State.Position.z, b.State.Position.z);
            Assert.AreEqual(a.State.Velocity.x, b.State.Velocity.x);
            Assert.AreEqual(a.Rocket.CurrentPropellant, b.Rocket.CurrentPropellant);
            Assert.AreEqual(a.Rocket.PitchDegrees, b.Rocket.PitchDegrees);
        }

        // ---- Low-thrust time warp ----------------------------------------------------------------------

        static RocketConfiguration Hall() => new RocketConfiguration
        {
            stages = new[] { new RocketStage { name = "E", dryMass = 500.0, propellantMass = 500.0, vacuumThrust = 0.6, vacuumIsp = 2800.0, seaLevelIsp = 0.0,
                engineKind = EngineKind.Electric, electricPowerDemand = 1.0 } },
            payloadMass = 1000.0,
            steadyPower = 1.0
        };

        [Test]
        public void Warp_LowThrustOnSafeOrbit_UpTo10000_ChemicalStays100()
        {
            var s = new TimeWarpSituation { EngineBurning = true, ThrustAcceleration = 3e-4, Altitude = 500000, BodyRadius = 6.4e6, ZoneAltitude = 150000, SafeTrajectory = true };
            TimeWarpLimit low = TimeWarpPolicy.Evaluate(s);
            Assert.AreEqual(TimeWarpZone.LowThrustFlight, low.Zone);
            Assert.AreEqual(10000.0, low.MaxWarp);

            s.ThrustAcceleration = 15.0;
            Assert.AreEqual(TimeWarpPolicy.PoweredWarp, TimeWarpPolicy.Evaluate(s).MaxWarp, "a chemical engine keeps ×100");
            s.ThrustAcceleration = 0.0;
            Assert.AreEqual(TimeWarpPolicy.PoweredWarp, TimeWarpPolicy.Evaluate(s).MaxWarp, "unknown thrust: not counted as low");

            s.ThrustAcceleration = 3e-4;
            s.SafeTrajectory = false;
            Assert.AreEqual(TimeWarpPolicy.PoweredWarp, TimeWarpPolicy.Evaluate(s).MaxWarp, "periapsis in the zone: no speed-up");
            s.SafeTrajectory = true;
            s.Altitude = 100000;
            Assert.AreEqual(TimeWarpZone.UpperAtmosphere, TimeWarpPolicy.Evaluate(s).Zone, "inside the zone the zone rules");
            Assert.AreEqual(TimeWarpZone.LowThrustFlight, TimeWarpPolicy.Evaluate(new TimeWarpSituation { Interplanetary = true, EngineBurning = true, ThrustAcceleration = 1e-4 }).Zone);
        }

        [Test]
        public void Warp_LowThrustLongSteps_MatchFineSteps()
        {
            // RailsStep of a low-thrust burn is the same numerical integration in long sub-stepped pieces: ×10 000 for 0.1 s = 1000 s.
            FreeFlightSimulation Make()
            {
                var sim = TestPlanets.Simulation(TestPlanets.Earth(rotating: false, atmosphere: false));
                double r0 = TestPlanets.EarthRadius + 500000.0;
                sim.SetLocalState(0.0, 0.0, 500000.0, Math.Sqrt(TestPlanets.EarthMu / r0), 0.0, 0.0, false, 0.0);
                sim.Rocket = new RocketState(Hall()) { PitchDegrees = 90.0, HeadingDegrees = 90.0, EngineOn = true };
                return sim;
            }
            FreeFlightSimulation fine = Make(), coarse = Make();
            Assert.IsTrue(coarse.LowThrustBurnOnSafeTrajectory());
            for (int i = 0; i < 50000; i++) fine.Step(0.02);
            coarse.RailsStep(1000.0, 0.02);
            Assert.AreEqual(fine.State.Time, coarse.State.Time, 1e-6);
            Assert.AreEqual(0.0, (fine.State.Position - coarse.State.Position).Magnitude, 1.0, "within a metre after 1000 s");
            Assert.AreEqual(0.0, (fine.State.Velocity - coarse.State.Velocity).Magnitude, 1e-3);
            Assert.AreEqual(fine.Rocket.CurrentPropellant, coarse.Rocket.CurrentPropellant, 1e-9, "same propellant burned");
            Assert.Less(coarse.LastSubstepCount, coarse.MaxSubsteps);
            Assert.AreEqual(0.02, coarse.State.Time - coarse.PreviousState.Time, 1e-9, "previous state one render step before the end");
        }
    }
}
