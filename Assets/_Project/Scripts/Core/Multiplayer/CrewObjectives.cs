using System;
using System.Collections.Generic;
using SpaceSim.Exploration;
using SpaceSim.Localization;
using SpaceSim.Planets;
using SpaceSim.Vehicles;

namespace SpaceSim.Multiplayer
{
    /// <summary>What has to happen for one objective of a team mission.</summary>
    public enum CrewGoalKind
    {
        /// <summary>The rocket on the pad carries one of the modules in <see cref="CrewGoal.Modules"/>.</summary>
        RocketModule,
        /// <summary>The vehicle is in the target body's sphere of influence.</summary>
        ReachBody,
        /// <summary>A closed orbit around the target body, periapsis above its atmosphere (space boundary).</summary>
        OrbitBody,
        /// <summary>Standing on the target body's ground, intact.</summary>
        LandOnBody,
        /// <summary>The robot operator has taken <see cref="CrewGoal.Count"/> photos at the target.</summary>
        RobotPhotos,
        /// <summary>The robot has taken <see cref="CrewGoal.Count"/> samples on the target body.</summary>
        RobotSamples,
        /// <summary>After the surface work the robot is back inside the rocket.</summary>
        RobotReturn
    }

    /// <summary>One objective of one role.</summary>
    public sealed class CrewGoal
    {
        public string Id;
        public CrewRole Role;
        public CrewGoalKind Kind;
        /// <summary>RocketModule: any of these module ids counts.</summary>
        public string[] Modules = new string[0];
        /// <summary>RobotPhotos / RobotSamples: how many.</summary>
        public int Count = 1;
        /// <summary>Source text (Russian) of the title; shown through <see cref="Title"/>.</summary>
        public string RawTitle = "";
        /// <summary>Body name or module name added to the title (not translated: a proper name).</summary>
        public string Subject = "";

        public string Title => Loc.T(RawTitle) + (Subject.Length > 0 ? " " + Subject : "") + (Count > 1 ? " (" + Count + ")" : "");
    }

    /// <summary>What this computer sees right now, for checking the objectives (filled by the runtime every frame).</summary>
    public sealed class CrewMissionFacts
    {
        /// <summary>Module ids on the current rocket (engines, tanks, side sets, payload).</summary>
        public ICollection<string> RocketModules = new string[0];
        /// <summary>Id of the body whose frame the vehicle flies in.</summary>
        public string FrameBodyId = "";
        public bool Landed, Destroyed;
        /// <summary>A closed orbit with the periapsis above the space boundary of the frame body.</summary>
        public bool StableOrbit;
        /// <summary>Photos the robot operator has taken at the target (theirs, or from their snapshots).</summary>
        public int Photos;
        /// <summary>Samples taken on the target body (this computer's list; the operator's are mirrored into it).</summary>
        public int SamplesOnTarget;
        /// <summary>The robot is outside right now.</summary>
        public bool RobotOutside;
    }

    /// <summary>
    /// The objectives of a team mission, per role, made from the mission's target (<see cref="CrewMissionSurvey"/>).
    /// The same mission and room give the same list on every computer. An objective, once met, stays met (the pilot may leave
    /// the orbit for the landing, the robot may walk on after the last photo). Pure, no Unity.
    ///   Engineer: the instruments the robot's job needs (camera for photos, sample collector, terrain scanner for the ice
    ///   radar, relay antenna) and landing legs for a surface mission.
    ///   Pilot: reach the target body (when it is not the start world), then orbit it or land on it.
    ///   Robot operator: photos and samples at the target, then back into the rocket after surface work.
    /// </summary>
    public sealed class CrewObjectives
    {
        /// <summary>Photos asked in orbit at most (the survey's count is for a satellite; a crew presses a key per photo).</summary>
        public const int MaxOrbitPhotos = 8;

        public CrewMissionSurvey.Target Target { get; }
        public RobotSurvey Survey { get; }
        public IReadOnlyList<CrewGoal> Goals => goals;

        readonly List<CrewGoal> goals = new List<CrewGoal>();
        readonly bool[] done;
        readonly int[] progress;

        public CrewObjectives(CrewMissionSurvey.Target target, RobotSurvey survey, string startBodyId = HomeSystemCatalog.HomeWorldId)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            Survey = survey ?? throw new ArgumentNullException(nameof(survey));
            string body = survey.BodyName ?? target.BodyId;

            // Engineer: what the robot's job needs on board.
            if ((target.Tasks & RobotTask.Photos) != 0) Module("eng-camera", "На ракете камера", ModuleCatalog.Camera);
            if ((target.Tasks & RobotTask.Samples) != 0) Module("eng-collector", "На ракете пробоотборник", ModuleCatalog.CollectorBasic);
            if ((target.Tasks & RobotTask.IceRadar) != 0) Module("eng-radar", "На ракете сканер рельефа", ModuleCatalog.TerrainScanner);
            if ((target.Tasks & RobotTask.Relay) != 0) Module("eng-relay", "На ракете ретранслятор", ModuleCatalog.Relay);
            if (target.Surface) Module("eng-legs", "На ракете посадочные опоры", ModuleCatalog.LegsLight, ModuleCatalog.LegsHeavy);

            // Pilot: get there, then orbit or land.
            if (target.BodyId != startBodyId) Add("pilot-reach", CrewRole.Pilot, CrewGoalKind.ReachBody, "Долететь до", body);
            if (target.Surface) Add("pilot-land", CrewRole.Pilot, CrewGoalKind.LandOnBody, "Сесть на", body);
            else Add("pilot-orbit", CrewRole.Pilot, CrewGoalKind.OrbitBody, "Выйти на орбиту", body);

            // Robot operator: the work at the target.
            if ((target.Tasks & RobotTask.Photos) != 0)
            {
                int n = target.Surface ? Math.Max(1, survey.Photos) : Math.Max(1, Math.Min(MaxOrbitPhotos, survey.Photos));
                Add("robot-photos", CrewRole.RobotOperator, CrewGoalKind.RobotPhotos, target.Surface ? "Снимки поверхности:" : "Снимки с орбиты:", body, n);
            }
            if (target.Surface && survey.Samples > 0)
                Add("robot-samples", CrewRole.RobotOperator, CrewGoalKind.RobotSamples, "Образцы грунта:", body, survey.Samples);
            if (target.Surface) Add("robot-return", CrewRole.RobotOperator, CrewGoalKind.RobotReturn, "Вернуть робота в ракету", "");

            done = new bool[goals.Count];
            progress = new int[goals.Count];
        }

        /// <summary>The objectives of a mission in a room (the same on every computer of the room).</summary>
        public static CrewObjectives For(string scenarioId, string roomCode) =>
            new CrewObjectives(CrewMissionSurvey.TargetOf(scenarioId), CrewMissionSurvey.Build(scenarioId, roomCode));

        void Module(string id, string title, params string[] modules)
        {
            ModuleDefinition m = ModuleCatalog.Find(modules[0]);
            goals.Add(new CrewGoal
            {
                Id = id, Role = CrewRole.Engineer, Kind = CrewGoalKind.RocketModule, Modules = modules, RawTitle = title,
                Subject = m != null ? "(" + m.name + ")" : "",
            });
        }

        void Add(string id, CrewRole role, CrewGoalKind kind, string title, string subject, int count = 1) =>
            goals.Add(new CrewGoal { Id = id, Role = role, Kind = kind, RawTitle = title, Subject = subject ?? "", Count = count });

        public bool IsDone(int i) => done[i];
        /// <summary>Counted objectives: how far (capped at the count); others 0 or 1.</summary>
        public int Progress(int i) => progress[i];
        public bool AllDone => Array.TrueForAll(done, d => d);

        public int DoneCount(CrewRole role)
        {
            int n = 0;
            for (int i = 0; i < goals.Count; i++) if (goals[i].Role == role && done[i]) n++;
            return n;
        }

        public int CountOf(CrewRole role)
        {
            int n = 0;
            foreach (CrewGoal g in goals) if (g.Role == role) n++;
            return n;
        }

        /// <summary>Checks every objective against what is seen now; returns the indices that became done in this call.</summary>
        public List<int> Update(CrewMissionFacts f)
        {
            var newlyDone = new List<int>();
            if (f == null) return newlyDone;
            bool atTarget = f.FrameBodyId == Target.BodyId;
            for (int i = 0; i < goals.Count; i++)
            {
                CrewGoal g = goals[i];
                int p;
                switch (g.Kind)
                {
                    case CrewGoalKind.RocketModule: p = HasAny(f.RocketModules, g.Modules) ? 1 : 0; break;
                    case CrewGoalKind.ReachBody: p = atTarget && !f.Destroyed ? 1 : 0; break;
                    case CrewGoalKind.OrbitBody: p = atTarget && !f.Destroyed && !f.Landed && f.StableOrbit ? 1 : 0; break;
                    case CrewGoalKind.LandOnBody: p = atTarget && !f.Destroyed && f.Landed ? 1 : 0; break;
                    case CrewGoalKind.RobotPhotos: p = f.Photos; break;
                    case CrewGoalKind.RobotSamples: p = f.SamplesOnTarget; break;
                    case CrewGoalKind.RobotReturn: p = WorkBeforeReturnDone() && !f.RobotOutside && atTarget && f.Landed ? 1 : 0; break;
                    default: p = 0; break;
                }
                progress[i] = Math.Max(progress[i], Math.Min(p, g.Count)); // never goes back
                if (!done[i] && progress[i] >= g.Count)
                {
                    done[i] = true;
                    newlyDone.Add(i);
                }
            }
            return newlyDone;
        }

        /// <summary>Photos and samples are done (the return counts only after the work).</summary>
        bool WorkBeforeReturnDone()
        {
            for (int i = 0; i < goals.Count; i++)
                if ((goals[i].Kind == CrewGoalKind.RobotPhotos || goals[i].Kind == CrewGoalKind.RobotSamples) && !done[i]) return false;
            return true;
        }

        static bool HasAny(ICollection<string> have, string[] wanted)
        {
            if (have == null) return false;
            foreach (string w in wanted)
                if (have.Contains(w)) return true;
            return false;
        }

        /// <summary>Every module id on a design (engines, tanks, side sets, payload).</summary>
        public static HashSet<string> ModulesOf(RocketBlueprint design)
        {
            var set = new HashSet<string>();
            if (design == null) return set;
            foreach (StageBlueprint s in design.stages)
            {
                if (!string.IsNullOrEmpty(s.engineId)) set.Add(s.engineId);
                foreach (TankLoad t in s.tanks) if (!string.IsNullOrEmpty(t.moduleId)) set.Add(t.moduleId);
                if (s.attachments != null) foreach (string a in s.attachments) if (!string.IsNullOrEmpty(a)) set.Add(a);
            }
            foreach (string p in design.payloadModules) if (!string.IsNullOrEmpty(p)) set.Add(p);
            return set;
        }
    }

    /// <summary>
    /// When the robot operator may take a photo that counts: at the target body, outside on its ground with the robot's camera,
    /// or from an orbit around it with the rocket's camera; each photo of a new place (surface: some metres away; orbit: some
    /// time later, the ground below has moved on). Pure.
    /// </summary>
    public static class CrewPhotoRules
    {
        /// <summary>Distance between two surface photos [m].</summary>
        public const double SurfaceSpacing = 10.0;
        /// <summary>Simulated time between two orbit photos [s].</summary>
        public const double OrbitSpacing = 30.0;

        /// <summary>Empty when a photo counts now, else why not (shown to the player).</summary>
        public static string Check(CrewMissionSurvey.Target target, string frameBodyId, bool eva, bool landed, bool stableOrbit,
            bool rocketHasCamera, double metresFromLastPhoto, double secondsSinceLastPhoto)
        {
            if (target == null) return Loc.T("Нет цели миссии.");
            if (frameBodyId != target.BodyId) return Loc.T("Снимки считаются только у цели миссии.");
            if (target.Surface)
            {
                if (!eva) return Loc.T("Снимки поверхности делает робот снаружи (E у ракеты на грунте).");
                if (metresFromLastPhoto < SurfaceSpacing)
                    return Loc.T("Это место уже снято: отойдите на ") + SurfaceSpacing.ToString("0") + Loc.T(" м.");
                return "";
            }
            if (landed || !stableOrbit) return Loc.T("Снимки с орбиты: нужна устойчивая орбита вокруг цели.");
            if (!rocketHasCamera) return Loc.T("На ракете нет камеры: её ставит инженер.");
            if (secondsSinceLastPhoto < OrbitSpacing)
                return Loc.T("Под камерой тот же участок: следующий снимок через ") + Math.Ceiling(OrbitSpacing - secondsSinceLastPhoto).ToString("0") + Loc.T(" с.");
            return "";
        }
    }
}
