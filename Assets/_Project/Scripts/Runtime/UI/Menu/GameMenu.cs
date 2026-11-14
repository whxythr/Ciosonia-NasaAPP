using SpaceSim.Localization;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static SpaceSim.Runtime.Menu.MenuKit;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>What the pause screen shows about the flight (filled by the HUD, which knows the vehicle).</summary>
    public sealed class PauseSummary
    {
        public string Eyebrow = "", Title = "", Subtitle = "", Clock = "", BodyName = "";
        public string Altitude = "—", AltitudeUnit = "", Velocity = "—", VelocityUnit = "";
        public readonly List<KeyValuePair<string, string>> Rows = new List<KeyValuePair<string, string>>();
        public readonly List<KeyValuePair<string, bool>> Goals = new List<KeyValuePair<string, bool>>();
        /// <summary>Text after «Новая ракета на старте» ("ПОЛЁТ T+ 03:12 БУДЕТ ПОТЕРЯН"), empty on the pad.</summary>
        public string RevertCost = "";
    }

    /// <summary>The flight side of the pause screen.</summary>
    public interface IFlightPauseHost
    {
        PauseSummary PauseSummary();
        void RevertToLaunch();
        void RevertToAssembly();
        SpaceSim.Economy.PlayerProfile Profile { get; }
    }

    /// <summary>
    /// The in-game pause screen (2026-11 redesign, Esc): «ПАУЗА» with the actions — resume, new rocket on the pad, back to the
    /// hangar, save the game (a manual save slot with a picture), settings, main menu, quit — and the flight summary with the
    /// research goals on the right. Esc first leaves a text field, then closes the window on top (hangar, shop, Academy, navigation,
    /// manual, map…); only with nothing open does it open this screen. While it is open the simulation is paused (unless it already
    /// was), the HUD hides and every flight hotkey is ignored.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameMenu : MonoBehaviour
    {
        /// <summary>Scene loaded by «Главное меню» (SpaceSim ▸ Create Main Menu Scene puts it in Build Settings).</summary>
        public const string MainMenuScene = "MainMenu";

        UIDocument document;
        PanelSettings panel;
        VisualElement root, modal, pauseScreen, settingsScreen, panelBox, list;
        Label modalEyebrow, modalTitle, modalText, modalConfirmLabel, eyebrow, subtitle, statusLabel, footLabel;
        Action modalConfirm;
        SettingsView settings;
        SimulationController simulation;
        IFlightPauseHost flight;
        Func<bool> closeTopWindow;
        bool open, pausedByMenu, inSettings;
        Texture2D lastFrame;
        readonly List<VisualElement> items = new List<VisualElement>();
        string status = "";

        public bool IsOpen => open;
        public bool InSettings => inSettings;
        public bool ModalOpen => modal != null && modal.ClassListContains("modal--open");
        public SettingsView Settings => settings;
        /// <summary>The status line after the last action («Сохранено: …»).</summary>
        public string Status => status;
        /// <summary>True when the main menu scene is in Build Settings (the «Главное меню» button works).</summary>
        public static bool MainMenuAvailable => Application.CanStreamedLevelBeLoaded(MainMenuScene);

        public void Initialize(SimulationController sim) => simulation = sim;

        /// <summary>The HUD of the current flight stack (rebuilt on a body switch).</summary>
        public void SetFlight(IFlightPauseHost host) => flight = host;

        /// <summary>Esc closes the window on top through this (the HUD knows its windows); returns false if none was open.</summary>
        public void SetWindowCloser(Func<bool> closer) => closeTopWindow = closer;

        void Awake()
        {
            document = GetComponent<UIDocument>();
            panel = CreatePanel("GameMenuPanel (runtime)", opaque: false, sortingOrder: 100);
            document.panelSettings = panel;
        }

        void OnEnable()
        {
            if (document != null) Build(document.rootVisualElement);
        }

        void OnDisable()
        {
            settings?.Detach();
            if (open) Close();
        }

        void OnDestroy()
        {
            FlightInput.PauseMenuOpen = false;
            if (pausedByMenu && simulation != null) simulation.SetPaused(false);
            pausedByMenu = false;
            if (panel != null) Destroy(panel);
            if (lastFrame != null) Destroy(lastFrame);
        }

        void Update()
        {
            if (open && inSettings) settings?.Tick();
            if (FlightInput.EscapePressed && !CrewScreen.OwnsEscape && !LoadingScreen.IsShowing) HandleEscape(); // no pause menu under the loading screen
        }

        /// <summary>
        /// What Esc does now: cancel a key prompt / close the confirmation / leave the settings / close this screen; otherwise leave a
        /// text field, close the top window, or — with nothing open — open this screen.
        /// </summary>
        public void HandleEscape()
        {
            if (open)
            {
                if (settings != null && settings.BlocksBack) return; // that Esc cancelled a key prompt
                if (ModalOpen) CloseModal();
                else if (inSettings) settings.Apply();
                else Close();
                return;
            }
            if (GUIUtility.keyboardControl != 0)
            {
                GUIUtility.keyboardControl = 0; // Esc leaves a text field first
                return;
            }
            if (closeTopWindow != null && closeTopWindow()) return;
            Open();
        }

        public void Open()
        {
            if (open || root == null) return;
            open = true;
            FlightInput.PauseMenuOpen = true;
            GUIUtility.keyboardControl = 0;
            if (simulation != null && !simulation.Paused)
            {
                simulation.SetPaused(true);
                pausedByMenu = true;
            }
            status = "";
            ShowPause();
            root.style.display = DisplayStyle.Flex;
            root.schedule.Execute(() => { if (open) root.AddToClassList("game-menu--open"); }).StartingIn(16);
            if (isActiveAndEnabled) StartCoroutine(CaptureFrame(null)); // the 3D view without HUD, for the settings preview
        }

        public void Close()
        {
            if (!open) return;
            settings?.CancelCapture();
            CloseModal();
            open = false;
            inSettings = false;
            FlightInput.PauseMenuOpen = false;
            if (pausedByMenu && simulation != null) simulation.SetPaused(false);
            pausedByMenu = false;
            root.RemoveFromClassList("game-menu--open");
            root.schedule.Execute(() => { if (!open) root.style.display = DisplayStyle.None; }).StartingIn(200);
            root.focusController?.focusedElement?.Blur();
        }

        /// <summary>Opens the settings page of the pause screen.</summary>
        public void ShowSettings()
        {
            if (!open) Open();
            inSettings = true;
            pauseScreen.style.display = DisplayStyle.None;
            settingsScreen.style.display = DisplayStyle.Flex;
            settings.Open();
        }

        public void ShowPause()
        {
            inSettings = false;
            if (settingsScreen != null) settingsScreen.style.display = DisplayStyle.None;
            if (pauseScreen != null) pauseScreen.style.display = DisplayStyle.Flex;
            RefreshPause();
        }

        // ─────────────────────────────── layout ───────────────────────────────

        void Build(VisualElement documentRoot)
        {
            documentRoot.Clear();
            items.Clear();
            StretchRoot(documentRoot);
            AddStyleSheet(documentRoot, MenuStyleSheet);
            AddStyleSheet(documentRoot, GameStyleSheet);

            root = El("game-menu");
            root.style.display = DisplayStyle.None;
            documentRoot.Add(root);
            ApplyRootFonts(root);

            // ── pause (photo 5) ──
            pauseScreen = Add(root, El("game-menu__page"));
            eyebrow = Add(pauseScreen, Text("", "pause-eyebrow", "t-mono"));
            pauseScreen.Add(Text("ПАУЗА", "pause-title"));
            subtitle = Add(pauseScreen, Text("", "pause-sub"));
            list = Add(pauseScreen, El("pause-list"));
            statusLabel = Add(pauseScreen, Text("", "pause-status"));
            footLabel = Add(pauseScreen, Text("", "pause-foot", "t-mono"));
            panelBox = Add(pauseScreen, El("pause-panel"));

            // ── settings (photo 2) ──
            settingsScreen = Add(root, El("game-menu__page"));
            settingsScreen.style.display = DisplayStyle.None;
            var head = Add(settingsScreen, El("screen-head"));
            head.Add(Text("НАСТРОЙКИ", "screen-title"));
            settings?.Detach();
            settings = new SettingsView(settingsScreen, _ => ShowPause(), RebuildForLanguage, () => lastFrame);

            BuildModal();
        }

        void BuildItems(PauseSummary s)
        {
            list.Clear();
            items.Clear();
            Item("ПРОДОЛЖИТЬ ПОЛЁТ", "ESC", Close);
            Item("НОВАЯ РАКЕТА НА СТАРТЕ", s != null ? s.RevertCost : "", () =>
            {
                if (flight == null) return;
                if (s != null && s.RevertCost.Length > 0)
                    Confirm(Loc.T("НОВАЯ РАКЕТА"), Loc.T("Начать заново на старте?"),
                        Loc.T("Текущий полёт закончится: ракета и образцы на борту будут потеряны, экипаж вернётся в отряд."), Loc.T("НА СТАРТ"), () =>
                        {
                            Close();
                            flight.RevertToLaunch();
                        });
                else
                {
                    Close();
                    flight.RevertToLaunch();
                }
            });
            Item("В АНГАР", "", () =>
            {
                if (flight == null) return;
                Confirm(Loc.T("СБОРКА"), Loc.T("Вернуться в ангар?"),
                    Loc.T("Новая ракета встанет на площадку и откроется ангар. Текущий полёт закончится."), Loc.T("В АНГАР"), () =>
                    {
                        Close();
                        flight.RevertToAssembly();
                    });
            });
            Item("СОХРАНИТЬ ИГРУ", "", SaveGame);
            Item("НАСТРОЙКИ", "", ShowSettings);
            Item("ГЛАВНОЕ МЕНЮ", MainMenuAvailable ? "" : Loc.T("НЕТ СЦЕНЫ"), () =>
            {
                if (!MainMenuAvailable) return;
                Confirm(Loc.T("ГЛАВНОЕ МЕНЮ"), Loc.T("Выйти в главное меню?"),
                    Loc.T("Текущий полёт не сохраняется. Профиль — кредиты, модули, образцы, экипаж — уже сохранён."),
                    Loc.T("В ГЛАВНОЕ МЕНЮ"), ReturnToMainMenu);
            });
            Item("ВЫЙТИ ИЗ ИГРЫ", "", () => Confirm(Loc.T("ЗАВЕРШИТЬ СЕАНС"), Loc.T("Выйти из игры?"),
                Loc.T("Текущий полёт не сохраняется. Профиль игрока уже сохранён."), Loc.T("ВЫЙТИ НА РАБОЧИЙ СТОЛ"), Quit));
            Select(0);
        }

        void Item(string label, string meta, Action onClick)
        {
            var item = new Button(onClick) { focusable = true };
            item.AddToClassList("menu-item");
            item.Add(Text(label, "menu-item__label"));
            if (!string.IsNullOrEmpty(meta)) item.Add(Text(meta, "menu-item__meta", "t-mono"));
            int i = items.Count;
            item.RegisterCallback<FocusInEvent>(_ => Select(i));
            item.RegisterCallback<PointerEnterEvent>(_ => Select(i));
            items.Add(item);
            list.Add(item);
        }

        void Select(int index)
        {
            for (int i = 0; i < items.Count; i++) items[i].EnableInClassList("menu-item--selected", i == index);
        }

        void RefreshPause()
        {
            if (pauseScreen == null) return;
            PauseSummary s = flight?.PauseSummary();
            eyebrow.text = Loc.T("ПОЛЁТ НА ПАУЗЕ") + (s != null && s.Clock.Length > 0 ? "  ·  " + s.Clock : "");
            subtitle.text = s != null ? s.Subtitle : "";
            BuildItems(s);
            statusLabel.text = status;
            DateTime saved = DateTime.MinValue;
            if (flight?.Profile != null)
                DateTime.TryParse(flight.Profile.lastSavedUtc, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out saved);
            footLabel.text = Loc.T("ВРЕМЯ ИГРЫ ОСТАНОВЛЕНО") + "  ·  " + Loc.T("АВТОСОХРАНЕНИЕ") + " " +
                             (saved == DateTime.MinValue ? "—" : SaveSlots.Ago(saved.ToLocalTime()).ToUpperInvariant());

            panelBox.Clear();
            if (s == null)
            {
                panelBox.style.display = DisplayStyle.None;
                return;
            }
            panelBox.style.display = DisplayStyle.Flex;
            panelBox.Add(Text(s.Eyebrow, "eyebrow", "t-mono"));
            Add(panelBox, Text(s.Title, "panel__title", "t-strong")).style.marginTop = 6;
            var stats = Add(panelBox, El("pause-stats"));
            Stat(stats, "ВЫСОТА", s.Altitude, s.AltitudeUnit);
            Stat(stats, "СКОРОСТЬ", s.Velocity, s.VelocityUnit);
            foreach (KeyValuePair<string, string> r in s.Rows)
            {
                var row = Add(panelBox, El("hz-kv"));
                row.Add(Text(r.Key, "hz-kv__k"));
                row.Add(Text(r.Value, "hz-kv__v", "t-mono"));
            }
            Add(panelBox, Text("ЦЕЛИ ПРОГРАММЫ", "crew-section", "t-mono")).style.marginTop = 22;
            foreach (KeyValuePair<string, bool> g in s.Goals)
            {
                var goal = Add(panelBox, El("pause-goal", g.Value ? "pause-goal--done" : null));
                goal.Add(El("pause-goal__box", g.Value ? "pause-goal__box--on" : null));
                goal.Add(Text(g.Key, "pause-goal__text"));
            }
            ApplyFonts(pauseScreen);
        }

        static void Stat(VisualElement parent, string label, string value, string unit)
        {
            var s = Add(parent, El("pause-stat"));
            s.Add(Text(label, "eyebrow", "eyebrow--muted", "t-mono"));
            var line = Add(s, El("row"));
            line.style.alignItems = Align.FlexEnd;
            line.Add(Text(value, "stat-big", "t-mono"));
            line.Add(Text(unit, "stat-unit", "t-mono"));
        }

        void BuildModal()
        {
            modal = Add(root, El("modal"));
            var box = Add(modal, El("panel", "modal__box"));
            modalEyebrow = Add(box, Text("", "eyebrow", "t-mono"));
            modalTitle = Add(box, Text("", "panel__title", "t-strong"));
            modalTitle.style.fontSize = 34;
            modalText = Add(box, Text("", "panel__text"));
            var buttons = Add(box, El("modal__buttons"));
            buttons.Add(ButtonEl(Loc.T("ОТМЕНА"), CloseModal));
            var confirm = Add(buttons, ButtonEl("", () => modalConfirm?.Invoke(), primary: true));
            modalConfirmLabel = confirm.Q<Label>();
        }

        void Confirm(string eyebrowText, string title, string text, string action, Action onConfirm)
        {
            settings?.CancelCapture();
            modalEyebrow.text = eyebrowText;
            modalTitle.text = title;
            modalText.text = text;
            modalConfirmLabel.text = action;
            modalConfirm = () =>
            {
                CloseModal();
                onConfirm();
            };
            modal.AddToClassList("modal--open");
        }

        void CloseModal() => modal?.RemoveFromClassList("modal--open");

        // ─────────────────────────────── save game ───────────────────────────────

        /// <summary>«Сохранить игру»: a manual save slot of the profile with a picture of the 3D view (taken without the menu).</summary>
        public void SaveGame()
        {
            if (flight == null || flight.Profile == null)
            {
                SetStatus(Loc.T("Нечего сохранять: профиль не загружен."));
                return;
            }
            if (isActiveAndEnabled) StartCoroutine(CaptureFrame(WriteSave));
            else WriteSave(null);
        }

        void WriteSave(Texture2D picture)
        {
            PauseSummary s = flight.PauseSummary();
            string name = (s != null && s.Title.Length > 0 ? s.Title : Loc.T("Полёт")) + (s != null && s.BodyName.Length > 0 ? " · " + s.BodyName : "");
            try
            {
                SaveSlotMeta meta = SaveSlots.Save(flight.Profile, name, s != null ? s.BodyName : "", picture);
                SetStatus(Loc.T("Сохранено: ") + meta.name + "  ·  " + Loc.T("в главном меню: «Продолжить»."));
            }
            catch (Exception e)
            {
                SetStatus(Loc.T("Не удалось сохранить: ") + e.Message);
            }
        }

        void SetStatus(string text)
        {
            status = text;
            if (statusLabel != null) statusLabel.text = text;
        }

        /// <summary>Grabs the frame with this overlay hidden, scaled to 640 × 360; hands it to <paramref name="done"/> if given.</summary>
        IEnumerator CaptureFrame(Action<Texture2D> done)
        {
            root.style.opacity = 0f;
            yield return new WaitForEndOfFrame();
            Texture2D full = null;
            try
            {
                full = ScreenCapture.CaptureScreenshotAsTexture();
                if (full != null)
                {
                    if (lastFrame != null) Destroy(lastFrame);
                    lastFrame = Scaled(full, 640, 360);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SpaceSim] Screenshot failed: " + e.Message);
            }
            finally
            {
                if (full != null) Destroy(full);
                root.style.opacity = StyleKeyword.Null;
            }
            done?.Invoke(lastFrame);
        }

        static Texture2D Scaled(Texture2D source, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 0);
            Graphics.Blit(source, rt);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var result = new Texture2D(width, height, TextureFormat.RGB24, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        /// <summary>Leaves the flight for the main menu scene (the profile is already saved).</summary>
        public void ReturnToMainMenu()
        {
            Close();
            FlightInput.ModalWindowOpen = false;
            SceneManager.LoadScene(MainMenuScene);
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>The language changed: rebuilds this screen in the new language and stays where it was.</summary>
        public void RebuildForLanguage()
        {
            bool wasSettings = inSettings;
            document.rootVisualElement.schedule.Execute(() =>
            {
                Build(document.rootVisualElement);
                root.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                if (open) root.AddToClassList("game-menu--open");
                if (open && wasSettings)
                {
                    inSettings = true;
                    pauseScreen.style.display = DisplayStyle.None;
                    settingsScreen.style.display = DisplayStyle.Flex;
                    settings.ShowPage(SettingsView.Page.Language);
                }
                else if (open) ShowPause();
            });
        }
    }
}
