using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    // The stage stack on the right, in the order the stages work: the head section on top, then the stages from the upper one down to
    // stage 1, which burns first. Each card shows its number, its ideal Δv and its hardware; a click selects the stage (catalog clicks
    // install there), × removes it, a dragged part dropped on a card is installed on that stage. The total Δv is at the bottom.
    public sealed partial class HangarUI
    {
        const float StageCardHeight = 76f;
        const float StackIcon = 22f;

        Vector2 stackScroll;
        GUIStyle tinyButton;

        static Rect Clip(Rect a, Rect b)
        {
            float x0 = Mathf.Max(a.x, b.x), y0 = Mathf.Max(a.y, b.y), x1 = Mathf.Min(a.xMax, b.xMax), y1 = Mathf.Min(a.yMax, b.yMax);
            return x1 > x0 && y1 > y0 ? new Rect(x0, y0, x1 - x0, y1 - y0) : new Rect(0f, 0f, 0f, 0f);
        }

        int IssuesOn(int stageIndex)
        {
            int n = 0;
            foreach (BlueprintIssue i in compiled.Issues) if (i.StageIndex == stageIndex) n++;
            return n;
        }

        float PayloadCardHeight(float width)
        {
            int perRow = Mathf.Max(1, (int)Math.Floor((width - 16f) / (StackIcon + 3f)));
            int rows = Mathf.Max(1, (design.payloadModules.Count + perRow - 1) / perRow);
            return 30f + rows * (StackIcon + 3f) + 8f;
        }

        void DrawStageStack(Vector2 mouse, bool interactive)
        {
            if (tinyButton == null)
            {
                tinyButton = new GUIStyle(HudStyle.Button) { padding = new RectOffset(0, 0, 0, 0), fontSize = 13 };
            }
            int? highlight = dragging ? DropStageHighlight(mouse) : null; // uses the drop rects of the previous pass
            stackTargets.Clear();

            HudStyle.WindowFrame(stackRect, false);
            float x = stackRect.x + 10f, w = stackRect.width - 20f, y = stackRect.y + 8f;
            GUI.Label(new Rect(x, y, w, 20f), Loc.T("СТУПЕНИ"), barTitle);
            y += 20f;
            GUI.Label(new Rect(x, y, w, 16f), Loc.T("снизу вверх: ступень 1 работает первой"), compact);
            y += 20f;
            if (Button(new Rect(x, y, w, 26f), Loc.T("+ Ступень сверху"), true)) Defer(AddStageOnTop);
            y += 32f;

            const float footer = 88f;
            var view = new Rect(x, y, w, Mathf.Max(StageCardHeight, stackRect.yMax - 8f - footer - 4f - y));
            float cw = w - 12f;
            float payloadHeight = PayloadCardHeight(cw);
            float contentHeight = payloadHeight + 6f + design.stages.Count * (StageCardHeight + 6f);
            bool inView = interactive && view.Contains(mouse);
            stackScroll = GUI.BeginScrollView(view, stackScroll, new Rect(0f, 0f, cw, contentHeight));
            Event e = Event.current;
            Vector2 local = e != null ? e.mousePosition : Vector2.zero;

            float cy = 0f;
            var pr = new Rect(0f, cy, cw, payloadHeight);
            DrawPayloadCard(pr, highlight == -1);
            AddTarget(pr, view, -1);
            if (inView && e != null && e.type == EventType.MouseDown && e.button == 0 && pr.Contains(local))
            {
                Defer(() => facility?.FocusOn(q => q.Stage == -1 && q.IsModule, false));
                e.Use();
            }
            cy += payloadHeight + 6f;

            if (design.stages.Count == 0)
                Colored(new Rect(0f, cy, cw, 40f), Loc.T("Ступеней нет — ракете нечем взлетать."), note, HudStyle.Danger);
            for (int i = design.stages.Count - 1; i >= 0; i--)
            {
                var r = new Rect(0f, cy, cw, StageCardHeight);
                DrawStageCard(r, i, highlight == i);
                AddTarget(r, view, i);
                if (inView && e != null && e.type == EventType.MouseDown && e.button == 0 && r.Contains(local))
                {
                    int index = i;
                    Defer(() =>
                    {
                        selectedStage = index;
                        facility?.FocusOn(q => q.Stage == index && (q.Role == PartRole.Tank || q.Role == PartRole.Engine || q.Role == PartRole.StageSpacer), false);
                    });
                    e.Use();
                }
                cy += StageCardHeight + 6f;
            }
            GUI.EndScrollView();

            var f = new Rect(x, stackRect.yMax - 8f - footer, w, footer);
            HudStyle.HLine(f.x, f.y, f.width, new Color(1f, 1f, 1f, 0.12f));
            GUI.Label(new Rect(f.x, f.y + 6f, f.width, 14f), Loc.T("ВСЕГО Δv · ВАКУУМ"), HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted));
            GUI.Label(new Rect(f.x - 2f, f.y + 18f, f.width, 48f), N(report.TotalDeltaVVacuum, 0), HudStyle.Style(HudStyle.Face.DisplayStrong, 44, HudStyle.TextStrong));
            GUI.Label(new Rect(f.x, f.y + 68f, f.width, 14f), Loc.T("М/С") + "  ·  TWR " + N(report.LiftoffTwr, 2) + (report.CanLiftOff ? "" : "  < 1"),
                HudStyle.Style(HudStyle.Face.Mono, 10, report.CanLiftOff ? HudStyle.TextMuted : HudStyle.Danger));
        }

        void AddTarget(Rect content, Rect view, int stage)
        {
            var abs = new Rect(view.x + content.x - stackScroll.x, view.y + content.y - stackScroll.y, content.width, content.height);
            Rect visible = Clip(abs, view);
            if (visible.width > 0f) stackTargets.Add(new KeyValuePair<Rect, int>(visible, stage));
        }

        void CardBackground(Rect r, bool selected, bool dropHere)
        {
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            if (selected)
            {
                Fill(new Rect(r.x + 1f, r.y + 6f, 3f, r.height - 12f), HudStyle.Blue);
                Frame(r, new Color(HudStyle.Cyan.r, HudStyle.Cyan.g, HudStyle.Cyan.b, 0.5f));
            }
            if (dropHere)
            {
                Fill(r, new Color(HudStyle.Cyan.r, HudStyle.Cyan.g, HudStyle.Cyan.b, 0.12f));
                Frame(r, HudStyle.Cyan, 2f);
            }
        }

        void DrawPayloadCard(Rect r, bool dropHere)
        {
            CardBackground(r, false, dropHere);
            GUI.Label(new Rect(r.x + 10f, r.y + 5f, r.width - 60f, 18f), Loc.T("Головной отсек"), barTitle);
            int issues = IssuesOn(-1);
            if (issues > 0) Colored(new Rect(r.xMax - 40f, r.y + 5f, 32f, 18f), "! " + issues, compactWhite, HudStyle.Warning);
            GUI.Label(new Rect(r.x + 10f, r.y + 5f, r.width - 50f, 18f), Mass(compiled.PayloadMass), countRight);
            float ix = r.x + 8f, iy = r.y + 28f;
            if (design.payloadModules.Count == 0)
                GUI.Label(new Rect(ix, iy, r.width - 16f, StackIcon), Loc.T("пусто — будет поставлен зонд"), compact);
            foreach (string id in design.payloadModules)
            {
                if (ix + StackIcon > r.xMax - 6f)
                {
                    ix = r.x + 8f;
                    iy += StackIcon + 3f;
                }
                PartIcon(new Rect(ix, iy, StackIcon, StackIcon), ModuleCatalog.Find(id));
                ix += StackIcon + 3f;
            }
        }

        void DrawStageCard(Rect r, int i, bool dropHere)
        {
            StageBlueprint s = design.stages[i];
            StageReport sr = i < report.Stages.Count ? report.Stages[i] : null;
            CardBackground(r, i == selectedStage, dropHere);

            bool sel = i == selectedStage;
            GUI.Label(new Rect(r.x + 10f, r.y + 2f, 28f, 30f), (i + 1).ToString(), HudStyle.Style(HudStyle.Face.DisplayStrong, 24, sel ? HudStyle.Blue : HudStyle.TextStrong));
            string title = i == 0 ? Loc.T("СТАРТ · РАБОТАЕТ ПЕРВОЙ") : Loc.T("СТУПЕНЬ");
            GUI.Label(new Rect(r.x + 36f, r.y + 6f, r.width - 100f, 14f), title, HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted));
            int issues = IssuesOn(i);
            if (issues > 0) Colored(new Rect(r.xMax - 64f, r.y + 5f, 32f, 18f), "! " + issues, compactWhite, HudStyle.Warning);
            int index = i;
            if (Button(new Rect(r.xMax - 26f, r.y + 5f, 20f, 20f), "×", true, tinyButton)) Defer(() => RemoveStage(index));

            string line = sr != null ? "Δv " + Speed(sr.DeltaVVacuum) + (sr.VacuumThrust > 0.0 ? " · TWR " + N(sr.IgnitionTwr, 2) : "") : "Δv —";
            GUI.Label(new Rect(r.x + 36f, r.y + 22f, r.width - 46f, 16f), line, HudStyle.Style(HudStyle.Face.Mono, 12, HudStyle.TextStrong));

            float ix = r.x + 8f, iy = r.y + 46f, right = r.xMax - 6f;
            ModuleDefinition engine = string.IsNullOrEmpty(s.engineId) ? null : ModuleCatalog.Find(s.engineId);
            if (engine == null)
            {
                Colored(new Rect(ix, iy + 2f, 96f, 18f), Loc.T("нет двигателя"), compactWhite, HudStyle.Warning);
                ix += 98f;
            }
            else
            {
                PartIcon(new Rect(ix, iy, StackIcon, StackIcon), engine);
                ix += StackIcon + 2f;
                GUI.Label(new Rect(ix, iy + 2f, 24f, 18f), "×" + s.engineCount, compact);
                ix += 24f;
                if (Button(new Rect(ix, iy + 2f, 18f, 18f), "−", s.engineCount > 1, tinyButton)) Defer(() => ChangeEngineCount(index, -1));
                ix += 20f;
                if (Button(new Rect(ix, iy + 2f, 18f, 18f), "+", s.engineCount < BlueprintCompiler.MaxEnginesPerStage, tinyButton))
                    Defer(() => ChangeEngineCount(index, +1));
                ix += 24f;
            }

            var parts = new List<string>();
            foreach (TankLoad t in s.tanks) parts.Add(t.moduleId);
            if (s.attachments != null) parts.AddRange(s.attachments);
            int fit = Mathf.Max(0, (int)Math.Floor((right - ix) / (StackIcon + 2f)));
            for (int k = 0; k < parts.Count; k++)
            {
                if (k == fit - 1 && parts.Count > fit)
                {
                    GUI.Label(new Rect(ix, iy + 2f, StackIcon + 6f, 18f), "+" + (parts.Count - k), compact);
                    break;
                }
                if (k >= fit) break;
                PartIcon(new Rect(ix, iy, StackIcon, StackIcon), ModuleCatalog.Find(parts[k]));
                ix += StackIcon + 2f;
            }
        }

        /// <summary>A small picture of an installed part: its preview, or its category icon.</summary>
        void PartIcon(Rect r, ModuleDefinition m)
        {
            if (m == null)
            {
                GUI.Label(r, "?", centeredSmall);
                return;
            }
            Thumbnail(r, m, r.height, IsLocked(m) ? HudStyle.Danger : HudStyle.TextSoft);
        }
    }
}
