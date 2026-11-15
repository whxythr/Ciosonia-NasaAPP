using System.Collections.Generic;
using NUnit.Framework;
using SpaceSim.Economy;
using SpaceSim.Exploration;
using SpaceSim.Flight;
using SpaceSim.Planets;
using SpaceSim.Science;

namespace SpaceSim.Tests
{
    /// <summary>Career contract board, rocket upgrades, robot talents, and the sandbox's endless propellant.</summary>
    public class CareerProgressionTests
    {
        static PlayerProfile Career() => PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId, GameMode.Career);
        static PlayerProfile Sandbox() => PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId, GameMode.Sandbox);

        // ───────────── contracts ─────────────

        [Test]
        public void Contracts_AreUniqueAndChainsPointAtExistingContracts()
        {
            var ids = new HashSet<string>();
            foreach (ContractDefinition c in Contracts.All)
            {
                Assert.IsTrue(ids.Add(c.Id), "duplicate " + c.Id);
                Assert.Greater(c.Credits, 0, c.Id);
                if (c.Requires != null) Assert.IsNotNull(Contracts.Find(c.Requires), c.Id + " requires " + c.Requires);
                if (c.Kind == ContractKind.Milestone) Assert.IsNotNull(ResearchMilestones.Find(c.Target), c.Id);
                else Assert.IsTrue(HomeSystemCatalog.Bodies().Exists(b => b.Id == c.Target), c.Id + " targets " + c.Target);
            }
        }

        [Test]
        public void Contract_CompletesOncePaysCreditsAndTalentPoints()
        {
            PlayerProfile p = Career();
            int credits = p.credits;
            Assert.IsEmpty(Contracts.CheckCompletions(p), "nothing done yet");
            ResearchMilestones.Award(p, ResearchMilestones.ReachSpace);
            int afterMilestone = p.credits;
            List<ContractDefinition> done = Contracts.CheckCompletions(p);
            Assert.AreEqual(1, done.Count);
            Assert.AreEqual("c-reach-space", done[0].Id);
            Assert.AreEqual(afterMilestone + done[0].Credits, p.credits);
            Assert.Greater(p.credits, credits);
            Assert.AreEqual(1, p.talentPointsEarned);
            Assert.IsEmpty(Contracts.CheckCompletions(p), "never paid twice");
        }

        [Test]
        public void LockedContract_WaitsForItsPrerequisite_ThenChainCompletesAtOnce()
        {
            PlayerProfile p = Career();
            p.visitedBodies.Add(HomeSystemCatalog.KolimpriId);
            Assert.IsEmpty(Contracts.CheckCompletions(p), "«К Колимпри» needs «На орбите» first");
            ResearchMilestones.Award(p, ResearchMilestones.ReachSpace);
            ResearchMilestones.Award(p, ResearchMilestones.FirstOrbit);
            var ids = Contracts.CheckCompletions(p).ConvertAll(c => c.Id);
            CollectionAssert.IsSubsetOf(new[] { "c-reach-space", "c-first-orbit", "c-kolimpri-visit" }, ids);
        }

        [Test]
        public void MapAndSampleContracts_ReadTheProfile()
        {
            PlayerProfile p = Career();
            p.completedContracts.Add("c-reach-space");
            p.scienceRewards.Add(new ScienceRewardRecord { bodyId = HomeSystemCatalog.HomeWorldId, layer = (int)ScienceLayer.Temperature, rewardedPercent = 30 });
            Assert.IsTrue(Contracts.CheckCompletions(p).Exists(c => c.Id == "c-home-weather"));

            p.samples.Add(new SampleRecord { id = "s1", planetId = HomeSystemCatalog.TromosId });
            p.DeliverCargo();
            CollectionAssert.Contains(p.returnedSampleBodies, HomeSystemCatalog.TromosId);
        }

        [Test]
        public void Sandbox_HasNoContracts()
        {
            PlayerProfile p = Sandbox();
            ResearchMilestones.Award(p, ResearchMilestones.ReachSpace);
            Assert.IsEmpty(Contracts.CheckCompletions(p));
        }

        // ───────────── rocket upgrades ─────────────

        [Test]
        public void Upgrade_BoughtLevelByLevel_NeedsTierAndCredits()
        {
            PlayerProfile p = Career();
            p.credits = 100000;
            Assert.AreEqual(PurchaseResult.Success, RocketUpgrades.TryBuy(p, RocketUpgrades.Efficiency, out _));
            Assert.AreEqual(1, RocketUpgrades.LevelOf(p, RocketUpgrades.Efficiency));
            Assert.AreEqual(100000 - 6000, p.credits);
            Assert.AreEqual(PurchaseResult.RequirementsNotMet, RocketUpgrades.TryBuy(p, RocketUpgrades.Efficiency, out _), "level 2 needs tier 1");
            p.unlockedTiers.Add(1);
            Assert.AreEqual(PurchaseResult.Success, RocketUpgrades.TryBuy(p, RocketUpgrades.Efficiency, out _));
            p.credits = 0;
            p.unlockedTiers.Add(2);
            Assert.AreEqual(PurchaseResult.InsufficientFunds, RocketUpgrades.TryBuy(p, RocketUpgrades.Efficiency, out _));
            Assert.AreEqual(2, RocketUpgrades.LevelOf(p, RocketUpgrades.Efficiency));
        }

        [Test]
        public void Upgrades_ChangeTheCompiledRocket_SandboxGetsThemAll()
        {
            var baseline = new RocketConfiguration();
            var upgraded = baseline.Clone();
            PlayerProfile p = Career();
            p.rocketUpgrades.Add(RocketUpgrades.Efficiency);
            p.rocketUpgrades.Add(RocketUpgrades.LightTanks);
            RocketUpgrades.Apply(upgraded, p);
            Assert.AreEqual(baseline.stages[0].vacuumIsp * 1.03, upgraded.stages[0].vacuumIsp, 1e-9);
            Assert.AreEqual(baseline.stages[0].dryMass * 0.95, upgraded.stages[0].dryMass, 1e-9);
            Assert.AreEqual(baseline.stages[0].vacuumThrust, upgraded.stages[0].vacuumThrust, 1e-9, "thrust not bought");

            foreach (UpgradeDefinition u in RocketUpgrades.All)
                Assert.AreEqual(u.MaxLevel, RocketUpgrades.LevelOf(Sandbox(), u.Id));
            Assert.AreEqual(PurchaseResult.SandboxMode, RocketUpgrades.TryBuy(Sandbox(), RocketUpgrades.Thrust, out _));
        }

        // ───────────── robot talents ─────────────

        [Test]
        public void Talents_NeedPointsAndThePreviousTalentOfTheBranch()
        {
            PlayerProfile p = Career();
            Assert.IsFalse(RobotTalents.TryLearn(p, RobotTalents.Armor, out _), "no points yet");
            p.talentPointsEarned = 2;
            Assert.IsFalse(RobotTalents.TryLearn(p, RobotTalents.Thermal, out _), "Armor first");
            Assert.IsTrue(RobotTalents.TryLearn(p, RobotTalents.Armor, out _));
            Assert.IsTrue(RobotTalents.TryLearn(p, RobotTalents.Thermal, out _));
            Assert.AreEqual(0, RobotTalents.FreePoints(p));
            Assert.AreEqual(1, RobotTalents.RankOf(p, RobotTalents.Armor));
        }

        [Test]
        public void Talents_ChangeRobotRatesWithoutCompounding()
        {
            PlayerProfile p = Career();
            p.learnedTalents.Add(RobotTalents.Armor);
            p.learnedTalents.Add(RobotTalents.Shocks);
            var rates = RobotDamageRates.Default;
            RobotTalents.Configure(rates, p);
            RobotTalents.Configure(rates, p);
            Assert.AreEqual(RobotDamageRates.Default.integrityPerImpactSpeed * 0.8, rates.integrityPerImpactSpeed, 1e-12);
            Assert.AreEqual(RobotDamageRates.Default.safeImpactSpeed + 1.0, rates.safeImpactSpeed, 1e-12);

            var sample = new SampleRecord { basePrice = 100, scientificValue = 10 };
            p.learnedTalents.Add(RobotTalents.Sampling);
            RobotTalents.ApplyToSample(sample, p);
            Assert.AreEqual(115, sample.basePrice);
            Assert.AreEqual(1.0, RobotTalents.SpeedFactor(p), 1e-12);
            Assert.AreEqual(1.3, RobotTalents.SpeedFactor(Sandbox()), 1e-12, "sandbox: top rank everywhere");
        }

        // ───────────── sandbox fuel ─────────────

        [Test]
        public void InfinitePropellant_NeverEmptiesTheTanks()
        {
            var r = new RocketState(new RocketConfiguration()) { InfinitePropellant = true, EngineOn = true };
            double full = r.Propellant[0];
            for (int i = 0; i < 1000; i++) r.Burn(1.0);
            Assert.AreEqual(full, r.Propellant[0]);
            Assert.IsTrue(r.EngineOn);
            Assert.AreEqual(5.0, r.DrawRcsPropellant(5.0));
            Assert.AreEqual(full, r.Propellant[0]);

            var normal = new RocketState(new RocketConfiguration()) { EngineOn = true };
            normal.Burn(1.0);
            Assert.Less(normal.Propellant[0], full);
        }
    }
}
