using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Small radio-link indicator of a remote-controlled vehicle (no crew on board): signal bars, the link margin and the light time
    /// to the ground station on the home world (<see cref="CommLinkStatus"/>, <see cref="PartPhysics.SignalStrength"/>; range from
    /// the vehicle's antennas, <see cref="VehicleCapabilities.CommRange"/>). Out of range it only WARNS — the controls keep working
    /// (losing control would strand the existing interplanetary probe flights).
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class CommLinkOverlay : MonoBehaviour
    {
        FlightBody body;
        FlightStateController state;
        PlanetRuntime planet;
        bool wasConnected = true;
        float nextCheck;

        public CommLinkStatus Link { get; private set; }
        /// <summary>The indicator is for this vehicle (it has no crewed cabin).</summary>
        public bool Applies => body != null && body.DesignCapabilities.CrewedCabins == 0;

        public void Initialize(FlightBody flightBody, FlightStateController flightState, PlanetRuntime planetRuntime)
        {
            body = flightBody;
            state = flightState;
            planet = planetRuntime;
        }

        /// <summary>Distance from the vehicle to the home world's surface station now [m].</summary>
        public double StationDistance()
        {
            var sim = body.Simulation;
            if (sim == null) return 0.0;
            var nav = sim.Navigation;
            if (nav != null)
            {
                int home = nav.IndexOf(HomeSystemCatalog.HomeWorldId);
                if (home >= 0)
                {
                    double t = sim.State.Time;
                    Vector3d vehicle = sim.State.Position + nav.Position(sim.FrameBody, t);
                    Vector3d station = nav.Position(home, t);
                    return CommLinkStatus.StationDistance((vehicle - station).Magnitude, nav[home].Radius);
                }
            }
            // No navigation model: the active body is taken as home.
            return CommLinkStatus.StationDistance((sim.State.Position - planet.Model.Position).Magnitude, planet.Model.Radius);
        }

        void Update()
        {
            if (body == null || body.Simulation == null || planet == null || planet.Model == null) return;
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.25f;
            Link = new CommLinkStatus(body.Capabilities.CommRange, StationDistance());
            if (!Applies) return;
            if (wasConnected && !Link.Connected && state != null)
                state.Notify(Loc.T("Связь с Четвёртой планетой потеряна: аппарат вне дальности антенн. Управление пока сохраняется."));
            else if (!wasConnected && Link.Connected && state != null)
                state.Notify(Loc.T("Связь с Четвёртой планетой восстановлена."));
            wasConnected = Link.Connected;
        }

        void OnGUI()
        {
            if (!Applies || FlightInput.PauseMenuOpen || FlightInput.MapOpen || FlightInput.ModalWindowOpen) return;
            if (body.VisualHidden || body.Destroyed) return;
            HudStyle.Begin();
            Vector2 screen = HudStyle.VirtualScreen;
            float w = 300f, h = 66f; // wide enough for "задержка света … (туда-обратно …)" with millisecond delays
            float y = Mathf.Min(430f, screen.y - 560f - h - 8f);
            if (y < 8f) y = 8f;
            var r = new Rect(screen.x - w - 14f, y, w, h);
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            CommLinkStatus link = Link;
            Color title = !link.Connected ? HudStyle.Danger : link.Weak ? HudStyle.Warning : HudStyle.Accent;
            Color back = GUI.color;
            // Bars.
            for (int i = 0; i < 4; i++)
            {
                float bh = 6f + 4f * i;
                var bar = new Rect(r.x + 10f + i * 7f, r.y + 28f - bh, 5f, bh);
                GUI.color = i < link.Bars ? title : new Color(1f, 1f, 1f, 0.15f);
                GUI.DrawTexture(bar, Texture2D.whiteTexture);
            }
            GUI.color = back;
            string head = !link.Connected ? Loc.T("НЕТ СВЯЗИ") : link.Weak ? Loc.T("СЛАБАЯ СВЯЗЬ") : Loc.T("СВЯЗЬ");
            GUI.Label(new Rect(r.x + 46f, r.y + 6f, w - 52f, 20f), "<color=#" + ColorUtilityHex(title) + ">" + head + "</color>", HudStyle.HintRichLeft);
            string signal = link.Range <= 0.0 ? Loc.T("нет антенн")
                : !link.Connected ? Loc.T("сигнал ") + UnitFormatter.Number(link.Strength * 100.0, link.Strength < 0.01 ? 2 : 0) + Loc.T(" % от минимума")
                : double.IsPositiveInfinity(link.Margin) || link.Margin > 1e4 ? Loc.T("запас сигнала > 10⁴")
                : Loc.T("запас сигнала ×") + UnitFormatter.Number(link.Margin, link.Margin < 10.0 ? 1 : 0);
            GUI.Label(new Rect(r.x + 10f, r.y + 30f, w - 20f, 16f), signal + " · " + Distance(link.Distance) + " / " + Distance(link.Range), HudStyle.Small);
            GUI.Label(new Rect(r.x + 10f, r.y + 46f, w - 20f, 16f), Loc.T("задержка света ") + Delay(link.OneWayDelay) + Loc.T(" (туда-обратно ") + Delay(link.RoundTripDelay) + ")", HudStyle.Small);
        }

        static string ColorUtilityHex(Color c)
        {
            int R = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255), G = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255), B = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return R.ToString("X2") + G.ToString("X2") + B.ToString("X2");
        }

        static string Distance(double m)
        {
            if (m < 1000.0) return UnitFormatter.Number(m, 0) + Loc.T(" м");
            if (m < 1e7) return UnitFormatter.Number(m / 1000.0, 0) + Loc.T(" км");
            return UnitFormatter.Number(m / 1e9, 2) + Loc.T(" млн км");
        }

        static string Delay(double s)
        {
            if (s < 1e-3) return UnitFormatter.Number(s * 1e6, 0) + Loc.T(" мкс");
            if (s < 1.0) return UnitFormatter.Number(s * 1000.0, 1) + Loc.T(" мс");
            if (s < 120.0) return UnitFormatter.Number(s, 1) + Loc.T(" с");
            return UnitFormatter.Number(s / 60.0, 1) + Loc.T(" мин");
        }
    }
}
