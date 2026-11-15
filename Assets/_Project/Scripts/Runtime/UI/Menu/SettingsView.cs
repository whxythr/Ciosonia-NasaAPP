using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using static SpaceSim.Runtime.Menu.MenuKit;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>
    /// The Settings screen shared by the main menu and the in-game pause menu (2026-11 redesign): numbered tabs on the left
    /// («Графика», «Управление», «Язык»), the rows of the tab in the middle, a preview / explanation panel on the right and a bottom
    /// bar with key hints and «Сбросить всё · Отмена · Применить». Changes take effect at once (the preview shows them); «Отмена»
    /// puts back everything as it was when the screen opened. The host calls <see cref="Open"/> when it shows the screen,
    /// <see cref="Tick"/> from Update and <see cref="Detach"/> when it is destroyed.
    /// </summary>
    public sealed partial class SettingsView
    {
        public enum Page { Graphics, Controls, Language, Audio }

        static readonly string[] PageNames = { "Графика", "Управление", "Язык", "Звук" };
        static readonly string[] LanguageNames = { "Русский", "English", "Қазақша" };
        const float BrightnessStepEv = 0.05f, SensitivityStep = 0.05f;

        /// <summary>Survives the rebuild after a language change, so «Отмена» still restores the state from before the screen opened.</summary>
        static GameSettings.Snapshot carried;

        readonly VisualElement host, list, previewImage;
        readonly Label heading, headingTag, infoEyebrow, infoTitle, infoText, statusLabel, fpsLabel, resLabel, vramLabel;
        readonly VisualElement statsRow;
        readonly List<VisualElement> tabs = new List<VisualElement>();
        readonly Dictionary<InputKey, (VisualElement row, Label key)> keyRows = new Dictionary<InputKey, (VisualElement, Label)>();
        readonly Action languageChanged;
        readonly Action<bool> closed;
        readonly Func<Texture2D> preview;
        GameSettings.Snapshot snapshot;
        Label resolutionLabel;
        SettingSlider brightness, sensitivity;
        VisualElement invertSwitch, vsyncSwitch;
        readonly List<(VisualElement item, Func<bool> on)> segments = new List<(VisualElement, Func<bool>)>();
        VisualElement viewport, content, scrollThumb;
        float scroll, fps = 60f;
        Page page;
        InputKey? listening;
        int captureStartFrame = -10, captureEndFrame = -10;
        string status = "";

        public Page CurrentPage => page;
        public bool IsCapturing => listening.HasValue;
        public InputKey? ListeningFor => listening;
        /// <summary>The host must not treat Esc / Back as "leave the screen" now (it cancelled a key prompt).</summary>
        public bool BlocksBack => IsCapturing || Time.frameCount <= captureEndFrame + 1;
        public string Status => status;

        /// <param name="screen">Absolutely positioned screen (1920×1080 reference) that receives the whole layout.</param>
        /// <param name="onClosed">The player left the screen: true = «Применить», false = «Отмена» (already restored).</param>
        /// <param name="onLanguageChanged">The host rebuilds its screens in the new language (on the next UI tick).</param>
        /// <param name="previewTexture">Picture for the Graphics preview (a frame of the game or the menu planet); may return null.</param>
        public SettingsView(VisualElement screen, Action<bool> onClosed, Action onLanguageChanged = null, Func<Texture2D> previewTexture = null)
        {
            host = screen;
            closed = onClosed;
            languageChanged = onLanguageChanged;
            preview = previewTexture;
            AddStyleSheet(screen, GameStyleSheet);

            var tabColumn = Add(screen, El("set-tabs"));
            for (int i = 0; i < PageNames.Length; i++)
            {
                var p = (Page)i;
                var tab = new Button(() => ShowPage(p));
                tab.AddToClassList("set-tab");
                tab.Add(Text((i + 1).ToString("00"), "set-tab__num", "t-mono"));
                tab.Add(Text(PageNames[i], "set-tab__label"));
                tabs.Add(tab);
                tabColumn.Add(tab);
            }

            var body = Add(screen, El("set-body"));
            var head = Add(body, El("set-heading"));
            heading = Add(head, Text("", "set-heading__title", "t-strong"));
            headingTag = Add(head, Text("", "set-heading__tag", "t-mono"));
            list = Add(body, El("set-list"));

            var side = Add(screen, El("set-preview"));
            infoEyebrow = Add(side, Text("", "eyebrow", "eyebrow--muted"));
            previewImage = Add(side, El("set-preview__image"));
            infoTitle = Add(side, Text("", "panel__title", "t-strong"));
            infoTitle.style.marginTop = 6;
            statsRow = Add(side, El("set-preview__stats"));
            fpsLabel = Stat(statsRow, "ЧАСТОТА КАДРОВ", "set-preview__value--green");
            resLabel = Stat(statsRow, "КАДР", null);
            vramLabel = Stat(statsRow, "ВИДЕОПАМЯТЬ", "set-preview__value--amber");
            infoText = Add(side, Text("", "hz-note"));
            statusLabel = Add(side, Text("", "settings-status"));

            var bottom = Add(screen, El("set-bottom"));
            KeyButton(bottom, "ESC", Loc.T("НАЗАД"), Apply);
            KeyButton(bottom, "Q / E", Loc.T("ВКЛАДКИ"), () => ShowPage((Page)(((int)page + 1) % PageNames.Length)));
            KeyButton(bottom, "R", Loc.T("СБРОС РАЗДЕЛА"), ResetPage);
            bottom.Add(El("grow"));
            bottom.Add(TextButton(Loc.T("СБРОСИТЬ ВСЁ"), ResetAll));
            bottom.Add(ButtonEl(Loc.T("ОТМЕНА"), Cancel));
            bottom.Add(ButtonEl(Loc.T("ПРИМЕНИТЬ") + "  →", Apply, primary: true));

            // While a key prompt is open, keyboard navigation must not move the focus, press buttons or leave the screen.
            screen.RegisterCallback<NavigationMoveEvent>(BlockWhileCapturing, TrickleDown.TrickleDown);
            screen.RegisterCallback<NavigationSubmitEvent>(BlockWhileCapturing, TrickleDown.TrickleDown);
            screen.RegisterCallback<NavigationCancelEvent>(BlockWhileCapturing, TrickleDown.TrickleDown);
            screen.RegisterCallback<KeyDownEvent>(BlockWhileCapturing, TrickleDown.TrickleDown);
            screen.RegisterCallback<PointerDownEvent>(e =>
            {
                if (IsCapturing && !IsInsideBindRow(e.target as VisualElement)) CancelCapture();
            }, TrickleDown.TrickleDown);

            GameSettings.Changed += Refresh;
            snapshot = carried ?? GameSettings.Capture();
            carried = null;
            ShowPage(Page.Graphics);
        }

        static Label Stat(VisualElement parent, string label, string valueClass)
        {
            var s = Add(parent, El("set-preview__stat"));
            s.Add(Text(label, "eyebrow", "eyebrow--muted"));
            return Add(s, Text("", "set-preview__value", valueClass, "t-mono"));
        }

        static void KeyButton(VisualElement parent, string key, string label, Action onClick)
        {
            VisualElement hint = KeyHint(parent, key, label);
            hint.AddToClassList("key-hint--button");
            hint.RegisterCallback<ClickEvent>(_ => onClick());
        }

        static Button TextButton(string label, Action onClick)
        {
            Button b = ButtonEl(label, onClick);
            b.AddToClassList("btn--text");
            return b;
        }

        /// <summary>Unsubscribes from <see cref="GameSettings.Changed"/>.</summary>
        public void Detach() => GameSettings.Changed -= Refresh;

        /// <summary>The host shows the screen: remembers the current settings for «Отмена» and opens the first tab.</summary>
        public void Open(Page first = Page.Graphics)
        {
            snapshot = GameSettings.Capture();
            ShowPage(first);
        }

        /// <summary>Call every frame while the screen is visible.</summary>
        public void Tick()
        {
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            if (page == Page.Graphics && fpsLabel != null && Time.frameCount % 10 == 0) RefreshStats();
            if (listening.HasValue)
            {
                if (Time.frameCount == captureStartFrame) return; // ignore the key that opened the prompt
                if (FlightInput.EscapePressed)
                {
                    CancelCapture();
                    return;
                }
                if (FlightInput.TryGetPressedKey(out KeyCode code)) Assign(listening.Value, code);
                return;
            }
            if (Time.frameCount <= captureEndFrame + 1) return;
            if (FlightInput.KeyDown(KeyCode.Q)) ShowPage((Page)(((int)page + PageNames.Length - 1) % PageNames.Length));
            else if (FlightInput.KeyDown(KeyCode.E)) ShowPage((Page)(((int)page + 1) % PageNames.Length));
            else if (FlightInput.KeyDown(KeyCode.R)) ResetPage();
        }

        public void ShowPage(Page p)
        {
            if (IsCapturing) CancelCapture();
            page = p;
            for (int i = 0; i < tabs.Count; i++) tabs[i].EnableInClassList("set-tab--on", i == (int)p);
            status = "";
            BuildPage();
        }

        /// <summary>«Применить»: keeps the changes (they are already saved) and leaves.</summary>
        public void Apply()
        {
            if (IsCapturing) CancelCapture();
            snapshot = GameSettings.Capture();
            closed?.Invoke(true);
        }

        /// <summary>«Отмена»: puts every setting back as it was when the screen opened, then leaves.</summary>
        public void Cancel()
        {
            if (IsCapturing) CancelCapture();
            bool lang = GameSettings.Restore(snapshot);
            closed?.Invoke(false);
            if (lang) languageChanged?.Invoke();
        }

        /// <summary>Opens the "press a key" prompt for a command.</summary>
        public void BeginCapture(InputKey key)
        {
            if (!KeyBindings.IsRebindable(key)) return;
            if (page != Page.Controls) ShowPage(Page.Controls);
            listening = key;
            captureStartFrame = Time.frameCount;
            status = Loc.T("Нажмите клавишу для «") + KeyBindings.LabelOf(key) + Loc.T("». Esc — отмена.");
            RefreshKeys();
            if (keyRows.TryGetValue(key, out var r)) EnsureVisible(r.row);
            RefreshInfo();
        }

        /// <summary>Closes the "press a key" prompt without changing the binding.</summary>
        public void CancelCapture()
        {
            if (!listening.HasValue) return;
            status = Loc.T("Отмена: клавиша для «") + KeyBindings.LabelOf(listening.Value) + Loc.T("» не изменилась.");
            listening = null;
            captureEndFrame = Time.frameCount;
            RefreshKeys();
            RefreshInfo();
        }

        /// <summary>Re-reads every value from <see cref="GameSettings"/> (after a reset or a change elsewhere).</summary>
        public void Refresh()
        {
            brightness?.SetValueWithoutNotify(GameSettings.ExposureEv);
            sensitivity?.SetValueWithoutNotify(GameSettings.MouseSensitivity);
            invertSwitch?.EnableInClassList("switch--on", GameSettings.InvertMouseY);
            vsyncSwitch?.EnableInClassList("switch--on", GameSettings.VSync);
            foreach (var (item, on) in segments) item.EnableInClassList("set-seg__item--on", on());
            if (resolutionLabel != null && page == Page.Graphics) resolutionLabel.text = ResolutionText();
            RefreshKeys();
            RefreshAudio();
            RefreshInfo();
        }

        void Assign(InputKey key, KeyCode code)
        {
            KeyCode before = GameSettings.GetBinding(key);
            InputKey? swapped = GameSettings.SetBinding(key, code);
            listening = null;
            captureEndFrame = Time.frameCount;
            string name = KeyBindings.DisplayName(code);
            if (before == code) status = "«" + KeyBindings.LabelOf(key) + Loc.T("» и так на ") + name + ".";
            else if (swapped.HasValue)
                status = "«" + KeyBindings.LabelOf(key) + Loc.T("» — теперь ") + name + Loc.T(". Клавиша была у «") + KeyBindings.LabelOf(swapped.Value) +
                         Loc.T("», ей досталась ") + KeyBindings.DisplayName(GameSettings.GetBinding(swapped.Value)) + ".";
            else status = "«" + KeyBindings.LabelOf(key) + Loc.T("» — теперь ") + name + ".";
            Refresh();
        }

        void ResetPage()
        {
            if (IsCapturing) CancelCapture();
            switch (page)
            {
                case Page.Graphics:
                    GameSettings.ResetGraphics();
                    ScreenResolution.ResetToDisplay();
                    status = Loc.T("Графика по умолчанию: яркость 100 %, разрешение экрана") + " " + ResolutionText() + ".";
                    break;
                case Page.Language:
                    if (GameSettings.Language != SpaceSim.Localization.Language.Russian) SetLanguage(SpaceSim.Localization.Language.Russian);
                    break;
                case Page.Audio:
                    GameSettings.ResetAudio();
                    status = Loc.T("Музыка по умолчанию.");
                    break;
                default:
                    GameSettings.ResetMouse();
                    GameSettings.ResetBindings();
                    status = Loc.T("Мышь и все клавиши — как по умолчанию.");
                    break;
            }
            Refresh();
        }

        void ResetAll()
        {
            if (IsCapturing) CancelCapture();
            GameSettings.ResetGraphics();
            ScreenResolution.ResetToDisplay();
            GameSettings.ResetMouse();
            GameSettings.ResetBindings();
            GameSettings.ResetAudio();
            status = Loc.T("Все настройки — по умолчанию (язык не меняется).");
            Refresh();
        }

        // ─────────────────────────────── pages ───────────────────────────────

        void BuildPage()
        {
            list.Clear();
            keyRows.Clear();
            segments.Clear();
            brightness = sensitivity = null;
            invertSwitch = vsyncSwitch = null;
            resolutionLabel = null;
            viewport = content = scrollThumb = null;
            scroll = 0f;
            heading.text = Loc.T(PageNames[(int)page]);

            VisualElement rows = list;
            if (page == Page.Controls || page == Page.Audio) rows = BuildScroll();
            switch (page)
            {
                case Page.Graphics:
                {
                    headingTag.text = Loc.T("ПРИМЕНЯЕТСЯ СРАЗУ");
                    var mode = SettingRow(rows, Loc.T("Режим экрана"),
                        Application.isEditor ? Loc.T("В редакторе Unity окно не меняется — только в собранной игре") : null);
                    Segmented(mode, new[] { Loc.T("ОКНО"), Loc.T("БЕЗ РАМКИ"), Loc.T("ПОЛНЫЙ ЭКРАН") },
                        i => (int)GameSettings.DisplayMode == i, i => GameSettings.DisplayMode = (GameSettings.WindowMode)i);

                    var res = SettingRow(rows, Loc.T("Разрешение"), Loc.T("Размер изображения в пикселях"));
                    resolutionLabel = Stepper(res, ResolutionText(), StepResolution);

                    var cap = SettingRow(rows, Loc.T("Ограничение частоты кадров"), Loc.T("Не действует, пока включена вертикальная синхронизация"));
                    int[] caps = GameSettings.FrameRateOptions;
                    Segmented(cap, new[] { "30", "60", "120", Loc.T("НЕТ") }, i => GameSettings.FrameRateLimit == caps[i], i => GameSettings.FrameRateLimit = caps[i]);

                    var vs = SettingRow(rows, Loc.T("Вертикальная синхронизация"), Loc.T("Кадр совпадает с обновлением монитора: без разрывов"));
                    vsyncSwitch = Switch(vs, GameSettings.VSync, () => GameSettings.VSync = !GameSettings.VSync);

                    var b = SettingRow(rows, Loc.T("Яркость"), Loc.T("Экспозиция кадра от −2 до +2 ступеней (EV)"));
                    brightness = new SettingSlider(-2f, 2f, BrightnessStepEv, GameSettings.ExposureEv,
                        ev => Mathf.RoundToInt(Mathf.Pow(2f, ev) * 100f) + " %",
                        ev => GameSettings.Brightness = Mathf.Pow(2f, ev));
                    b.Add(brightness);
                    SliderKeys(b, brightness);
                    break;
                }
                case Page.Language:
                {
                    headingTag.text = "";
                    var row = SettingRow(rows, Loc.T("Язык игры"), Loc.T("Меню, приборы и окна игры"));
                    Stepper(row, LanguageNames[(int)GameSettings.Language], StepLanguage);
                    break;
                }
                case Page.Audio:
                    BuildAudio(rows);
                    break;
                default:
                    headingTag.text = Loc.T("МЫШЬ И КЛАВИШИ");
                    BuildControls(rows);
                    break;
            }
            if (previewImage != null)
            {
                Texture2D t = page == Page.Graphics ? preview?.Invoke() : null;
                previewImage.style.display = t != null ? DisplayStyle.Flex : DisplayStyle.None;
                if (t != null) previewImage.style.backgroundImage = new StyleBackground(t);
            }
            statsRow.style.display = page == Page.Graphics ? DisplayStyle.Flex : DisplayStyle.None;
            ApplyFonts(list);
            Refresh();
            RefreshStats();
        }

        VisualElement BuildScroll()
        {
            viewport = Add(list, El("scroll-viewport"));
            content = Add(viewport, El("scroll-content"));
            var bar = Add(list, El("scroll-bar"));
            scrollThumb = Add(bar, El("scroll-bar__thumb"));
            list.style.flexDirection = FlexDirection.Row;
            viewport.RegisterCallback<WheelEvent>(e =>
            {
                float notches = Mathf.Abs(e.delta.y) >= 10f ? e.delta.y / 120f : e.delta.y;
                ScrollBy(notches * 46f);
                e.StopPropagation();
            });
            viewport.RegisterCallback<GeometryChangedEvent>(_ => ScrollBy(0f));
            content.RegisterCallback<GeometryChangedEvent>(_ => ScrollBy(0f));
            return content;
        }

        void BuildControls(VisualElement rows)
        {
            rows.Add(Text("МЫШЬ", "bind-group", "t-mono"));
            var row = SettingRow(rows, Loc.T("Чувствительность мыши"), Loc.T("Обзор в полёте (ПКМ / СКМ) и взгляд на поверхности"));
            sensitivity = new SettingSlider(GameSettings.MinMouseSensitivity, GameSettings.MaxMouseSensitivity, SensitivityStep,
                GameSettings.MouseSensitivity, v => "×" + v.ToString("0.00", CultureInfo.InvariantCulture),
                v => GameSettings.MouseSensitivity = v);
            row.Add(sensitivity);
            SliderKeys(row, sensitivity);
            var inv = SettingRow(rows, Loc.T("Инвертировать ось Y"), Loc.T("Мышь вверх — взгляд вниз, как в авиасимуляторах"));
            invertSwitch = Switch(inv, GameSettings.InvertMouseY, () => GameSettings.InvertMouseY = !GameSettings.InvertMouseY);

            string group = null;
            foreach (KeyAction a in KeyBindings.Actions)
            {
                if (a.Group != group)
                {
                    group = a.Group;
                    rows.Add(Text(group, "bind-group", "t-mono"));
                }
                InputKey key = a.Key;
                var r = SettingRow(rows, a.Label, null);
                r.AddToClassList("setting--bind");
                var cap = Add(r, Text("", "bind-key", "t-mono"));
                r.RegisterCallback<ClickEvent>(_ => OnBindRow(key));
                r.RegisterCallback<NavigationSubmitEvent>(_ => OnBindRow(key));
                r.RegisterCallback<FocusInEvent>(_ => EnsureVisible(r));
                keyRows[key] = (r, cap);
            }
        }

        void OnBindRow(InputKey key)
        {
            if (Time.frameCount <= captureEndFrame + 1) return; // the key that just finished a prompt must not reopen it
            if (listening == key) CancelCapture();
            else BeginCapture(key);
        }

        void RefreshKeys()
        {
            foreach (KeyValuePair<InputKey, (VisualElement row, Label key)> p in keyRows)
            {
                bool on = listening == p.Key;
                p.Value.key.text = on ? Loc.T("НАЖМИТЕ КЛАВИШУ") : KeyBindings.DisplayName(GameSettings.GetBinding(p.Key));
                p.Value.key.EnableInClassList("bind-key--listening", on);
                p.Value.key.EnableInClassList("bind-key--custom", !on && !GameSettings.IsDefaultBinding(p.Key));
                p.Value.row.EnableInClassList("setting--listening", on);
            }
        }

        void RefreshStats()
        {
            if (fpsLabel == null) return;
            fpsLabel.text = Mathf.RoundToInt(fps) + " fps";
            resLabel.text = Screen.width + " × " + Screen.height;
            int mb = SystemInfo.graphicsMemorySize;
            vramLabel.text = mb > 0 ? (mb / 1024f).ToString("0.#", CultureInfo.InvariantCulture) + Loc.T(" ГБ") : "—";
        }

        void RefreshInfo()
        {
            switch (page)
            {
                case Page.Graphics:
                    infoEyebrow.text = Loc.T("ПРЕДПРОСМОТР");
                    infoTitle.text = "";
                    infoTitle.style.display = DisplayStyle.None;
                    infoText.text = Loc.T("Изменения видны сразу. «Отмена» вернёт всё, как было до входа в настройки. ") +
                                    Loc.T("Яркость меняет только изображение: освещённость в симуляции, приборы и физика те же.");
                    break;
                case Page.Language:
                    infoEyebrow.text = Loc.T("ЯЗЫК");
                    infoTitle.style.display = DisplayStyle.Flex;
                    infoTitle.text = Loc.T("Язык");
                    infoText.text = Loc.T("Текст меню, приборов и окон меняется сразу.");
                    break;
                case Page.Audio:
                    AudioInfo();
                    break;
                default:
                    infoEyebrow.text = Loc.T("УПРАВЛЕНИЕ");
                    infoTitle.style.display = DisplayStyle.Flex;
                    infoTitle.text = Loc.T("Клавиши");
                    infoText.text = Loc.T("Нажмите на команду, затем нужную клавишу. Esc — отмена. Если клавиша уже занята, команды ") +
                                    Loc.T("поменяются клавишами. Esc всегда закрывает окна и открывает это меню. ") +
                                    Loc.T("Чувствительность умножает поворот камеры (ПКМ / СКМ) и взгляд астронавта; ×1 — как было изначально.");
                    break;
            }
            statusLabel.text = status;
            statusLabel.style.display = string.IsNullOrEmpty(status) ? DisplayStyle.None : DisplayStyle.Flex;
            statusLabel.EnableInClassList("settings-status--active", IsCapturing);
        }

        // ─────────────────────────────── resolution and language ───────────────────────────────

        static string ResolutionText()
        {
            Vector2Int r = ScreenResolution.Current;
            return r.x + " × " + r.y;
        }

        void StepResolution(int direction)
        {
            List<Vector2Int> options = ScreenResolution.Options();
            Vector2Int current = ScreenResolution.Current;
            int index = options.FindIndex(o => o.x == current.x && o.y == current.y);
            int next = Mathf.Clamp(index + direction, 0, options.Count - 1);
            if (next != index) ScreenResolution.Apply(options[next].x, options[next].y);
            if (resolutionLabel != null) resolutionLabel.text = options[next].x + " × " + options[next].y;
        }

        void StepLanguage(int direction)
        {
            int count = LanguageNames.Length;
            SetLanguage((SpaceSim.Localization.Language)(((int)GameSettings.Language + direction + count) % count));
        }

        void SetLanguage(SpaceSim.Localization.Language language)
        {
            carried = snapshot; // the host rebuilds this view in the new language
            GameSettings.Language = language;
            languageChanged?.Invoke();
        }

        // ─────────────────────────────── controls ───────────────────────────────

        /// <summary>The "‹ value ›" control: arrows, ← / → on the focused row.</summary>
        static Label Stepper(VisualElement row, string value, Action<int> step)
        {
            var s = Add(row, El("stepper"));
            var previous = new Button(() => step(-1)) { text = "‹" };
            var next = new Button(() => step(1)) { text = "›" };
            previous.AddToClassList("stepper__arrow");
            next.AddToClassList("stepper__arrow");
            s.Add(previous);
            var label = Add(s, Text(value, "stepper__value", "t-mono"));
            s.Add(next);
            row.RegisterCallback<NavigationMoveEvent>(e =>
            {
                int dir = e.direction == NavigationMoveEvent.Direction.Left ? -1 : e.direction == NavigationMoveEvent.Direction.Right ? 1 : 0;
                if (dir == 0) return;
                step(dir);
                e.StopPropagation();
                row.focusController?.IgnoreEvent(e);
            });
            return label;
        }

        /// <summary>Boxed options, the chosen one cream ("LOW · MED · HIGH" in the mock-up).</summary>
        void Segmented(VisualElement row, string[] options, Func<int, bool> isOn, Action<int> choose)
        {
            var seg = Add(row, El("set-seg"));
            for (int i = 0; i < options.Length; i++)
            {
                int index = i;
                var b = new Button(() => { choose(index); Refresh(); }) { text = options[i] };
                b.AddToClassList("set-seg__item");
                b.AddToClassList("t-mono");
                seg.Add(b);
                segments.Add((b, () => isOn(index)));
            }
        }

        static VisualElement Switch(VisualElement row, bool on, Action toggle)
        {
            var sw = Add(row, El("switch", on ? "switch--on" : null));
            sw.Add(El("switch__knob"));
            row.RegisterCallback<ClickEvent>(_ => toggle());
            row.RegisterCallback<NavigationSubmitEvent>(_ => toggle());
            return sw;
        }

        static VisualElement SettingRow(VisualElement parent, string label, string hint)
        {
            var row = Add(parent, El("setting", "setting--live"));
            row.focusable = true;
            row.RegisterCallback<FocusInEvent>(_ => row.AddToClassList("setting--focused"));
            row.RegisterCallback<FocusOutEvent>(_ => row.RemoveFromClassList("setting--focused"));
            var text = Add(row, El("grow"));
            text.Add(Text(label, "setting__label"));
            if (hint != null) text.Add(Text(hint, "setting__hint"));
            return row;
        }

        /// <summary>← / → change the value of a focused slider row (instead of moving the focus).</summary>
        static void SliderKeys(VisualElement row, SettingSlider slider)
        {
            row.RegisterCallback<NavigationMoveEvent>(e =>
            {
                int dir = e.direction == NavigationMoveEvent.Direction.Left ? -1 : e.direction == NavigationMoveEvent.Direction.Right ? 1 : 0;
                if (dir == 0) return;
                slider.StepBy(dir);
                e.StopPropagation();
                row.focusController?.IgnoreEvent(e);
            });
        }

        void BlockWhileCapturing(EventBase e)
        {
            if (!IsCapturing && Time.frameCount > captureEndFrame + 1) return;
            e.StopImmediatePropagation();
            host.focusController?.IgnoreEvent(e);
        }

        static bool IsInsideBindRow(VisualElement e)
        {
            for (; e != null; e = e.parent)
                if (e.ClassListContains("setting--bind")) return true;
            return false;
        }

        // ─────────────────────────────── scrolling ───────────────────────────────

        void ScrollBy(float dy)
        {
            if (viewport == null || content == null) return;
            float view = viewport.layout.height, total = content.layout.height;
            if (float.IsNaN(view) || float.IsNaN(total) || view <= 0f) return;
            float max = Mathf.Max(0f, total - view);
            scroll = Mathf.Clamp(scroll + dy, 0f, max);
            content.style.top = -scroll;
            if (scrollThumb != null)
            {
                bool needed = max > 0.5f;
                scrollThumb.parent.style.display = needed ? DisplayStyle.Flex : DisplayStyle.None;
                if (needed)
                {
                    float frac = view / total;
                    scrollThumb.style.height = Length.Percent(frac * 100f);
                    scrollThumb.style.top = Length.Percent((1f - frac) * (scroll / max) * 100f);
                }
            }
        }

        void EnsureVisible(VisualElement row)
        {
            if (viewport == null) return;
            float top = row.layout.y, bottom = top + row.layout.height, view = viewport.layout.height;
            if (float.IsNaN(top) || view <= 0f) return;
            if (top < scroll) ScrollBy(top - scroll - 8f);
            else if (bottom > scroll + view) ScrollBy(bottom - scroll - view + 8f);
        }

        /// <summary>Slider: track, blue fill, cream bar knob, value — draggable and steppable.</summary>
        sealed class SettingSlider : VisualElement
        {
            readonly VisualElement track, fill, knob;
            readonly Label valueLabel;
            readonly float min, max, step;
            readonly Func<float, string> format;
            readonly Action<float> changed;
            float value;
            int pointer = -1;

            public SettingSlider(float min, float max, float step, float value, Func<float, string> format, Action<float> changed)
            {
                this.min = min;
                this.max = max;
                this.step = step;
                this.format = format;
                this.changed = changed;
                AddToClassList("slider");
                AddToClassList("slider--live");
                track = MenuKit.Add(this, MenuKit.El("slider__track"));
                fill = MenuKit.Add(track, MenuKit.El("slider__fill"));
                knob = MenuKit.Add(track, MenuKit.El("slider__knob"));
                valueLabel = MenuKit.Add(this, MenuKit.Text("", "slider__value", "t-mono"));
                RegisterCallback<PointerDownEvent>(OnPointerDown);
                RegisterCallback<PointerMoveEvent>(OnPointerMove);
                RegisterCallback<PointerUpEvent>(OnPointerUp);
                RegisterCallback<PointerCaptureOutEvent>(_ => EndDrag());
                SetValueWithoutNotify(value);
            }

            public void StepBy(int direction) => Set(value + direction * step);

            public void SetValueWithoutNotify(float v)
            {
                value = Mathf.Clamp(v, min, max);
                float t = max > min ? (value - min) / (max - min) : 0f;
                fill.style.width = Length.Percent(t * 100f);
                knob.style.left = Length.Percent(t * 100f);
                valueLabel.text = format(value);
            }

            void Set(float v)
            {
                v = Mathf.Clamp(Mathf.Round(v / step) * step, min, max);
                if (Mathf.Abs(v - value) < step * 0.01f) return;
                SetValueWithoutNotify(v);
                changed?.Invoke(value);
            }

            void OnPointerDown(PointerDownEvent e)
            {
                if (e.button != 0) return;
                pointer = e.pointerId;
                this.CapturePointer(pointer);
                AddToClassList("slider--drag");
                SetFromPanel(e.position);
                e.StopPropagation();
            }

            void OnPointerMove(PointerMoveEvent e)
            {
                if (e.pointerId != pointer || !this.HasPointerCapture(pointer)) return;
                SetFromPanel(e.position);
            }

            void OnPointerUp(PointerUpEvent e)
            {
                if (e.pointerId != pointer) return;
                this.ReleasePointer(pointer);
                EndDrag();
            }

            void EndDrag()
            {
                pointer = -1;
                RemoveFromClassList("slider--drag");
            }

            void SetFromPanel(Vector3 panelPosition)
            {
                float w = track.layout.width;
                if (float.IsNaN(w) || w <= 0f) return;
                Vector2 local = track.WorldToLocal(panelPosition);
                Set(Mathf.Lerp(min, max, Mathf.Clamp01(local.x / w)));
            }
        }
    }
}
