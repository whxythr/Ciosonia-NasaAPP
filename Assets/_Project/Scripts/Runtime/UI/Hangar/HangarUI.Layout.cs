using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using System.Globalization;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    // Frame of the hangar screen: where every panel goes, the top bar (project name and actions), the summary line, the tool strip
    // under the 3D view, the exit confirmation and the projects dialog. Everything is placed in scaled GUI units (HudStyle), so the
    // same layout fits 1280×720 (1707×960 units) up to 2560×1440 (1920×1080 units) without overlaps.
    public sealed partial class HangarUI
    {
        const float Margin = 12f;
        const float Gap = 8f;
        const float TopBarHeight = 44f;
        const float RailWidth = 50f;
        const float PanelWidth = 340f;
        const float CardWidth = 300f;
        const float StackWidth = 236f;
        const float DetailsWidth = 440f;
        const float CollapsedHeight = 40f;
        const float SummaryHeight = 76f;
        const float ToolHeight = 42f;

        Rect topRect, railRect, panelRect, stackRect, detailsRect, summaryRect, toolRect, confirmRect, dialogRect, nameRect, searchRect;
        bool projectsOpen;
        string deleteArmed;
        Vector2 projectsScroll;
        GUIStyle barTitle, primarySmall, middleSmall, middleWhite;

        void OnGUI()
        {
            if (FlightInput.PauseMenuOpen) return;
            HudStyle.Begin();
            GUI.depth = -10; // in front of the HUD
            EnsureStyles();
            Vector2 screen = HudStyle.VirtualScreen;

            if (open && design != null && (facility == null || facility.Inside))
            {
                Event e = Event.current;
                if (compiled == null || report == null || (e != null && e.type == EventType.Layout)) Refresh();
                if (compiled != null && report != null) DrawHangar(screen);
                else if (facility != null) facility.PointerOverUi = true;
            }
            else if (facility != null)
            {
                facility.PointerOverUi = true;
                facility.HighlightStage = null;
            }
            DrawFade(screen);
            if (!Busy) ApplyPending();
        }

        void DrawFade(Vector2 screen)
        {
            if (fade <= 0f || !Repaint) return;
            Color c = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, Mathf.SmoothStep(0f, 1f, fade));
            GUI.DrawTexture(new Rect(0f, 0f, screen.x, screen.y), Texture2D.whiteTexture);
            GUI.color = c;
        }

        void EnsureLayoutStyles()
        {
            if (barTitle != null && primarySmall != null && middleWhite != null) return;
            barTitle = new GUIStyle(HudStyle.Title) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
            primarySmall = new GUIStyle(HudStyle.Primary) { fontSize = 15 };
            middleSmall = new GUIStyle(HudStyle.Small) { alignment = TextAnchor.MiddleLeft, wordWrap = true };
            middleWhite = new GUIStyle(middleSmall) { fontSize = 12 };
            middleWhite.normal.textColor = Color.white;
        }

        void PlacePanels(Vector2 screen)
        {
            topRect = new Rect(Margin, Margin, screen.x - 2f * Margin, TopBarHeight);
            float top = topRect.yMax + Gap, bottom = screen.y - Margin, h = bottom - top;
            railRect = new Rect(Margin, top, RailWidth, h);
            panelRect = new Rect(railRect.xMax + 6f, top, PanelWidth, h);
            stackRect = new Rect(screen.x - Margin - StackWidth, top, StackWidth, h);
            detailsRect = new Rect(stackRect.x - Gap - DetailsWidth, top, DetailsWidth, detailsCollapsed ? CollapsedHeight : h);
            float cx = panelRect.xMax + Gap, cw = Mathf.Max(200f, detailsRect.x - Gap - cx);
            summaryRect = new Rect(cx, top, cw, SummaryHeight);
            toolRect = new Rect(cx, bottom - ToolHeight, cw, ToolHeight);
        }

        void DrawHangar(Vector2 screen)
        {
            EnsureLayoutStyles();
            PlacePanels(screen);
            Event e = Event.current;
            Vector2 mouse = e != null ? e.mousePosition : Vector2.zero;

            bool shopOpen = shop != null && shop.IsOpen;
            bool modal = confirmExit || projectsOpen;
            bool interactive = !shopOpen && !Busy && !modal;
            if (!interactive) CancelDrag();
            HandleDragEvents(e, mouse, interactive);
            // A click anywhere outside the text fields gives the keyboard back to the camera keys.
            if (e != null && e.type == EventType.MouseDown && GUIUtility.keyboardControl != 0 && !nameRect.Contains(mouse) && !searchRect.Contains(mouse))
                GUIUtility.keyboardControl = 0;

            bool previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && interactive; // the shop window / a dialog is on top and owns the input; no clicks while fading

            DrawTopBar();
            DrawRail(mouse, interactive);
            DrawPanel(mouse, interactive);
            DrawStageStack(mouse, interactive);
            DrawDetails();
            DrawSummary();
            DrawToolStrip();

            GUI.enabled = previousEnabled && !shopOpen && !Busy;
            if (confirmExit) DrawConfirm(screen);
            else if (projectsOpen) DrawProjects(screen);
            GUI.enabled = previousEnabled;

            bool cardShown = false;
            if (interactive && !dragging)
            {
                cardShown = DrawCard(screen);
                DrawRailTooltip();
            }

            bool overUi = shopOpen || modal || topRect.Contains(mouse) || railRect.Contains(mouse) || panelRect.Contains(mouse) ||
                          stackRect.Contains(mouse) || detailsRect.Contains(mouse) || summaryRect.Contains(mouse) || toolRect.Contains(mouse) ||
                          (cardShown && cardRect.Contains(mouse));
            if (facility != null)
            {
                facility.PointerOverUi = overUi;
                facility.HighlightStage = dragging ? DropStageHighlight(mouse) : (int?)null;
            }
            if (!overUi && !dragging) DrawTooltip(mouse, screen);
            if (dragging) DrawDragGhost(mouse, screen);
            SwallowMouse(overUi);
        }

        /// <summary>Clicks and scrolling on the panels must not reach anything underneath.</summary>
        static void SwallowMouse(bool overUi)
        {
            Event e = Event.current;
            if (e == null || !overUi) return;
            EventType t = e.type;
            if (t == EventType.MouseDown || t == EventType.MouseUp || t == EventType.MouseDrag || t == EventType.ScrollWheel) e.Use();
        }

        // ---- Top bar: project name and actions ----------------------------------------------------

        void DrawTopBar()
        {
            HudStyle.WindowFrame(topRect, false);
            float x = topRect.x + 12f, y = topRect.y + 7f, h = topRect.height - 14f;
            PlanetRuntime planet = body.Planet;
            string where = planet != null && planet.Definition != null ? " · " + Loc.T(planet.Definition.displayName) : "";
            // The title is as wide as the planet's name needs (it was clipped at a fixed 180 units).
            string title = Loc.T("АНГАР") + where;
            float titleWidth = Mathf.Clamp(barTitle.CalcSize(new GUIContent(title)).x + 4f, 120f, 300f);
            GUI.Label(new Rect(x, y, titleWidth, h), title, barTitle);
            x += titleWidth + 6f;

            nameRect = new Rect(x, y + 1f, 230f, h - 2f);
            GUI.SetNextControlName("hangar-project-name");
            string name = design.name ?? "";
            string edited = GUI.TextField(nameRect, name, HudStyle.Field);
            if (edited != name && CrewEditAllowed())
            {
                design.name = edited.Length > HangarProjectStore.MaxNameLength ? edited.Substring(0, HangarProjectStore.MaxNameLength) : edited;
                modified = true;
            }
            x += 238f;

            if (Button(new Rect(x, y, 84f, h), Loc.T("Новый"), true)) Defer(NewProject);
            x += 90f;
            if (Button(new Rect(x, y, 96f, h), Loc.T("Открыть"), true))
                Defer(() =>
                {
                    projectsOpen = true;
                    deleteArmed = null;
                });
            x += 102f;
            if (Button(new Rect(x, y, 104f, h), Loc.T("Сохранить"), !ProjectsReadOnly)) Defer(SaveProject);
            x += 110f;

            float rx = topRect.xMax - 12f;
            rx -= 104f;
            if (Button(new Rect(rx, y, 104f, h), Loc.T("Выйти"), true)) Defer(Toggle);
            rx -= Gap + 240f;
            bool onPad = body.IsOnLaunchPad;
            if (Button(new Rect(rx, y, 240f, h), Loc.T("ВЫВЕЗТИ НА СТАРТ") + "  →", onPad, primarySmall)) Defer(() => Leave(true));
            if (shop != null)
            {
                rx -= Gap + 130f;
                if (Button(new Rect(rx, y, 130f, h), Loc.T("МАГАЗИН (") + FlightInput.KeyLabel(InputKey.K) + ")", true)) Defer(() => shop.Toggle());
            }

            float mw = rx - x - 8f;
            if (mw > 60f)
            {
                var r = new Rect(x, y, mw, h);
                if (!onPad) Colored(r, Loc.T("Перестройка возможна только на стартовой площадке"), middleSmall, HudStyle.Danger);
                else if (modified) Colored(r, Loc.T("есть неприменённые изменения"), middleSmall, HudStyle.Warning);
                else GUI.Label(r, Loc.T("изменений нет"), middleSmall);
            }
        }

        // ---- Centre: live summary and camera keys, tool strip under the view ------------------------------

        void DrawSummary()
        {
            HudStyle.WindowFrame(summaryRect, false);
            float x = summaryRect.x + 14f, w = summaryRect.width - 28f;
            double height = facility != null && facility.Preview != null ? facility.Preview.Layout.Height : 0.0;
            ProjectPrice(out long total, out _);
            var stats = new[]
            {
                new KeyValuePair<string, string>(Loc.T("МАССА"), Mass(report.LaunchMass)),
                new KeyValuePair<string, string>(Loc.T("СТУПЕНЕЙ"), design.stages.Count.ToString()),
                new KeyValuePair<string, string>(Loc.T("СТОИМОСТЬ"), Credits(total)),
                new KeyValuePair<string, string>(Loc.T("ВЫСОТА"), N(height, 1) + Loc.T(" м")),
                new KeyValuePair<string, string>("TWR", N(report.LiftoffTwr, 2)),
            };
            float sx = x;
            foreach (KeyValuePair<string, string> s in stats)
            {
                GUIStyle vs = HudStyle.Style(HudStyle.Face.Mono, 17, s.Key == "TWR" && !report.CanLiftOff ? HudStyle.Danger : HudStyle.TextStrong);
                float vw = Mathf.Max(70f, vs.CalcSize(new GUIContent(s.Value)).x + 26f);
                if (sx + vw > summaryRect.xMax - 8f) break;
                GUI.Label(new Rect(sx, summaryRect.y + 8f, vw, 14f), s.Key, HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted));
                GUI.Label(new Rect(sx, summaryRect.y + 22f, vw, 24f), s.Value, vs);
                sx += vw;
            }
            GUI.Label(new Rect(x, summaryRect.y + 52f, w, 18f),
                HudStyle.KeyText(Loc.T("ПКМ")) + Loc.T(" — обзор · ") + HudStyle.KeyText(Loc.T("колесо")) + Loc.T(" — зум · ") + HudStyle.Key(InputKey.W) + "/" +
                HudStyle.Key(InputKey.S) + Loc.T(" — выше/ниже · ") + HudStyle.Key(InputKey.A) + "/" + HudStyle.Key(InputKey.D) +
                Loc.T(" — вращать · ") + HudStyle.Key(InputKey.F) + Loc.T(" — вся ракета · ") + HudStyle.KeyText(Loc.T("клик")) + Loc.T(" — выбрать деталь"),
                HudStyle.HintRichLeft);
        }

        void DrawToolStrip()
        {
            HudStyle.WindowFrame(toolRect, false);
            float x = toolRect.x + 8f, y = toolRect.y + 7f, h = toolRect.height - 14f;
            if (facility != null)
            {
                bool fairing = facility.ShowFairing;
                if (Button(new Rect(x, y, 150f, h), fairing ? Loc.T("Обтекатель: виден") : Loc.T("Обтекатель: снят"), true))
                    Defer(() => facility.ShowFairing = !fairing);
                x += 156f;
                if (Button(new Rect(x, y, 130f, h), Loc.T("Вся ракета (") + FlightInput.KeyLabel(InputKey.F) + ")", true)) Defer(() => facility.FrameAll(false));
                x += 138f;
            }
            float w = toolRect.xMax - 8f - x;
            if (w < 40f) return;
            var r = new Rect(x, toolRect.y + 2f, w, toolRect.height - 4f);
            if (!string.IsNullOrEmpty(status)) Colored(r, status, middleWhite, statusColor);
            else GUI.Label(r, Loc.T("Клик по детали каталога ставит её на выбранную ступень; деталь можно и перетащить на ракету или на ступень справа."),
                middleSmall);
        }

        void DrawTooltip(Vector2 mouse, Vector2 screen)
        {
            RocketModel.Part part = facility != null ? facility.Hovered : null;
            if (part == null) return;
            PartPlacement p = part.Placement;
            ModuleDefinition m = ModuleCatalog.Find(p.ModuleId);
            if (m == null) return;
            string text = "<b>" + m.name + "</b>\n" + Where(p) + " · " + Mass(m.dryMass) + (part.FromLibrary ? "" : Loc.T("\n<color=#8A97AB>модель-заготовка</color>")) +
                          Loc.T("\nклик — выбрать");
            const float w = 260f, h = 70f;
            var r = new Rect(Mathf.Min(mouse.x + 18f, screen.x - w - 4f), Mathf.Min(mouse.y + 18f, screen.y - h - 4f), w, h);
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            GUI.Label(new Rect(r.x + 8f, r.y + 5f, w - 16f, h - 8f), text, tooltip);
        }

        string Where(PartPlacement p)
        {
            switch (p.Role)
            {
                case PartRole.Engine:
                {
                    int n = design != null && ValidStage(p.Stage) ? Math.Max(1, Math.Min(BlueprintCompiler.MaxEnginesPerStage, design.stages[p.Stage].engineCount)) : 1;
                    return Loc.T("ступень ") + (p.Stage + 1) + Loc.T(" · двигатель ") + (p.Index + 1) + Loc.T(" из ") + n;
                }
                case PartRole.Tank: return Loc.T("ступень ") + (p.Stage + 1) + Loc.T(" · бак ") + (p.Index + 1);
                case PartRole.Wrap: return Loc.T("головной отсек · защитный слой вокруг зонда");
                case PartRole.Instrument: return Loc.T("головной отсек · на корпусе зонда");
                case PartRole.ProbeBus: return Loc.T("головной отсек · ядро зонда");
                default: return p.Stage >= 0 ? Loc.T("ступень ") + (p.Stage + 1) : Loc.T("головной отсек");
            }
        }

        // ---- Dialogs --------------------------------------------------------------------------------

        void DrawConfirm(Vector2 screen)
        {
            const float w = 560f, h = 128f;
            confirmRect = new Rect((screen.x - w) * 0.5f, Mathf.Max(topRect.yMax + 70f, screen.y * 0.3f), w, h);
            HudStyle.DialogFrame(confirmRect);
            GUILayout.BeginArea(new Rect(confirmRect.x + 14f, confirmRect.y + 10f, w - 28f, h - 18f));
            GUILayout.Label(Loc.T("Выйти из ангара?"), HudStyle.Title);
            GUILayout.Label(Loc.T("В проекте есть неприменённые изменения. Вывезти новую ракету на старт или вернуться к прежней?"), HudStyle.Small);
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (Button(Loc.T("Вывезти на старт"), body.IsOnLaunchPad, GUILayout.Width(170), GUILayout.Height(34))) Defer(() => Leave(true));
            if (Button(Loc.T("Выйти без изменений"), true, GUILayout.Width(190), GUILayout.Height(34))) Defer(() => Leave(false));
            if (Button(Loc.T("Остаться"), true, GUILayout.Width(120), GUILayout.Height(34))) Defer(() => confirmExit = false);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        void DrawProjects(Vector2 screen)
        {
            float w = 600f, h = Mathf.Min(480f, screen.y - topRect.yMax - 80f);
            dialogRect = new Rect((screen.x - w) * 0.5f, topRect.yMax + 40f, w, h);
            HudStyle.DialogFrame(dialogRect);
            GUILayout.BeginArea(new Rect(dialogRect.x + 14f, dialogRect.y + 10f, w - 28f, h - 20f));
            GUILayout.Label(Loc.T("Проекты ангара"), HudStyle.WindowTitle);
            GUILayout.Label(Loc.T("Проекты хранятся рядом с профилем игрока. Открытый проект заменяет текущий в ангаре; на площадку он попадёт после «Вывезти на старт»."),
                HudStyle.Small);
            if (modified) Colored(Loc.T("В текущем проекте есть неприменённые изменения — сохраните его, если он ещё нужен."), note, HudStyle.Warning);
            if (ProjectsReadOnly) Colored(Loc.T("Профиль только для чтения: сохранять и удалять проекты нельзя."), note, HudStyle.Danger);
            string problem = HangarProjectStore.Problem;
            if (!string.IsNullOrEmpty(problem)) Colored(problem, note, HudStyle.Danger);
            GUILayout.Space(4);

            projectsScroll = GUILayout.BeginScrollView(projectsScroll, GUILayout.Height(Mathf.Max(80f, h - 190f)));
            GUILayout.BeginHorizontal(HudStyle.Panel);
            GUILayout.BeginVertical();
            GUILayout.Label(Loc.T("Стандартная ракета"), HudStyle.Title);
            GUILayout.Label(Loc.T("Образец: 2 ступени, зонд. Можно взять за основу."), HudStyle.Small);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (Button(Loc.T("Открыть"), true, GUILayout.Width(96), GUILayout.Height(28)))
                Defer(() =>
                {
                    ResetToDefault();
                    projectsOpen = false;
                });
            GUILayout.EndHorizontal();

            var projects = HangarProjectStore.All;
            if (projects.Count == 0) GUILayout.Label(Loc.T("Сохранённых проектов пока нет — «Сохранить» в верхней полосе сохраняет текущий под его именем."), HudStyle.Small);
            foreach (HangarProject p in projects)
            {
                string name = p.name;
                GUILayout.BeginHorizontal(HudStyle.Panel);
                GUILayout.BeginVertical();
                GUILayout.Label(name, HudStyle.Title);
                int stages = p.blueprint != null && p.blueprint.stages != null ? p.blueprint.stages.Count : 0;
                GUILayout.Label(Loc.T("ступеней: ") + stages + " · " + SavedDate(p.savedUtc), HudStyle.Small);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                if (Button(Loc.T("Открыть"), true, GUILayout.Width(96), GUILayout.Height(28))) Defer(() => OpenProject(name));
                bool armed = deleteArmed == name;
                if (Button(armed ? Loc.T("Точно удалить?") : Loc.T("Удалить"), !ProjectsReadOnly, GUILayout.Width(armed ? 130 : 96), GUILayout.Height(28)))
                    Defer(() =>
                    {
                        if (armed) DeleteProject(name);
                        else deleteArmed = name;
                    });
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            GUILayout.BeginHorizontal();
            if (Button(Loc.T("Сохранить текущий"), !ProjectsReadOnly, GUILayout.Width(180), GUILayout.Height(30))) Defer(SaveProject);
            GUILayout.FlexibleSpace();
            if (Button(Loc.T("Закрыть"), true, GUILayout.Width(120), GUILayout.Height(30))) Defer(() => projectsOpen = false);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        static string SavedDate(string utc)
        {
            if (DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime t))
                return t.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
            return "—";
        }
    }
}
