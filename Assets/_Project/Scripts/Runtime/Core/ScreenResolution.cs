using System.Collections.Generic;
using SpaceSim.Localization;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>Loads the translation tables from Resources/Localization once and selects the current language.</summary>
    public static class LocalizationTables
    {
        static bool loaded;

        public static void Apply(Language language)
        {
            if (!loaded)
            {
                loaded = true;
                Load(Language.Russian, "ru");
                Load(Language.English, "en");
                Load(Language.Kazakh, "kk");
            }
            Loc.SetLanguage(language);
        }

        static void Load(Language language, string file)
        {
            var asset = Resources.Load<TextAsset>("Localization/" + file);
            if (asset != null) Loc.SetTable(language, Loc.Parse(asset.text));
            else Debug.LogWarning("[SpaceSim] Translation table missing: Resources/Localization/" + file + ".txt");
        }
    }

    /// <summary>The display modes offered in Settings, and applying the player's choice (keeps the current window mode).</summary>
    public static class ScreenResolution
    {
        static bool appliedSaved;

        /// <summary>The resolution in effect: the saved choice, otherwise the current window size.</summary>
        public static Vector2Int Current =>
            GameSettings.ResolutionWidth > 0
                ? new Vector2Int(GameSettings.ResolutionWidth, GameSettings.ResolutionHeight)
                : new Vector2Int(Screen.width, Screen.height);

        /// <summary>Distinct sizes the display supports, smallest first; always contains the current one.</summary>
        public static List<Vector2Int> Options()
        {
            var seen = new HashSet<long>();
            var list = new List<Vector2Int>();
            void AddMode(int w, int h)
            {
                if (w >= 800 && h >= 600 && seen.Add(w * 100000L + h)) list.Add(new Vector2Int(w, h));
            }
            foreach (Resolution r in Screen.resolutions) AddMode(r.width, r.height);
            Vector2Int current = Current;
            AddMode(current.x, current.y);
            list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            return list;
        }

        public static void Apply(int width, int height)
        {
            Screen.SetResolution(width, height, Screen.fullScreenMode);
            GameSettings.SetResolution(width, height);
        }

        /// <summary>Back to the display's own resolution; the choice is forgotten.</summary>
        public static void ResetToDisplay()
        {
            Resolution display = Screen.currentResolution;
            Screen.SetResolution(display.width, display.height, Screen.fullScreenMode);
            GameSettings.SetResolution(0, 0);
        }

        /// <summary>Applies the saved choice once per run (scene loads keep it afterwards).</summary>
        public static void ApplySaved()
        {
            if (appliedSaved) return;
            appliedSaved = true;
            if (GameSettings.ResolutionWidth > 0)
                Screen.SetResolution(GameSettings.ResolutionWidth, GameSettings.ResolutionHeight, Screen.fullScreenMode);
        }
    }
}
