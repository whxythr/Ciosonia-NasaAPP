using System;
using NUnit.Framework;
using SpaceSim.Exploration;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Rover;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    public class RoverTests
    {
        const double Moon = 1.62, Earth = 9.81;

        static RoverSpec Rover(double mass = 600, double wheelLoad = 4 * 8000, double maxSpeed = 5, double power = 300) =>
            new RoverSpec(mass, 4, wheelLoad, maxSpeed, power);

        static double Drive(RoverSpec spec, double g, double gradeDeg, double throttle, double seconds, out RoverStep last, RoverMotion motion = null)
        {
            motion = motion ?? new RoverMotion();
            last = default;
            double grade = gradeDeg * MathD.DegToRad;
            for (double t = 0; t < seconds; t += 0.02) last = RoverKinematics.Step(motion, spec, g, grade, throttle, 0.0, 1.0, 0.02);
            return motion.Speed;
        }

        [Test]
        public void FlatGround_ReachesTheWheelRating_WhenPowerAllows()
        {
            RoverSpec spec = Rover(mass: 300);
            // Rolling resistance 0.05·300·1.62 ≈ 24 N needs 122 W at 5 m/s — the 225 W at the wheels are enough.
            Assert.AreEqual(5.0, RoverKinematics.MaxSpeed(spec, Moon, 0.0), 1e-9);
            Assert.AreEqual(5.0, Drive(spec, Moon, 0.0, 1.0, 90.0, out _), 0.05);
        }

        [Test]
        public void TopSpeed_IsPowerOverForce_AndFallsWithGradeAndGravity()
        {
            RoverSpec spec = Rover(mass: 2000);
            double need = RoverKinematics.ClimbResistance(2000, Moon, 10.0 * MathD.DegToRad);
            double expected = RoverKinematics.DriveEfficiency * 300.0 / need;
            Assert.AreEqual(expected, RoverKinematics.MaxSpeed(spec, Moon, 10.0 * MathD.DegToRad), 1e-9);
            Assert.Less(RoverKinematics.MaxSpeed(spec, Moon, 15.0 * MathD.DegToRad), RoverKinematics.MaxSpeed(spec, Moon, 5.0 * MathD.DegToRad));
            Assert.Less(RoverKinematics.MaxSpeed(spec, Earth, 5.0 * MathD.DegToRad), RoverKinematics.MaxSpeed(spec, Moon, 5.0 * MathD.DegToRad));
            double v = Drive(spec, Moon, 10.0, 1.0, 60.0, out RoverStep step);
            Assert.AreEqual(expected, v, 0.02 * expected, "the integrated speed settles at ηP/F");
            Assert.AreEqual(RoverDriveMode.PowerLimited, step.Mode);
            Assert.AreEqual(300.0, step.Power, 3.0, "the drive draws its full rating");
        }

        [Test]
        public void TooSteep_TheWheelsSpin_AndBeyondTheHoldLimitItSlidesBack()
        {
            RoverSpec spec = Rover();
            Assert.AreEqual(Math.Atan(RoverKinematics.Friction - RoverKinematics.RollingResistance) * MathD.RadToDeg, RoverKinematics.ClimbLimitDegrees, 1e-9);
            Assert.AreEqual(0.0, RoverKinematics.MaxSpeed(spec, Moon, (RoverKinematics.ClimbLimitDegrees + 1.0) * MathD.DegToRad));

            // Between the climb and the hold limits: no headway, but the locked wheels hold.
            double between = 0.5 * (RoverKinematics.ClimbLimitDegrees + RoverKinematics.HoldLimitDegrees);
            double v = Drive(spec, Moon, between, 1.0, 10.0, out RoverStep held);
            Assert.LessOrEqual(v, 0.05);
            Assert.AreEqual(RoverDriveMode.Slipping, held.Mode);

            // Steeper than atan μ: down it goes, whatever the driver does.
            v = Drive(spec, Moon, RoverKinematics.HoldLimitDegrees + 5.0, 1.0, 10.0, out RoverStep slid);
            Assert.Less(v, -0.5);
            Assert.AreEqual(RoverDriveMode.SlidingBack, slid.Mode);
        }

        [Test]
        public void ReleasedThrottle_BrakesToAStop_OnAGentleSlope()
        {
            var motion = new RoverMotion { Speed = 3.0 };
            Drive(Rover(), Moon, 5.0, 0.0, 10.0, out RoverStep step, motion);
            Assert.AreEqual(0.0, motion.Speed, 1e-6);
            Assert.AreEqual(RoverDriveMode.Stopped, step.Mode);
        }

        [Test]
        public void Overloaded_WheelsDoNotMove()
        {
            RoverSpec spec = Rover(mass: 10000, wheelLoad: 4 * 1200);
            Assert.IsFalse(PartPhysics.WheelsCarry(spec.WheelLoad, spec.Mass, Moon));
            Assert.AreEqual(0.0, RoverKinematics.MaxSpeed(spec, Moon, 0.0));
            double v = Drive(spec, Moon, 0.0, 1.0, 5.0, out RoverStep step);
            Assert.AreEqual(0.0, v);
            Assert.AreEqual(RoverDriveMode.Overloaded, step.Mode);
        }

        [Test]
        public void Energy_IsForceTimesDistanceOverEfficiency_OnFlatGround()
        {
            RoverSpec spec = Rover(mass: 300, maxSpeed: 3);
            var motion = new RoverMotion();
            Drive(spec, Moon, 0.0, 1.0, 30.0, out _, motion); // accelerate
            double e0 = motion.EnergyUsed, d0 = motion.Distance;
            Drive(spec, Moon, 0.0, 1.0, 20.0, out _, motion); // cruise
            double perMetre = (motion.EnergyUsed - e0) / (motion.Distance - d0);
            Assert.AreEqual(RoverKinematics.RollingResistance * 300 * Moon / RoverKinematics.DriveEfficiency, perMetre, 0.02 * perMetre);
        }

        [Test]
        public void NoPower_NoDrive()
        {
            var balance = new PowerBalance(0.0, 300.0, 0.0, 0.0, 0.0);
            Assert.AreEqual(0.0, RoverKinematics.SupplyFraction(balance));
            Assert.AreEqual(1.0, RoverKinematics.SupplyFraction(new PowerBalance(0.0, 300.0, 0.0, 1000.0, 5000.0)), "batteries cover it");
            Assert.AreEqual(0.5, RoverKinematics.SupplyFraction(new PowerBalance(150.0, 300.0, 0.0, 0.0, 0.0)), 1e-12);
            var motion = new RoverMotion();
            RoverKinematics.Step(motion, Rover(), Moon, 0.0, 1.0, 0.0, 0.0, 1.0);
            Assert.AreEqual(0.0, motion.Speed, 1e-12);
        }

        [Test]
        public void Steering_TurnsAtSpeedOverRadius()
        {
            var motion = new RoverMotion { Speed = 3.0, HeadingDegrees = 0.0 };
            RoverKinematics.Step(motion, Rover(), Moon, 0.0, 0.6, 1.0, 1.0, 0.1);
            double expected = 0.5 * (3.0 + motion.Speed) / RoverKinematics.MinTurnRadius * 0.1 * MathD.RadToDeg;
            Assert.AreEqual(expected, motion.HeadingDegrees, 1e-9);
        }

        [Test]
        public void Spec_FromADesign_UsesTheCatalogData()
        {
            var design = RocketBlueprint.CreateDefault();
            design.payloadModules.AddRange(new[] { ModuleCatalog.RoverChassis, ModuleCatalog.RoverDrive, ModuleCatalog.WheelLarge, ModuleCatalog.WheelLarge,
                ModuleCatalog.WheelLarge, ModuleCatalog.WheelLarge });
            VehicleCapabilities caps = BlueprintCompiler.Compile(design).Capabilities;
            Assert.IsTrue(caps.RoverAssembled);
            RoverSpec spec = RoverSpec.From(caps, 1500);
            Assert.AreEqual(4, spec.WheelCount);
            Assert.AreEqual(32000.0, spec.WheelLoad, 1e-9);
            Assert.AreEqual(5.0, spec.WheelMaxSpeed, 1e-9);
            Assert.AreEqual(300.0, spec.DrivePower, 1e-9);
        }

        [Test]
        public void Terrain_GradeAndNormal_OnATiltedPlane()
        {
            var surface = new TiltedSurface(1.0e6, 0.2); // rises 0.2 m per metre to the north
            Vector3d up = PlanetRotation.DirectionFromLatLon(0.1, 0.3);
            Assert.AreEqual(Math.Atan(0.2), RoverTerrain.GradeAlong(surface, 1.0e6, up, 0.0), 2e-3, "uphill to the north");
            Assert.AreEqual(-Math.Atan(0.2), RoverTerrain.GradeAlong(surface, 1.0e6, up, 180.0), 2e-3);
            Assert.AreEqual(0.0, RoverTerrain.GradeAlong(surface, 1.0e6, up, 90.0), 2e-3);
            Vector3d n = RoverTerrain.Normal(surface, 1.0e6, up);
            Assert.AreEqual(Math.Atan(0.2), Math.Acos(Vector3d.Dot(n, up)), 2e-3);
            Vector3d next = RoverTerrain.Advance(up, 90.0, 10.0, 1.0e6);
            Assert.AreEqual(10.0, (next - up).Magnitude * 1.0e6, 1e-3);
        }

        sealed class TiltedSurface : ISurfaceHeightProvider
        {
            readonly double radius, slope;
            public TiltedSurface(double r, double s) { radius = r; slope = s; }
            public double SurfaceHeight(Vector3d d)
            {
                PlanetRotation.LatLonFromDirection(d.Normalized, out double lat, out _);
                return lat * radius * slope;
            }
            public double MinSurfaceHeight => -1e9;
            public double MaxSurfaceHeight => 1e9;
        }

        // ---- Drills -------------------------------------------------------------------------------------------

        [Test]
        public void Drill_ShallowTakesSoil_DeepCutsRock_MassIsRhoAL()
        {
            Vector3d dir = PlanetRotation.DirectionFromLatLon(0.2, 1.1);
            DrillCore shallow = DrillCores.At("quoris", 3.4e6, dir, 0.05);
            Assert.IsNotNull(shallow);
            Assert.AreEqual(SampleKinds.Soil, shallow.Site.Required, "0.05 m never leaves the soil layer (≥ 0.08 m)");
            Assert.AreEqual(DrillCores.SoilDensity * Math.PI * DrillCores.CoreRadius * DrillCores.CoreRadius * 0.05, shallow.Site.Mass, 1e-12);

            DrillCore deep = DrillCores.At("quoris", 3.4e6, dir, 2.0);
            Assert.AreEqual(shallow.Site.Id, deep.Site.Id, "same cell — one hole");
            Assert.AreEqual(shallow.SoilDepth, deep.SoilDepth, 1e-12, "deterministic");
            Assert.AreNotEqual(0, (int)(deep.Site.Required & SampleKinds.Rock), "2 m reaches the bedrock");
            double expectedMass = (DrillCores.SoilDensity * deep.SoilDepth + DrillCores.RockDensity * (2.0 - deep.SoilDepth)) * Math.PI * DrillCores.CoreRadius * DrillCores.CoreRadius;
            Assert.AreEqual(expectedMass, deep.Site.Mass, 1e-9);
            Assert.Greater(deep.Energy, shallow.Energy);
        }

        [Test]
        public void Drill_CatalogCoresFitTheirDrills_AndGoThroughTheSampleRules()
        {
            Vector3d dir = PlanetRotation.DirectionFromLatLon(-0.4, 2.0);
            ModuleDefinition small = ModuleCatalog.Find(ModuleCatalog.DrillSmall), large = ModuleCatalog.Find(ModuleCatalog.DrillLarge);
            DrillCore s = DrillCores.At("teonan", 2.0e6, dir, small.drillDepth);
            DrillCore l = DrillCores.At("teonan", 2.0e6, dir, large.drillDepth);
            Assert.LessOrEqual(s.Site.Mass, small.sampleCapacity);
            Assert.LessOrEqual(l.Site.Mass, large.sampleCapacity);

            var design = RocketBlueprint.CreateDefault();
            design.payloadModules.Add(ModuleCatalog.DrillSmall);
            VehicleCapabilities caps = BlueprintCompiler.Compile(design).Capabilities;
            Assert.IsTrue(DrillCores.CanDrill(caps).Ok);
            Assert.IsTrue(SampleCollection.Check(s.Site, caps, 0.0, null).Ok, "the small drill alone takes its soil core");
            Assert.IsFalse(DrillCores.CanDrill(BlueprintCompiler.Compile(RocketBlueprint.CreateDefault()).Capabilities).Ok);
            Assert.AreEqual(150.0, DrillCores.DrillPower(design), 1e-12);
        }

        [Test]
        public void Drill_Time_IsEnergyOverPower()
        {
            double e = DrillCores.CuttingEnergy(0.3, 1.7);
            Assert.AreEqual(Math.PI * DrillCores.CoreRadius * DrillCores.CoreRadius * DrillCores.KerfFactor *
                            (DrillCores.SoilCuttingEnergy * 0.3 + DrillCores.RockCuttingEnergy * 1.7), e, 1e-9);
            Assert.AreEqual(DrillCores.HandlingTime + e / (DrillCores.DrillEfficiency * 500.0), DrillCores.DrillTime(e, 500.0), 1e-9);
            Assert.IsTrue(double.IsPositiveInfinity(DrillCores.DrillTime(e, 500.0, 0.0)));
            Assert.IsNull(DrillCores.At("home", 6.0e6, Vector3d.UnitX, 1.0, d => true), "no drilling in water");
        }

        // ---- Lamps --------------------------------------------------------------------------------------------

        [Test]
        public void Lamp_IntensityIsFluxOverSolidAngle()
        {
            double i = LampPhotometry.Intensity(1500.0, 60.0);
            Assert.AreEqual(1500.0 / (2.0 * Math.PI * (1.0 - Math.Cos(Math.PI / 6.0))), i, 1e-9);
            Assert.AreEqual(i / 25.0, LampPhotometry.Illuminance(i, 5.0), 1e-12);
            Assert.AreEqual(5.0, LampPhotometry.Reach(i, i / 25.0), 1e-9);
            Assert.AreEqual(LampPhotometry.FloodBeamDegrees, LampPhotometry.BeamAngleDegrees(1500.0));
            Assert.AreEqual(LampPhotometry.SearchlightBeamDegrees, LampPhotometry.BeamAngleDegrees(20000.0));
            Assert.Greater(LampPhotometry.Intensity(1000.0, 30.0), LampPhotometry.Intensity(1000.0, 60.0), "a narrower beam is brighter on its axis");
        }
    }
}
