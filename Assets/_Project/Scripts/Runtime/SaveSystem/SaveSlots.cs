using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SpaceSim.Economy;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>What the load screen shows about one save (read from its meta file, the profile itself is loaded only on demand).</summary>
    [Serializable]
    public class SaveSlotMeta
    {
        public string id = "";
        public string name = "";
        public string savedUtc = "";
        public int credits;
        public double research;
        public int samples;
        public int visitedBodies;
        public int crew;
        public int crewInFlight;
        public int flights;
        public double playSeconds;
        /// <summary>Body the flight was at when saved (empty in the main menu).</summary>
        public string bodyName = "";

        /// <summary>The live profile (profile.json), autosaved after every change: not a slot of its own.</summary>
        public bool IsAutosave => id == SaveSlots.AutosaveId;

        public DateTime SavedLocal =>
            DateTime.TryParse(savedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime t) ? t.ToLocalTime() : DateTime.MinValue;
    }

    /// <summary>
    /// Manual saves next to the player profile: Saves/&lt;id&gt;/profile.json + meta.json + thumb.png. The live profile
    /// (<see cref="PlayerSession.SavePath"/>) is the autosave and is listed first. Loading a slot copies its profile over the live one
    /// (atomically); the flight scene then starts from it.
    /// </summary>
    public static class SaveSlots
    {
        public const string AutosaveId = "autosave";
        public const int MaxNameLength = 40;

        public static string Directory => Path.Combine(PlayerSession.SaveDirectory, "Saves");

        static string SlotDir(string id) => Path.Combine(Directory, id);

        public static SaveSlotMeta Describe(PlayerProfile p, string id, string name, string bodyName)
        {
            var m = new SaveSlotMeta
            {
                id = id,
                name = name,
                savedUtc = string.IsNullOrEmpty(p.lastSavedUtc) ? DateTime.UtcNow.ToString("o") : p.lastSavedUtc,
                credits = p.credits,
                research = p.researchPoints,
                visitedBodies = p.visitedBodies != null ? p.visitedBodies.Count : 0,
                playSeconds = p.playSeconds,
                bodyName = bodyName ?? "",
            };
            if (p.samples != null) m.samples = p.samples.Count;
            if (p.crew != null && p.crew.members != null)
            {
                m.crew = p.crew.members.FindAll(c => c.status != SpaceSim.Crew.CrewStatus.Applicant).Count;
                m.crewInFlight = SpaceSim.Crew.CrewRoster.Count(p.crew, SpaceSim.Crew.CrewStatus.OnMission);
                m.flights = p.crew.flightCounter;
            }
            return m;
        }

        /// <summary>The autosave first, then the manual saves, newest first.</summary>
        public static List<SaveSlotMeta> List()
        {
            var list = new List<SaveSlotMeta>();
            PlayerProfile live = PlayerSession.ReadProfileFile();
            if (live != null) list.Add(Describe(live, AutosaveId, SpaceSim.Localization.Loc.T("Автосохранение"), ""));
            var manual = new List<SaveSlotMeta>();
            try
            {
                if (System.IO.Directory.Exists(Directory))
                    foreach (string dir in System.IO.Directory.GetDirectories(Directory))
                    {
                        string metaPath = Path.Combine(dir, "meta.json");
                        if (!File.Exists(metaPath) || !File.Exists(Path.Combine(dir, "profile.json"))) continue;
                        try
                        {
                            var m = JsonUtility.FromJson<SaveSlotMeta>(File.ReadAllText(metaPath));
                            if (m == null) continue;
                            m.id = Path.GetFileName(dir);
                            manual.Add(m);
                        }
                        catch (Exception)
                        {
                            // A broken slot is skipped, never deleted.
                        }
                    }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SpaceSim] Saves folder cannot be read: " + e.Message);
            }
            manual.Sort((a, b) => b.SavedLocal.CompareTo(a.SavedLocal));
            list.AddRange(manual);
            return list;
        }

        /// <summary>Writes a new manual save of <paramref name="profile"/>; <paramref name="thumbnail"/> (optional) becomes thumb.png.</summary>
        public static SaveSlotMeta Save(PlayerProfile profile, string name, string bodyName, Texture2D thumbnail)
        {
            string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            string dir = SlotDir(id);
            System.IO.Directory.CreateDirectory(dir);
            profile.lastSavedUtc = DateTime.UtcNow.ToString("o");
            File.WriteAllText(Path.Combine(dir, "profile.json"), JsonUtility.ToJson(profile, true));
            name = Clean(name);
            SaveSlotMeta meta = Describe(profile, id, name, bodyName);
            File.WriteAllText(Path.Combine(dir, "meta.json"), JsonUtility.ToJson(meta, true));
            if (thumbnail != null)
            {
                try
                {
                    File.WriteAllBytes(Path.Combine(dir, "thumb.png"), thumbnail.EncodeToPNG());
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[SpaceSim] Save thumbnail not written: " + e.Message);
                }
            }
            return meta;
        }

        /// <summary>Copies a manual save over the live profile. False if the slot is missing, broken or from a newer build.</summary>
        public static bool Load(string id, out string problem)
        {
            problem = "";
            if (id == AutosaveId) return true; // already the live profile
            string path = Path.Combine(SlotDir(id), "profile.json");
            PlayerProfile p = PlayerSession.ReadProfileFile(path);
            if (p == null)
            {
                problem = SpaceSim.Localization.Loc.T("Сохранение повреждено или создано более новой версией игры.");
                return false;
            }
            PlayerSession.WriteProfileFile(p);
            return true;
        }

        public static bool Delete(string id)
        {
            if (id == AutosaveId) return false;
            try
            {
                System.IO.Directory.Delete(SlotDir(id), true);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool Rename(string id, string name)
        {
            if (id == AutosaveId) return false;
            string metaPath = Path.Combine(SlotDir(id), "meta.json");
            try
            {
                var m = JsonUtility.FromJson<SaveSlotMeta>(File.ReadAllText(metaPath));
                m.name = Clean(name);
                File.WriteAllText(metaPath, JsonUtility.ToJson(m, true));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>The slot's picture (null if none). The caller owns the texture.</summary>
        public static Texture2D Thumbnail(string id)
        {
            if (id == AutosaveId) return null;
            string path = Path.Combine(SlotDir(id), "thumb.png");
            if (!File.Exists(path)) return null;
            try
            {
                var t = new Texture2D(2, 2, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
                if (t.LoadImage(File.ReadAllBytes(path))) return t;
                UnityEngine.Object.Destroy(t);
            }
            catch (Exception)
            {
                // no picture
            }
            return null;
        }

        static string Clean(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) name = SpaceSim.Localization.Loc.T("Сохранение");
            return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength) : name;
        }

        /// <summary>"5 мин назад", "вчера", "12.11.2026".</summary>
        public static string Ago(DateTime local)
        {
            if (local == DateTime.MinValue) return "—";
            TimeSpan d = DateTime.Now - local;
            if (d.TotalMinutes < 1) return SpaceSim.Localization.Loc.T("только что");
            if (d.TotalHours < 1) return (int)d.TotalMinutes + SpaceSim.Localization.Loc.T(" мин назад");
            if (d.TotalDays < 1 && local.Date == DateTime.Today) return (int)d.TotalHours + SpaceSim.Localization.Loc.T(" ч назад");
            if (local.Date == DateTime.Today.AddDays(-1)) return SpaceSim.Localization.Loc.T("вчера");
            return local.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        /// <summary>"14 ч 32 мин".</summary>
        public static string PlayTime(double seconds)
        {
            int minutes = (int)(seconds / 60.0);
            int h = minutes / 60, m = minutes % 60;
            return h > 0 ? h + SpaceSim.Localization.Loc.T(" ч ") + m + SpaceSim.Localization.Loc.T(" мин") : m + SpaceSim.Localization.Loc.T(" мин");
        }
    }
}
