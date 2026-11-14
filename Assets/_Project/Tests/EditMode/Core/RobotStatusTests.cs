using System;
using NUnit.Framework;
using SpaceSim.Exploration;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Tests
{
    /// <summary>The surface robot's condition: four independent values, worn by the environment and by impacts; exit rules.</summary>
    public class RobotStatusTests
    {
        static CelestialBodyPreset Preset(string id) => HomeSystemCatalog.Bodies().Find(b => b.Id == id);

        static RobotEnvironment Env(double pressureAtm = 1.0, double kelvin = 290.0, int acid = 0, int radiation = 0) =>
            new RobotEnvironment(pressureAtm * 101325.0, kelvin, acid, radiation);

        [Test]
        public void NewRobotIsIntactAndSafeAtHome()
        {
            var r = new RobotStatus();
            for (int i = 0; i < RobotStatus.ConditionCount; i++) Assert.AreEqual(1.0, r[(RobotCondition)i]);
            CelestialBodyPreset home = Preset(HomeSystemCatalog.HomeWorldId);
            RobotEnvironment env = RobotEnvironment.AtSurface(home.Physical, home.Hazards);
            r.Step(env, 3600.0);
            for (int i = 0; i < RobotStatus.ConditionCount; i++) Assert.AreEqual(1.0, r[(RobotCondition)i], ((RobotCondition)i).ToString());
            Assert.IsFalse(r.Disabled);
            Assert.IsFalse(r.Blind);
            Assert.IsTrue(double.IsPositiveInfinity(r.TimeToDisabled()));
            Assert.AreEqual("", RobotEva.Warning(env));
        }

        [Test]
        public void HeatWearsOnlyTheThermalProtection()
        {
            var r = new RobotStatus();
            RobotDamageRates d = r.Rates;
            r.Step(Env(kelvin: d.maxSafeTemperature + 100.0), 10.0);
            Assert.AreEqual(1.0 - 100.0 * d.thermalPerKelvinSecond * 10.0, r.Thermal, 1e-12);
            Assert.AreEqual(1.0, r.Integrity);
            Assert.AreEqual(1.0, r.Chemical);
            Assert.AreEqual(1.0, r.Visor);
            Assert.AreEqual(100.0 * d.thermalPerKelvinSecond, r.RateOf(RobotCondition.Thermal), 1e-15);
        }

        [Test]
        public void ColdBelowTheRangeAlsoWearsThermal()
        {
            var r = new RobotStatus();
            r.Step(Env(kelvin: r.Rates.minSafeTemperature - 20.0), 1.0);
            Assert.Less(r.Thermal, 1.0);
            var unknown = new RobotStatus();
            unknown.Step(Env(kelvin: 0.0), 100.0);
            Assert.AreEqual(1.0, unknown.Thermal, "temperature 0 = no data, no load");
        }

        [Test]
        public void PressureAboveTwoAtmospheresWearsTheHull()
        {
            var r = new RobotStatus();
            double over = 5.0 * 101325.0 - r.Rates.safePressure;
            r.Step(Env(pressureAtm: 5.0), 20.0);
            Assert.AreEqual(1.0 - over * r.Rates.integrityPerPascalSecond * 20.0, r.Integrity, 1e-12);
            Assert.AreEqual(1.0, r.Thermal);
        }

        [Test]
        public void AcidWearsChemicalAndPartlyTheVisor()
        {
            var r = new RobotStatus();
            double rate = r.Rates.chemicalPerLevel[1];
            r.Step(Env(acid: 1), 30.0);
            Assert.AreEqual(1.0 - rate * 30.0, r.Chemical, 1e-12);
            Assert.AreEqual(1.0 - rate * r.Rates.visorChemicalShare * 30.0, r.Visor, 1e-12);
            Assert.AreEqual(1.0, r.Integrity);
            Assert.AreEqual(1.0, r.Thermal);
        }

        [Test]
        public void RadiationWearsOnlyTheVisorAndBlindsWithoutDisabling()
        {
            var r = new RobotStatus();
            double rate = r.Rates.visorRadiationPerLevel[2];
            Assert.AreEqual(1.0 / rate, 300.0, 1e-9, "level 2 blinds in 5 min");
            r.Step(Env(radiation: 2), 1.0);
            Assert.AreEqual(1.0 / rate - 1.0, r.TimeToBlind(), 1e-6);
            r.Step(Env(radiation: 2), 1000.0);
            Assert.AreEqual(0.0, r.Visor);
            Assert.IsTrue(r.Blind);
            Assert.IsFalse(r.Disabled, "a blind robot still moves");
            Assert.AreEqual(1.0, r.Integrity);
        }

        [Test]
        public void LevelsAboveTheTableUseTheLastEntry()
        {
            var r = new RobotStatus();
            r.Step(Env(radiation: 9), 1.0);
            double[] t = r.Rates.visorRadiationPerLevel;
            Assert.AreEqual(t[t.Length - 1], r.RateOf(RobotCondition.Visor), 1e-15);
        }

        [Test]
        public void ImpactsBelowTheSafeSpeedAreHarmless()
        {
            var r = new RobotStatus();
            Assert.AreEqual(0.0, r.Impact(r.Rates.safeImpactSpeed));
            Assert.AreEqual(0, r.Impacts);
            Assert.AreEqual(1.0, r.Integrity);
        }

        [Test]
        public void HardImpactDamagesHullAndCracksTheVisor()
        {
            var r = new RobotStatus();
            double v = r.Rates.safeImpactSpeed + 5.0;
            double dmg = r.Impact(v);
            Assert.AreEqual(5.0 * r.Rates.integrityPerImpactSpeed, dmg, 1e-12);
            Assert.AreEqual(1.0 - dmg, r.Integrity, 1e-12);
            Assert.AreEqual(1.0 - dmg * r.Rates.visorImpactShare, r.Visor, 1e-12);
            Assert.AreEqual(1, r.Impacts);
            Assert.AreEqual(v, r.LastImpactSpeed);
            Assert.AreEqual(1.0, r.Thermal);
            Assert.AreEqual(1.0, r.Chemical);
        }

        [Test]
        public void VehicleCrashWrecksTheRobotAndResetRestoresIt()
        {
            var r = new RobotStatus();
            r.VehicleDamage(LandingPhysics.HardLandingDamage);
            Assert.AreEqual(1.0 - LandingPhysics.HardLandingDamage, r.Integrity, 1e-12);
            Assert.IsFalse(r.Disabled);
            r.VehicleDamage(1.0);
            Assert.IsTrue(r.Disabled);
            Assert.IsFalse(RobotEva.CanExit(Preset(HomeSystemCatalog.HomeWorldId).Physical, Preset(HomeSystemCatalog.HomeWorldId).Hazards, r).Ok);
            r.Reset();
            Assert.IsFalse(r.Disabled);
            Assert.AreEqual(1.0, r.Integrity);
            Assert.AreEqual(1.0, r.Visor);
            Assert.AreEqual(0, r.Impacts);
        }

        [Test]
        public void ValuesAreIndependentAndClamped()
        {
            var r = new RobotStatus();
            r.Damage(RobotCondition.Chemical, 2.0);
            Assert.AreEqual(0.0, r.Chemical);
            Assert.AreEqual(1.0, r.Integrity);
            Assert.IsTrue(r.Disabled);
            r.Damage(RobotCondition.Visor, -1.0);
            Assert.AreEqual(1.0, r.Visor, "negative damage never repairs");
            r.Set(RobotCondition.Thermal, 1.5);
            Assert.AreEqual(1.0, r.Thermal);
        }

        [Test]
        public void TimeToDisabledIsTheFastestWearingValue()
        {
            var r = new RobotStatus();
            r.Step(Env(pressureAtm: 1.0, kelvin: r.Rates.maxSafeTemperature + 100.0, acid: 1), 0.0);
            double thermal = 1.0 / (100.0 * r.Rates.thermalPerKelvinSecond), chemical = 1.0 / r.Rates.chemicalPerLevel[1];
            Assert.AreEqual(Math.Min(thermal, chemical), r.TimeToDisabled(), 1e-6);
        }

        [Test]
        public void RatesAreDataAndCanBeTuned()
        {
            var rates = RobotDamageRates.Default.Clone();
            rates.thermalPerKelvinSecond *= 2.0;
            rates.chemicalPerLevel[1] = 0.5;
            var r = new RobotStatus(rates);
            r.Step(Env(acid: 1), 1.0);
            Assert.AreEqual(0.5, r.Chemical, 1e-12);
            Assert.AreNotEqual(0.5, RobotDamageRates.Default.chemicalPerLevel[1], "the clone does not share arrays");
        }

        [Test]
        public void SurfaceEnvironmentFollowsTheHazardModel()
        {
            // Teonan: no air, 178 °C, radiation 2 → reaches the ground. Home: air shields its radiation.
            CelestialBodyPreset teonan = Preset(HomeSystemCatalog.TeonanId);
            RobotEnvironment t = RobotEnvironment.AtSurface(teonan.Physical, teonan.Hazards);
            Assert.AreEqual(0.0, t.Pressure);
            Assert.AreEqual(2, t.RadiationLevel);
            Assert.AreEqual(teonan.Physical.atmosphere.surfaceTemperature, t.Temperature, 1e-9);

            CelestialBodyPreset mora = Preset(HomeSystemCatalog.HermeousMoraId);
            RobotEnvironment m = RobotEnvironment.AtSurface(mora.Physical, mora.Hazards);
            Assert.AreEqual(1, m.AcidLevel, "acid acts inside the air");
            Assert.AreEqual(0, m.RadiationLevel, "the dense air shields the ground");
            Assert.AreEqual(mora.Physical.atmosphere.surfacePressure, m.Pressure, 1e-6);
        }

        [Test]
        public void RobotExitRulesAreWiderThanTheSuitButFinite()
        {
            CelestialBodyPreset home = Preset(HomeSystemCatalog.HomeWorldId), teonan = Preset(HomeSystemCatalog.TeonanId);
            CelestialBodyPreset mora = Preset(HomeSystemCatalog.HermeousMoraId), tholin = Preset(HomeSystemCatalog.TholinId);
            Assert.IsTrue(RobotEva.CanExit(home.Physical, home.Hazards).Ok);
            Assert.IsFalse(SampleCollection.CanExit(teonan.Physical, teonan.Hazards).Ok, "a suit may not go out on Teonan…");
            Assert.IsTrue(RobotEva.CanExit(teonan.Physical, teonan.Hazards).Ok, "…the robot may, and its visor wears");
            StringAssert.Contains("Визор", RobotEva.Warning(RobotEnvironment.AtSurface(teonan.Physical, teonan.Hazards)));
            StringAssert.Contains("Давление", RobotEva.CanExit(mora.Physical, mora.Hazards).Reason, "74 atm is above the 10 atm hull rating");
            Assert.IsFalse(RobotEva.CanExit(tholin.Physical, tholin.Hazards).Ok, "60 bar");
        }

        [Test]
        public void ExitLimitsFollowTheData()
        {
            var planet = new PlanetPhysicalProperties();
            planet.atmosphere.surfacePressure = 5.0 * 101325.0;
            planet.atmosphere.surfaceTemperature = 600.0;
            var acid = new EnvironmentHazards { acidLevel = 1 };
            Assert.IsTrue(RobotEva.CanExit(planet, acid).Ok, "5 atm, 327 °C, acid 1: allowed, with wear");
            var strict = RobotDamageRates.Default.Clone();
            strict.maxExitTemperature = 500.0;
            StringAssert.Contains("Температура", RobotEva.CanExit(planet, acid, null, strict).Reason);
            Assert.IsFalse(RobotEva.CanExit(planet, new EnvironmentHazards { acidLevel = 2 }).Ok);
            planet.atmosphere.surfaceTemperature = 40.0;
            Assert.IsFalse(RobotEva.CanExit(planet, null).Ok, "colder than 60 K");
        }

        [Test]
        public void WalkerReportsTetherImpacts()
        {
            // A sprint into the end of the tether is a collision the robot feels.
            const double mu = 3.986e14, radius = 6.371e6;
            var start = new Vector3d(1, 0, 0);
            var w = new SurfaceWalker(mu, radius, null, start);
            w.PlaceAt(start * radius);
            Assert.AreEqual(0, w.Impacts);
            for (int i = 0; i < 20000 && w.Impacts == 0; i++) w.Step(0.02, 1.0, 0.0, 0.0, true, false);
            Assert.Greater(w.Impacts, 0, "stopped by the tether at " + SurfaceWalker.TetherLength + " m");
            Assert.Greater(w.LastImpactSpeed, 0.05);
            Assert.AreEqual(SurfaceWalker.TetherLength, w.DistanceToAnchor, 0.5);
        }
    }
}
