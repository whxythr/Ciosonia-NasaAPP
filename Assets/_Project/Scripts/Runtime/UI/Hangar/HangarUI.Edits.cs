using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Vehicles;

namespace SpaceSim.Runtime
{
    // Changes to the edited design. Called from deferred actions (between IMGUI passes), from the bridge and from tests.
    public sealed partial class HangarUI
    {
        bool ValidStage(int index) => design != null && index >= 0 && index < design.stages.Count;

        void SetEngine(int stage, string engineId)
        {
            if (!CrewEditAllowed()) return;
            if (!ValidStage(stage)) return;
            StageBlueprint s = design.stages[stage];
            s.engineId = engineId;
            s.engineCount = Math.Max(1, Math.Min(BlueprintCompiler.MaxEnginesPerStage, s.engineCount));
            focusRequest = p => p.Role == PartRole.Engine && p.Stage == stage;
            MarkChanged();
        }

        void ChangeEngineCount(int stage, int delta)
        {
            if (!CrewEditAllowed()) return;
            if (!ValidStage(stage)) return;
            StageBlueprint s = design.stages[stage];
            s.engineCount = Math.Max(1, Math.Min(BlueprintCompiler.MaxEnginesPerStage, s.engineCount + delta));
            int last = s.engineCount - 1;
            focusRequest = p => p.Role == PartRole.Engine && p.Stage == stage && (delta < 0 || p.Index == last);
            MarkChanged();
        }

        void RemoveEngine(int stage)
        {
            if (!CrewEditAllowed()) return;
            if (!ValidStage(stage)) return;
            design.stages[stage].engineId = "";
            MarkChanged();
        }

        /// <summary>Adds a tank on top of the stage's tanks (public for tools and tests).</summary>
        public void AddTank(int stage, string tankId)
        {
            if (!CrewEditAllowed()) return;
            ModuleDefinition tank = ModuleCatalog.Find(tankId);
            if (!ValidStage(stage) || tank == null) return;
            design.stages[stage].tanks.Add(new TankLoad { moduleId = tankId, propellantLoaded = tank.propellantCapacity });
            int index = design.stages[stage].tanks.Count - 1;
            focusRequest = p => p.Role == PartRole.Tank && p.Stage == stage && p.Index == index;
            MarkChanged();
        }

        void RemoveTank(int stage, int tank)
        {
            if (!CrewEditAllowed()) return;
            if (!ValidStage(stage)) return;
            List<TankLoad> tanks = design.stages[stage].tanks;
            if (tank < 0 || tank >= tanks.Count) return;
            tanks.RemoveAt(tank);
            MarkChanged();
        }

        /// <summary>Installs a payload module in the head section (public for tools and tests).</summary>
        public void AddPayload(string moduleId)
        {
            if (!CrewEditAllowed()) return;
            ModuleDefinition m = ModuleCatalog.Find(moduleId);
            if (design == null || m == null || !m.IsPayload) return;
            design.payloadModules.Add(moduleId);
            int index = design.payloadModules.Count - 1;
            focusRequest = p => p.Stage == -1 && p.Index == index && p.IsModule;
            MarkChanged();
        }

        /// <summary>Mounts a side set (fins, RCS, landing legs) on a stage.</summary>
        void AddAttachment(int stage, string moduleId)
        {
            if (!CrewEditAllowed()) return;
            if (!ValidStage(stage)) return;
            design.stages[stage].EnsureLists();
            design.stages[stage].attachments.Add(moduleId);
            MarkChanged();
        }

        void RemoveAttachment(int stage, int index)
        {
            if (!CrewEditAllowed()) return;
            if (!ValidStage(stage)) return;
            List<string> list = design.stages[stage].attachments;
            if (list == null || index < 0 || index >= list.Count) return;
            list.RemoveAt(index);
            MarkChanged();
        }

        void RemovePayload(int index)
        {
            if (!CrewEditAllowed()) return;
            if (design == null || index < 0 || index >= design.payloadModules.Count) return;
            design.payloadModules.RemoveAt(index);
            MarkChanged();
        }

        /// <summary>
        /// Installs any catalog module where its <see cref="ModuleDefinition.mount"/> says: the engine slot, the tank stack or the side
        /// of <paramref name="stage"/>, or the head section. False when the hangar is closed or the module is unknown or not owned.
        /// </summary>
        public bool Install(string moduleId, int stage)
        {
            if (!CrewEditAllowed()) return false;
            ModuleDefinition m = ModuleCatalog.Find(moduleId);
            if (!open || m == null || IsLocked(m)) return false;
            switch (m.mount)
            {
                case ModuleMount.StageEngine: SetEngine(stage, moduleId); break;
                case ModuleMount.StageTank: AddTank(stage, moduleId); break;
                case ModuleMount.StageSide: AddAttachment(stage, moduleId); break;
                default: AddPayload(moduleId); break;
            }
            Refresh();
            return true;
        }

        /// <summary>
        /// Installation from the menu (a click on a tile or a drop on the rocket): explains where the part went and which construction
        /// issues or design warnings appeared because of it. Nothing is refused for being unusual.
        /// </summary>
        void InstallFromMenu(string moduleId, int stage)
        {
            ModuleDefinition m = ModuleCatalog.Find(moduleId);
            if (m == null || design == null) return;
            selectedModule = m.id;
            if (IsLocked(m))
            {
                SetStatus(m.name + Loc.T(" ещё не куплен — купите его в магазине (") + FlightInput.KeyLabel(InputKey.K) + ").", HudStyle.Warning);
                if (shop != null && !shop.IsOpen) shop.Toggle();
                return;
            }
            bool onStage = m.mount != ModuleMount.Payload;
            if (onStage && design.stages.Count == 0)
            {
                SetStatus(Loc.T("Сначала добавьте ступень: ") + m.name + Loc.T(" ставится на ступень."), HudStyle.Warning);
                return;
            }
            if (onStage) stage = Math.Max(0, Math.Min(design.stages.Count - 1, stage));
            Refresh();
            Dictionary<string, int> before = RemarkCounts(compiled, report);
            if (!Install(moduleId, stage)) return;
            if (onStage) selectedStage = stage;
            List<string> added = NewRemarks(before, RemarkCounts(compiled, report));
            string where = onStage ? Loc.T("ступень ") + (stage + 1) : Loc.T("головной отсек");
            string text = Loc.T("Установлено: ") + m.name + " → " + where + ".";
            if (added.Count > 0) SetStatus(text + " " + added[0] + (added.Count > 1 ? Loc.T(" (ещё замечаний: ") + (added.Count - 1) + ")" : ""), HudStyle.Warning);
            else SetStatus(text, HudStyle.Accent);
        }

        /// <summary>Messages of all construction issues and design warnings, counted (the same text can appear twice).</summary>
        static Dictionary<string, int> RemarkCounts(CompiledRocket c, SpaceSim.Flight.RocketDesignReport r)
        {
            var counts = new Dictionary<string, int>();
            void Add(string s)
            {
                if (string.IsNullOrEmpty(s)) return;
                counts[s] = counts.TryGetValue(s, out int n) ? n + 1 : 1;
            }
            if (c != null) foreach (BlueprintIssue i in c.Issues) Add(i.Message);
            if (r != null) foreach (SpaceSim.Flight.DesignWarning w in r.Warnings) Add(w.Message);
            return counts;
        }

        static List<string> NewRemarks(Dictionary<string, int> before, Dictionary<string, int> after)
        {
            var list = new List<string>();
            foreach (KeyValuePair<string, int> kv in after)
            {
                // The same message repeated (one per engine, tank, stage…) is shown once with its count.
                before.TryGetValue(kv.Key, out int was);
                int added = kv.Value - was;
                if (added == 1) list.Add(kv.Key);
                else if (added > 1) list.Add(kv.Key + " (×" + added + ")");
            }
            return list;
        }

        void AddStageOnTop()
        {
            if (!CrewEditAllowed()) return;
            design.stages.Add(new StageBlueprint { name = Loc.T("Ступень ") + (design.stages.Count + 1), engineId = "", engineCount = 1 });
            selectedStage = design.stages.Count - 1;
            RenumberStages();
            MarkChanged();
        }

        void RemoveStage(int stage)
        {
            if (!CrewEditAllowed()) return;
            if (!ValidStage(stage)) return;
            design.stages.RemoveAt(stage);
            if (selectedStage >= design.stages.Count) selectedStage = design.stages.Count - 1;
            if (selectedStage < 0) selectedStage = 0;
            RenumberStages();
            MarkChanged();
        }

        /// <summary>Keeps default stage names in flight order ("Ступень 1" burns first) after stages are added or removed.</summary>
        void RenumberStages()
        {
            for (int i = 0; i < design.stages.Count; i++)
            {
                StageBlueprint s = design.stages[i];
                if (string.IsNullOrEmpty(s.name) || s.name.StartsWith("Ступень", StringComparison.Ordinal))
                    s.name = "Ступень " + (i + 1);
            }
        }

        void ResetToDefault()
        {
            if (!CrewEditAllowed()) return;
            design = RocketBlueprint.CreateDefault();
            selectedStage = 0;
            MarkChanged();
            facility?.Select(null);
            SetStatus(Loc.T("Проект сброшен к стандартной ракете — «Вывезти на старт», чтобы поставить её на площадку."), HudStyle.Accent);
        }

        /// <summary>"Новый": an empty project — one stage without engine and tanks, a probe core in the head section.</summary>
        void NewProject()
        {
            if (!CrewEditAllowed()) return;
            design = new RocketBlueprint { name = Loc.T("Новая ракета") };
            design.stages.Add(new StageBlueprint { name = "Ступень 1", engineId = "", engineCount = 1 });
            design.payloadModules.Add(ModuleCatalog.ProbeCore);
            selectedStage = 0;
            MarkChanged();
            facility?.Select(null);
            SetStatus(Loc.T("Новый проект: одна пустая ступень и зонд. Добавьте двигатель и баки из каталога."), HudStyle.Accent);
        }

        void OpenProject(string name)
        {
            if (!CrewEditAllowed()) return;
            RocketBlueprint loaded = HangarProjectStore.Load(name, out string message);
            if (loaded == null)
            {
                SetStatus(message, HudStyle.Warning);
                return;
            }
            design = loaded;
            selectedStage = 0;
            MarkChanged();
            facility?.Select(null);
            projectsOpen = false;
            SetStatus(message, HudStyle.Accent);
        }

        void SaveProject()
        {
            if (design == null) return;
            bool ok = HangarProjectStore.Save(design, ProjectsReadOnly, out string message);
            SetStatus(message, ok ? HudStyle.Accent : HudStyle.Warning);
        }

        void DeleteProject(string name)
        {
            bool ok = HangarProjectStore.Delete(name, ProjectsReadOnly, out string message);
            SetStatus(message, ok ? HudStyle.Accent : HudStyle.Warning);
            deleteArmed = null;
        }

        /// <summary>The profile is read-only (saved by a newer build): named projects are not written either.</summary>
        bool ProjectsReadOnly => session != null && session.ReadOnly;

        static bool IsSidePiece(PartRole r) => r == PartRole.SideFin || r == PartRole.GridFin || r == PartRole.RcsBlock || r == PartRole.LandingLeg;

        /// <summary>The selected part can be taken off: an engine, a tank, a side set or any payload module the player installed
        /// (the probe core added automatically for a rocket without a cabin cannot).</summary>
        bool CanRemove(PartPlacement p)
        {
            if (design == null) return false;
            switch (p.Role)
            {
                case PartRole.Engine: return ValidStage(p.Stage) && !string.IsNullOrEmpty(design.stages[p.Stage].engineId);
                case PartRole.Tank: return ValidStage(p.Stage) && p.Index >= 0 && p.Index < design.stages[p.Stage].tanks.Count;
            }
            if (IsSidePiece(p.Role))
            {
                List<string> list = ValidStage(p.Stage) ? design.stages[p.Stage].attachments : null;
                return list != null && p.Index >= 0 && p.Index < list.Count;
            }
            return p.Stage == -1 && p.Index >= 0 && p.Index < design.payloadModules.Count && design.payloadModules[p.Index] == p.ModuleId;
        }

        /// <summary>Removes the part selected on the rocket (see <see cref="CanRemove"/>).</summary>
        void RemoveSelectedPart(PartPlacement p)
        {
            if (!CanRemove(p)) return;
            if (p.Role == PartRole.Engine) RemoveEngine(p.Stage);
            else if (p.Role == PartRole.Tank) RemoveTank(p.Stage, p.Index);
            else if (IsSidePiece(p.Role)) RemoveAttachment(p.Stage, p.Index);
            else RemovePayload(p.Index);
            facility?.Select(null);
        }

        void ApplyDesign()
        {
            if (!body.ApplyBlueprint(design))
            {
                state?.Notify(Loc.T("Ангар: не удалось применить проект — ракета не на стартовой площадке."));
                return;
            }
            if (state != null && report != null)
            {
                int remarks = compiled.Issues.Count + report.Warnings.Count;
                state.Notify(Loc.T("Ангар: на площадку вывезена ракета «") + Loc.T(design.name) + Loc.T("» — ступеней: ") + design.stages.Count +
                             Loc.T(", стартовая масса ") + Mass(report.LaunchMass) + Loc.T(", идеальный Δv ") + Speed(report.TotalDeltaVVacuum) +
                             (remarks > 0 ? Loc.T(". Замечаний: ") + remarks + Loc.T(" (см. ангар).") : "."));
            }
        }

        // ---- Sums over the design (display only) ------------------------------------------------

        /// <summary>How many of this module are on the rocket (engines counted per engine).</summary>
        int CountOnRocket(string id)
        {
            if (design == null) return 0;
            int n = 0;
            foreach (StageBlueprint s in design.stages)
            {
                if (s.engineId == id) n += Math.Max(1, Math.Min(BlueprintCompiler.MaxEnginesPerStage, s.engineCount));
                foreach (TankLoad t in s.tanks) if (t.moduleId == id) n++;
                if (s.attachments != null) foreach (string a in s.attachments) if (a == id) n++;
            }
            foreach (string p in design.payloadModules) if (p == id) n++;
            return n;
        }

        /// <summary>Catalog price of every part of the design, and of the parts the player still has to buy (each module once).</summary>
        void ProjectPrice(out long total, out long toBuy)
        {
            long sum = 0, buy = 0;
            total = 0;
            toBuy = 0;
            if (design == null) return;
            var missing = new HashSet<string>();
            void Add(string id, int count)
            {
                ModuleDefinition m = ModuleCatalog.Find(id);
                if (m == null || count <= 0) return;
                sum += (long)m.price * count;
                if (IsLocked(m) && missing.Add(id)) buy += m.price;
            }
            foreach (StageBlueprint s in design.stages)
            {
                if (!string.IsNullOrEmpty(s.engineId)) Add(s.engineId, Math.Max(1, Math.Min(BlueprintCompiler.MaxEnginesPerStage, s.engineCount)));
                foreach (TankLoad t in s.tanks) Add(t.moduleId, 1);
                if (s.attachments != null) foreach (string a in s.attachments) Add(a, 1);
            }
            foreach (string p in design.payloadModules) Add(p, 1);
            total = sum;
            toBuy = buy;
        }
    }
}
