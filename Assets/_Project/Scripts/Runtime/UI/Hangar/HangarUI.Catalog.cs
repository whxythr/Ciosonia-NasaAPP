using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    // The parts catalog: the category rail on the left, the panel with search, sorting, sub-tabs and the grid of part tiles, the
    // selection on the rocket and the project's sums under it. A click on a tile installs the part; a tile dragged onto the rocket
    // or onto a stage of the stack installs it where it is dropped.
    public sealed partial class HangarUI
    {
        enum SortKey { Catalog, Name, Mass, Price, Size }

        static readonly SortKey[] SortKeys = { SortKey.Name, SortKey.Mass, SortKey.Price, SortKey.Size };
        static readonly string[] SortNames = { "Имя", "Масса", "Цена", "Размер" };

        const float TileHeight = 112f;
        const float TileGap = 6f;
        const float DragThreshold = 6f;

        ModuleCategory category = ModuleCategory.Engine;
        ModuleSubgroup subgroup = ModuleSubgroup.None;
        string search = "";
        SortKey sortKey = SortKey.Catalog;
        bool sortDescending;
        /// <summary>The tile last picked or installed (also follows a part clicked on the rocket).</summary>
        string selectedModule = "";

        List<ModuleDefinition> shown;
        string shownKey;
        Vector2 gridScroll;

        string hoveredModule;
        Rect hoveredTile;
        int railHover = -1;
        Rect railHoverRect;

        // Drag and drop of a tile: pressed on a tile, becomes a drag after a few pixels, installs on release.
        string pressId;
        Vector2 pressPos;
        bool dragging;
        readonly List<KeyValuePair<Rect, int>> stackTargets = new List<KeyValuePair<Rect, int>>();

        GUIStyle placeholder, countRight;

        bool Searching => !string.IsNullOrEmpty(search) && search.Trim().Length > 0;

        void SelectCategory(ModuleCategory c)
        {
            category = c;
            subgroup = ModuleSubgroup.None;
            gridScroll = Vector2.zero;
        }

        /// <summary>The tiles in the panel: the category (and sub-tab), or the whole catalog while searching; sorted as chosen.</summary>
        List<ModuleDefinition> ShownModules()
        {
            string key = category + "|" + subgroup + "|" + search + "|" + sortKey + "|" + sortDescending + "|" + Loc.Version;
            if (shown != null && key == shownKey) return shown;
            shownKey = key;
            var list = new List<ModuleDefinition>();
            var order = new Dictionary<ModuleDefinition, int>();
            string q = (search ?? "").Trim().ToLowerInvariant();
            IReadOnlyList<ModuleDefinition> all = ModuleCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                ModuleDefinition m = all[i];
                order[m] = i;
                if (q.Length > 0)
                {
                    bool match = (m.name ?? "").ToLowerInvariant().Contains(q) || (m.description ?? "").ToLowerInvariant().Contains(q);
                    if (!match) continue;
                }
                else
                {
                    if (m.category != category) continue;
                    if (subgroup != ModuleSubgroup.None && m.Subgroup != subgroup) continue;
                }
                list.Add(m);
            }
            list.Sort((a, b) =>
            {
                int c = 0;
                switch (sortKey)
                {
                    case SortKey.Name: c = string.Compare(a.name, b.name, StringComparison.CurrentCultureIgnoreCase); break;
                    case SortKey.Mass: c = a.dryMass.CompareTo(b.dryMass); break;
                    case SortKey.Price: c = a.price.CompareTo(b.price); break;
                    case SortKey.Size:
                        c = a.length.CompareTo(b.length);
                        if (c == 0) c = a.diameter.CompareTo(b.diameter);
                        break;
                }
                if (sortDescending) c = -c;
                return c != 0 ? c : order[a].CompareTo(order[b]); // stable: catalog order breaks ties
            });
            shown = list;
            return list;
        }

        // ---- Category rail ----------------------------------------------------------------------

        void DrawRail(Vector2 mouse, bool interactive)
        {
            HudStyle.WindowFrame(railRect, false);
            railHover = -1;
            const float s = 40f;
            float x = railRect.x + (railRect.width - s) * 0.5f, y = railRect.y + 8f;
            for (int i = 0; i < Categories.Length; i++)
            {
                ModuleCategory c = Categories[i];
                var r = new Rect(x, y + i * (s + 6f), s, s);
                if (r.yMax > railRect.yMax - 4f) break;
                bool on = !Searching && category == c;
                bool now = GUI.Toggle(r, on, "", HudStyle.Button);
                if (now != on && !on)
                    Defer(() =>
                    {
                        search = "";
                        SelectCategory(c);
                    });
                Icon(Inset(r, 7f), HangarIcons.Category(c), on ? Color.white : HudStyle.TextSoft);
                if (on) Fill(new Rect(railRect.x + 1f, r.y + 8f, 3f, r.height - 16f), HudStyle.Blue);
                if (interactive && r.Contains(mouse))
                {
                    railHover = i;
                    railHoverRect = r;
                }
            }
        }

        void DrawRailTooltip()
        {
            if (railHover < 0 || railHover >= Categories.Length) return;
            ModuleCategory c = Categories[railHover];
            int count = ModuleCatalog.InCategory(c).Count;
            var r = new Rect(railHoverRect.xMax + 10f, railHoverRect.y, 250f, 44f);
            HudStyle.WindowFrame(r, false);
            GUI.Label(new Rect(r.x + 10f, r.y + 5f, r.width - 20f, 18f), ModuleCatalog.CategoryName(c), HudStyle.Title);
            GUI.Label(new Rect(r.x + 10f, r.y + 23f, r.width - 20f, 16f), Loc.T("деталей: ") + count, compact);
        }

        // ---- Panel ------------------------------------------------------------------------------

        void DrawPanel(Vector2 mouse, bool interactive)
        {
            if (placeholder == null || countRight == null)
            {
                placeholder = new GUIStyle(HudStyle.Small) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
                placeholder.normal.textColor = HudStyle.TextDim;
                countRight = new GUIStyle(HudStyle.Small) { alignment = TextAnchor.MiddleRight, wordWrap = false };
            }
            HudStyle.WindowFrame(panelRect, false);
            float x = panelRect.x + 10f, w = panelRect.width - 20f, y = panelRect.y + 8f;
            List<ModuleDefinition> list = ShownModules();
            hoveredModule = null;

            string head = Searching ? Loc.T("Поиск по всему каталогу") : ModuleCatalog.CategoryName(category);
            GUI.Label(new Rect(x, y, w - 70f, 20f), head, barTitle);
            GUI.Label(new Rect(x + w - 70f, y, 70f, 20f), Loc.T("деталей: ") + list.Count, countRight);
            y += 24f;

            // Search by name and description.
            searchRect = new Rect(x, y, w - 30f, 26f);
            GUI.SetNextControlName("hangar-search");
            string typed = GUI.TextField(searchRect, search ?? "", HudStyle.Field);
            if (typed != search)
            {
                search = typed.Length > 40 ? typed.Substring(0, 40) : typed;
                gridScroll = Vector2.zero;
            }
            if (string.IsNullOrEmpty(search) && GUI.GetNameOfFocusedControl() != "hangar-search")
                GUI.Label(new Rect(searchRect.x + 9f, searchRect.y, searchRect.width - 12f, searchRect.height), Loc.T("Поиск по названию и описанию"), placeholder);
            if (Button(new Rect(searchRect.xMax + 4f, y, 26f, 26f), "×", Searching))
                Defer(() =>
                {
                    search = "";
                    GUIUtility.keyboardControl = 0;
                });
            y += 32f;

            // Sorting: a second click on the active key reverses the direction.
            float bw = (w - 3f * 4f) / 4f;
            for (int i = 0; i < SortKeys.Length; i++)
            {
                SortKey k = SortKeys[i];
                bool on = sortKey == k;
                string label = Loc.T(SortNames[i]) + (on ? (sortDescending ? " ▼" : " ▲") : "");
                bool now = GUI.Toggle(new Rect(x + i * (bw + 4f), y, bw, 24f), on, label, HudStyle.Tab);
                if (now != on)
                    Defer(() =>
                    {
                        if (sortKey == k) sortDescending = !sortDescending;
                        else
                        {
                            sortKey = k;
                            sortDescending = false;
                        }
                    });
            }
            y += 30f;

            // Sub-tabs of the category (none while searching).
            IReadOnlyList<ModuleSubgroup> groups = Searching ? null : ModuleCatalog.SubgroupsOf(category);
            if (groups != null && groups.Count > 0)
            {
                var tabs = new List<ModuleSubgroup>();
                if (groups.Count > 1) tabs.Add(ModuleSubgroup.None);
                tabs.AddRange(groups);
                float tx = x;
                foreach (ModuleSubgroup g in tabs)
                {
                    string label = g == ModuleSubgroup.None ? Loc.T("Все") : ModuleCatalog.SubgroupName(g);
                    float tw = Mathf.Min(w, Mathf.Max(48f, HudStyle.Tab.CalcSize(new GUIContent(label)).x + 6f));
                    if (tx + tw > x + w + 0.5f)
                    {
                        tx = x;
                        y += 28f;
                    }
                    bool on = subgroup == g || (groups.Count == 1 && subgroup == ModuleSubgroup.None);
                    bool now = GUI.Toggle(new Rect(tx, y, tw, 24f), on, label, HudStyle.Tab);
                    if (now != on && !on)
                        Defer(() =>
                        {
                            subgroup = g;
                            gridScroll = Vector2.zero;
                        });
                    tx += tw + 4f;
                }
                y += 30f;
            }

            const float footerHeight = 64f;
            RocketModel.Part picked = facility != null ? facility.Selected : null;
            ModuleDefinition pickedModule = picked != null ? ModuleCatalog.Find(picked.Placement.ModuleId) : null;
            float selectionHeight = pickedModule != null ? 86f : 0f;
            float gridBottom = panelRect.yMax - 8f - footerHeight - selectionHeight - 6f;
            var grid = new Rect(x, y, w, Mathf.Max(TileHeight, gridBottom - y));
            DrawGrid(grid, list, mouse, interactive);

            float by = grid.yMax + 6f;
            if (pickedModule != null)
            {
                DrawSelection(new Rect(x, by, w, selectionHeight - 6f), picked, pickedModule);
                by += selectionHeight;
            }
            DrawFooter(new Rect(x, by, w, footerHeight));
        }

        void DrawGrid(Rect view, List<ModuleDefinition> list, Vector2 mouse, bool interactive)
        {
            float contentWidth = view.width - 14f; // room for the scroll bar
            float tw = (contentWidth - 2f * TileGap) / 3f;
            int rows = (list.Count + 2) / 3;
            float contentHeight = Mathf.Max(0f, rows * (TileHeight + TileGap) - TileGap);
            bool inView = interactive && view.Contains(mouse);
            gridScroll = GUI.BeginScrollView(view, gridScroll, new Rect(0f, 0f, contentWidth, contentHeight));
            Event e = Event.current;
            Vector2 local = e != null ? e.mousePosition : Vector2.zero;
            for (int i = 0; i < list.Count; i++)
            {
                var r = new Rect((i % 3) * (tw + TileGap), (i / 3) * (TileHeight + TileGap), tw, TileHeight);
                if (r.yMax < gridScroll.y - 1f || r.y > gridScroll.y + view.height + 1f) continue; // off screen
                ModuleDefinition m = list[i];
                bool forced = DebugHover != null && m.id == DebugHover; // bridge / tests: a hover without the mouse
                bool hover = forced || (inView && !dragging && r.Contains(local));
                DrawTile(r, m, hover);
                if (hover)
                {
                    hoveredModule = m.id;
                    hoveredTile = new Rect(view.x + r.x - gridScroll.x, view.y + r.y - gridScroll.y, r.width, r.height);
                }
                if (!inView || !r.Contains(local)) continue;
                if (e != null && e.type == EventType.MouseDown && e.button == 0)
                {
                    pressId = m.id;
                    pressPos = mouse;
                    dragging = false;
                    GUIUtility.keyboardControl = 0;
                    e.Use();
                }
            }
            GUI.EndScrollView();
            if (list.Count == 0)
                GUI.Label(new Rect(view.x, view.y + 8f, view.width, 40f),
                    Searching ? Loc.T("Ничего не найдено. Попробуйте другое слово.") : Loc.T("В этой группе деталей нет."), note);
        }

        void DrawTile(Rect r, ModuleDefinition m, bool hover)
        {
            bool locked = IsLocked(m);
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            if (hover)
            {
                Fill(r, new Color(HudStyle.Cyan.r, HudStyle.Cyan.g, HudStyle.Cyan.b, 0.07f));
                Frame(r, new Color(HudStyle.Cyan.r, HudStyle.Cyan.g, HudStyle.Cyan.b, 0.9f));
            }
            else if (m.id == selectedModule) Frame(r, new Color(HudStyle.Cyan.r, HudStyle.Cyan.g, HudStyle.Cyan.b, 0.45f));

            var thumb = new Rect(r.x + 6f, r.y + 8f, r.width - 12f, 62f);
            Thumbnail(thumb, m, 40f, locked ? HudStyle.TextDim : HudStyle.TextSoft);
            PropellantType fuel = TilePropellant(m);
            if (fuel != PropellantType.None)
                Icon(new Rect(r.xMax - 21f, r.y + 5f, 16f, 16f), HangarIcons.Propellant(fuel), HangarIcons.PropellantColor(fuel));
            int count = CountOnRocket(m.id);
            if (count > 0) Colored(new Rect(r.x + 6f, r.y + 4f, 40f, 16f), "×" + count, compactWhite, HudStyle.Accent);

            if (locked)
            {
                Fill(Inset(r, 1f), new Color(0.01f, 0.02f, 0.05f, 0.55f));
                Colored(new Rect(r.x, thumb.yMax - 18f, r.width, 18f), Price(m.price), centeredSmall, HudStyle.Amber);
            }
            Color nameColor = locked ? HudStyle.TextDim : (hover ? HudStyle.TextStrong : HudStyle.TextSoft);
            Colored(new Rect(r.x + 4f, r.y + 74f, r.width - 8f, 34f), m.name, tileName, nameColor);
        }

        /// <summary>Which propellant badge a tile shows: what an engine burns or a tank holds.</summary>
        static PropellantType TilePropellant(ModuleDefinition m)
        {
            if (m.IsEngine) return m.engineKind == EngineKind.Solid ? PropellantType.Solid : m.propellant;
            if (m.IsTank) return m.propellant;
            return PropellantType.None;
        }

        void DrawSelection(Rect r, RocketModel.Part picked, ModuleDefinition m)
        {
            PartPlacement p = picked.Placement;
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            GUI.Label(new Rect(r.x + 10f, r.y + 6f, r.width - 20f, 18f), Loc.T("НА РАКЕТЕ: ") + m.name, barTitle);
            GUI.Label(new Rect(r.x + 10f, r.y + 25f, r.width - 20f, 16f), Where(p), compact);
            bool removable = CanRemove(p);
            float y = r.y + 46f, x = r.x + 10f;
            if (Button(new Rect(x, y, 104f, 26f), removable ? Loc.T("Снять") : Loc.T("Обязательный"), removable)) Defer(() => RemoveSelectedPart(p));
            if (Button(new Rect(x + 110f, y, 90f, 26f), Loc.T("Показать"), true)) Defer(() => facility.FocusOn(q => q == p, false));
            if (Button(new Rect(x + 206f, y, r.width - 226f, 26f), Loc.T("Снять выбор"), true)) Defer(() => facility.Select(null));
        }

        void DrawFooter(Rect r)
        {
            HudStyle.HLine(r.x - 10f, r.y, r.width + 20f, new Color(1f, 1f, 1f, 0.12f));
            ProjectPrice(out long total, out long toBuy);
            float x = r.x + 2f, y = r.y + 8f;
            bool hasProfile = session != null && session.Profile != null;
            string budget = hasProfile ? Credits(session.Profile.credits) : "—";
            GUI.Label(new Rect(x, y, 160f, 14f), Loc.T("БЮДЖЕТ"), HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted));
            GUI.Label(new Rect(x, y + 14f, 200f, 32f), budget, HudStyle.Style(HudStyle.Face.Mono, 24,
                hasProfile && toBuy > session.Profile.credits ? HudStyle.Danger : HudStyle.Accent));
            // Price of the project and what is still to buy, small on the right.
            GUIStyle k = HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted, TextAnchor.MiddleRight);
            GUI.Label(new Rect(r.xMax - 150f, y, 148f, 14f), Loc.T("ПРОЕКТ ") + Credits(total), k);
            GUI.Label(new Rect(r.xMax - 150f, y + 18f, 148f, 14f), toBuy > 0 ? Loc.T("КУПИТЬ ") + Credits(toBuy) : Loc.T("ВСЁ КУПЛЕНО"),
                HudStyle.Style(HudStyle.Face.Mono, 10, toBuy > 0 ? HudStyle.Warning : HudStyle.TextMuted, TextAnchor.MiddleRight));
            GUI.Label(new Rect(r.xMax - 150f, y + 36f, 148f, 14f), Mass(report.LaunchMass), k);
        }

        // ---- Drag and drop ----------------------------------------------------------------------

        void CancelDrag()
        {
            pressId = null;
            dragging = false;
        }

        /// <summary>Runs before any control of the pass: turns a press on a tile into a drag, installs on release.</summary>
        void HandleDragEvents(Event e, Vector2 mouse, bool interactive)
        {
            if (e == null || pressId == null) return;
            if (e.type == EventType.MouseDrag)
            {
                if (!dragging && Vector2.Distance(mouse, pressPos) > DragThreshold) dragging = true;
                if (dragging) e.Use();
                return;
            }
            if (e.type != EventType.MouseUp || e.button != 0) return;
            string id = pressId;
            bool wasDrag = dragging;
            CancelDrag();
            if (!interactive) return;
            if (!wasDrag)
            {
                int target = selectedStage;
                Defer(() => InstallFromMenu(id, target));
            }
            else if (DropTarget(mouse, id, out int stage))
            {
                Defer(() => InstallFromMenu(id, stage));
            }
            else SetStatus(Loc.T("Отпустите деталь над ракетой или над ступенью справа, чтобы установить её."), HudStyle.TextMuted);
            e.Use();
        }

        bool OverPanels(Vector2 mouse) =>
            topRect.Contains(mouse) || railRect.Contains(mouse) || panelRect.Contains(mouse) || stackRect.Contains(mouse) ||
            detailsRect.Contains(mouse) || summaryRect.Contains(mouse) || toolRect.Contains(mouse);

        /// <summary>
        /// Where a dragged part goes: the stage card under the cursor, or the stage of the rocket part under it in the 3D view
        /// (the selected stage when the cursor is over the hall). Payload parts always go to the head section.
        /// </summary>
        bool DropTarget(Vector2 mouse, string id, out int stage)
        {
            stage = selectedStage;
            ModuleDefinition m = ModuleCatalog.Find(id);
            if (m == null) return false;
            bool payload = m.mount == ModuleMount.Payload;
            foreach (KeyValuePair<Rect, int> t in stackTargets)
            {
                if (!t.Key.Contains(mouse)) continue;
                if (!payload && t.Value >= 0) stage = t.Value;
                return true;
            }
            if (OverPanels(mouse)) return false;
            RocketModel.Part part = facility != null ? facility.Hovered : null;
            if (!payload && part != null && part.Placement.Stage >= 0) stage = part.Placement.Stage;
            return true;
        }

        /// <summary>The stage lit up on the rocket while dragging (−1 = head section), null when nothing would be installed.</summary>
        int? DropStageHighlight(Vector2 mouse)
        {
            if (pressId == null || !DropTarget(mouse, pressId, out int stage)) return null;
            ModuleDefinition m = ModuleCatalog.Find(pressId);
            if (m == null) return null;
            if (m.mount == ModuleMount.Payload) return -1;
            return ValidStage(stage) ? stage : (int?)null;
        }

        void DrawDragGhost(Vector2 mouse, Vector2 screen)
        {
            ModuleDefinition m = ModuleCatalog.Find(pressId);
            if (m == null) return;
            int? target = DropStageHighlight(mouse);
            string text = target == null ? Loc.T("сюда нельзя") : target.Value < 0 ? Loc.T("→ головной отсек") : Loc.T("→ ступень ") + (target.Value + 1);
            var r = new Rect(Mathf.Min(mouse.x + 14f, screen.x - 180f), Mathf.Min(mouse.y + 10f, screen.y - 90f), 170f, 82f);
            Color c = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.78f);
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            Thumbnail(new Rect(r.x + 6f, r.y + 6f, 50f, 50f), m, 38f, HudStyle.TextSoft);
            GUI.Label(new Rect(r.x + 62f, r.y + 6f, r.width - 68f, 50f), m.name, tooltip);
            GUI.color = c;
            Colored(new Rect(r.x + 8f, r.y + 58f, r.width - 16f, 18f), text, compactWhite, target == null ? HudStyle.TextMuted : HudStyle.Accent);
        }
    }
}
