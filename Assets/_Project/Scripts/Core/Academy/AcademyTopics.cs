using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Physics;
using SpaceSim.Units;

namespace SpaceSim.Academy
{
    /// <summary>
    /// The 13 Flight Academy lessons. Worked examples and exercise answers are computed with the central physics functions
    /// from the current planet and the player's rocket, so the lessons describe exactly the game being played.
    /// </summary>
    public static class AcademyTopics
    {
        public const string MassThrust = "mass-thrust";
        public const string Gravity = "gravity";
        public const string Acceleration = "acceleration";
        public const string FuelConsumption = "fuel-consumption";
        public const string Tsiolkovsky = "tsiolkovsky";
        public const string DeltaVBudget = "delta-v";
        public const string OrbitalVelocity = "orbital-velocity";
        public const string EscapeVelocity = "escape-velocity";
        public const string Drag = "drag";
        public const string Insertion = "orbital-insertion";
        public const string Hohmann = "hohmann";
        public const string LaunchWindows = "launch-windows";
        public const string Rendezvous = "rendezvous";

        const double Tonne = 1000.0, KiloNewton = 1000.0, Km = 1000.0, Day = 86400.0, Minute = 60.0;
        const double G0 = RocketEquations.G0;

        static string N(double v) => AcademyNumber.Format(v);
        static double S3(double v) => AcademyNumber.RoundSignificant(v, 3);
        static FormulaVariable V(string symbol, string meaning, string unit) => new FormulaVariable(symbol, meaning, unit);

        public static AcademyTopic Find(IEnumerable<AcademyTopic> topics, string id)
        {
            foreach (var t in topics)
                if (t.Id == id) return t;
            return null;
        }

        public static List<AcademyTopic> Build(AcademyContext c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            var list = new List<AcademyTopic>
            {
                MassAndThrust(c), GravityTopic(c), AccelerationTopic(c), FuelConsumptionTopic(c), TsiolkovskyTopic(c), DeltaVTopic(c),
                OrbitalVelocityTopic(c), EscapeVelocityTopic(c), DragTopic(c), InsertionTopic(c), HohmannTopic(c), LaunchWindowsTopic(c),
                RendezvousTopic(c)
            };
            for (int i = 0; i < list.Count; i++) list[i].Number = i + 1;
            return list;
        }

        static AcademyExercise Exercise(string id, string question, double answer, string unit, string[] hints, string[] steps)
        {
            var e = new AcademyExercise { Id = id, Question = question, Answer = answer, Unit = unit };
            e.Hints.AddRange(hints);
            e.SolutionSteps.AddRange(steps);
            return e;
        }

        static string RocketLabel(AcademyContext c) => AcademyCalculators.HasRocket(c) ? Loc.T("вашей ракеты") : Loc.T("учебной ракеты");

        // ---- 1 ---------------------------------------------------------------------------------------------------

        static AcademyTopic MassAndThrust(AcademyContext c)
        {
            StageReport s1 = AcademyCalculators.Stage(c, 0);
            double launchMass = AcademyCalculators.HasRocket(c) && c.Design.LaunchMass > 0.0 ? c.Design.LaunchMass : s1.MassAtIgnition;
            double g = c.SurfaceGravity;
            double twr = RocketEquations.ThrustToWeight(s1.SurfaceThrust, launchMass, g);
            var t = new AcademyTopic
            {
                Id = Loc.T(MassThrust),
                Title = Loc.T("Масса ракеты и тяга"),
                Explanation =
                    Loc.T("Ракетный двигатель отбрасывает назад продукты сгорания со скоростью vₑ. По третьему закону Ньютона газ толкает ракету вперёд ") +
                    Loc.T("с силой F = ṁ·vₑ — это тяга. Удельный импульс Isp — та же скорость истечения, выраженная в секундах: vₑ = Isp·g₀. ") +
                    Loc.T("Чем выше Isp, тем меньше топлива уходит на ту же тягу.\n\n") +
                    Loc.T("Почти вся масса ракеты на старте — топливо. Чтобы ракета вообще оторвалась, тяга должна быть больше её веса: ") +
                    Loc.T("отношение тяга/вес (TWR) > 1. Когда топливо выгорает, масса падает и TWR растёт."),
                Example =
                    Loc.T("Первая ступень ") + RocketLabel(c) + " («" + s1.Name + "»): ṁ = " + N(s1.MassFlow) + Loc.T(" кг/с, Isp = ") + N(s1.VacuumIsp) + Loc.T(" с в вакууме и ") +
                    N(s1.SurfaceIsp) + Loc.T(" с у поверхности.\n") +
                    Loc.T("F_вак = ") + N(s1.MassFlow) + " · " + N(s1.VacuumIsp) + " · 9.80665 = " + N(s1.VacuumThrust / KiloNewton) + Loc.T(" кН;  F_пов = ") + N(s1.SurfaceThrust / KiloNewton) + Loc.T(" кН.\n") +
                    Loc.T("Стартовая масса ") + N(launchMass / Tonne) + Loc.T(" т, g = ") + N(g) + Loc.T(" м/с² → TWR = ") + N(s1.SurfaceThrust) + " / (" + N(launchMass) + " · " + N(g) + ") = " + N(twr) + ".",
                Application =
                    Loc.T("Ангар считает массу, тягу и TWR у старта по этим формулам. Кнопка ЗАПУСК не запрещает старт при TWR < 1 — двигатель будет работать, ") +
                    Loc.T("а ракета стоять на площадке и жечь топливо. В полёте панель РАКЕТА показывает «Тяга/вес (макс.)» для текущей массы и давления."),
                Limitations =
                    Loc.T("Тяга зависит от давления снаружи: у поверхности Isp и тяга меньше, чем в вакууме (в игре это учтено). ") +
                    Loc.T("TWR — статическая оценка: она не учитывает сопротивление воздуха и наклон ракеты.")
            };
            t.Formulas.Add(new AcademyFormula(Loc.T("m₀ = m_сух + m_топл + m_груз"),
                V("m₀", Loc.T("стартовая масса"), Loc.T("кг")), V(Loc.T("m_сух"), Loc.T("сухая масса ступеней: баки, двигатели"), Loc.T("кг")),
                V(Loc.T("m_топл"), Loc.T("масса топлива"), Loc.T("кг")), V(Loc.T("m_груз"), Loc.T("полезная нагрузка и приборы"), Loc.T("кг"))));
            t.Formulas.Add(new AcademyFormula("F = ṁ·Isp·g₀ = ṁ·vₑ",
                V("F", Loc.T("тяга"), Loc.T("Н")), V("ṁ", Loc.T("массовый расход топлива"), Loc.T("кг/с")), V("Isp", Loc.T("удельный импульс"), Loc.T("с")),
                V("g₀", Loc.T("стандартное ускорение 9.80665 (постоянная пересчёта, не местная гравитация)"), Loc.T("м/с²")), V("vₑ", Loc.T("скорость истечения"), Loc.T("м/с"))));
            t.Formulas.Add(new AcademyFormula("TWR = F / (m·g)",
                V("TWR", Loc.T("отношение тяги к весу"), Loc.T("безразмерно")), V("m", Loc.T("текущая масса"), Loc.T("кг")), V("g", Loc.T("местное ускорение свободного падения"), Loc.T("м/с²"))));
            t.CalculatorIds.Add(AcademyCalculators.Thrust);
            t.CalculatorIds.Add(AcademyCalculators.ThrustToWeight);

            double mdot = S3(s1.MassFlow), isp = S3(s1.VacuumIsp);
            double ve = RocketEquations.ExhaustVelocity(isp);
            double f = RocketEquations.ThrustFromMassFlow(mdot, isp);
            t.Exercise = Exercise("ex-thrust",
                Loc.T("Двигатель расходует ṁ = ") + N(mdot) + Loc.T(" кг/с топлива при Isp = ") + N(isp) + Loc.T(" с. Какова его тяга? Ответ в килоньютонах (кН)."),
                f / KiloNewton, Loc.T("кН"),
                new[] { Loc.T("Тяга — это расход, умноженный на скорость истечения."), Loc.T("Скорость истечения vₑ = Isp·g₀, где g₀ = 9.80665 м/с²."), Loc.T("Получится ответ в ньютонах — переведите в кН.") },
                new[] { "vₑ = Isp·g₀ = " + N(isp) + " · 9.80665 = " + N(ve) + Loc.T(" м/с"), "F = ṁ·vₑ = " + N(mdot) + " · " + N(ve) + " = " + N(f) + Loc.T(" Н"), "F = " + N(f / KiloNewton) + Loc.T(" кН") });
            return t;
        }

        // ---- 2 ---------------------------------------------------------------------------------------------------

        static AcademyTopic GravityTopic(AcademyContext c)
        {
            double g0 = c.SurfaceGravity;
            double g400 = PlanetaryGravity.AccelerationMagnitude(c.Mu, c.Radius + 400000.0);
            var t = new AcademyTopic
            {
                Id = Loc.T(Gravity),
                Title = Loc.T("Гравитация"),
                Explanation =
                    Loc.T("Любые две массы притягиваются с силой, обратно пропорциональной квадрату расстояния между их центрами. ") +
                    Loc.T("Расстояние r отсчитывается от ЦЕНТРА планеты, а не от поверхности, поэтому с высотой гравитация слабеет медленно.\n\n") +
                    Loc.T("На орбите гравитация никуда не исчезает: невесомость — это свободное падение, при котором аппарат всё время «промахивается» мимо планеты."),
                Example =
                    c.PlanetName + ": μ = " + N(c.Mu) + Loc.T(" м³/с², R = ") + N(c.Radius / Km) + Loc.T(" км.\n") +
                    Loc.T("У поверхности g = μ/R² = ") + N(g0) + Loc.T(" м/с².\n") +
                    Loc.T("На высоте 400 км: r = ") + N((c.Radius + 400000.0) / Km) + Loc.T(" км, g = ") + N(g400) + Loc.T(" м/с² — это ") + N(g400 / g0 * 100.0) + Loc.T(" % от поверхностной."),
                Application =
                    Loc.T("Телеметрия показывает g(r) = μ/r². На подъёме гравитация — главный источник потерь: каждую секунду, пока ракета летит вверх, ") +
                    Loc.T("она отнимает около g м/с скорости («Потери: гравит.» в панели РАКЕТА)."),
                Limitations =
                    Loc.T("Модель — сферическая однородная планета: горы и сжатие на g не влияют. В симуляции действует притяжение только того тела, ") +
                    Loc.T("в чьей сфере влияния находится аппарат (метод сопряжённых конических сечений).")
            };
            t.Formulas.Add(new AcademyFormula("F = G·M·m / r²",
                V("F", Loc.T("сила притяжения"), Loc.T("Н")), V("G", Loc.T("гравитационная постоянная 6.6743·10⁻¹¹"), Loc.T("м³/(кг·с²)")),
                V("M", Loc.T("масса планеты"), Loc.T("кг")), V("m", Loc.T("масса аппарата"), Loc.T("кг")), V("r", Loc.T("расстояние от центра планеты"), Loc.T("м"))));
            t.Formulas.Add(new AcademyFormula("g(r) = μ / r²,  μ = G·M",
                V("g", Loc.T("ускорение свободного падения"), Loc.T("м/с²")), V("μ", Loc.T("гравитационный параметр"), Loc.T("м³/с²"))));
            t.CalculatorIds.Add(AcademyCalculators.Gravity);

            double r = c.Radius + 400000.0;
            t.Exercise = Exercise("ex-gravity",
                Loc.T("Чему равно ускорение свободного падения на высоте 400 км над планетой «") + c.PlanetName + "»? μ = " + N(c.Mu) + Loc.T(" м³/с², R = ") + N(c.Radius / Km) + Loc.T(" км. Ответ в м/с²."),
                g400, Loc.T("м/с²"),
                new[] { Loc.T("В формулу подставляется расстояние от центра, а не высота."), Loc.T("r = R + h — и всё в метрах."), "g = μ/r²." },
                new[] { "r = " + N(c.Radius) + " + 400 000 = " + N(r) + Loc.T(" м"), "r² = " + N(r * r) + Loc.T(" м²"), "g = " + N(c.Mu) + " / " + N(r * r) + " = " + N(g400) + Loc.T(" м/с²") });
            return t;
        }

        // ---- 3 ---------------------------------------------------------------------------------------------------

        static AcademyTopic AccelerationTopic(AcademyContext c)
        {
            StageReport s1 = AcademyCalculators.Stage(c, 0);
            double g = c.SurfaceGravity;
            double a0 = RocketEquations.Acceleration(s1.SurfaceThrust, s1.MassAtIgnition);
            double net0 = RocketEquations.Acceleration(s1.SurfaceThrust - s1.MassAtIgnition * g, s1.MassAtIgnition);
            double a1 = RocketEquations.Acceleration(s1.VacuumThrust, s1.MassAtBurnout);
            var t = new AcademyTopic
            {
                Id = Loc.T(Acceleration),
                Title = Loc.T("Ускорение"),
                Explanation =
                    Loc.T("Второй закон Ньютона: ускорение равно сумме сил, делённой на массу. На ракету при вертикальном подъёме действуют тяга вверх и вес вниз, ") +
                    Loc.T("в атмосфере — ещё сопротивление.\n\n") +
                    Loc.T("Масса ракеты всё время уменьшается, поэтому при постоянной тяге ускорение растёт и максимально перед выключением ступени. ") +
                    Loc.T("Акселерометр («перегрузка») показывает только негравитационные силы — тягу и сопротивление; в свободном падении он показывает ноль."),
                Example =
                    Loc.T("Первая ступень ") + RocketLabel(c) + Loc.T(" у старта: a = F/m = ") + N(s1.SurfaceThrust / KiloNewton) + Loc.T(" кН / ") + N(s1.MassAtIgnition / Tonne) + Loc.T(" т = ") + N(a0) + Loc.T(" м/с²; ") +
                    Loc.T("вверх остаётся a − g = ") + N(net0) + Loc.T(" м/с².\n") +
                    Loc.T("Перед выгоранием (масса ") + N(s1.MassAtBurnout / Tonne) + Loc.T(" т, вакуум): a = ") + N(a1) + Loc.T(" м/с² ≈ ") + N(a1 / G0) + " g₀.",
                Application =
                    Loc.T("Телеметрия: «|a| полное», «Перегрузка (акселерометр)», в панели РАКЕТА — ускорение от тяги. Сильная перегрузка в конце ступени — ") +
                    Loc.T("повод уменьшить тягу (Ctrl) для хрупкой нагрузки."),
                Limitations = Loc.T("Формула a_верт — для строго вертикального полёта без сопротивления. При наклоне на подъём идёт только часть тяги.")
            };
            t.Formulas.Add(new AcademyFormula("a = F / m", V("a", Loc.T("ускорение"), Loc.T("м/с²")), V("F", Loc.T("сила (тяга)"), Loc.T("Н")), V("m", Loc.T("масса"), Loc.T("кг"))));
            t.Formulas.Add(new AcademyFormula(Loc.T("a_верт = (F − m·g) / m"), V(Loc.T("a_верт"), Loc.T("ускорение вверх при вертикальном подъёме"), Loc.T("м/с²")), V("g", Loc.T("местная гравитация"), Loc.T("м/с²"))));
            t.CalculatorIds.Add(AcademyCalculators.Acceleration);

            double fx = S3(s1.SurfaceThrust / KiloNewton), mx = S3(s1.MassAtIgnition / Tonne), gx = S3(g);
            if (fx * KiloNewton <= mx * Tonne * gx * 1.05)
            {
                fx = 3000.0;
                mx = 200.0;
            }
            double weight = mx * Tonne * gx;
            double net = RocketEquations.Acceleration(fx * KiloNewton - weight, mx * Tonne);
            t.Exercise = Exercise("ex-acceleration",
                Loc.T("Ракета массой ") + N(mx) + Loc.T(" т стартует вертикально с тягой ") + N(fx) + Loc.T(" кН, g = ") + N(gx) + Loc.T(" м/с². С каким ускорением она поднимается сразу после отрыва (без сопротивления)? Ответ в м/с²."),
                net, Loc.T("м/с²"),
                new[] { Loc.T("Действуют две силы: тяга вверх и вес вниз."), Loc.T("Вес W = m·g; результирующая сила — F − W."), Loc.T("Ускорение = результирующая сила / масса. Массу — в килограммах, силу — в ньютонах.") },
                new[] { "W = " + N(mx * Tonne) + " · " + N(gx) + " = " + N(weight) + Loc.T(" Н"), "F − W = " + N(fx * KiloNewton) + " − " + N(weight) + " = " + N(fx * KiloNewton - weight) + Loc.T(" Н"), "a = " + N(fx * KiloNewton - weight) + " / " + N(mx * Tonne) + " = " + N(net) + Loc.T(" м/с²") });
            return t;
        }

        // ---- 4 ---------------------------------------------------------------------------------------------------

        static AcademyTopic FuelConsumptionTopic(AcademyContext c)
        {
            StageReport s1 = AcademyCalculators.Stage(c, 0), s2 = AcademyCalculators.Stage(c, AcademyCalculators.LastStage(c));
            double t1 = RocketEquations.BurnTime(s1.PropellantMass, RocketEquations.MassFlow(s1.VacuumThrust, s1.VacuumIsp));
            double t2 = RocketEquations.BurnTime(s2.PropellantMass, RocketEquations.MassFlow(s2.VacuumThrust, s2.VacuumIsp));
            var t = new AcademyTopic
            {
                Id = Loc.T(FuelConsumption),
                Title = Loc.T("Расход топлива"),
                Explanation =
                    Loc.T("Для заданной тяги двигатель должен выбрасывать ṁ килограммов в секунду; чем выше скорость истечения (Isp), тем меньше нужно. ") +
                    Loc.T("Время работы ступени — запас топлива, делённый на расход.\n\n") +
                    Loc.T("Дроссель уменьшает расход и тягу в одной пропорции: ступень работает дольше, но Δv — тот же, потому что он зависит только от масс и Isp."),
                Example =
                    "«" + s1.Name + "»: ṁ = " + N(s1.MassFlow) + Loc.T(" кг/с, топлива ") + N(s1.PropellantMass / Tonne) + Loc.T(" т → t = ") + N(t1) + Loc.T(" с (") + N(t1 / Minute) + Loc.T(" мин).\n") +
                    "«" + s2.Name + "»: ṁ = " + N(RocketEquations.MassFlow(s2.VacuumThrust, s2.VacuumIsp)) + Loc.T(" кг/с, топлива ") + N(s2.PropellantMass / Tonne) + Loc.T(" т → t = ") + N(t2) + Loc.T(" с."),
                Application =
                    Loc.T("Панель РАКЕТА: «Расход ṁ» и «Топливо ступени». Shift/Ctrl меняют дроссель — следите, как меняется ṁ. Пустая ступень — лишний груз: отделите её (Пробел)."),
                Limitations =
                    Loc.T("Постоянные Isp и расход; без переходных режимов запуска и остатков топлива. У поверхности Isp ниже, поэтому при том же расходе тяга меньше.")
            };
            t.Formulas.Add(new AcademyFormula("ṁ = F / (Isp·g₀)", V("ṁ", Loc.T("массовый расход"), Loc.T("кг/с")), V("F", Loc.T("тяга"), Loc.T("Н")), V("Isp", Loc.T("удельный импульс"), Loc.T("с")), V("g₀", "9.80665", Loc.T("м/с²"))));
            t.Formulas.Add(new AcademyFormula(Loc.T("t = m_топл / ṁ"), V("t", Loc.T("время работы на полной тяге"), Loc.T("с")), V(Loc.T("m_топл"), Loc.T("запас топлива ступени"), Loc.T("кг"))));
            t.Formulas.Add(new AcademyFormula("m(t) = m₀ − ṁ·t", V("m(t)", Loc.T("масса через t секунд"), Loc.T("кг")), V("m₀", Loc.T("масса при зажигании"), Loc.T("кг"))));
            t.CalculatorIds.Add(AcademyCalculators.Burn);

            double fx = S3(s2.VacuumThrust / KiloNewton), ispx = S3(s2.VacuumIsp), mpx = S3(s2.PropellantMass / Tonne);
            double mdot = RocketEquations.MassFlow(fx * KiloNewton, ispx);
            double burn = RocketEquations.BurnTime(mpx * Tonne, mdot);
            t.Exercise = Exercise("ex-burn",
                Loc.T("Ступень с тягой ") + N(fx) + Loc.T(" кН и Isp ") + N(ispx) + Loc.T(" с несёт ") + N(mpx) + Loc.T(" т топлива. Сколько секунд она проработает на полной тяге?"),
                burn, Loc.T("с"),
                new[] { Loc.T("Сначала найдите расход ṁ."), Loc.T("ṁ = F/(Isp·g₀) — тягу подставьте в ньютонах."), Loc.T("t = m_топл/ṁ — массу в килограммах.") },
                new[] { "ṁ = " + N(fx * KiloNewton) + " / (" + N(ispx) + " · 9.80665) = " + N(mdot) + Loc.T(" кг/с"), "t = " + N(mpx * Tonne) + " / " + N(mdot) + " = " + N(burn) + Loc.T(" с") });
            return t;
        }

        // ---- 5 ---------------------------------------------------------------------------------------------------

        static AcademyTopic TsiolkovskyTopic(AcademyContext c)
        {
            StageReport s2 = AcademyCalculators.Stage(c, AcademyCalculators.LastStage(c));
            double dv = RocketEquations.DeltaV(s2.VacuumIsp, s2.MassAtIgnition, s2.MassAtBurnout);
            double extra = 0.4 * s2.PropellantMass;
            double dvMore = RocketEquations.DeltaV(s2.VacuumIsp, s2.MassAtIgnition + extra, s2.MassAtBurnout);
            string total = AcademyCalculators.HasRocket(c) ? Loc.T("Δv всей ракеты (сумма ступеней) — ") + N(c.Design.TotalDeltaVVacuum) + Loc.T(" м/с.") : "";
            var t = new AcademyTopic
            {
                Id = Loc.T(Tsiolkovsky),
                Title = Loc.T("Формула Циолковского"),
                Explanation =
                    Loc.T("Сколько скорости может набрать ракета, сжигая топливо? Каждая порция газа уносит импульс, а ракете приходится разгонять ещё не сожжённое ") +
                    Loc.T("топливо. В итоге Δv растёт как ЛОГАРИФМ отношения масс: удвоение топлива не удваивает Δv.\n\n") +
                    Loc.T("Отсюда главная идея ракетостроения — ступени: сбрасывая пустые баки, ракета перестаёт разгонять мёртвый груз."),
                Example =
                    "«" + s2.Name + "»: m₀ = " + N(s2.MassAtIgnition / Tonne) + Loc.T(" т, m_f = ") + N(s2.MassAtBurnout / Tonne) + Loc.T(" т, Isp = ") + N(s2.VacuumIsp) + Loc.T(" с → ") +
                    "Δv = " + N(s2.VacuumIsp * G0) + " · ln(" + N(s2.MassAtIgnition / s2.MassAtBurnout) + ") = " + N(dv) + Loc.T(" м/с.\n") +
                    Loc.T("Добавим 40 % топлива (+") + N(extra / Tonne) + Loc.T(" т): Δv = ") + N(dvMore) + Loc.T(" м/с — прирост лишь ") + N((dvMore / dv - 1.0) * 100.0) + " %. " + total,
                Application =
                    Loc.T("«Δv идеальн. остаток» в панели РАКЕТА и Δv ступеней в ангаре считаются этой формулой — одной и той же функцией ядра игры."),
                Limitations =
                    Loc.T("Это уровень A — идеальный расчёт: без гравитации, сопротивления и потерь на управление. Реальный разгон при подъёме меньше ") +
                    Loc.T("на сумму потерь, которую панель РАКЕТА показывает отдельно.")
            };
            t.Formulas.Add(new AcademyFormula("Δv = Isp·g₀·ln(m₀ / m_f)",
                V("Δv", Loc.T("идеальное изменение скорости"), Loc.T("м/с")), V("Isp", Loc.T("удельный импульс"), Loc.T("с")), V("g₀", "9.80665", Loc.T("м/с²")),
                V("m₀", Loc.T("масса в начале работы"), Loc.T("кг")), V("m_f", Loc.T("масса в конце (без сожжённого топлива)"), Loc.T("кг")), V("ln", Loc.T("натуральный логарифм"), "—")));
            t.CalculatorIds.Add(AcademyCalculators.DeltaV);

            double ispx = S3(s2.VacuumIsp), m0x = S3(s2.MassAtIgnition / Tonne), mfx = S3(s2.MassAtBurnout / Tonne);
            if (mfx >= m0x) mfx = S3(m0x * 0.4);
            double answer = RocketEquations.DeltaV(ispx, m0x, mfx);
            t.Exercise = Exercise("ex-tsiolkovsky",
                Loc.T("Ступень: Isp = ") + N(ispx) + Loc.T(" с, масса при зажигании ") + N(m0x) + Loc.T(" т, после выгорания ") + N(mfx) + Loc.T(" т. Каков её идеальный Δv? Ответ в м/с."),
                answer, Loc.T("м/с"),
                new[] { Loc.T("Отношение масс безразмерно — тонны можно не переводить в килограммы."), Loc.T("ln — натуральный логарифм (по основанию e)."), Loc.T("Скорость истечения vₑ = Isp·g₀.") },
                new[] { "vₑ = " + N(ispx) + " · 9.80665 = " + N(ispx * G0) + Loc.T(" м/с"), "m₀/m_f = " + N(m0x / mfx) + ",  ln = " + N(Math.Log(m0x / mfx)), "Δv = " + N(answer) + Loc.T(" м/с") });
            return t;
        }

        // ---- 6 ---------------------------------------------------------------------------------------------------

        static AcademyTopic DeltaVTopic(AcademyContext c)
        {
            MissionTarget[] targets = MissionRequirements.StandardTargets(c.Mu, c.Radius, c.SiteRotationSpeed, c.SpaceBoundary, c.HasAtmosphere);
            string table = "";
            foreach (var m in targets) table += "• " + m.Name + ": " + N(m.IdealDeltaV) + Loc.T(" м/с\n");
            string verdict = "";
            if (AcademyCalculators.HasRocket(c) && targets.Length > 1)
            {
                double margin = c.Design.TotalDeltaVVacuum - targets[1].IdealDeltaV;
                verdict = Loc.T("У ") + RocketLabel(c) + Loc.T(" идеальный Δv ") + N(c.Design.TotalDeltaVVacuum) + Loc.T(" м/с: запас над идеальным требованием орбиты ") + N(margin) + Loc.T(" м/с") +
                          (margin < 1000.0 ? Loc.T(" — на потери подъёма этого, скорее всего, не хватит.") : Loc.T(". Хватит ли его на потери — проверит полёт."));
            }
            StageReport s2 = AcademyCalculators.Stage(c, AcademyCalculators.LastStage(c));
            var t = new AcademyTopic
            {
                Id = Loc.T(DeltaVBudget),
                Title = Loc.T("Δv — бюджет миссии"),
                Explanation =
                    Loc.T("Δv — «валюта» космического полёта: каждая задача (подняться, выйти на орбиту, улететь) требует определённого изменения скорости. ") +
                    Loc.T("Ракета несёт запас Δv — сумму Δv своих ступеней. Миссия возможна, если запас больше требования С УЧЁТОМ потерь.\n\n") +
                    Loc.T("Идеальные требования (уровень A) — нижняя граница. Реально подъём через атмосферу стоит больше: часть тяги тратится на борьбу ") +
                    Loc.T("с гравитацией, сопротивлением и на повороты (уровень B — это и есть полёт)."),
                Example = Loc.T("Идеальные требования для планеты «") + c.PlanetName + Loc.T("» (старт на восток, вращение даёт ") + N(c.SiteRotationSpeed) + Loc.T(" м/с):\n") + table + verdict,
                Application =
                    Loc.T("Ангар: «Оценка возможностей» сравнивает Δv ракеты с этими требованиями. После полёта сравните «Δv израсходовано» и набранную скорость: ") +
                    Loc.T("разница — потери, и панель РАКЕТА разбивает их на гравитационные, аэродинамические и на управление."),
                Limitations =
                    Loc.T("Требования посчитаны для идеальных импульсов. Потери зависят от траектории и тяги/веса: медленная ракета теряет больше на гравитацию, ") +
                    Loc.T("быстрая в плотном воздухе — на сопротивление.")
            };
            t.Formulas.Add(new AcademyFormula(Loc.T("Δv_ракеты = Σ Isp_i·g₀·ln(m₀ᵢ / m_fᵢ)"), V("i", Loc.T("номер ступени"), "—"), V("m₀ᵢ, m_fᵢ", Loc.T("масса всей ракеты при зажигании и выгорании ступени i"), Loc.T("кг"))));
            t.Formulas.Add(new AcademyFormula(Loc.T("m_топл = m_f·(e^(Δv/(Isp·g₀)) − 1)"), V(Loc.T("m_топл"), Loc.T("топливо для получения Δv"), Loc.T("кг")), V("m_f", Loc.T("масса без этого топлива"), Loc.T("кг"))));
            t.Formulas.Add(new AcademyFormula(Loc.T("Δv_израсх = Δv_набрано + потери_грав + потери_сопр + потери_упр"), V(Loc.T("потери"), Loc.T("скорость, потраченная не на разгон (уровень B)"), Loc.T("м/с"))));
            t.CalculatorIds.Add(AcademyCalculators.Fuel);
            t.CalculatorIds.Add(AcademyCalculators.DeltaV);

            double dvx = 2500.0, ispx = S3(s2.VacuumIsp), mfx = S3(s2.MassAtBurnout / Tonne);
            double ratio = RocketEquations.MassRatio(dvx, ispx);
            double mp = RocketEquations.PropellantForDeltaV(dvx, ispx, mfx * Tonne);
            t.Exercise = Exercise("ex-fuel",
                Loc.T("Ступень вместе с грузом весит без топлива ") + N(mfx) + Loc.T(" т, Isp = ") + N(ispx) + Loc.T(" с. Сколько тонн топлива нужно, чтобы она дала Δv = ") + N(dvx) + Loc.T(" м/с?"),
                mp / Tonne, Loc.T("т"),
                new[] { Loc.T("Переверните формулу Циолковского: найдите отношение масс m₀/m_f = e^(Δv/vₑ)."), Loc.T("Топливо — это m₀ − m_f = m_f·(m₀/m_f − 1).") },
                new[] { "vₑ = " + N(ispx * G0) + Loc.T(" м/с"), "m₀/m_f = e^(" + N(dvx) + "/" + N(ispx * G0) + ") = " + N(ratio), Loc.T("m_топл = ") + N(mfx) + " · " + N(ratio - 1.0) + " = " + N(mp / Tonne) + Loc.T(" т") });
            return t;
        }

        // ---- 7 ---------------------------------------------------------------------------------------------------

        static AcademyTopic OrbitalVelocityTopic(AcademyContext c)
        {
            double low = c.LowOrbitAltitude;
            double rLow = c.Radius + low;
            double vLow = OrbitalMechanics.CircularVelocity(c.Mu, rLow);
            double tLow = OrbitalMechanics.CircularPeriod(c.Mu, rLow);
            double v400 = OrbitalMechanics.CircularVelocity(c.Mu, c.Radius + 400000.0);
            var t = new AcademyTopic
            {
                Id = Loc.T(OrbitalVelocity),
                Title = Loc.T("Орбитальная скорость"),
                Explanation =
                    Loc.T("Орбита — это падение, при котором аппарат движется вбок так быстро, что поверхность «уходит» из-под него с той же скоростью, с какой он падает. ") +
                    Loc.T("Для круговой орбиты гравитация как раз обеспечивает нужное центростремительное ускорение: v²/r = μ/r².\n\n") +
                    Loc.T("Нужна ГОРИЗОНТАЛЬНАЯ скорость. Ракета, летящая только вверх, поднимется и упадёт обратно, сколько бы топлива ни сожгла. ") +
                    Loc.T("Чем выше орбита, тем меньше на ней скорость — но тем больше энергии нужно, чтобы на неё подняться."),
                Example =
                    Loc.T("Орбита ") + N(low / Km) + Loc.T(" км над «") + c.PlanetName + "»: r = " + N(rLow / Km) + Loc.T(" км, v = √(μ/r) = ") + N(vLow) + Loc.T(" м/с, период T = ") + N(tLow / Minute) + Loc.T(" мин.\n") +
                    Loc.T("Орбита 400 км: v = ") + N(v400) + Loc.T(" м/с — медленнее.\n") +
                    Loc.T("Старт на восток экономит скорость вращения площадки: ") + N(c.SiteRotationSpeed) + Loc.T(" м/с."),
                Application =
                    Loc.T("Телеметрия показывает «v горизонт.» и ε = v²/2 − μ/r. Орбита достигнута, когда перицентр выше атмосферы — фаза полёта станет «орбита», ") +
                    Loc.T("за первую орбиту — +20 000 кр. С навигационным компьютером НК-1 элементы орбиты видны по O; без него — считайте сами."),
                Limitations =
                    Loc.T("Круговая орбита вокруг сферической планеты без атмосферы. Низкие реальные орбиты медленно тормозятся разреженным воздухом (в игре тоже: плотность нигде не обнуляется).")
            };
            t.Formulas.Add(new AcademyFormula(Loc.T("v_круг = √(μ / r)"), V(Loc.T("v_круг"), Loc.T("скорость круговой орбиты (инерциальная)"), Loc.T("м/с")), V("μ", Loc.T("гравитационный параметр"), Loc.T("м³/с²")), V("r", Loc.T("радиус орбиты от центра"), Loc.T("м"))));
            t.Formulas.Add(new AcademyFormula("T = 2π·√(r³ / μ)", V("T", Loc.T("период обращения"), Loc.T("с"))));
            t.Formulas.Add(new AcademyFormula("ε = v²/2 − μ/r", V("ε", Loc.T("удельная орбитальная энергия; < 0 — орбита замкнута"), Loc.T("Дж/кг"))));
            t.CalculatorIds.Add(AcademyCalculators.Orbit);

            double hx = S3(low / Km);
            double r = c.Radius + hx * Km;
            double answer = OrbitalMechanics.CircularVelocity(c.Mu, r);
            t.Exercise = Exercise("ex-orbit",
                Loc.T("Какая скорость нужна для круговой орбиты на высоте ") + N(hx) + Loc.T(" км над планетой «") + c.PlanetName + "»? μ = " + N(c.Mu) + Loc.T(" м³/с², R = ") + N(c.Radius / Km) + Loc.T(" км. Ответ в м/с."),
                answer, Loc.T("м/с"),
                new[] { Loc.T("Радиус орбиты отсчитывается от центра планеты."), Loc.T("r = R + h, в метрах; затем v = √(μ/r).") },
                new[] { "r = " + N(c.Radius) + " + " + N(hx * Km) + " = " + N(r) + Loc.T(" м"), "v = √(" + N(c.Mu) + " / " + N(r) + ") = " + N(answer) + Loc.T(" м/с") });
            return t;
        }

        // ---- 8 ---------------------------------------------------------------------------------------------------

        static AcademyTopic EscapeVelocityTopic(AcademyContext c)
        {
            double ve = OrbitalMechanics.EscapeVelocity(c.Mu, c.Radius);
            double rLow = c.Radius + c.LowOrbitAltitude;
            double veLow = OrbitalMechanics.EscapeVelocity(c.Mu, rLow);
            double vcLow = OrbitalMechanics.CircularVelocity(c.Mu, rLow);
            var t = new AcademyTopic
            {
                Id = Loc.T(EscapeVelocity),
                Title = Loc.T("Вторая космическая скорость"),
                Explanation =
                    Loc.T("Чтобы улететь навсегда, кинетической энергии должно хватить на подъём «до бесконечности»: v²/2 ≥ μ/r, то есть ε ≥ 0. ") +
                    Loc.T("Скорость ухода в √2 ≈ 1.414 раза больше круговой на той же высоте.\n\n") +
                    Loc.T("Уходить выгоднее с орбиты, а не прямо с поверхности: там ракета уже имеет круговую скорость, и добавить нужно лишь разницу."),
                Example =
                    Loc.T("У поверхности «") + c.PlanetName + Loc.T("»: v_уход = √(2μ/R) = ") + N(ve) + Loc.T(" м/с.\n") +
                    Loc.T("С орбиты ") + N(c.LowOrbitAltitude / Km) + Loc.T(" км: v_уход = ") + N(veLow) + Loc.T(" м/с, круговая там ") + N(vcLow) + Loc.T(" м/с → добавить ") + N(veLow - vcLow) + Loc.T(" м/с."),
                Application = Loc.T("Фаза полёта «уход» включается, когда ε ≥ 0. Достижение «Уход с планеты» даёт +30 000 кр и открывает тяжёлую защиту в магазине."),
                Limitations =
                    Loc.T("Только притяжение планеты: притяжение звезды и других тел не учтено. С поверхности с атмосферой разогнаться до v_уход внизу невозможно — ") +
                    Loc.T("сопротивление огромно; разгоняются выше атмосферы.")
            };
            t.Formulas.Add(new AcademyFormula(Loc.T("v_уход = √(2μ / r) = √2 · v_круг"), V(Loc.T("v_уход"), Loc.T("скорость ухода (вторая космическая)"), Loc.T("м/с")), V("r", Loc.T("расстояние от центра"), Loc.T("м"))));
            t.Formulas.Add(new AcademyFormula("ε = v²/2 − μ/r ≥ 0", V("ε", Loc.T("удельная орбитальная энергия"), Loc.T("Дж/кг"))));
            t.CalculatorIds.Add(AcademyCalculators.Escape);

            t.Exercise = Exercise("ex-escape",
                Loc.T("Найдите вторую космическую скорость у поверхности планеты «") + c.PlanetName + "» (r = R = " + N(c.Radius / Km) + Loc.T(" км, μ = ") + N(c.Mu) + Loc.T(" м³/с²). Ответ в м/с."),
                ve, Loc.T("м/с"),
                new[] { Loc.T("Аппарат улетает, когда удельная энергия ε = v²/2 − μ/r становится нулевой."), Loc.T("Из v²/2 = μ/R: v = √(2μ/R).") },
                new[] { "2μ/R = 2 · " + N(c.Mu) + " / " + N(c.Radius) + " = " + N(2.0 * c.Mu / c.Radius) + Loc.T(" м²/с²"), "v = √(" + N(2.0 * c.Mu / c.Radius) + ") = " + N(ve) + Loc.T(" м/с") });
            return t;
        }

        // ---- 9 ---------------------------------------------------------------------------------------------------

        static AcademyTopic DragTopic(AcademyContext c)
        {
            bool exponential = c.HasAtmosphere && !double.IsNaN(c.ScaleHeight);
            double rho0 = exponential ? c.Atmosphere.ReferenceDensity : 1.225;
            double scaleHeight = exponential ? c.ScaleHeight : 8500.0;
            var air = new ExponentialAtmosphere(rho0, scaleHeight, 0.0, 0.0);
            double cd = c.Rocket != null ? c.Rocket.dragCoefficient : 0.3;
            double area = c.Rocket != null ? c.Rocket.ReferenceArea : Math.PI * 1.85 * 1.85;
            double q10 = AtmosphericDrag.DynamicPressure(air.Density(10000.0), 500.0);
            string source = exponential ? Loc.T("атмосфера «") + c.PlanetName + "»" : Loc.T("земные значения (у «") + c.PlanetName + "» " + (c.HasAtmosphere ? Loc.T("табличная атмосфера") : Loc.T("нет атмосферы")) + ")";
            var t = new AcademyTopic
            {
                Id = Loc.T(Drag),
                Title = Loc.T("Сопротивление атмосферы"),
                Explanation =
                    Loc.T("Плотность воздуха падает с высотой экспоненциально: каждые H километров (шкала высот) — в e ≈ 2.72 раза. ") +
                    Loc.T("Сила сопротивления пропорциональна плотности и КВАДРАТУ скорости, поэтому быстро лететь низко — дорого.\n\n") +
                    Loc.T("Скоростной напор q = ½ρv² сначала растёт (ракета разгоняется), потом падает (воздух редеет) — максимум называют Max Q. ") +
                    Loc.T("Поэтому ракеты сначала поднимаются почти вертикально и набирают горизонтальную скорость уже в разреженном воздухе."),
                Example =
                    Loc.T("Источник: ") + source + ": ρ₀ = " + N(rho0) + Loc.T(" кг/м³, H = ") + N(scaleHeight / Km) + Loc.T(" км.\n") +
                    Loc.T("ρ(10 км) = ") + N(air.Density(10000.0)) + Loc.T(", ρ(30 км) = ") + N(air.Density(30000.0)) + ", ρ(" + N(c.SpaceBoundary / Km) + Loc.T(" км) = ") + N(air.Density(c.SpaceBoundary)) + Loc.T(" кг/м³.\n") +
                    Loc.T("На 10 км при 500 м/с: q = ") + N(q10 / 1000.0) + Loc.T(" кПа, сила на ракету (C_d = ") + N(cd) + ", A = " + N(area) + Loc.T(" м²) — ") + N(q10 * cd * area / KiloNewton) + Loc.T(" кН."),
                Application =
                    Loc.T("Телеметрия: «Плотность ρ», «Скоростной напор q», «Сопротивление a_drag»; панель РАКЕТА — «Потери: сопрот.». ") +
                    Loc.T("Воздух вращается вместе с планетой, поэтому сопротивление считается от скорости относительно воздуха."),
                Limitations = Loc.T("Изотермическая модель, постоянный C_d (на сверхзвуке он выше), без ветра и нагрева. На границе космоса плотность мала, но не ноль.")
            };
            t.Formulas.Add(new AcademyFormula("ρ(h) = ρ₀·e^(−h/H)", V("ρ", Loc.T("плотность воздуха"), Loc.T("кг/м³")), V("ρ₀", Loc.T("плотность у поверхности"), Loc.T("кг/м³")), V("h", Loc.T("высота"), Loc.T("м")), V("H", Loc.T("шкала высот"), Loc.T("м"))));
            t.Formulas.Add(new AcademyFormula("q = ½·ρ·v²", V("q", Loc.T("скоростной напор"), Loc.T("Па")), V("v", Loc.T("скорость относительно воздуха"), Loc.T("м/с"))));
            t.Formulas.Add(new AcademyFormula("F_d = q·C_d·A", V("F_d", Loc.T("сила сопротивления"), Loc.T("Н")), V("C_d", Loc.T("коэффициент сопротивления"), Loc.T("безразмерно")), V("A", Loc.T("площадь поперечного сечения"), Loc.T("м²"))));
            t.Formulas.Add(new AcademyFormula(Loc.T("v_пред = √(2·m·g / (ρ·C_d·A))"), V(Loc.T("v_пред"), Loc.T("предельная скорость падения: сопротивление = вес"), Loc.T("м/с"))));
            t.CalculatorIds.Add(AcademyCalculators.Drag);

            double rx = S3(rho0), hx = S3(scaleHeight / Km);
            var exAir = new ExponentialAtmosphere(rx, hx * Km, 0.0, 0.0);
            double rho = exAir.Density(10000.0);
            double q = AtmosphericDrag.DynamicPressure(rho, 600.0);
            t.Exercise = Exercise("ex-drag",
                Loc.T("Ракета летит на высоте 10 км со скоростью 600 м/с. ρ₀ = ") + N(rx) + Loc.T(" кг/м³, H = ") + N(hx) + Loc.T(" км. Каков скоростной напор q? Ответ в кПа."),
                q / 1000.0, Loc.T("кПа"),
                new[] { Loc.T("Сначала найдите плотность на высоте 10 км: ρ = ρ₀·e^(−h/H), h и H — в одних единицах."), "q = ½·ρ·v².", Loc.T("Паскали → килопаскали: делите на 1000.") },
                new[] { "ρ = " + N(rx) + " · e^(−10/" + N(hx) + ") = " + N(rho) + Loc.T(" кг/м³"), "q = 0.5 · " + N(rho) + " · 600² = " + N(q) + Loc.T(" Па"), "q = " + N(q / 1000.0) + Loc.T(" кПа") });
            return t;
        }

        // ---- 10 --------------------------------------------------------------------------------------------------

        static AcademyTopic InsertionTopic(AcademyContext c)
        {
            double apo = c.LowOrbitAltitude;
            double ra = c.Radius + apo;
            double dv0 = OrbitalMechanics.CircularizationDeltaV(c.Mu, ra, c.Radius);
            double dvDeep = OrbitalMechanics.CircularizationDeltaV(c.Mu, ra, c.Radius * 0.5);
            var t = new AcademyTopic
            {
                Id = Loc.T(Insertion),
                Title = Loc.T("Выход на орбиту"),
                Explanation =
                    Loc.T("Пока двигатель выключен, аппарат летит по эллипсу (или его куску). Если перицентр этого эллипса под поверхностью или в атмосфере — ") +
                    Loc.T("это ещё не орбита, аппарат упадёт. Выход на орбиту — разгон В АПОЦЕНТРЕ по направлению движения: он поднимает противоположную точку — перицентр.\n\n") +
                    Loc.T("Чем более пологой (горизонтальной) была траектория подъёма, тем выше перицентр и тем дешевле этот последний манёвр. ") +
                    Loc.T("Поэтому ракету постепенно наклоняют на восток — «гравитационный разворот»."),
                Example =
                    Loc.T("Апоцентр ") + N(apo / Km) + Loc.T(" км над «") + c.PlanetName + Loc.T("», перицентр на уровне поверхности: Δv = ") + N(dv0) + Loc.T(" м/с в апоцентре.\n") +
                    Loc.T("Если подъём был почти вертикальным (перицентр на половине радиуса от центра) — уже ") + N(dvDeep) + Loc.T(" м/с."),
                Application =
                    Loc.T("Без НК-1 апоцентр и перицентр вычисляйте сами по r, v и v_r (темы 7 и 8); с НК-1 нажмите O. У апоцентра v_r ≈ 0. ") +
                    Loc.T("Разгон — тангаж 90° (горизонтально), курс 90° (на восток)."),
                Limitations =
                    Loc.T("Импульсный манёвр. Реальный двигатель работает конечное время — начинайте разгон чуть раньше апоцентра. Сопротивление при низком перицентре не учтено.")
            };
            t.Formulas.Add(new AcademyFormula("a = (r_a + r_p) / 2", V("a", Loc.T("большая полуось эллипса"), Loc.T("м")), V("r_a, r_p", Loc.T("радиусы апоцентра и перицентра от центра"), Loc.T("м"))));
            t.Formulas.Add(new AcademyFormula("v(r) = √(μ·(2/r − 1/a))", V("v(r)", Loc.T("скорость на эллипсе на расстоянии r (формула vis-viva)"), Loc.T("м/с"))));
            t.Formulas.Add(new AcademyFormula("Δv = √(μ/r_a) − √(μ·(2/r_a − 1/a))", V("Δv", Loc.T("разгон в апоцентре до круговой скорости"), Loc.T("м/с"))));
            t.CalculatorIds.Add(AcademyCalculators.Insertion);

            double hax = S3(apo / Km), hpx = S3(c.SpaceBoundary / Km * 0.3);
            double rax = c.Radius + hax * Km, rpx = c.Radius + hpx * Km;
            double a = 0.5 * (rax + rpx);
            double va = OrbitalMechanics.VisVivaSpeed(c.Mu, rax, a);
            double vc = OrbitalMechanics.CircularVelocity(c.Mu, rax);
            double answer = OrbitalMechanics.CircularizationDeltaV(c.Mu, rax, rpx);
            t.Exercise = Exercise("ex-insertion",
                Loc.T("Аппарат над «") + c.PlanetName + Loc.T("» летит по траектории с апоцентром ") + N(hax) + Loc.T(" км и перицентром ") + N(hpx) + Loc.T(" км (высоты над R = ") + N(c.Radius / Km) + Loc.T(" км, μ = ") + N(c.Mu) + "). " +
                Loc.T("Какой разгон нужен в апоцентре, чтобы орбита стала круговой? Ответ в м/с."),
                answer, Loc.T("м/с"),
                new[] { Loc.T("Переведите высоты в радиусы от центра и найдите полуось a = (r_a + r_p)/2."), Loc.T("Скорость в апоцентре — по формуле vis-viva."), Loc.T("Нужный разгон — разница круговой скорости на r_a и скорости в апоцентре.") },
                new[] { "r_a = " + N(rax) + Loc.T(" м, r_p = ") + N(rpx) + Loc.T(" м, a = ") + N(a) + Loc.T(" м"), "v_a = √(μ(2/r_a − 1/a)) = " + N(va) + Loc.T(" м/с"), Loc.T("v_круг = √(μ/r_a) = ") + N(vc) + Loc.T(" м/с"), "Δv = " + N(answer) + Loc.T(" м/с") });
            return t;
        }

        // ---- 11 --------------------------------------------------------------------------------------------------

        static AcademyTopic HohmannTopic(AcademyContext c)
        {
            double low = c.LowOrbitAltitude;
            HohmannTransfer local = TransferCalculator.Hohmann(c.Mu, c.Radius + low, c.Radius + 2.0 * low);
            string helio = "";
            AcademyBody origin = c.Find(c.PlanetId);
            if (origin != null && origin.OrbitsStar && c.StarMu > 0.0)
                foreach (var target in c.Planets())
                {
                    if (target.Id == origin.Id) continue;
                    HohmannTransfer h = TransferCalculator.Hohmann(c.StarMu, origin.OrbitRadius, target.OrbitRadius);
                    helio = Loc.T("Вокруг звезды: ") + origin.Name + " → " + target.Name + ": Δv₁ = " + N(h.DeltaV1) + Loc.T(" м/с, Δv₂ = ") + N(h.DeltaV2) + Loc.T(" м/с, перелёт ") + N(h.TransferTime / Day) + Loc.T(" сут.");
                    break;
                }
            var t = new AcademyTopic
            {
                Id = Loc.T(Hohmann),
                Title = Loc.T("Гомановский переход"),
                Explanation =
                    Loc.T("Самый экономный способ перейти между двумя круговыми орбитами в одной плоскости — по половине эллипса, касающегося обеих: ") +
                    Loc.T("первый импульс на r₁ вытягивает орбиту до r₂, второй на r₂ скругляет её.\n\n") +
                    Loc.T("Переход медленный: ровно полпериода переходного эллипса. Тот же принцип работает и вокруг планеты, и вокруг звезды — меняется только μ."),
                Example =
                    Loc.T("Над «") + c.PlanetName + "»: " + N(low / Km) + " → " + N(2.0 * low / Km) + Loc.T(" км: Δv₁ = ") + N(local.DeltaV1) + ", Δv₂ = " + N(local.DeltaV2) + Loc.T(", всего ") + N(local.TotalDeltaV) +
                    Loc.T(" м/с за ") + N(local.TransferTime / Minute) + Loc.T(" мин.\n") + helio,
                Application = Loc.T("Подъём и смена орбиты; основа межпланетных перелётов (навигация, M5) — она будет использовать эти же функции."),
                Limitations =
                    Loc.T("Круговые компланарные орбиты и мгновенные импульсы. При r₂/r₁ > 11.94 выгоднее трёхимпульсный (биэллиптический) переход.")
            };
            t.Formulas.Add(new AcademyFormula("a = (r₁ + r₂) / 2", V("r₁, r₂", Loc.T("радиусы начальной и конечной орбит от центра"), Loc.T("м"))));
            t.Formulas.Add(new AcademyFormula("Δv₁ = √(μ(2/r₁ − 1/a)) − √(μ/r₁)", V("Δv₁", Loc.T("первый импульс (на r₁)"), Loc.T("м/с"))));
            t.Formulas.Add(new AcademyFormula("Δv₂ = √(μ/r₂) − √(μ(2/r₂ − 1/a))", V("Δv₂", Loc.T("второй импульс (на r₂)"), Loc.T("м/с"))));
            t.Formulas.Add(new AcademyFormula("t = π·√(a³ / μ)", V("t", Loc.T("время перехода"), Loc.T("с"))));
            t.CalculatorIds.Add(AcademyCalculators.Hohmann);
            t.CalculatorIds.Add(AcademyCalculators.Transfer);

            double r1 = c.Radius + 200000.0, r2 = c.Radius + 2000000.0;
            HohmannTransfer ex = TransferCalculator.Hohmann(c.Mu, r1, r2);
            t.Exercise = Exercise("ex-hohmann",
                Loc.T("Перелёт с круговой орбиты 200 км на круговую 2000 км над «") + c.PlanetName + "» (R = " + N(c.Radius / Km) + Loc.T(" км, μ = ") + N(c.Mu) + Loc.T("). Каков суммарный Δv двух импульсов? Ответ в м/с."),
                ex.TotalDeltaV, Loc.T("м/с"),
                new[] { Loc.T("Начните с полуоси переходного эллипса — радиусы считаются от центра."), Loc.T("Первый импульс: скорость на эллипсе в перицентре минус круговая на r₁."), Loc.T("Второй: круговая на r₂ минус скорость эллипса в апоцентре. Сложите.") },
                new[] { "r₁ = " + N(r1) + Loc.T(" м, r₂ = ") + N(r2) + Loc.T(" м, a = ") + N(ex.SemiMajorAxis) + Loc.T(" м"), "Δv₁ = " + N(ex.DepartureSpeed) + " − " + N(OrbitalMechanics.CircularVelocity(c.Mu, r1)) + " = " + N(ex.DeltaV1) + Loc.T(" м/с"),
                        "Δv₂ = " + N(OrbitalMechanics.CircularVelocity(c.Mu, r2)) + " − " + N(ex.ArrivalSpeed) + " = " + N(ex.DeltaV2) + Loc.T(" м/с"), "Δv = " + N(ex.TotalDeltaV) + Loc.T(" м/с") });
            return t;
        }

        // ---- 12 --------------------------------------------------------------------------------------------------

        static AcademyTopic LaunchWindowsTopic(AcademyContext c)
        {
            AcademyBody origin = c.Find(c.PlanetId);
            if (origin == null || !origin.OrbitsStar) origin = c.Find(SpaceSim.Planets.HomeSystemCatalog.HomeWorldId);
            string table = "";
            AcademyBody exTarget = null;
            if (origin != null && c.StarMu > 0.0)
                foreach (var target in c.Planets())
                {
                    if (target.Id == origin.Id) continue;
                    if (exTarget == null) exTarget = target;
                    double phase = TransferCalculator.HohmannPhaseAngle(c.StarMu, origin.OrbitRadius, target.OrbitRadius);
                    double synodic = TransferCalculator.SynodicPeriod(Period(c, origin), Period(c, target));
                    double time = TransferCalculator.Hohmann(c.StarMu, origin.OrbitRadius, target.OrbitRadius).TransferTime;
                    table += "• → " + target.Name + ": θ = " + N(phase * 180.0 / Math.PI) + Loc.T("°, перелёт ") + N(time / Day) + Loc.T(" сут, окно каждые ") + N(synodic / Day) + Loc.T(" сут\n");
                }
            var t = new AcademyTopic
            {
                Id = Loc.T(LaunchWindows),
                Title = Loc.T("Окна запуска"),
                Explanation =
                    Loc.T("Цель не стоит на месте: пока аппарат летит по переходному эллипсу, планета-цель проходит часть своей орбиты. ") +
                    Loc.T("Улетать нужно, когда цель опережает (или отстаёт) на фазовый угол θ — тогда к концу перелёта вы окажетесь в одной точке.\n\n") +
                    Loc.T("Такая расстановка планет повторяется раз в синодический период — это и есть окна запуска. Пропустили окно — ждите следующего ") +
                    Loc.T("или платите за перелёт гораздо большим Δv."),
                Example = origin != null ? Loc.T("Перелёты с «") + origin.Name + Loc.T("» по Гоману:\n") + table : Loc.T("Нет данных о планетах системы."),
                Application = Loc.T("Навигация (M5) будет проверять окно запуска по положениям планет. Уже сейчас оцените окна калькулятором «Оценка межпланетного перелёта»."),
                Limitations =
                    Loc.T("Круговые компланарные орбиты: реальные эллиптичность и наклоны орбит сдвигают окна и меняют Δv. Отлёт и захват — по сопряжённым коническим сечениям.")
            };
            t.Formulas.Add(new AcademyFormula(Loc.T("θ = π − n₂·t_перелёта,  n₂ = √(μ★ / r₂³)"), V("θ", Loc.T("фазовый угол цели при отлёте"), Loc.T("рад")), V("n₂", Loc.T("угловая скорость цели"), Loc.T("рад/с")), V("μ★", Loc.T("гравитационный параметр звезды"), Loc.T("м³/с²"))));
            t.Formulas.Add(new AcademyFormula(Loc.T("T_син = 1 / |1/T₁ − 1/T₂|"), V(Loc.T("T_син"), Loc.T("синодический период — интервал между окнами"), Loc.T("с")), V("T₁, T₂", Loc.T("периоды обращения планет вокруг звезды"), Loc.T("с"))));
            t.Formulas.Add(new AcademyFormula(Loc.T("Δv_отл = √(v∞² + 2μ/r) − √(μ/r)"), V("v∞", Loc.T("нужный избыток скорости относительно планеты"), Loc.T("м/с")), V("r", Loc.T("радиус орбиты ожидания"), Loc.T("м"))));
            t.CalculatorIds.Add(AcademyCalculators.Transfer);

            if (origin != null && exTarget != null)
            {
                double t1 = S3(Period(c, origin) / Day), t2 = S3(Period(c, exTarget) / Day);
                double syn = TransferCalculator.SynodicPeriod(t1, t2);
                t.Exercise = Exercise("ex-window",
                    Loc.T("Период обращения «") + origin.Name + Loc.T("» вокруг звезды — ") + N(t1) + Loc.T(" сут, «") + exTarget.Name + "» — " + N(t2) + Loc.T(" сут. Как часто повторяются окна запуска между ними? Ответ в сутках."),
                    syn, Loc.T("сут"),
                    new[] { Loc.T("Важна разность угловых скоростей: за сутки одна планета «обгоняет» другую на (1/T₁ − 1/T₂) оборота."), Loc.T("Окно повторяется, когда разница набегает на целый оборот.") },
                    new[] { "1/T₁ − 1/T₂ = " + N(1.0 / t1) + " − " + N(1.0 / t2) + " = " + N(1.0 / t1 - 1.0 / t2) + Loc.T(" об/сут"), Loc.T("T_син = 1/|…| = ") + N(syn) + Loc.T(" сут") });
            }
            return t;
        }

        static double Period(AcademyContext c, AcademyBody b) => b.OrbitPeriod > 0.0 ? b.OrbitPeriod : OrbitalMechanics.CircularPeriod(c.StarMu, b.OrbitRadius);

        // ---- 13 --------------------------------------------------------------------------------------------------

        static AcademyTopic RendezvousTopic(AcademyContext c)
        {
            double r1 = c.Radius + 200000.0, r2 = c.Radius + 400000.0;
            HohmannTransfer h = TransferCalculator.Hohmann(c.Mu, r1, r2);
            double phase = TransferCalculator.HohmannPhaseAngle(c.Mu, r1, r2);
            double synodic = TransferCalculator.SynodicPeriod(OrbitalMechanics.CircularPeriod(c.Mu, r1), OrbitalMechanics.CircularPeriod(c.Mu, r2));
            var t = new AcademyTopic
            {
                Id = Loc.T(Rendezvous),
                Title = Loc.T("Орбитальная встреча"),
                Explanation =
                    Loc.T("Встреча (стыковка) — та же задача окна запуска, только вокруг планеты: нужно прибыть на орбиту цели туда, где в этот момент окажется цель.\n\n") +
                    Loc.T("Парадокс орбитальной механики: чтобы догнать цель впереди на той же орбите, нужно ПРИТОРМОЗИТЬ — более низкая орбита короче и быстрее по угловой скорости. ") +
                    Loc.T("Чтобы пропустить цель вперёд — разогнаться и подняться."),
                Example =
                    Loc.T("Вы на 200 км, станция на 400 км над «") + c.PlanetName + Loc.T("»: переход ") + N(h.TransferTime / Minute) + Loc.T(" мин, Δv = ") + N(h.TotalDeltaV) + Loc.T(" м/с. ") +
                    Loc.T("Станция должна опережать вас на ") + N(phase * 180.0 / Math.PI) + Loc.T("°; такая расстановка повторяется каждые ") + N(synodic / 3600.0) + Loc.T(" ч."),
                Application = Loc.T("Пригодится для встреч на орбите в следующих этапах. Сейчас — тренируйте расчёт фазового угла и ожидания калькулятором «Гомановский переход»."),
                Limitations = Loc.T("Круговые компланарные орбиты. Сближение на последних сотнях метров описывается уравнениями относительного движения — их здесь нет.")
            };
            t.Formulas.Add(new AcademyFormula(Loc.T("θ = π − n_ц·t_перехода,  n_ц = √(μ / r_ц³)"), V("θ", Loc.T("на сколько цель должна опережать вас"), Loc.T("рад")), V(Loc.T("n_ц"), Loc.T("угловая скорость цели"), Loc.T("рад/с")), V(Loc.T("r_ц"), Loc.T("радиус орбиты цели"), Loc.T("м"))));
            t.Formulas.Add(new AcademyFormula(Loc.T("t_ожид = Δθ / |n₁ − n₂|"), V(Loc.T("t_ожид"), Loc.T("сколько ждать нужного угла"), Loc.T("с")), V("Δθ", Loc.T("сколько угла осталось набрать"), Loc.T("рад")), V("n₁, n₂", Loc.T("угловые скорости орбит"), Loc.T("рад/с"))));
            t.CalculatorIds.Add(AcademyCalculators.Hohmann);

            t.Exercise = Exercise("ex-rendezvous",
                Loc.T("Вы на круговой орбите 200 км над «") + c.PlanetName + Loc.T("», станция — на 400 км (R = ") + N(c.Radius / Km) + Loc.T(" км, μ = ") + N(c.Mu) + Loc.T("). На какой угол станция должна опережать вас в момент первого импульса? Ответ в градусах."),
                phase * 180.0 / Math.PI, "°",
                new[] { Loc.T("Найдите время гомановского перехода t = π√(a³/μ)."), Loc.T("За это время станция пройдёт угол n₂·t, где n₂ = √(μ/r₂³) — в радианах."), Loc.T("Вы за переход проходите ровно 180°: θ = 180° − n₂·t.") },
                new[] { "a = " + N(h.SemiMajorAxis) + Loc.T(" м, t = ") + N(h.TransferTime) + Loc.T(" с"), "n₂ = " + UnitFormatter.Scientific(Math.Sqrt(c.Mu / (r2 * r2 * r2)), 4) + Loc.T(" рад/с, n₂·t = ") + N(Math.Sqrt(c.Mu / (r2 * r2 * r2)) * h.TransferTime * 180.0 / Math.PI) + "°",
                        "θ = 180° − " + N(Math.Sqrt(c.Mu / (r2 * r2 * r2)) * h.TransferTime * 180.0 / Math.PI) + "° = " + N(phase * 180.0 / Math.PI) + "°" });
            return t;
        }
    }
}
