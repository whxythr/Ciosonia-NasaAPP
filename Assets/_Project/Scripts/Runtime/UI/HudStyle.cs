using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Shared look of every in-game IMGUI panel and window, matched to the HORIZON main menu (Resources/UI/MainMenu.uss):
    /// deep-navy glass panels with a hairline blue border, DIN-style type (Bahnschrift) with monospaced numbers, cyan for
    /// interactive elements, amber for the primary action and the active tab. Must be used from OnGUI.
    /// </summary>
    public static class HudStyle
    {
        // ── HORIZON palette, 2026-11 redesign (MainMenu.uss): near-black glass, cream type, one blue accent ──
        public static readonly Color Space = Rgb(10, 10, 11);
        public static readonly Color TextStrong = Rgb(240, 234, 220);
        public static readonly Color Text = Rgb(226, 221, 209);
        public static readonly Color TextSoft = Rgb(204, 199, 188);
        public static readonly Color TextPanel = Rgb(170, 166, 157);
        public static readonly Color TextMuted = Rgb(134, 131, 124);
        public static readonly Color TextDim = Rgb(96, 94, 89);
        /// <summary>The one accent colour (interactive, selected, primary action). Kept under its old name.</summary>
        public static readonly Color Cyan = Rgb(98, 140, 255);
        public static readonly Color Blue = Cyan;
        /// <summary>Caution (amber/orange).</summary>
        public static readonly Color Amber = Rgb(240, 168, 64);
        public static readonly Color Violet = Rgb(169, 139, 255);
        public static readonly Color Green = Rgb(170, 226, 110);
        public static readonly Color Red = Rgb(255, 104, 92);
        /// <summary>Hairline between rows and around panels.</summary>
        public static readonly Color Line = Rgb(255, 255, 255, 0.10f);

        /// <summary>Information / interactive (cyan).</summary>
        public static readonly Color Accent = Cyan;
        public static readonly Color Warning = Amber;
        public static readonly Color Danger = Red;
        public static readonly Color Success = Green;

        static bool initialized;
        static Texture2D panelTexture, barTexture, barBackTexture, dimTexture, footerTexture;
        static GUISkin skin;
        static Font regular, strong, mono, display, displayStrong;
        static readonly System.Collections.Generic.Dictionary<long, GUIStyle> sized = new System.Collections.Generic.Dictionary<long, GUIStyle>();

        public enum Face { Body, Strong, Mono, Display, DisplayStrong }

        /// <summary>
        /// A text style of the redesign by face, size and colour (cached): Display = Oswald (big numbers and titles), Mono = IBM Plex
        /// Mono (labels and values), Body / Strong = Inter. Size hierarchy is the point: the more important the value, the larger.
        /// </summary>
        public static GUIStyle Style(Face face, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft)
        {
            Ensure();
            long key = ((long)face << 56) ^ ((long)size << 40) ^ ((long)align << 32) ^ (uint)((Color32)color).GetHashCode();
            if (sized.TryGetValue(key, out GUIStyle s) && s != null) return s;
            Font f = face == Face.Display ? display : face == Face.DisplayStrong ? displayStrong ?? display : face == Face.Mono ? mono :
                face == Face.Strong ? strong : regular;
            s = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = align, wordWrap = false, richText = true, clipping = TextClipping.Overflow };
            s.padding = new RectOffset(0, 0, 0, 0);
            s.margin = new RectOffset(0, 0, 0, 0);
            if (f != null) s.font = f;
            else if (face != Face.Body && face != Face.Mono) s.fontStyle = FontStyle.Bold;
            s.normal.textColor = color;
            sized[key] = s;
            return s;
        }

        /// <summary>Filled rectangle (repaint only).</summary>
        public static void Fill(Rect r, Color c)
        {
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// <summary>1-unit outline.</summary>
        public static void Frame(Rect r, Color c, float w = 1f)
        {
            Fill(new Rect(r.x, r.y, r.width, w), c);
            Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
            Fill(new Rect(r.x, r.y, w, r.height), c);
            Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
        }

        /// <summary>The redesign's panel: near-black glass with a hairline border.</summary>
        public static void Card(Rect r, float alpha = 0.86f)
        {
            Fill(r, new Color(Space.r, Space.g, Space.b, alpha));
            Frame(r, Line);
        }

        /// <summary>Horizontal hairline.</summary>
        public static void HLine(float x, float y, float w, Color? c = null) => Fill(new Rect(x, y, w, 1f), c ?? Line);

        public static GUIStyle Panel, Title, Label, Value, Small, Mono, Phase, Hint, Button, Field, Body, Header, BigButton;

        /// <summary>Large modal window background (more opaque than <see cref="Panel"/>).</summary>
        public static GUIStyle Window;
        /// <summary>Window heading: white DIN type with the amber selection bar of the main menu on its left.</summary>
        public static GUIStyle WindowTitle;
        /// <summary>Window tab: muted text, amber underline when selected (menu ".tab--on").</summary>
        public static GUIStyle Tab;
        /// <summary>The one primary action on screen (menu ".btn--primary", amber).</summary>
        public static GUIStyle Primary;
        /// <summary>Keyboard key cap ("ESC", "B").</summary>
        public static GUIStyle KeyCap;
        /// <summary>Small amber caps line above a heading (menu ".eyebrow").</summary>
        public static GUIStyle Eyebrow;
        /// <summary>Like <see cref="Hint"/> with rich text, for key hints built with <see cref="Key"/>.</summary>
        public static GUIStyle HintRich;
        public static GUIStyle HintRichLeft;
        /// <summary>Left-aligned, wrapping list entry (a <see cref="Button"/> used as a selectable row).</summary>
        public static GUIStyle ListButton;

        /// <summary>Fonts actually in use (for diagnostics): regular / strong / mono.</summary>
        public static string FontInfo => (regular != null ? regular.name : "builtin") + " / " + (strong != null ? strong.name : "builtin") + " / " +
                                         (mono != null ? mono.name : "builtin");

        /// <summary>Filled square marker centred at <paramref name="center"/>.</summary>
        public static void Dot(Vector2 center, float size, Color color)
        {
            var c = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - size * 0.5f, center.y - size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.color = c;
        }

        /// <summary>UI scale for the current resolution (1080p = 1).</summary>
        public static float Scale => Mathf.Clamp(Screen.height / 1080f, 0.75f, 2.5f);

        public static void Begin()
        {
            Ensure();
            GUI.skin = skin;
            float s = Scale;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        }

        /// <summary>Screen size in scaled GUI units.</summary>
        public static Vector2 VirtualScreen => new Vector2(Screen.width / Scale, Screen.height / Scale);

        /// <summary>
        /// Background of a large window: the whole screen dims (like the main menu's modal) and the window gets the glass
        /// panel with its hairline border. Call before the window's GUILayout area.
        /// </summary>
        public static void WindowFrame(Rect r, bool dimBehind = true)
        {
            Ensure();
            if (dimBehind && Event.current != null && Event.current.type == EventType.Repaint)
            {
                Vector2 screen = VirtualScreen;
                GUI.DrawTexture(new Rect(0f, 0f, screen.x, screen.y), dimTexture);
            }
            GUI.Box(r, GUIContent.none, Window);
        }

        /// <summary>
        /// A dialog drawn over another window (the hangar's projects / exit questions): the same glass frame on a nearly opaque
        /// backing, so the panels and the 3D view behind it do not show through the text.
        /// </summary>
        public static void DialogFrame(Rect r)
        {
            Ensure();
            if (Event.current != null && Event.current.type == EventType.Repaint)
            {
                Color c = GUI.color;
                GUI.color = new Color(0.025f, 0.035f, 0.06f, 0.97f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = c;
            }
            GUI.Box(r, GUIContent.none, Window);
        }

        /// <summary>
        /// Dark band behind the key hint lines at the bottom of the screen (the main menu's bottom bar: a hairline on top,
        /// fading in from the 3D view), so the hints stay readable over a bright sky or desert.
        /// </summary>
        public static void FooterBand(Rect r)
        {
            Ensure();
            if (Event.current == null || Event.current.type != EventType.Repaint) return;
            GUI.DrawTexture(r, footerTexture);
            var c = GUI.color;
            GUI.color = new Color(0.59f, 0.75f, 1f, 0.12f);
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), Texture2D.whiteTexture);
            GUI.color = c;
        }

        /// <summary>Rich-text key name for hint lines drawn with <see cref="HintRich"/>: bright, bold, in brackets.</summary>
        public static string Key(InputKey key) => KeyText(FlightInput.KeyLabel(key));

        public static string KeyText(string key) => "<color=#F0EADC>" + key + "</color>";

        /// <summary>A key cap with a caption after it ("[M]  КАРТА"); returns the width used.</summary>
        public static float KeyHint(float x, float y, string key, string caption)
        {
            Ensure();
            GUIStyle capStyle = Style(Face.Mono, 11, Text, TextAnchor.MiddleCenter);
            float kw = Mathf.Max(22f, capStyle.CalcSize(new GUIContent(key)).x + 12f);
            var cap = new Rect(x, y, kw, 20f);
            Frame(cap, new Color(1f, 1f, 1f, 0.32f));
            GUI.Label(cap, key, capStyle);
            GUIStyle cs = Style(Face.Mono, 11, TextMuted);
            float cw = cs.CalcSize(new GUIContent(caption)).x;
            GUI.Label(new Rect(cap.xMax + 8f, y, cw + 2f, 20f), caption, cs);
            return kw + 8f + cw + 22f;
        }

        static Color Rgb(int r, int g, int b, float a = 1f) => new Color(r / 255f, g / 255f, b / 255f, a);

        static void Ensure()
        {
            if (initialized && panelTexture != null && skin != null) return;
            initialized = true;

            // ── textures (2026-11 redesign: square near-black glass, hairline borders) ──
            panelTexture = Rounded(12, 1.5f, Rgb(12, 12, 13, 0.84f), Rgb(255, 255, 255, 0.10f));
            Texture2D windowTexture = Rounded(12, 1.5f, Rgb(11, 11, 12, 0.96f), Rgb(255, 255, 255, 0.13f));
            barTexture = Solid(Color.white);
            barBackTexture = Solid(new Color(1f, 1f, 1f, 0.10f));
            dimTexture = Solid(Rgb(4, 4, 5, 0.62f));
            footerTexture = VerticalFade(32, Rgb(10, 10, 11, 0.86f), Rgb(10, 10, 11, 0.30f));

            // ── fonts: bundled OFL fonts (Resources/UI/Fonts): Inter for text, IBM Plex Mono for labels and numbers, Oswald for
            //    titles and the big numbers. OS fonts are the fallback if an import is missing. ──
            regular = Bundled("Inter-Regular") ?? OsFont("Inter", "Segoe UI", "Segoe UI Symbol", "Cambria Math", "Arial", "DejaVu Sans");
            strong = Bundled("Inter-SemiBold") ?? OsFont("Inter SemiBold", "Segoe UI Semibold", "Segoe UI", "Arial", "DejaVu Sans");
            mono = Bundled("IBMPlexMono-Regular") ?? OsFont("IBM Plex Mono", "Consolas", "Cascadia Mono", "DejaVu Sans Mono", "Courier New");
            display = Bundled("Oswald-Medium") ?? OsFont("Oswald", "Bahnschrift", "Segoe UI Semibold", "Arial");
            displayStrong = Bundled("Oswald-SemiBold") ?? display;
            const bool strongIsBold = true; // the strong faces are real semibold fonts

            // ── the skin: a copy of the default one, restyled, so unstyled controls (scroll bars, toggles, sliders,
            //    GUILayout.Window) match too. Assigned in Begin(); IMGUI resets GUI.skin for every OnGUI call. ──
            skin = Object.Instantiate(GUI.skin);
            skin.name = "HORIZON HUD";
            if (regular != null) skin.font = regular;

            Panel = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 9, 9), alignment = TextAnchor.UpperLeft, border = new RectOffset(3, 3, 3, 3) };
            Panel.overflow = new RectOffset(0, 0, 0, 0);
            AllStates(Panel, panelTexture, TextPanel);

            Window = new GUIStyle(Panel) { padding = new RectOffset(14, 14, 12, 12) };
            AllStates(Window, windowTexture, TextPanel);

            // Section label: small mono caps (".eyebrow" of the menu), cream.
            Title = TextStyle(GUI.skin.label, mono ?? strong, 12, TextSoft, !strongIsBold);

            Label = TextStyle(GUI.skin.label, regular, 13, TextPanel, false);

            Value = TextStyle(GUI.skin.label, mono ?? regular, 13, Text, false);
            Value.alignment = TextAnchor.MiddleRight;

            Mono = TextStyle(GUI.skin.label, mono ?? regular, 13, TextPanel, false);

            Small = TextStyle(GUI.skin.label, regular, 11, TextMuted, false);
            Small.wordWrap = true;

            Phase = TextStyle(GUI.skin.label, displayStrong ?? strong, 22, TextStrong, !strongIsBold);
            Phase.alignment = TextAnchor.MiddleCenter;

            Hint = new GUIStyle(Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            if (mono != null) Hint.font = mono;
            HintRich = new GUIStyle(Hint) { richText = true };
            HintRichLeft = new GUIStyle(HintRich) { alignment = TextAnchor.MiddleLeft };

            Eyebrow = TextStyle(GUI.skin.label, mono ?? strong, 11, Cyan, !strongIsBold);
            Eyebrow.wordWrap = false;

            // Window heading: big condensed caps (Oswald), like "SETTINGS" / "CREW" in the menu.
            WindowTitle = TextStyle(GUI.skin.label, displayStrong ?? strong, 26, TextStrong, !strongIsBold);
            WindowTitle.padding = new RectOffset(0, 4, 0, 2);
            WindowTitle.wordWrap = false; // one line; a long title is clipped, never wrapped under the tabs

            Body = TextStyle(GUI.skin.label, regular, 14, TextSoft, false);
            Body.wordWrap = true;
            Body.richText = true;

            Header = TextStyle(GUI.skin.label, strong, 18, TextStrong, !strongIsBold);
            Header.wordWrap = true;
            Header.richText = true;

            // Buttons: transparent with a light hairline; cream hairline on hover; selected (toggle "on") = blue-tinted row.
            Color hair = Rgb(255, 255, 255, 0.22f), hairHot = Rgb(240, 234, 220, 0.85f);
            Color selFill = Rgb(98, 140, 255, 0.20f), selLine = Rgb(98, 140, 255, 0.85f);
            Button = ButtonStyle(GUI.skin.button, regular, 12, false,
                Rounded(10, 1.5f, Rgb(16, 16, 18, 0.88f), hair),
                Rounded(10, 1.5f, Rgb(28, 28, 31, 0.92f), hairHot),
                Rounded(10, 1.5f, Rgb(40, 40, 44, 0.96f), TextStrong),
                Rounded(10, 1.5f, selFill, selLine),
                Rounded(10, 1.5f, Rgb(98, 140, 255, 0.28f), Cyan));
            Button.padding = new RectOffset(10, 10, 5, 5);
            ListButton = new GUIStyle(Button) { alignment = TextAnchor.MiddleLeft, wordWrap = true };

            // Secondary action: mono caps in an outline box ("CANCEL", "DELETE").
            BigButton = ButtonStyle(GUI.skin.button, mono ?? strong, 13, false,
                Rounded(10, 1.5f, Rgb(12, 12, 13, 0.80f), Rgb(255, 255, 255, 0.30f)),
                Rounded(10, 1.5f, Rgb(30, 30, 33, 0.90f), hairHot),
                Rounded(10, 1.5f, Rgb(44, 44, 48, 0.96f), TextStrong),
                Rounded(10, 1.5f, selFill, selLine),
                Rounded(10, 1.5f, Rgb(98, 140, 255, 0.28f), Cyan));

            // The one primary action: solid blue with dark mono caps ("LAUNCH →", "APPLY →").
            Primary = ButtonStyle(GUI.skin.button, mono ?? strong, 16, false,
                Rounded(10, 1.5f, Cyan, Cyan),
                Rounded(10, 1.5f, Rgb(128, 163, 255), Rgb(160, 188, 255)),
                Rounded(10, 1.5f, Rgb(74, 116, 228), Rgb(160, 188, 255)),
                Rounded(10, 1.5f, Rgb(128, 163, 255), Rgb(160, 188, 255)),
                Rounded(10, 1.5f, Rgb(128, 163, 255), Rgb(160, 188, 255)));
            Color ink = Rgb(8, 10, 18);
            Primary.normal.textColor = Primary.hover.textColor = Primary.active.textColor = Primary.focused.textColor = ink;
            Primary.onNormal.textColor = Primary.onHover.textColor = Primary.onActive.textColor = Primary.onFocused.textColor = ink;

            Tab = ButtonStyle(GUI.skin.button, mono ?? strong, 12, false,
                Underline(Rgb(255, 255, 255, 0.10f), 1, Color.clear),
                Underline(Rgb(255, 255, 255, 0.30f), 1, Rgb(255, 255, 255, 0.03f)),
                Underline(Cyan, 2, Rgb(98, 140, 255, 0.12f)),
                Underline(Cyan, 2, Rgb(98, 140, 255, 0.08f)),
                Underline(Cyan, 2, Rgb(98, 140, 255, 0.12f)));
            Tab.border = new RectOffset(2, 2, 2, 3);
            Tab.padding = new RectOffset(12, 12, 4, 6);
            Tab.normal.textColor = TextMuted;
            Tab.hover.textColor = Text;

            KeyCap = TextStyle(GUI.skin.label, mono ?? strong, 11, Text, false);
            KeyCap.alignment = TextAnchor.MiddleCenter;
            KeyCap.wordWrap = false;
            KeyCap.padding = new RectOffset(7, 7, 2, 2);
            KeyCap.border = new RectOffset(3, 3, 3, 3);
            KeyCap.normal.background = Rounded(10, 1.5f, Rgb(255, 255, 255, 0.03f), Rgb(255, 255, 255, 0.34f));

            Field = new GUIStyle(GUI.skin.textField) { fontSize = 13, padding = new RectOffset(8, 8, 4, 4), border = new RectOffset(3, 3, 3, 3) };
            Field.overflow = new RectOffset(0, 0, 0, 0);
            if (mono != null) Field.font = mono;
            Texture2D fieldNormal = Rounded(10, 1.5f, Rgb(14, 14, 16, 0.96f), Rgb(255, 255, 255, 0.20f));
            Texture2D fieldHover = Rounded(10, 1.5f, Rgb(14, 14, 16, 0.96f), Rgb(255, 255, 255, 0.40f));
            Texture2D fieldFocus = Rounded(10, 1.5f, Rgb(16, 18, 26, 0.98f), Cyan);
            SetState(Field.normal, fieldNormal, Text);
            SetState(Field.hover, fieldHover, Text);
            SetState(Field.active, fieldFocus, Text);
            SetState(Field.focused, fieldFocus, TextStrong);
            SetState(Field.onNormal, fieldNormal, Text);
            SetState(Field.onHover, fieldHover, Text);
            SetState(Field.onActive, fieldFocus, Text);
            SetState(Field.onFocused, fieldFocus, TextStrong);

            RestyleSkin(windowTexture);
        }

        /// <summary>A font imported from Resources/UI/Fonts (the bundled OFL fonts), or null.</summary>
        static Font Bundled(string file)
        {
            try
            {
                return Resources.Load<Font>("UI/Fonts/" + file);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Default-skin controls that the panels use without an explicit style.</summary>
        static void RestyleSkin(Texture2D windowTexture)
        {
            skin.box = Panel;
            skin.label = Label;
            skin.button = Button;
            skin.textField = Field;
            skin.settings.cursorColor = Cyan;
            skin.settings.selectionColor = new Color(Cyan.r, Cyan.g, Cyan.b, 0.35f);

            // GUILayout.Window: title in amber caps at the top left, content below it.
            GUIStyle w = skin.window;
            w.border = new RectOffset(5, 5, 5, 5);
            w.padding = new RectOffset(12, 12, 34, 12);
            w.overflow = new RectOffset(0, 0, 0, 0);
            w.contentOffset = new Vector2(0f, -26f);
            w.alignment = TextAnchor.UpperLeft;
            w.fontSize = 12;
            if (strong != null) w.font = strong;
            AllStates(w, windowTexture, Amber);

            // Toggles: the menu's switch (pill + knob), label to the right.
            GUIStyle t = skin.toggle;
            t.border = new RectOffset(32, 2, 0, 0);
            t.padding = new RectOffset(40, 4, 1, 1);
            t.margin = new RectOffset(4, 4, 3, 3);
            t.overflow = new RectOffset(0, 0, 0, 0);
            t.fixedHeight = 18f;
            t.alignment = TextAnchor.MiddleLeft;
            t.fontSize = 13;
            if (regular != null) t.font = regular;
            Texture2D off = Switch(false, false), offHover = Switch(false, true), on = Switch(true, false), onHover = Switch(true, true);
            SetState(t.normal, off, TextSoft);
            SetState(t.hover, offHover, Text);
            SetState(t.active, offHover, Text);
            SetState(t.focused, off, TextSoft);
            SetState(t.onNormal, on, TextStrong);
            SetState(t.onHover, onHover, TextStrong);
            SetState(t.onActive, onHover, TextStrong);
            SetState(t.onFocused, on, TextStrong);

            // Sliders: thin track, white round knob (cyan on hover).
            GUIStyle s = skin.horizontalSlider;
            s.border = new RectOffset(4, 4, 0, 0);
            s.padding = new RectOffset(0, 0, 0, 0);
            s.margin = new RectOffset(4, 4, 4, 4);
            s.overflow = new RectOffset(0, 0, 0, 0);
            s.fixedHeight = 14f;
            AllStates(s, Track(), Color.white);
            GUIStyle k = skin.horizontalSliderThumb;
            k.border = new RectOffset(0, 0, 0, 0);
            k.padding = new RectOffset(0, 0, 0, 0);
            k.margin = new RectOffset(0, 0, 0, 0);
            k.overflow = new RectOffset(0, 0, 0, 0);
            k.fixedWidth = 14f;
            k.fixedHeight = 14f;
            Texture2D knob = Circle(14, Color.white), knobHot = Circle(14, Cyan);
            AllStates(k, knob, Color.white);
            SetState(k.hover, knobHot, Color.white);
            SetState(k.active, knobHot, Color.white);
            SetState(k.onHover, knobHot, Color.white);
            SetState(k.onActive, knobHot, Color.white);

            // Scroll bars: 8 px, no arrow buttons, faint track, blue thumb.
            Texture2D track = Rounded(8, 4f, Rgb(255, 255, 255, 0.05f), Color.clear, 0f);
            Texture2D thumb = Rounded(8, 4f, Rgb(150, 190, 255, 0.35f), Color.clear, 0f);
            Texture2D thumbHot = Rounded(8, 4f, Rgb(127, 212, 255, 0.75f), Color.clear, 0f);
            ScrollBar(skin.verticalScrollbar, skin.verticalScrollbarThumb, track, thumb, thumbHot, vertical: true);
            // The panels size their content to the view width; a few pixels of overflow must not grow a horizontal bar.
            foreach (GUIStyle h in new[] { skin.horizontalScrollbar, skin.horizontalScrollbarThumb })
            {
                h.fixedHeight = 0f;
                h.margin = new RectOffset(0, 0, 0, 0);
                h.padding = new RectOffset(0, 0, 0, 0);
                h.border = new RectOffset(0, 0, 0, 0);
                h.overflow = new RectOffset(0, 0, 0, 0);
                AllStates(h, null, Color.clear);
            }
            foreach (GUIStyle b in new[] { skin.verticalScrollbarUpButton, skin.verticalScrollbarDownButton,
                         skin.horizontalScrollbarLeftButton, skin.horizontalScrollbarRightButton })
            {
                b.fixedWidth = 0f;
                b.fixedHeight = 0f;
                b.margin = new RectOffset(0, 0, 0, 0);
                b.padding = new RectOffset(0, 0, 0, 0);
                b.border = new RectOffset(0, 0, 0, 0);
                AllStates(b, null, Color.clear);
            }
            skin.scrollView.padding = new RectOffset(0, 0, 0, 0);
            AllStates(skin.scrollView, null, TextPanel);
        }

        static void ScrollBar(GUIStyle bar, GUIStyle thumb, Texture2D track, Texture2D normal, Texture2D hot, bool vertical)
        {
            bar.border = thumb.border = new RectOffset(4, 4, 4, 4);
            bar.padding = thumb.padding = new RectOffset(0, 0, 0, 0);
            bar.overflow = thumb.overflow = new RectOffset(0, 0, 0, 0);
            thumb.margin = new RectOffset(0, 0, 0, 0);
            if (vertical)
            {
                bar.fixedWidth = thumb.fixedWidth = 8f;
                bar.margin = new RectOffset(6, 2, 2, 2);
            }
            else
            {
                bar.fixedHeight = thumb.fixedHeight = 8f;
                bar.margin = new RectOffset(2, 2, 6, 2);
            }
            AllStates(bar, track, Color.white);
            AllStates(thumb, normal, Color.white);
            SetState(thumb.hover, hot, Color.white);
            SetState(thumb.active, hot, Color.white);
        }

        static GUIStyle TextStyle(GUIStyle baseStyle, Font font, int size, Color color, bool bold)
        {
            var s = new GUIStyle(baseStyle) { fontSize = size, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal };
            if (font != null) s.font = font;
            s.normal.textColor = color;
            return s;
        }

        static GUIStyle ButtonStyle(GUIStyle baseStyle, Font font, int size, bool bold,
            Texture2D normal, Texture2D hover, Texture2D active, Texture2D on, Texture2D onHover)
        {
            var s = new GUIStyle(baseStyle)
            {
                fontSize = size,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                border = new RectOffset(4, 4, 4, 4),
                overflow = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleCenter,
            };
            if (font != null) s.font = font;
            SetState(s.normal, normal, Text);
            SetState(s.hover, hover, Color.white);
            SetState(s.active, active, Color.white);
            SetState(s.focused, normal, Text);
            SetState(s.onNormal, on, Color.white);
            SetState(s.onHover, onHover, Color.white);
            SetState(s.onActive, active, Color.white);
            SetState(s.onFocused, on, Color.white);
            return s;
        }

        static void AllStates(GUIStyle s, Texture2D background, Color text)
        {
            foreach (GUIStyleState st in new[] { s.normal, s.hover, s.active, s.focused, s.onNormal, s.onHover, s.onActive, s.onFocused })
                SetState(st, background, text);
        }

        static void SetState(GUIStyleState state, Texture2D background, Color text)
        {
            state.background = background;
#if UNITY_EDITOR
            // Editor only (the property does not exist in a player build): the default skin's @2x images would win over ours on HiDPI.
            state.scaledBackgrounds = new Texture2D[0];
#endif
            state.textColor = text;
        }

        static Font OsFont(params string[] names)
        {
            try
            {
                string[] installed = Font.GetOSInstalledFontNames();
                if (installed != null && System.Array.IndexOf(installed, names[0]) < 0)
                {
                    // Keep the order but drop the first choice if it is missing, so the fallback chain starts with a real font.
                    var available = new System.Collections.Generic.List<string>();
                    foreach (string n in names) if (System.Array.IndexOf(installed, n) >= 0) available.Add(n);
                    if (available.Count == 0) return null;
                    names = available.ToArray();
                }
                Font f = Font.CreateDynamicFontFromOSFont(names, 14);
                if (f != null) f.name = names[0];
                return f;
            }
            catch
            {
                return null; // fall back to the default font
            }
        }

        // ───────────────────────── procedural textures (no art assets) ─────────────────────────

        static Texture2D NewTexture(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
        }

        static Texture2D Solid(Color c)
        {
            var t = NewTexture(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        /// <summary>Anti-aliased rounded rectangle: fill plus a 1 px border, for 9-slice use (border = radius + 1).</summary>
        static Texture2D Rounded(int size, float radius, Color fill, Color border, float borderWidth = 1f)
        {
            var t = NewTexture(size, size);
            var px = new Color[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - (half - radius), 0f);
                float qy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - (half - radius), 0f);
                float d = Mathf.Sqrt(qx * qx + qy * qy) - radius; // < 0 inside
                float coverage = Mathf.Clamp01(0.5f - d);
                float edge = borderWidth > 0f ? Mathf.Clamp01(d + borderWidth + 0.5f) : 0f;
                px[y * size + x] = Over(new Color(border.r, border.g, border.b, border.a * edge), fill, coverage);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>1 × n vertical gradient: <paramref name="bottom"/> at the bottom row, <paramref name="top"/> at the top.</summary>
        static Texture2D VerticalFade(int n, Color bottom, Color top)
        {
            var t = NewTexture(1, n);
            var px = new Color[n];
            for (int y = 0; y < n; y++) px[y] = Color.Lerp(bottom, top, (y + 0.5f) / n);
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Tab background: a line along the bottom (selected tab: 2 px amber) over an optional faint fill.</summary>
        static Texture2D Underline(Color line, int thickness, Color fill)
        {
            const int n = 8;
            var t = NewTexture(n, n);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = y < thickness ? line : fill; // texture rows go bottom → top
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Amber bar on the left with 4 px insets top and bottom (the main menu's selection bar).</summary>
        static Texture2D SelectionBar()
        {
            const int w = 8, h = 16;
            var t = NewTexture(w, h);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = x < 3 && y >= 4 && y < h - 4 ? Amber : Color.clear;
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        static Texture2D Circle(int n, Color c)
        {
            var t = NewTexture(n, n);
            var px = new Color[n * n];
            float r = n * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = x + 0.5f - r, dy = y + 0.5f - r;
                float a = Mathf.Clamp01(r - 0.5f - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                px[y * n + x] = new Color(c.r, c.g, c.b, c.a * a);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>Slider track: a 4 px rounded line through the middle of a 14 px tall texture.</summary>
        static Texture2D Track()
        {
            const int w = 12, h = 14;
            var t = NewTexture(w, h);
            var px = new Color[w * h];
            var line = new Color(1f, 1f, 1f, 0.16f);
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                px[y * w + x] = y >= 5 && y < 9 ? line : Color.clear;
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>The menu's on/off switch, drawn in the left 30 px of a 34×18 texture.</summary>
        static Texture2D Switch(bool on, bool hover)
        {
            const int w = 34, h = 18;
            var t = NewTexture(w, h);
            var px = new Color[w * h];
            Color trackColor = on ? new Color(Cyan.r, Cyan.g, Cyan.b, hover ? 0.45f : 0.35f) : new Color(1f, 1f, 1f, hover ? 0.22f : 0.14f);
            Color knobColor = on ? Cyan : (hover ? Text : TextPanel);
            float cy = h * 0.5f, knobX = on ? 21f : 9f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float fx = x + 0.5f, fy = y + 0.5f;
                // Pill from x = 2 to 28, height 14 (radius 7).
                float qx = Mathf.Max(Mathf.Abs(fx - 15f) - 6f, 0f), qy = Mathf.Abs(fy - cy);
                float dTrack = Mathf.Sqrt(qx * qx + qy * qy) - 7f;
                float dKnob = Mathf.Sqrt((fx - knobX) * (fx - knobX) + (fy - cy) * (fy - cy)) - 5f;
                Color c = Over(Color.clear, trackColor, Mathf.Clamp01(0.5f - dTrack));
                px[y * w + x] = Over(new Color(knobColor.r, knobColor.g, knobColor.b, Mathf.Clamp01(0.5f - dKnob)), c, 1f);
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>"top over bottom" alpha compositing, then scaled by <paramref name="coverage"/>.</summary>
        static Color Over(Color top, Color bottom, float coverage)
        {
            float a = top.a + bottom.a * (1f - top.a);
            if (a <= 1e-5f) return Color.clear;
            var c = new Color(
                (top.r * top.a + bottom.r * bottom.a * (1f - top.a)) / a,
                (top.g * top.a + bottom.g * bottom.a * (1f - top.a)) / a,
                (top.b * top.a + bottom.b * bottom.a * (1f - top.a)) / a,
                a * coverage);
            return c;
        }

        public static void Row(string label, string value, float labelWidth = 150f)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, Label, GUILayout.Width(labelWidth));
            GUILayout.Label(value, Value);
            GUILayout.EndHorizontal();
        }

        public static void Bar(Rect r, float fraction, Color color)
        {
            GUI.DrawTexture(r, barBackTexture);
            var c = GUI.color;
            GUI.color = color;
            float f = Mathf.Clamp01(fraction);
            GUI.DrawTexture(new Rect(r.x, r.yMax - r.height * f, r.width, r.height * f), barTexture);
            GUI.color = c;
        }
    }
}
