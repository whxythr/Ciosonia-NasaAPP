using System;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>
    /// Shared building blocks of the HORIZON UI Toolkit screens (main menu, in-game Esc menu): element helpers, the
    /// runtime panel, and the DIN-style fonts. Classes come from Resources/UI/MainMenu.uss (+ GameMenu.uss).
    /// </summary>
    public static class MenuKit
    {
        public const string MenuStyleSheet = "UI/MainMenu", GameStyleSheet = "UI/GameMenu", PanelTemplate = "UI/HorizonPanel";

        static bool fontsLoaded;
        static FontDefinition? regular, strong, mono;

        // ─────────────────────────────── elements ───────────────────────────────

        public static T Add<T>(VisualElement parent, T child) where T : VisualElement
        {
            parent.Add(child);
            return child;
        }

        public static VisualElement El(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
            return e;
        }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(SpaceSim.Localization.Loc.T(text));
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) l.AddToClassList(c);
            return l;
        }

        /// <summary>
        /// Label whose letters fade left → right from <paramref name="from"/> to <paramref name="to"/> (rich-text colour per
        /// character: UI Toolkit has no gradient fill for text). The text is not localized.
        /// </summary>
        public static Label GradientText(string text, Color from, Color to, params string[] classes)
        {
            var sb = new System.Text.StringBuilder();
            int last = Mathf.Max(1, text.Length - 1);
            for (int i = 0; i < text.Length; i++)
            {
                Color c = Color.Lerp(from, to, i / (float)last);
                sb.Append("<color=#")
                  .Append(((int)(Mathf.Clamp01(c.r) * 255f + 0.5f)).ToString("X2"))
                  .Append(((int)(Mathf.Clamp01(c.g) * 255f + 0.5f)).ToString("X2"))
                  .Append(((int)(Mathf.Clamp01(c.b) * 255f + 0.5f)).ToString("X2"))
                  .Append('>').Append(text[i]).Append("</color>");
            }
            var l = new Label(sb.ToString()); // rich text is on by default for Label
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) l.AddToClassList(c);
            return l;
        }

        /// <summary>Menu button (".btn"); primary = amber.</summary>
        public static Button ButtonEl(string label, Action onClick, bool primary = false, bool disabled = false)
        {
            var b = new Button(onClick);
            b.AddToClassList("btn");
            if (primary) b.AddToClassList("btn--primary");
            if (disabled) b.AddToClassList("btn--disabled");
            b.Add(Text(label, "btn__label", "t-strong"));
            return b;
        }

        /// <summary>Key cap plus caption, as in the menu's bottom bar ("ESC  BACK").</summary>
        public static VisualElement KeyHint(VisualElement parent, string key, string label)
        {
            var hint = Add(parent, El("key-hint"));
            hint.Add(Text(key, "key-cap", "t-strong"));
            hint.Add(Text(label, "key-hint__label", "t-strong"));
            return hint;
        }

        /// <summary>The amber diamond + HORIZON word mark of the top bar.</summary>
        public static VisualElement BrandBar(VisualElement parent, string subtitle)
        {
            var top = Add(parent, El("hud-top"));
            var mark = Add(top, El("brand-mark"));
            mark.Add(El("brand-mark__core"));
            top.Add(Text("HORIZON", "brand-name", "t-strong"));
            top.Add(Text(subtitle, "brand-sub"));
            top.Add(El("grow"));
            return top;
        }

        /// <summary>Loads a style sheet from Resources and attaches it (warns once if missing).</summary>
        public static void AddStyleSheet(VisualElement element, string resource)
        {
            var sheet = Resources.Load<StyleSheet>(resource);
            if (sheet != null) element.styleSheets.Add(sheet);
            else Debug.LogWarning("[HORIZON UI] Resources/" + resource + ".uss not found — the screen will be unstyled.");
        }

        // ─────────────────────────────── panel ───────────────────────────────

        /// <summary>
        /// Runtime panel at the menu's 1920×1080 reference resolution. <paramref name="opaque"/> clears the screen (main menu);
        /// an overlay over the 3D view must not.
        /// </summary>
        public static PanelSettings CreatePanel(string name, bool opaque, int sortingOrder)
        {
            // A copy of Resources/UI/HorizonPanel (it carries the empty HORIZON theme): a panel created from scratch has no
            // theme and Unity warns about it on creation. Created by SpaceSim ▸ Create Main Menu Scene / Setup.
            var template = Resources.Load<PanelSettings>(PanelTemplate);
            var panel = template != null ? UnityEngine.Object.Instantiate(template) : ScriptableObject.CreateInstance<PanelSettings>();
            panel.name = name;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.clearColor = opaque;
            panel.colorClearValue = new Color(0.016f, 0.024f, 0.047f, 1f);
            panel.sortingOrder = sortingOrder;
            return panel;
        }

        /// <summary>What the default runtime theme would do (the runtime-created panel has none): fill the screen.</summary>
        public static void StretchRoot(VisualElement documentRoot)
        {
            documentRoot.style.position = Position.Absolute;
            documentRoot.style.left = documentRoot.style.top = documentRoot.style.right = documentRoot.style.bottom = 0;
        }

        // ─────────────────────────────── fonts ───────────────────────────────

        /// <summary>
        /// Classes that get a face by their role (so screens need not tag every label): condensed Oswald for titles, menu items and
        /// big numbers; IBM Plex Mono for the small caps labels, buttons, key caps and values; Inter for everything else.
        /// Explicit ".t-display" / ".t-mono" / ".t-strong" win over these.
        /// </summary>
        static readonly string[] DisplayClasses =
        {
            "title", "screen-title", "menu-item__label", "big-number", "detail__title", "hz-title", "hz-display", "hz-big",
            "crew-title", "load-title", "pause-title", "stat-big",
        };

        static readonly string[] MonoClasses =
        {
            "eyebrow", "key-cap", "key-hint__label", "chip__label", "chip__value", "btn__label", "tab", "build-info", "brand-name", "brand-sub",
            "section-label", "flow__label", "th", "segmented__item", "stepper__value", "slider__value", "bind-key", "bind-group", "hz-mono",
            "hz-label", "hz-caps", "res-chip__label", "res-chip__value", "menu-item__index", "menu-item__meta", "timer", "room-code",
        };

        static FontDefinition? display;

        /// <summary>
        /// The bundled OFL fonts (Resources/UI/Fonts) — Inter, IBM Plex Mono, Oswald — with Windows fonts as the fallback.
        /// Sets the root font now and the role fonts on the next frame (after the screens are built).
        /// </summary>
        public static void ApplyRootFonts(VisualElement root)
        {
            EnsureFonts();
            root.style.unityFontDefinition = regular ?? FontDefinition.FromFont(Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
            root.schedule.Execute(() => ApplyFonts(root.parent ?? root));
        }

        /// <summary>Applies the role fonts to every element under <paramref name="scope"/>.</summary>
        public static void ApplyFonts(VisualElement scope)
        {
            if (scope == null) return;
            EnsureFonts();
            if (strong.HasValue) scope.Query(className: "t-strong").ForEach(e => e.style.unityFontDefinition = strong.Value);
            else scope.Query(className: "t-strong").ForEach(e => e.style.unityFontStyleAndWeight = FontStyle.Bold);
            if (mono.HasValue)
            {
                foreach (string c in MonoClasses) scope.Query(className: c).ForEach(e => e.style.unityFontDefinition = mono.Value);
                scope.Query(className: "t-mono").ForEach(e => e.style.unityFontDefinition = mono.Value);
            }
            if (display.HasValue)
            {
                foreach (string c in DisplayClasses) scope.Query(className: c).ForEach(e => e.style.unityFontDefinition = display.Value);
                scope.Query(className: "t-display").ForEach(e => e.style.unityFontDefinition = display.Value);
            }
        }

        static void EnsureFonts()
        {
            // Runtime font assets survive scene loads (DontUnloadUnusedAsset); rebuild only if something destroyed them.
            if (fontsLoaded && (!regular.HasValue || regular.Value.fontAsset != null)) return;
            fontsLoaded = true;
            regular = Bundled("Inter-Regular") ?? LoadFont(("Inter", "Regular"), ("Segoe UI", "Regular"));
            strong = Bundled("Inter-SemiBold") ?? LoadFont(("Segoe UI", "Semibold"), ("Segoe UI", "Bold"));
            mono = Bundled("IBMPlexMono-Regular") ?? LoadFont(("IBM Plex Mono", "Regular"), ("Consolas", "Regular"));
            display = Bundled("Oswald-SemiBold") ?? LoadFont(("Oswald", "SemiBold"), ("Bahnschrift", "Regular"));
            // Glyphs a face lacks (arrows, math signs) come from Segoe UI.
            FontAsset fallback = null;
            try { fallback = FontAsset.CreateFontAsset("Segoe UI", "Regular"); } catch (Exception) { }
            if (fallback != null)
            {
                fallback.hideFlags = HideFlags.DontUnloadUnusedAsset;
                foreach (FontDefinition? d in new[] { regular, strong, mono, display })
                    if (d.HasValue && d.Value.fontAsset != null)
                    {
                        if (d.Value.fontAsset.fallbackFontAssetTable == null) d.Value.fontAsset.fallbackFontAssetTable = new System.Collections.Generic.List<FontAsset>();
                        if (!d.Value.fontAsset.fallbackFontAssetTable.Contains(fallback)) d.Value.fontAsset.fallbackFontAssetTable.Add(fallback);
                    }
            }
        }

        /// <summary>A runtime SDF font asset made from a bundled font file, or null.</summary>
        static FontDefinition? Bundled(string file)
        {
            try
            {
                var font = Resources.Load<Font>("UI/Fonts/" + file);
                if (font == null) return null;
                FontAsset asset = FontAsset.CreateFontAsset(font);
                if (asset == null) return null;
                asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return FontDefinition.FromSDFFont(asset);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static FontDefinition? LoadFont(params (string family, string style)[] candidates)
        {
            foreach (var (family, style) in candidates)
            {
                try
                {
                    var asset = FontAsset.CreateFontAsset(family, style);
                    if (asset == null) continue;
                    asset.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    return FontDefinition.FromSDFFont(asset);
                }
                catch (Exception)
                {
                    // Font not installed on this machine — try the next one.
                }
            }
            return null;
        }
    }
}
