using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    // The collapsible panel left of the stage stack: the detailed project (engine count, tank fill sliders, removing parts),
    // the statistics of the design and the remarks (construction issues and design warnings). Drawn with GUILayout.
    public sealed partial class HangarUI
    {
        enum Tab { Design, Statistics, Warnings }
        static readonly string[] TabNames = { "Проект", "Характеристики", "Замечания" };

        Tab tab = Tab.Design;
        bool detailsCollapsed;
        Vector2 designScroll, statsScroll, warningsScroll;
        float detailsWidth;

        void DrawDetails()
        {
            HudStyle.WindowFrame(detailsRect, false);
            detailsWidth = detailsRect.width - 20f;
            float height = detailsRect.height - 12f;
            GUILayout.BeginArea(new Rect(detailsRect.x + 10f, detailsRect.y + 6f, detailsWidth, height));
            int remarks = compiled.Issues.Count + report.Warnings.Count;
            GUILayout.BeginHorizontal();
            float tw = (detailsWidth - 44f) / 3f;
            for (int i = 0; i < TabNames.Length; i++)
            {
                var t = (Tab)i;
                bool on = tab == t && !detailsCollapsed;
                string label = t == Tab.Warnings && remarks > 0 ? Loc.T(TabNames[i]) + " (" + remarks + ")" : Loc.T(TabNames[i]);
                if (GUILayout.Toggle(on, label, HudStyle.Tab, GUILayout.Width(tw), GUILayout.Height(26)) != on)
                    Defer(() =>
                    {
                        if (tab == t && !detailsCollapsed) detailsCollapsed = true;
                        else
                        {
                            tab = t;
                            detailsCollapsed = false;
                        }
                    });
            }
            if (GUILayout.Button(detailsCollapsed ? "▾" : "▴", HudStyle.Button, GUILayout.Width(30), GUILayout.Height(26)))
                Defer(() => detailsCollapsed = !detailsCollapsed);
            GUILayout.EndHorizontal();
            if (!detailsCollapsed)
            {
                GUILayout.Space(4);
                float content = height - 34f;
                switch (tab)
                {
                    case Tab.Design: DrawDesign(content); break;
                    case Tab.Statistics: DrawStatistics(content); break;
                    default: DrawWarnings(content); break;
                }
            }
            GUILayout.EndArea();
        }

        // ---- Project ------------------------------------------------------------------------------

        void DrawDesign(float height)
        {
            GUILayout.Label(Loc.T("Сверху вниз — как ракета стоит в ангаре. Ступень 1 работает первой, затем отделяется."), HudStyle.Small);
            designScroll = GUILayout.BeginScrollView(designScroll, GUILayout.Height(height - 22f));

            DrawPayloadSection();
            GUILayout.Space(4);
            if (Button(Loc.T("Добавить ступень сверху"), true)) Defer(AddStageOnTop);
            GUILayout.Space(4);
            if (design.stages.Count == 0)
                Colored(Loc.T("Ступеней нет — ракете нечем взлетать. Добавьте ступень, двигатель и баки."), note, HudStyle.Danger);
            for (int i = design.stages.Count - 1; i >= 0; i--)
            {
                DrawStage(i);
                GUILayout.Space(4);
            }
            GUILayout.Label(Loc.T("— сборочный стенд —"), compact);
            GUILayout.EndScrollView();
        }

        void DrawPayloadSection()
        {
            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.Label(Loc.T("Головной отсек (полезная нагрузка) · ") + Mass(compiled.PayloadMass), HudStyle.Title);
            for (int p = 0; p < design.payloadModules.Count; p++)
            {
                int index = p;
                string id = design.payloadModules[p];
                ModuleDefinition m = ModuleCatalog.Find(id);
                GUILayout.BeginHorizontal();
                GUILayout.Label(m != null ? m.name + " · " + Mass(m.dryMass) : Loc.T("неизвестный модуль «") + id + "»", HudStyle.Label);
                GUILayout.FlexibleSpace();
                if (facility != null && GUILayout.Button(Loc.T("Найти"), HudStyle.Button, GUILayout.Width(56)))
                    Defer(() => facility.FocusOn(q => q.Stage == -1 && q.Index == index && q.IsModule, true));
                if (GUILayout.Button(Loc.T("Убрать"), HudStyle.Button, GUILayout.Width(70))) Defer(() => RemovePayload(index));
                GUILayout.EndHorizontal();
            }
            if (design.payloadModules.Count == 0)
                GUILayout.Label(Loc.T("Пусто — ядро зонда будет добавлено автоматически."), HudStyle.Small);
            GUILayout.Label(Loc.T("Модули добавляются из каталога. Без кабины будет поставлен зонд. «Найти» — показать на ракете."), HudStyle.Small);
            GUILayout.EndVertical();
        }

        void DrawStage(int i)
        {
            StageBlueprint s = design.stages[i];
            StageReport sr = i < report.Stages.Count ? report.Stages[i] : null;
            int index = i;
            bool selected = i == selectedStage;
            float inner = detailsWidth - 46f;

            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.BeginHorizontal();
            Color c = GUI.color;
            if (selected) GUI.color = HudStyle.Accent;
            if (GUILayout.Toggle(selected, StageTitle(i), HudStyle.Button, GUILayout.Width(inner - 130f)) && !selected)
                Defer(() =>
                {
                    selectedStage = index;
                    facility?.FocusOn(q => q.Stage == index && (q.Role == PartRole.Tank || q.Role == PartRole.Engine || q.Role == PartRole.StageSpacer), false);
                });
            GUI.color = c;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Loc.T("Удалить ступень"), HudStyle.Button, GUILayout.Width(120))) Defer(() => RemoveStage(index));
            GUILayout.EndHorizontal();

            // Engine
            ModuleDefinition engine = string.IsNullOrEmpty(s.engineId) ? null : ModuleCatalog.Find(s.engineId);
            if (string.IsNullOrEmpty(s.engineId))
            {
                Colored(Loc.T("Двигатель: нет — выберите ступень и кликните двигатель в каталоге (или перетащите его на ступень)."), note, HudStyle.Warning);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(Loc.T("Двигатель: ") + (engine != null ? engine.name : s.engineId) + " × " + s.engineCount, HudStyle.Label);
                GUILayout.FlexibleSpace();
                if (Button("−", s.engineCount > 1, GUILayout.Width(28))) Defer(() => ChangeEngineCount(index, -1));
                if (Button("+", s.engineCount < BlueprintCompiler.MaxEnginesPerStage, GUILayout.Width(28)))
                    Defer(() => ChangeEngineCount(index, +1));
                if (GUILayout.Button(Loc.T("Снять"), HudStyle.Button, GUILayout.Width(60))) Defer(() => RemoveEngine(index));
                GUILayout.EndHorizontal();
            }

            // Tanks
            if (s.tanks.Count == 0 && (engine == null || engine.engineKind != EngineKind.Solid))
                GUILayout.Label(Loc.T("Баков нет — добавьте из каталога «Топливные баки»."), HudStyle.Small);
            for (int t = 0; t < s.tanks.Count; t++)
            {
                int tankIndex = t;
                TankLoad load = s.tanks[t];
                ModuleDefinition tank = ModuleCatalog.Find(load.moduleId);
                GUILayout.BeginHorizontal();
                GUILayout.Label(tank != null ? tank.name + " · " + BlueprintCompiler.Name(tank.propellant) : Loc.T("неизвестный бак «") + load.moduleId + "»",
                    HudStyle.Label);
                GUILayout.FlexibleSpace();
                if (facility != null && GUILayout.Button(Loc.T("Найти"), HudStyle.Button, GUILayout.Width(56)))
                    Defer(() => facility.FocusOn(q => q.Role == PartRole.Tank && q.Stage == index && q.Index == tankIndex, true));
                if (GUILayout.Button(Loc.T("Убрать"), HudStyle.Button, GUILayout.Width(70))) Defer(() => RemoveTank(index, tankIndex));
                GUILayout.EndHorizontal();

                if (tank == null || !tank.IsTank || tank.propellantCapacity <= 0.0) continue;
                double capacity = tank.propellantCapacity;
                double shown = Math.Max(0.0, Math.Min(capacity, load.propellantLoaded));
                GUILayout.BeginHorizontal();
                float slider = GUILayout.HorizontalSlider((float)shown, 0f, (float)capacity, GUILayout.Width(Mathf.Max(80f, inner - 165f)));
                GUILayout.Label(Tonnes(shown) + " / " + Tonnes(capacity) + Loc.T(" т (") + N(shown / capacity * 100.0, 0) + " %)", compact,
                    GUILayout.Width(160));
                GUILayout.EndHorizontal();
                if (Math.Abs(slider - shown) > capacity * 1e-4 && CrewEditAllowed())
                {
                    // The value itself can change mid-pass (same controls); flags/status/recompile wait until after the pass.
                    double step = capacity * SliderStep;
                    load.propellantLoaded = Math.Max(0.0, Math.Min(capacity, Math.Round(slider / step) * step));
                    Defer(MarkChanged);
                }
                if (engine != null && engine.IsEngine && tank.propellant != engine.propellant)
                    Colored(Loc.T("Топливо не подходит двигателю ") + engine.name + " (" + BlueprintCompiler.Name(engine.propellant) +
                            Loc.T(") — его нечем сжечь, это мёртвый груз."), note, HudStyle.Danger);
            }

            // Side hardware
            if (s.attachments != null)
            {
                for (int a = 0; a < s.attachments.Count; a++)
                {
                    int attachmentIndex = a;
                    ModuleDefinition part = ModuleCatalog.Find(s.attachments[a]);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(Loc.T("На борту: ") + (part != null ? part.name + " · " + Mass(part.dryMass) : s.attachments[a]), HudStyle.Label);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(Loc.T("Убрать"), HudStyle.Button, GUILayout.Width(70))) Defer(() => RemoveAttachment(index, attachmentIndex));
                    GUILayout.EndHorizontal();
                }
            }

            if (sr != null)
            {
                GUILayout.Label(Loc.T("Ступень: сухая ") + Mass(sr.DryMass) + Loc.T(" · топливо ") + Mass(sr.PropellantMass) +
                                " · Δv " + Speed(sr.DeltaVVacuum) + Loc.T(" (идеально)"), HudStyle.Small);
            }
            GUILayout.EndVertical();
        }

        string StageTitle(int i)
        {
            if (i == 0) return Loc.T("Ступень 1 (первой работает)");
            if (i == design.stages.Count - 1) return Loc.T("Ступень ") + (i + 1) + Loc.T(" (верхняя)");
            return Loc.T("Ступень ") + (i + 1);
        }

        // ---- Statistics ---------------------------------------------------------------------------

        void DrawStatistics(float height)
        {
            GUILayout.Label(Loc.T("Все цифры здесь — идеально (уровень A): уравнение Циолковского без потерь на гравитацию, сопротивление ") +
                            Loc.T("воздуха и управление. В настоящем полёте (уровень B) Δv понадобится больше."), HudStyle.Small);
            statsScroll = GUILayout.BeginScrollView(statsScroll, GUILayout.Height(height - 46f));
            const float lw = 185f;

            HudStyle.Row(Loc.T("Стартовая масса"), Mass(report.LaunchMass), lw);
            HudStyle.Row(Loc.T("Сухая масса (с отсеком)"), Mass(report.DryMass), lw);
            HudStyle.Row(Loc.T("  в т.ч. головной отсек"), Mass(compiled.PayloadMass), lw);
            HudStyle.Row(Loc.T("Топливо рабочее / ёмкость"), Tonnes(compiled.UsableFuel) + " / " + Tonnes(compiled.MaxFuelCapacity) + Loc.T(" т"), lw);
            if (compiled.UnusableFuel > 0.0)
            {
                ColoredRow(Loc.T("Неиспользуемое топливо"), Mass(compiled.UnusableFuel), lw, HudStyle.Danger);
                Colored(Loc.T("Это топливо не подходит двигателю своей ступени — его везут как мёртвый груз."), note, HudStyle.Danger);
            }
            HudStyle.Row(Loc.T("Макс. тяга у поверхности"), Force(report.MaxSurfaceThrust), lw);
            HudStyle.Row(Loc.T("Макс. тяга в вакууме"), Force(report.MaxVacuumThrust), lw);

            StageReport first = report.Stages.Count > 0 ? report.Stages[0] : null;
            bool firstHasEngine = first != null && first.VacuumIsp > 0.0;
            HudStyle.Row(Loc.T("Isp ст. 1 (пов. / вак.)"), firstHasEngine ? N(first.SurfaceIsp, 0) + " / " + N(first.VacuumIsp, 0) + Loc.T(" с") : "—", lw);
            HudStyle.Row(Loc.T("Расход ṁ ступени 1"), firstHasEngine ? N(first.MassFlow, 1) + Loc.T(" кг/с") : "—", lw);
            ColoredRow(Loc.T("Тяга/вес на старте (TWR)"), N(report.LiftoffTwr, 2) + (report.CanLiftOff ? "" : "  < 1"), lw,
                report.CanLiftOff ? Color.white : HudStyle.Danger);
            HudStyle.Row(Loc.T("g на площадке"), N(report.SurfaceGravity, 2) + Loc.T(" м/с²"), lw);
            ColoredRow(Loc.T("Идеальный Δv (вакуум)"), Speed(report.TotalDeltaVVacuum), lw, HudStyle.Accent);

            GUILayout.Space(6);
            GUILayout.Label(Loc.T("По ступеням (в порядке работы)"), HudStyle.Title);
            if (report.Stages.Count == 0) GUILayout.Label(Loc.T("Ступеней нет."), HudStyle.Small);
            for (int i = 0; i < report.Stages.Count; i++)
            {
                StageReport sr = report.Stages[i];
                bool hasEngine = sr.VacuumThrust > 0.0 && sr.VacuumIsp > 0.0;
                HudStyle.Row(Loc.T("Ступень ") + (i + 1) + " · Δv", Speed(sr.DeltaVVacuum), lw);
                GUILayout.Label(hasEngine
                        ? Loc.T("   работает ") + BurnTime(sr.BurnTime) + Loc.T(" · TWR при включении ") + N(sr.IgnitionTwr, 2) +
                          (i == 0 ? Loc.T(" (тяга у поверхности)") : Loc.T(" (тяга в вакууме, g площадки)"))
                        : Loc.T("   нет двигателя — Δv не даёт"),
                    HudStyle.Small);
            }

            GUILayout.Space(6);
            DrawFlightCapability(lw);
            GUILayout.Space(6);
            DrawCapabilities(lw);
            GUILayout.EndScrollView();
        }

        void DrawFlightCapability(float lw)
        {
            GUILayout.Label(Loc.T("Возможности полёта"), HudStyle.Title);
            GUILayout.Label(Loc.T("Сравнение идеального запаса Δv с идеальной потребностью. Потери на гравитацию и сопротивление НЕ учтены — ") +
                            Loc.T("реальному полёту нужно больше. Отмеченная цель проверяется в замечаниях."), HudStyle.Small);
            if (targets == null || targets.Length == 0)
            {
                GUILayout.Label(Loc.T("Цели не рассчитаны."), HudStyle.Small);
                return;
            }
            var notes = new List<string>();
            for (int t = 0; t < targets.Length; t++)
            {
                MissionTarget target = targets[t];
                int index = t;
                bool selected = t == requiredTarget;
                bool enough = report.TotalDeltaVVacuum >= target.IdealDeltaV;
                if (GUILayout.Toggle(selected, " " + target.Name, GUILayout.Width(detailsWidth - 30f)) && !selected)
                    Defer(() =>
                    {
                        requiredTarget = index;
                        dirty = true;
                    });
                Color verdictColor = enough ? HudStyle.Accent : HudStyle.Danger;
                ColoredRow(Loc.T("   нужно ≥ ") + Speed(target.IdealDeltaV), enough ? Loc.T("✓ хватает") : Loc.T("✗ не хватает"), lw - 20f, verdictColor);
                Colored(enough
                        ? Loc.T("      запас ") + Speed(report.TotalDeltaVVacuum - target.IdealDeltaV) + Loc.T(" (идеально)")
                        : Loc.T("      не хватает ") + Speed(target.IdealDeltaV - report.TotalDeltaVVacuum) + Loc.T(" даже без потерь"),
                    compact, verdictColor);
                if (!string.IsNullOrEmpty(target.Notes) && !notes.Contains(target.Notes)) notes.Add(target.Notes);
            }
            foreach (string n in notes) GUILayout.Label(n, HudStyle.Small);
        }

        void DrawCapabilities(float lw)
        {
            VehicleCapabilities caps = compiled.Capabilities;
            GUILayout.Label(Loc.T("Оборудование"), HudStyle.Title);
            HudStyle.Row(Loc.T("Управление"), caps.CrewCapacity > 0 ? Loc.T("экипаж ") + caps.CrewCapacity + Loc.T(" чел.") : Loc.T("дистанционное"), lw);
            HudStyle.Row(Loc.T("Прогноз орбиты"), YesNo(caps.Has(ModuleCapability.OrbitPrediction)), lw);
            HudStyle.Row(Loc.T("Межпланетная навигация"), YesNo(caps.Has(ModuleCapability.InterplanetaryNavigation)), lw);
            HudStyle.Row(Loc.T("Анализ атмосферы"), YesNo(caps.Has(ModuleCapability.AtmosphereAnalysis)), lw);
            HudStyle.Row(Loc.T("Анализ образцов"), caps.Has(ModuleCapability.SampleAnalysis) ? Loc.T("да, ×") + N(caps.ResearchMultiplier, 1) : Loc.T("нет"), lw);
            if (caps.Measurements != ScienceMeasurement.None) GUILayout.Label(Loc.T("Измеряет: ") + MeasurementList(caps.Measurements), note);
            HudStyle.Row(Loc.T("Питание: выдаёт / потребляет"), Power(caps.GeneratedPower) + " / " + Power(caps.PowerConsumption), lw);
            if (caps.BatteryEnergy > 0.0) HudStyle.Row(Loc.T("Батареи"), N(caps.BatteryEnergy / 3.6e6, 1) + Loc.T(" кВт·ч"), lw);
            HudStyle.Row(Loc.T("Дальность связи"), caps.CommRange > 0.0 ? Distance(caps.CommRange) : Loc.T("нет"), lw);
            if (caps.ParachuteCount > 0) HudStyle.Row(Loc.T("Парашюты"), caps.ParachuteCount + Loc.T(" шт., ") + N(caps.ParachuteArea, 0) + Loc.T(" м²"), lw);
            if (caps.LegCount > 0) HudStyle.Row(Loc.T("Посадочные опоры"), caps.LegCount + Loc.T(" шт., до ") + N(caps.LegTouchdownSpeed, 1) + Loc.T(" м/с"), lw);
            if (caps.FinArea > 0.0) HudStyle.Row(Loc.T("Стабилизаторы"), N(caps.FinArea, 1) + Loc.T(" м²"), lw);
            if (caps.HeatShieldFluxLimit > 0.0) HudStyle.Row(Loc.T("Термощит"), N(caps.HeatShieldFluxLimit / 1.0e6, 1) + Loc.T(" МВт/м²"), lw);
            if (caps.CargoVolume > 0.0) HudStyle.Row(Loc.T("Грузовой объём"), N(caps.CargoVolume, 0) + Loc.T(" м³"), lw);
            if (caps.WheelCount > 0 || caps.HasRoverChassis || caps.HasRoverDrive)
                HudStyle.Row(Loc.T("Ровер"), caps.RoverAssembled ? Loc.T("собран") : Loc.T("не собран"), lw);
            HudStyle.Row(Loc.T("Радиационная защита"), Level(caps.RadiationShielding), lw);
            HudStyle.Row(Loc.T("Кислотостойкость"), Level(caps.AcidResistance), lw);
            HudStyle.Row(Loc.T("Образцы, до"), caps.SampleCapacity > 0.0 ? Mass(caps.SampleCapacity) : Loc.T("нет пробоотборника"), lw);
            if (caps.SampleKinds != SampleKinds.None) GUILayout.Label(Loc.T("Виды образцов: ") + KindList(caps.SampleKinds), note);
        }

        // ---- Remarks ------------------------------------------------------------------------------

        void DrawWarnings(float height)
        {
            GUILayout.Label(Loc.T("Замечания — это последствия, а не запреты: вывезти на старт можно любой проект."), HudStyle.Small);
            warningsScroll = GUILayout.BeginScrollView(warningsScroll, GUILayout.Height(height - 30f));
            int shown = 0;
            foreach (BlueprintIssue issue in compiled.Issues)
            {
                Colored("• " + issue.Message, note, HudStyle.Warning);
                shown++;
            }
            foreach (DesignWarning warning in report.Warnings)
            {
                bool severe = warning.Code == DesignWarningCode.CannotLiftOff || warning.Code == DesignWarningCode.InsufficientDeltaV;
                Colored("• " + warning.Message, note, severe ? HudStyle.Danger : HudStyle.Warning);
                shown++;
            }
            if (shown == 0)
                Colored(Loc.T("Замечаний нет: ракета оторвётся от площадки, идеального Δv хватает на отмеченную цель (потери в полёте не учтены)."),
                    note, HudStyle.Accent);
            GUILayout.EndScrollView();
        }
    }
}
