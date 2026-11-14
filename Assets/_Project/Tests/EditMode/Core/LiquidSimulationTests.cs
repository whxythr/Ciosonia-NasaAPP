using System;
using System.Diagnostics;
using NUnit.Framework;
using SpaceSim.Liquids;
using SpaceSim.Mathematics;
using SpaceSim.Planets;

namespace SpaceSim.Tests
{
    /// <summary>
    /// Behaviour of the grid simulation. Cells are 10 cm cubes (1 L) unless stated. Expected values are derived by hand in
    /// the comments; assertions use inequalities/tolerances because the scheme is explicit and approximate.
    /// </summary>
    public class LiquidSimulationTests
    {
        const string Water = LiquidLibrary.Water, Oil = LiquidLibrary.CrudeOil, Mercury = LiquidLibrary.Mercury;
        const string Glycerol = LiquidLibrary.Glycerol, Ethanol = LiquidLibrary.Ethanol;
        const double Litre = 1e-3;

        static LiquidSimulation Column(LiquidEnvironment env, int height, LiquidSimulationSettings s, params string[] ids) =>
            LiquidFixtures.Sim(1, height, env, s, ids);

        static double TotalMass(LiquidSimulation sim, params string[] ids)
        {
            double m = 0.0;
            foreach (string id in ids) m += sim.TotalLiquidMass(id) + sim.TotalSolidMass(id);
            return m;
        }

        // ───────────── density layering ─────────────

        [Test]
        public void Layering_OilRisesAboveWater()
        {
            LiquidEnvironment env = LiquidFixtures.Home();
            LiquidSimulation sim = Column(env, 10, null, Water, Oil);
            double t = env.AmbientTemperature;
            sim.FillRegion(0, 0, 0, 4, Oil, 1.0, t);   // light oil (≈ 845 kg/m³) at the bottom …
            sim.FillRegion(0, 5, 0, 9, Water, 1.0, t); // … heavy water (≈ 998) on top: unstable
            double m0 = sim.TotalAccountedMass();
            Assert.Less(sim.LiquidCentroidHeight(Oil), sim.LiquidCentroidHeight(Water));

            // Buoyancy exchange per face: Δp = Δρ·g·Δy = 152·8.27·0.1 ≈ 126 Pa through half a face (0.005 m²), inertia-limited
            // Q ≈ √(Δp/a), a = ρ/(2A²) = 922/(5e-5) → Q ≈ 2.6 L/s; 5 L must cross the interface → separated within ~10–20 s.
            LiquidFixtures.Run(sim, 60.0, 0.05);

            Assert.Less(sim.LiquidCentroidHeight(Water), sim.LiquidCentroidHeight(Oil), "water below oil");
            Assert.Greater(sim.GetVolumeFraction(0, 0, Water), 0.95, "bottom cell is water");
            Assert.Greater(sim.GetVolumeFraction(0, 1, Water), 0.95);
            Assert.Greater(sim.GetVolumeFraction(0, 8, Oil), 0.95, "upper cells are oil");
            Assert.AreEqual(m0, sim.TotalAccountedMass(), 1e-9 * m0, "mass conserved");
            Assert.AreEqual(0.0, sim.SpilledMass(Water) + sim.SpilledMass(Oil), 1e-12, "nothing overflowed");
        }

        [Test]
        public void Layering_MercurySinksBelowWater()
        {
            LiquidEnvironment env = LiquidFixtures.Home();
            LiquidSimulation sim = Column(env, 6, null, Water, Mercury);
            double t = env.AmbientTemperature;
            sim.FillRegion(0, 0, 0, 3, Water, 1.0, t);
            sim.FillRegion(0, 4, 0, 5, Mercury, 1.0, t);
            double m0 = sim.TotalAccountedMass();

            // Δρ = 12548 kg/m³: Δp ≈ 1.0e4 Pa per cell → Q ≈ 8 L/s per face; separation takes a few seconds.
            LiquidFixtures.Run(sim, 20.0, 0.05);

            Assert.Less(sim.LiquidCentroidHeight(Mercury), sim.LiquidCentroidHeight(Water));
            Assert.Greater(sim.GetVolumeFraction(0, 0, Mercury), 0.95);
            Assert.Greater(sim.GetVolumeFraction(0, 1, Mercury), 0.95);
            Assert.Greater(sim.GetVolumeFraction(0, 5, Water), 0.95, "water on top");
            Assert.AreEqual(m0, sim.TotalAccountedMass(), 1e-9 * m0);
        }

        // ───────────── viscosity ─────────────

        [Test]
        public void Viscosity_GlycerolDrainsSlowerThanWater()
        {
            LiquidEnvironment env = LiquidFixtures.Home();
            double t = env.AmbientTemperature; // 295.45 K, above glycerol's melting point (290.9 K)
            LiquidSimulation water = Column(env, 10, null, Water);
            LiquidSimulation glycerol = Column(env, 10, null, Glycerol);
            water.FillRegion(0, 0, 0, 9, Water, 1.0, t);
            glycerol.FillRegion(0, 0, 0, 9, Glycerol, 1.0, t);
            int dw = water.AddDrain(0, 0, 0.004, 0.1); // 4 mm pipe, 10 cm long, at the bottom
            int dg = glycerol.AddDrain(0, 0, 0.004, 0.1);

            water.Step(0.05);
            glycerol.Step(0.05);
            // Water, Δp ≈ ρgH = 998·8.27·1.0 ≈ 8.25 kPa: Poiseuille b = 128μL/(πd⁴) = 1.2e7, inertia a = 1.5ρ/(2A²) = 4.7e12
            // → Q = 2Δp/(b + √(b² + 4aΔp)) ≈ 4.0e-5 m³/s (inertia-limited).
            double g = env.GravityMagnitude;
            double qWater = LiquidPhysics.PipeFlowRate(998.0 * g * 1.0, 0.004, 0.1, Water_mu(t), 998.0);
            Assert.AreEqual(qWater, water.DrainFlowRate(dw), 0.15 * qWater);
            // Glycerol (μ ≈ 1.15 Pa·s at 295 K): b ≈ 1.8e10 → Q ≈ Δp/b ≈ 10.4 kPa / 1.8e10 ≈ 5.7e-7 m³/s, ~70× slower.
            Assert.Less(glycerol.DrainFlowRate(dg), water.DrainFlowRate(dw) / 20.0);

            LiquidFixtures.Run(water, 30.0, 0.05);
            LiquidFixtures.Run(glycerol, 30.0, 0.05);
            double drainedWater = water.DrainedMass(Water), drainedGlycerol = glycerol.DrainedMass(Glycerol);
            Assert.Greater(drainedWater, 0.5, "≈ 1 kg of water in 30 s");
            Assert.Greater(drainedGlycerol, 0.0);
            Assert.Greater(drainedWater / drainedGlycerol, 10.0);
            Assert.AreEqual(drainedWater, water.DrainedMassThrough(dw, Water), 1e-12);
        }

        static double Water_mu(double t) => LiquidLibrary.CreateWater().DynamicViscosity(t);

        [Test]
        public void Viscosity_ThroughNarrowPassages_GlycerolSpreadsSlower()
        {
            // A 10-cell trough with 2 mm passages between cells (slot permeability k = w²/12 = 3.3e-7 m²).
            var s = LiquidFixtures.Cells();
            s.passageWidth = 0.002;
            LiquidEnvironment env = LiquidFixtures.Home();
            LiquidSimulation water = LiquidFixtures.Sim(10, 1, env, s, Water);
            LiquidSimulation glycerol = LiquidFixtures.Sim(10, 1, env, s, Glycerol);
            water.AddLiquid(0, 0, Water, Litre, env.AmbientTemperature);
            glycerol.AddLiquid(0, 0, Glycerol, Litre, env.AmbientTemperature);

            // Head ½ρgh ≈ 413 Pa through a 0.01 m² face. Water: b = μL/(kA) ≈ 2.9e4, a ≈ 5e6 → Q ≈ 6.7 L/s.
            // Glycerol: b ≈ 3.4e7 → Q ≈ 1.5e-5 m³/s, i.e. only ≈ 3 % of the cell in 2 s.
            LiquidFixtures.Run(water, 2.0, 0.02);
            LiquidFixtures.Run(glycerol, 2.0, 0.02);
            Assert.Less(water.GetLiquidVolume(0, 0, Water), 0.5 * Litre, "water levels out along the trough");
            Assert.Greater(glycerol.GetLiquidVolume(0, 0, Glycerol), 0.9 * Litre, "glycerol barely moves");
            Assert.Greater(water.GetLiquidVolume(9, 0, Water), glycerol.GetLiquidVolume(9, 0, Glycerol));
        }

        // ───────────── temperature / phase changes ─────────────

        static LiquidSimulationSettings NoHeat()
        {
            var s = LiquidFixtures.Cells();
            s.enableHeat = false;
            return s;
        }

        [Test]
        public void Temperature_SupercooledWaterFreezesToMeltingPoint()
        {
            LiquidSimulation sim = LiquidFixtures.Sim(1, 1, LiquidFixtures.Home(), NoHeat(), Water);
            sim.AddLiquid(0, 0, Water, 0.5 * Litre, 268.15);
            double m0 = sim.GetLiquidMass(0, 0, Water);
            sim.Step(0.01);
            // Releasing L_f re-warms the cell to 0 °C: frozen fraction = c_p·ΔT / L_f = 4182·5 / 334000 = 0.0626.
            Assert.AreEqual(0.0626, sim.GetSolidMass(0, 0, Water) / m0, 0.002);
            Assert.AreEqual(273.15, sim.GetTemperature(0, 0), 1e-6);
            Assert.AreEqual(m0, sim.GetSolidMass(0, 0, Water) + sim.GetLiquidMass(0, 0, Water), 1e-12);
        }

        [Test]
        public void Temperature_FreezesInColdAmbient_WithLatentHeatPlateau()
        {
            var s = LiquidFixtures.Cells();
            s.wallHeatTransferCoefficient = 500.0; // well-conducting metal can in contact with cold ground
            LiquidSimulation sim = LiquidFixtures.Sim(1, 1, LiquidFixtures.Home(250.0), s, Water);
            sim.AddLiquid(0, 0, Water, 0.8 * Litre, 280.0);
            double m0 = sim.GetLiquidMass(0, 0, Water);

            // ≈ 0.042 m² · 500 W/(m²K) · 23 K ≈ 480 W: cooling to 0 °C takes ~50 s, freezing 0.8·334 kJ ≈ 550 s.
            bool sawPlateau = false;
            for (int i = 0; i < 200; i++)
            {
                sim.Step(5.0);
                double frozen = sim.GetSolidMass(0, 0, Water) / m0;
                if (frozen > 0.2 && frozen < 0.8)
                {
                    sawPlateau = true;
                    Assert.AreEqual(273.15, sim.GetTemperature(0, 0), 0.01, "ice and water coexist at the melting point");
                }
            }
            Assert.IsTrue(sawPlateau);
            Assert.AreEqual(m0, sim.GetSolidMass(0, 0, Water), 1e-6 * m0, "all frozen after 1000 s");
            Assert.Less(sim.GetTemperature(0, 0), 273.15);
            Assert.GreaterOrEqual(sim.GetTemperature(0, 0), 250.0 - 1e-9, "never colder than the surroundings");
        }

        [Test]
        public void Temperature_SuperheatedWaterBoilsDownToBoilingPoint()
        {
            LiquidSimulation sim = LiquidFixtures.Sim(1, 1, LiquidFixtures.Home(), NoHeat(), Water);
            sim.AddLiquid(0, 0, Water, 0.5 * Litre, 380.0);
            double m0 = sim.GetLiquidMass(0, 0, Water);
            sim.Step(0.01);
            // p ≈ 101325 + ½ρgh ≈ 101.5 kPa → T_b ≈ 373.2 K; vented = c_p·(380 − 373.2)/L_v = 4182·6.8/2.257e6 ≈ 1.26 %.
            double tb = sim.GetDefinition(Water).BoilingPoint(sim.GetPressure(0, 0));
            Assert.AreEqual(tb, sim.GetTemperature(0, 0), 0.01, "pressure re-evaluated after venting differs by a few Pa");
            Assert.That(sim.VentedMass(Water) / m0, Is.InRange(0.008, 0.02));
            Assert.AreEqual(m0, sim.GetLiquidMass(0, 0, Water) + sim.VentedMass(Water), 1e-12);
        }

        [Test]
        public void Temperature_BoilingDependsOnPlanetPressure()
        {
            // Water at 200 °C (473.15 K), heat exchange off to isolate the phase rule.
            LiquidSimulation home = LiquidFixtures.Sim(1, 1, LiquidFixtures.Home(), NoHeat(), Water);
            LiquidSimulation mora = LiquidFixtures.Sim(1, 1, LiquidFixtures.Planet(HomeSystemCatalog.HermeousMoraId), NoHeat(), Water);
            home.AddLiquid(0, 0, Water, 0.5 * Litre, 473.15);
            mora.AddLiquid(0, 0, Water, 0.5 * Litre, 473.15);
            double mHome = home.GetLiquidMass(0, 0, Water), mMora = mora.GetLiquidMass(0, 0, Water);
            home.Step(0.01);
            mora.Step(0.01);
            // 1 atm: flashes ≈ c_p·100 K / L_v ≈ 18 % to steam. 75 bar: T_b ≈ 561 K, so 473 K water stays liquid.
            Assert.Greater(home.VentedMass(Water) / mHome, 0.1);
            Assert.AreEqual(0.0, mora.VentedMass(Water));
            Assert.AreEqual(mMora, mora.GetLiquidMass(0, 0, Water), 1e-12);
            Assert.AreEqual(473.15, mora.GetTemperature(0, 0), 1e-9);

            // Quoris (100 Pa < triple point): 20 °C water cannot stay liquid. It evaporates down to 0 °C
            // (c_p·20/L_v ≈ 3 %), the rest splits at 0 °C (L_f/(L_f + L_v) ≈ 12 % vapour, 88 % ice), then ice sublimates
            // until T_s(≈107 Pa) ≈ 252 K: final ≈ 84 % ice, ≈ 16 % vapour, 0 % liquid.
            LiquidSimulation quoris = LiquidFixtures.Sim(1, 1, LiquidFixtures.Planet(HomeSystemCatalog.QuorisId), NoHeat(), Water);
            quoris.AddLiquid(0, 0, Water, 0.1 * Litre, 293.15);
            double mq = quoris.GetLiquidMass(0, 0, Water);
            quoris.Step(0.01);
            Assert.AreEqual(0.0, quoris.GetLiquidMass(0, 0, Water), 1e-12 * mq, "no liquid water at 100 Pa");
            Assert.That(quoris.GetSolidMass(0, 0, Water) / mq, Is.InRange(0.75, 0.92));
            Assert.That(quoris.VentedMass(Water) / mq, Is.InRange(0.08, 0.25));
            Assert.Less(quoris.GetTemperature(0, 0), 273.15);
            Assert.AreEqual(mq, quoris.TotalAccountedMass(), 1e-12 * mq);
        }

        [Test]
        public void Temperature_LiquidCO2OnTholin_LiquidBelowBoilingPoint_BoilsAwayAtMeanTemperature()
        {
            const string co2 = LiquidLibrary.LiquidCarbonDioxide;
            LiquidEnvironment tholin = LiquidFixtures.Planet(HomeSystemCatalog.TholinId); // 60 bar, 310.95 K
            double tb = LiquidLibrary.CreateLiquidCarbonDioxide().BoilingPoint(tholin.AmbientPressure); // ≈ 294.9 K

            // A lake-like pool at 285 K (below T_b) stays liquid.
            LiquidSimulation cold = LiquidFixtures.Sim(1, 1, tholin, NoHeat(), co2);
            cold.AddLiquid(0, 0, co2, 0.5 * Litre, 285.0);
            double m0 = cold.GetLiquidMass(0, 0, co2);
            cold.Step(0.01);
            Assert.AreEqual(m0, cold.GetLiquidMass(0, 0, co2), 1e-12);
            Assert.AreEqual(0.0, cold.VentedMass(co2));

            // At the 310.95 K mean (above both T_b and T_c = 304.13 K) the liquid cannot persist: it is cooled to T_b by
            // boiling at once (c_p·ΔT/L_v(T_b) = 1970·16/1.48e5 ≈ 21 %), and the warm surroundings then boil the rest.
            var s = LiquidFixtures.Cells();
            s.wallHeatTransferCoefficient = 500.0;
            LiquidSimulation warm = LiquidFixtures.Sim(1, 1, tholin, s, co2);
            warm.AddLiquid(0, 0, co2, 0.5 * Litre, tholin.AmbientTemperature);
            double m1 = warm.GetLiquidMass(0, 0, co2);
            warm.Step(0.01);
            Assert.LessOrEqual(warm.GetTemperature(0, 0), tb + 0.05, "liquid never stays above its boiling point");
            Assert.Greater(warm.VentedMass(co2), 0.1 * m1);
            // Remaining ≈ 0.29 kg × L_v ≈ 43 kJ fed at ≥ 80 W (floor alone: 500 W/m²K · 0.01 m² · 16 K; walls and the dense
            // CO₂ gas add more) → gone well within 20 min.
            LiquidFixtures.Run(warm, 1200.0, 1.0);
            Assert.AreEqual(0.0, warm.GetLiquidMass(0, 0, co2), 1e-9 * m1, "no liquid CO₂ at 310.95 K");
            Assert.AreEqual(m1, warm.TotalAccountedMass(), 1e-9 * m1);
        }

        // ───────────── mixing ─────────────

        [Test]
        public void Mixing_WaterAndEthanolFormOnePhase_PropertiesBlend()
        {
            LiquidEnvironment env = LiquidFixtures.Home();
            LiquidSimulation sim = LiquidFixtures.Sim(1, 1, env, null, Water, Ethanol, Oil);
            Assert.IsTrue(sim.ShareLiquidPhase(Water, Ethanol));
            Assert.IsFalse(sim.ShareLiquidPhase(Water, Oil));
            Assert.AreEqual(2, sim.PhaseGroupCount);

            double t = env.AmbientTemperature;
            sim.AddLiquid(0, 0, Water, 0.4 * Litre, t);
            sim.AddLiquid(0, 0, Ethanol, 0.4 * Litre, t);
            double mw = sim.GetLiquidMass(0, 0, Water), me = sim.GetLiquidMass(0, 0, Ethanol);
            double vw = sim.GetLiquidVolume(0, 0, Water), ve = sim.GetLiquidVolume(0, 0, Ethanol);

            // Volume-additive density: (0.399 + 0.315 kg) / 0.8 L ≈ 893 kg/m³.
            double rho = sim.GetPhaseDensity(0, 0, Ethanol);
            Assert.AreEqual((mw + me) / (vw + ve), rho, 1e-9 * rho);
            Assert.That(rho, Is.InRange(880.0, 905.0));

            // Arrhenius mixing with mole fractions (x_water ≈ 0.76): ln μ = Σ xᵢ ln μᵢ, between the pure values.
            LiquidDefinition dw = sim.GetDefinition(Water), de = sim.GetDefinition(Ethanol);
            double nw = mw / dw.molarMass, ne = me / de.molarMass;
            double expected = Math.Exp((nw * Math.Log(dw.DynamicViscosity(t)) + ne * Math.Log(de.DynamicViscosity(t))) / (nw + ne));
            double mu = sim.GetPhaseViscosity(0, 0, Water);
            Assert.AreEqual(expected, mu, 1e-9 * expected);
            Assert.That(mu, Is.InRange(Math.Min(dw.DynamicViscosity(t), de.DynamicViscosity(t)), Math.Max(dw.DynamicViscosity(t), de.DynamicViscosity(t))));
            double muRefutas = sim.GetPhaseViscosity(0, 0, Water, ViscosityMixingRule.Refutas);
            Assert.That(muRefutas, Is.InRange(0.5 * mu, 2.0 * mu), "Refutas blend of comparable magnitude");

            sim.AddLiquid(0, 0, Oil, 0.1 * Litre, t);
            Assert.AreEqual(sim.GetDefinition(Oil).Density(t, sim.GetPressure(0, 0)), sim.GetPhaseDensity(0, 0, Oil), 1e-3, "oil stays its own phase");
        }

        [Test]
        public void PhaseGroups_ACoSolventNeverBridgesTwoImmiscibleLiquids()
        {
            // Force a co-solvent that is miscible with both water and oil (which are immiscible with each other).
            LiquidRegistry r = LiquidLibrary.CreateDefaultRegistry();
            r.SetMiscibility(Ethanol, Oil, true);
            Assert.IsTrue(r.AreMiscible(Ethanol, Water));
            Assert.IsFalse(r.AreMiscible(Water, Oil));
            LiquidEnvironment env = LiquidFixtures.Home();
            foreach (string[] order in new[] { new[] { Water, Ethanol, Oil }, new[] { Oil, Ethanol, Water }, new[] { Ethanol, Oil, Water } })
            {
                var sim = new LiquidSimulation(r, 1, 1, LiquidFixtures.Cells(), env, order);
                Assert.IsFalse(sim.ShareLiquidPhase(Water, Oil), "water and oil never share a phase");
                Assert.AreEqual(2, sim.PhaseGroupCount, string.Join(",", order));
                // The co-solvent joins its most compatible partner (smallest Hansen distance: crude oil, Ra ≈ 20 vs 24 to water),
                // whatever order the liquids are listed in.
                Assert.IsTrue(sim.ShareLiquidPhase(Ethanol, Oil), string.Join(",", order));
            }
        }

        [Test]
        public void Mixing_MiscibleLiquidsDiffuseTogether_ImmiscibleStaySeparate()
        {
            var s = LiquidFixtures.Cells();
            s.eddyDiffusivity = 1e-3; // stirred tank
            LiquidEnvironment env = LiquidFixtures.Home();
            double t = env.AmbientTemperature;

            // Ethanol (light) on water (heavy): gravitationally stable, so only diffusion mixes them.
            // θ = D·A·h/(Δy·V) = 1e-3·0.01·h/(0.1·1e-3) = 0.1·h per substep → e-folding ≈ 5 s; 300 s mixes completely.
            LiquidSimulation miscible = LiquidFixtures.Sim(1, 2, env, s, Water, Ethanol);
            miscible.AddLiquid(0, 0, Water, Litre, t);
            miscible.AddLiquid(0, 1, Ethanol, Litre, t);
            double m0 = miscible.TotalAccountedMass();
            LiquidFixtures.Run(miscible, 300.0, 0.1);
            Assert.That(miscible.GetVolumeFraction(0, 0, Ethanol), Is.InRange(0.4, 0.6), "ethanol reached the bottom");
            Assert.That(miscible.GetVolumeFraction(0, 1, Water), Is.InRange(0.4, 0.6), "water reached the top");
            Assert.AreEqual(m0, miscible.TotalAccountedMass(), 1e-9 * m0);

            // Oil on water with the same stirring: separate phases, no exchange beyond the ≈ 1 % compression slack.
            LiquidSimulation immiscible = LiquidFixtures.Sim(1, 2, env, s, Water, Oil);
            immiscible.AddLiquid(0, 0, Water, Litre, t);
            immiscible.AddLiquid(0, 1, Oil, Litre, t);
            LiquidFixtures.Run(immiscible, 300.0, 0.1);
            Assert.Less(immiscible.GetVolumeFraction(0, 0, Oil), 0.03);
            Assert.Less(immiscible.GetVolumeFraction(0, 1, Water), 0.03);
        }

        // ───────────── gravity ─────────────

        [Test]
        public void Gravity_ZeroG_NoSettlingAndNoFlow()
        {
            LiquidEnvironment orbit = LiquidEnvironment.ZeroGravity(101325.0, 295.0, 1.2);
            double t = orbit.AmbientTemperature;
            // An oil/water emulsion in every cell stays as it is: no buoyancy without gravity.
            LiquidSimulation emulsion = Column(orbit, 4, null, Water, Oil);
            for (int y = 0; y < 4; y++)
            {
                emulsion.AddLiquid(0, y, Water, 0.5 * Litre, t);
                emulsion.AddLiquid(0, y, Oil, 0.5 * Litre, t);
            }
            LiquidFixtures.Run(emulsion, 10.0, 0.05);
            for (int y = 0; y < 4; y++) Assert.AreEqual(0.5, emulsion.GetVolumeFraction(0, y, Oil), 1e-6);

            // A blob of water at the top of an empty column does not fall, and a drain does not run.
            LiquidSimulation blob = Column(orbit, 4, null, Water);
            blob.AddLiquid(0, 3, Water, 0.5 * Litre, t);
            int d = blob.AddDrain(0, 3, 0.01, 0.01);
            double m = blob.GetLiquidMass(0, 3, Water);
            LiquidFixtures.Run(blob, 10.0, 0.05);
            Assert.AreEqual(m, blob.GetLiquidMass(0, 3, Water), 1e-12);
            Assert.AreEqual(0.0, blob.DrainedMassThrough(d, Water));

            // The same blob under home gravity falls to the bottom.
            LiquidSimulation falling = Column(LiquidFixtures.Home(), 4, null, Water);
            falling.AddLiquid(0, 3, Water, 0.5 * Litre, t);
            LiquidFixtures.Run(falling, 2.0, 0.05);
            Assert.Greater(falling.GetLiquidMass(0, 0, Water), 0.99 * m);
        }

        [Test]
        public void Gravity_StrongerGravitySettlesFaster()
        {
            LiquidEnvironment home = LiquidFixtures.Home();
            double t = home.AmbientTemperature;
            double[] gravities =
            {
                LiquidFixtures.Planet(HomeSystemCatalog.QuorisId).GravityMagnitude,       // ≈ 1.4
                home.GravityMagnitude,                                                   // ≈ 8.3
                LiquidFixtures.Planet(HomeSystemCatalog.HermeousMoraId).GravityMagnitude // ≈ 14.5
            };
            // Same temperature/pressure, only gravity differs. Buoyant exchange is inertia-limited, v ~ √(Δρ/ρ · g · L):
            // with Δρ/ρ ≈ 0.15 and L = 0.5 m that is ≈ 0.3 m/s (g = 1.4), 0.8 m/s (8.3) and 1.0 m/s (14.5), so the 0.5 m
            // layers need ≳ 0.5 s to swap even under the strongest gravity. After 0.2 s every column is still mid-way and
            // the oil has risen further under stronger gravity.
            var rise = new double[gravities.Length];
            for (int i = 0; i < gravities.Length; i++)
            {
                LiquidEnvironment env = home.WithGravity(new Vector3d(0.0, -gravities[i], 0.0));
                LiquidSimulation sim = Column(env, 10, null, Water, Oil);
                sim.FillRegion(0, 0, 0, 4, Oil, 1.0, t);
                sim.FillRegion(0, 5, 0, 9, Water, 1.0, t);
                double start = sim.LiquidCentroidHeight(Oil);
                LiquidFixtures.Run(sim, 0.2, 0.05);
                rise[i] = sim.LiquidCentroidHeight(Oil) - start;
                Assert.Less(sim.LiquidCentroidHeight(Oil), sim.LiquidCentroidHeight(Water) + 1e-9, "not finished yet");
            }
            Assert.Greater(rise[0], 0.0);
            Assert.Greater(rise[1], rise[0]);
            Assert.Greater(rise[2], rise[1]);
        }

        // ───────────── pressure ─────────────

        [Test]
        public void Pressure_HydrostaticColumn_ScalesWithGravity()
        {
            LiquidEnvironment home = LiquidFixtures.Home();
            LiquidEnvironment heavy = home.WithGravity(LiquidFixtures.Planet(HomeSystemCatalog.HermeousMoraId).Gravity);
            double t = home.AmbientTemperature;
            double[] gauge = new double[2];
            LiquidEnvironment[] envs = { home, heavy };
            for (int i = 0; i < 2; i++)
            {
                LiquidSimulation sim = Column(envs[i], 10, null, Water);
                sim.FillRegion(0, 0, 0, 9, Water, 1.0, t);
                LiquidFixtures.Run(sim, 5.0, 0.05);
                double g = envs[i].GravityMagnitude;
                double rho = sim.GetMixtureDensity(0, 0);
                // Middle of the bottom cell lies 0.95 m below the surface: p = p₀ + ρ·g·0.95 (≈ 7.8 kPa at home).
                gauge[i] = sim.GetPressure(0, 0) - envs[i].AmbientPressure;
                Assert.AreEqual(LiquidPhysics.HydrostaticPressure(0.0, rho, g, 0.95), gauge[i], 0.015 * gauge[i]);
                Assert.Greater(sim.GetPressure(0, 0), sim.GetPressure(0, 5));
                Assert.Greater(sim.GetPressure(0, 5), sim.GetPressure(0, 9));
                // The surface cell only carries half of its own weight above the ambient pressure.
                Assert.That(sim.GetPressure(0, 9) - envs[i].AmbientPressure, Is.InRange(0.0, 0.5 * rho * g * 0.1 + 1e-9));
                Assert.Greater(sim.GetFillFraction(0, 0), 1.0, "the deepest cell is slightly compressed (artificial compressibility)");
                Assert.Less(sim.GetFillFraction(0, 0), 1.0 + sim.Settings.maxCompression + 1e-9);
            }
            Assert.AreEqual(heavy.GravityMagnitude / home.GravityMagnitude, gauge[1] / gauge[0], 0.01);
        }

        // ───────────── conservation ─────────────

        [Test]
        public void Conservation_MassAndVolume_ClosedTank()
        {
            var s = LiquidFixtures.Cells();
            s.openTop = false;
            LiquidEnvironment env = LiquidFixtures.Home();
            double t = env.AmbientTemperature;
            LiquidSimulation sim = LiquidFixtures.Sim(8, 8, env, s, Water, Oil, Mercury);
            sim.FillRegion(0, 0, 7, 2, Water, 1.0, t);   // 24 L
            sim.FillRegion(0, 5, 3, 6, Oil, 1.0, t);     // 8 L, floating mid-air at first
            sim.FillRegion(6, 7, 7, 7, Mercury, 1.0, t); // 2 L at the very top
            double[] m0 = { sim.TotalLiquidMass(Water), sim.TotalLiquidMass(Oil), sim.TotalLiquidMass(Mercury) };
            double[] v0 = { sim.TotalLiquidVolume(Water), sim.TotalLiquidVolume(Oil), sim.TotalLiquidVolume(Mercury) };
            double mercuryStart = sim.LiquidCentroidHeight(Mercury);

            LiquidFixtures.Run(sim, 4.0, 0.02);

            string[] ids = { Water, Oil, Mercury };
            for (int i = 0; i < 3; i++)
            {
                Assert.AreEqual(m0[i], sim.TotalLiquidMass(ids[i]), 1e-9 * m0[i], ids[i] + " mass");
                // Volumes change only through the (tiny) pressure compressibility: < 0.1 %.
                Assert.AreEqual(v0[i], sim.TotalLiquidVolume(ids[i]), 1e-3 * v0[i], ids[i] + " volume");
            }
            for (int y = 0; y < 8; y++)
                for (int x = 0; x < 8; x++)
                {
                    Assert.IsFalse(double.IsNaN(sim.GetTemperature(x, y)));
                    Assert.AreEqual(t, sim.GetTemperature(x, y), 1e-6, "isothermal setup stays isothermal");
                    foreach (string id in ids) Assert.GreaterOrEqual(sim.GetLiquidMass(x, y, id), 0.0);
                }
            Assert.Less(sim.LiquidCentroidHeight(Mercury), mercuryStart - 0.2, "mercury fell");
        }

        [Test]
        public void Heat_HotWaterBelowColdWater_ConvectsToEnergyWeightedMean()
        {
            var s = LiquidFixtures.Cells();
            s.openTop = false;
            s.wallHeatTransferCoefficient = 0.0;
            s.surfaceHeatTransferCoefficient = 0.0;
            s.radiativeExchange = false; // insulated: energy must be conserved exactly
            LiquidSimulation sim = LiquidFixtures.Sim(1, 2, LiquidFixtures.Home(), s, Water);
            sim.AddLiquid(0, 0, Water, Litre, 350.0); // hot, light water below …
            sim.AddLiquid(0, 1, Water, Litre, 290.0); // … cold, dense water above
            double m1 = sim.GetLiquidMass(0, 0, Water), m2 = sim.GetLiquidMass(0, 1, Water);
            double expected = (m1 * 350.0 + m2 * 290.0) / (m1 + m2); // same c_p → mass-weighted (≈ 319.6 K)

            LiquidFixtures.Run(sim, 600.0, 0.1);
            double ta = sim.GetTemperature(0, 0), tb = sim.GetTemperature(0, 1);
            double ma = sim.GetLiquidMass(0, 0, Water), mb = sim.GetLiquidMass(0, 1, Water);
            Assert.AreEqual(expected, (ma * ta + mb * tb) / (ma + mb), 1e-6, "enthalpy conserved");
            Assert.AreEqual(expected, ta, 0.05);
            Assert.AreEqual(expected, tb, 0.05);
        }

        [Test]
        public void Heat_ExchangeWithEnvironment_FasterInAirThanVacuum()
        {
            var s = LiquidFixtures.Cells();
            s.enablePhaseChanges = false; // keep the vacuum sample liquid to compare heat transfer only
            LiquidEnvironment air = LiquidEnvironment.Create(8.27, 101325.0, 295.0, 1.2, "air");
            LiquidEnvironment vacuum = LiquidEnvironment.Create(8.27, 0.0, 295.0, 0.0, "vacuum");
            LiquidSimulation a = LiquidFixtures.Sim(1, 1, air, s, Water);
            LiquidSimulation v = LiquidFixtures.Sim(1, 1, vacuum, s, Water);
            a.AddLiquid(0, 0, Water, 0.5 * Litre, 350.0);
            v.AddLiquid(0, 0, Water, 0.5 * Litre, 350.0);
            // Air: walls 2·0.03 + convection 10·0.01 + radiation ≈ 0.07 → ≈ 0.23 W/K; vacuum: walls + radiation ≈ 0.13 W/K.
            // C ≈ 0.49 kg · 4182 ≈ 2040 J/K → time constants ≈ 2.4 h vs 4.3 h.
            double previous = 350.0;
            for (int i = 0; i < 10; i++)
            {
                LiquidFixtures.Run(a, 60.0, 1.0);
                Assert.Less(a.GetTemperature(0, 0), previous, "cools monotonically");
                previous = a.GetTemperature(0, 0);
            }
            LiquidFixtures.Run(v, 600.0, 1.0);
            Assert.Less(a.GetTemperature(0, 0), v.GetTemperature(0, 0), "convection only exists in air");
            Assert.Less(v.GetTemperature(0, 0), 350.0, "radiation and wall conduction still cool it in vacuum");
            Assert.Greater(a.GetTemperature(0, 0), 295.0);
        }

        // ───────────── performance ─────────────

        [Test]
        public void Performance_64x64_ThreeLiquids_NoAllocationInStep()
        {
            LiquidEnvironment env = LiquidFixtures.Home();
            double t = env.AmbientTemperature;
            LiquidSimulation sim = LiquidFixtures.Sim(64, 64, env, LiquidFixtures.Cells(0.05), Water, Oil, Mercury);
            sim.FillRegion(0, 0, 63, 20, Water, 1.0, t);
            sim.FillRegion(0, 21, 31, 30, Oil, 1.0, t);
            sim.FillRegion(40, 40, 47, 47, Mercury, 1.0, t);
            double m0 = sim.TotalAccountedMass();

            sim.Step(1.0 / 60.0); // warm-up (JIT)
            sim.Step(1.0 / 60.0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 30; i++) sim.Step(1.0 / 60.0);
            clock.Stop();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Less(allocated, 16 * 1024, "Step must not allocate (only the Stopwatch above is counted)");
            Assert.Less(clock.Elapsed.TotalSeconds, 30.0, "generous budget: 4096 cells × 3 liquids × ~5 substeps × 30 steps");
            Assert.GreaterOrEqual(sim.LastSubstepCount, 1);
            Assert.AreEqual(m0, sim.TotalAccountedMass(), 1e-9 * m0);
        }
    }
}
