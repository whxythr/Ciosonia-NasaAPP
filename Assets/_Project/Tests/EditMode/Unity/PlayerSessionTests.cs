using System.IO;
using NUnit.Framework;
using SpaceSim.Economy;
using SpaceSim.Planets;
using SpaceSim.Runtime;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Tests
{
    /// <summary>Save/load of the player profile through Unity's JsonUtility, in a temporary folder.</summary>
    public class PlayerSessionTests
    {
        string dir;
        GameObject go;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "SpaceSimSessionTest_" + System.Guid.NewGuid().ToString("N"));
            PlayerSession.SaveDirectoryOverride = dir;
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
            PlayerSession.SaveDirectoryOverride = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        PlayerSession NewSession()
        {
            if (go != null) Object.DestroyImmediate(go);
            go = new GameObject("session");
            var s = go.AddComponent<PlayerSession>();
            s.Initialize(HomeSystemCatalog.HomeWorldId);
            return s;
        }

        [Test]
        public void Transactions_SurviveRestart()
        {
            PlayerSession.WriteProfileFile(PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId, GameMode.Career)); // as the main menu does
            var s = NewSession();
            Assert.AreEqual(PurchaseResult.Success, s.Purchase(ModuleCatalog.TankK140, out _));
            s.Award(ResearchMilestones.ReachSpace);
            Assert.AreEqual(PurchaseResult.Success, s.UnlockTier(1, out _));
            Assert.IsTrue(s.AwardFunds(500, "test"));
            var design = RocketBlueprint.CreateDefault();
            design.stages[1].tanks[0].propellantLoaded = 12345;
            design.payloadModules.Add(ModuleCatalog.NavComputer);
            s.SetBlueprint(design);
            s.Profile.samples.Add(new SampleRecord { id = "s1", name = "Базальт", planetId = "teonan", mass = 1.5, basePrice = 200 });
            s.SetUnits(SpaceSim.Units.DisplayUnitSystem.Kilometric);
            int credits = s.Profile.credits;

            var restarted = NewSession(); // reads the file written above
            var p = restarted.Profile;
            Assert.AreEqual(credits, p.credits);
            Assert.AreEqual(GameMode.Career, p.gameMode, "the mode survives a restart");
            Assert.IsTrue(TechTree.IsUnlocked(p, 1));
            Assert.IsTrue(p.Owns(ModuleCatalog.TankK140));
            Assert.IsFalse(p.Owns(ModuleCatalog.TankK10), "career: not bought");
            Assert.IsTrue(p.HasMilestone(ResearchMilestones.ReachSpace));
            Assert.AreEqual(12345.0, p.blueprint.stages[1].tanks[0].propellantLoaded);
            Assert.Contains(ModuleCatalog.NavComputer, p.blueprint.payloadModules);
            Assert.AreEqual("Базальт", p.samples[0].name);
            Assert.AreEqual(SpaceSim.Units.DisplayUnitSystem.Kilometric, p.settings.units);
            Assert.IsFalse(File.Exists(PlayerSession.SavePath + ".tmp"), "atomic save leaves no temp file");
        }

        [Test]
        public void SaveWithoutGameMode_LoadsAsSandbox()
        {
            Directory.CreateDirectory(dir);
            // A v1 profile as written before game modes existed: no gameMode / unlockedTiers / ledger fields.
            File.WriteAllText(PlayerSession.SavePath,
                "{\"version\":1,\"credits\":4321,\"researchPoints\":12.0,\"ownedModules\":[\"tank-k140\"],\"achievedMilestones\":[\"reach-space\"]}");
            var s = NewSession();
            Assert.AreEqual(GameMode.Sandbox, s.Mode);
            Assert.AreEqual(4321, s.Profile.credits);
            Assert.AreEqual(PlayerProfile.CurrentVersion, s.Profile.version);
            Assert.IsTrue(s.Profile.Owns(ModuleCatalog.EngineNuclearLarge), "sandbox: everything available");
            Assert.AreEqual(PurchaseResult.SandboxMode, s.Purchase(ModuleCatalog.TankK10, out _));
        }

        [Test]
        public void NewGame_KeepsTheOldGameAsASlot()
        {
            PlayerSession.WriteProfileFile(PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId, GameMode.Career));
            string backup = NewGame.Start(GameMode.Sandbox);
            Assert.IsNotEmpty(backup);
            Assert.AreEqual(GameMode.Sandbox, PlayerSession.ReadProfileFile().gameMode);
            SaveSlotMeta kept = SaveSlots.List().Find(m => !m.IsAutosave);
            Assert.IsNotNull(kept);
            Assert.AreEqual(GameMode.Career, kept.gameMode, "the career is still there, as a career");
        }

        [Test]
        public void CorruptSave_IsBackedUp_AndANewProfileStarts()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(PlayerSession.SavePath, "{ this is not json");
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("повреждено"));
            var s = NewSession();
            Assert.AreEqual(PlayerProfile.StartingCredits, s.Profile.credits);
            Assert.AreEqual(1, Directory.GetFiles(dir, "profile.json.corrupt-*").Length, "the broken file is kept");
        }

        [Test]
        public void SaveFromNewerVersion_IsNeverOverwritten()
        {
            Directory.CreateDirectory(dir);
            var future = PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId);
            future.version = 99;
            future.credits = 777;
            string json = JsonUtility.ToJson(future);
            File.WriteAllText(PlayerSession.SavePath, json);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("новой версией"));
            var s = NewSession();
            Assert.IsTrue(s.ReadOnly);
            Assert.AreEqual(PurchaseResult.ReadOnlyProfile, s.Purchase(ModuleCatalog.TankK140, out _), "no trading on a read-only profile");
            Assert.IsFalse(s.Profile.ownedModules.Contains(ModuleCatalog.TankK140));
            Assert.AreEqual(json, File.ReadAllText(PlayerSession.SavePath), "the file stays untouched");
        }
    }
}
