using System;
using System.Collections.Generic;
using SpaceSim.Economy;
using SpaceSim.Flight;
using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Planets;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using UnityEngine;
using Face = SpaceSim.Runtime.HudStyle.Face;

namespace SpaceSim.Runtime
{
    // The 2026-11 instrument layout (mock-up "FLT 13"): the bigger a value is drawn, the more it matters.
    //   1st — mission clock, altitude, velocity, throttle (big condensed numbers);
    //   2nd — heading tape, flight phase, stage resources, trajectory, staging, Δv (medium mono);
    //   3rd — the full telemetry of the training mode (small mono columns under the ladders), key hints.
    // Every value is the same SI telemetry as before; only its place and size changed.
    public sealed partial class FlightHUD
    {
        const float M = 36f;            // screen margin
        const float StripHeight = 150f; // bottom instrument strip
        const float RowH = 15f;         // small telemetry rows

        static Texture2D ring;
        static double launchTime;
        static int launchFlight;
        SpaceSim.Flight.RocketState launchedRocket;
        double lastTrackTime = double.NaN;
        float stripTop, actionsTop;
        float BannerTop => navigation != null && (navigation.Guidance.Active || (navigation.Prediction != null && navigation.Plan.Target >= 0)) ? 196f : 104f;

        // ─────────────────────────────── flight bookkeeping (crew, mission clock) ───────────────────────────────

        /// <summary>Number of the flight in the air (0 = on the pad, nothing launched yet).</summary>
        public int FlightNumber => session != null && session.Profile.crew.activeFlight == launchFlight ? launchFlight : 0;

        /// <summary>"FLT 13 · Стартовая ракета".</summary>
        public string FlightTitle => (FlightNumber > 0 ? "FLT " + FlightNumber + "  ·  " : "") + RocketName;

        /// <summary>"T+ 00:03:12" since ignition on the pad ("" before launch).</summary>
        public string MissionClock => FlightNumber > 0 ? "T+ " + Clock(state.Telemetry.Time - launchTime) : "";

        string RocketName
        {
            get
            {
                string n = session != null && session.Profile.blueprint != null ? session.Profile.blueprint.name : null;
                return string.IsNullOrEmpty(n) ? Loc.T("Ракета") : Loc.T(n);
            }
        }

        static string Clock(double seconds)
        {
            if (double.IsNaN(seconds) || seconds < 0.0) seconds = 0.0;
            long s = (long)seconds;
            long d = s / 86400;
            return (d > 0 ? d + Loc.T(" д ") : "") + ((s / 3600) % 24).ToString("00") + ":" + ((s / 60) % 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        /// <summary>Seats of the crewed cabin of the vehicle (0 = probe / remote control).</summary>
        int Seats()
        {
            RocketModel m = body.Model;
            PartPlacement p = m != null && m.Layout != null ? CabinRules.CrewedCabin(m.Layout.Parts) : null;
            ModuleDefinition def = p != null ? ModuleCatalog.Find(p.ModuleId) : null;
            return def != null ? def.crewCapacity : 0;
        }

        /// <summary>Ignition on the pad starts a flight (crew boards); the crew logs time above the space boundary.</summary>
        void TrackFlight()
        {
            if (session == null || session.ReadOnly) return;
            FlightTelemetry t = state.Telemetry;
            var r = body.Rocket;
            if (r != null && r.IsBurning && body.IsOnLaunchPad && !body.UsedTestTool && launchedRocket != r)
            {
                launchedRocket = r;
                launchTime = t.Time;
                launchFlight = session.BeginCrewFlight(Seats());
                int aboard = SpaceSim.Crew.CrewRoster.Aboard(session.Profile.crew).Count;
                state.Notify("FLT " + launchFlight + Loc.T(" — пуск. ") + (aboard > 0 ? Loc.T("На борту: ") + aboard + Loc.T(" (Tab — экипаж).") : Loc.T("Без экипажа.")));
            }
            if (FlightNumber > 0 && t.AboveSpaceBoundary && !double.IsNaN(lastTrackTime))
                session.AddCrewTimeInSpace(Math.Max(0.0, t.Time - lastTrackTime));
            lastTrackTime = t.Time;
        }

        // ─────────────────────────────── pause screen data ───────────────────────────────

        public PlayerProfile Profile => session != null ? session.Profile : null;

        public void RevertToLaunch() => newRocket?.Invoke();

        public void RevertToAssembly()
        {
            var boot = GetComponentInParent<SpaceSimBootstrap>();
            if (boot != null) boot.RevertToAssembly();
            else newRocket?.Invoke();
        }

        public SpaceSim.Runtime.Menu.PauseSummary PauseSummary()
        {
            var s = new SpaceSim.Runtime.Menu.PauseSummary();
            if (state == null || planet == null || planet.Model == null) return s;
            FlightTelemetry t = state.Telemetry;
            var r = body.Rocket;
            string where = Loc.T(planet.Definition.displayName);
            s.BodyName = where;
            s.Eyebrow = (FlightNumber > 0 ? Loc.T("ПОЛЁТ") + "  ·  FLT " + FlightNumber : Loc.T("НА СТАРТЕ")) + "  ·  " + where.ToUpperInvariant();
            s.Title = RocketName;
            s.Clock = MissionClock;
            double alt = Math.Max(0.0, t.AltitudeAboveSurface);
            s.Subtitle = body.Destroyed ? RocketName + Loc.T(" потеряна.")
                : body.Simulation.IsLanded ? (body.IsOnLaunchPad ? RocketName + Loc.T(" стоит на стартовой площадке.") : RocketName + Loc.T(" на поверхности: ") + where + ".")
                : RocketName + Loc.T(" держится на высоте ") + BigAltitude(alt, out string u1) + " " + u1.ToLowerInvariant() + ".";
            s.Altitude = BigAltitude(alt, out s.AltitudeUnit);
            s.Velocity = UnitFormatter.Number(Speed(t), 0);
            s.VelocityUnit = Loc.T("м/с");
            if (r != null)
                s.Rows.Add(new KeyValuePair<string, string>(Loc.T("Ступень"), r.HasStages ? (r.StageIndex + 1) + Loc.T(" из ") + r.StageCount : Loc.T("только зонд")));
            if (r != null) s.Rows.Add(new KeyValuePair<string, string>(Loc.T("Δv идеальн. остаток"), UnitFormatter.Number(r.DeltaVRemaining(), 0) + Loc.T(" м/с")));
            if (session != null)
            {
                int aboard = SpaceSim.Crew.CrewRoster.Aboard(session.Profile.crew).Count;
                s.Rows.Add(new KeyValuePair<string, string>(Loc.T("Экипаж на борту"), aboard > 0 ? aboard.ToString() : Loc.T("нет")));
                s.Rows.Add(new KeyValuePair<string, string>(Loc.T("Исследования"), UnitFormatter.Number(session.Profile.researchPoints, 0) + Loc.T(" очк.")));
                s.Rows.Add(new KeyValuePair<string, string>(Loc.T("Кредиты"), UnitFormatter.Number(session.Profile.credits, 0) + Loc.T(" кр")));
                foreach (MilestoneDefinition m in ResearchMilestones.All)
                    s.Goals.Add(new KeyValuePair<string, bool>(m.Name, session.Profile.HasMilestone(m.Id)));
            }
            s.RevertCost = FlightNumber > 0 ? Loc.T("ПОЛЁТ ") + MissionClock + Loc.T(" БУДЕТ ПОТЕРЯН") : "";
            return s;
        }

        /// <summary>The headline speed: relative to the ground in the air, inertial in space.</summary>
        double Speed(FlightTelemetry t) => SurfaceFrame ? t.SurfaceSpeed : t.InertialSpeed;

        bool SurfaceFrame
        {
            get
            {
                FlightPhase p = state.Phase;
                return p == FlightPhase.Ground || p == FlightPhase.AtmosphericAscent || p == FlightPhase.AtmosphericDescent || p == FlightPhase.Landing;
            }
        }

        static string BigAltitude(double metres, out string unit)
        {
            if (metres < 1.0e5)
            {
                unit = Loc.T("М · НАД ПОВЕРХН.");
                return UnitFormatter.Number(metres, 0);
            }
            unit = Loc.T("КМ · НАД ПОВЕРХН.");
            return UnitFormatter.Number(metres / 1000.0, metres < 1.0e7 ? 1 : 0);
        }

        // ─────────────────────────────── OnGUI ───────────────────────────────

        void OnGUI()
        {
            if (state == null || planet.Model == null || FlightInput.PauseMenuOpen) return; // the pause / crew screen covers the view
            if (hangar != null && hangar.IsOpen) return; // inside the hangar its own panels replace the instrument panel
            HudStyle.Begin();
            Vector2 screen = HudStyle.VirtualScreen;
            if (map != null && map.IsOpen)
            {
                DrawNotifications(screen, screen.y - 120f); // the map draws its own panel and warp scale
                return;
            }
            if ((InCockpit && !visibleInCockpit) || !visible)
            {
                if (InCockpit) DrawNotifications(screen, screen.y - 60f);
                HudStyle.KeyHint(M, 14f, FlightInput.KeyLabel(InputKey.H), Loc.T("ПОКАЗАТЬ ПРИБОРЫ"));
                return;
            }
            FlightTelemetry t = state.Telemetry;
            stripTop = screen.y - 44f - StripHeight;
            actionsTop = stripTop - 54f;

            if (system.SpaceUnlocked && showMarkers) DrawBodyMarkers();
            DrawMissionClock(t);
            DrawHeadingTape(screen);
            DrawAlerts(t, screen);
            DrawAltitude(t, screen);
            DrawVelocity(t, screen);
            DrawDetails(t, screen);
            DrawStrip(t, screen);
            DrawActions(screen);
            DrawManeuverCard(screen);
            DrawNotifications(screen, actionsTop - 34f);
            DrawAcademyBanner(screen);
            if (system.SpaceUnlocked && showMap) DrawSystemMap(screen);
            DrawKeyLine(screen);
        }

        // ─────────────────────────────── 1st rank: clock, altitude, velocity ───────────────────────────────

        void DrawMissionClock(FlightTelemetry t)
        {
            string flt = (FlightNumber > 0 ? "FLT " + FlightNumber + "   ·   " : "") + RocketName.ToUpperInvariant();
            GUI.Label(new Rect(M, 22f, 600f, 18f), flt, HudStyle.Style(Face.Mono, 12, HudStyle.TextSoft));
            string clock = FlightNumber > 0 ? "T+ " + Clock(t.Time - launchTime) : body.IsOnLaunchPad ? "T− " + Loc.T("СТАРТ") : Clock(t.Time);
            GUI.Label(new Rect(M - 2f, 36f, 640f, 70f), clock, HudStyle.Style(Face.DisplayStrong, 60, HudStyle.TextStrong));
            GUI.Label(new Rect(M, 110f, 640f, 18f), PhaseName(state.Phase), HudStyle.Style(Face.Mono, 12, HudStyle.Blue));
            var reason = new GUIStyle(HudStyle.Small) { wordWrap = true };
            GUI.Label(new Rect(M, 128f, 440f, 32f), ReasonText(state.Reason), reason);
        }

        void DrawAltitude(FlightTelemetry t, Vector2 screen)
        {
            double alt = Math.Max(0.0, t.AltitudeAboveSurface);
            var ladder = new Rect(M, 200f, 160f, 330f);
            Ladder(ladder, alt, true);
            string value = BigAltitude(alt, out string unit);
            Rect box = ValueBox(new Rect(ladder.x + 36f, ladder.center.y - 38f, 168f, 76f), Loc.T("ВЫСОТА"), value, unit, HudStyle.Blue);

            // Time to contact: the old proximity gauge, now a line under the altitude box.
            double tti = t.TimeToImpactEstimate;
            if (!double.IsNaN(tti) && tti < 600.0 && !body.Simulation.IsLanded)
            {
                Color c = tti < 30.0 ? HudStyle.Red : tti < 120.0 ? HudStyle.Amber : HudStyle.TextSoft;
                GUI.Label(new Rect(box.x, box.yMax + 6f, 240f, 18f), Loc.T("ДО КОНТАКТА ≈ ") + UnitFormatter.Duration(tti), HudStyle.Style(Face.Mono, 12, c));
            }
        }

        void DrawVelocity(FlightTelemetry t, Vector2 screen)
        {
            double v = Speed(t);
            var ladder = new Rect(screen.x - M - 160f, 200f, 160f, 330f);
            Ladder(ladder, v, false);
            string unit = SurfaceFrame ? Loc.T("М/С · ОТН. ПОВЕРХН.") : Loc.T("М/С · ИНЕРЦИАЛЬНАЯ");
            ValueBox(new Rect(ladder.xMax - 36f - 168f, ladder.center.y - 38f, 168f, 76f), Loc.T("СКОРОСТЬ"), UnitFormatter.Number(v, 0), unit, HudStyle.Blue);
        }

        /// <summary>The boxed big value on a ladder ("ALTITUDE 42,315 METRES, ASL").</summary>
        static Rect ValueBox(Rect r, string label, string value, string unit, Color accent)
        {
            HudStyle.Fill(r, new Color(0.04f, 0.04f, 0.045f, 0.82f));
            HudStyle.Frame(r, accent);
            GUI.Label(new Rect(r.x + 10f, r.y + 6f, r.width - 20f, 14f), label, HudStyle.Style(Face.Mono, 10, accent));
            GUI.Label(new Rect(r.x + 9f, r.y + 18f, r.width - 18f, 36f), value, HudStyle.Style(Face.Mono, 28, HudStyle.TextStrong));
            GUI.Label(new Rect(r.x + 10f, r.y + 56f, r.width - 20f, 14f), unit, HudStyle.Style(Face.Mono, 9, HudStyle.TextMuted));
            return r;
        }

        /// <summary>A vertical tape around <paramref name="value"/>: minor and labelled major ticks on a hairline.</summary>
        static void Ladder(Rect r, double value, bool left)
        {
            double span = Math.Max(200.0, Math.Abs(value) * 0.16);
            double major = NiceStep(span / 4.0), minor = major / 5.0;
            float lineX = left ? r.x + 4f : r.xMax - 4f;
            HudStyle.Fill(new Rect(lineX, r.y, 1f, r.height), new Color(1f, 1f, 1f, 0.35f));
            double lo = value - span * 0.5, hi = value + span * 0.5;
            GUIStyle label = HudStyle.Style(Face.Mono, 10, HudStyle.TextMuted, left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight);
            for (double v = Math.Ceiling(lo / minor) * minor; v <= hi; v += minor)
            {
                if (v < 0.0) continue;
                float y = r.center.y - (float)((v - value) / span) * r.height;
                bool isMajor = Math.Abs(v / major - Math.Round(v / major)) < 1e-6;
                float len = isMajor ? 14f : 7f;
                HudStyle.Fill(new Rect(left ? lineX : lineX - len, y, len, 1f), new Color(1f, 1f, 1f, isMajor ? 0.55f : 0.25f));
                if (isMajor)
                    GUI.Label(left ? new Rect(lineX + 18f, y - 8f, 60f, 16f) : new Rect(lineX - 78f, y - 8f, 60f, 16f), TickLabel(v, major), label);
            }
        }

        static double NiceStep(double raw)
        {
            double p = Math.Pow(10.0, Math.Floor(Math.Log10(Math.Max(raw, 1e-9))));
            double f = raw / p;
            return (f < 1.5 ? 1.0 : f < 3.5 ? 2.0 : f < 7.5 ? 5.0 : 10.0) * p;
        }

        static string TickLabel(double v, double step)
        {
            if (v >= 1.0e6) return UnitFormatter.Number(v / 1.0e6, step >= 1.0e6 ? 0 : 1) + "M";
            if (v >= 1000.0) return UnitFormatter.Number(v / 1000.0, step >= 1000.0 ? 0 : 1) + "k";
            return UnitFormatter.Number(v, 0);
        }

        // ─────────────────────────────── 2nd rank: heading, alerts, strip ───────────────────────────────

        void DrawHeadingTape(Vector2 screen)
        {
            var r = body.Rocket;
            if (r == null) return;
            const float w = 560f;
            float x0 = (screen.x - w) * 0.5f, y = 40f;
            double hdg = r.HeadingDegrees;
            HudStyle.Fill(new Rect(x0, y, w, 1f), new Color(1f, 1f, 1f, 0.45f));
            GUIStyle label = HudStyle.Style(Face.Mono, 10, HudStyle.TextMuted, TextAnchor.MiddleCenter);
            for (int d = (int)Math.Floor(hdg - 30.0); d <= hdg + 30.0; d++)
            {
                if (d % 2 != 0) continue;
                float x = x0 + w * 0.5f + (float)((d - hdg) / 60.0) * w;
                bool major = d % 10 == 0;
                HudStyle.Fill(new Rect(x, y, 1f, major ? 10f : 5f), new Color(1f, 1f, 1f, major ? 0.6f : 0.3f));
                if (major) GUI.Label(new Rect(x - 20f, y - 18f, 40f, 14f), (((d % 360) + 360) % 360).ToString("000"), label);
            }
            float cx = x0 + w * 0.5f;
            GUI.Label(new Rect(cx - 10f, y + 6f, 20f, 16f), "▲", HudStyle.Style(Face.Body, 13, HudStyle.Blue, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(cx - 150f, y + 22f, 300f, 16f),
                Loc.T("КУРС ") + UnitFormatter.Number(hdg, 0).PadLeft(3, '0') + "   ·   " + Loc.T("ТАНГАЖ ") + UnitFormatter.Number(r.PitchDegrees, 0) + "°",
                HudStyle.Style(Face.Mono, 12, HudStyle.Blue, TextAnchor.MiddleCenter));
        }

        void DrawAlerts(FlightTelemetry t, Vector2 screen)
        {
            var lines = new List<KeyValuePair<string, Color>>();
            var r = body.Rocket;
            if (simulation.Paused) lines.Add(Line(Loc.T("ПАУЗА СИМУЛЯЦИИ"), HudStyle.Amber));
            if (body.Destroyed) lines.Add(Line(Loc.T("АППАРАТ ПОТЕРЯН · ") + FlightInput.KeyLabel(InputKey.R) + Loc.T(" — НОВАЯ РАКЕТА"), HudStyle.Red));
            if (r != null && r.HasStages && r.CurrentStage.propellantMass > 0.0 && !body.Destroyed)
            {
                double fuel = r.CurrentPropellant / r.CurrentStage.propellantMass;
                if (fuel <= 0.0) lines.Add(Line(Loc.T("ТОПЛИВО СТУПЕНИ КОНЧИЛОСЬ · ") + FlightInput.KeyLabel(InputKey.Space) + Loc.T(" — ОТДЕЛИТЬ"), HudStyle.Red));
                else if (fuel < 0.2 && r.IsBurning) lines.Add(Line(Loc.T("ТОПЛИВО СТУПЕНИ ") + UnitFormatter.Number(fuel * 100.0, 0) + Loc.T("% · СКОРО ОТДЕЛЕНИЕ"), HudStyle.Amber));
            }
            HazardExposure hz = body.Condition.LastExposure;
            if (hz.RadiationExcess > 0) lines.Add(Line(Loc.T("РАДИАЦИЯ ") + hz.Radiation + Loc.T(" · ЗАЩИТА ") + body.DesignCapabilities.RadiationShielding, HudStyle.Red));
            if (hz.AcidExcess > 0) lines.Add(Line(Loc.T("КИСЛОТА ") + hz.Acid + Loc.T(" · ЗАЩИТА ") + body.DesignCapabilities.AcidResistance, HudStyle.Red));
            if (body.Condition.Integrity < 1.0)
                lines.Add(Line(Loc.T("ОБОРУДОВАНИЕ ") + UnitFormatter.Number(body.Condition.Integrity * 100.0, 0) + "%" +
                               (body.Condition.InstrumentsOnline ? "" : Loc.T(" · ПРИБОРЫ ОТКЛЮЧЕНЫ")), body.Condition.InstrumentsOnline ? HudStyle.Amber : HudStyle.Red));
            var heat = body.Simulation.Systems;
            if (heat != null && heat.AllowedHeatFlux > 0.0 && heat.HeatFlux > 0.6 * heat.AllowedHeatFlux)
                lines.Add(Line(Loc.T("НАГРЕВ ") + UnitFormatter.Number(100.0 * heat.HeatFlux / heat.AllowedHeatFlux, 0) + Loc.T("% ДОПУСТИМОГО"), HudStyle.Amber));
            if (state.Phase == FlightPhase.OrbitalFlight) lines.Add(Line(Loc.T("ОРБИТА"), HudStyle.Green));
            else if (t.AboveSpaceBoundary) lines.Add(Line(Loc.T("В КОСМОСЕ"), HudStyle.Green));
            var sim = body.Simulation;
            if (sim.Navigation != null && sim.Primary != sim.FrameBody)
                lines.Add(Line(Loc.T("ПРИТЯГИВАЕТ: ") + sim.Navigation.NameOf(sim.Primary).ToUpperInvariant(), HudStyle.Blue));
            if (session != null && session.ReadOnly) lines.Add(Line(Loc.T("ПРОФИЛЬ ТОЛЬКО ДЛЯ ЧТЕНИЯ"), HudStyle.Amber));

            float y = 22f, right = screen.x - M;
            GUIStyle s = HudStyle.Style(Face.Mono, 12, HudStyle.Text, TextAnchor.MiddleRight);
            foreach (KeyValuePair<string, Color> l in lines)
            {
                s = HudStyle.Style(Face.Mono, 12, l.Value, TextAnchor.MiddleRight);
                float w = s.CalcSize(new GUIContent(l.Key)).x;
                HudStyle.Fill(new Rect(right - w - 16f, y + 5f, 8f, 8f), l.Value);
                GUI.Label(new Rect(right - w - 2f, y, w + 2f, 18f), l.Key, s);
                y += 20f;
            }
            // Economy and view settings: small, they rarely matter mid-flight.
            string money = session != null ? Loc.T("КРЕДИТЫ ") + UnitFormatter.Number(session.Profile.credits, 0) + Loc.T("  ·  ИССЛ. ") +
                                             UnitFormatter.Number(session.Profile.researchPoints, 0) : "";
            GUI.Label(new Rect(right - 600f, y + 4f, 600f, 14f), money, HudStyle.Style(Face.Mono, 10, HudStyle.TextMuted, TextAnchor.MiddleRight));
            string cam = (cameraController.Focus == SpaceCameraController.FocusTarget.Planet ? Loc.T("камера: планета") : Loc.T("камера: аппарат")) +
                         (cameraController.Frame == SpaceCameraController.ReferenceFrame.Inertial ? Loc.T(", инерциальная") : Loc.T(", местный горизонт")) +
                         Loc.T("  ·  шаг ") + UnitFormatter.Number(simulation.FixedStep, 3) + " s  ·  " + Loc.T("звёзды ") + UnitFormatter.Number(stars.Visibility * 100.0, 0) + "%";
            GUI.Label(new Rect(right - 600f, y + 20f, 600f, 14f), cam, HudStyle.Style(Face.Mono, 10, HudStyle.TextDim, TextAnchor.MiddleRight));
            if (simulation.OnRails || simulation.RailsBlocked || simulation.WarpLimited)
            {
                string rails = simulation.OnRails ? Loc.T("на «рельсах»: точные кеплеровы дуги")
                    : simulation.RailsBlocked ? Loc.T("выше ×") + UnitFormatter.Number(simulation.Settings.maxNumericWarp, 0) + Loc.T(" нельзя: ") + body.RailsBlockedReason()
                    : Loc.T("ускорение ограничено производительностью");
                GUI.Label(new Rect(right - 600f, y + 36f, 600f, 14f), rails, HudStyle.Style(Face.Mono, 10, HudStyle.TextDim, TextAnchor.MiddleRight));
            }
        }

        static KeyValuePair<string, Color> Line(string text, Color c) => new KeyValuePair<string, Color>(text, c);

        /// <summary>The bottom strip: throttle · attitude · stage resources · trajectory · staging.</summary>
        void DrawStrip(FlightTelemetry t, Vector2 screen)
        {
            float w = screen.x - 2f * M;
            var strip = new Rect(M, stripTop, w, StripHeight);
            HudStyle.Fill(strip, new Color(0.035f, 0.035f, 0.04f, 0.86f));
            HudStyle.Frame(strip, new Color(1f, 1f, 1f, 0.16f));
            float[] parts = { 0.15f, 0.14f, 0.25f, 0.22f, 0.24f };
            float x = strip.x;
            var cells = new Rect[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                cells[i] = new Rect(x, strip.y, w * parts[i], strip.height);
                if (i > 0) HudStyle.Fill(new Rect(x, strip.y, 1f, strip.height), new Color(1f, 1f, 1f, 0.16f));
                x += w * parts[i];
            }
            var r = body.Rocket;
            DrawThrottle(cells[0], r);
            DrawAttitude(cells[1], r);
            DrawResources(cells[2], r, t);
            DrawTrajectory(cells[3], t);
            DrawStaging(cells[4], r, t);
        }

        static void CellLabel(Rect c, string text) =>
            GUI.Label(new Rect(c.x + 16f, c.y + 12f, c.width - 32f, 14f), text, HudStyle.Style(Face.Mono, 11, HudStyle.TextSoft));

        void DrawThrottle(Rect c, SpaceSim.Flight.RocketState r)
        {
            CellLabel(c, Loc.T("ТЯГА"));
            if (r == null || !r.HasStages)
            {
                GUI.Label(new Rect(c.x + 16f, c.y + 36f, c.width - 32f, 50f), "—", HudStyle.Style(Face.DisplayStrong, 46, HudStyle.TextDim));
                return;
            }
            double throttle = r.CanThrottle ? r.Throttle : 1.0;
            Color big = r.IsBurning ? HudStyle.Blue : HudStyle.TextMuted;
            GUI.Label(new Rect(c.x + 14f, c.y + 28f, c.width - 28f, 58f), UnitFormatter.Number(throttle * 100.0, 0) + "%", HudStyle.Style(Face.DisplayStrong, 50, big));
            // Bar of 24 ticks: lit up to the throttle.
            int n = 24;
            float bw = (c.width - 32f) / n;
            for (int i = 0; i < n; i++)
            {
                bool on = (i + 0.5f) / n <= throttle;
                float h = i % 6 == 5 ? 16f : 12f;
                HudStyle.Fill(new Rect(c.x + 16f + i * bw, c.y + 104f - h, Mathf.Max(1f, bw - 3f), h), on ? HudStyle.Blue : new Color(1f, 1f, 1f, 0.55f));
            }
            string engine = r.IsBurning ? (r.IsSolidBurning ? Loc.T("ТВЁРДОТОПЛ. · НЕ ВЫКЛ.") : Loc.T("РАБОТАЕТ")) :
                r.CanBurn ? Loc.T("ВЫКЛЮЧЕН") : r.CurrentStage.IsElectric && r.CurrentPropellant > 0.0 ? Loc.T("НЕТ ПИТАНИЯ") : Loc.T("ТОПЛИВО КОНЧИЛОСЬ");
            GUI.Label(new Rect(c.x + 16f, c.y + 110f, c.width - 32f, 14f), engine, HudStyle.Style(Face.Mono, 10, r.IsBurning ? HudStyle.Green : HudStyle.TextMuted));
            GUI.Label(new Rect(c.x + 16f, c.y + 126f, c.width - 32f, 14f),
                FlightInput.KeyLabel(InputKey.LeftShift) + "/" + FlightInput.KeyLabel(InputKey.LeftCtrl) + "  ·  " + FlightInput.KeyLabel(InputKey.Z) + " " +
                FlightInput.KeyLabel(InputKey.X), HudStyle.Style(Face.Mono, 10, HudStyle.TextDim));
        }

        void DrawAttitude(Rect c, SpaceSim.Flight.RocketState r)
        {
            float size = 76f;
            var ball = new Rect(c.center.x - size * 0.5f, c.y + 14f, size, size);
            GUI.DrawTexture(ball, Ring());
            if (r != null)
            {
                // Horizon line: nose up (pitch 90°) puts the horizon at the bottom of the ball.
                float off = Mathf.Clamp((float)(r.PitchDegrees / 90.0), -1f, 1f) * size * 0.42f;
                float half = Mathf.Sqrt(Mathf.Max(0f, size * size * 0.25f - off * off)) - 3f;
                HudStyle.Fill(new Rect(ball.center.x - half, ball.center.y + off, half * 2f, 1f), new Color(1f, 1f, 1f, 0.75f));
                HudStyle.Fill(new Rect(ball.center.x - 14f, ball.center.y - 1f, 28f, 2f), HudStyle.Blue);
                HudStyle.Fill(new Rect(ball.center.x - 1f, ball.center.y - 6f, 2f, 6f), HudStyle.Blue);
            }
            var s = body.Simulation.Systems;
            var chips = new List<KeyValuePair<string, bool>>();
            if (s != null && s.Capabilities.RcsThrust > 0.0) chips.Add(new KeyValuePair<string, bool>(Loc.T("РСУ"), s.RcsEnabled));
            if (s != null && s.DesignHasLegs) chips.Add(new KeyValuePair<string, bool>(Loc.T("ОПОРЫ"), s.LegsDeployed));
            if (s != null && s.ParachuteCount > 0)
                chips.Add(new KeyValuePair<string, bool>(Loc.T("ПАРАШ."), s.CanopiesIn(SpaceSim.Flight.CanopyState.Deployed) > 0));
            chips.Add(new KeyValuePair<string, bool>(Loc.T("ДВИГ."), r != null && r.IsBurning));
            float total = 0f;
            GUIStyle on = HudStyle.Style(Face.Mono, 10, HudStyle.Green), off2 = HudStyle.Style(Face.Mono, 10, HudStyle.TextMuted);
            foreach (var ch in chips) total += off2.CalcSize(new GUIContent(ch.Key)).x + 10f;
            float x = c.center.x - total * 0.5f;
            foreach (var ch in chips)
            {
                float cw = off2.CalcSize(new GUIContent(ch.Key)).x;
                GUI.Label(new Rect(x, c.y + 100f, cw + 2f, 14f), ch.Key, ch.Value ? on : off2);
                x += cw + 10f;
            }
            GUI.Label(new Rect(c.x + 8f, c.y + 126f, c.width - 16f, 14f),
                FlightInput.KeyLabel(InputKey.W) + "/" + FlightInput.KeyLabel(InputKey.S) + Loc.T(" ТАНГАЖ  ·  ") + FlightInput.KeyLabel(InputKey.A) + "/" +
                FlightInput.KeyLabel(InputKey.D) + Loc.T(" КУРС"), HudStyle.Style(Face.Mono, 10, HudStyle.TextDim, TextAnchor.MiddleCenter));
        }

        void DrawResources(Rect c, SpaceSim.Flight.RocketState r, FlightTelemetry t)
        {
            CellLabel(c, r != null && r.HasStages ? Loc.T("СТУПЕНЬ ") + (r.StageIndex + 1) + Loc.T(" · РЕСУРСЫ") : Loc.T("РЕСУРСЫ"));
            float y = c.y + 38f;
            if (r != null && r.HasStages && r.CurrentStage.propellantMass > 0.0)
            {
                double f = r.CurrentPropellant / r.CurrentStage.propellantMass;
                ResourceBar(c, ref y, Loc.T("Топливо"), f, UnitFormatter.Number(f * 100.0, 0) + "%  " + UnitFormatter.Number(r.CurrentPropellant / 1000.0, 1) + Loc.T(" т"));
            }
            if (r != null && r.Configuration.batteryEnergy > 0.0)
            {
                double f = r.BatteryEnergy / r.Configuration.batteryEnergy;
                ResourceBar(c, ref y, Loc.T("Заряд"), f, UnitFormatter.Number(f * 100.0, 0) + "%", HudStyle.Green);
            }
            if (r != null && r.RcsPropellantAvailable > 0.0)
                ResourceRow(c, ref y, Loc.T("РСУ, топливо"), UnitFormatter.Number(r.RcsPropellantAvailable, 1) + Loc.T(" кг"));
            ResourceBar(c, ref y, Loc.T("Оборудование"), body.Condition.Integrity, UnitFormatter.Number(body.Condition.Integrity * 100.0, 0) + "%", HudStyle.Green);
            if (r != null && ShowPower(r) && y < c.yMax - 18f)
            {
                SpaceSim.Physics.PowerBalance b = r.Electrics;
                double engineUse = r.IsBurning && r.CurrentStage != null && r.CurrentStage.IsElectric ? Math.Min(r.Throttle, r.PowerFraction) * r.CurrentStage.electricPowerDemand : 0.0;
                double net = b.NetGeneration - engineUse;
                ResourceRow(c, ref y, Loc.T("Питание"), (net >= 0.0 ? "+" : "−") + Power(Math.Abs(net)));
            }
            if (y < c.yMax - 18f) ResourceRow(c, ref y, Loc.T("Масса"), UnitFormatter.Number(body.Simulation.CurrentMass / 1000.0, 2) + Loc.T(" т"));
        }

        static void ResourceBar(Rect c, ref float y, string label, double fraction, string value, Color? full = null)
        {
            float f = Mathf.Clamp01((float)fraction);
            Color col = f < 0.1f ? HudStyle.Red : f < 0.25f ? HudStyle.Amber : full ?? HudStyle.TextStrong;
            GUI.Label(new Rect(c.x + 16f, y, 110f, 16f), label, HudStyle.Style(Face.Body, 12, HudStyle.TextPanel));
            float bx = c.x + 126f, bw = c.width - 126f - 110f;
            HudStyle.Fill(new Rect(bx, y + 7f, bw, 3f), new Color(1f, 1f, 1f, 0.12f));
            HudStyle.Fill(new Rect(bx, y + 7f, bw * f, 3f), col);
            GUI.Label(new Rect(c.xMax - 104f, y, 90f, 16f), value, HudStyle.Style(Face.Mono, 11, f >= 0.25f && full == null ? HudStyle.Text : col, TextAnchor.MiddleRight));
            y += 21f;
        }

        static void ResourceRow(Rect c, ref float y, string label, string value)
        {
            GUI.Label(new Rect(c.x + 16f, y, 140f, 16f), label, HudStyle.Style(Face.Body, 12, HudStyle.TextPanel));
            GUI.Label(new Rect(c.xMax - 190f, y, 176f, 16f), value, HudStyle.Style(Face.Mono, 11, HudStyle.Text, TextAnchor.MiddleRight));
            y += 21f;
        }

        void DrawTrajectory(Rect c, FlightTelemetry t)
        {
            bool reveal = showOrbitAnalysis && body.Capabilities.Has(ModuleCapability.OrbitPrediction);
            CellLabel(c, reveal ? Loc.T("ТРАЕКТОРИЯ · ОРБИТА") : Loc.T("ТРАЕКТОРИЯ"));
            float colW = (c.width - 32f) * 0.5f;
            if (reveal)
            {
                var o = t.Orbit;
                double R = planet.Model.Radius;
                Big(c.x + 16f, c.y + 34f, colW, Loc.T("АПОЦЕНТР"), o.IsBound ? UnitFormatter.Number((o.ApoapsisRadius - R) / 1000.0, 1) : "∞", Loc.T("км"));
                Big(c.x + 16f + colW, c.y + 34f, colW, Loc.T("ПЕРИЦЕНТР"), UnitFormatter.Number((o.PeriapsisRadius - R) / 1000.0, 1), Loc.T("км"));
                Big(c.x + 16f, c.y + 88f, colW, Loc.T("ПЕРИОД"), o.IsBound ? UnitFormatter.Duration(o.Period) : "—", "");
                Big(c.x + 16f + colW, c.y + 88f, colW, Loc.T("НАКЛОНЕНИЕ"), UnitFormatter.Number(o.Inclination * 180.0 / Math.PI, 1), "°");
            }
            else
            {
                Big(c.x + 16f, c.y + 34f, colW, Loc.T("ВЕРТИКАЛЬНАЯ"), UnitFormatter.Number(t.RadialVelocity, 0), Loc.T("м/с"));
                Big(c.x + 16f + colW, c.y + 34f, colW, Loc.T("ГОРИЗОНТ."), UnitFormatter.Number(t.HorizontalInertialSpeed, 0), Loc.T("м/с"));
                Big(c.x + 16f, c.y + 88f, colW, "ε = v²/2 − μ/r", UnitFormatter.Number(t.SpecificEnergy / 1.0e6, 2), Loc.T("МДж/кг"));
                Big(c.x + 16f + colW, c.y + 88f, colW, "g(r)", UnitFormatter.Number(t.GravityMagnitude, 2), Loc.T("м/с²"));
                string hint = body.Capabilities.Has(ModuleCapability.OrbitPrediction) ? FlightInput.KeyLabel(InputKey.O) + Loc.T(" — орбита (проверка)") : Loc.T("орбиту считаете вы (НК-1 — расчёт)");
                GUI.Label(new Rect(c.x + 16f, c.y + 132f, c.width - 32f, 14f), hint, HudStyle.Style(Face.Mono, 10, HudStyle.TextDim));
            }
        }

        static void Big(float x, float y, float w, string label, string value, string unit)
        {
            GUI.Label(new Rect(x, y, w, 14f), label, HudStyle.Style(Face.Mono, 10, HudStyle.TextMuted));
            GUIStyle v = HudStyle.Style(Face.Mono, 22, HudStyle.TextStrong);
            GUI.Label(new Rect(x, y + 14f, w, 28f), value, v);
            if (unit.Length > 0)
            {
                float vw = v.CalcSize(new GUIContent(value)).x;
                GUI.Label(new Rect(x + vw + 4f, y + 24f, 60f, 14f), unit, HudStyle.Style(Face.Mono, 10, HudStyle.TextMuted));
            }
        }

        void DrawStaging(Rect c, SpaceSim.Flight.RocketState r, FlightTelemetry t)
        {
            CellLabel(c, Loc.T("СТУПЕНИ"));
            if (r == null) return;
            float y = c.y + 32f;
            int shown = 0;
            for (int i = r.StageIndex; i < r.StageCount && shown < 3; i++, shown++)
            {
                bool current = i == r.StageIndex;
                Color numColor = current ? HudStyle.Blue : HudStyle.TextSoft;
                GUI.Label(new Rect(c.x + 16f, y - 4f, 26f, 26f), (i + 1).ToString(), HudStyle.Style(Face.DisplayStrong, 22, numColor));
                GUIStyle name = HudStyle.Style(Face.Body, 13, current ? HudStyle.TextStrong : HudStyle.TextPanel);
                string stageName = Loc.T(r.Configuration.stages[i].name);
                GUI.Label(new Rect(c.x + 46f, y, c.width - 190f, 18f), stageName, name);
                string tag = current ? (r.CanBurn ? Loc.T("АКТИВНА") : FlightInput.KeyLabel(InputKey.Space).ToUpperInvariant() + Loc.T(" — ОТДЕЛИТЬ")) :
                    i == r.StageIndex + 1 ? Loc.T("СЛЕДУЮЩАЯ") : "";
                GUI.Label(new Rect(c.xMax - 160f, y, 146f, 18f), tag,
                    HudStyle.Style(Face.Mono, 10, current ? (r.CanBurn ? HudStyle.Blue : HudStyle.Amber) : HudStyle.TextMuted, TextAnchor.MiddleRight));
                y += 26f;
            }
            if (!r.HasStages)
                GUI.Label(new Rect(c.x + 16f, y, c.width - 32f, 18f), Loc.T("Только зонд — ступеней не осталось"), HudStyle.Style(Face.Body, 13, HudStyle.TextPanel));
            // Δv and thrust-to-weight: what the remaining stack can still do.
            double p0 = planet.Model.Atmosphere.Pressure(0.0);
            double pr = p0 > 0.0 ? t.Pressure / p0 : 0.0;
            string twr = r.HasStages ? UnitFormatter.Number(r.AvailableThrustToWeight(pr, t.GravityMagnitude), r.CurrentStage.IsElectric ? 4 : 2) : "—";
            HudStyle.HLine(c.x + 16f, c.y + 112f, c.width - 32f);
            GUI.Label(new Rect(c.x + 16f, c.y + 118f, c.width - 32f, 22f),
                "Δv " + UnitFormatter.Number(r.DeltaVRemaining(), 0) + Loc.T(" м/с") + "   ·   TWR " + twr,
                HudStyle.Style(Face.Mono, 14, HudStyle.TextStrong));
        }

        // ─────────────────────────────── actions and warp ───────────────────────────────

        void DrawActions(Vector2 screen)
        {
            var r = body.Rocket;
            bool outside = rocketControls != null && rocketControls.Suspended; // astronaut on foot
            float y = actionsTop;
            // The one primary action, centred.
            var primary = new Rect(screen.x * 0.5f - 160f, y, 320f, 42f);
            if (r != null && !body.Destroyed && !outside)
            {
                if (r.HasStages && !r.IsBurning && r.CanBurn)
                {
                    string label = (body.Simulation.IsLanded ? Loc.T("ЗАПУСК") : Loc.T("ВКЛЮЧИТЬ ДВИГАТЕЛЬ")) + "  →";
                    if (GUI.Button(primary, label, HudStyle.Primary)) rocketControls.Ignite();
                }
                else if (r.HasStages && !r.CanBurn)
                {
                    if (GUI.Button(primary, Loc.T("ОТДЕЛИТЬ СТУПЕНЬ") + "  →", HudStyle.Primary)) rocketControls.Separate();
                }
            }

            // Windows: small outline buttons on the left.
            float x = M, h = 34f, by = y + 4f;
            if (hangar != null && body.IsOnLaunchPad && r != null && !r.IsBurning && !AnyModalOpen && !outside && !body.Destroyed)
                x = ActionButton(x, by, h, Loc.T("АНГАР"), InputKey.B, () => hangar.Toggle());
            if (shop != null && !AnyModalOpen) x = ActionButton(x, by, h, Loc.T("МАГАЗИН"), InputKey.K, () => shop.Toggle());
            if (navigationUi != null && !AnyModalOpen) x = ActionButton(x, by, h, Loc.T("НАВИГАЦИЯ"), InputKey.G, () => navigationUi.Toggle());
            if (academy != null && !AnyModalOpen) ActionButton(x, by, h, Loc.T("АКАДЕМИЯ"), InputKey.L, () => academy.Toggle());

            if (!AnyModalOpen) DrawWarpScale(new Rect(screen.x - M - 440f, y, 440f, 46f), simulation, WarpZoneName(simulation.WarpZone));
        }

        static float ActionButton(float x, float y, float h, string label, InputKey key, Action click)
        {
            string text = label + "  " + FlightInput.KeyLabel(key);
            float w = HudStyle.BigButton.CalcSize(new GUIContent(text)).x + 26f;
            if (GUI.Button(new Rect(x, y, w, h), text, HudStyle.BigButton)) click();
            return x + w + 8f;
        }

        static readonly double[] WarpSteps = { 1, 4, 10, 50, 100, 1000, 10000, 100000, 1000000 };

        /// <summary>
        /// The time-warp scale (mock-up: "1× 5× 10× … 100000×"): click a step to run at it; steps above the limit of the current flight
        /// zone are dimmed and do nothing (the Core policy would clamp them anyway). Shared by the HUD and the 3D map.
        /// </summary>
        public static void DrawWarpScale(Rect r, SimulationController sim, string zone)
        {
            if (sim == null) return;
            double limit = sim.WarpLimit, warp = sim.Paused ? 0.0 : sim.TimeWarp;
            GUI.Label(new Rect(r.x, r.y, r.width, 14f),
                Loc.T("УСКОРЕНИЕ ВРЕМЕНИ") + "  ·  " + (sim.Paused ? Loc.T("ПАУЗА") : "×" + UnitFormatter.Number(sim.TimeWarp, 0)) + "  ·  " + Loc.T("МАКС. ×") +
                UnitFormatter.Number(limit, 0) + "  ·  " + zone.ToUpperInvariant(), HudStyle.Style(Face.Mono, 10, HudStyle.TextMuted));
            float y = r.y + 22f, x0 = r.x, w = r.width;
            HudStyle.Fill(new Rect(x0, y + 8f, w, 1f), new Color(1f, 1f, 1f, 0.35f));
            int n = WarpSteps.Length;
            GUIStyle on = HudStyle.Style(Face.Mono, 10, HudStyle.Blue, TextAnchor.MiddleCenter);
            GUIStyle normal = HudStyle.Style(Face.Mono, 10, HudStyle.TextSoft, TextAnchor.MiddleCenter);
            GUIStyle dim = HudStyle.Style(Face.Mono, 10, HudStyle.TextDim, TextAnchor.MiddleCenter);
            for (int i = 0; i < n; i++)
            {
                double step = WarpSteps[i];
                float x = x0 + w * i / (n - 1);
                bool allowed = step <= limit * 1.0001;
                bool current = !sim.Paused && Math.Abs(Math.Log10(Math.Max(warp, 1.0)) - Math.Log10(step)) < 0.15;
                HudStyle.Fill(new Rect(x, y + 3f, 1f, 6f), allowed ? new Color(1f, 1f, 1f, 0.6f) : new Color(1f, 1f, 1f, 0.2f));
                if (current) HudStyle.Fill(new Rect(x - 1f, y - 2f, 3f, 11f), HudStyle.Blue);
                string label = step >= 1000.0 ? UnitFormatter.Number(step / 1000.0, 0) + "k×" : UnitFormatter.Number(step, 0) + "×";
                var hit = new Rect(x - 22f, y + 8f, 44f, 18f);
                GUI.Label(hit, label, current ? on : allowed ? normal : dim);
                if (allowed && GUI.Button(hit, "", GUIStyle.none))
                {
                    sim.SetPaused(false);
                    if (step <= 1.0) sim.ResetWarp();
                    else sim.SetWarp(step);
                }
            }
        }

        // ─────────────────────────────── 3rd rank: the full telemetry, small ───────────────────────────────

        void DrawDetails(FlightTelemetry t, Vector2 screen)
        {
            float top = 586f, bottom = actionsTop - 44f;
            if (bottom - top < 60f) return;
            var left = new List<KeyValuePair<string, string>>
            {
                Kv(Loc.T("Время симуляции"), UnitFormatter.Duration(t.Time)),
                Kv(Loc.T("Широта / долгота"), UnitFormatter.Angle(t.Latitude) + " " + UnitFormatter.Angle(t.Longitude)),
                Kv(Loc.T("Высота над R (h)"), UnitFormatter.Distance(t.AltitudeAboveReference, units)),
                Kv(Loc.T("От центра (r)"), UnitFormatter.Distance(t.DistanceFromCenter, units)),
                Kv(Loc.T("|v| инерциальная"), UnitFormatter.Speed(t.InertialSpeed, units)),
                Kv(Loc.T("|v| отн. поверхн."), UnitFormatter.Speed(t.SurfaceSpeed, units)),
                Kv(Loc.T("|v| отн. воздуха"), UnitFormatter.Speed(t.AirSpeed, units)),
                Kv(Loc.T("v радиальная (v_r)"), UnitFormatter.Speed(t.RadialVelocity, units)),
                Kv(Loc.T("v горизонт. (инерц.)"), UnitFormatter.Speed(t.HorizontalInertialSpeed, units)),
                Kv(Loc.T("v⃗ отн. пов. E/N/Up"), UnitFormatter.Number(Conv(t.VelocityEast), Dec) + " / " + UnitFormatter.Number(Conv(t.VelocityNorth), Dec) + " / " +
                                                UnitFormatter.Number(Conv(t.VelocityUp), Dec)),
                Kv(Loc.T("v⃗ инерц. x/y/z"), UnitFormatter.Number(Conv(t.InertialVelocity.x), Dec) + " / " + UnitFormatter.Number(Conv(t.InertialVelocity.y), Dec) + " / " +
                                            UnitFormatter.Number(Conv(t.InertialVelocity.z), Dec)),
                Kv("ε = v²/2 − μ/r", UnitFormatter.SpecificEnergy(t.SpecificEnergy, units)),
            };
            var r = body.Rocket;
            var rec = body.Simulation.Recorder;
            var right = new List<KeyValuePair<string, string>>
            {
                Kv("g(r) = μ/r²", UnitFormatter.Acceleration(t.GravityMagnitude)),
                Kv(Loc.T("|a| полное"), UnitFormatter.Acceleration(t.TotalAcceleration)),
                Kv(Loc.T("Перегрузка"), UnitFormatter.Number(t.ProperAcceleration / PhysicalConstants.StandardGravity, 3) + " g₀"),
                Kv(Loc.T("Плотность ρ"), UnitFormatter.Density(t.Density)),
                Kv("ρ/ρ₀", UnitFormatter.Scientific(t.DensityRatio, 3)),
                Kv(Loc.T("Давление p"), UnitFormatter.Pressure(t.Pressure)),
                Kv(Loc.T("Скоростной напор q"), UnitFormatter.Pressure(t.DynamicPressure)),
                Kv(Loc.T("Сопротивление a_drag"), UnitFormatter.Acceleration(t.DragAcceleration)),
            };
            var heat = body.Simulation.Systems;
            if (heat != null && heat.HeatFlux > 1.0)
                right.Add(Kv(Loc.T("Нагрев носа"), UnitFormatter.Number(heat.HeatFlux / 1000.0, 1) + " / " + UnitFormatter.Number(heat.AllowedHeatFlux / 1000.0, 0) + Loc.T(" кВт/м²")));
            if (r != null && r.HasStages)
            {
                double p0 = planet.Model.Atmosphere.Pressure(0.0);
                double pr = p0 > 0.0 ? t.Pressure / p0 : 0.0;
                double maxThrust = RocketEquations.ThrustFromMassFlow(r.FullThrottleMassFlow, r.Isp(pr));
                right.Add(Kv(Loc.T("Тяга сейчас / макс."), Force(r.Thrust(pr)) + " / " + Force(maxThrust)));
                right.Add(Kv(Loc.T("Расход ṁ"), Flow(r.MassFlow)));
            }
            right.Add(Kv(Loc.T("Δv израсходовано"), UnitFormatter.Speed(rec.DeltaVExpended, DisplayUnitSystem.SI)));
            right.Add(Kv(Loc.T("Потери: гравит. / сопрот. / управл."), UnitFormatter.Number(rec.GravityLoss, 0) + " / " + UnitFormatter.Number(rec.DragLoss, 0) + " / " +
                                                                       UnitFormatter.Number(rec.SteeringLoss, 0) + Loc.T(" м/с")));
            if (showOrbitAnalysis && body.Capabilities.Has(ModuleCapability.OrbitPrediction))
            {
                var o = t.Orbit;
                right.Add(Kv(Loc.T("Эксцентриситет e"), UnitFormatter.Number(o.Eccentricity, 5)));
                right.Add(Kv(Loc.T("Большая полуось a"), o.IsBound ? UnitFormatter.Distance(o.SemiMajorAxis, units) : "— (ε ≥ 0)"));
                right.Add(Kv(Loc.T("v_круг / v_уход"), UnitFormatter.Speed(t.CircularVelocityHere, units) + " / " + UnitFormatter.Speed(t.EscapeVelocityHere, units)));
            }
            DetailColumn(new Rect(M, top, 330f, bottom - top), Loc.T("ПОЛОЖЕНИЕ И СКОРОСТЬ"), left);
            DetailColumn(new Rect(screen.x - M - 360f, top, 360f, bottom - top), Loc.T("СРЕДА, СИЛЫ, РАСХОД Δv"), right);
        }

        static KeyValuePair<string, string> Kv(string k, string v) => new KeyValuePair<string, string>(k, v);

        static void DetailColumn(Rect r, string title, List<KeyValuePair<string, string>> rows)
        {
            GUI.Label(new Rect(r.x, r.y, r.width, 14f), title, HudStyle.Style(Face.Mono, 10, HudStyle.TextMuted));
            HudStyle.HLine(r.x, r.y + 17f, r.width, new Color(1f, 1f, 1f, 0.12f));
            float y = r.y + 22f;
            GUIStyle k = HudStyle.Style(Face.Body, 11, HudStyle.TextMuted), v = HudStyle.Style(Face.Mono, 11, HudStyle.TextSoft, TextAnchor.MiddleRight);
            foreach (var row in rows)
            {
                if (y + RowH > r.yMax) break;
                GUI.Label(new Rect(r.x, y, r.width * 0.5f, RowH), row.Key, k);
                GUI.Label(new Rect(r.x + r.width * 0.42f, y, r.width * 0.58f, RowH), row.Value, v);
                y += RowH;
            }
        }

        // ─────────────────────────────── manoeuvre, notifications, key line ───────────────────────────────

        /// <summary>Burn guidance for a planned manoeuvre and the predicted encounter (Core predictor); the player flies the burn.</summary>
        void DrawManeuverCard(Vector2 screen)
        {
            if (navigation == null) return;
            NodeGuidance g = navigation.Guidance;
            SpaceSim.Navigation.TrajectoryPrediction p = navigation.Prediction;
            int target = navigation.Plan.Target;
            if (!g.Active && (p == null || target < 0)) return;
            var r = new Rect((screen.x - 560f) * 0.5f, 100f, 560f, 84f);
            HudStyle.Card(r);
            var sys = navigation.System;
            if (g.Active)
            {
                GUI.Label(new Rect(r.x + 14f, r.y + 8f, 300f, 14f), Loc.T("МАНЁВР · ЗАПЛАНИРОВАН"), HudStyle.Style(Face.Mono, 10, HudStyle.Blue));
                string burnIn = g.Started ? Loc.T("ИДЁТ") : g.TimeToIgnition > 0 ? Clock(g.TimeToIgnition) : Loc.T("СЕЙЧАС");
                GUI.Label(new Rect(r.x + 14f, r.y + 22f, 200f, 40f), burnIn, HudStyle.Style(Face.DisplayStrong, 34, HudStyle.Blue));
                GUI.Label(new Rect(r.x + 200f, r.y + 22f, 160f, 40f), UnitFormatter.Number(g.Remaining, 0), HudStyle.Style(Face.DisplayStrong, 34, HudStyle.TextStrong));
                GUI.Label(new Rect(r.x + 14f, r.y + 62f, 400f, 14f),
                    Loc.T("ДО ЗАЖИГАНИЯ") + "  ·  Δv " + Loc.T("ОСТАЛОСЬ, М/С") + "  ·  " + Loc.T("ТАНГАЖ ") + UnitFormatter.Number(g.Pitch, 1) + "° " +
                    Loc.T("КУРС ") + UnitFormatter.Number(g.Heading, 1) + "°  ·  " + UnitFormatter.Number(g.BurnTime, 0) + Loc.T(" С"), HudStyle.Style(Face.Mono, 10, HudStyle.TextMuted));
            }
            if (p != null && target >= 0 && sys != null)
            {
                string text = p.Encounter ? Loc.T("СФЕРА ВЛИЯНИЯ ЧЕРЕЗ ") + UnitFormatter.Duration(p.EncounterTime - navigation.Now)
                    : Loc.T("ПРОМАХ ") + UnitFormatter.Number(p.ClosestDistance / 1e9, 2) + Loc.T(" МЛН КМ");
                GUI.Label(new Rect(r.x + 380f, r.y + 8f, 166f, 14f), sys.NameOf(target).ToUpperInvariant(), HudStyle.Style(Face.Mono, 10, HudStyle.Amber, TextAnchor.MiddleRight));
                var wrap = new GUIStyle(HudStyle.Style(Face.Mono, 11, HudStyle.TextSoft, TextAnchor.UpperRight)) { wordWrap = true };
                GUI.Label(new Rect(r.x + 330f, r.y + 26f, 216f, 40f), text, wrap);
            }
        }

        void DrawNotifications(Vector2 screen, float bottomY)
        {
            float y = bottomY;
            GUIStyle s = HudStyle.Style(Face.Body, 13, HudStyle.TextStrong, TextAnchor.MiddleCenter);
            for (int i = state.Notifications.Count - 1; i >= 0; i--)
            {
                var n = state.Notifications[i];
                float age = Time.unscaledTime - n.RealTime;
                float a = Mathf.Clamp01(1.5f - age / 8f);
                if (a <= 0f) continue;
                float w = Mathf.Min(900f, s.CalcSize(new GUIContent(n.Text)).x + 28f);
                var r = new Rect((screen.x - w) * 0.5f, y, w, 22f);
                Color c = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, a);
                HudStyle.Fill(r, new Color(0.04f, 0.04f, 0.045f, 0.78f));
                GUI.Label(r, n.Text, s);
                GUI.color = c;
                y -= 25f;
            }
        }

        void DrawKeyLine(Vector2 screen)
        {
            float x = M, y = screen.y - 32f;
            x += HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.M), Loc.T("КАРТА"));
            x += HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.Tab), Loc.T("ЭКИПАЖ"));
            if (system.SpaceUnlocked)
            {
                x += HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.Y), Loc.T("СХЕМА"));
                x += HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.J), Loc.T("К ДРУГОМУ ТЕЛУ"));
            }
            x += HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.Space), Loc.T("ОТДЕЛИТЬ"));
            x += HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.R), Loc.T("НОВАЯ РАКЕТА"));
            x += HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.F1), "MANUAL");
            x += HudStyle.KeyHint(x, y, FlightInput.KeyLabel(InputKey.H), Loc.T("СКРЫТЬ"));
            HudStyle.KeyHint(x, y, "ESC", Loc.T("ПАУЗА"));
            GUI.Label(new Rect(screen.x - M - 520f, y, 520f, 20f), Loc.T("все клавиши: Esc → Настройки → Управление"),
                HudStyle.Style(Face.Mono, 10, HudStyle.TextDim, TextAnchor.MiddleRight));
        }

        static Texture2D Ring()
        {
            if (ring != null) return ring;
            const int n = 128;
            ring = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float c = n * 0.5f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - c) * (x + 0.5f - c) + (y + 0.5f - c) * (y + 0.5f - c));
                float a = Mathf.Clamp01(1.4f - Mathf.Abs(d - (c - 2f)));
                float fill = d < c - 2f ? 0.05f : 0f;
                px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Max(a * 0.85f, fill));
            }
            ring.SetPixels(px);
            ring.Apply();
            return ring;
        }
    }
}
