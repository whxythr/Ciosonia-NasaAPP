using SpaceSim.Exploration;
using SpaceSim.Localization;
using SpaceSim.Units;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The robot's condition on the HUD during the EVA, in the flight HUD's style (<see cref="HudStyle"/>): four bars —
    /// hull, thermal control, chemical protection, visor — with their current loss per minute, the surroundings that cause
    /// it and the time left before the robot fails. Drawn in front of the visor blackout
    /// (<see cref="RobotVisorOverlay.StatusDepth"/>), so a blind robot still shows its status.
    /// </summary>
    public sealed class RobotStatusPanel : MonoBehaviour
    {
        public const float Width = 330f, Height = 196f;

        public RobotStatus Status { get; set; }
        public RobotEnvironment Environment { get; set; }
        public bool Visible { get; set; }
        /// <summary>Panel rectangle in HUD units (top-left area, under the altitude ladder).</summary>
        public static Rect Area(Vector2 screen) => new Rect(24f, Mathf.Min(560f, screen.y - Height - 330f), Width, Height);

        void OnGUI()
        {
            if (!Visible || Status == null || FlightInput.PauseMenuOpen || FlightInput.MapOpen) return;
            GUI.depth = RobotVisorOverlay.StatusDepth;
            HudStyle.Begin();
            Rect r = Area(HudStyle.VirtualScreen);
            HudStyle.Card(r);
            Color accent = Status.Disabled ? HudStyle.Danger : Status.Blind ? HudStyle.Warning : HudStyle.Accent;
            HudStyle.Fill(new Rect(r.x, r.y, 3f, r.height), accent);

            float x = r.x + 14f, y = r.y + 8f, w = r.width - 26f;
            GUI.Label(new Rect(x, y, w, 16f), Loc.T("РОБОТ · СОСТОЯНИЕ"), HudStyle.Style(HudStyle.Face.Mono, 11, accent));
            string flag = Status.Disabled ? Loc.T("ОТКАЗ") : Status.Blind ? Loc.T("СЛЕП") : "";
            if (flag.Length > 0) GUI.Label(new Rect(x, y, w, 16f), flag, HudStyle.Style(HudStyle.Face.Mono, 11, accent, TextAnchor.MiddleRight));
            y += 22f;

            for (int i = 0; i < RobotStatus.ConditionCount; i++)
            {
                var c = (RobotCondition)i;
                Row(new Rect(x, y, w, 22f), RobotEva.ConditionName(c), (float)Status[c], Status.RateOf(c));
                y += 26f;
            }

            HudStyle.HLine(x, y + 2f, w);
            y += 8f;
            RobotEnvironment e = Environment;
            string env = (e.Pressure > 0.0 ? UnitFormatter.Number(e.Pressure / 101325.0, 2) + Loc.T(" атм") : Loc.T("вакуум")) +
                         (e.Temperature > 0.0 ? " · " + UnitFormatter.Number(e.Temperature - 273.15, 0) + " °C" : "") +
                         (e.AcidLevel > 0 ? Loc.T(" · кислота ") + e.AcidLevel : "") +
                         (e.RadiationLevel > 0 ? Loc.T(" · радиация ") + e.RadiationLevel : "");
            GUI.Label(new Rect(x, y, w, 16f), env, HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.TextMuted));
            y += 17f;
            GUI.Label(new Rect(x, y, w, 16f), Summary(), HudStyle.Style(HudStyle.Face.Mono, 10, Status.Disabled || Status.Blind ? accent : HudStyle.TextSoft));
        }

        string Summary()
        {
            if (Status.Disabled) return Loc.T("Робот неисправен — возврат в ракету");
            double fail = Status.TimeToDisabled(), blind = Status.TimeToBlind();
            if (Status.Blind)
                return Loc.T("Визор разрушен: изображения нет") + (double.IsPositiveInfinity(fail) ? "" : Loc.T(" · отказ через ") + UnitFormatter.Duration(fail));
            if (!double.IsPositiveInfinity(fail) && fail <= blind) return Loc.T("До отказа ≈ ") + UnitFormatter.Duration(fail);
            if (!double.IsPositiveInfinity(blind)) return Loc.T("До потери зрения ≈ ") + UnitFormatter.Duration(blind);
            return Loc.T("Среда безопасна для робота");
        }

        static void Row(Rect r, string name, float value, double ratePerSecond)
        {
            GUI.Label(new Rect(r.x, r.y, 120f, r.height), name, HudStyle.Style(HudStyle.Face.Body, 12, HudStyle.TextSoft));
            var bar = new Rect(r.x + 122f, r.y + 7f, r.width - 122f - 96f, 8f);
            HudStyle.Fill(bar, new Color(1f, 1f, 1f, 0.12f));
            Color c = value <= 0.0001f ? HudStyle.Danger : value < 0.25f ? HudStyle.Danger : value < 0.5f ? HudStyle.Warning : HudStyle.Success;
            HudStyle.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(value), bar.height), c);
            string text = UnitFormatter.Number(value * 100.0, 0) + " %";
            if (ratePerSecond > 0.0) text += "  −" + UnitFormatter.Number(ratePerSecond * 6000.0, 1) + Loc.T("/мин");
            GUI.Label(new Rect(bar.xMax + 6f, r.y, 90f, r.height), text,
                HudStyle.Style(HudStyle.Face.Mono, 10, ratePerSecond > 0.0 ? HudStyle.Warning : HudStyle.TextPanel, TextAnchor.MiddleRight));
        }
    }
}
