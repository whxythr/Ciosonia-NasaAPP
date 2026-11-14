using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    // The card next to the catalog while the pointer is over a tile: name, large preview, every meaningful figure of the part for its
    // kind, description, price, what installing it would change (computed by the Core on a copy of the design) and how to install it.
    public sealed partial class HangarUI
    {
        const float CardLabelWidth = 136f;

        Rect cardRect;
        string previewKey;
        InstallPreview preview;

        sealed class InstallPreview
        {
            public List<string> Remarks;
            public double DeltaVBefore, DeltaVAfter, MassBefore, MassAfter, TwrBefore, TwrAfter;
        }

        struct CardRow
        {
            public string Label, Value;
            /// <summary>Long text: drawn on its own wrapped line under the label.</summary>
            public bool Wide;
        }

        /// <summary>Where a click on the tile would put the part: the selected stage, or −1 for the head section.</summary>
        int ClickTarget(ModuleDefinition m) =>
            m.mount == ModuleMount.Payload ? -1 : Math.Max(0, Math.Min(design.stages.Count - 1, selectedStage));

        bool DrawCard(Vector2 screen)
        {
            if (string.IsNullOrEmpty(hoveredModule)) return false;
            ModuleDefinition m = ModuleCatalog.Find(hoveredModule);
            if (m == null) return false;
            bool locked = IsLocked(m);
            int stage = ClickTarget(m);
            List<CardRow> rows = CardRows(m);
            InstallPreview p = PreviewOf(m, stage);
            float w = CardWidth, inner = w - 24f;

            string effect = null;
            if (p != null)
                effect = Loc.T("После установки: Δv ") + Speed(p.DeltaVBefore) + " → " + Speed(p.DeltaVAfter) + " · TWR " + N(p.TwrBefore, 2) + " → " +
                         N(p.TwrAfter, 2) + Loc.T(" · масса ") + Mass(p.MassAfter);
            string hint = CardHint(m, locked, stage);
            string price = Loc.T("Цена: ") + Price(m.price) +
                           (locked ? Loc.T(" · не куплено") : m.requiresPurchase ? Loc.T(" · куплено") : "");
            int count = CountOnRocket(m.id);
            if (count > 0) price += Loc.T(" · на ракете: ") + count + Loc.T(" шт.");

            // Measure first, then place the card beside the tile, inside the screen.
            float h = 10f + 22f + 18f + 6f + 104f + 6f;
            foreach (CardRow row in rows) h += row.Wide ? 16f + Height(cardText, row.Value, inner) : 18f;
            h += 6f + Height(cardText, m.description, inner) + 6f;
            if (effect != null) h += Height(cardText, effect, inner) + 2f;
            if (p != null) foreach (string r in p.Remarks) h += Height(cardText, "• " + r, inner) + 2f;
            h += 20f + Height(cardText, hint, inner) + 10f;
            float top = topRect.yMax + Gap, maxH = screen.y - Margin - top;
            h = Mathf.Min(h, maxH);
            float y = Mathf.Clamp(hoveredTile.y - 8f, top, screen.y - Margin - h);
            cardRect = new Rect(panelRect.xMax + Gap, y, w, h);

            HudStyle.DialogFrame(cardRect);
            float x = cardRect.x + 12f, cy = cardRect.y + 10f, bottom = cardRect.yMax - 8f;
            GUI.Label(new Rect(x, cy, inner, 22f), m.name, barTitle);
            cy += 22f;
            string group = ModuleCatalog.CategoryName(m.category);
            if (m.Subgroup != ModuleSubgroup.None) group += " · " + ModuleCatalog.SubgroupName(m.Subgroup);
            GUI.Label(new Rect(x, cy, inner, 18f), group, compact);
            cy += 24f;

            var big = new Rect(x, cy, inner, 104f);
            GUI.Box(big, GUIContent.none, HudStyle.Panel);
            Thumbnail(Inset(big, 6f), m, 72f, locked ? HudStyle.TextMuted : HudStyle.TextSoft);
            PropellantType fuel = TilePropellant(m);
            if (fuel != PropellantType.None)
            {
                Icon(new Rect(big.xMax - 26f, big.y + 6f, 20f, 20f), HangarIcons.Propellant(fuel), HangarIcons.PropellantColor(fuel));
                GUI.Label(new Rect(big.x + 8f, big.yMax - 20f, inner - 16f, 16f), PropellantName(fuel), compact);
            }
            cy += 110f;

            foreach (CardRow row in rows)
            {
                if (cy > bottom) break;
                if (row.Wide)
                {
                    GUI.Label(new Rect(x, cy, inner, 16f), row.Label, HudStyle.Label);
                    cy += 16f;
                    float rh = Height(cardText, row.Value, inner);
                    GUI.Label(new Rect(x, cy, inner, rh), row.Value, cardText);
                    cy += rh;
                }
                else
                {
                    GUI.Label(new Rect(x, cy, CardLabelWidth, 18f), row.Label, HudStyle.Label);
                    GUI.Label(new Rect(x + CardLabelWidth, cy, inner - CardLabelWidth, 18f), row.Value, valueSmall);
                    cy += 18f;
                }
            }
            cy += 6f;
            float dh = Height(cardText, m.description, inner);
            GUI.Label(new Rect(x, cy, inner, dh), m.description, cardText);
            cy += dh + 6f;
            if (effect != null)
            {
                float eh = Height(cardText, effect, inner);
                Colored(new Rect(x, cy, inner, eh), effect, note, HudStyle.Accent);
                cy += eh + 2f;
            }
            if (p != null)
                foreach (string r in p.Remarks)
                {
                    float rh = Height(cardText, "• " + r, inner);
                    Colored(new Rect(x, cy, inner, rh), "• " + r, note, HudStyle.Warning);
                    cy += rh + 2f;
                }
            Colored(new Rect(x, cy, inner, 18f), price, compactWhite, locked ? HudStyle.Amber : HudStyle.TextSoft);
            cy += 20f;
            GUI.Label(new Rect(x, cy, inner, Mathf.Max(16f, bottom - cy)), hint, cardText);
            return true;
        }

        static float Height(GUIStyle style, string text, float width) =>
            string.IsNullOrEmpty(text) ? 0f : Mathf.Max(16f, style.CalcHeight(new GUIContent(text), width));

        string CardHint(ModuleDefinition m, bool locked, int stage)
        {
            string click = HudStyle.KeyText(Loc.T("Клик"));
            string drag = HudStyle.KeyText(Loc.T("перетащите"));
            if (locked) return click + Loc.T(" — открыть магазин (") + FlightInput.KeyLabel(InputKey.K) + Loc.T("): деталь ещё не куплена.");
            if (m.mount == ModuleMount.Payload)
                return click + (m.IsWrap ? Loc.T(" — обернуть зонд защитным слоем") : Loc.T(" — установить в головной отсек")) +
                       Loc.T(", или ") + drag + Loc.T(" на ракету.");
            if (design.stages.Count == 0) return Loc.T("Сначала добавьте ступень (справа, «+ Ступень»).");
            string where = Loc.T("ступень ") + (stage + 1);
            switch (m.mount)
            {
                case ModuleMount.StageEngine:
                {
                    StageBlueprint s = design.stages[stage];
                    string now = string.IsNullOrEmpty(s.engineId) ? Loc.T("двигателя нет") : Loc.T("сейчас: ") + EngineSummary(s);
                    return click + Loc.T(" — поставить двигателем на ") + where + " (" + now + Loc.T("), или ") + drag + Loc.T(" на другую ступень справа или на ракету.");
                }
                case ModuleMount.StageTank:
                    return click + Loc.T(" — добавить бак сверху на ") + where + Loc.T(", заправленным полностью, или ") + drag + Loc.T(" на другую ступень.");
                default:
                    return click + Loc.T(" — закрепить комплект на ") + where + Loc.T(" (улетает вместе с ней), или ") + drag + Loc.T(" на другую ступень.");
            }
        }

        static string EngineSummary(StageBlueprint s)
        {
            if (string.IsNullOrEmpty(s.engineId)) return Loc.T("нет");
            ModuleDefinition e = ModuleCatalog.Find(s.engineId);
            return (e != null ? e.name : s.engineId) + " × " + s.engineCount;
        }

        /// <summary>
        /// What installing the part would change: the Core compiles and analyses a copy of the design with the part added; the card
        /// shows the new totals and the construction issues / design warnings that would appear. Cached until the design changes.
        /// </summary>
        InstallPreview PreviewOf(ModuleDefinition m, int stage)
        {
            string key = m.id + "|" + stage + "|" + editVersion;
            if (key == previewKey) return preview;
            previewKey = key;
            preview = null;
            if (IsLocked(m) || compiled == null || report == null) return null;
            if (m.mount != ModuleMount.Payload && !ValidStage(stage)) return null;
            RocketBlueprint copy = design.Clone();
            copy.EnsureLists();
            ApplyTo(copy, m, stage);
            CompiledRocket c = BlueprintCompiler.Compile(copy, body.OwnsModule);
            RocketDesignReport r = RocketDesignAnalysis.Analyze(c.Configuration, lastGravity, lastRequired);
            preview = new InstallPreview
            {
                Remarks = NewRemarks(RemarkCounts(compiled, report), RemarkCounts(c, r)),
                DeltaVBefore = report.TotalDeltaVVacuum,
                DeltaVAfter = r.TotalDeltaVVacuum,
                MassBefore = report.LaunchMass,
                MassAfter = r.LaunchMass,
                TwrBefore = report.LiftoffTwr,
                TwrAfter = r.LiftoffTwr
            };
            return preview;
        }

        /// <summary>The same placement rules as <see cref="Install"/>, on any blueprint (used for previews).</summary>
        static void ApplyTo(RocketBlueprint d, ModuleDefinition m, int stage)
        {
            if (m.mount == ModuleMount.Payload)
            {
                d.payloadModules.Add(m.id);
                return;
            }
            if (stage < 0 || stage >= d.stages.Count) return;
            StageBlueprint s = d.stages[stage];
            switch (m.mount)
            {
                case ModuleMount.StageEngine:
                    s.engineId = m.id;
                    s.engineCount = Math.Max(1, Math.Min(BlueprintCompiler.MaxEnginesPerStage, s.engineCount));
                    break;
                case ModuleMount.StageTank:
                    s.tanks.Add(new TankLoad { moduleId = m.id, propellantLoaded = m.propellantCapacity });
                    break;
                default:
                    s.EnsureLists();
                    s.attachments.Add(m.id);
                    break;
            }
        }

        /// <summary>Every figure of the part that matters for its kind (straight from the catalog data).</summary>
        List<CardRow> CardRows(ModuleDefinition m)
        {
            var rows = new List<CardRow>();
            void Add(string label, string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                rows.Add(new CardRow { Label = label, Value = value, Wide = value.Length > 24 });
            }

            Add(Loc.T("Масса (сухая)"), Mass(m.dryMass));
            switch (m.category)
            {
                case ModuleCategory.Cabin:
                    Add(Loc.T("Управление"), m.crewCapacity > 0 ? Loc.T("экипаж ") + m.crewCapacity + Loc.T(" чел.") : Loc.T("дистанционное"));
                    break;
                case ModuleCategory.Engine:
                    Add(Loc.T("Тип"), EngineKindName(m.engineKind));
                    Add(Loc.T("Тяга в вакууме"), Force(m.vacuumThrust));
                    Add(Loc.T("Isp в вакууме"), Isp(m.vacuumIsp));
                    Add(Loc.T("Isp у поверхности"), m.seaLevelIsp > 0.0 ? Isp(m.seaLevelIsp) : Loc.T("не работает"));
                    Add(Loc.T("Топливо"), PropellantName(m.engineKind == EngineKind.Solid ? PropellantType.Solid : m.propellant));
                    if (m.solidPropellantMass > 0.0) Add(Loc.T("Заряд внутри"), Mass(m.solidPropellantMass));
                    if (m.engineKind == EngineKind.Electric)
                    {
                        Add(Loc.T("Мощность"), Power(RocketEquations.ElectricPower(m.vacuumThrust, m.vacuumIsp, m.electricEfficiency)));
                        Add(Loc.T("КПД η"), N(m.electricEfficiency * 100.0, 0) + " %");
                    }
                    if (m.diameter > 0.0) Add(Loc.T("Срез сопла"), Metres(m.diameter));
                    Add(Loc.T("Дроссель"), m.engineKind == EngineKind.Solid ? Loc.T("нет: горит до конца") : Loc.T("есть"));
                    break;
                case ModuleCategory.FuelTank:
                    Add(Loc.T("Топливо"), PropellantName(m.propellant));
                    Add(Loc.T("Ёмкость"), Mass(m.propellantCapacity));
                    Add(Loc.T("Масса с топливом"), Mass(m.dryMass + m.propellantCapacity));
                    break;
                case ModuleCategory.Aerodynamics:
                    if (m.finArea > 0.0) Add(Loc.T("Площадь комплекта"), N(m.finArea, 1) + Loc.T(" м²"));
                    if (m.rcsThrust > 0.0) Add(Loc.T("Тяга РСУ"), Force(m.rcsThrust));
                    Add(Loc.T("Комплект"), Loc.T("на всю ступень"));
                    break;
            }

            if (m.parachuteArea > 0.0)
            {
                Add(Loc.T("Площадь купола"), N(m.parachuteArea, 1) + Loc.T(" м²"));
                Add(Loc.T("Коэфф. сопротивления"), N(m.parachuteCd, 2));
            }
            if (m.legCount > 0)
            {
                Add(Loc.T("Опоры"), m.legCount + Loc.T(" шт."));
                Add(Loc.T("Мягкая посадка до"), N(m.legTouchdownSpeed, 1) + Loc.T(" м/с"));
            }
            if (m.heatFluxLimit > 0.0) Add(Loc.T("Тепловой поток до"), N(m.heatFluxLimit / 1.0e6, 1) + Loc.T(" МВт/м²"));
            if (m.lightOutput > 0.0) Add(Loc.T("Световой поток"), N(m.lightOutput, 0) + Loc.T(" лм"));
            if (m.radiationShielding > 0) Add(Loc.T("Радиационная защита"), Level(m.radiationShielding));
            if (m.acidResistance > 0) Add(Loc.T("Кислотостойкость"), Level(m.acidResistance));
            switch (m.powerSource)
            {
                case PowerSourceKind.Solar: Add(Loc.T("Солнечное питание"), Power(m.powerOutput)); break;
                case PowerSourceKind.Steady: Add(Loc.T("Выдаёт постоянно"), Power(m.powerOutput)); break;
                case PowerSourceKind.Battery:
                    Add(Loc.T("Разряд до"), Power(m.powerOutput));
                    Add(Loc.T("Запас энергии"), N(m.batteryEnergy / 3.6e6, 1) + Loc.T(" кВт·ч"));
                    break;
            }
            if (m.powerDraw > 0.0) Add(Loc.T("Потребляет"), Power(m.powerDraw));
            if (m.antennaRange > 0.0) Add(Loc.T("Дальность связи"), Distance(m.antennaRange) + Loc.T(" при ") + Power(m.antennaPower));
            if (m.relay) Add(Loc.T("Ретранслятор"), Loc.T("да"));
            if (m.wheelLoad > 0.0) Add(Loc.T("Нагрузка на колесо"), Force(m.wheelLoad));
            if (m.wheelMaxSpeed > 0.0) Add(Loc.T("Скорость до"), N(m.wheelMaxSpeed, 1) + Loc.T(" м/с"));
            if (m.drillDepth > 0.0) Add(Loc.T("Глубина бурения"), Metres(m.drillDepth));
            if (m.sampleCapacity > 0.0) Add(Loc.T("Образцы, до"), Mass(m.sampleCapacity));
            if (m.sampleKinds != SampleKinds.None) Add(Loc.T("Виды образцов"), KindList(m.sampleKinds));
            switch (m.mechanismKind)
            {
                case MechanismKind.RoverChassis: Add(Loc.T("Назначение"), Loc.T("рама ровера")); break;
                case MechanismKind.RoverDrive: Add(Loc.T("Назначение"), Loc.T("привод колёс ровера")); break;
                case MechanismKind.Manipulator: Add(Loc.T("Назначение"), Loc.T("рука для образцов")); break;
            }
            if (m.cargoVolume > 0.0) Add(Loc.T("Объём"), N(m.cargoVolume, 0) + Loc.T(" м³"));
            if (m.pressurized) Add(Loc.T("Герметичный"), Loc.T("да"));
            if (m.measurements != ScienceMeasurement.None) Add(Loc.T("Измеряет"), MeasurementList(m.measurements));
            if (m.IsScanner)
            {
                Add(Loc.T("Поле обзора"), "±" + N(m.scanHalfAngle, 0) + "°");
                Add(Loc.T("Высота съёмки"), N(m.scanMinAltitude / 1000.0, 0) + "–" + N(m.scanMaxAltitude / 1000.0, 0) + Loc.T(" км"));
                Add(Loc.T("Разрешение"), N(m.scanIfov * 1000.0, 1) + Loc.T(" мрад"));
            }
            if (m.capabilities != ModuleCapability.None) Add(Loc.T("Возможности"), CapabilityList(m.capabilities));
            if (m.researchMultiplier != 1.0) Add(Loc.T("Научная ценность"), "×" + N(m.researchMultiplier, 1));
            if (m.length > 0.0) Add(Loc.T("Длина"), Metres(m.length));
            Add(Loc.T("Крепление"), MountName(m.mount));
            return rows;
        }
    }
}
