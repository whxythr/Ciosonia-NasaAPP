using System.Collections.Generic;
using NUnit.Framework;
using SpaceSim.Crew;
using SpaceSim.Economy;
using SpaceSim.Planets;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    /// <summary>Sandbox and Career: ownership, purchases, technology tiers, income API, persistence of the mode.</summary>
    public class GameModeTests
    {
        static PlayerProfile Career() => PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId, GameMode.Career);
        static PlayerProfile Sandbox() => PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId, GameMode.Sandbox);

        // ───────────── sandbox ─────────────

        [Test]
        public void Sandbox_NeverRequiresAPurchase()
        {
            PlayerProfile p = Sandbox();
            p.credits = 0;
            foreach (ModuleDefinition m in ModuleCatalog.All)
            {
                Assert.IsTrue(p.Owns(m.id), m.id);
                Assert.IsTrue(p.TierUnlockedFor(m.id), m.id);
                Assert.AreEqual(ShopItemState.Owned, Shop.StateOf(p, Shop.Find(m.id)), m.id);
            }
            Assert.AreEqual(PurchaseResult.SandboxMode, Shop.TryPurchase(p, ModuleCatalog.EngineNuclearLarge, out _));
            Assert.AreEqual(0, p.credits, "nothing is charged");
            Assert.AreEqual(PurchaseResult.SandboxMode, TechTree.TryUnlock(p, 4, out _));

            var b = RocketBlueprint.CreateDefault();
            b.stages[0].engineId = ModuleCatalog.EngineNuclearLarge;
            b.payloadModules.Add(ModuleCatalog.Reactor);
            Assert.IsEmpty(GameModeRules.UnavailableModules(b, p.Owns), "the hangar never blocks a sandbox design");
            Assert.IsFalse(BlueprintCompiler.Compile(b, p.Owns).Issues.Exists(i => i.Code == BlueprintIssueCode.LockedModule));
        }

        [Test]
        public void Sandbox_HiresCrewWithoutMoney()
        {
            PlayerProfile p = Sandbox();
            p.credits = 0;
            CrewMember applicant = p.crew.members.Find(m => m.status == CrewStatus.Applicant);
            Assert.AreEqual(0, CrewRoster.HireCostFor(p));
            Assert.IsTrue(CrewRoster.Hire(p, p.crew, applicant.id, out _));
            Assert.AreEqual(0, p.credits);
            Assert.AreEqual(CrewRoster.HireCost, CrewRoster.HireCostFor(Career()));
        }

        // ───────────── career ─────────────

        [Test]
        public void Career_StartsWithBudgetAndTheStarterKitOnly()
        {
            PlayerProfile p = Career();
            Assert.AreEqual(GameModeRules.CareerStartingCredits, p.credits);
            foreach (string id in ModuleCatalog.CareerStarterKit)
            {
                Assert.IsTrue(p.Owns(id), id);
                Assert.AreEqual(0, ModuleCatalog.TierOf(id), "starter parts are tier 0: " + id);
            }
            Assert.IsEmpty(GameModeRules.UnavailableModules(RocketBlueprint.CreateDefault(), p.Owns), "the starting rocket can launch");
            Assert.IsFalse(p.Owns(ModuleCatalog.ParachuteSmall));
            Assert.AreEqual(0, TechTree.HighestUnlocked(p));
        }

        [Test]
        public void Career_BlocksLockedAndUnboughtParts()
        {
            PlayerProfile p = Career();
            var b = RocketBlueprint.CreateDefault();
            b.stages[1].engineId = ModuleCatalog.EngineHydra;  // tier 2: tier closed
            b.payloadModules.Add(ModuleCatalog.ParachuteSmall); // tier 0: open, not bought
            b.payloadModules.Add(ModuleCatalog.ParachuteSmall); // listed once

            List<string> missing = GameModeRules.UnavailableModules(b, p.Owns);
            CollectionAssert.AreEqual(new[] { ModuleCatalog.EngineHydra, ModuleCatalog.ParachuteSmall }, missing);
            Assert.AreEqual(ModuleCatalog.Find(ModuleCatalog.EngineHydra).price + ModuleCatalog.Find(ModuleCatalog.ParachuteSmall).price,
                GameModeRules.PriceToBuy(b, p.Owns));

            CompiledRocket c = BlueprintCompiler.Compile(b, p.Owns);
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.LockedModule), "the compiler never installs them either");

            Assert.AreEqual(ShopItemState.Locked, Shop.StateOf(p, Shop.Find(ModuleCatalog.EngineHydra)));
            Assert.AreEqual(PurchaseResult.RequirementsNotMet, Shop.TryPurchase(p, ModuleCatalog.EngineHydra, out string msg));
            StringAssert.Contains("Первая орбита", msg, "the message names what opens the tier");
            Assert.AreEqual(GameModeRules.CareerStartingCredits, p.credits);
        }

        [Test]
        public void Career_PurchaseDeductsFundsAndMarksOwned()
        {
            PlayerProfile p = Career();
            int price = ModuleCatalog.Find(ModuleCatalog.ParachuteSmall).price;
            Assert.AreEqual(ShopItemState.Available, Shop.StateOf(p, Shop.Find(ModuleCatalog.ParachuteSmall)));
            Assert.AreEqual(PurchaseResult.Success, Shop.TryPurchase(p, ModuleCatalog.ParachuteSmall, out _));
            Assert.AreEqual(GameModeRules.CareerStartingCredits - price, p.credits);
            Assert.IsTrue(p.Owns(ModuleCatalog.ParachuteSmall));
            Assert.AreEqual(-price, p.ledger[p.ledger.Count - 1].credits, "the purchase is in the ledger");
            Assert.AreEqual(PurchaseResult.AlreadyOwned, Shop.TryPurchase(p, ModuleCatalog.ParachuteSmall, out _), "buy once");
            Assert.AreEqual(GameModeRules.CareerStartingCredits - price, p.credits);
        }

        [Test]
        public void Career_InsufficientFundsAreRejected()
        {
            PlayerProfile p = Career();
            ModuleDefinition tank = ModuleCatalog.Find(ModuleCatalog.TankK140);
            p.credits = tank.price - 1;
            Assert.AreEqual(ShopItemState.Unaffordable, Shop.StateOf(p, Shop.Find(tank.id)));
            Assert.AreEqual(PurchaseResult.InsufficientFunds, Shop.TryPurchase(p, tank.id, out _));
            Assert.AreEqual(tank.price - 1, p.credits);
            Assert.IsFalse(p.Owns(tank.id));
        }

        [Test]
        public void Career_TiersOpenInOrderWithMilestonesAndResearch()
        {
            PlayerProfile p = Career();
            Assert.AreEqual(TechTierState.NotEnoughResearch, TechTree.StateOf(p, 1));
            Assert.AreEqual(TechTierState.PreviousTierLocked, TechTree.StateOf(p, 2));
            Assert.AreEqual(TechTree.Get(1).EarlyResearchCost, TechTree.CostFor(p, 1), "without the milestone: dearer");
            Assert.AreEqual(PurchaseResult.InsufficientResearch, TechTree.TryUnlock(p, 1, out _));

            // Reaching space pays exactly the research of tier 1.
            MilestoneDefinition space = ResearchMilestones.Award(p, ResearchMilestones.ReachSpace);
            Assert.AreEqual(GameModeRules.CareerStartingCredits + space.CreditReward, p.credits, "milestone income");
            Assert.AreEqual(TechTree.Get(1).ResearchCost, TechTree.CostFor(p, 1));
            Assert.AreEqual(TechTierState.Researchable, TechTree.StateOf(p, 1));
            Assert.AreEqual(PurchaseResult.RequirementsNotMet, TechTree.TryUnlock(p, 2, out _), "tiers open in order");
            Assert.AreEqual(PurchaseResult.Success, TechTree.TryUnlock(p, 1, out _));
            Assert.AreEqual(0.0, p.researchPoints, 1e-9);
            Assert.AreEqual(PurchaseResult.AlreadyOwned, TechTree.TryUnlock(p, 1, out _));
            Assert.AreEqual(1, TechTree.HighestUnlocked(p));

            // Tier 1 parts can now be bought.
            Assert.AreEqual(PurchaseResult.Success, Shop.TryPurchase(p, ModuleCatalog.EngineSokolM, out _));

            // Science from elsewhere (the public API) can open a tier before its milestone, at the early price.
            Assert.IsTrue(p.AwardScience(TechTree.Get(2).EarlyResearchCost, "test science"));
            Assert.AreEqual(PurchaseResult.Success, TechTree.TryUnlock(p, 2, out _));
            Assert.AreEqual(PurchaseResult.Success, Shop.TryPurchase(p, ModuleCatalog.EngineHydra, out _));
        }

        [Test]
        public void IncomeApi_AddsFundsAndScienceWithAReason()
        {
            PlayerProfile p = Career();
            long lifetime = p.lifetimeEarnings;
            Assert.IsTrue(p.AwardFunds(1234, "Контракт"));
            Assert.AreEqual(GameModeRules.CareerStartingCredits + 1234, p.credits);
            Assert.AreEqual(lifetime + 1234, p.lifetimeEarnings);
            Assert.AreEqual("Контракт", p.ledger[p.ledger.Count - 1].reason);
            Assert.IsFalse(p.AwardFunds(0, "nothing"));
            Assert.IsFalse(p.AwardFunds(-5, "negative"));
            Assert.IsFalse(p.AwardScience(double.NaN, "bad"));
            Assert.IsTrue(p.AwardScience(2.5, "Измерение"));
            Assert.AreEqual(2.5, p.researchPoints, 1e-9);
            for (int i = 0; i < PlayerProfile.LedgerSize + 10; i++) p.AwardFunds(1, "x");
            Assert.AreEqual(PlayerProfile.LedgerSize, p.ledger.Count, "the ledger is bounded");
        }

        [Test]
        public void Catalog_PricesAndTiersAreDataDriven()
        {
            foreach (ModuleDefinition m in ModuleCatalog.All)
            {
                Assert.Greater(m.price, 0, m.id);
                Assert.That(m.techTier, Is.InRange(0, ModuleCatalog.MaxTechTier), m.id);
            }
            // The classic shop prices are kept; free parts got a derived price.
            Assert.AreEqual(6000, ModuleCatalog.Find(ModuleCatalog.TankK140).price);
            Assert.AreEqual(9000, ModuleCatalog.Find(ModuleCatalog.EngineSokolM).price);
            Assert.Greater(ModuleCatalog.Find(ModuleCatalog.TankK100).price, ModuleCatalog.Find(ModuleCatalog.TankK10).price, "bigger costs more");
            Assert.Greater(ModuleCatalog.Find(ModuleCatalog.EngineNuclearLarge).price, ModuleCatalog.Find(ModuleCatalog.EngineSokol).price);
            // The former milestone gates are the tiers of those modules.
            Assert.AreEqual(1, ModuleCatalog.TierOf(ModuleCatalog.RadiationShield));
            Assert.AreEqual(2, ModuleCatalog.TierOf(ModuleCatalog.AcidCoating));
            Assert.AreEqual(3, ModuleCatalog.TierOf(ModuleCatalog.RadiationShieldHeavy));
            // A module without a table entry gets a tier and price from its data.
            var custom = new ModuleDefinition { id = "test-nuke", category = ModuleCategory.Engine, engineKind = EngineKind.Nuclear, dryMass = 4000, vacuumThrust = 8e4 };
            Assert.AreEqual(3, ModuleCatalog.DeriveTier(custom));
            custom.techTier = 3;
            Assert.Greater(ModuleCatalog.DerivePrice(custom), 10000);
        }

        // ───────────── persistence ─────────────

        [Test]
        public void SaveLoad_RoundTripsModeFundsOwnedAndTiers()
        {
            PlayerProfile p = Career();
            ResearchMilestones.Award(p, ResearchMilestones.ReachSpace);
            TechTree.TryUnlock(p, 1, out _);
            Shop.TryPurchase(p, ModuleCatalog.EngineSokolM, out _);

            PlayerProfile loaded = JsonLike.RoundTrip(p);
            Assert.AreEqual(MigrationResult.UpToDate, ProfileMigration.Migrate(loaded));
            Assert.AreEqual(GameMode.Career, loaded.gameMode);
            Assert.AreEqual(p.credits, loaded.credits);
            Assert.AreEqual(p.researchPoints, loaded.researchPoints, 1e-9);
            CollectionAssert.AreEquivalent(p.ownedModules, loaded.ownedModules);
            CollectionAssert.AreEquivalent(p.unlockedTiers, loaded.unlockedTiers);
            Assert.IsTrue(loaded.Owns(ModuleCatalog.EngineSokolM));
            Assert.IsFalse(loaded.Owns(ModuleCatalog.TankK10));
            Assert.AreEqual(p.ledger.Count, loaded.ledger.Count);

            PlayerProfile sandbox = JsonLike.RoundTrip(Sandbox());
            Assert.AreEqual(GameMode.Sandbox, sandbox.gameMode);
        }

        [Test]
        public void OldSaveWithoutMode_LoadsAsSandbox()
        {
            // A v1 file (before game modes): no gameMode, unlockedTiers or ledger.
            const string v1 = "{\"version\":1,\"credits\":4321,\"researchPoints\":12.0,\"ownedModules\":[\"tank-k140\"]," +
                              "\"achievedMilestones\":[\"reach-space\"],\"visitedBodies\":[\"home\"]}";
            PlayerProfile p = JsonLike.Read(v1);
            Assert.AreEqual(MigrationResult.Migrated, ProfileMigration.Migrate(p));
            Assert.AreEqual(PlayerProfile.CurrentVersion, p.version);
            Assert.AreEqual(GameMode.Sandbox, p.gameMode);
            Assert.AreEqual(4321, p.credits, "progress is kept");
            Assert.IsTrue(p.HasMilestone(ResearchMilestones.ReachSpace));
            Assert.IsNotNull(p.unlockedTiers);
            Assert.IsNotNull(p.ledger);
            Assert.IsTrue(p.Owns(ModuleCatalog.EngineNuclearLarge), "nothing the player had is taken away");
        }

        [Test]
        public void DamagedModeValue_NeverBecomesSandbox()
        {
            PlayerProfile p = Career();
            p.gameMode = (GameMode)7;
            Assert.AreEqual(MigrationResult.Migrated, ProfileMigration.Migrate(p));
            Assert.AreEqual(GameMode.Career, p.gameMode);
        }

        /// <summary>
        /// Stand-in for Unity's JsonUtility outside Unity: public fields only, enums as numbers, missing fields keep the field
        /// initializers (the object is created with its default constructor) — the same rules the real save uses.
        /// </summary>
        static class JsonLike
        {
#if UNITY_5_3_OR_NEWER
            // Inside Unity: the real serializer of the save files.
            public static PlayerProfile RoundTrip(PlayerProfile p) => Read(UnityEngine.JsonUtility.ToJson(p));

            public static PlayerProfile Read(string json) => UnityEngine.JsonUtility.FromJson<PlayerProfile>(json);
#else
            static readonly System.Text.Json.JsonSerializerOptions Options = new System.Text.Json.JsonSerializerOptions
            {
                IncludeFields = true,
                IgnoreReadOnlyProperties = true,
            };

            public static PlayerProfile RoundTrip(PlayerProfile p) => Read(System.Text.Json.JsonSerializer.Serialize(p, Options));

            public static PlayerProfile Read(string json) => System.Text.Json.JsonSerializer.Deserialize<PlayerProfile>(json, Options);
#endif
        }
    }
}
