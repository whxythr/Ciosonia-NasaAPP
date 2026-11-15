using System;
using System.Collections.Generic;
using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Procedural;
using SpaceSim.Science;
using SpaceSim.Units;
using UnityEngine;
using Face = SpaceSim.Runtime.HudStyle.Face;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Science map window (F2 or the HUD button): the selected body as an equirectangular map of what the instruments measured.
    /// Layers: coverage, temperature, pressure, terrain. Unscanned cells are dimmed and hatched; a click on a cell shows its
    /// latitude/longitude and the measured values; the vehicle's ground point and instrument footprint are marked on the active body.
    /// Not modal: the flight goes on while it is open.
    /// </summary>
    public sealed class ScienceMapUI : MonoBehaviour
    {
        enum View { Coverage, Temperature, Pressure, Terrain }

        static readonly string[] ViewNames = { "Покрытие", "Температура", "Давление", "Рельеф" };

        ScienceSession science;
        StarSystemRuntime system;
        bool open;
        View view = View.Coverage;
        string bodyId = "";
        int selectedCell = -1;
        Texture2D texture;
        Color32[] pixels;
        int builtVersion = -1, builtCellCount = -1;
        View builtView;
        string builtBody;
        float lastBuild;

        /// <summary>Texture pixels per map cell (hatching of unscanned cells needs a few).</summary>
        const int Px = 4;

        public bool IsOpen => open;

        public void Initialize(ScienceSession session, StarSystemRuntime starSystem)
        {
            science = session;
            system = starSystem;
        }

        public void Toggle()
        {
            open = !open;
            if (open)
            {
                if (science != null && !string.IsNullOrEmpty(science.ActiveBodyId)) bodyId = science.ActiveBodyId;
                else if (system != null) bodyId = system.ActiveBody.Id;
                selectedCell = -1;
            }
        }

        public void Close() => open = false;

        void Update()
        {
            if (FlightInput.IsTyping || FlightInput.PauseMenuOpen) return;
            if (FlightInput.GetKeyDown(InputKey.F2)) Toggle();
        }

        void OnDestroy()
        {
            if (texture != null) Destroy(texture);
        }

        // ─────────────────────────────── window ───────────────────────────────

        void OnGUI()
        {
            if (!open || science == null || FlightInput.PauseMenuOpen || FlightInput.ModalWindowOpen || FlightInput.MapOpen) return;
            HudStyle.Begin();
            GUI.depth = -2;
            Vector2 screen = HudStyle.VirtualScreen;
            float w = Mathf.Min(1180f, screen.x - 40f), h = Mathf.Min(700f, screen.y - 140f);
            var win = new Rect((screen.x - w) * 0.5f, Mathf.Max(20f, (screen.y - h) * 0.5f - 30f), w, h);
            HudStyle.WindowFrame(win, false);

            float x = win.x + 18f, y = win.y + 12f;
            GUI.Label(new Rect(x, y, w - 120f, 26f), Loc.T("НАУЧНАЯ КАРТА") + "  ·  " + BodyName(bodyId), HudStyle.Style(Face.DisplayStrong, 20, HudStyle.TextStrong));
            if (GUI.Button(new Rect(win.xMax - 120f, y, 102f, 26f), Loc.T("Закрыть") + " " + FlightInput.KeyLabel(InputKey.F2), HudStyle.Button)) open = false;
            y += 34f;

            y = DrawBodySelector(x, y, w - 36f);
            ScienceBodyMap map = null;
            if (science.Store.TryGet(bodyId, out ScienceBodyMap m)) map = m;
            y = DrawLayerTabs(x, y, map);

            const float side = 330f;
            float mapW = w - 36f - side - 16f, mapH = mapW * 0.5f;
            float maxMapH = win.yMax - y - 70f;
            if (mapH > maxMapH)
            {
                mapH = maxMapH;
                mapW = mapH * 2f;
            }
            var mapRect = new Rect(x, y, mapW, mapH);
            DrawMap(mapRect, map);
            DrawLegend(new Rect(x, mapRect.yMax + 8f, mapW, 52f), map);
            DrawSidePanel(new Rect(win.xMax - 18f - side, y, side, win.yMax - y - 12f), map);
        }

        float DrawBodySelector(float x, float y, float width)
        {
            GUI.Label(new Rect(x, y, 60f, 22f), Loc.T("Тело:"), HudStyle.Style(Face.Mono, 12, HudStyle.TextMuted));
            float bx = x + 60f;
            foreach (string id in BodyIds())
            {
                string label = BodyName(id);
                if (science.Store.TryGet(id, out ScienceBodyMap m)) label += "  " + UnitFormatter.Number(100.0 * m.AnyCoverage, 0) + "%";
                if (id == science.ActiveBodyId) label = "◉ " + label;
                float bw = HudStyle.Button.CalcSize(new GUIContent(label)).x + 16f;
                if (bx + bw > x + width)
                {
                    bx = x + 60f;
                    y += 26f;
                }
                bool on = id == bodyId;
                if (GUI.Toggle(new Rect(bx, y, bw, 22f), on, label, HudStyle.Button) && !on)
                {
                    bodyId = id;
                    selectedCell = -1;
                }
                bx += bw + 6f;
            }
            return y + 30f;
        }

        IEnumerable<string> BodyIds()
        {
            var seen = new HashSet<string>();
            if (system != null)
                foreach (var b in system.Bodies)
                    if (seen.Add(b.Id)) yield return b.Id;
            foreach (ScienceBodyMap m in science.Store.Bodies)
                if (seen.Add(m.BodyId)) yield return m.BodyId;
        }

        string BodyName(string id)
        {
            if (system != null)
            {
                int i = system.IndexOf(id);
                if (i >= 0) return system.Bodies[i].Name;
            }
            return science.Store.TryGet(id, out ScienceBodyMap m) && m.DisplayName.Length > 0 ? Loc.T(m.DisplayName) : id;
        }

        float DrawLayerTabs(float x, float y, ScienceBodyMap map)
        {
            float tx = x;
            for (int i = 0; i < ViewNames.Length; i++)
            {
                string label = Loc.T(ViewNames[i]);
                if (map != null)
                {
                    double c = i == 0 ? map.AnyCoverage : map[(ScienceLayer)(i - 1)].Coverage;
                    label += "  " + UnitFormatter.Number(100.0 * c, 1) + "%";
                }
                float tw = HudStyle.Tab.CalcSize(new GUIContent(label)).x + 24f;
                bool on = (int)view == i;
                if (GUI.Toggle(new Rect(tx, y, tw, 26f), on, label, HudStyle.Tab) && !on) view = (View)i;
                tx += tw + 4f;
            }
            return y + 34f;
        }

        // ─────────────────────────────── map ───────────────────────────────

        void DrawMap(Rect r, ScienceBodyMap map)
        {
            HudStyle.Fill(r, new Color(0.03f, 0.035f, 0.05f, 1f));
            LatLonGrid grid = map != null ? map.Grid : LatLonGrid.Default;
            EnsureTexture(map, grid);
            if (texture != null) GUI.DrawTexture(r, texture);
            // Graticule every 30°.
            Color line = new Color(1f, 1f, 1f, 0.12f);
            for (int lon = -150; lon <= 150; lon += 30) HudStyle.Fill(new Rect(r.x + (lon + 180f) / 360f * r.width, r.y, 1f, r.height), line);
            for (int lat = -60; lat <= 60; lat += 30) HudStyle.Fill(new Rect(r.x, r.y + (90f - lat) / 180f * r.height, r.width, 1f), lat == 0 ? new Color(1f, 1f, 1f, 0.22f) : line);
            HudStyle.Frame(r, HudStyle.Line);
            GUIStyle tick = HudStyle.Style(Face.Mono, 9, HudStyle.TextDim);
            for (int lon = -120; lon <= 120; lon += 60) GUI.Label(new Rect(r.x + (lon + 180f) / 360f * r.width + 3f, r.yMax - 14f, 60f, 12f), lon + "°", tick);
            for (int lat = -60; lat <= 60; lat += 30) GUI.Label(new Rect(r.x + 3f, r.y + (90f - lat) / 180f * r.height - 13f, 60f, 12f), lat + "°", tick);

            if (map == null)
                GUI.Label(r, Loc.T("Нет данных: поставьте на аппарат научные сканеры (Ангар → Наука) и выйдите на орбиту этого тела."),
                    HudStyle.Style(Face.Body, 14, HudStyle.TextMuted, TextAnchor.MiddleCenter));

            DrawVehicleMarker(r, grid);

            if (selectedCell >= 0 && selectedCell < grid.CellCount)
            {
                float cw = r.width / grid.Columns, ch = r.height / grid.Rows;
                int row = grid.RowOf(selectedCell), col = grid.ColumnOf(selectedCell);
                HudStyle.Frame(new Rect(r.x + col * cw - 1f, r.y + (grid.Rows - 1 - row) * ch - 1f, cw + 2f, ch + 2f), Color.white, 2f);
            }

            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition))
            {
                float u = (e.mousePosition.x - r.x) / r.width, v = (e.mousePosition.y - r.y) / r.height;
                int col = Mathf.Clamp((int)(u * grid.Columns), 0, grid.Columns - 1);
                int row = Mathf.Clamp(grid.Rows - 1 - (int)(v * grid.Rows), 0, grid.Rows - 1);
                selectedCell = grid.Index(row, col);
                e.Use();
            }
        }

        /// <summary>Current ground point of the vehicle and the footprint of its widest working instrument (active body only).</summary>
        void DrawVehicleMarker(Rect r, LatLonGrid grid)
        {
            ScanReport rep = science.LastReport;
            if (rep == null || bodyId != science.ActiveBodyId) return;
            Vector2 p = ToMap(r, rep.Latitude, rep.Longitude);
            double footprint = 0.0;
            foreach (double a in rep.FootprintAngle) footprint = Math.Max(footprint, a);
            if (footprint > 0.0)
            {
                // The footprint circle: points at central angle λ around the ground point, drawn on the map.
                const int n = 48;
                for (int k = 0; k < n; k++)
                {
                    double az = k * MathD.TwoPi / n;
                    double lat2 = Math.Asin(Math.Sin(rep.Latitude) * Math.Cos(footprint) + Math.Cos(rep.Latitude) * Math.Sin(footprint) * Math.Cos(az));
                    double lon2 = rep.Longitude + Math.Atan2(Math.Sin(az) * Math.Sin(footprint) * Math.Cos(rep.Latitude),
                        Math.Cos(footprint) - Math.Sin(rep.Latitude) * Math.Sin(lat2));
                    HudStyle.Dot(ToMap(r, lat2, MathD.WrapPi(lon2)), 2f, new Color(1f, 1f, 1f, 0.8f));
                }
            }
            HudStyle.Dot(p, 9f, Color.black);
            HudStyle.Dot(p, 6f, rep.AnyScanning ? HudStyle.Green : HudStyle.Amber);
        }

        static Vector2 ToMap(Rect r, double lat, double lon) =>
            new Vector2(r.x + (float)((lon * MathD.RadToDeg + 180.0) / 360.0) * r.width, r.y + (float)((90.0 - lat * MathD.RadToDeg) / 180.0) * r.height);

        void EnsureTexture(ScienceBodyMap map, LatLonGrid grid)
        {
            int version = science.Store.Version;
            bool changed = builtView != view || builtBody != bodyId || builtCellCount != grid.CellCount;
            if (!changed && (version == builtVersion || Time.unscaledTime - lastBuild < 0.25f) && texture != null) return;
            int tw = grid.Columns * Px, th = grid.Rows * Px;
            if (texture == null || texture.width != tw || texture.height != th)
            {
                if (texture != null) Destroy(texture);
                texture = new Texture2D(tw, th, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
                pixels = new Color32[tw * th];
            }
            Range(map, out double lo, out double hi);
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                int row = grid.RowOf(cell), col = grid.ColumnOf(cell);
                bool covered = map != null && (view == View.Coverage ? map.IsAnyCovered(cell) : map[LayerOf(view)].IsCovered(cell));
                Color32 c = covered ? CellColor(map, cell, lo, hi) : new Color32(0, 0, 0, 0);
                for (int py = 0; py < Px; py++)
                for (int px = 0; px < Px; px++)
                {
                    int X = col * Px + px, Y = row * Px + py; // texture row 0 = bottom = south
                    Color32 o = c;
                    if (!covered)
                        o = ((X + Y) % 6) == 0 ? new Color32(70, 74, 86, 255) : new Color32(18, 20, 26, 255); // hatching: not scanned
                    pixels[Y * tw + X] = o;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            builtVersion = version;
            builtView = view;
            builtBody = bodyId;
            builtCellCount = grid.CellCount;
            lastBuild = Time.unscaledTime;
        }

        static ScienceLayer LayerOf(View v) => v == View.Temperature ? ScienceLayer.Temperature : v == View.Pressure ? ScienceLayer.Pressure : ScienceLayer.Terrain;

        /// <summary>Colour scale bounds of the current view: measured min/max (pressure on a log scale).</summary>
        void Range(ScienceBodyMap map, out double lo, out double hi)
        {
            lo = 0.0;
            hi = 1.0;
            if (map == null || view == View.Coverage) return;
            ScienceLayerMap layer = map[LayerOf(view)];
            if (layer.CoveredCount == 0) return;
            lo = layer.Min;
            hi = layer.Max;
            if (view == View.Pressure)
            {
                lo = lo > 0.0 ? Math.Log10(lo) : 0.0;
                hi = hi > 0.0 ? Math.Log10(hi) : 0.0;
            }
            if (hi - lo < 1e-6)
            {
                lo -= 0.5;
                hi += 0.5;
            }
        }

        Color32 CellColor(ScienceBodyMap map, int cell, double lo, double hi)
        {
            switch (view)
            {
                case View.Coverage:
                {
                    int n = 0;
                    foreach (ScienceLayer l in ScienceLayers.All) if (map[l].IsCovered(cell)) n++;
                    return n >= 3 ? new Color32(120, 220, 140, 255) : n == 2 ? new Color32(80, 160, 210, 255) : new Color32(70, 90, 170, 255);
                }
                case View.Temperature:
                    return Thermal((map[ScienceLayer.Temperature].Value(cell) - lo) / (hi - lo));
                case View.Pressure:
                {
                    float p = map[ScienceLayer.Pressure].Value(cell);
                    if (p <= 0f) return new Color32(110, 96, 128, 255); // vacuum
                    return Barometric((Math.Log10(p) - lo) / (hi - lo));
                }
                default:
                {
                    ScienceLayerMap t = map[ScienceLayer.Terrain];
                    bool liquid = (int)t.Value(cell, ScienceLayers.SurfaceCodeChannel) >= ScienceLayers.LiquidFlag;
                    double f = (t.Value(cell, ScienceLayers.ElevationChannel) - lo) / (hi - lo);
                    Color c = liquid ? Color.Lerp(new Color(0.05f, 0.12f, 0.35f), new Color(0.2f, 0.45f, 0.75f), (float)MathD.Clamp01(f * 2.0)) : Hypsometric(f);
                    float shade = 0.75f + 0.5f * Mathf.Clamp01(t.Value(cell, ScienceLayers.AlbedoChannel)); // brighter ground, brighter cell
                    return (Color)new Color(Mathf.Clamp01(c.r * shade), Mathf.Clamp01(c.g * shade), Mathf.Clamp01(c.b * shade), 1f);
                }
            }
        }

        static Color32 Ramp(double f, Color[] stops)
        {
            f = MathD.Clamp01(f) * (stops.Length - 1);
            int i = Math.Min(stops.Length - 2, (int)f);
            return (Color32)Color.Lerp(stops[i], stops[i + 1], (float)(f - i));
        }

        static readonly Color[] ThermalStops =
        {
            new Color(0.10f, 0.15f, 0.55f), new Color(0.15f, 0.55f, 0.95f), new Color(0.55f, 0.90f, 0.85f),
            new Color(0.98f, 0.90f, 0.35f), new Color(0.95f, 0.45f, 0.15f), new Color(0.75f, 0.08f, 0.10f)
        };

        static readonly Color[] PressureStops =
        {
            new Color(0.27f, 0.00f, 0.33f), new Color(0.23f, 0.32f, 0.55f), new Color(0.13f, 0.57f, 0.55f),
            new Color(0.37f, 0.79f, 0.38f), new Color(0.99f, 0.91f, 0.14f)
        };

        static readonly Color[] TerrainStops =
        {
            new Color(0.20f, 0.30f, 0.20f), new Color(0.45f, 0.55f, 0.30f), new Color(0.70f, 0.62f, 0.40f),
            new Color(0.55f, 0.42f, 0.32f), new Color(0.92f, 0.92f, 0.95f)
        };

        static Color32 Thermal(double f) => Ramp(f, ThermalStops);
        static Color32 Barometric(double f) => Ramp(f, PressureStops);
        static Color Hypsometric(double f) => Ramp(f, TerrainStops);

        // ─────────────────────────────── legend ───────────────────────────────

        void DrawLegend(Rect r, ScienceBodyMap map)
        {
            GUIStyle small = HudStyle.Style(Face.Mono, 11, HudStyle.TextSoft);
            GUIStyle dim = HudStyle.Style(Face.Mono, 11, HudStyle.TextMuted);
            float x = r.x;
            Swatch(ref x, r.y, new Color32(18, 20, 26, 255), Loc.T("не исследовано"), dim, true);
            if (view == View.Coverage)
            {
                Swatch(ref x, r.y, new Color32(70, 90, 170, 255), Loc.T("1 слой"), small);
                Swatch(ref x, r.y, new Color32(80, 160, 210, 255), Loc.T("2 слоя"), small);
                Swatch(ref x, r.y, new Color32(120, 220, 140, 255), Loc.T("все 3 слоя"), small);
                return;
            }
            if (map == null || map[LayerOf(view)].CoveredCount == 0) return;
            Range(map, out double lo, out double hi);
            Color[] stops = view == View.Temperature ? ThermalStops : view == View.Pressure ? PressureStops : TerrainStops;
            var bar = new Rect(x + 10f, r.y + 4f, Mathf.Min(320f, r.xMax - x - 20f), 12f);
            for (int i = 0; i < 64; i++)
                HudStyle.Fill(new Rect(bar.x + bar.width * i / 64f, bar.y, bar.width / 64f + 1f, bar.height), Ramp(i / 63.0, stops));
            HudStyle.Frame(bar, HudStyle.Line);
            string loText, hiText;
            if (view == View.Temperature)
            {
                loText = TemperatureText(lo);
                hiText = TemperatureText(hi);
            }
            else if (view == View.Pressure)
            {
                loText = PressureText(Math.Pow(10.0, lo));
                hiText = PressureText(Math.Pow(10.0, hi));
            }
            else
            {
                loText = ElevationText(lo);
                hiText = ElevationText(hi);
            }
            GUI.Label(new Rect(bar.x, bar.yMax + 3f, 200f, 16f), loText, small);
            GUI.Label(new Rect(bar.xMax - 200f, bar.yMax + 3f, 200f, 16f), hiText, HudStyle.Style(Face.Mono, 11, HudStyle.TextSoft, TextAnchor.MiddleRight));
            float after = bar.xMax + 16f;
            if (view == View.Pressure)
            {
                if (map.Airless || map[ScienceLayer.Pressure].Min <= 0.0) Swatch(ref after, r.y, new Color32(110, 96, 128, 255), Loc.T("вакуум (0 Па)"), small);
                GUI.Label(new Rect(bar.x, bar.yMax + 20f, 400f, 16f), Loc.T("логарифмическая шкала"), dim);
            }
            if (view == View.Terrain) Swatch(ref after, r.y, new Color32(30, 70, 150, 255), Loc.T("жидкость"), small);
        }

        static void Swatch(ref float x, float y, Color32 color, string text, GUIStyle style, bool hatched = false)
        {
            var box = new Rect(x, y + 3f, 14f, 14f);
            HudStyle.Fill(box, color);
            if (hatched)
                for (int k = 0; k < 3; k++) HudStyle.Fill(new Rect(box.x + 2f + 4f * k, box.y + 2f, 1f, 10f), new Color32(70, 74, 86, 255));
            HudStyle.Frame(box, HudStyle.Line);
            float w = style.CalcSize(new GUIContent(text)).x;
            GUI.Label(new Rect(x + 20f, y + 1f, w + 4f, 18f), text, style);
            x += 20f + w + 18f;
        }

        // ─────────────────────────────── side panel ───────────────────────────────

        void DrawSidePanel(Rect r, ScienceBodyMap map)
        {
            HudStyle.Card(r, 0.6f);
            float x = r.x + 12f, y = r.y + 10f, w = r.width - 24f;
            GUIStyle head = HudStyle.Style(Face.Mono, 11, HudStyle.Amber);
            GUIStyle text = HudStyle.Style(Face.Body, 13, HudStyle.Text);
            GUIStyle dim = HudStyle.Style(Face.Body, 12, HudStyle.TextMuted);

            GUI.Label(new Rect(x, y, w, 16f), Loc.T("ЯЧЕЙКА"), head);
            y += 20f;
            LatLonGrid grid = map != null ? map.Grid : LatLonGrid.Default;
            if (selectedCell < 0)
            {
                GUI.Label(new Rect(x, y, w, 18f), Loc.T("Щёлкните по карте, чтобы увидеть значения."), dim);
                y += 22f;
            }
            else
            {
                double lat = grid.CenterLatitude(grid.RowOf(selectedCell)), lon = grid.CenterLongitude(grid.ColumnOf(selectedCell));
                y = Row(x, y, w, Loc.T("Координаты"), LatLonText(lat, lon), text);
                if (map == null || !map.IsAnyCovered(selectedCell)) y = Row(x, y, w, "", Loc.T("не исследовано"), dim);
                else
                {
                    if (map[ScienceLayer.Temperature].IsCovered(selectedCell))
                        y = Row(x, y, w, Loc.T("Температура"), TemperatureText(map[ScienceLayer.Temperature].Value(selectedCell)), text);
                    if (map[ScienceLayer.Pressure].IsCovered(selectedCell))
                        y = Row(x, y, w, Loc.T("Давление"), PressureText(map[ScienceLayer.Pressure].Value(selectedCell)), text);
                    ScienceLayerMap t = map[ScienceLayer.Terrain];
                    if (t.IsCovered(selectedCell))
                    {
                        int code = (int)t.Value(selectedCell, ScienceLayers.SurfaceCodeChannel);
                        y = Row(x, y, w, Loc.T("Высота"), ElevationText(t.Value(selectedCell, ScienceLayers.ElevationChannel)), text);
                        y = Row(x, y, w, Loc.T("Альбедо"), UnitFormatter.Number(t.Value(selectedCell, ScienceLayers.AlbedoChannel), 3), text);
                        y = Row(x, y, w, Loc.T("Поверхность"), BiomeName((Biome)(code % ScienceLayers.LiquidFlag)) + (code >= ScienceLayers.LiquidFlag ? Loc.T(" (жидкость)") : ""), text);
                    }
                }
            }

            y += 8f;
            HudStyle.HLine(x, y, w);
            y += 8f;
            GUI.Label(new Rect(x, y, w, 16f), Loc.T("ПРИБОРЫ АППАРАТА"), head);
            y += 20f;
            IReadOnlyList<ScannerSpec> scanners = science.ActiveScanners;
            ScanReport rep = science.LastReport;
            if (scanners == null || scanners.Count == 0)
            {
                GUI.Label(new Rect(x, y, w, 36f), Loc.T("Сканеров на борту нет (или приборы отключены повреждением)."), new GUIStyle(dim) { wordWrap = true });
                y += 40f;
            }
            else
            {
                if (!science.Powered)
                {
                    GUI.Label(new Rect(x, y, w, 18f), Loc.T("Нет электропитания — приборы стоят."), HudStyle.Style(Face.Body, 12, HudStyle.Red));
                    y += 20f;
                }
                for (int i = 0; i < scanners.Count; i++)
                {
                    ScannerSpec s = scanners[i];
                    ScannerStatus st = rep != null && i < rep.Status.Count ? rep.Status[i] : ScannerStatus.Off;
                    Color c = st == ScannerStatus.Scanning ? HudStyle.Green : st == ScannerStatus.Off ? HudStyle.TextMuted : HudStyle.Amber;
                    GUI.Label(new Rect(x, y, w, 16f), s.Name, text);
                    y += 17f;
                    GUI.Label(new Rect(x + 8f, y, w - 8f, 16f), StatusText(st, s), HudStyle.Style(Face.Mono, 11, c));
                    y += 16f;
                    GUI.Label(new Rect(x + 8f, y, w - 8f, 16f),
                        "±" + UnitFormatter.Number(s.HalfAngle * MathD.RadToDeg, 0) + "°  ·  " + UnitFormatter.Number(s.MinAltitude / 1000.0, 0) + "–" +
                        UnitFormatter.Number(s.MaxAltitude / 1000.0, 0) + Loc.T(" км") + "  ·  " + UnitFormatter.Number(s.Ifov * 1000.0, 1) + Loc.T(" мрад") +
                        "  ·  " + UnitFormatter.Number(s.PowerDraw, 0) + Loc.T(" Вт"), HudStyle.Style(Face.Mono, 10, HudStyle.TextDim));
                    y += 18f;
                }
                for (int l = 0; l < ScienceLayers.Count; l++)
                {
                    string label = Loc.T(ViewNames[l + 1]) + ": " + (science.LayerOn[l] ? Loc.T("вкл.") : Loc.T("выкл."));
                    science.LayerOn[l] = GUI.Toggle(new Rect(x + (w / 3f) * l, y, w / 3f - 4f, 22f), science.LayerOn[l], label, HudStyle.Button);
                }
                y += 28f;
            }
            if (rep != null && bodyId == science.ActiveBodyId)
            {
                y = Row(x, y, w, Loc.T("Под аппаратом"), LatLonText(rep.Latitude, rep.Longitude), text);
                y = Row(x, y, w, Loc.T("Высота орбиты"), UnitFormatter.Number(rep.Altitude / 1000.0, 1) + Loc.T(" км"), text);
                double fp = 0.0;
                foreach (double a in rep.FootprintAngle) fp = Math.Max(fp, a);
                if (fp > 0.0 && map != null && map.Radius > 0.0) y = Row(x, y, w, Loc.T("Радиус пятна"), UnitFormatter.Number(fp * map.Radius / 1000.0, 1) + Loc.T(" км"), text);
                if (map != null && map.PendingCount > 0) y = Row(x, y, w, Loc.T("В обработке"), map.PendingCount + Loc.T(" яч."), dim);
                if (rep.Capped) GUI.Label(new Rect(x, y, w, 32f), Loc.T("Слишком сильное ускорение: между кадрами возможны пропуски трассы."), new GUIStyle(dim) { wordWrap = true });
            }
        }

        static float Row(float x, float y, float w, string label, string value, GUIStyle style)
        {
            GUI.Label(new Rect(x, y, 120f, 18f), label, HudStyle.Style(Face.Mono, 11, HudStyle.TextMuted));
            GUI.Label(new Rect(x + 120f, y, w - 120f, 18f), value, style);
            return y + 20f;
        }

        static string StatusText(ScannerStatus st, ScannerSpec s)
        {
            switch (st)
            {
                case ScannerStatus.Scanning: return Loc.T("ИДЁТ СЪЁМКА");
                case ScannerStatus.TooLow: return Loc.T("слишком низко (мин. ") + UnitFormatter.Number(s.MinAltitude / 1000.0, 0) + Loc.T(" км)");
                case ScannerStatus.TooHigh: return Loc.T("слишком высоко (макс. ") + UnitFormatter.Number(s.MaxAltitude / 1000.0, 0) + Loc.T(" км)");
                case ScannerStatus.ResolutionTooCoarse: return Loc.T("разрешение хуже ячейки карты");
                case ScannerStatus.NoPower: return Loc.T("нет питания");
                case ScannerStatus.Landed: return Loc.T("на поверхности");
                default: return Loc.T("выключен");
            }
        }

        // ─────────────────────────────── formatting ───────────────────────────────

        static string TemperatureText(double kelvin) =>
            UnitFormatter.Number(kelvin, 1) + " K (" + UnitFormatter.Number(kelvin - 273.15, 1) + " °C)";

        static string PressureText(double pa)
        {
            if (!(pa > 0.0)) return Loc.T("вакуум (0 Па)");
            if (pa < 1.0) return UnitFormatter.Scientific(pa, 3) + Loc.T(" Па");
            if (pa < 1.0e4) return UnitFormatter.Number(pa, pa < 100.0 ? 2 : 0) + Loc.T(" Па");
            if (pa < 1.0e6) return UnitFormatter.Number(pa / 1000.0, 1) + Loc.T(" кПа");
            return UnitFormatter.Number(pa / 1.0e6, 2) + Loc.T(" МПа");
        }

        static string ElevationText(double m) =>
            Math.Abs(m) < 10000.0 ? UnitFormatter.Number(m, 0) + Loc.T(" м") : UnitFormatter.Number(m / 1000.0, 2) + Loc.T(" км");

        static string LatLonText(double lat, double lon)
        {
            double la = lat * MathD.RadToDeg, lo = lon * MathD.RadToDeg;
            return UnitFormatter.Number(Math.Abs(la), 1) + "° " + (la >= 0.0 ? Loc.T("с.ш.") : Loc.T("ю.ш.")) + ", " +
                   UnitFormatter.Number(Math.Abs(lo), 1) + "° " + (lo >= 0.0 ? Loc.T("в.д.") : Loc.T("з.д."));
        }

        static string BiomeName(Biome b)
        {
            switch (b)
            {
                case Biome.DeepOcean: return Loc.T("глубоководье");
                case Biome.ShallowOcean: return Loc.T("мелководье");
                case Biome.SeaIce: return Loc.T("морской лёд");
                case Biome.Beach: return Loc.T("побережье");
                case Biome.Desert: return Loc.T("равнина / пустыня");
                case Biome.Grassland: return Loc.T("луга");
                case Biome.Forest: return Loc.T("лес");
                case Biome.Tundra: return Loc.T("тундра");
                case Biome.Rock: return Loc.T("скалы");
                default: return Loc.T("снег и лёд");
            }
        }
    }
}
