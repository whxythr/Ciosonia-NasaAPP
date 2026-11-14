using SpaceSim.Localization;
using SpaceSim.Navigation;
using SpaceSim.Units;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Compact flight-view readout (left edge, below the middle) while time is warped to a point or a node, or a burn is being
    /// executed: ETA, time and distance left, speeds, the node direction and the remaining Δv, with a cancel button.
    /// The full editor lives on the map (<see cref="OrbitalMapView"/>).
    /// </summary>
    public sealed class ManeuverOverlay : MonoBehaviour
    {
        SpaceSimBootstrap boot;
        NavigationService nav;

        public void Initialize(SpaceSimBootstrap bootstrap, NavigationService navigation)
        {
            boot = bootstrap;
            nav = navigation;
        }

        void OnGUI()
        {
            if (boot == null || nav == null || nav.Maneuvers == null || FlightInput.PauseMenuOpen || FlightInput.MapOpen) return;
            ManeuverController m = nav.Maneuvers;
            bool burning = m.Mode != BurnMode.None;
            if (!m.Warping && !burning) return;
            GUI.depth = 6;
            HudStyle.Begin();
            Vector2 screen = HudStyle.VirtualScreen;
            const float w = 300f;
            float h = m.Warping ? 150f : 0f;
            if (burning) h += 96f;
            var r = new Rect(24f, screen.y * 0.42f, w, h);
            HudStyle.Card(r, 0.88f);
            float x = r.x + 12f, y = r.y + 8f, iw = w - 24f;
            GUIStyle k = HudStyle.Style(HudStyle.Face.Body, 12, HudStyle.TextPanel);
            GUIStyle v = HudStyle.Style(HudStyle.Face.Mono, 12, HudStyle.TextStrong, TextAnchor.MiddleRight);
            if (m.Warping)
            {
                WarpReadout(m, x, ref y, iw, k, v);
                if (GUI.Button(new Rect(x, y + 2f, iw, 24f), Loc.T("ОТМЕНИТЬ УСКОРЕНИЕ") + "  " + FlightInput.KeyLabel(InputKey.WarpToTarget), HudStyle.Button)) m.CancelWarp();
                y += 32f;
            }
            if (burning)
            {
                NodeGuidance g = nav.Guidance;
                GUI.Label(new Rect(x, y, iw, 14f), Loc.T("МАНЁВР · ") + m.BurnStatus.ToUpperInvariant(), HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.Blue));
                y += 16f;
                Row(x, ref y, iw, Loc.T("Осталось Δv"), UnitFormatter.Number(g.Remaining, 1) + Loc.T(" м/с"), k, v);
                Row(x, ref y, iw, Loc.T("Тангаж / курс узла"), UnitFormatter.Number(g.Pitch, 1) + "° / " + UnitFormatter.Number(g.Heading, 1) + "°", k, v);
                if (GUI.Button(new Rect(x, y + 2f, iw, 24f), Loc.T("ПРЕРВАТЬ МАНЁВР"), HudStyle.Button)) m.AbortBurn();
            }
        }

        /// <summary>ETA (simulation clock), time and distance left, speed now and at the point.</summary>
        public static void WarpReadout(ManeuverController m, float x, ref float y, float w, GUIStyle k, GUIStyle v)
        {
            WarpTarget t = m.Warp;
            GUI.Label(new Rect(x, y, w, 14f), Loc.T("УСКОРЕНИЕ ДО: ") + t.Label.ToUpperInvariant(), HudStyle.Style(HudStyle.Face.Mono, 10, HudStyle.Blue));
            y += 16f;
            Row(x, ref y, w, Loc.T("Прибытие (часы симуляции)"), UnitFormatter.Duration(t.Time), k, v);
            Row(x, ref y, w, Loc.T("Осталось времени"), UnitFormatter.Duration(m.WarpRemaining), k, v);
            double d = m.DistanceToWarpPoint();
            if (!double.IsNaN(d)) Row(x, ref y, w, Loc.T("До точки"), NavigationUI.Dist(d), k, v);
            Row(x, ref y, w, Loc.T("Скорость сейчас"), UnitFormatter.Number(m.CurrentSpeed(), 1) + Loc.T(" м/с"), k, v);
            if (t.HasPoint) Row(x, ref y, w, Loc.T("Скорость в точке"), UnitFormatter.Number(t.Point.Velocity.Magnitude, 1) + Loc.T(" м/с"), k, v);
        }

        static void Row(float x, ref float y, float w, string key, string value, GUIStyle k, GUIStyle v)
        {
            GUI.Label(new Rect(x, y, w * 0.6f, 18f), key, k);
            GUI.Label(new Rect(x + w * 0.35f, y, w * 0.65f, 18f), value, v);
            y += 19f;
        }
    }
}
