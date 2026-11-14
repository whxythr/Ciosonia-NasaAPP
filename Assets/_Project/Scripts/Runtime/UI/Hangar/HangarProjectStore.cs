using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using System.IO;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>A rocket design saved in the hangar under its own name.</summary>
    [Serializable]
    public class HangarProject
    {
        public string name = "";
        public string savedUtc = "";
        public RocketBlueprint blueprint = new RocketBlueprint();
    }

    [Serializable]
    class HangarProjectFile
    {
        public int version = 1;
        public List<HangarProject> projects = new List<HangarProject>();
    }

    /// <summary>
    /// Named hangar projects ("Открыть" / "Сохранить"). Kept in their own file next to the player profile
    /// (<see cref="PlayerSession.SaveDirectory"/>/hangar-projects.json), so the profile format does not change; the design that flies
    /// is still the profile's blueprint. Writes are atomic (temp file + replace); a damaged file is kept as a backup and never
    /// overwritten; nothing is written while the profile is read-only.
    /// </summary>
    public static class HangarProjectStore
    {
        public const int MaxNameLength = 40;

        static HangarProjectFile file;
        static string loadedFrom;
        static string loadProblem = "";

        public static string FilePath => Path.Combine(PlayerSession.SaveDirectory, "hangar-projects.json");

        /// <summary>Saved projects, newest first.</summary>
        public static IReadOnlyList<HangarProject> All
        {
            get
            {
                Ensure();
                return file.projects;
            }
        }

        /// <summary>Why the file could not be read (empty when it is fine or does not exist yet).</summary>
        public static string Problem
        {
            get
            {
                Ensure();
                return loadProblem;
            }
        }

        public static string CleanName(string name)
        {
            string n = (name ?? "").Trim();
            if (n.Length > MaxNameLength) n = n.Substring(0, MaxNameLength).Trim();
            return n.Length > 0 ? n : Loc.T("Ракета");
        }

        static void Ensure()
        {
            string path = FilePath;
            if (file != null && loadedFrom == path) return;
            loadedFrom = path;
            loadProblem = "";
            file = new HangarProjectFile();
            if (!File.Exists(path)) return;
            try
            {
                var f = JsonUtility.FromJson<HangarProjectFile>(File.ReadAllText(path));
                if (f == null) throw new InvalidDataException("empty file");
                if (f.projects == null) f.projects = new List<HangarProject>();
                f.projects.RemoveAll(p => p == null || p.blueprint == null || p.blueprint.stages == null);
                foreach (HangarProject p in f.projects)
                {
                    if (p.blueprint.payloadModules == null) p.blueprint.payloadModules = new List<string>();
                    p.blueprint.EnsureLists();
                }
                file = f;
            }
            catch (Exception e)
            {
                string backup = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json";
                try { File.Move(path, backup); } catch { /* keep the damaged file where it is */ }
                loadProblem = Loc.T("Файл проектов повреждён (") + e.Message + Loc.T(") — сохранён как ") + Path.GetFileName(backup) + ".";
                Debug.LogWarning("[SpaceSim] " + loadProblem);
            }
        }

        static int IndexOf(string name)
        {
            Ensure();
            string n = CleanName(name);
            for (int i = 0; i < file.projects.Count; i++)
                if (string.Equals(CleanName(file.projects[i].name), n, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public static bool Exists(string name) => IndexOf(name) >= 0;

        /// <summary>Saves a copy of <paramref name="design"/> under its name (replacing a project of the same name).</summary>
        public static bool Save(RocketBlueprint design, bool readOnly, out string message)
        {
            if (design == null)
            {
                message = "";
                return false;
            }
            if (readOnly)
            {
                message = Loc.T("Профиль только для чтения — проект не сохранён.");
                return false;
            }
            Ensure();
            string name = CleanName(design.name);
            RocketBlueprint copy = design.Clone();
            copy.name = name;
            int i = IndexOf(name);
            bool replaced = i >= 0;
            if (replaced) file.projects.RemoveAt(i);
            file.projects.Insert(0, new HangarProject { name = name, savedUtc = DateTime.UtcNow.ToString("o"), blueprint = copy });
            if (!Write(out message)) return false;
            message = (replaced ? Loc.T("Проект «") + name + Loc.T("» перезаписан.") : Loc.T("Проект «") + name + Loc.T("» сохранён.")) +
                      Loc.T(" Открыть его можно кнопкой «Открыть».");
            return true;
        }

        /// <summary>A copy of the saved design (null and a message when there is none).</summary>
        public static RocketBlueprint Load(string name, out string message)
        {
            int i = IndexOf(name);
            if (i < 0)
            {
                message = Loc.T("Проект «") + name + Loc.T("» не найден.");
                return null;
            }
            RocketBlueprint b = file.projects[i].blueprint.Clone();
            b.EnsureLists();
            message = Loc.T("Открыт проект «") + file.projects[i].name + Loc.T("». «Вывезти на старт», чтобы поставить его на площадку.");
            return b;
        }

        public static bool Delete(string name, bool readOnly, out string message)
        {
            if (readOnly)
            {
                message = Loc.T("Профиль только для чтения — проект не удалён.");
                return false;
            }
            int i = IndexOf(name);
            if (i < 0)
            {
                message = Loc.T("Проект «") + name + Loc.T("» не найден.");
                return false;
            }
            string n = file.projects[i].name;
            file.projects.RemoveAt(i);
            if (!Write(out message)) return false;
            message = Loc.T("Проект «") + n + Loc.T("» удалён.");
            return true;
        }

        static bool Write(out string message)
        {
            message = "";
            if (!string.IsNullOrEmpty(loadProblem) && File.Exists(FilePath))
            {
                message = loadProblem;
                return false;
            }
            try
            {
                Directory.CreateDirectory(PlayerSession.SaveDirectory);
                string path = FilePath, tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(file, true));
                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
                loadProblem = "";
                return true;
            }
            catch (Exception e)
            {
                message = Loc.T("Не удалось записать файл проектов: ") + e.Message;
                Debug.LogWarning("[SpaceSim] " + message);
                return false;
            }
        }

        /// <summary>Forgets the cached file (tests switch the save folder).</summary>
        public static void Reload()
        {
            file = null;
            loadedFrom = null;
        }
    }
}
