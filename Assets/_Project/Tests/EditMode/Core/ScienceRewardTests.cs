using NUnit.Framework;
using SpaceSim.Economy;
using SpaceSim.Planets;
using SpaceSim.Science;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    /// <summary>
    /// Integration of orbital mapping with the career economy (<see cref="ScienceRewards"/>): research points and credits once per
    /// newly mapped percent, persisted with the maps, nothing in Sandbox, written off after test tools; and the scanners' career data.
    /// </summary>
    public class ScienceRewardTests
    {
        const string Far = HomeSystemCatalog.TromosId;

        static PlayerProfile Career() => PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId, GameMode.Career);

        /// <summary>Measures the first <paramref name="share"/> of the cells of one layer (by index: whole latitude rows).</summary>
        static void Cover(ScienceMapStore store, string bodyId, ScienceLayer layer, double share, bool airless = false)
        {
            ScienceBodyMap map = store.GetOrCreate(bodyId, bodyId);
            map.Airless = airless;
            ScienceLayerMap l = map[layer];
            var values = new float[l.ChannelCount];
            int n = (int)(l.Grid.CellCount * share);
            for (int i = 0; i < n; i++) l.Store(i, values);
        }

        static void CoverAll(ScienceMapStore store, string bodyId, ScienceLayer layer, bool airless = false) => Cover(store, bodyId, layer, 1.0, airless);

        [Test]
        public void Career_FullMapPaysTheTableRateOnce()
        {
            PlayerProfile p = Career();
            int credits0 = p.credits;
            double research0 = p.researchPoints;
            var store = new ScienceMapStore();
            CoverAll(store, Far, ScienceLayer.Terrain);

            ScienceRewardPayout pay = ScienceRewards.Collect(p, store, HomeSystemCatalog.HomeWorldId);
            ScienceRewards.LayerRate rate = ScienceRewards.Table[(int)ScienceLayer.Terrain];
            Assert.IsTrue(pay.Paid);
            Assert.AreEqual(100, pay.Percent);
            Assert.AreEqual(rate.CreditsPerFullMap, p.credits - credits0);
            Assert.AreEqual(rate.ResearchPerFullMap, p.researchPoints - research0, 1e-9);
            Assert.AreEqual(pay.Reason, p.ledger[p.ledger.Count - 1].reason, "one ledger line per settlement");

            ScienceRewardPayout again = ScienceRewards.Collect(p, store, HomeSystemCatalog.HomeWorldId);
            Assert.IsFalse(again.Paid, "the same coverage is never paid twice");
            Assert.AreEqual(credits0 + rate.CreditsPerFullMap, p.credits);
        }

        [Test]
        public void Career_PaysOnlyTheNewlyMappedShare()
        {
            PlayerProfile p = Career();
            var store = new ScienceMapStore();
            Cover(store, Far, ScienceLayer.Temperature, 0.3);
            ScienceRewards.Collect(p, store, HomeSystemCatalog.HomeWorldId);
            int first = ScienceRewards.PercentOf(store.Bodies[0][ScienceLayer.Temperature].Coverage);
            Assert.Greater(first, 0);
            double researchAfterFirst = p.researchPoints;

            CoverAll(store, Far, ScienceLayer.Temperature);
            ScienceRewardPayout pay = ScienceRewards.Collect(p, store, HomeSystemCatalog.HomeWorldId);
            Assert.AreEqual(100 - first, pay.Percent);
            Assert.AreEqual(ScienceRewards.Table[(int)ScienceLayer.Temperature].ResearchPerFullMap * (100 - first) / 100.0,
                p.researchPoints - researchAfterFirst, 1e-9);
        }

        [Test]
        public void Sandbox_EarnsAndRecordsNothing()
        {
            PlayerProfile p = PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId, GameMode.Sandbox);
            int credits0 = p.credits, ledger0 = p.ledger.Count;
            var store = new ScienceMapStore();
            CoverAll(store, Far, ScienceLayer.Terrain);
            Assert.IsFalse(ScienceRewards.Collect(p, store, HomeSystemCatalog.HomeWorldId).Paid);
            Assert.AreEqual(credits0, p.credits);
            Assert.AreEqual(0.0, p.researchPoints);
            Assert.AreEqual(ledger0, p.ledger.Count);
            Assert.AreEqual(0, p.scienceRewards.Count);
        }

        [Test]
        public void TestToolCoverage_IsWrittenOffUnpaid()
        {
            PlayerProfile p = Career();
            int credits0 = p.credits;
            var store = new ScienceMapStore();
            CoverAll(store, Far, ScienceLayer.Pressure);
            ScienceRewardPayout pay = ScienceRewards.Collect(p, store, HomeSystemCatalog.HomeWorldId, forfeit: true);
            Assert.IsFalse(pay.Paid);
            Assert.AreEqual(100, pay.Percent);
            Assert.IsFalse(ScienceRewards.Collect(p, store, HomeSystemCatalog.HomeWorldId).Paid, "a later real flight cannot cash it in");
            Assert.AreEqual(credits0, p.credits);
        }

        [Test]
        public void HomeBodyPaysHalf_AirlessPressurePaysNothing()
        {
            PlayerProfile p = Career();
            var store = new ScienceMapStore();
            CoverAll(store, HomeSystemCatalog.HomeWorldId, ScienceLayer.Temperature);
            ScienceRewards.Collect(p, store, HomeSystemCatalog.HomeWorldId);
            Assert.AreEqual(ScienceRewards.Table[0].ResearchPerFullMap * ScienceRewards.HomeBodyFactor, p.researchPoints, 1e-9);

            double before = p.researchPoints;
            CoverAll(store, Far, ScienceLayer.Pressure, airless: true);
            Assert.IsFalse(ScienceRewards.Collect(p, store, HomeSystemCatalog.HomeWorldId).Paid);
            Assert.AreEqual(before, p.researchPoints);
        }

        [Test]
        public void SaveLoad_DoesNotPayTwice()
        {
            PlayerProfile p = Career();
            var store = new ScienceMapStore();
            Cover(store, Far, ScienceLayer.Terrain, 0.5);
            ScienceRewards.Collect(p, store, HomeSystemCatalog.HomeWorldId);
            p.science = ScienceArchiveCodec.Write(store); // what ScienceSession.Sync writes before settling

            PlayerProfile loaded = JsonLike.RoundTrip(p);
            Assert.AreEqual(MigrationResult.UpToDate, ProfileMigration.Migrate(loaded));
            Assert.AreEqual(GameMode.Career, loaded.gameMode);
            Assert.AreEqual(p.credits, loaded.credits);
            Assert.AreEqual(p.scienceRewards.Count, loaded.scienceRewards.Count);
            ScienceMapStore reloaded = ScienceArchiveCodec.Read(loaded.science);
            Assert.AreEqual(store.Bodies[0][ScienceLayer.Terrain].CoveredCount, reloaded.Bodies[0][ScienceLayer.Terrain].CoveredCount);

            Assert.IsFalse(ScienceRewards.Collect(loaded, reloaded, HomeSystemCatalog.HomeWorldId).Paid, "reloading never pays again");
        }

        [Test]
        public void OldSaveWithoutRewardRecords_Migrates()
        {
            // A v2 save from before the science rewards: no scienceRewards (and a v1 one without science maps either).
            PlayerProfile p = JsonLike.Read("{\"version\":2,\"gameMode\":1,\"credits\":500}");
            p.scienceRewards = null;
            p.science = null;
            Assert.AreEqual(MigrationResult.Migrated, ProfileMigration.Migrate(p));
            Assert.IsNotNull(p.scienceRewards);
            Assert.IsNotNull(p.science);
            Assert.AreEqual(GameMode.Career, p.gameMode);
        }

        [Test]
        public void Scanners_AreOrbitalTierModulesWithAPrice()
        {
            foreach (string id in new[] { ModuleCatalog.TemperatureRadar, ModuleCatalog.PressureRadar, ModuleCatalog.TerrainScanner })
            {
                Assert.AreEqual(2, ModuleCatalog.TierOf(id), id);
                Assert.Greater(ModuleCatalog.Find(id).price, 0, id);
                PlayerProfile career = Career();
                Assert.IsFalse(career.Owns(id), "a new career has to buy the scanners");
                Assert.IsTrue(PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId, GameMode.Sandbox).Owns(id), "sandbox has them all");
                career.unlockedTiers.Add(1);
                career.unlockedTiers.Add(2);
                career.credits = 100000;
                Assert.AreEqual(PurchaseResult.Success, Shop.TryPurchase(career, id, out string msg), msg);
                Assert.IsTrue(career.Owns(id));
            }
        }

        /// <summary>JsonUtility inside Unity, System.Text.Json (public fields) in the standalone test project.</summary>
        static class JsonLike
        {
#if UNITY_5_3_OR_NEWER
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
