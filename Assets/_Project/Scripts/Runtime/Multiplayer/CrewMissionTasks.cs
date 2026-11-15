using System.Collections.Generic;
using SpaceSim.Exploration;
using SpaceSim.Flight;
using SpaceSim.Localization;
using SpaceSim.Mathematics;
using SpaceSim.Multiplayer;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime.Multiplayer
{
    /// <summary>
    /// The objectives of a team mission on this computer (<see cref="CrewObjectives"/>): checked every frame against what this
    /// computer sees (the shared rocket, the mirrored flight, the robot operator's photos and samples), announced when met, and
    /// shown in the «ЗАДАНИЯ» panel (J) with the satellite survey of the target. The robot operator takes the photos here (V):
    /// outside with the robot's camera on a surface mission, from an orbit with the rocket's camera otherwise
    /// (<see cref="CrewPhotoRules"/>). Lives on the bootstrap for the whole mission.
    /// </summary>
    public sealed class CrewMissionTasks : MonoBehaviour
    {
        /// <summary>Photos the robot operator has taken on this computer (sent to the others in the robot snapshots).</summary>
        public static int LocalPhotos { get; private set; }

        SpaceSimBootstrap boot;
        CrewObjectives objectives;
        readonly CrewMissionFacts facts = new CrewMissionFacts();
        bool expanded = true, allAnnounced;
        Vector3d lastPhotoAt = new Vector3d(double.MaxValue, 0.0, 0.0);
        double lastPhotoTime = double.NegativeInfinity;

        public CrewObjectives Objectives => objectives;

        public void Initialize(SpaceSimBootstrap bootstrap)
        {
            boot = bootstrap;
            LocalPhotos = 0;
            objectives = CrewObjectives.For(CrewSession.ScenarioId, CrewSession.RoomCode);
        }

        static bool IsOperator => CrewSession.Can(CrewAction.WorkOnSurface);

        void Notify(string text)
        {
            if (boot != null && boot.FlightState != null) boot.FlightState.Notify(text);
        }

        void Update()
        {
            if (objectives == null || boot == null || boot.Body == null || boot.Body.Simulation == null || !CrewSession.Active) return;
            Gather();
            foreach (int i in objectives.Update(facts))
            {
                CrewGoal g = objectives.Goals[i];
                Notify(Loc.T("ЗАДАНИЕ ВЫПОЛНЕНО") + " · " + CrewRoles.Info(g.Role).Title + ": " + g.Title);
            }
            if (objectives.AllDone && !allAnnounced)
            {
                allAnnounced = true;
                Notify(Loc.T("МИССИЯ ВЫПОЛНЕНА: экипаж справился со всеми заданиями."));
            }

            if (FlightInput.IsTyping) return;
            if (FlightInput.GetKeyDown(InputKey.J)) expanded = !expanded;
            if (FlightInput.GetKeyDown(InputKey.V))
            {
                if (CrewGate.Allows(CrewAction.WorkOnSurface, boot.FlightState)) TakePhoto();
            }
        }

        /// <summary>What this computer sees now, for the objectives.</summary>
        void Gather()
        {
            FlightBody body = boot.Body;
            FreeFlightSimulation sim = body.Simulation;
            facts.RocketModules = CrewObjectives.ModulesOf(body.Blueprint);
            facts.FrameBodyId = boot.System != null ? boot.System.ActiveBody.Id : "";
            facts.Landed = sim.IsLanded;
            facts.Destroyed = body.Destroyed;
            facts.StableOrbit = StableOrbit();
            facts.Photos = IsOperator ? LocalPhotos : CrewRobotLink.RemotePhotos;
            facts.RobotOutside = IsOperator ? boot.Explorer != null && boot.Explorer.Eva : CrewRobotLink.RemoteEva;
            int samples = 0;
            if (boot.Explorer != null)
            {
                string prefix = objectives.Target.BodyId + ":";
                foreach (string id in boot.Explorer.CollectedSites)
                    if (id != null && id.StartsWith(prefix, System.StringComparison.Ordinal)) samples++;
            }
            facts.SamplesOnTarget = samples;
        }

        /// <summary>A closed orbit around the frame body with the periapsis above its space boundary (atmosphere).</summary>
        bool StableOrbit()
        {
            FreeFlightSimulation sim = boot.Body.Simulation;
            PlanetRuntime planet = boot.Planet;
            if (sim.IsLanded || planet == null || planet.Model == null) return false;
            PlanetModel model = planet.Model;
            Vector3d r, v;
            if (sim.Navigation != null)
            {
                CoastState c = sim.CoastStateNow();
                if (c.Primary != sim.FrameBody) return false; // the vehicle is leaving or arriving: not an orbit of this body
                r = c.Position;
                v = c.Velocity;
            }
            else
            {
                r = sim.State.Position - model.Position;
                v = sim.State.Velocity;
            }
            double mu = model.GravitationalParameter;
            if (OrbitalMechanics.SpecificOrbitalEnergy(mu, r.Magnitude, v.Magnitude) >= 0.0) return false;
            OrbitApsides a = OrbitApsides.FromState(mu, r, v);
            return a.PeriapsisRadius > model.Radius + model.SpaceBoundaryAltitude;
        }

        /// <summary>V: a photo of the target, if it counts here and now (the reason otherwise).</summary>
        void TakePhoto()
        {
            FlightBody body = boot.Body;
            SurfaceExplorer ex = boot.Explorer;
            bool eva = ex != null && ex.Eva;
            double now = boot.Simulation.SimulationTime;
            double metres = eva ? (ex.Walker.Position - lastPhotoAt).Magnitude : double.PositiveInfinity;
            bool camera = facts.RocketModules != null && facts.RocketModules.Contains(ModuleCatalog.Camera);
            string why = CrewPhotoRules.Check(objectives.Target, facts.FrameBodyId, eva, body.Simulation.IsLanded, facts.StableOrbit,
                camera, metres, now - lastPhotoTime);
            if (why.Length > 0)
            {
                Notify(Loc.T("Снимок не засчитан: ") + why);
                return;
            }
            LocalPhotos++;
            if (eva) lastPhotoAt = ex.Walker.Position;
            lastPhotoTime = now;
            int need = 0;
            foreach (CrewGoal g in objectives.Goals)
                if (g.Kind == CrewGoalKind.RobotPhotos) need = g.Count;
            Notify(Loc.T("Снимок ") + LocalPhotos + (need > 0 ? " / " + need : "") + (eva ? Loc.T(" — камера робота.") : Loc.T(" — камера ракеты, с орбиты.")));
        }

        // ---- Panel ---------------------------------------------------------------------------------------------------

        void OnGUI()
        {
            if (objectives == null || !CrewSession.Active || FlightInput.PauseMenuOpen) return;
            HudStyle.Begin();
            GUI.depth = -39;
            Vector2 screen = HudStyle.VirtualScreen;
            int total = objectives.Goals.Count, doneAll = 0;
            for (int i = 0; i < total; i++) if (objectives.IsDone(i)) doneAll++;

            const float W = 440f;
            float x = Mathf.Round((screen.x - W) * 0.5f);
            string head = Loc.T("ЗАДАНИЯ ЭКИПАЖА") + "  " + doneAll + "/" + total + "   " + FlightInput.KeyLabel(InputKey.J);
            if (!expanded)
            {
                var strip = new Rect(x, 8f, W, 26f);
                HudStyle.Fill(strip, new Color(0.04f, 0.04f, 0.045f, 0.78f));
                GUI.Label(new Rect(strip.x + 12f, strip.y, strip.width - 24f, strip.height), head,
                    HudStyle.Style(HudStyle.Face.Mono, 11, doneAll == total ? HudStyle.Green : HudStyle.Amber));
                return;
            }

            float rowH = 20f;
            int headers = 0;
            foreach (CrewRole role in CrewRoles.All) if (objectives.CountOf(role) > 0) headers++;
            float h = 30f + (headers + total) * rowH + headers * 4f + 4f * rowH + 12f; // title, roles with their goals, survey
            var r = new Rect(x, 8f, W, h);
            HudStyle.Fill(r, new Color(0.04f, 0.04f, 0.045f, 0.86f));
            GUI.Label(new Rect(r.x + 12f, r.y + 4f, r.width - 24f, 22f), head,
                HudStyle.Style(HudStyle.Face.Mono, 12, doneAll == total ? HudStyle.Green : HudStyle.Amber));
            float y = r.y + 30f;

            foreach (CrewRole role in CrewRoles.All)
            {
                if (objectives.CountOf(role) == 0) continue;
                bool mine = CrewSession.Has(role);
                CrewRoleInfo info = CrewRoles.Info(role);
                string holder = CrewSession.HolderOf(role);
                GUI.Label(new Rect(r.x + 12f, y, r.width - 24f, rowH),
                    info.Title + (holder != null ? "  · " + holder : "") + (mine ? "  ◀ " + Loc.T("ВЫ") : ""),
                    HudStyle.Style(HudStyle.Face.Mono, 11, new Color32(info.R, info.G, info.B, 255)));
                y += rowH;
                for (int i = 0; i < total; i++)
                {
                    CrewGoal g = objectives.Goals[i];
                    if (g.Role != role) continue;
                    bool done = objectives.IsDone(i);
                    string mark = done ? "✓" : "○";
                    string count = g.Count > 1 && !done ? "  " + objectives.Progress(i) + "/" + g.Count : "";
                    GUI.Label(new Rect(r.x + 22f, y, r.width - 34f, rowH), mark + "  " + Loc.T(g.RawTitle) + (g.Subject.Length > 0 ? " " + g.Subject : "") + count,
                        HudStyle.Style(HudStyle.Face.Mono, 11, done ? HudStyle.Green : mine ? HudStyle.TextStrong : HudStyle.TextMuted));
                    y += rowH;
                }
                y += 4f;
            }

            // The satellite survey of the target: what the robot operator builds and plans for (everyone may read it).
            RobotSurvey s = objectives.Survey;
            GUI.Label(new Rect(r.x + 12f, y, r.width - 24f, rowH), Loc.T("СПУТНИКОВЫЙ СНИМОК ЦЕЛИ") + ": " + s.BodyName,
                HudStyle.Style(HudStyle.Face.Mono, 11, HudStyle.Blue));
            y += rowH;
            var soft = HudStyle.Style(HudStyle.Face.Mono, 11, HudStyle.TextSoft);
            string where = s.Surface
                ? Loc.T("Поверхность") + " · " + RobotParts.GroundName(s.Ground) + " · " + Loc.T("до места ") + UnitFormatter.Number(s.DistanceKm, 1) + Loc.T(" км, подъём ") + UnitFormatter.Number(s.ClimbM, 0) + Loc.T(" м")
                : Loc.T("Орбита") + " · " + Loc.T("работа ") + UnitFormatter.Number(s.WorkHours, 0) + Loc.T(" ч");
            GUI.Label(new Rect(r.x + 22f, y, r.width - 34f, rowH), where, soft);
            y += rowH;
            GUI.Label(new Rect(r.x + 22f, y, r.width - 34f, rowH),
                "g " + UnitFormatter.Number(s.Gravity, 2) + Loc.T(" м/с²") + " · " + UnitFormatter.Number(s.MinTemperature - 273.15, 0) + "…" +
                UnitFormatter.Number(s.MaxTemperature - 273.15, 0) + " °C · " + Loc.T("давление ") + UnitFormatter.Number(s.Pressure / 1000.0, 1) + Loc.T(" кПа"), soft);
            y += rowH;
            GUI.Label(new Rect(r.x + 22f, y, r.width - 34f, rowH),
                Loc.T("радиация ") + s.Radiation + " · " + Loc.T("кислота ") + s.Acid + "   " +
                (CrewSession.Has(CrewRole.RobotOperator) ? FlightInput.KeyLabel(InputKey.V) + Loc.T(" — снимок") : ""), soft);
        }
    }
}
