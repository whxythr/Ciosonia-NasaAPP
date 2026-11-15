using SpaceSim.Localization;
using System.Globalization;
using SpaceSim.Units;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Test tool for Phase 1 (there is no engine yet): set the body's position and velocity directly and watch
    /// what gravity and drag do with it. It deliberately offers no "circularize" or "orbit" button — the player
    /// enters numbers they computed themselves (e.g. v = √(μ/r) from the Flight Manual).
    /// </summary>
    [DefaultExecutionOrder(210)]
    public sealed class InitialConditionsPanel : MonoBehaviour
    {
        const int WindowId = 0x53494350; // "SICP": unique among the project's IMGUI windows

        FlightBody body;
        PlanetRuntime planet;
        FlightStateController state;
        bool open;

        public bool IsOpen => open;
        Rect window = new Rect(390, 120, 380, 380);

        string lat = "", lon = "", alt = "0", vEast = "0", vNorth = "0", vUp = "0";
        bool relativeToSurface = true;
        string error = "";

        public void Initialize(FlightBody flightBody, PlanetRuntime planetRuntime, FlightStateController flightState)
        {
            body = flightBody;
            planet = planetRuntime;
            state = flightState;
            var site = planet.Definition.launchSite;
            lat = site.latitudeDegrees.ToString("0.####", CultureInfo.InvariantCulture);
            lon = site.longitudeDegrees.ToString("0.####", CultureInfo.InvariantCulture);
        }

        public void Toggle()
        {
            // Team mission: the test tools move the shared rocket, so they are the pilot's.
            if (!open && !SpaceSim.Runtime.Multiplayer.CrewGate.Allows(SpaceSim.Multiplayer.CrewAction.FlyRocket, state)) return;
            open = !open;
            if (!open) GUIUtility.keyboardControl = 0;
        }

        void OnGUI()
        {
            if (!open || body == null || FlightInput.PauseMenuOpen) return;
            HudStyle.Begin();
            // Fixed IMGUI window id (one panel per scene): GetInstanceID() is a compile error from Unity 6.2 on.
            window = GUILayout.Window(WindowId, window, Draw, Loc.T("Начальные условия (тестовый инструмент)"));
        }

        void Draw(int id)
        {
            GUILayout.Label(Loc.T("Двигателя нет: аппарат получает только заданное состояние, дальше — гравитация и сопротивление."), HudStyle.Small);
            GUILayout.Space(4);
            Field(Loc.T("Широта [°]"), ref lat);
            Field(Loc.T("Долгота [°]"), ref lon);
            Field(Loc.T("Высота над R [м]"), ref alt);
            GUILayout.Space(4);
            Field(Loc.T("v восток [м/с]"), ref vEast);
            Field(Loc.T("v север [м/с]"), ref vNorth);
            Field(Loc.T("v вверх [м/с]"), ref vUp);
            relativeToSurface = GUILayout.Toggle(relativeToSurface,
                relativeToSurface ? Loc.T(" Скорость относительно вращающейся поверхности") : Loc.T(" Скорость инерциальная (без вращения планеты)"));

            var t = state.Telemetry;
            GUILayout.Label(Loc.T("Подсказка: на текущей широте поверхность движется на восток со скоростью ") +
                            UnitFormatter.Number(planet.Model.Rotation.AngularSpeed * planet.Model.Radius * System.Math.Cos(t.Latitude), 1) +
                            Loc.T(" м/с (ω·R·cos φ)."), HudStyle.Small);

            if (!string.IsNullOrEmpty(error))
            {
                var c = GUI.color;
                GUI.color = HudStyle.Danger;
                GUILayout.Label(error, HudStyle.Small);
                GUI.color = c;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Применить"), HudStyle.Button)) Apply();
            if (GUILayout.Button(Loc.T("Сброс на площадку"), HudStyle.Button))
            {
                body.ResetToLaunchSite();
                state.Notify(Loc.T("Аппарат возвращён на стартовую площадку."));
            }
            if (GUILayout.Button(Loc.T("Закрыть"), HudStyle.Button)) Toggle();
            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        static void Field(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, HudStyle.Label, GUILayout.Width(150));
            value = GUILayout.TextField(value, HudStyle.Field);
            GUILayout.EndHorizontal();
        }

        void Apply()
        {
            if (!Parse(lat, out double la) || !Parse(lon, out double lo) || !Parse(alt, out double h) ||
                !Parse(vEast, out double ve) || !Parse(vNorth, out double vn) || !Parse(vUp, out double vu))
            {
                error = Loc.T("Неверное число (используйте точку как десятичный разделитель).");
                return;
            }
            if (la < -90.0 || la > 90.0)
            {
                error = Loc.T("Широта должна быть в диапазоне −90…90°.");
                return;
            }
            error = "";
            body.ApplyInitialConditions(la, lo, h, ve, vn, vu, relativeToSurface);
            GUIUtility.keyboardControl = 0;
            state.Notify(Loc.T("Заданы начальные условия: h = ") + UnitFormatter.Number(h, 0) + Loc.T(" м, v(E,N,Up) = (") +
                         UnitFormatter.Number(ve, 1) + ", " + UnitFormatter.Number(vn, 1) + ", " + UnitFormatter.Number(vu, 1) + Loc.T(") м/с") +
                         (relativeToSurface ? Loc.T(" отн. поверхности.") : Loc.T(" инерциально.")));
        }

        static bool Parse(string s, out double v) =>
            double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) &&
            !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
