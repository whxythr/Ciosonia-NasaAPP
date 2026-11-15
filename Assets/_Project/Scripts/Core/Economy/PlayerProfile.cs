using System;
using System.Collections.Generic;
using SpaceSim.Localization;
using SpaceSim.Units;
using SpaceSim.Vehicles;

namespace SpaceSim.Economy
{
    public enum SampleCategory
    {
        Rock,
        Mineral,
        RareGeology,
        Soil,
        MineralSoil,
        UnusualCompound,
        Water,
        OtherLiquid,
        Acid
    }

    public enum SampleRarity
    {
        Common,
        Uncommon,
        Rare,
        Exceptional
    }

    /// <summary>A collected sample (created by the exploration systems, M6). SI mass.</summary>
    [Serializable]
    public class SampleRecord
    {
        public string id = "";
        public string name = "";
        public SampleCategory category;
        public string planetId = "";
        /// <summary>[kg]</summary>
        public double mass;
        public SampleRarity rarity;
        /// <summary>Research points the sample is worth when analysed.</summary>
        public double scientificValue;
        /// <summary>Base price in credits before analysis bonuses.</summary>
        public int basePrice;
        /// <summary>Analysed on site with a spectrometer (raises value).</summary>
        public bool analysed;
        public double analysisMultiplier = 1.0;
        /// <summary>Brought back to the start planet (only delivered samples can be sold; cargo is lost with the rocket).</summary>
        public bool delivered;
    }

    [Serializable]
    public class PlayerSettings
    {
        public DisplayUnitSystem units = DisplayUnitSystem.SI;
        /// <summary>Flight planning mode for M5: true = Assisted, false = Manual.</summary>
        public bool assistedPlanning = true;
    }

    /// <summary>Flight Academy progress (lessons opened, exercises solved). Additive: older saves simply lack it.</summary>
    [Serializable]
    public class AcademyProgress
    {
        public List<string> readTopics = new List<string>();
        public List<string> solvedExercises = new List<string>();

        public bool HasRead(string topicId) => readTopics.Contains(topicId);
        public bool HasSolved(string exerciseId) => solvedExercises.Contains(exerciseId);
    }

    /// <summary>One line of the money/research history (newest last; the profile keeps the last <see cref="PlayerProfile.LedgerSize"/>).</summary>
    [Serializable]
    public class LedgerEntry
    {
        public string reason = "";
        /// <summary>Credits gained (+) or spent (−).</summary>
        public int credits;
        /// <summary>Research points gained (+) or spent (−).</summary>
        public double research;
    }

    /// <summary>
    /// Everything that persists between game sessions (no transient physics state). Versioned: see
    /// <see cref="ProfileMigration"/>. Plain serializable data — the Runtime layer writes it as JSON.
    /// </summary>
    [Serializable]
    public class PlayerProfile
    {
        /// <summary>v2: game mode, career tiers, ledger.</summary>
        public const int CurrentVersion = 2;
        public const int StartingCredits = 10000;
        public const int LedgerSize = 40;

        public int version = CurrentVersion;
        /// <summary>
        /// Sandbox or Career: set by <see cref="CreateNew(string, GameMode)"/> and never changed afterwards (no setter API).
        /// A file without the field (v1 and older) loads as Sandbox — see <see cref="ProfileMigration"/>.
        /// </summary>
        public GameMode gameMode = GameMode.Sandbox;
        /// <summary>Career technology tiers opened (tier 0 is always open and not listed).</summary>
        public List<int> unlockedTiers = new List<int>();
        /// <summary>Recent income and spending (shop «Финансы»).</summary>
        public List<LedgerEntry> ledger = new List<LedgerEntry>();
        public int credits = StartingCredits;
        public double researchPoints;
        /// <summary>Purchased module ids (free modules are always available).</summary>
        public List<string> ownedModules = new List<string>();
        public RocketBlueprint blueprint = RocketBlueprint.CreateDefault();
        public List<SampleRecord> samples = new List<SampleRecord>();
        /// <summary>Bodies the player has reached (discoveries).</summary>
        public List<string> visitedBodies = new List<string>();
        /// <summary>Bodies the player may select as destinations (navigation, M5).</summary>
        public List<string> unlockedBodies = new List<string>();
        public List<string> achievedMilestones = new List<string>();
        /// <summary>Sample sites already taken (sites are deterministic: the same rock cannot be collected twice).</summary>
        public List<string> collectedSites = new List<string>();
        public PlayerSettings settings = new PlayerSettings();
        public AcademyProgress academy = new AcademyProgress();
        public string lastSavedUtc = "";
        /// <summary>Total credits ever earned (statistics).</summary>
        public long lifetimeEarnings;
        /// <summary>The astronauts of the programme (filled with the starting crew on load).</summary>
        public SpaceSim.Crew.CrewRosterData crew = new SpaceSim.Crew.CrewRosterData();
        /// <summary>Real time played in the flight scene [s] (shown in the load screen).</summary>
        public double playSeconds;
        /// <summary>
        /// Goal of the game this profile was last started with: a research mission id from the main menu ("weather-watch", …)
        /// or "sandbox". Additive: saves from before this field read as "" and are treated as the sandbox.
        /// </summary>
        public string scenarioId = "";
        /// <summary>Orbital science maps (scanned cells and measured values per body). Additive: older saves simply lack it.</summary>
        public SpaceSim.Science.ScienceArchive science = new SpaceSim.Science.ScienceArchive();
        /// <summary>
        /// Career: the share of each body/layer map already paid for (<see cref="SpaceSim.Science.ScienceRewards"/>), saved with the
        /// maps so a reload never pays twice. Additive: older saves simply lack it.
        /// </summary>
        public List<SpaceSim.Science.ScienceRewardRecord> scienceRewards = new List<SpaceSim.Science.ScienceRewardRecord>();

        /// <summary>A new Sandbox profile (also what the flight scene creates when there is no save at all).</summary>
        public static PlayerProfile CreateNew(string homeBodyId) => CreateNew(homeBodyId, GameMode.Sandbox);

        /// <summary>A new profile in the given mode. Career: <see cref="GameModeRules.CareerStartingCredits"/> and the starter kit owned.</summary>
        public static PlayerProfile CreateNew(string homeBodyId, GameMode mode)
        {
            var p = new PlayerProfile { gameMode = mode };
            if (!string.IsNullOrEmpty(homeBodyId))
            {
                p.visitedBodies.Add(homeBodyId);
                p.unlockedBodies.Add(homeBodyId);
            }
            if (mode == GameMode.Career)
            {
                p.credits = GameModeRules.CareerStartingCredits;
                foreach (string id in ModuleCatalog.CareerStarterKit) p.ownedModules.Add(id);
                p.Record(Loc.T("Стартовый бюджет карьеры"), p.credits, 0.0);
            }
            SpaceSim.Crew.CrewRoster.EnsureRoster(p.crew);
            return p;
        }

        public bool IsCareer => gameMode == GameMode.Career;
        public bool IsSandbox => gameMode != GameMode.Career;

        /// <summary>
        /// True if the module may be used. Sandbox: every catalog module. Career: only modules the player owns (the starter kit and
        /// purchases; a purchase needs its tier open).
        /// </summary>
        public bool Owns(string moduleId)
        {
            ModuleDefinition m = ModuleCatalog.Find(moduleId);
            if (m == null) return false;
            if (!IsCareer) return true;
            return ownedModules.Contains(moduleId);
        }

        /// <summary>Career: the module's technology tier is open (always true in Sandbox).</summary>
        public bool TierUnlockedFor(string moduleId) => TechTree.IsUnlocked(this, ModuleCatalog.TierOf(moduleId));

        // ───────────── public income API (milestones, market, science instruments, future contracts) ─────────────

        /// <summary>
        /// Adds credits from any source (science results, contracts, rewards) and logs <paramref name="reason"/> in the ledger.
        /// Counts toward <see cref="lifetimeEarnings"/>. Works in both modes (Sandbox simply never needs the money).
        /// False (nothing changes) for a non-positive amount. The Runtime wrapper that also saves is PlayerSession.AwardFunds.
        /// </summary>
        public bool AwardFunds(int amount, string reason) => Award(amount, 0.0, reason);

        /// <summary>Adds research points (science) and logs the reason. False for a non-positive or invalid amount.</summary>
        public bool AwardScience(double points, string reason) => Award(0, points, reason);

        /// <summary>Credits and/or research points for one reason (one ledger line). Negative or invalid parts are ignored.</summary>
        public bool Award(int funds, double science, string reason)
        {
            if (funds < 0) funds = 0;
            if (!(science > 0.0) || double.IsInfinity(science)) science = 0.0;
            if (funds == 0 && science == 0.0) return false;
            credits += funds;
            lifetimeEarnings += funds;
            researchPoints += science;
            Record(reason, funds, science);
            return true;
        }

        /// <summary>Appends a ledger line (keeps the last <see cref="LedgerSize"/>).</summary>
        public void Record(string reason, int creditsDelta, double researchDelta)
        {
            if (ledger == null) ledger = new List<LedgerEntry>();
            ledger.Add(new LedgerEntry { reason = reason ?? "", credits = creditsDelta, research = researchDelta });
            if (ledger.Count > LedgerSize) ledger.RemoveRange(0, ledger.Count - LedgerSize);
        }

        public bool HasMilestone(string id) => achievedMilestones.Contains(id);

        public double SampleMass()
        {
            double m = 0.0;
            foreach (var s in samples) m += s.mass;
            return m;
        }

        /// <summary>Mass of samples still on board the rocket (collected, not yet delivered) [kg].</summary>
        public double CargoMass()
        {
            double m = 0.0;
            foreach (var s in samples)
                if (!s.delivered) m += s.mass;
            return m;
        }

        /// <summary>Puts a collected sample on board. False if the site was already taken.</summary>
        public bool AddCargo(SampleRecord sample)
        {
            if (sample == null || collectedSites.Contains(sample.id)) return false;
            sample.delivered = false;
            samples.Add(sample);
            collectedSites.Add(sample.id);
            return true;
        }

        /// <summary>The rocket is back on the start planet: everything on board becomes sellable. Returns how many.</summary>
        public int DeliverCargo()
        {
            int n = 0;
            foreach (var s in samples)
                if (!s.delivered)
                {
                    s.delivered = true;
                    n++;
                }
            return n;
        }

        /// <summary>The rocket was lost or replaced: undelivered samples are gone (their sites stay taken). Returns how many.</summary>
        public int LoseCargo() => samples.RemoveAll(s => !s.delivered);

        internal void Earn(int amount, string reason = "") => AwardFunds(amount, reason);
    }

    public enum MigrationResult
    {
        UpToDate,
        Migrated,
        /// <summary>Saved by a newer build: must not be overwritten by this one.</summary>
        TooNew
    }

    /// <summary>Upgrades old profiles step by step; never silently downgrades a newer save.</summary>
    public static class ProfileMigration
    {
        public static MigrationResult Migrate(PlayerProfile p)
        {
            if (p.version > PlayerProfile.CurrentVersion) return MigrationResult.TooNew;
            bool changed = false;
            if (p.version < 1)
            {
                // v0 (pre-release) → v1: fill collections that older files may lack.
                p.version = 1;
                changed = true;
            }
            if (p.version < 2)
            {
                // v1 → v2: game modes. A v1 file has no gameMode, so JsonUtility leaves the default Sandbox. Its credits and
                // ownedModules come from the old hybrid economy where almost every part was free: turning it into a Career would
                // take away parts the player was flying with, so it stays Sandbox (credits, purchases and milestones are kept).
                p.version = 2;
                changed = true;
            }
            if (!Enum.IsDefined(typeof(GameMode), p.gameMode))
            {
                // A damaged / hand-edited value never turns into a free-for-all: the stricter mode wins.
                p.gameMode = GameMode.Career;
                changed = true;
            }
            if (p.unlockedTiers == null) { p.unlockedTiers = new List<int>(); changed = true; }
            if (p.ledger == null) { p.ledger = new List<LedgerEntry>(); changed = true; }
            if (p.science == null) { p.science = new SpaceSim.Science.ScienceArchive(); changed = true; }
            if (p.scienceRewards == null) { p.scienceRewards = new List<SpaceSim.Science.ScienceRewardRecord>(); changed = true; }
            if (p.unlockedTiers.RemoveAll(t => t <= 0 || t > TechTree.MaxTier) > 0) changed = true;
            if (p.ownedModules == null) { p.ownedModules = new List<string>(); changed = true; }
            if (p.samples == null) { p.samples = new List<SampleRecord>(); changed = true; }
            if (p.visitedBodies == null) { p.visitedBodies = new List<string>(); changed = true; }
            if (p.unlockedBodies == null) { p.unlockedBodies = new List<string>(); changed = true; }
            if (p.achievedMilestones == null) { p.achievedMilestones = new List<string>(); changed = true; }
            if (p.collectedSites == null) { p.collectedSites = new List<string>(); changed = true; }
            if (p.settings == null) { p.settings = new PlayerSettings(); changed = true; }
            if (p.academy == null) { p.academy = new AcademyProgress(); changed = true; }
            if (p.academy.readTopics == null) { p.academy.readTopics = new List<string>(); changed = true; }
            if (p.crew == null) { p.crew = new SpaceSim.Crew.CrewRosterData(); changed = true; }
            if (p.crew.members == null) { p.crew.members = new List<SpaceSim.Crew.CrewMember>(); changed = true; }
            foreach (SpaceSim.Crew.CrewMember m in p.crew.members) if (m.bodiesVisited == null) m.bodiesVisited = new List<string>();
            SpaceSim.Crew.CrewRoster.EnsureRoster(p.crew); // the starting crew is content, not a format change: no "migrated" flag
            if (p.academy.solvedExercises == null) { p.academy.solvedExercises = new List<string>(); changed = true; }
            if (p.blueprint == null || p.blueprint.stages == null || p.blueprint.stages.Count == 0)
            {
                p.blueprint = RocketBlueprint.CreateDefault();
                changed = true;
            }
            if (p.blueprint.EnsureLists()) changed = true; // stage attachments did not exist in the first saves
            // Drop module ids this build no longer knows (keeps the design loadable).
            if (p.ownedModules.RemoveAll(id => ModuleCatalog.Find(id) == null) > 0) changed = true;
            return changed ? MigrationResult.Migrated : MigrationResult.UpToDate;
        }
    }
}
