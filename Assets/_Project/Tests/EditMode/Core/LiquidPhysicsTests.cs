using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceSim.Liquids;
using SpaceSim.Mathematics;
using SpaceSim.Planets;

namespace SpaceSim.Tests
{
    /// <summary>Shared fixtures of the liquid tests (planet environments from the home-system catalog).</summary>
    internal static class LiquidFixtures
    {
        public static CelestialBodyPreset Body(string id) => HomeSystemCatalog.Bodies().Find(b => b.Id == id);

        public static LiquidEnvironment Planet(string id, double? temperature = null) =>
            LiquidEnvironment.FromPlanet(Body(id).Physical, 0.0, temperature, id);

        public static LiquidEnvironment Home(double? temperature = null) => Planet(HomeSystemCatalog.HomeWorldId, temperature);

        public static LiquidSimulationSettings Cells(double size = 0.1) =>
            new LiquidSimulationSettings { cellWidth = size, cellHeight = size, cellDepth = size };

        public static LiquidSimulation Sim(int width, int height, LiquidEnvironment env, LiquidSimulationSettings settings, params string[] liquids) =>
            new LiquidSimulation(LiquidLibrary.CreateDefaultRegistry(), width, height, settings ?? Cells(), env, liquids);

        public static void Run(LiquidSimulation sim, double seconds, double dt)
        {
            int n = (int)Math.Round(seconds / dt);
            for (int i = 0; i < n; i++) sim.Step(dt);
        }
    }

    /// <summary>Liquid data, closed-form physics helpers, environments and the registry.</summary>
    public class LiquidPhysicsTests
    {
        static readonly LiquidRegistry Registry = LiquidLibrary.CreateDefaultRegistry();
        static LiquidDefinition L(string id) => Registry.Get(id);

        static readonly string[] StarterIds =
        {
            LiquidLibrary.Water, LiquidLibrary.SaltWater, LiquidLibrary.Brine, LiquidLibrary.Ethanol, LiquidLibrary.Glycerol,
            LiquidLibrary.CrudeOil, LiquidLibrary.Kerosene, LiquidLibrary.Mercury, LiquidLibrary.SulfuricAcid,
            LiquidLibrary.HydrochloricAcid, LiquidLibrary.SodiumHydroxide, LiquidLibrary.SodiumSulfateSolution,
            LiquidLibrary.LiquidMethane, LiquidLibrary.LiquidNitrogen, LiquidLibrary.LiquidAmmonia, LiquidLibrary.LiquidCarbonDioxide
        };

        /// <summary>Upper end of the liquid range used for property sweeps: the 1-atm boiling point, or T_c when there is none.</summary>
        static double LiquidUpperBound(LiquidDefinition d) =>
            d.IsLiquidPossibleAt(101325.0) ? d.normalBoilingPoint : d.criticalTemperature;

        // ───────────── data ─────────────

        [Test]
        public void Library_ContainsStarterSet_WithValidData()
        {
            foreach (string id in StarterIds)
            {
                Assert.IsTrue(Registry.Contains(id), id);
                Assert.IsEmpty(L(id).Validate(), id);
                Assert.IsFalse(string.IsNullOrEmpty(L(id).formula), id);
            }
            Assert.AreEqual(StarterIds.Length, Registry.Count);
            Assert.AreEqual(2, Registry.Reactions.Count);
            foreach (LiquidReaction r in Registry.Reactions) Assert.IsEmpty(r.Validate(), r.id);
        }

        [Test]
        public void Density_ReferenceValues_ThermalExpansion_AndOrdering()
        {
            LiquidDefinition water = L(LiquidLibrary.Water);
            Assert.AreEqual(998.2, water.Density(293.15), 1e-9);
            // ρ(100 °C) = 998.2 / (1 + 2.07e-4·80 + 4.10e-6·80²) = 998.2 / 1.04280 = 957.2 (steam tables 958.4).
            Assert.AreEqual(958.4, water.Density(373.15), 2.0);
            Assert.Less(L(LiquidLibrary.Ethanol).Density(313.15), L(LiquidLibrary.Ethanol).Density(293.15), "expands when heated");

            // 20 °C ordering: ethanol 789 < kerosene ≈ 796 < crude ≈ 846 < water 998 < salt water 1019 < glycerol 1261 < H₂SO₄ 1836 < Hg.
            double t = 293.15;
            Assert.Less(L(LiquidLibrary.Ethanol).Density(t), L(LiquidLibrary.Kerosene).Density(t));
            Assert.Less(L(LiquidLibrary.Kerosene).Density(t), L(LiquidLibrary.CrudeOil).Density(t));
            Assert.Less(L(LiquidLibrary.CrudeOil).Density(t), water.Density(t));
            Assert.Less(water.Density(t), L(LiquidLibrary.SaltWater).Density(t));
            Assert.Less(L(LiquidLibrary.SaltWater).Density(t), L(LiquidLibrary.Glycerol).Density(t));
            Assert.Less(L(LiquidLibrary.Glycerol).Density(t), L(LiquidLibrary.SulfuricAcid).Density(t));
            Assert.Greater(L(LiquidLibrary.Mercury).Density(t), 13500.0);
            Assert.AreEqual(806.1, L(LiquidLibrary.LiquidNitrogen).Density(77.36), 1e-9);
        }

        [Test]
        public void Density_IncreasesWithPressure_ThroughTheBulkModulus()
        {
            LiquidDefinition water = L(LiquidLibrary.Water);
            // 1000 bar: ρ/ρ₀ = exp((1e8 − 101325)/2.2e9) = exp(0.045408) = 1.0465 (measured ≈ 1.044).
            double ratio = water.Density(293.15, 1e8) / water.Density(293.15, 101325.0);
            Assert.AreEqual(Math.Exp((1e8 - 101325.0) / 2.2e9), ratio, 1e-12);
            Assert.That(ratio, Is.InRange(1.04, 1.05));
            Assert.Less(L(LiquidLibrary.Mercury).Density(293.15, 1e8) / L(LiquidLibrary.Mercury).Density(293.15, 101325.0), ratio,
                "mercury (K = 25 GPa) is ten times stiffer than water");
        }

        [Test]
        public void Viscosity_MatchesTables_AndFallsWithTemperature()
        {
            LiquidDefinition water = L(LiquidLibrary.Water), glycerol = L(LiquidLibrary.Glycerol);
            // Water, Vogel: 2.414e-5 · exp(570.58 / 153.15) = 2.414e-5 · 41.49 = 1.0016e-3 Pa·s (table 1.002e-3).
            Assert.AreEqual(1.002e-3, water.DynamicViscosity(293.15), 0.02e-3);
            // 100 °C: 2.414e-5 · exp(570.58 / 233.15) = 2.79e-4 (table 2.82e-4).
            Assert.AreEqual(0.282e-3, water.DynamicViscosity(373.15), 0.01e-3);
            // Glycerol, Vogel: ln μ = ln 4.32e-6 + 1756.7/138.35 = −12.352 + 12.697 = 0.345 → 1.412 Pa·s.
            Assert.AreEqual(1.412, glycerol.DynamicViscosity(293.15), 0.03);
            // 40 °C: −12.352 + 1756.7/158.35 = −1.258 → 0.284 Pa·s.
            Assert.AreEqual(0.284, glycerol.DynamicViscosity(313.15), 0.01);
            Assert.Greater(glycerol.DynamicViscosity(293.15) / water.DynamicViscosity(293.15), 1000.0);
            // Ethanol, Andrade: 4.267e-6 · exp(1649/293.15) = 1.18e-3 (table 1.20e-3).
            Assert.AreEqual(1.20e-3, L(LiquidLibrary.Ethanol).DynamicViscosity(293.15), 0.05e-3);
            Assert.AreEqual(1.554e-3, L(LiquidLibrary.Mercury).DynamicViscosity(293.15), 0.01e-3);

            foreach (string id in StarterIds)
            {
                LiquidDefinition d = L(id);
                double t0 = d.meltingPoint + 5.0;
                double t1 = Math.Min(LiquidUpperBound(d) - 5.0, t0 + 40.0);
                Assert.Less(d.DynamicViscosity(t1), d.DynamicViscosity(t0), id + ": viscosity falls with temperature");
                Assert.Less(d.SurfaceTension(t1), d.SurfaceTension(t0), id + ": surface tension falls with temperature");
                Assert.AreEqual(d.DynamicViscosity(t0) / d.Density(t0), d.KinematicViscosity(t0), 1e-15, id);
            }
        }

        [Test]
        public void BoilingPoint_DependsOnPlanetPressure()
        {
            LiquidDefinition water = L(LiquidLibrary.Water);
            LiquidEnvironment home = LiquidFixtures.Home();
            LiquidEnvironment quoris = LiquidFixtures.Planet(HomeSystemCatalog.QuorisId);
            LiquidEnvironment mora = LiquidFixtures.Planet(HomeSystemCatalog.HermeousMoraId);

            // Antoine at 101325 Pa: 1730.63 / (10.19621 − 5.00572) − (−39.724) = 373.15 K.
            Assert.AreEqual(373.15, water.BoilingPoint(home.AmbientPressure), 0.1);
            // Quoris, 100 Pa: 1730.63 / (10.19621 − 2) + 39.724 = 250.9 K (−22 °C), below the melting point:
            // liquid water cannot exist there (the fit's triple point is 605 Pa; real 611.657 Pa).
            Assert.AreEqual(250.9, water.BoilingPoint(quoris.AmbientPressure), 0.5);
            Assert.Less(water.BoilingPoint(quoris.AmbientPressure), water.meltingPoint);
            Assert.IsFalse(water.IsLiquidPossibleAt(quoris.AmbientPressure));
            Assert.IsTrue(water.IsLiquidPossibleAt(home.AmbientPressure));
            Assert.AreEqual(611.657, water.TriplePointPressure, 15.0);
            // Hermeous Mora, 75 bar: 1730.63 / (10.19621 − 6.87506) + 39.724 = 560.8 K (steam tables: 563.6 K = 290.5 °C).
            double tMora = water.BoilingPoint(mora.AmbientPressure);
            Assert.AreEqual(563.6, tMora, 5.0);
            Assert.Greater(tMora, 373.15 + 150.0, "water stays liquid far above 100 °C under 75 bar");

            // Clausius–Clapeyron (constant ΔH = 40.66 kJ/mol) gives 555.6 K at 75 bar: 1/T = 1/373.15 − R·ln(74.02)/40660.
            Assert.AreEqual(555.6, LiquidPhysics.ClausiusClapeyronBoilingPoint(373.15, 101325.0, 7.5e6, 40660.0), 1.0);

            foreach (string id in StarterIds)
            {
                LiquidDefinition d = L(id);
                if (d.IsLiquidPossibleAt(101325.0))
                    Assert.AreEqual(d.normalBoilingPoint, d.BoilingPoint(101325.0), 0.5, id + ": curve passes through the normal boiling point");
                Assert.Less(d.BoilingPoint(1e4), d.BoilingPoint(1e5), id);
                Assert.LessOrEqual(d.BoilingPoint(1e9), d.criticalTemperature, id + ": capped at the critical point");
                Assert.AreEqual(1e4, d.VaporPressure(d.BoilingPoint(1e4)), 1.0, id + ": vapour pressure is the inverse");
            }
            Assert.AreEqual(0.0, water.BoilingPoint(0.0), "hard vacuum: boils at any temperature");
            // Below the triple point the solid sublimates at T_s(p) < T_m.
            Assert.Less(water.SublimationPoint(100.0), water.meltingPoint);
            Assert.AreEqual(water.meltingPoint, water.SublimationPoint(1e5), 1e-12);
        }

        [Test]
        public void CarbonDioxide_LiquidOnlyBetweenTriplePointAndCriticalPoint_Tholin()
        {
            LiquidDefinition co2 = L(LiquidLibrary.LiquidCarbonDioxide);
            LiquidEnvironment tholin = LiquidFixtures.Planet(HomeSystemCatalog.TholinId);
            Assert.AreEqual(6.0e6, tholin.AmbientPressure, 1e3, "60 bar CO₂ atmosphere");

            // Antoine fit: log10(p/Pa) = 9.81383 − 916.8/(7.08 + T). At 60 bar: 916.8/(9.81383 − 6.77815) − 7.08 = 294.9 K
            // (tables: 295.1 K). It passes through the triple point (216.58 K, 5.185 bar) and the critical point (304.13 K, 73.77 bar).
            double tb = co2.BoilingPoint(tholin.AmbientPressure);
            Assert.AreEqual(295.0, tb, 2.0);
            Assert.AreEqual(216.58, co2.BoilingPoint(5.185e5), 0.05);
            Assert.AreEqual(7.3773e6, co2.VaporPressure(304.13), 0.01e6);
            Assert.AreEqual(5.185e5, co2.TriplePointPressure, 1e-6);

            // Tholin (60 bar): liquid below ≈ 295 K, not at the 310.95 K mean.
            Assert.IsTrue(co2.IsLiquidStableAt(280.0, tholin.AmbientPressure));
            Assert.IsTrue(co2.IsLiquidStableAt(tb - 1.0, tholin.AmbientPressure));
            Assert.IsFalse(co2.IsLiquidStableAt(tholin.AmbientTemperature, tholin.AmbientPressure));
            // Never liquid at 1 atm (below the triple point: dry ice sublimes) nor above T_c at any pressure.
            Assert.IsFalse(co2.IsLiquidPossibleAt(101325.0));
            Assert.IsFalse(co2.IsLiquidStableAt(250.0, 101325.0));
            Assert.IsFalse(co2.IsLiquidStableAt(305.0, 2e7));
            Assert.AreEqual(co2.criticalTemperature, co2.BoilingPoint(2e7), 1e-12, "boiling point capped at T_c");
            Assert.AreEqual(194.7, co2.SublimationPoint(101325.0), 4.0, "dry ice sublimes at −78.5 °C (194.7 K)");

            // Data: ρ 1030 (253 K), 930 (273 K); μ ≈ 0.1 mPa·s at 0 °C; L_v Watson-scaled: 15.3 kJ/mol at the triple point,
            // 15.3·((304.13 − 273.15)/(304.13 − 216.58))^0.38 = 15.3·0.674 = 10.3 kJ/mol at 0 °C, 0 at T_c.
            Assert.AreEqual(1030.0, co2.Density(253.15, 1.97e6), 1e-9);
            Assert.AreEqual(930.0, co2.Density(273.15, 1.97e6), 1.0);
            Assert.AreEqual(0.1e-3, co2.DynamicViscosity(273.15), 0.01e-3);
            Assert.AreEqual(15.3e3, co2.EnthalpyOfVaporizationAt(216.58) * co2.molarMass, 50.0);
            Assert.AreEqual(10.3e3, co2.EnthalpyOfVaporizationAt(273.15) * co2.molarMass, 200.0);
            Assert.AreEqual(0.0, co2.EnthalpyOfVaporizationAt(304.13));
            Assert.IsTrue(Registry.AreMiscible(LiquidLibrary.LiquidCarbonDioxide, LiquidLibrary.LiquidMethane));
            Assert.IsFalse(Registry.AreMiscible(LiquidLibrary.LiquidCarbonDioxide, LiquidLibrary.Water));
        }

        [Test]
        public void SurfaceTension_Capillarity_AndBondNumber()
        {
            LiquidDefinition water = L(LiquidLibrary.Water);
            Assert.AreEqual(0.0728, water.SurfaceTension(293.15), 1e-12);
            // 100 °C: 0.0728 − 1.74e-4 · 80 = 0.05888 N/m (table 0.0589).
            Assert.AreEqual(0.0589, water.SurfaceTension(373.15), 0.001);
            Assert.AreEqual(0.0, water.SurfaceTension(700.0), "no surface tension above the critical point");

            double g = LiquidFixtures.Home().GravityMagnitude;
            // Jurin: h = 2σ cos θ / (ρ g r) = 2·0.0728 / (998.2 · 8.27 · 5e-4) ≈ 0.035 m in a 1 mm glass tube.
            double h = LiquidPhysics.CapillaryRise(0.0728, 0.0, 998.2, g, 5e-4);
            Assert.AreEqual(2.0 * 0.0728 / (998.2 * g * 5e-4), h, 1e-12);
            Assert.That(h, Is.InRange(0.03, 0.04));
            Assert.IsTrue(double.IsPositiveInfinity(LiquidPhysics.CapillaryRise(0.0728, 0.0, 998.2, 0.0, 5e-4)), "wicks without limit in zero g");
            Assert.Less(LiquidPhysics.CapillaryRise(0.4855, Math.PI * 140.0 / 180.0, 13546.0, g, 5e-4), 0.0, "mercury is depressed");
            // A 1 cm water drop: Bo = 998.2 · 8.27 · 1e-4 / 0.0728 ≈ 11 (gravity wins); in orbit Bo = 0 (surface tension wins).
            Assert.Greater(LiquidPhysics.BondNumber(998.2, g, 0.01, 0.0728), 1.0);
            Assert.AreEqual(0.0, LiquidPhysics.BondNumber(998.2, 0.0, 0.01, 0.0728));
            Assert.AreEqual(2.0 * 0.0728 / 1e-3, LiquidPhysics.LaplacePressure(0.0728, 1e-3), 1e-12);
        }

        // ───────────── buoyancy / settling ─────────────

        [Test]
        public void Buoyancy_ArchimedesForce_ScalesWithGravity()
        {
            LiquidEnvironment home = LiquidFixtures.Home();
            LiquidEnvironment mora = LiquidFixtures.Planet(HomeSystemCatalog.HermeousMoraId);
            Vector3d fb = LiquidPhysics.BuoyantForce(998.2, 1.0, home.Gravity);
            Assert.AreEqual(998.2 * home.GravityMagnitude, fb.y, 1e-9, "1 m³ of displaced water, pointing up");
            Assert.AreEqual(0.0, fb.x, 0.0);

            Assert.Greater(LiquidPhysics.NetImmersedForce(500.0, 998.2, 1.0, home.Gravity).y, 0.0, "wood is pushed up");
            Assert.Less(LiquidPhysics.NetImmersedForce(7850.0, 998.2, 1.0, home.Gravity).y, 0.0, "steel sinks");
            Assert.Greater(LiquidPhysics.NetImmersedForce(7850.0, 13546.0, 1.0, home.Gravity).y, 0.0, "steel floats on mercury");
            // Net force on a 500 kg/m³ body: (998.2 − 500) · 1 · g upward.
            Assert.AreEqual(498.2 * home.GravityMagnitude, LiquidPhysics.NetImmersedForce(500.0, 998.2, 1.0, home.Gravity).y, 1e-9);

            Assert.AreEqual(500.0 / 998.2, LiquidPhysics.FloatingSubmergedFraction(500.0, 998.2, home.GravityMagnitude), 1e-12);
            Assert.AreEqual(1.0, LiquidPhysics.FloatingSubmergedFraction(7850.0, 998.2, home.GravityMagnitude));
            Assert.IsTrue(double.IsNaN(LiquidPhysics.FloatingSubmergedFraction(500.0, 998.2, 0.0)), "no up or down in free fall");

            double fHome = LiquidPhysics.BuoyantForce(998.2, 1.0, home.Gravity).Magnitude;
            double fMora = LiquidPhysics.BuoyantForce(998.2, 1.0, mora.Gravity).Magnitude;
            Assert.AreEqual(mora.GravityMagnitude / home.GravityMagnitude, fMora / fHome, 1e-12);
            Assert.Greater(fMora, fHome);
            Assert.AreEqual(0.0, LiquidPhysics.BuoyantForce(998.2, 1.0, Vector3d.Zero).Magnitude);
        }

        [Test]
        public void Settling_StokesAndTerminalVelocity()
        {
            const double g = 9.80665, muWater = 1.002e-3;
            // Quartz grain of radius 10 µm: v = 2/9 · (2650 − 998.2) · 9.80665 · 1e-10 / 1.002e-3
            //   = 0.2222 · 1651.8 · 9.80665 · 1e-10 / 1.002e-3 = 3.5998e-7 / 1.002e-3 = 3.593e-4 m/s.
            double v = LiquidPhysics.StokesVelocity(2650.0, 998.2, 1e-5, muWater, g);
            Assert.AreEqual(2.0 / 9.0 * 1651.8 * g * 1e-10 / muWater, v, 1e-15);
            Assert.AreEqual(3.593e-4, v, 0.002e-4);
            // Re = 998.2 · 3.6e-4 · 2e-5 / 1e-3 ≈ 7e-3 ≪ 1: the general law reduces to Stokes, slowed only by the
            // Schiller–Naumann factor 1 + 0.15·Re^0.687 ≈ 1.005.
            double re = 998.2 * v * 2e-5 / muWater;
            double vTerminal = LiquidPhysics.TerminalVelocity(2650.0, 998.2, 1e-5, muWater, g);
            Assert.AreEqual(v / (1.0 + 0.15 * Math.Pow(re, 0.687)), vTerminal, 0.001 * v);
            Assert.That(vTerminal / v, Is.InRange(0.99, 1.0));

            double vGly = LiquidPhysics.TerminalVelocity(2650.0, 1261.0, 1e-5, 1.412, g);
            Assert.Less(vGly, v / 1000.0, "settles over 1000× slower in glycerol");
            Assert.Less(LiquidPhysics.TerminalVelocity(850.0, 998.2, 1e-4, muWater, g), 0.0, "an oil droplet rises");
            Assert.AreEqual(0.0, LiquidPhysics.TerminalVelocity(2650.0, 998.2, 1e-5, muWater, 0.0), "no settling in zero g");
            Assert.AreEqual(14.46 / g, LiquidPhysics.StokesVelocity(2650.0, 998.2, 1e-5, muWater, 14.46) / v, 1e-12, "∝ g");

            // 5 mm steel ball (Re ≈ 1.4e4, Newton regime): v = √(8/3 · (Δρ/ρ) · g · r / 0.44) = √(2.667·6.864·9.807·0.005/0.44) ≈ 1.43 m/s,
            // far below the (invalid) Stokes value of ≈ 370 m/s.
            double vBall = LiquidPhysics.TerminalVelocity(7850.0, 998.2, 0.005, muWater, g);
            Assert.AreEqual(1.43, vBall, 0.05);
            Assert.Less(vBall, LiquidPhysics.StokesVelocity(7850.0, 998.2, 0.005, muWater, g) / 100.0);
        }

        [Test]
        public void PipeFlow_PoiseuilleAndInertialLimits()
        {
            // Hagen–Poiseuille: π r⁴ Δp / (8 μ L) with r = 1 mm, L = 1 m, Δp = 1000 Pa, μ = 1e-3 → 3.927e-7 m³/s.
            Assert.AreEqual(Math.PI * 1e-12 * 1000.0 / (8.0 * 1e-3 * 1.0), LiquidPhysics.PoiseuilleFlowRate(1000.0, 1e-3, 1.0, 1e-3), 1e-18);
            // Viscous-dominated: Q → Δp/b.  Inertia-dominated: Q → √(Δp/a).
            Assert.AreEqual(1e-3, LiquidPhysics.CombinedFlowRate(1.0, 1000.0, 1e-6), 1e-9);
            Assert.AreEqual(Math.Sqrt(100.0 / 1e6), LiquidPhysics.CombinedFlowRate(100.0, 0.0, 1e6), 1e-12);
            Assert.AreEqual(0.0, LiquidPhysics.CombinedFlowRate(-5.0, 1.0, 1.0));
            // 0.2 mm capillary, 1 m, 1 kPa: Poiseuille v = Q/A ≈ 1.2 mm/s, ½ρv² ≈ 8e-4 Pa ≪ Δp → laminar, so the combined law
            // equals Poiseuille; glycerol flows μ_g/μ_w ≈ 1400× slower.
            double qw = LiquidPhysics.PipeFlowRate(1000.0, 2e-4, 1.0, 1.002e-3, 998.2);
            double qg = LiquidPhysics.PipeFlowRate(1000.0, 2e-4, 1.0, 1.412, 1261.0);
            Assert.AreEqual(LiquidPhysics.PoiseuilleFlowRate(1000.0, 1e-4, 1.0, 1.002e-3), qw, 0.02 * qw);
            Assert.Greater(qw / qg, 1000.0);
            // A 1 mm × 1 m pipe at 1 kPa is still laminar: b = 128μL/(πd⁴) = 4.08e10 Pa·s/m³ ≫ √(4aΔp) = 2.2e9, so
            // Q = Poiseuille with r = 0.5 mm = π·6.25e-14·1000/(8·1.002e-3) = 2.45e-8 m³/s (v = 3 cm/s, Re ≈ 31).
            Assert.AreEqual(LiquidPhysics.PoiseuilleFlowRate(1000.0, 5e-4, 1.0, 1.002e-3), LiquidPhysics.PipeFlowRate(1000.0, 1e-3, 1.0, 1.002e-3, 998.2),
                0.02 * 2.45e-8);
            // A wide short orifice (d = 1 cm, L = 1 cm, 1 kPa) is inertia-limited instead: b = 4.1e4 ≪ √(4aΔp) = 2.2e7 with
            // a = 1.5ρ/(2A²) = 1.21e11, so Q ≈ A·√(2Δp/(1.5ρ)) = 7.854e-5 · 1.1557 = 9.08e-5 m³/s (Re ≈ 1.2e4),
            // far below the (invalid) Poiseuille estimate π·(5e-3)⁴·1000/(8·1.002e-3·0.01) ≈ 2.4e-2 m³/s.
            double area = 0.25 * Math.PI * 1e-4;
            double qOrifice = LiquidPhysics.PipeFlowRate(1000.0, 1e-2, 0.01, 1.002e-3, 998.2);
            Assert.AreEqual(area * Math.Sqrt(2.0 * 1000.0 / (1.5 * 998.2)), qOrifice, 0.02 * 9.08e-5);
            Assert.Less(qOrifice, LiquidPhysics.PoiseuilleFlowRate(1000.0, 5e-3, 0.01, 1.002e-3) / 100.0);
        }

        // ───────────── environment ─────────────

        [Test]
        public void Environment_FromPlanetData_DiffersPerPlanet()
        {
            CelestialBodyPreset homeBody = LiquidFixtures.Body(HomeSystemCatalog.HomeWorldId);
            LiquidEnvironment home = LiquidFixtures.Home();
            Assert.AreEqual(homeBody.Physical.SurfaceGravity, home.GravityMagnitude, 1e-9);
            Assert.AreEqual(-homeBody.Physical.SurfaceGravity, home.Gravity.y, 1e-9, "upright container: gravity along −Y");
            Assert.AreEqual(homeBody.Physical.atmosphere.surfacePressure, home.AmbientPressure, 1e-6);
            Assert.AreEqual(homeBody.Physical.atmosphere.surfaceTemperature, home.AmbientTemperature, 1e-9);
            Assert.AreEqual(8.27, home.GravityMagnitude, 0.05);
            Assert.IsTrue(home.HasAtmosphere);

            LiquidEnvironment mora = LiquidFixtures.Planet(HomeSystemCatalog.HermeousMoraId);
            LiquidEnvironment quoris = LiquidFixtures.Planet(HomeSystemCatalog.QuorisId);
            LiquidEnvironment teonan = LiquidFixtures.Planet(HomeSystemCatalog.TeonanId);
            Assert.Greater(mora.GravityMagnitude, home.GravityMagnitude);
            Assert.Greater(mora.AmbientPressure, 50.0 * home.AmbientPressure);
            Assert.Greater(mora.AmbientTemperature, 500.0);
            Assert.Less(quoris.AmbientPressure, 1000.0);
            Assert.Less(quoris.GravityMagnitude, home.GravityMagnitude);
            Assert.AreEqual(0.0, teonan.AmbientPressure, "airless");
            Assert.AreEqual(0.0, teonan.AmbientGasDensity);
            Assert.IsFalse(teonan.HasAtmosphere);
            Assert.Greater(teonan.AmbientTemperature, 0.0, "ground temperature still defined");

            LiquidEnvironment high = LiquidEnvironment.FromPlanet(homeBody.Physical, 5000.0);
            Assert.Less(high.AmbientPressure, home.AmbientPressure);
            Assert.Less(high.GravityMagnitude, home.GravityMagnitude);
            Assert.AreEqual(250.0, LiquidFixtures.Home(250.0).AmbientTemperature, "local temperature override");
            Assert.AreEqual(0.0, LiquidEnvironment.ZeroGravity(101325.0, 293.15, 1.2).GravityMagnitude);
        }

        // ───────────── registry / miscibility / mixing rules ─────────────

        [Test]
        public void Miscibility_HansenRuleMetalsAndOverrides()
        {
            // Ra(water, ethanol) = √(4·0.3² + 7.2² + 22.9²) = 24.0 < 25.
            Assert.AreEqual(24.0, LiquidChemistry.HansenDistance(L(LiquidLibrary.Water), L(LiquidLibrary.Ethanol)), 0.05);
            Assert.IsTrue(Registry.AreMiscible(LiquidLibrary.Water, LiquidLibrary.Ethanol));
            Assert.IsTrue(Registry.AreMiscible(LiquidLibrary.Water, LiquidLibrary.Glycerol));
            Assert.IsTrue(Registry.AreMiscible(LiquidLibrary.Water, LiquidLibrary.HydrochloricAcid));
            Assert.IsTrue(Registry.AreMiscible(LiquidLibrary.SulfuricAcid, LiquidLibrary.SodiumHydroxide));
            Assert.IsTrue(Registry.AreMiscible(LiquidLibrary.Kerosene, LiquidLibrary.CrudeOil));
            Assert.IsTrue(Registry.AreMiscible(LiquidLibrary.LiquidMethane, LiquidLibrary.LiquidNitrogen));
            Assert.IsFalse(Registry.AreMiscible(LiquidLibrary.Water, LiquidLibrary.CrudeOil));
            Assert.IsFalse(Registry.AreMiscible(LiquidLibrary.Glycerol, LiquidLibrary.Kerosene));
            Assert.IsFalse(Registry.AreMiscible(LiquidLibrary.Water, LiquidLibrary.Mercury), "liquid metal");
            Assert.IsFalse(Registry.AreMiscible(LiquidLibrary.Kerosene, LiquidLibrary.Mercury));
            Assert.IsFalse(Registry.AreMiscible(LiquidLibrary.LiquidAmmonia, LiquidLibrary.Kerosene), "data override");
            Assert.IsTrue(Registry.AreMiscible(LiquidLibrary.Mercury, LiquidLibrary.Mercury));
        }

        [Test]
        public void Registry_NewLiquidAndReaction_AreDataOnly()
        {
            var r = LiquidLibrary.CreateDefaultRegistry();
            LiquidDefinition oxygen = new LiquidDefinition
            {
                id = "liquid_oxygen", displayName = "LOX", formula = "O₂", molarMass = 0.031999,
                referenceTemperature = 90.19, referenceDensity = 1141.0, thermalExpansion = 4.2e-3, bulkModulus = 0.9e9, solidDensity = 1350.0,
                viscosityModel = ViscosityModel.Andrade, viscosityA = 4.0e-6, viscosityB = 300.0,
                surfaceTension = 0.0132, surfaceTensionSlope = -2.3e-4,
                specificHeatLiquid = 1699.0, specificHeatSolid = 1300.0, thermalConductivityLiquid = 0.15, thermalConductivitySolid = 0.2,
                meltingPoint = 54.36, normalBoilingPoint = 90.19, enthalpyOfVaporization = 2.13e5, enthalpyOfFusion = 1.39e4,
                criticalTemperature = 154.58, hansenDispersion = 12.0, hansenPolar = 0.0, hansenHydrogen = 0.0, reactivity = 3
            };
            r.Register(oxygen);
            Assert.IsTrue(r.Contains("liquid_oxygen"));
            Assert.IsTrue(r.AreMiscible("liquid_oxygen", LiquidLibrary.LiquidNitrogen));
            Assert.AreEqual(90.19, r.Get("liquid_oxygen").BoilingPoint(101325.0), 1e-9);
            Assert.Throws<ArgumentException>(() => r.Register(oxygen), "duplicate id");

            LiquidDefinition broken = LiquidLibrary.CreateWater();
            broken.id = "broken";
            broken.referenceDensity = -1.0;
            Assert.Throws<ArgumentException>(() => r.Register(broken));

            var unbalanced = new LiquidReaction
            {
                id = "bad", reactants = new[] { new ReactionTerm(LiquidLibrary.Water, 1.0) },
                products = new[] { new ReactionTerm(LiquidLibrary.SaltWater, 2.0) }, instantaneous = true
            };
            Assert.Throws<ArgumentException>(() => r.RegisterReaction(unbalanced), "mass must be conserved");
            var unknown = new LiquidReaction
            {
                id = "unknown", reactants = new[] { new ReactionTerm("nope", 1.0) },
                products = new[] { new ReactionTerm(LiquidLibrary.Water, 1.0) }, instantaneous = true
            };
            Assert.Throws<ArgumentException>(() => r.RegisterReaction(unknown));
            Assert.Throws<KeyNotFoundException>(() => r.Get("nope"));
        }

        [Test]
        public void MixingRules_DensityViscosityAndPH()
        {
            // 1 kg water + 1 kg ethanol, ideal volumes: ρ = 2 / (1/998.2 + 1/789.3) = 881.9 kg/m³.
            double rho = LiquidChemistry.MixtureDensity(new[] { 1.0, 1.0 }, new[] { 998.2, 789.3 }, 2);
            Assert.AreEqual(2.0 / (1.0 / 998.2 + 1.0 / 789.3), rho, 1e-9);
            // Arrhenius with equal moles = geometric mean.
            Assert.AreEqual(Math.Sqrt(1e-3 * 1.4), LiquidChemistry.ArrheniusViscosity(new[] { 1.0, 1.0 }, new[] { 1e-3, 1.4 }, 2), 1e-12);
            // Refutas: 50/50 crude (ν ≈ 10.9 cSt) and kerosene (ν ≈ 2.3 cSt) blends in between.
            double nuCrude = 10.9e-6, nuKero = 2.3e-6;
            double nu = LiquidChemistry.RefutasKinematicViscosity(new[] { 1.0, 1.0 }, new[] { nuCrude, nuKero }, 2);
            Assert.That(nu, Is.InRange(nuKero, nuCrude));
            Assert.AreEqual(nuCrude, LiquidChemistry.RefutasKinematicViscosity(new[] { 1.0, 0.0 }, new[] { nuCrude, nuKero }, 2), 1e-12);
            Assert.IsFalse(LiquidChemistry.RefutasApplicable(new[] { 0.1e-6 }, 1), "mercury-thin liquids fall back to Arrhenius");

            // pH: 0.01 M strong acid → 2.00; 0.01 M strong base at 25 °C → pKw − 2 = 11.995; neutral → pKw/2.
            Assert.AreEqual(2.0, LiquidChemistry.PHFromNetAcidity(0.01, 298.15), 1e-6);
            Assert.AreEqual(13.995 - 2.0, LiquidChemistry.PHFromNetAcidity(-0.01, 298.15), 0.005);
            Assert.AreEqual(13.995 / 2.0, LiquidChemistry.PHFromNetAcidity(0.0, 298.15), 0.005);
            // Neutral water at 60 °C: pKw = 4470.99/333.15 − 6.0875 + 0.01706·333.15 = 13.016 → pH 6.51.
            Assert.AreEqual(6.51, LiquidChemistry.PHFromNetAcidity(0.0, 333.15), 0.01);
            Assert.AreEqual(7.0, L(LiquidLibrary.Water).PH(298.15), 0.01);
            Assert.Less(L(LiquidLibrary.HydrochloricAcid).PH(298.15), 0.1);
            Assert.Greater(L(LiquidLibrary.SodiumHydroxide).PH(298.15), 13.5);
            Assert.IsTrue(double.IsNaN(L(LiquidLibrary.Kerosene).PH(298.15)), "pH undefined for non-aqueous liquids");
            Assert.IsTrue(L(LiquidLibrary.SulfuricAcid).IsChemicallyStable(600.0));
            Assert.IsFalse(L(LiquidLibrary.SulfuricAcid).IsChemicallyStable(650.0));
            Assert.Greater(L(LiquidLibrary.HydrochloricAcid).CorrosionRateAt(320.0), L(LiquidLibrary.HydrochloricAcid).CorrosionRateAt(298.15), "Arrhenius");
        }
    }
}
