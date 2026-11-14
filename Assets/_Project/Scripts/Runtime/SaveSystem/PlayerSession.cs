using SpaceSim.Localization;
using System;
using System.IO;
using SpaceSim.Economy;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Owns the player's persistent <see cref="PlayerProfile"/>: loads it at start, applies transactions through the
    /// Core rules (<see cref="Shop"/>, <see cref="Market"/>, <see cref="ResearchMilestones"/>) and saves after every change.
    /// Saving is atomic (temp file + replace); a corrupt file is kept as a backup; a profile written by a newer build is
    /// never overwritten (read-only session).
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class PlayerSession : MonoBehaviour
    {
        /// <summary>Tests point this at a temporary folder so they never touch the player's real save.</summary>
        public static string SaveDirectoryOverride;

        public static string SaveDirectory => SaveDirectoryOverride ?? Path.Combine(Application.persistentDataPath, "Player");
        public static string SavePath => Path.Combine(SaveDirectory, "profile.json");

        public PlayerProfile Profile { get; private set; }
        /// <summary>The save on disk is from a newer game version: play continues, nothing is written.</summary>
        public bool ReadOnly { get; private set; }
        public string LoadMessage { get; private set; } = "";

        /// <summary>Raised after any change to the profile (purchase, sale, milestone, design, settings).</summary>
        public event Action Changed;

        string initializedPath;

        /// <summary>
        /// Set by the main menu right before it loads the flight scene: the goal the player just started ("sandbox" or a research
        /// mission id). The next <see cref="Initialize"/> writes it into <see cref="PlayerProfile.scenarioId"/> (so the autosave
        /// knows its goal) and clears it. Null = keep the goal stored in the profile («Продолжить», loading a save).
        /// </summary>
        public static string PendingScenarioId;

        /// <summary>The scenario id stored in a profile, with old saves (no field) read as the sandbox.</summary>
        public static string ScenarioOf(PlayerProfile p) =>
            p == null || string.IsNullOrEmpty(p.scenarioId) ? SandboxScenarioId : p.scenarioId;

        public const string SandboxScenarioId = "sandbox";

        public void Initialize(string homeBodyId)
        {
            initializedPath = SavePath;
            Profile = Load(homeBodyId, out string message, out bool readOnly);
            LoadMessage = message;
            ReadOnly = readOnly;
            if (PendingScenarioId != null)
            {
                Profile.scenarioId = PendingScenarioId;
                PendingScenarioId = null;
            }
            // Every scene starts with a fresh rocket on the pad: a flight left in the air last time is over.
            if (Profile.crew != null) SpaceSim.Crew.CrewRoster.EndFlight(Profile.crew);
            if (!readOnly) Save();
        }

        static PlayerProfile Load(string homeBodyId, out string message, out bool readOnly)
        {
            readOnly = false;
            string path = SavePath;
            if (!File.Exists(path))
            {
                message = Loc.T("Новый профиль: ") + PlayerProfile.StartingCredits + Loc.T(" кр.");
                return PlayerProfile.CreateNew(homeBodyId);
            }
            try
            {
                var p = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(path));
                if (p == null) throw new InvalidDataException("empty profile");
                switch (ProfileMigration.Migrate(p))
                {
                    case MigrationResult.TooNew:
                        readOnly = true;
                        message = Loc.T("Сохранение создано более новой версией игры (v") + p.version + Loc.T(") — профиль только для чтения, изменения не сохраняются.");
                        Debug.LogWarning("[SpaceSim] " + message);
                        return p;
                    case MigrationResult.Migrated:
                        message = Loc.T("Профиль обновлён до версии ") + PlayerProfile.CurrentVersion + ".";
                        return p;
                    default:
                        message = Loc.T("Профиль загружен: ") + p.credits + Loc.T(" кр.");
                        return p;
                }
            }
            catch (Exception e)
            {
                string backup = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json";
                try { File.Move(path, backup); } catch { /* keep going with a fresh profile */ }
                message = Loc.T("Сохранение повреждено (") + e.Message + Loc.T(") — сохранено как ") + Path.GetFileName(backup) + Loc.T(", создан новый профиль.");
                Debug.LogWarning("[SpaceSim] " + message);
                return PlayerProfile.CreateNew(homeBodyId);
            }
        }

        public void Save()
        {
            if (ReadOnly || Profile == null) return;
            Directory.CreateDirectory(SaveDirectory);
            Profile.lastSavedUtc = DateTime.UtcNow.ToString("o");
            string path = SavePath, tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(Profile, true));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        void Commit()
        {
            Save();
            Changed?.Invoke();
        }

        public bool Owns(string moduleId) => Profile != null && Profile.Owns(moduleId);

        public PurchaseResult Purchase(string moduleId, out string message)
        {
            if (ReadOnly)
            {
                message = Loc.T("Профиль только для чтения (сохранение от более новой версии игры) — покупки недоступны.");
                return PurchaseResult.ReadOnlyProfile;
            }
            PurchaseResult r = Shop.TryPurchase(Profile, moduleId, out message);
            if (r == PurchaseResult.Success) Commit();
            return r;
        }

        public bool SellSample(string sampleId, out int credits)
        {
            credits = 0;
            if (ReadOnly) return false;
            bool ok = Market.Sell(Profile, sampleId, out credits);
            if (ok) Commit();
            return ok;
        }

        /// <summary>Grants a research milestone once; returns it if newly achieved.</summary>
        public MilestoneDefinition Award(string milestoneId)
        {
            MilestoneDefinition m = ResearchMilestones.Award(Profile, milestoneId);
            if (m != null) Commit();
            return m;
        }

        public void SetBlueprint(RocketBlueprint design)
        {
            if (design == null) return;
            Profile.blueprint = design.Clone();
            Commit();
        }

        public void MarkVisited(string bodyId)
        {
            if (string.IsNullOrEmpty(bodyId) || Profile.visitedBodies.Contains(bodyId)) return;
            Profile.visitedBodies.Add(bodyId);
            if (!Profile.unlockedBodies.Contains(bodyId)) Profile.unlockedBodies.Add(bodyId);
            Commit();
        }

        /// <summary>Puts a collected sample on board (saved at once). False if that site was already taken or the profile is read-only.</summary>
        public bool AddCargo(SampleRecord sample)
        {
            if (ReadOnly || !Profile.AddCargo(sample)) return false;
            Commit();
            return true;
        }

        /// <summary>The rocket is back home: on-board samples become sellable. Returns how many.</summary>
        public int DeliverCargo()
        {
            int n = Profile.DeliverCargo();
            if (n > 0) Commit();
            return n;
        }

        /// <summary>The rocket is lost or replaced: on-board samples are gone. Returns how many.</summary>
        public int LoseCargo()
        {
            int n = Profile.LoseCargo();
            if (n > 0) Commit();
            return n;
        }

        /// <summary>Records that a Flight Academy lesson was opened.</summary>
        public void MarkTopicRead(string topicId)
        {
            if (string.IsNullOrEmpty(topicId) || Profile.academy.HasRead(topicId)) return;
            Profile.academy.readTopics.Add(topicId);
            Commit();
        }

        /// <summary>Records a solved Flight Academy exercise.</summary>
        public void MarkExerciseSolved(string exerciseId)
        {
            if (string.IsNullOrEmpty(exerciseId) || Profile.academy.HasSolved(exerciseId)) return;
            Profile.academy.solvedExercises.Add(exerciseId);
            Commit();
        }

        /// <summary>Flight planning mode: assisted (suggestions, hints) or manual (the player computes everything).</summary>
        public void SetAssistedPlanning(bool assisted)
        {
            if (Profile.settings.assistedPlanning == assisted) return;
            Profile.settings.assistedPlanning = assisted;
            Commit();
        }

        public void SetUnits(SpaceSim.Units.DisplayUnitSystem units)
        {
            if (Profile.settings.units == units) return;
            Profile.settings.units = units;
            Commit();
        }

        // ───────────────────────────── crew (Tab screen) ─────────────────────────────

        /// <summary>A launch from the pad: the crew boards (seats of the crewed cabin; 0 = probe). Returns the flight number.</summary>
        public int BeginCrewFlight(int seats)
        {
            FlushCrewTime();
            int n = SpaceSim.Crew.CrewRoster.BeginFlight(Profile.crew, seats);
            Commit();
            return n;
        }

        /// <summary>The flight is over (new rocket, recovery, loss): its crew is free again.</summary>
        public void EndCrewFlight()
        {
            if (Profile.crew.activeFlight == 0 && SpaceSim.Crew.CrewRoster.Aboard(Profile.crew).Count == 0) return;
            FlushCrewTime();
            SpaceSim.Crew.CrewRoster.EndFlight(Profile.crew);
            Commit();
        }

        double pendingSpaceSeconds;
        float lastCrewFlush;

        /// <summary>Simulation time above the space boundary; written to the profile now and then (not every frame).</summary>
        public void AddCrewTimeInSpace(double seconds)
        {
            if (seconds <= 0.0 || double.IsNaN(seconds)) return;
            pendingSpaceSeconds += seconds;
            if (Time.unscaledTime - lastCrewFlush > 20f) FlushCrewTime(save: true);
        }

        void FlushCrewTime(bool save = false)
        {
            lastCrewFlush = Time.unscaledTime;
            if (pendingSpaceSeconds <= 0.0 || Profile == null) return;
            SpaceSim.Crew.CrewRoster.AddTimeInSpace(Profile.crew, pendingSpaceSeconds);
            pendingSpaceSeconds = 0.0;
            if (save) Save();
        }

        public void CrewVisited(string bodyId)
        {
            if (SpaceSim.Crew.CrewRoster.Aboard(Profile.crew).Count == 0) return;
            SpaceSim.Crew.CrewRoster.VisitBody(Profile.crew, bodyId);
            Commit();
        }

        public void CrewEva()
        {
            if (SpaceSim.Crew.CrewRoster.RecordEva(Profile.crew)) Commit();
        }

        public bool SendCrewToTraining(string id)
        {
            if (ReadOnly || !SpaceSim.Crew.CrewRoster.SendToTraining(Profile.crew, id)) return false;
            Commit();
            return true;
        }

        public bool HireCrew(string id)
        {
            if (ReadOnly || !SpaceSim.Crew.CrewRoster.Hire(Profile, Profile.crew, id, out _)) return false;
            Commit();
            return true;
        }

        /// <summary>Real time played is counted while the flight scene runs.</summary>
        void Update()
        {
            if (Profile != null) Profile.playSeconds += Time.unscaledDeltaTime;
        }

        // ───────────────────────────── profile file for the main menu ─────────────────────────────

        /// <summary>Reads the saved profile without a session (main menu): null when there is none or it cannot be read.</summary>
        public static PlayerProfile ReadProfileFile(string path = null)
        {
            path = path ?? SavePath;
            try
            {
                if (!File.Exists(path)) return null;
                var p = JsonUtility.FromJson<PlayerProfile>(File.ReadAllText(path));
                if (p == null || ProfileMigration.Migrate(p) == MigrationResult.TooNew) return null;
                return p;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Writes a profile atomically (main menu: crew changes, loading a save slot).</summary>
        public static void WriteProfileFile(PlayerProfile profile, string path = null)
        {
            path = path ?? SavePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            profile.lastSavedUtc = DateTime.UtcNow.ToString("o");
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(profile, true));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        void OnApplicationQuit()
        {
            FlushCrewTime();
            Save();
        }

        void OnDestroy()
        {
            // Never write into another folder than the one loaded from (tests swap SaveDirectoryOverride back).
            if (Profile == null || ReadOnly || SavePath != initializedPath) return;
            FlushCrewTime();
            Save(); // leaving the flight scene (main menu): keep the play time and crew statistics
        }
    }
}
