using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    // IMGUI helpers and number formatting (display only; all values stay SI).
    public sealed partial class HangarUI
    {
        GUIStyle note, compact, compactWhite, tooltip, tileName, cardText, centeredSmall, valueSmall;

        void EnsureStyles()
        {
            if (note != null && compact != null && tooltip != null && tileName != null && compactWhite != null) return;
            // White base colour so GUI.color tints give exactly HudStyle.Accent / Warning / Danger.
            note = new GUIStyle(HudStyle.Small) { fontSize = 12, wordWrap = true };
            note.normal.textColor = Color.white;
            compact = new GUIStyle(HudStyle.Small) { wordWrap = false };
            tooltip = new GUIStyle(HudStyle.Small) { wordWrap = true, richText = true };
            tileName = new GUIStyle(HudStyle.Small) { wordWrap = true, alignment = TextAnchor.UpperCenter };
            tileName.normal.textColor = Color.white;
            compactWhite = new GUIStyle(compact);
            compactWhite.normal.textColor = Color.white;
            cardText = new GUIStyle(HudStyle.Small) { fontSize = 12, wordWrap = true, richText = true };
            centeredSmall = new GUIStyle(HudStyle.Small) { wordWrap = false, alignment = TextAnchor.MiddleCenter };
            centeredSmall.normal.textColor = Color.white;
            valueSmall = new GUIStyle(HudStyle.Value) { fontSize = 12 };
        }

        static bool Button(string text, bool enabled, params GUILayoutOption[] options)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            bool clicked = GUILayout.Button(text, HudStyle.Button, options);
            GUI.enabled = previous;
            return clicked && enabled;
        }

        static bool Button(Rect r, string text, bool enabled, GUIStyle style = null)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            bool clicked = GUI.Button(r, text, style ?? HudStyle.Button);
            GUI.enabled = previous;
            return clicked && enabled;
        }

        static void Colored(string text, GUIStyle style, Color color, params GUILayoutOption[] options)
        {
            Color c = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, style, options);
            GUI.color = c;
        }

        static void Colored(Rect r, string text, GUIStyle style, Color color)
        {
            Color c = GUI.color;
            GUI.color = color;
            GUI.Label(r, text, style);
            GUI.color = c;
        }

        static void ColoredRow(string label, string value, float labelWidth, Color color)
        {
            Color c = GUI.color;
            GUI.color = color;
            HudStyle.Row(label, value, labelWidth);
            GUI.color = c;
        }

        static bool Repaint => Event.current != null && Event.current.type == EventType.Repaint;

        /// <summary>A filled rectangle (overlays, separators).</summary>
        static void Fill(Rect r, Color color)
        {
            if (!Repaint) return;
            Color c = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = c;
        }

        /// <summary>A hairline frame around <paramref name="r"/> (hover, selection, drop target).</summary>
        static void Frame(Rect r, Color color, float thickness = 1f)
        {
            Fill(new Rect(r.x, r.y, r.width, thickness), color);
            Fill(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            Fill(new Rect(r.x, r.y, thickness, r.height), color);
            Fill(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }

        static void Icon(Rect r, Texture texture, Color color)
        {
            if (texture == null || !Repaint) return;
            Color c = GUI.color;
            GUI.color = new Color(color.r, color.g, color.b, color.a * c.a);
            GUI.DrawTexture(r, texture, ScaleMode.ScaleToFit, true);
            GUI.color = c;
        }

        /// <summary>
        /// The module's preview image, asked for every time it is drawn (previews appear gradually); while there is none, the
        /// category icon stands in for it.
        /// </summary>
        static void Thumbnail(Rect r, ModuleDefinition m, float iconSize, Color iconColor)
        {
            if (m == null) return;
            Texture tex = ModuleThumbnails.Get(m.id);
            if (tex != null)
            {
                if (!Repaint) return;
                Color c = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, c.a * iconColor.a);
                GUI.DrawTexture(r, tex, ScaleMode.ScaleToFit, true);
                GUI.color = c;
                return;
            }
            float s = Mathf.Min(iconSize, Mathf.Min(r.width, r.height));
            Icon(new Rect(r.x + (r.width - s) * 0.5f, r.y + (r.height - s) * 0.5f, s, s), HangarIcons.Category(m.category), iconColor);
        }

        static Rect Inset(Rect r, float d) => new Rect(r.x + d, r.y + d, r.width - 2f * d, r.height - 2f * d);

        // ---- Formatting -------------------------------------------------------------------------

        static string N(double value, int decimals) => UnitFormatter.Number(value, decimals);

        static string Mass(double kg)
        {
            double a = Math.Abs(kg);
            if (a < 1000.0) return N(kg, a < 10.0 && Math.Abs(kg - Math.Round(kg)) > 1e-6 ? 1 : 0) + Loc.T(" кг");
            if (a < 10000.0) return N(kg / 1000.0, 2) + Loc.T(" т");
            return N(kg / 1000.0, 1) + Loc.T(" т");
        }

        /// <summary>Kilograms shown as tonnes with one decimal (no unit suffix).</summary>
        static string Tonnes(double kg) => N(kg / 1000.0, 1);

        static string Force(double newtons)
        {
            double a = Math.Abs(newtons);
            if (a >= 1.0e6) return N(newtons / 1.0e6, 2) + Loc.T(" МН");
            if (a >= 1.0e3) return N(newtons / 1.0e3, 0) + Loc.T(" кН");
            return N(newtons, a < 10.0 ? 2 : 0) + Loc.T(" Н");
        }

        static string Power(double watts)
        {
            double a = Math.Abs(watts);
            if (a >= 1.0e6) return N(watts / 1.0e6, 2) + Loc.T(" МВт");
            if (a >= 1.0e3) return N(watts / 1.0e3, a >= 1.0e4 ? 0 : 1) + Loc.T(" кВт");
            return N(watts, 0) + Loc.T(" Вт");
        }

        static string Distance(double metres)
        {
            double km = metres / 1000.0;
            if (km >= 1.0e6) return N(km / 1.0e6, 0) + Loc.T(" млн км");
            return N(km, 0) + Loc.T(" км");
        }

        static string Speed(double metresPerSecond) => N(metresPerSecond, 0) + Loc.T(" м/с");

        static string Isp(double seconds) => N(seconds, 0) + Loc.T(" с");

        static string Metres(double m) => N(m, m < 10.0 ? 2 : 1) + Loc.T(" м");

        static string BurnTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "—";
            if (seconds < 60.0) return N(seconds, 1) + Loc.T(" с");
            long total = (long)Math.Round(seconds);
            return (total / 60) + Loc.T(" мин ") + (total % 60).ToString("00") + Loc.T(" с");
        }

        static string Price(int credits) => credits > 0 ? N(credits, 0) + Loc.T(" кр.") : Loc.T("бесплатно");

        static string Credits(long credits) => N(credits, 0) + Loc.T(" кр.");

        static string Level(int level) => level > 0 ? Loc.T("уровень ") + level : Loc.T("нет");

        static string YesNo(bool value) => value ? Loc.T("да") : Loc.T("нет");

        static string CapabilityList(ModuleCapability caps)
        {
            var parts = new List<string>();
            if ((caps & ModuleCapability.OrbitPrediction) != 0) parts.Add(Loc.T("прогноз орбиты"));
            if ((caps & ModuleCapability.InterplanetaryNavigation) != 0) parts.Add(Loc.T("межпланетная навигация"));
            if ((caps & ModuleCapability.AtmosphereAnalysis) != 0) parts.Add(Loc.T("анализ атмосферы"));
            if ((caps & ModuleCapability.SampleAnalysis) != 0) parts.Add(Loc.T("анализ образцов"));
            return parts.Count > 0 ? string.Join(", ", parts) : "—";
        }

        static string KindList(SampleKinds kinds)
        {
            var parts = new List<string>();
            if ((kinds & SampleKinds.Rock) != 0) parts.Add(Loc.T("камни"));
            if ((kinds & SampleKinds.Soil) != 0) parts.Add(Loc.T("грунт"));
            if ((kinds & SampleKinds.Liquid) != 0) parts.Add(Loc.T("жидкости"));
            if ((kinds & SampleKinds.Acid) != 0) parts.Add(Loc.T("кислоты"));
            if ((kinds & SampleKinds.Rare) != 0) parts.Add(Loc.T("редкие материалы"));
            return parts.Count > 0 ? string.Join(", ", parts) : "—";
        }

        static string MeasurementList(ScienceMeasurement m)
        {
            var parts = new List<string>();
            if ((m & ScienceMeasurement.Temperature) != 0) parts.Add(Loc.T("температура"));
            if ((m & ScienceMeasurement.Pressure) != 0) parts.Add(Loc.T("давление"));
            if ((m & ScienceMeasurement.AtmosphereComposition) != 0) parts.Add(Loc.T("состав атмосферы"));
            if ((m & ScienceMeasurement.Gravity) != 0) parts.Add(Loc.T("гравитация"));
            if ((m & ScienceMeasurement.Seismic) != 0) parts.Add(Loc.T("сейсмика"));
            if ((m & ScienceMeasurement.Radiation) != 0) parts.Add(Loc.T("радиация"));
            if ((m & ScienceMeasurement.MagneticField) != 0) parts.Add(Loc.T("магнитное поле"));
            if ((m & ScienceMeasurement.Imaging) != 0) parts.Add(Loc.T("съёмка"));
            if ((m & ScienceMeasurement.Mineralogy) != 0) parts.Add(Loc.T("минералогия"));
            return parts.Count > 0 ? string.Join(", ", parts) : "—";
        }

        static string PropellantName(PropellantType p) => BlueprintCompiler.Name(p);

        static string EngineKindName(EngineKind k)
        {
            switch (k)
            {
                case EngineKind.Solid: return Loc.T("твердотопливный");
                case EngineKind.Nuclear: return Loc.T("ядерный");
                case EngineKind.Electric: return Loc.T("электрический");
                default: return Loc.T("жидкостный");
            }
        }

        static string MountName(ModuleMount mount)
        {
            switch (mount)
            {
                case ModuleMount.StageEngine: return Loc.T("двигатель ступени");
                case ModuleMount.StageTank: return Loc.T("бак ступени");
                case ModuleMount.StageSide: return Loc.T("на борт ступени (комплект)");
                default: return Loc.T("головной отсек");
            }
        }
    }
}
