using NUnit.Framework;
using SpaceSim.Liquids;
using SpaceSim.Planets;

namespace SpaceSim.Tests
{
    /// <summary>Reactions, pH, corrosion and decomposition inside the grid simulation.</summary>
    public class LiquidChemistryTests
    {
        const string HCl = LiquidLibrary.HydrochloricAcid, NaOH = LiquidLibrary.SodiumHydroxide, Salt = LiquidLibrary.SaltWater;
        const string H2SO4 = LiquidLibrary.SulfuricAcid, Na2SO4 = LiquidLibrary.SodiumSulfateSolution, Water = LiquidLibrary.Water;
        const double Litre = 1e-3;

        /// <summary>One 15 cm cube (3.375 L), no heat exchange (adiabatic), at 20 °C on the home planet.</summary>
        static LiquidSimulation Beaker(params string[] liquids)
        {
            LiquidSimulationSettings s = LiquidFixtures.Cells(0.15);
            s.enableHeat = false;
            return LiquidFixtures.Sim(1, 1, LiquidFixtures.Home(293.15), s, liquids);
        }

        [Test]
        public void Neutralisation_HClPlusNaOH_GivesNeutralBrineAndHeat()
        {
            LiquidSimulation sim = Beaker(HCl, NaOH, Salt);
            Assert.AreEqual(1, sim.ActiveReactionCount, "only the HCl rule has all its liquids here");
            sim.AddLiquid(0, 0, HCl, Litre, 293.15);   // 1.017 kg = 1 mol HCl
            sim.AddLiquid(0, 0, NaOH, Litre, 293.15);  // 1.041 kg = 1 mol NaOH
            Assert.Less(sim.GetDefinition(HCl).PH(293.15), 0.1);
            double m0 = sim.TotalAccountedMass();

            sim.Step(0.01);

            // ξ = 1 mol: both reactants consumed, 2.058 kg of 0.5 M NaCl formed.
            Assert.AreEqual(0.0, sim.GetLiquidMass(0, 0, HCl), 1e-9);
            Assert.AreEqual(0.0, sim.GetLiquidMass(0, 0, NaOH), 1e-9);
            Assert.AreEqual(2.058, sim.GetLiquidMass(0, 0, Salt), 1e-6);
            Assert.AreEqual(1.0, sim.ReactionExtent(LiquidLibrary.NeutralisationHClNaOH), 1e-6);
            // ΔT = 57 100 J / (2.058 kg · 3990 J/(kg·K)) = 6.95 K (lab value ≈ 6.8 K for 1 M + 1 M).
            Assert.That(sim.GetTemperature(0, 0) - 293.15, Is.InRange(6.0, 8.0));
            // Only neutral salt water left: pH = pK_w(300 K)/2 ≈ 6.97.
            Assert.That(sim.GetPH(0, 0), Is.InRange(6.5, 7.5));
            Assert.AreEqual(m0, sim.TotalAccountedMass(), 1e-9 * m0, "mass conserved by the reaction");
        }

        [Test]
        public void Neutralisation_ExcessAcid_PHTracksRemainingAcid()
        {
            LiquidSimulation sim = Beaker(HCl, NaOH, Salt);
            sim.AddLiquid(0, 0, HCl, Litre, 293.15);        // 1 mol H⁺
            sim.AddLiquid(0, 0, NaOH, 0.5 * Litre, 293.15); // 0.5 mol OH⁻
            // Before: net 0.5 mol H⁺ in 1.5 L → 0.333 M → pH = −log10(0.333) = 0.48.
            double before = sim.GetPH(0, 0);
            Assert.AreEqual(0.477, before, 0.02);

            sim.Step(0.01);

            // After: 0.5 L of 1 M HCl left in ≈ 1.51 L → 0.331 M → pH ≈ 0.48: the reaction conserves the net equivalents.
            Assert.AreEqual(0.0, sim.GetLiquidMass(0, 0, NaOH), 1e-9);
            Assert.AreEqual(0.5 * 1.017, sim.GetLiquidMass(0, 0, HCl), 1e-6);
            Assert.AreEqual(0.5 * 2.058, sim.GetLiquidMass(0, 0, Salt), 1e-6);
            Assert.AreEqual(before, sim.GetPH(0, 0), 0.05);
            // ΔT = 0.5 · 57 100 / (0.5085·3950 + 1.029·3990) = 28 550 / 6114 = 4.7 K.
            Assert.That(sim.GetTemperature(0, 0) - 293.15, Is.InRange(3.5, 6.0));

            // Adding the missing base finishes the neutralisation: pH → 7.
            sim.AddLiquid(0, 0, NaOH, 0.5 * Litre, 293.15);
            sim.Step(0.01);
            Assert.AreEqual(0.0, sim.GetLiquidMass(0, 0, HCl), 1e-9);
            Assert.That(sim.GetPH(0, 0), Is.InRange(6.5, 7.5));
            Assert.Greater(LiquidFixtures.Sim(1, 1, LiquidFixtures.Home(), null, NaOH).GetDefinition(NaOH).PH(293.15), 13.0);
        }

        [Test]
        public void Neutralisation_SulphuricAcidPlusNaOH_IsStronglyExothermic()
        {
            LiquidSimulation sim = Beaker(H2SO4, NaOH, Na2SO4);
            sim.AddLiquidMass(0, 0, H2SO4, 0.100082, 293.15); // 1 mol H₂SO₄ as the 98 % grade (54.5 mL)
            sim.AddLiquid(0, 0, NaOH, 2.0 * Litre, 293.15);   // 2 mol NaOH
            Assert.Less(LiquidFixtures.Sim(1, 1, LiquidFixtures.Home(), null, H2SO4).GetDefinition(H2SO4).PH(293.15), 0.0, "concentrated acid");
            double m0 = sim.TotalAccountedMass();

            sim.Step(0.01);

            Assert.AreEqual(2.182082, sim.GetLiquidMass(0, 0, Na2SO4), 1e-6);
            Assert.AreEqual(0.0, sim.GetLiquidMass(0, 0, H2SO4), 1e-9);
            // ΔT = 188 200 J / (2.182 kg · 3880 J/(kg·K)) = 22.2 K.
            Assert.That(sim.GetTemperature(0, 0) - 293.15, Is.InRange(18.0, 26.0));
            Assert.That(sim.GetPH(0, 0), Is.InRange(6.3, 7.7));
            Assert.AreEqual(m0, sim.TotalAccountedMass(), 1e-9 * m0);
        }

        [Test]
        public void CustomKineticReaction_FollowsArrheniusAndTemperatureWindow()
        {
            // A new reaction between new liquids, added purely as data.
            LiquidRegistry registry = LiquidLibrary.CreateDefaultRegistry();
            foreach (string id in new[] { "reagent_a", "reagent_b", "product_c" })
            {
                LiquidDefinition d = LiquidLibrary.CreateWater();
                d.id = id;
                d.displayName = id;
                registry.Register(d);
            }
            registry.RegisterReaction(new LiquidReaction
            {
                id = "a_plus_b", name = "A + B → C",
                reactants = new[] { new ReactionTerm("reagent_a", 1.0), new ReactionTerm("reagent_b", 1.0) },
                products = new[] { new ReactionTerm("product_c", 2.0) },
                enthalpy = -1000.0, instantaneous = false,
                preExponentialFactor = 6.9e3, activationEnergy = 50000.0, // k(310 K) ≈ 2.6e-5 m³/(mol·s)
                minTemperature = 300.0
            });

            // 0.4 L of each in one cell: n ≈ 0.4 mol each, c ≈ 500 mol/m³, V = 0.8 L →
            // dξ/dt = k·V·c_A·c_B ≈ 2.6e-5 · 8e-4 · 2.5e5 ≈ 5e-3 mol/s at 310 K; k(330)/k(310) = exp(Ea/R·(1/310 − 1/330)) = 3.24.
            double Extent(double temperature)
            {
                LiquidSimulationSettings s = LiquidFixtures.Cells();
                s.enableHeat = false;
                var env = LiquidEnvironment.Create(9.81, 101325.0, temperature, 1.2);
                var sim = new LiquidSimulation(registry, 1, 1, s, env, "reagent_a", "reagent_b", "product_c");
                sim.AddLiquid(0, 0, "reagent_a", 0.4 * Litre, temperature);
                sim.AddLiquid(0, 0, "reagent_b", 0.4 * Litre, temperature);
                double m0 = sim.TotalAccountedMass();
                LiquidFixtures.Run(sim, 10.0, 0.5);
                double xi = sim.ReactionExtent("a_plus_b");
                Assert.AreEqual(2.0 * xi, sim.GetLiquidMass(0, 0, "product_c"), 1e-9, "stoichiometry");
                Assert.AreEqual(m0, sim.TotalAccountedMass(), 1e-9 * m0);
                return xi;
            }

            double cold = Extent(295.0), warm = Extent(310.0), hot = Extent(330.0);
            Assert.AreEqual(0.0, cold, "below the rule's minimum temperature");
            Assert.That(warm, Is.InRange(0.01, 0.2), "partial conversion in 10 s");
            Assert.Greater(hot, 2.0 * warm, "Arrhenius: faster when hotter");
        }

        [Test]
        public void Corrosion_AcidThinsWallsMuchFasterThanWater()
        {
            double Loss(string id)
            {
                LiquidSimulationSettings s = LiquidFixtures.Cells();
                s.enableHeat = false;
                s.wallThickness = 1e-6; // a 1 µm coating, so the effect shows within seconds
                LiquidSimulation sim = LiquidFixtures.Sim(1, 1, LiquidFixtures.Home(), s, id);
                sim.AddLiquid(0, 0, id, 0.5 * Litre, 295.45);
                LiquidFixtures.Run(sim, 100.0, 1.0);
                Assert.IsFalse(sim.IsBreached);
                return 1.0 - sim.ContainerIntegrity;
            }
            // 1 M HCl: 3.17e-10 m/s · exp(−40000/R·(1/295.45 − 1/298.15)) = 2.7e-10 m/s → 2.7e-8 m in 100 s = 2.7 % of 1 µm.
            // Water: 3.2e-12 m/s → 0.03 %.
            double acid = Loss(HCl), water = Loss(Water);
            Assert.That(acid, Is.InRange(0.015, 0.05));
            Assert.Greater(water, 0.0);
            Assert.Greater(acid, 10.0 * water);
        }

        [Test]
        public void Corrosion_BreachedWallLeaksTheLiquid()
        {
            LiquidSimulationSettings s = LiquidFixtures.Cells();
            s.enableHeat = false;
            s.wallThickness = 1e-8; // eaten through after ≈ 1e-8 / 2.7e-10 ≈ 37 s
            LiquidSimulation sim = LiquidFixtures.Sim(1, 1, LiquidFixtures.Home(), s, HCl);
            sim.AddLiquid(0, 0, HCl, 0.5 * Litre, 295.45);
            double m0 = sim.TotalAccountedMass();
            LiquidFixtures.Run(sim, 20.0, 0.5);
            Assert.IsFalse(sim.IsBreached);
            Assert.AreEqual(0.0, sim.DrainedMass(HCl));

            // A 5 mm hole under ≈ 420 Pa of head: Q₀ = √(Δp/a) ≈ 1.5e-5 m³/s → the 0.5 L drains in about a minute.
            LiquidFixtures.Run(sim, 100.0, 0.5);
            Assert.IsTrue(sim.IsBreached);
            Assert.AreEqual(0.0, sim.WallIntegrity(0, 0));
            Assert.Greater(sim.DrainedMass(HCl), 0.2);
            Assert.AreEqual(m0, sim.TotalAccountedMass(), 1e-9 * m0);
        }

        [Test]
        public void Decomposition_SulphuricAcidAbove613K_IsEndothermicAndSelfLimiting()
        {
            // Under Hermeous Mora's 75 bar the acid cannot boil below its critical point (924 K), but at 650 K it
            // decomposes to SO₃ + H₂O (+1.8 MJ/kg). Without external heating the reaction cools the liquid back to
            // 613 K: decomposed fraction = c_p·ΔT/(ΔH + c_p·ΔT) = 1390·36.85/(1.8e6 + 51 220) ≈ 2.8 %.
            LiquidSimulationSettings s = LiquidFixtures.Cells();
            s.enableHeat = false;
            LiquidSimulation sim = LiquidFixtures.Sim(1, 1, LiquidFixtures.Planet(HomeSystemCatalog.HermeousMoraId), s, H2SO4);
            sim.AddLiquid(0, 0, H2SO4, 0.1 * Litre, 650.0);
            double m0 = sim.GetLiquidMass(0, 0, H2SO4);
            LiquidFixtures.Run(sim, 10.0, 0.5);
            double fraction = sim.DecomposedMass(H2SO4) / m0;
            Assert.That(fraction, Is.InRange(0.01, 0.05));
            Assert.AreEqual(0.0, sim.VentedMass(H2SO4), "no boiling at 75 bar");
            Assert.GreaterOrEqual(sim.GetTemperature(0, 0), 613.15 - 1e-6);
            Assert.Less(sim.GetTemperature(0, 0), 650.0);
            Assert.AreEqual(m0, sim.GetLiquidMass(0, 0, H2SO4) + sim.DecomposedMass(H2SO4), 1e-12);
        }
    }
}
