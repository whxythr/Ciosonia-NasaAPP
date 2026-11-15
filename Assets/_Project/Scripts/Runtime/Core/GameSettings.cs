using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SpaceSim.Localization;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>Where <see cref="GameSettings"/> keeps its values (PlayerPrefs in the game, memory in tests).</summary>
    public interface ISettingsStore
    {
        bool TryGet(string key, out string value);
        void Set(string key, string value);
        void Save();
    }

    /// <summary>Player preferences, shared by every scene (main menu and flight).</summary>
    public sealed class PlayerPrefsSettingsStore : ISettingsStore
    {
        public bool TryGet(string key, out string value)
        {
            value = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key, "") : null;
            return value != null;
        }

        public void Set(string key, string value) => PlayerPrefs.SetString(key, value);
        public void Save() => PlayerPrefs.Save();
    }

    /// <summary>In-memory store for tests: nothing touches the player's real preferences.</summary>
    public sealed class MemorySettingsStore : ISettingsStore
    {
        public readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public int Saves { get; private set; }
        public bool TryGet(string key, out string value) => Values.TryGetValue(key, out value);
        public void Set(string key, string value) => Values[key] = value;
        public void Save() => Saves++;
    }

    /// <summary>
    /// Player settings that apply everywhere: screen brightness, mouse look (sensitivity, inverted Y) and key bindings.
    /// Changes take effect at once (<see cref="Changed"/>) and are saved immediately. Purely presentation/input — nothing
    /// here touches the physics.
    /// </summary>
    public static partial class GameSettings
    {
        public const float MinBrightness = 0.25f, MaxBrightness = 4f, DefaultBrightness = 1f;
        public const float MinMouseSensitivity = 0.2f, MaxMouseSensitivity = 3f, DefaultMouseSensitivity = 1f;

        public const string BrightnessKey = "SpaceSim.Settings.Brightness";
        public const string MouseSensitivityKey = "SpaceSim.Settings.MouseSensitivity";
        public const string InvertMouseYKey = "SpaceSim.Settings.InvertMouseY";
        public const string BindingsKey = "SpaceSim.Settings.KeyBindings";
        public const string LanguageKey = "SpaceSim.Settings.Language";
        public const string ResolutionKey = "SpaceSim.Settings.Resolution";

        static ISettingsStore store;
        static bool loaded;
        static float brightness = DefaultBrightness, mouseSensitivity = DefaultMouseSensitivity;
        static bool invertMouseY;
        static Language language = Language.Russian;
        static int resolutionWidth, resolutionHeight;
        static readonly Dictionary<InputKey, KeyCode> bindings = new Dictionary<InputKey, KeyCode>();

        /// <summary>Raised after any setting changed (brightness, mouse, bindings, resets).</summary>
        public static event Action Changed;

        /// <summary>Backing store. Assigning one (tests) reloads every value from it.</summary>
        public static ISettingsStore Store
        {
            get => store ?? (store = new PlayerPrefsSettingsStore());
            set
            {
                store = value;
                loaded = false;
            }
        }

        /// <summary>Screen brightness as a linear factor (1 = unchanged, 2 = +1 EV, 0.5 = −1 EV).</summary>
        public static float Brightness
        {
            get
            {
                EnsureLoaded();
                return brightness;
            }
            set
            {
                EnsureLoaded();
                float v = Mathf.Clamp(value, MinBrightness, MaxBrightness);
                if (Mathf.Abs(v - brightness) < 1e-4f) return;
                brightness = v;
                Store.Set(BrightnessKey, Format(v));
                Commit();
            }
        }

        /// <summary>Brightness as a photographic exposure offset in EV (stops): log2(<see cref="Brightness"/>).</summary>
        public static float ExposureEv => (float)Math.Log(Brightness, 2.0);

        /// <summary>Multiplier on mouse look in flight (right/middle button) and on foot.</summary>
        public static float MouseSensitivity
        {
            get
            {
                EnsureLoaded();
                return mouseSensitivity;
            }
            set
            {
                EnsureLoaded();
                float v = Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity);
                if (Mathf.Abs(v - mouseSensitivity) < 1e-4f) return;
                mouseSensitivity = v;
                Store.Set(MouseSensitivityKey, Format(v));
                Commit();
            }
        }

        public static bool InvertMouseY
        {
            get
            {
                EnsureLoaded();
                return invertMouseY;
            }
            set
            {
                EnsureLoaded();
                if (value == invertMouseY) return;
                invertMouseY = value;
                Store.Set(InvertMouseYKey, value ? "1" : "0");
                Commit();
            }
        }

        /// <summary>Interface language (Russian, English or Kazakh); applied at once.</summary>
        public static Language Language
        {
            get
            {
                EnsureLoaded();
                return language;
            }
            set
            {
                EnsureLoaded();
                if (value == language && Loc.Current == value) return;
                if (value == language)
                {
                    // Settings and the translator disagreed: put the chosen language into effect anyway.
                    LocalizationTables.Apply(language);
                    Commit();
                    return;
                }
                language = value;
                Store.Set(LanguageKey, value.ToString());
                LocalizationTables.Apply(language);
                Commit();
            }
        }

        /// <summary>Saved screen resolution in pixels (0 × 0 = never chosen: the display's own mode).</summary>
        public static int ResolutionWidth
        {
            get
            {
                EnsureLoaded();
                return resolutionWidth;
            }
        }

        public static int ResolutionHeight
        {
            get
            {
                EnsureLoaded();
                return resolutionHeight;
            }
        }

        public static void SetResolution(int width, int height)
        {
            EnsureLoaded();
            if (width == resolutionWidth && height == resolutionHeight) return;
            resolutionWidth = Math.Max(0, width);
            resolutionHeight = Math.Max(0, height);
            Store.Set(ResolutionKey, resolutionWidth + "x" + resolutionHeight);
            Commit();
        }

        /// <summary>The physical key that currently triggers a command.</summary>
        public static KeyCode GetBinding(InputKey key)
        {
            EnsureLoaded();
            return bindings.TryGetValue(key, out KeyCode code) ? code : KeyBindings.Default(key);
        }

        public static bool IsDefaultBinding(InputKey key) => GetBinding(key) == KeyBindings.Default(key);

        /// <summary>
        /// Assigns <paramref name="code"/> to <paramref name="key"/>. If another command already used that key the two swap
        /// keys, so no key ever does two things; that command is returned (null when nothing was swapped or nothing changed).
        /// Esc and keys outside <see cref="KeyBindings.Assignable"/> are refused (<see cref="ArgumentException"/>).
        /// </summary>
        public static InputKey? SetBinding(InputKey key, KeyCode code)
        {
            if (!KeyBindings.IsRebindable(key)) throw new ArgumentException("Command " + key + " cannot be rebound.", nameof(key));
            if (!KeyBindings.IsAssignable(code)) throw new ArgumentException("Key " + code + " cannot be assigned.", nameof(code));
            EnsureLoaded();
            KeyCode previous = GetBinding(key);
            if (previous == code) return null;

            InputKey? swapped = null;
            foreach (KeyAction other in KeyBindings.Actions)
            {
                if (other.Key == key || GetBinding(other.Key) != code) continue;
                bindings[other.Key] = previous;
                swapped = other.Key;
            }
            bindings[key] = code;
            SaveBindings();
            Commit();
            return swapped;
        }

        /// <summary>Command that currently uses <paramref name="code"/> (null if none).</summary>
        public static InputKey? CommandOn(KeyCode code)
        {
            foreach (KeyAction a in KeyBindings.Actions)
                if (GetBinding(a.Key) == code) return a.Key;
            return null;
        }

        public static void ResetBindings()
        {
            EnsureLoaded();
            bindings.Clear();
            SaveBindings();
            Commit();
        }

        public static void ResetDisplay()
        {
            EnsureLoaded();
            brightness = DefaultBrightness;
            Store.Set(BrightnessKey, Format(brightness));
            Commit();
        }

        public static void ResetMouse()
        {
            EnsureLoaded();
            mouseSensitivity = DefaultMouseSensitivity;
            invertMouseY = false;
            Store.Set(MouseSensitivityKey, Format(mouseSensitivity));
            Store.Set(InvertMouseYKey, "0");
            Commit();
        }

        // ─────────────────────────────── display: window mode, frame rate, vertical sync ───────────────────────────────

        public const string DisplayModeKey = "SpaceSim.Settings.DisplayMode";
        public const string FrameRateKey = "SpaceSim.Settings.FrameRateLimit";
        public const string VSyncKey = "SpaceSim.Settings.VSync";

        /// <summary>Window mode choices of the Graphics tab.</summary>
        public enum WindowMode { Windowed, Borderless, Fullscreen }

        /// <summary>Frame-rate caps offered (0 = no cap).</summary>
        public static readonly int[] FrameRateOptions = { 30, 60, 120, 0 };

        static WindowMode windowMode = WindowMode.Borderless;
        static int frameRateLimit;
        static bool vSync = true;

        public static WindowMode DisplayMode
        {
            get
            {
                EnsureLoaded();
                return windowMode;
            }
            set
            {
                EnsureLoaded();
                if (value == windowMode) return;
                windowMode = value;
                Store.Set(DisplayModeKey, value.ToString());
                ApplyDisplay();
                Commit();
            }
        }

        /// <summary>Frame-rate cap in frames per second (0 = none). Ignored while vertical sync is on.</summary>
        public static int FrameRateLimit
        {
            get
            {
                EnsureLoaded();
                return frameRateLimit;
            }
            set
            {
                EnsureLoaded();
                value = Math.Max(0, value);
                if (value == frameRateLimit) return;
                frameRateLimit = value;
                Store.Set(FrameRateKey, value.ToString(CultureInfo.InvariantCulture));
                ApplyDisplay();
                Commit();
            }
        }

        public static bool VSync
        {
            get
            {
                EnsureLoaded();
                return vSync;
            }
            set
            {
                EnsureLoaded();
                if (value == vSync) return;
                vSync = value;
                Store.Set(VSyncKey, value ? "1" : "0");
                ApplyDisplay();
                Commit();
            }
        }

        public static void ResetGraphics()
        {
            EnsureLoaded();
            brightness = DefaultBrightness;
            windowMode = WindowMode.Borderless;
            frameRateLimit = 0;
            vSync = true;
            Store.Set(BrightnessKey, Format(brightness));
            Store.Set(DisplayModeKey, windowMode.ToString());
            Store.Set(FrameRateKey, "0");
            Store.Set(VSyncKey, "1");
            ApplyDisplay();
            Commit();
        }

        /// <summary>Puts window mode, frame cap and vertical sync into effect (never in the editor's game view: it has no window).</summary>
        static void ApplyDisplay()
        {
            if (!Application.isPlaying) return;
            QualitySettings.vSyncCount = vSync ? 1 : 0;
            Application.targetFrameRate = vSync || frameRateLimit <= 0 ? -1 : frameRateLimit;
            if (Application.isEditor) return;
            FullScreenMode mode = windowMode == WindowMode.Windowed ? FullScreenMode.Windowed
                : windowMode == WindowMode.Fullscreen ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.FullScreenWindow;
            if (Screen.fullScreenMode != mode) Screen.fullScreenMode = mode;
        }

        // ─────────────────────────────── snapshot (Settings «Отмена») ───────────────────────────────

        /// <summary>Every setting at one moment: the Settings screen restores it when the player cancels.</summary>
        public sealed class Snapshot
        {
            internal float Brightness, Sensitivity;
            internal bool Invert, VSync;
            internal Language Language;
            internal int Width, Height, FrameRate;
            internal WindowMode Mode;
            internal readonly Dictionary<InputKey, KeyCode> Bindings = new Dictionary<InputKey, KeyCode>();
            internal float MusicVolume = DefaultMusicVolume, MusicCrossfade = DefaultMusicCrossfade;
            internal bool MusicMuted;
        }

        public static Snapshot Capture()
        {
            EnsureLoaded();
            var s = new Snapshot
            {
                Brightness = brightness, Sensitivity = mouseSensitivity, Invert = invertMouseY, VSync = vSync, Language = language,
                Width = resolutionWidth, Height = resolutionHeight, FrameRate = frameRateLimit, Mode = windowMode,
            };
            foreach (KeyValuePair<InputKey, KeyCode> p in bindings) s.Bindings[p.Key] = p.Value;
            CaptureAudio(s);
            return s;
        }

        /// <summary>Puts every setting back as it was in <paramref name="s"/>; true if the language changed (the UI must rebuild).</summary>
        public static bool Restore(Snapshot s)
        {
            if (s == null) return false;
            EnsureLoaded();
            bool languageChanged = s.Language != language;
            brightness = s.Brightness;
            mouseSensitivity = s.Sensitivity;
            invertMouseY = s.Invert;
            vSync = s.VSync;
            frameRateLimit = s.FrameRate;
            windowMode = s.Mode;
            language = s.Language;
            bindings.Clear();
            foreach (KeyValuePair<InputKey, KeyCode> p in s.Bindings) bindings[p.Key] = p.Value;
            RestoreAudio(s);
            Store.Set(BrightnessKey, Format(brightness));
            Store.Set(MouseSensitivityKey, Format(mouseSensitivity));
            Store.Set(InvertMouseYKey, invertMouseY ? "1" : "0");
            Store.Set(VSyncKey, vSync ? "1" : "0");
            Store.Set(FrameRateKey, frameRateLimit.ToString(CultureInfo.InvariantCulture));
            Store.Set(DisplayModeKey, windowMode.ToString());
            Store.Set(LanguageKey, language.ToString());
            SaveBindings();
            if (s.Width != resolutionWidth || s.Height != resolutionHeight)
            {
                if (s.Width > 0) ScreenResolution.Apply(s.Width, s.Height);
                else ScreenResolution.ResetToDisplay();
            }
            if (languageChanged) LocalizationTables.Apply(language);
            ApplyDisplay();
            Commit();
            return languageChanged;
        }

        /// <summary>Reads everything from <see cref="Store"/> again (unknown or broken values fall back to defaults).</summary>
        public static void Reload()
        {
            loaded = false;
            EnsureLoaded();
            Changed?.Invoke();
        }

        static void EnsureLoaded()
        {
            if (loaded) return;
            loaded = true;
            ISettingsStore s = Store;
            brightness = Mathf.Clamp(ReadFloat(s, BrightnessKey, DefaultBrightness), MinBrightness, MaxBrightness);
            mouseSensitivity = Mathf.Clamp(ReadFloat(s, MouseSensitivityKey, DefaultMouseSensitivity), MinMouseSensitivity, MaxMouseSensitivity);
            invertMouseY = s.TryGet(InvertMouseYKey, out string inv) && inv == "1";
            bindings.Clear();
            if (s.TryGet(BindingsKey, out string text)) ParseBindings(text, bindings);
            language = s.TryGet(LanguageKey, out string lang) && Enum.TryParse(lang, out Language parsed) &&
                       Enum.IsDefined(typeof(Language), parsed) ? parsed : Language.Russian;
            resolutionWidth = resolutionHeight = 0;
            if (s.TryGet(ResolutionKey, out string res))
            {
                string[] parts = res.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h) && w > 0 && h > 0)
                {
                    resolutionWidth = w;
                    resolutionHeight = h;
                }
            }
            windowMode = s.TryGet(DisplayModeKey, out string wm) && Enum.TryParse(wm, out WindowMode parsedMode) && Enum.IsDefined(typeof(WindowMode), parsedMode) ? parsedMode : WindowMode.Borderless;
            frameRateLimit = s.TryGet(FrameRateKey, out string fr) && int.TryParse(fr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frv) && frv > 0 ? frv : 0;
            vSync = !s.TryGet(VSyncKey, out string vs) || vs != "0";
            LoadAudio(s);
            LocalizationTables.Apply(language);
        }

        /// <summary>Loads the saved settings and puts the language and resolution into effect (call at scene start).</summary>
        public static void ApplyAtStart()
        {
            EnsureLoaded();
            ScreenResolution.ApplySaved();
            ApplyDisplay();
        }

        /// <summary>"W=UpArrow;Space=J": only non-default keys; malformed, duplicate or forbidden entries are ignored.</summary>
        public static string FormatBindings(IDictionary<InputKey, KeyCode> map)
        {
            var sb = new StringBuilder();
            foreach (KeyAction a in KeyBindings.Actions)
            {
                if (!map.TryGetValue(a.Key, out KeyCode code) || code == KeyBindings.Default(a.Key)) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(a.Key).Append('=').Append(code);
            }
            return sb.ToString();
        }

        public static void ParseBindings(string text, IDictionary<InputKey, KeyCode> into)
        {
            if (string.IsNullOrEmpty(text)) return;
            var used = new HashSet<KeyCode>();
            var parsed = new Dictionary<InputKey, KeyCode>();
            foreach (string pair in text.Split(';'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (!Enum.TryParse(pair.Substring(0, eq).Trim(), out InputKey key) || !KeyBindings.IsRebindable(key)) continue;
                if (!Enum.TryParse(pair.Substring(eq + 1).Trim(), out KeyCode code) || !KeyBindings.IsAssignable(code)) continue;
                parsed[key] = code;
            }
            // A key may serve only one command: drop any entry that would collide with another command's effective key.
            foreach (KeyAction a in KeyBindings.Actions)
            {
                KeyCode effective = parsed.TryGetValue(a.Key, out KeyCode c) ? c : KeyBindings.Default(a.Key);
                if (!used.Add(effective))
                {
                    into.Clear();
                    return; // inconsistent file: fall back to the defaults rather than a half-broken layout
                }
            }
            foreach (KeyValuePair<InputKey, KeyCode> p in parsed) into[p.Key] = p.Value;
        }

        static void SaveBindings() => Store.Set(BindingsKey, FormatBindings(bindings));

        static void Commit()
        {
            Store.Save();
            Changed?.Invoke();
        }

        static float ReadFloat(ISettingsStore s, string key, float fallback) =>
            s.TryGet(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) &&
            !float.IsNaN(v) && !float.IsInfinity(v)
                ? v
                : fallback;

        static string Format(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
