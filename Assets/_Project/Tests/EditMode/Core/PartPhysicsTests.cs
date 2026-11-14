using System;
using NUnit.Framework;
using SpaceSim.Physics;

namespace SpaceSim.Tests
{
    public class PartPhysicsTests
    {
        [Test]
        public void ParachuteSpeed_IsWhereDragEqualsWeight()
        {
            const double m = 5300, g = 9.81, rho = 1.2, cd = 0.8, area = 490;
            double v = PartPhysics.ParachuteDescentSpeed(m, g, rho, cd, area);
            Assert.AreEqual(Math.Sqrt(2.0 * m * g / (rho * cd * area)), v, 1e-12);
            Assert.AreEqual(m * g, 0.5 * rho * v * v * cd * area, 1e-6, "drag = weight");
            Assert.AreEqual(v / Math.Sqrt(2.0), PartPhysics.ParachuteDescentSpeed(m, g, rho, cd, 2.0 * area), 1e-9, "twice the canopy");
            Assert.AreEqual(v * Math.Sqrt(2.0), PartPhysics.ParachuteDescentSpeed(m, g, rho / 2.0, cd, area), 1e-9, "half the air");
            Assert.AreEqual(v, PartPhysics.ParachuteDescentSpeed(m, g, rho, cd * area), 1e-12, "C_d·A form");
        }

        [Test]
        public void ParachuteSpeed_IsInfiniteWithoutAirOrCanopy()
        {
            Assert.IsTrue(double.IsPositiveInfinity(PartPhysics.ParachuteDescentSpeed(1000, 9.81, 0.0, 0.8, 490)));
            Assert.IsTrue(double.IsPositiveInfinity(PartPhysics.ParachuteDescentSpeed(1000, 9.81, 1.2, 0.8, 0.0)));
            Assert.AreEqual(0.0, PartPhysics.ParachuteDescentSpeed(0.0, 9.81, 1.2, 0.8, 490));
        }

        [Test]
        public void Touchdown_UsesTheLegsLimit()
        {
            Assert.AreEqual(LandingResult.Soft, PartPhysics.AssessTouchdown(3.0, 4, 3.0).Result, "exactly at the limit");
            Assert.AreEqual(LandingResult.Hard, PartPhysics.AssessTouchdown(3.1, 4, 3.0).Result);
            double crash = 3.0 * PartPhysics.CrashToSoftRatio;
            Assert.AreEqual(LandingResult.Hard, PartPhysics.AssessTouchdown(crash, 4, 3.0).Result);
            Assert.AreEqual(LandingResult.Crash, PartPhysics.AssessTouchdown(crash + 0.1, 4, 3.0).Result);
            Assert.AreEqual(2.5, PartPhysics.CrashToSoftRatio, 1e-12, "same ratio as the flight limits");
            Assert.IsTrue(PartPhysics.AssessTouchdown(1.0, 4, 3.0).OnLegs);
            Assert.AreEqual(LandingResult.Soft, PartPhysics.AssessTouchdown(-5.0, 4, 3.0).Result, "going up is not a touchdown");
        }

        [Test]
        public void Touchdown_WithoutEnoughLegs_IsOnTheHull()
        {
            TouchdownCheck noLegs = PartPhysics.AssessTouchdown(2.0, 0, 0.0);
            Assert.IsFalse(noLegs.OnLegs);
            Assert.AreEqual(PartPhysics.HullTouchdownSpeed, noLegs.SoftLimit, 1e-12);
            Assert.AreEqual(LandingResult.Hard, noLegs.Result);
            Assert.AreEqual(LandingResult.Crash, PartPhysics.AssessTouchdown(5.0, 0, 0.0).Result);
            Assert.AreEqual(LandingResult.Hard, PartPhysics.AssessTouchdown(2.0, 2, 6.0).Result, "two legs cannot hold a vehicle up");
            Assert.AreEqual(LandingResult.Soft, PartPhysics.AssessTouchdown(2.0, 3, 6.0).Result);
        }

        [Test]
        public void Fins_MoveThePressureCentreDown_InProportionToTheirArea()
        {
            const double bodyArea = 10.75, bodyCp = 40, finHeight = 2, mass = 18, d = 3.7;
            double bare = PartPhysics.StaticMarginCalibers(mass, bodyCp, finHeight, 0.0, bodyArea, d);
            Assert.AreEqual((mass - bodyCp) / d, bare, 1e-12, "no fins: the pressure centre of the body");
            Assert.Less(bare, 0.0, "a bare rocket is unstable");

            double small = PartPhysics.StaticMarginCalibers(mass, bodyCp, finHeight, 2.0, bodyArea, d);
            double large = PartPhysics.StaticMarginCalibers(mass, bodyCp, finHeight, 20.0, bodyArea, d);
            Assert.Greater(small, bare);
            Assert.Greater(large, small);
            Assert.Less(PartPhysics.CenterOfPressure(bodyCp, finHeight, 20.0, bodyArea), bodyCp);
            Assert.Greater(PartPhysics.CenterOfPressure(bodyCp, finHeight, 20.0, bodyArea), finHeight, "between the body and the fins");
            Assert.AreEqual(2.0, PartPhysics.FinNormalForceSlope(2.0, bodyArea) / PartPhysics.FinNormalForceSlope(1.0, bodyArea), 1e-12);
        }

        [Test]
        public void FinTorque_IsLinearInEveryFactor()
        {
            double t = PartPhysics.FinRestoringTorque(1000, 6, 10.75, 0.1, 15);
            Assert.Greater(t, 0.0);
            Assert.AreEqual(2.0 * t, PartPhysics.FinRestoringTorque(2000, 6, 10.75, 0.1, 15), 1e-9);
            Assert.AreEqual(2.0 * t, PartPhysics.FinRestoringTorque(1000, 12, 10.75, 0.1, 15), 1e-9);
            Assert.AreEqual(2.0 * t, PartPhysics.FinRestoringTorque(1000, 6, 10.75, 0.2, 15), 1e-9);
            Assert.AreEqual(2.0 * t, PartPhysics.FinRestoringTorque(1000, 6, 10.75, 0.1, 30), 1e-9);
        }

        [Test]
        public void RcsTorque_IsThrustTimesArm()
        {
            Assert.AreEqual(1800.0 * 4.0, PartPhysics.RcsTorque(1800, 4), 1e-12);
            Assert.AreEqual(1800.0 * 4.0, PartPhysics.RcsTorque(1800, -4), 1e-12, "the arm is a distance");
            Assert.AreEqual(0.0, PartPhysics.RcsTorque(0, 4));
        }

        [Test]
        public void CommRange_FollowsTheInverseSquareLaw()
        {
            Assert.AreEqual(2e9, PartPhysics.CommRange(1e9, 10, 40), 1e-3, "four times the power, twice the reach");
            Assert.AreEqual(1e9, PartPhysics.CommRange(1e9, 10, 10), 1e-3);
            Assert.AreEqual(0.0, PartPhysics.CommRange(1e9, 10, 0));
            Assert.AreEqual(1.0, PartPhysics.SignalStrength(1e9, 5e8), 1e-12, "inside the range: full");
            Assert.AreEqual(0.25, PartPhysics.SignalStrength(1e9, 2e9), 1e-12, "twice the distance, a quarter of the signal");
            Assert.AreEqual(0.0, PartPhysics.SignalStrength(0.0, 1e9));
            Assert.AreEqual(5.0, PartPhysics.CombineRange(3.0, 4.0), 1e-12, "received powers add");
        }

        [Test]
        public void ElectricBalance_LimitsTheThrustFraction()
        {
            PowerBalance enough = PartPhysics.ElectricBalance(20000, 500, 13700, 0, 0);
            Assert.AreEqual(1.0, enough.EngineFraction);
            Assert.AreEqual(19500.0, enough.AvailableForEngines, 1e-9);

            PowerBalance short1 = PartPhysics.ElectricBalance(7350, 500, 13700, 0, 0);
            Assert.AreEqual(0.5, short1.EngineFraction, 1e-3);

            Assert.AreEqual(0.0, PartPhysics.ElectricBalance(100, 500, 13700, 0, 0).EngineFraction, 1e-12, "systems eat everything");
            Assert.IsFalse(PartPhysics.ElectricBalance(100, 500, 13700, 0, 0).SystemsCovered);

            PowerBalance battery = PartPhysics.ElectricBalance(0, 0, 10000, 5000, 1e6);
            Assert.AreEqual(0.5, battery.EngineFraction, 1e-12);
            Assert.AreEqual(0.0, PartPhysics.ElectricBalance(0, 0, 10000, 5000, 0.0).EngineFraction, 1e-12, "a flat battery gives nothing");
            Assert.AreEqual(1.0, PartPhysics.ElectricBalance(0, 0, 0, 0, 0).EngineFraction, "no thruster: nothing to limit");
        }

        [Test]
        public void SolarFlux_FallsAsTheSquareOfTheDistance()
        {
            Assert.AreEqual(1.0, PartPhysics.SolarFluxRatio(1.5e11, 1.5e11), 1e-12);
            Assert.AreEqual(0.25, PartPhysics.SolarFluxRatio(3e11, 1.5e11), 1e-12);
            Assert.AreEqual(4.0, PartPhysics.SolarFluxRatio(0.75e11, 1.5e11), 1e-12);
        }

        [Test]
        public void Wheels_CarryOnlyWhatTheirLoadAllows()
        {
            Assert.IsTrue(PartPhysics.WheelsCarry(4800, 1000, 3.7), "4 small wheels on a small world");
            Assert.IsFalse(PartPhysics.WheelsCarry(4800, 1000, 9.81), "the same rover on a big one");
        }
    }
}
