using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Units;

namespace SpaceSim.Academy
{
    /// <summary>
    /// The Flight Academy calculators (Level A). Each one only arranges inputs and calls the central physics functions the
    /// game itself uses: <see cref="RocketEquations"/>, <see cref="PlanetaryGravity"/>, <see cref="OrbitalMechanics"/>,
    /// <see cref="AtmosphericDrag"/>, <see cref="ExponentialAtmosphere"/>, <see cref="TransferCalculator"/>,
    /// <see cref="MissionRequirements"/>. Defaults and presets come from the player's planet and rocket.
    /// </summary>
    public static class AcademyCalculators
    {
        public const string Thrust = "thrust";
        public const string Gravity = "gravity";
        public const string Acceleration = "accel";
        public const string ThrustToWeight = "twr";
        public const string Burn = "burn";
        public const string DeltaV = "dv";
        public const string Fuel = "fuel";
        public const string Orbit = "orbit";
        public const string Escape = "escape";
        public const string Drag = "drag";
        public const string Insertion = "insertion";
        public const string Hohmann = "hohmann";
        public const string Transfer = "transfer";

        const double Tonne = 1000.0, KiloNewton = 1000.0, Km = 1000.0, MillionKm = 1.0e9;
        const double Minute = 60.0, Hour = 3600.0, Day = 86400.0, Percent = 0.01, KiloPascal = 1000.0;
        const double Degree = Math.PI / 180.0;
        const double G0 = RocketEquations.G0;
        const double G = PhysicalConstants.GravitationalConstant;

        static string N(double v) => AcademyNumber.Format(v);
        static double R4(double v) => AcademyNumber.RoundSignificant(v, 4);

        public static AcademyCalculator Find(IEnumerable<AcademyCalculator> calculators, string id)
        {
            foreach (var c in calculators)
                if (c.Id == id) return c;
            return null;
        }

        public static List<AcademyCalculator> Build(AcademyContext c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            return new List<AcademyCalculator>
            {
                BuildThrust(c), BuildGravity(c), BuildAcceleration(c), BuildThrustToWeight(c), BuildBurn(c), BuildDeltaV(c),
                BuildFuel(c), BuildOrbit(c), BuildEscape(c), BuildDrag(c), BuildInsertion(c), BuildHohmann(c), BuildTransfer(c)
            };
        }

        // ---- The player's rocket (or a training rocket when there is none) ---------------------------------------

        internal static StageReport Stage(AcademyContext c, int index)
        {
            if (c.Design != null && c.Design.Stages.Count > 0) return c.Design.Stages[Math.Min(index, c.Design.Stages.Count - 1)];
            return index == 0
                ? TrainingStage(Loc.T("Учебная ступень 1"), 15000.0, 135000.0, 42000.0, 3.0e6, 300.0, 270.0)
                : TrainingStage(Loc.T("Учебная ступень 2"), 4000.0, 36000.0, 2000.0, 5.0e5, 340.0, 300.0);
        }

        internal static int LastStage(AcademyContext c) => c.Design != null && c.Design.Stages.Count > 0 ? c.Design.Stages.Count - 1 : 1;

        internal static bool HasRocket(AcademyContext c) => c.Design != null && c.Design.Stages.Count > 0;

        static string RocketName(AcademyContext c, StageReport s) => (HasRocket(c) ? Loc.T("Моя ракета: ") : "") + s.Name;

        static StageReport TrainingStage(string name, double dry, double propellant, double above, double vacuumThrust, double vacuumIsp, double surfaceIsp)
        {
            var s = new StageReport
            {
                Name = name,
                DryMass = dry,
                PropellantMass = propellant,
                MassAtIgnition = dry + propellant + above,
                MassAtBurnout = dry + above,
                VacuumThrust = vacuumThrust,
                VacuumIsp = vacuumIsp,
                SurfaceIsp = surfaceIsp
            };
            s.MassFlow = RocketEquations.MassFlow(vacuumThrust, vacuumIsp);
            s.SurfaceThrust = RocketEquations.ThrustFromMassFlow(s.MassFlow, surfaceIsp);
            s.BurnTime = RocketEquations.BurnTime(propellant, s.MassFlow);
            s.DeltaVVacuum = RocketEquations.DeltaV(vacuumIsp, s.MassAtIgnition, s.MassAtBurnout);
            return s;
        }

        // ---- 1. Loc.T(Thrust) -------------------------------------------------------------------------------------------

        static AcademyCalculator BuildThrust(AcademyContext c)
        {
            StageReport s1 = Stage(c, 0);
            var calc = new AcademyCalculator(Thrust, Loc.T("Тяга двигателя"), AcademyTopics.MassThrust,
                Loc.T("F = ṁ·Isp·g₀: тяга — это расход топлива, умноженный на скорость истечения."),
                new[]
                {
                    new CalculatorInput("mdot", Loc.T("Расход топлива"), "ṁ", Loc.T("кг/с"), 1.0, R4(s1.MassFlow)),
                    new CalculatorInput("isp", Loc.T("Удельный импульс"), "Isp", Loc.T("с"), 1.0, R4(s1.VacuumIsp))
                },
                v =>
                {
                    double mdot = v[0], isp = v[1];
                    double ve = RocketEquations.ExhaustVelocity(isp);
                    double f = RocketEquations.ThrustFromMassFlow(mdot, isp);
                    return new CalculatorResult()
                        .Out(Loc.T("Скорость истечения"), "vₑ", ve, Loc.T("м/с"))
                        .Out(Loc.T("Тяга"), "F", f, Loc.T("кН"), KiloNewton)
                        .Step("vₑ = Isp·g₀ = " + N(isp) + Loc.T(" с · ") + N(G0) + Loc.T(" м/с² = ") + N(ve) + Loc.T(" м/с"))
                        .Step("F = ṁ·vₑ = " + N(mdot) + Loc.T(" кг/с · ") + N(ve) + Loc.T(" м/с = ") + N(f) + Loc.T(" Н = ") + N(f / KiloNewton) + Loc.T(" кН"))
                        .Note(Loc.T("g₀ = 9.80665 м/с² — постоянная пересчёта Isp в скорость, а не местная гравитация."))
                        .Note(Loc.T("У поверхности Isp ниже (давление мешает истечению), поэтому при том же расходе тяга меньше. В игре это учитывается."));
                });
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + Loc.T(" — вакуум"), R4(s1.MassFlow), R4(s1.VacuumIsp)));
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + Loc.T(" — у поверхности"), R4(s1.MassFlow), R4(s1.SurfaceIsp)));
            return calc;
        }

        // ---- 2. Loc.T(Gravity) ------------------------------------------------------------------------------------------

        static AcademyCalculator BuildGravity(AcademyContext c)
        {
            var calc = new AcademyCalculator(Gravity, Loc.T("Сила тяжести"), AcademyTopics.Gravity,
                Loc.T("F = G·M·m/r²: притяжение планеты на расстоянии r от её центра."),
                new[]
                {
                    new CalculatorInput("M", Loc.T("Масса планеты"), "M", Loc.T("кг"), 1.0, R4(c.Mu / G)),
                    new CalculatorInput("R", Loc.T("Радиус планеты"), "R", Loc.T("км"), Km, R4(c.Radius / Km)),
                    new CalculatorInput("h", Loc.T("Высота над R"), "h", Loc.T("км"), Km, 0.0, InputDomain.NonNegative),
                    new CalculatorInput("m", Loc.T("Масса аппарата"), "m", Loc.T("кг"), 1.0, 1000.0)
                },
                v =>
                {
                    double mass = v[0], radius = v[1], h = v[2], m = v[3];
                    double mu = PlanetaryGravity.GravitationalParameterFromMass(mass);
                    double r = radius + h;
                    double g = PlanetaryGravity.AccelerationMagnitude(mu, r);
                    double f = g * m;
                    return new CalculatorResult()
                        .Out(Loc.T("Гравитационный параметр"), "μ", mu, Loc.T("м³/с²"))
                        .Out(Loc.T("Расстояние от центра"), "r", r, Loc.T("км"), Km)
                        .Out(Loc.T("Ускорение свободного падения"), "g", g, Loc.T("м/с²"))
                        .Out(Loc.T("Сила тяжести"), "F", f, Loc.T("Н"))
                        .Step("μ = G·M = " + N(G) + " · " + N(mass) + " = " + N(mu) + Loc.T(" м³/с²"))
                        .Step("r = R + h = " + N(radius) + " + " + N(h) + " = " + N(r) + Loc.T(" м"))
                        .Step("g = μ/r² = " + N(mu) + " / " + N(r) + "² = " + N(g) + Loc.T(" м/с²"))
                        .Step("F = m·g = " + N(m) + " · " + N(g) + " = " + N(f) + Loc.T(" Н"))
                        .Note(Loc.T("Сферическая планета, без вращения: на экваторе вращающейся планеты вес чуть меньше."));
                });
            foreach (var b in c.Bodies)
                calc.Presets.Add(new CalculatorPreset(b.Name + Loc.T(" — поверхность"), R4(b.Mu / G), R4(b.Radius / Km), 0.0, 1000.0));
            calc.Presets.Add(new CalculatorPreset(c.PlanetName + Loc.T(" — высота 400 км"), R4(c.Mu / G), R4(c.Radius / Km), 400.0, 1000.0));
            return calc;
        }

        // ---- 3. Loc.T(Acceleration) -------------------------------------------------------------------------------------

        static AcademyCalculator BuildAcceleration(AcademyContext c)
        {
            StageReport s1 = Stage(c, 0);
            double g = c.SurfaceGravity;
            var calc = new AcademyCalculator(Acceleration, Loc.T("Ускорение ракеты"), AcademyTopics.Acceleration,
                Loc.T("a = F/m: ускорение от тяги, и сколько остаётся на подъём после вычета гравитации."),
                new[]
                {
                    new CalculatorInput("F", Loc.T("Тяга"), "F", Loc.T("кН"), KiloNewton, R4(s1.SurfaceThrust / KiloNewton)),
                    new CalculatorInput("m", Loc.T("Масса"), "m", Loc.T("т"), Tonne, R4(s1.MassAtIgnition / Tonne)),
                    new CalculatorInput("g", Loc.T("Местная гравитация"), "g", Loc.T("м/с²"), 1.0, R4(g), InputDomain.NonNegative)
                },
                v =>
                {
                    double f = v[0], m = v[1], gl = v[2];
                    double a = RocketEquations.Acceleration(f, m);
                    double net = RocketEquations.Acceleration(f - m * gl, m);
                    var r = new CalculatorResult()
                        .Out(Loc.T("Ускорение от тяги"), "a", a, Loc.T("м/с²"))
                        .Out(Loc.T("Перегрузка от тяги"), "a/g₀", a / G0, "g₀")
                        .Out(Loc.T("Ускорение вверх (вертикальный подъём)"), Loc.T("a_верт"), net, Loc.T("м/с²"))
                        .Step("a = F/m = " + N(f) + Loc.T(" Н / ") + N(m) + Loc.T(" кг = ") + N(a) + Loc.T(" м/с²"))
                        .Step(Loc.T("a_верт = (F − m·g)/m = (") + N(f) + " − " + N(m) + "·" + N(gl) + ") / " + N(m) + " = " + N(net) + Loc.T(" м/с²"))
                        .Note(Loc.T("Без сопротивления воздуха. Масса падает по мере расхода топлива, поэтому ускорение растёт к концу работы ступени."));
                    if (net <= 0.0) r.Warn(Loc.T("Тяга не больше веса: вертикально вверх ракета не ускоряется (тяга/вес ≤ 1)."));
                    return r;
                });
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + Loc.T(" — отрыв"), R4(s1.SurfaceThrust / KiloNewton), R4(s1.MassAtIgnition / Tonne), R4(g)));
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + Loc.T(" — конец работы (вакуум)"), R4(s1.VacuumThrust / KiloNewton), R4(s1.MassAtBurnout / Tonne), 0.0));
            return calc;
        }

        // ---- 4. Loc.T(Thrust)-to-weight ---------------------------------------------------------------------------------

        static AcademyCalculator BuildThrustToWeight(AcademyContext c)
        {
            StageReport s1 = Stage(c, 0);
            double launchMass = HasRocket(c) && c.Design.LaunchMass > 0.0 ? c.Design.LaunchMass : s1.MassAtIgnition;
            var calc = new AcademyCalculator(ThrustToWeight, Loc.T("Тяга/вес (TWR)"), AcademyTopics.MassThrust,
                Loc.T("TWR = F/(m·g): может ли ракета оторваться от поверхности."),
                new[]
                {
                    new CalculatorInput("F", Loc.T("Тяга"), "F", Loc.T("кН"), KiloNewton, R4(s1.SurfaceThrust / KiloNewton)),
                    new CalculatorInput("m", Loc.T("Масса"), "m", Loc.T("т"), Tonne, R4(launchMass / Tonne)),
                    new CalculatorInput("g", Loc.T("Местная гравитация"), "g", Loc.T("м/с²"), 1.0, R4(c.SurfaceGravity))
                },
                v =>
                {
                    double f = v[0], m = v[1], gl = v[2];
                    double twr = RocketEquations.ThrustToWeight(f, m, gl);
                    double weight = m * gl;
                    double maxMass = f / gl;
                    var r = new CalculatorResult()
                        .Out(Loc.T("Вес"), "W", weight, Loc.T("кН"), KiloNewton)
                        .Out(Loc.T("Тяга/вес"), "TWR", twr, "")
                        .Out(Loc.T("Наибольшая масса для отрыва"), "m_max", maxMass, Loc.T("т"), Tonne)
                        .Step("W = m·g = " + N(m) + Loc.T(" кг · ") + N(gl) + Loc.T(" м/с² = ") + N(weight) + Loc.T(" Н"))
                        .Step("TWR = F/W = " + N(f) + " / " + N(weight) + " = " + N(twr))
                        .Step("m_max = F/g = " + N(f) + " / " + N(gl) + " = " + N(maxMass) + Loc.T(" кг (при ней TWR = 1)"));
                    if (twr < 1.0) r.Warn(Loc.T("TWR < 1: двигатели не могут преодолеть местную гравитацию — ракета не оторвётся. Снизьте массу на ") +
                                          N((m - maxMass) / Tonne) + Loc.T(" т или поставьте двигатель мощнее."));
                    else if (twr < 1.2) r.Warn(Loc.T("TWR чуть больше 1: отрыв очень медленный, почти вся тяга уходит на удержание веса — огромные гравитационные потери."));
                    r.Note(Loc.T("Используйте тягу У ПОВЕРХНОСТИ (Isp там ниже) и полную стартовую массу со всеми ступенями."));
                    return r;
                });
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + Loc.T(" — старт с ") + c.PlanetName, R4(s1.SurfaceThrust / KiloNewton), R4(launchMass / Tonne), R4(c.SurfaceGravity)));
            foreach (var b in c.Bodies)
            {
                if (b.Id == c.PlanetId) continue;
                double gb = PlanetaryGravity.AccelerationMagnitude(b.Mu, b.Radius);
                calc.Presets.Add(new CalculatorPreset(Loc.T("Та же ракета на ") + b.Name, R4(s1.SurfaceThrust / KiloNewton), R4(launchMass / Tonne), R4(gb)));
            }
            return calc;
        }

        // ---- 5. Loc.T(Burn) duration / fuel consumption -----------------------------------------------------------------

        static AcademyCalculator BuildBurn(AcademyContext c)
        {
            StageReport s1 = Stage(c, 0), s2 = Stage(c, LastStage(c));
            var calc = new AcademyCalculator(Burn, Loc.T("Расход топлива и время работы"), AcademyTopics.FuelConsumption,
                Loc.T("ṁ = F/(Isp·g₀), t = m_топл/ṁ: сколько секунд проработает ступень и какой Δv она даст."),
                new[]
                {
                    new CalculatorInput("F", Loc.T("Тяга"), "F", Loc.T("кН"), KiloNewton, R4(s1.VacuumThrust / KiloNewton)),
                    new CalculatorInput("isp", Loc.T("Удельный импульс"), "Isp", Loc.T("с"), 1.0, R4(s1.VacuumIsp)),
                    new CalculatorInput("mp", Loc.T("Топливо"), Loc.T("m_топл"), Loc.T("т"), Tonne, R4(s1.PropellantMass / Tonne)),
                    new CalculatorInput("m0", Loc.T("Масса при зажигании"), "m₀", Loc.T("т"), Tonne, R4(s1.MassAtIgnition / Tonne))
                },
                v =>
                {
                    double f = v[0], isp = v[1], mp = v[2], m0 = v[3];
                    if (mp >= m0) return CalculatorResult.Fail(Loc.T("Топлива не может быть столько же или больше, чем вся масса при зажигании."));
                    double mdot = RocketEquations.MassFlow(f, isp);
                    double t = RocketEquations.BurnTime(mp, mdot);
                    double mf = m0 - mp;
                    double dv = RocketEquations.DeltaV(isp, m0, mf);
                    return new CalculatorResult()
                        .Out(Loc.T("Расход"), "ṁ", mdot, Loc.T("кг/с"))
                        .Out(Loc.T("Время работы"), "t", t, Loc.T("с"))
                        .Out(Loc.T("Время работы"), Loc.T("t_мин"), t, Loc.T("мин"), Minute)
                        .Out(Loc.T("Масса после выгорания"), "m_f", mf, Loc.T("т"), Tonne)
                        .Out(Loc.T("Идеальный Δv ступени"), "Δv", dv, Loc.T("м/с"))
                        .Step("ṁ = F/(Isp·g₀) = " + N(f) + " / (" + N(isp) + " · " + N(G0) + ") = " + N(mdot) + Loc.T(" кг/с"))
                        .Step(Loc.T("t = m_топл/ṁ = ") + N(mp) + " / " + N(mdot) + " = " + N(t) + Loc.T(" с"))
                        .Step(Loc.T("m_f = m₀ − m_топл = ") + N(m0) + " − " + N(mp) + " = " + N(mf) + Loc.T(" кг"))
                        .Step("Δv = Isp·g₀·ln(m₀/m_f) = " + N(isp * G0) + " · ln(" + N(m0 / mf) + ") = " + N(dv) + Loc.T(" м/с"))
                        .Note(Loc.T("Полная тяга всё время. На половинной тяге расход и время меняются вдвое, а Δv — нет: он зависит только от масс и Isp."));
                });
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1), R4(s1.VacuumThrust / KiloNewton), R4(s1.VacuumIsp), R4(s1.PropellantMass / Tonne), R4(s1.MassAtIgnition / Tonne)));
            if (LastStage(c) > 0)
                calc.Presets.Add(new CalculatorPreset(RocketName(c, s2), R4(s2.VacuumThrust / KiloNewton), R4(s2.VacuumIsp), R4(s2.PropellantMass / Tonne), R4(s2.MassAtIgnition / Tonne)));
            return calc;
        }

        // ---- 6. Tsiolkovsky --------------------------------------------------------------------------------------

        static AcademyCalculator BuildDeltaV(AcademyContext c)
        {
            StageReport s1 = Stage(c, 0), s2 = Stage(c, LastStage(c));
            var calc = new AcademyCalculator(DeltaV, Loc.T("Δv (формула Циолковского)"), AcademyTopics.Tsiolkovsky,
                Loc.T("Δv = Isp·g₀·ln(m₀/m_f): идеальный запас изменения скорости."),
                new[]
                {
                    new CalculatorInput("isp", Loc.T("Удельный импульс"), "Isp", Loc.T("с"), 1.0, R4(s1.VacuumIsp)),
                    new CalculatorInput("m0", Loc.T("Начальная масса"), "m₀", Loc.T("т"), Tonne, R4(s1.MassAtIgnition / Tonne)),
                    new CalculatorInput("mf", Loc.T("Конечная масса"), "m_f", Loc.T("т"), Tonne, R4(s1.MassAtBurnout / Tonne))
                },
                v =>
                {
                    double isp = v[0], m0 = v[1], mf = v[2];
                    if (mf > m0) return CalculatorResult.Fail(Loc.T("Конечная масса больше начальной: ракета не может потяжелеть, сжигая топливо."));
                    double ve = RocketEquations.ExhaustVelocity(isp);
                    double dv = RocketEquations.DeltaV(isp, m0, mf);
                    var r = new CalculatorResult()
                        .Out(Loc.T("Скорость истечения"), "vₑ", ve, Loc.T("м/с"))
                        .Out(Loc.T("Отношение масс"), "m₀/m_f", m0 / mf, "")
                        .Out(Loc.T("Доля топлива"), "(m₀−m_f)/m₀", (m0 - mf) / m0, "%", Percent)
                        .Out(Loc.T("Идеальный Δv"), "Δv", dv, Loc.T("м/с"))
                        .Step("vₑ = Isp·g₀ = " + N(isp) + " · " + N(G0) + " = " + N(ve) + Loc.T(" м/с"))
                        .Step("m₀/m_f = " + N(m0) + " / " + N(mf) + " = " + N(m0 / mf))
                        .Step("Δv = vₑ·ln(m₀/m_f) = " + N(ve) + " · " + N(Math.Log(m0 / mf)) + " = " + N(dv) + Loc.T(" м/с"))
                        .Note(Loc.T("Уровень A: без гравитационных и аэродинамических потерь. Реальный разгон при подъёме меньше — см. «Потери» в панели ракеты."));
                    if (HasRocket(c) && c.Design.Stages.Count > 1)
                        r.Note(Loc.T("Δv многоступенчатой ракеты — сумма Δv ступеней: у вашей ракеты ") + N(c.Design.TotalDeltaVVacuum) + Loc.T(" м/с (вакуум, ангар)."));
                    return r;
                });
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + Loc.T(" — вакуум"), R4(s1.VacuumIsp), R4(s1.MassAtIgnition / Tonne), R4(s1.MassAtBurnout / Tonne)));
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + Loc.T(" — Isp у поверхности"), R4(s1.SurfaceIsp), R4(s1.MassAtIgnition / Tonne), R4(s1.MassAtBurnout / Tonne)));
            if (LastStage(c) > 0)
                calc.Presets.Add(new CalculatorPreset(RocketName(c, s2), R4(s2.VacuumIsp), R4(s2.MassAtIgnition / Tonne), R4(s2.MassAtBurnout / Tonne)));
            return calc;
        }

        // ---- 7. Loc.T(Fuel) for a Δv ------------------------------------------------------------------------------------

        static AcademyCalculator BuildFuel(AcademyContext c)
        {
            StageReport s1 = Stage(c, 0), s2 = Stage(c, LastStage(c));
            MissionTarget[] targets = MissionRequirements.StandardTargets(c.Mu, c.Radius, c.SiteRotationSpeed, c.SpaceBoundary, c.HasAtmosphere);
            var calc = new AcademyCalculator(Fuel, Loc.T("Топливо для заданного Δv"), AcademyTopics.DeltaVBudget,
                Loc.T("m_топл = m_f·(e^(Δv/(Isp·g₀)) − 1): сколько топлива нужно, чтобы получить Δv."),
                new[]
                {
                    new CalculatorInput("dv", Loc.T("Нужный Δv"), "Δv", Loc.T("м/с"), 1.0, R4(targets.Length > 1 ? targets[1].IdealDeltaV : 3000.0)),
                    new CalculatorInput("isp", Loc.T("Удельный импульс"), "Isp", Loc.T("с"), 1.0, R4(s2.VacuumIsp)),
                    new CalculatorInput("mf", Loc.T("Масса без топлива (сухая + груз)"), "m_f", Loc.T("т"), Tonne, R4(s2.MassAtBurnout / Tonne))
                },
                v =>
                {
                    double dv = v[0], isp = v[1], mf = v[2];
                    double ratio = RocketEquations.MassRatio(dv, isp);
                    double mp = RocketEquations.PropellantForDeltaV(dv, isp, mf);
                    double m0 = mf + mp;
                    var r = new CalculatorResult()
                        .Out(Loc.T("Отношение масс"), "m₀/m_f", ratio, "")
                        .Out(Loc.T("Нужно топлива"), Loc.T("m_топл"), mp, Loc.T("т"), Tonne)
                        .Out(Loc.T("Стартовая масса"), "m₀", m0, Loc.T("т"), Tonne)
                        .Out(Loc.T("Доля топлива"), Loc.T("m_топл/m₀"), mp / m0, "%", Percent)
                        .Step("m₀/m_f = e^(Δv/(Isp·g₀)) = e^(" + N(dv) + " / " + N(isp * G0) + ") = " + N(ratio))
                        .Step(Loc.T("m_топл = m_f·(m₀/m_f − 1) = ") + N(mf) + " · " + N(ratio - 1.0) + " = " + N(mp) + Loc.T(" кг"))
                        .Step(Loc.T("m₀ = m_f + m_топл = ") + N(m0) + Loc.T(" кг"))
                        .Note(Loc.T("Уровень A: это минимум. Для подъёма через атмосферу добавьте к Δv потери (обычно 1–2 км/с) — или проверьте полётом."));
                    if (ratio > 10.0)
                        r.Warn(Loc.T("Отношение масс ") + N(ratio) + Loc.T(": топливо — больше 90 % ракеты. Баки и двигатели столько не выдержат — поэтому ракеты делают многоступенчатыми."));
                    return r;
                });
            foreach (var t in targets)
                calc.Presets.Add(new CalculatorPreset(t.Name + Loc.T(" (идеально, ") + N(t.IdealDeltaV) + Loc.T(" м/с)"), R4(t.IdealDeltaV), R4(s2.VacuumIsp), R4(s2.MassAtBurnout / Tonne)));
            calc.Presets.Add(new CalculatorPreset(RocketName(c, s1) + Loc.T(" — тот же Δv"), R4(targets.Length > 1 ? targets[1].IdealDeltaV : 3000.0), R4(s1.VacuumIsp), R4(s1.MassAtBurnout / Tonne)));
            return calc;
        }

        // ---- 8. Orbital velocity ---------------------------------------------------------------------------------

        static AcademyCalculator BuildOrbit(AcademyContext c)
        {
            var calc = new AcademyCalculator(Orbit, Loc.T("Круговая орбита"), AcademyTopics.OrbitalVelocity,
                Loc.T("v = √(μ/r), T = 2π√(r³/μ): скорость и период круговой орбиты."),
                new[]
                {
                    new CalculatorInput("mu", Loc.T("Гравитационный параметр"), "μ", Loc.T("м³/с²"), 1.0, R4(c.Mu)),
                    new CalculatorInput("R", Loc.T("Радиус планеты"), "R", Loc.T("км"), Km, R4(c.Radius / Km)),
                    new CalculatorInput("h", Loc.T("Высота орбиты над R"), "h", Loc.T("км"), Km, R4(c.LowOrbitAltitude / Km), InputDomain.NonNegative)
                },
                v =>
                {
                    double mu = v[0], radius = v[1], h = v[2];
                    double r = radius + h;
                    double vc = OrbitalMechanics.CircularVelocity(mu, r);
                    double period = OrbitalMechanics.CircularPeriod(mu, r);
                    double g = PlanetaryGravity.AccelerationMagnitude(mu, r);
                    return new CalculatorResult()
                        .Out(Loc.T("Радиус орбиты"), "r", r, Loc.T("км"), Km)
                        .Out(Loc.T("Круговая скорость"), Loc.T("v_круг"), vc, Loc.T("м/с"))
                        .Out(Loc.T("Период"), "T", period, Loc.T("мин"), Minute)
                        .Out(Loc.T("Гравитация на орбите"), "g(r)", g, Loc.T("м/с²"))
                        .Step("r = R + h = " + N(radius) + " + " + N(h) + " = " + N(r) + Loc.T(" м"))
                        .Step("v = √(μ/r) = √(" + N(mu) + " / " + N(r) + ") = " + N(vc) + Loc.T(" м/с"))
                        .Step("T = 2π·√(r³/μ) = " + N(period) + Loc.T(" с = ") + N(period / Minute) + Loc.T(" мин"))
                        .Note(Loc.T("Скорость — инерциальная. Стартуя на восток, вы уже имеете скорость вращения площадки: на ") + c.PlanetName + Loc.T(" это ") + N(c.SiteRotationSpeed) + Loc.T(" м/с."))
                        .Note(Loc.T("Нужна именно горизонтальная скорость: вертикальная скорость орбиты не даёт."));
                });
            calc.Presets.Add(new CalculatorPreset(c.PlanetName + " — " + N(c.LowOrbitAltitude / Km) + Loc.T(" км"), R4(c.Mu), R4(c.Radius / Km), R4(c.LowOrbitAltitude / Km)));
            foreach (var b in c.Bodies)
            {
                if (b.Id == c.PlanetId) continue;
                calc.Presets.Add(new CalculatorPreset(b.Name + Loc.T(" — 200 км"), R4(b.Mu), R4(b.Radius / Km), 200.0));
            }
            return calc;
        }

        // ---- 9. Escape velocity ----------------------------------------------------------------------------------

        static AcademyCalculator BuildEscape(AcademyContext c)
        {
            var calc = new AcademyCalculator(Escape, Loc.T("Вторая космическая"), AcademyTopics.EscapeVelocity,
                Loc.T("v = √(2μ/r): скорость, при которой аппарат уходит от планеты навсегда."),
                new[]
                {
                    new CalculatorInput("mu", Loc.T("Гравитационный параметр"), "μ", Loc.T("м³/с²"), 1.0, R4(c.Mu)),
                    new CalculatorInput("R", Loc.T("Радиус планеты"), "R", Loc.T("км"), Km, R4(c.Radius / Km)),
                    new CalculatorInput("h", Loc.T("Высота над R"), "h", Loc.T("км"), Km, 0.0, InputDomain.NonNegative),
                    new CalculatorInput("vrot", Loc.T("Скорость вращения площадки (старт на восток)"), Loc.T("v_вр"), Loc.T("м/с"), 1.0, R4(c.SiteRotationSpeed), InputDomain.NonNegative)
                },
                v =>
                {
                    double mu = v[0], radius = v[1], h = v[2], vrot = v[3];
                    double r = radius + h;
                    double ve = OrbitalMechanics.EscapeVelocity(mu, r);
                    double vc = OrbitalMechanics.CircularVelocity(mu, r);
                    double fromPad = MissionRequirements.Escape(mu, r, vrot);
                    return new CalculatorResult()
                        .Out(Loc.T("Вторая космическая"), Loc.T("v_уход"), ve, Loc.T("м/с"))
                        .Out(Loc.T("Круговая скорость там же"), Loc.T("v_круг"), vc, Loc.T("м/с"))
                        .Out(Loc.T("Отношение v_уход/v_круг"), "√2", ve / vc, "")
                        .Out(Loc.T("Нужно добавить при старте на восток"), Loc.T("v_уход − v_вр"), fromPad, Loc.T("м/с"))
                        .Step(Loc.T("v_уход = √(2μ/r) = √(2 · ") + N(mu) + " / " + N(r) + ") = " + N(ve) + Loc.T(" м/с"))
                        .Step(Loc.T("v_круг = √(μ/r) = ") + N(vc) + Loc.T(" м/с;  v_уход/v_круг = ") + N(ve / vc) + " = √2")
                        .Step(Loc.T("с учётом вращения: ") + N(ve) + " − " + N(vrot) + " = " + N(fromPad) + Loc.T(" м/с"))
                        .Note(Loc.T("Уровень A: удельная энергия ε = v²/2 − μ/r = 0. Без атмосферы и гравитационных потерь; притяжение звезды не учтено."));
                });
            foreach (var b in c.Bodies)
                calc.Presets.Add(new CalculatorPreset(b.Name, R4(b.Mu), R4(b.Radius / Km), 0.0, b.Id == c.PlanetId ? R4(c.SiteRotationSpeed) : 0.0));
            return calc;
        }

        // ---- 10. Loc.T(Drag) ----------------------------------------------------------------------------------------

        static AcademyCalculator BuildDrag(AcademyContext c)
        {
            bool exponential = c.HasAtmosphere && !double.IsNaN(c.ScaleHeight);
            double rho0 = exponential ? c.Atmosphere.ReferenceDensity : 1.225;
            double scaleHeight = exponential ? c.ScaleHeight : 8500.0;
            double cd = c.Rocket != null ? c.Rocket.dragCoefficient : 0.3;
            double area = c.Rocket != null ? c.Rocket.ReferenceArea : Math.PI * 1.85 * 1.85;
            StageReport s1 = Stage(c, 0);
            double mass = HasRocket(c) && c.Design.LaunchMass > 0.0 ? c.Design.LaunchMass : s1.MassAtIgnition;
            var calc = new AcademyCalculator(Drag, Loc.T("Сопротивление атмосферы"), AcademyTopics.Drag,
                Loc.T("ρ(h) = ρ₀·e^(−h/H), F = ½·ρ·v²·C_d·A: сила сопротивления на высоте h при скорости v."),
                new[]
                {
                    new CalculatorInput("rho0", Loc.T("Плотность у поверхности"), "ρ₀", Loc.T("кг/м³"), 1.0, R4(rho0), InputDomain.NonNegative),
                    new CalculatorInput("H", Loc.T("Шкала высот"), "H", Loc.T("км"), Km, R4(scaleHeight / Km)),
                    new CalculatorInput("h", Loc.T("Высота"), "h", Loc.T("км"), Km, 10.0, InputDomain.NonNegative),
                    new CalculatorInput("v", Loc.T("Скорость относительно воздуха"), "v", Loc.T("м/с"), 1.0, 500.0, InputDomain.NonNegative),
                    new CalculatorInput("cd", Loc.T("Коэффициент сопротивления"), "C_d", "", 1.0, R4(cd)),
                    new CalculatorInput("A", Loc.T("Площадь сечения"), "A", Loc.T("м²"), 1.0, R4(area)),
                    new CalculatorInput("m", Loc.T("Масса"), "m", Loc.T("т"), Tonne, R4(mass / Tonne)),
                    new CalculatorInput("g", Loc.T("Местная гравитация"), "g", Loc.T("м/с²"), 1.0, R4(c.SurfaceGravity), InputDomain.NonNegative)
                },
                v =>
                {
                    double r0 = v[0], hScale = v[1], h = v[2], speed = v[3], dragC = v[4], a = v[5], m = v[6], g = v[7];
                    var atmosphere = new ExponentialAtmosphere(r0, hScale, 0.0, 0.0);
                    double rho = atmosphere.Density(h);
                    double q = AtmosphericDrag.DynamicPressure(rho, speed);
                    double f = AtmosphericDrag.DragForceMagnitude(rho, speed, dragC, a);
                    double ad = RocketEquations.Acceleration(f, m);
                    var r = new CalculatorResult()
                        .Out(Loc.T("Плотность на высоте"), "ρ(h)", rho, Loc.T("кг/м³"))
                        .Out(Loc.T("Скоростной напор"), "q", q, Loc.T("кПа"), KiloPascal)
                        .Out(Loc.T("Сила сопротивления"), "F_d", f, Loc.T("кН"), KiloNewton)
                        .Out(Loc.T("Торможение"), "a_d", ad, Loc.T("м/с²"))
                        .Step("ρ = ρ₀·e^(−h/H) = " + N(r0) + " · e^(−" + N(h) + "/" + N(hScale) + ") = " + N(rho) + Loc.T(" кг/м³"))
                        .Step("q = ½·ρ·v² = 0.5 · " + N(rho) + " · " + N(speed) + "² = " + N(q) + Loc.T(" Па"))
                        .Step("F_d = q·C_d·A = " + N(q) + " · " + N(dragC) + " · " + N(a) + " = " + N(f) + Loc.T(" Н"))
                        .Step("a_d = F_d/m = " + N(f) + " / " + N(m) + " = " + N(ad) + Loc.T(" м/с²"));
                    if (rho > 0.0 && g > 0.0)
                    {
                        double vt = AtmosphericDrag.TerminalVelocity(m, g, rho, dragC, a);
                        r.Out(Loc.T("Предельная скорость падения"), Loc.T("v_пред"), vt, Loc.T("м/с"))
                            .Step(Loc.T("v_пред = √(2·m·g/(ρ·C_d·A)) = ") + N(vt) + Loc.T(" м/с (сопротивление = вес)"));
                    }
                    if (r0 <= 0.0) r.Note(Loc.T("Нет атмосферы — нет сопротивления."));
                    r.Note(Loc.T("Изотермическая атмосфера и постоянный C_d (в игре так же). На сверхзвуке реальный C_d выше."));
                    if (!exponential)
                        r.Note(c.HasAtmosphere ? Loc.T("У ") + c.PlanetName + Loc.T(" атмосфера задана таблицей — по умолчанию взяты земные ρ₀ и H.") : Loc.T("У ") + c.PlanetName + Loc.T(" нет атмосферы — по умолчанию взяты земные ρ₀ и H."));
                    return r;
                });
            calc.Presets.Add(new CalculatorPreset(Loc.T("Подъём: 10 км, 500 м/с"), R4(rho0), R4(scaleHeight / Km), 10.0, 500.0, R4(cd), R4(area), R4(mass / Tonne), R4(c.SurfaceGravity)));
            calc.Presets.Add(new CalculatorPreset(Loc.T("Плотные слои: 3 км, 300 м/с"), R4(rho0), R4(scaleHeight / Km), 3.0, 300.0, R4(cd), R4(area), R4(mass / Tonne), R4(c.SurfaceGravity)));
            calc.Presets.Add(new CalculatorPreset(Loc.T("Граница космоса: ") + N(c.SpaceBoundary / Km) + Loc.T(" км, 2 000 м/с"), R4(rho0), R4(scaleHeight / Km), R4(c.SpaceBoundary / Km), 2000.0, R4(cd), R4(area), R4(mass / Tonne), R4(c.SurfaceGravity)));
            return calc;
        }

        // ---- 11. Orbital insertion -------------------------------------------------------------------------------

        static AcademyCalculator BuildInsertion(AcademyContext c)
        {
            double apo = c.LowOrbitAltitude / Km;
            var calc = new AcademyCalculator(Insertion, Loc.T("Выход на орбиту (скругление)"), AcademyTopics.Insertion,
                Loc.T("Δv = √(μ/r_a) − √(μ·(2/r_a − 1/a)): разгон в апоцентре, превращающий траекторию в круговую орбиту."),
                new[]
                {
                    new CalculatorInput("mu", Loc.T("Гравитационный параметр"), "μ", Loc.T("м³/с²"), 1.0, R4(c.Mu)),
                    new CalculatorInput("R", Loc.T("Радиус планеты"), "R", Loc.T("км"), Km, R4(c.Radius / Km)),
                    new CalculatorInput("ha", Loc.T("Высота апоцентра"), "h_a", Loc.T("км"), Km, R4(apo), InputDomain.NonNegative),
                    new CalculatorInput("hp", Loc.T("Высота перицентра (может быть < 0)"), "h_p", Loc.T("км"), Km, 0.0, InputDomain.Any)
                },
                v =>
                {
                    double mu = v[0], radius = v[1], ha = v[2], hp = v[3];
                    double ra = radius + ha, rp = radius + hp;
                    if (!(rp > 0.0)) return CalculatorResult.Fail(Loc.T("r_p = R + h_p должно быть больше нуля: перицентр не может быть глубже центра планеты."));
                    if (rp > ra) return CalculatorResult.Fail(Loc.T("Перицентр выше апоцентра — поменяйте их местами."));
                    double a = 0.5 * (ra + rp);
                    double va = OrbitalMechanics.VisVivaSpeed(mu, ra, a);
                    double vc = OrbitalMechanics.CircularVelocity(mu, ra);
                    double dv = OrbitalMechanics.CircularizationDeltaV(mu, ra, rp);
                    var r = new CalculatorResult()
                        .Out(Loc.T("Большая полуось"), "a", a, Loc.T("км"), Km)
                        .Out(Loc.T("Скорость в апоцентре"), "v_a", va, Loc.T("м/с"))
                        .Out(Loc.T("Круговая скорость в апоцентре"), Loc.T("v_круг"), vc, Loc.T("м/с"))
                        .Out(Loc.T("Нужный разгон в апоцентре"), "Δv", dv, Loc.T("м/с"))
                        .Step("r_a = R + h_a = " + N(ra) + Loc.T(" м,  r_p = R + h_p = ") + N(rp) + Loc.T(" м"))
                        .Step("a = (r_a + r_p)/2 = " + N(a) + Loc.T(" м"))
                        .Step("v_a = √(μ·(2/r_a − 1/a)) = " + N(va) + Loc.T(" м/с  (vis-viva)"))
                        .Step(Loc.T("v_круг = √(μ/r_a) = ") + N(vc) + Loc.T(" м/с"))
                        .Step(Loc.T("Δv = v_круг − v_a = ") + N(dv) + Loc.T(" м/с — горизонтально, по направлению движения"))
                        .Note(Loc.T("Импульсный манёвр. Реальный двигатель работает секунды-минуты — начинайте чуть до апоцентра."));
                    if (hp < 0.0) r.Warn(Loc.T("Перицентр под поверхностью: без этого манёвра аппарат упадёт."));
                    else if (hp * Km < c.SpaceBoundary) r.Warn(Loc.T("Перицентр в атмосфере: на каждом проходе аппарат тормозится и в итоге упадёт."));
                    if (ha * Km < c.SpaceBoundary) r.Warn(Loc.T("Апоцентр ниже границы космоса — круговая орбита там быстро затормозится атмосферой."));
                    return r;
                });
            calc.Presets.Add(new CalculatorPreset(Loc.T("Апоцентр ") + N(apo) + Loc.T(" км, перицентр 0 км"), R4(c.Mu), R4(c.Radius / Km), R4(apo), 0.0));
            calc.Presets.Add(new CalculatorPreset(Loc.T("Почти вертикальный подъём: перицентр −") + N(c.Radius / Km * 0.5) + Loc.T(" км"), R4(c.Mu), R4(c.Radius / Km), R4(apo), -R4(c.Radius / Km * 0.5)));
            calc.Presets.Add(new CalculatorPreset(Loc.T("Апоцентр ") + N(apo) + Loc.T(" км, перицентр ") + N(c.SpaceBoundary / Km) + Loc.T(" км"), R4(c.Mu), R4(c.Radius / Km), R4(apo), R4(c.SpaceBoundary / Km)));
            return calc;
        }

        // ---- 12. Loc.T(Hohmann) transfer (also rendezvous) --------------------------------------------------------------

        static AcademyCalculator BuildHohmann(AcademyContext c)
        {
            double low = c.LowOrbitAltitude;
            var calc = new AcademyCalculator(Hohmann, Loc.T("Гомановский переход"), AcademyTopics.Hohmann,
                Loc.T("Два импульса между круговыми орбитами r₁ и r₂ по эллипсу a = (r₁ + r₂)/2; фазовый угол для встречи."),
                new[]
                {
                    new CalculatorInput("mu", Loc.T("Гравитационный параметр центра"), "μ", Loc.T("м³/с²"), 1.0, R4(c.Mu)),
                    new CalculatorInput("r1", Loc.T("Радиус начальной орбиты (от центра)"), "r₁", Loc.T("км"), Km, R4((c.Radius + low) / Km)),
                    new CalculatorInput("r2", Loc.T("Радиус конечной орбиты (от центра)"), "r₂", Loc.T("км"), Km, R4((c.Radius + 2.0 * low) / Km))
                },
                v =>
                {
                    double mu = v[0], r1 = v[1], r2 = v[2];
                    HohmannTransfer h = TransferCalculator.Hohmann(mu, r1, r2);
                    double phase = TransferCalculator.HohmannPhaseAngle(mu, r1, r2);
                    double synodic = TransferCalculator.SynodicPeriod(OrbitalMechanics.CircularPeriod(mu, r1), OrbitalMechanics.CircularPeriod(mu, r2));
                    var r = new CalculatorResult()
                        .Out(Loc.T("Большая полуось перехода"), "a", h.SemiMajorAxis, Loc.T("км"), Km)
                        .Out(Loc.T("Первый импульс"), "Δv₁", h.DeltaV1, Loc.T("м/с"))
                        .Out(Loc.T("Второй импульс"), "Δv₂", h.DeltaV2, Loc.T("м/с"))
                        .Out(Loc.T("Сумма"), "Δv", h.TotalDeltaV, Loc.T("м/с"))
                        .Out(Loc.T("Время перелёта"), "t", h.TransferTime, Loc.T("ч"), Hour)
                        .Out(Loc.T("Фазовый угол цели при старте"), "θ", phase, "°", Degree)
                        .Out(Loc.T("Синодический период (повтор окна)"), Loc.T("T_син"), synodic, Loc.T("ч"), Hour)
                        .Step("a = (r₁ + r₂)/2 = (" + N(r1) + " + " + N(r2) + ")/2 = " + N(h.SemiMajorAxis) + Loc.T(" м"))
                        .Step("Δv₁ = √(μ(2/r₁ − 1/a)) − √(μ/r₁) = " + N(h.DepartureSpeed) + " − " + N(OrbitalMechanics.CircularVelocity(mu, r1)) + " = " + N(h.DeltaV1) + Loc.T(" м/с"))
                        .Step("Δv₂ = √(μ/r₂) − √(μ(2/r₂ − 1/a)) = " + N(OrbitalMechanics.CircularVelocity(mu, r2)) + " − " + N(h.ArrivalSpeed) + " = " + N(h.DeltaV2) + Loc.T(" м/с"))
                        .Step("t = π·√(a³/μ) = " + N(h.TransferTime) + Loc.T(" с"))
                        .Step("θ = π − n₂·t,  n₂ = √(μ/r₂³)  →  θ = " + N(phase / Degree) + "°")
                        .Note(phase >= 0.0 ? Loc.T("θ > 0: в момент первого импульса цель должна быть ВПЕРЕДИ на θ.") : Loc.T("θ < 0: в момент первого импульса цель должна быть ПОЗАДИ на |θ|."))
                        .Note(Loc.T("Уровень A: круговые компланарные орбиты, мгновенные импульсы."));
                    if (r1 == r2) r.Note(Loc.T("r₁ = r₂: переход не нужен."));
                    return r;
                });
            calc.Presets.Add(new CalculatorPreset(Loc.T("Подъём орбиты ") + N(low / Km) + " → " + N(2.0 * low / Km) + Loc.T(" км"), R4(c.Mu), R4((c.Radius + low) / Km), R4((c.Radius + 2.0 * low) / Km)));
            calc.Presets.Add(new CalculatorPreset(Loc.T("Встреча: орбита 200 км → станция на 400 км"), R4(c.Mu), R4((c.Radius + 200000.0) / Km), R4((c.Radius + 400000.0) / Km)));
            AcademyBody origin = c.Find(c.PlanetId);
            if (origin != null && origin.OrbitsStar && c.StarMu > 0.0)
                foreach (var target in c.Planets())
                {
                    if (target.Id == origin.Id) continue;
                    calc.Presets.Add(new CalculatorPreset(origin.Name + " → " + target.Name + Loc.T(" (вокруг звезды)"), R4(c.StarMu), R4(origin.OrbitRadius / Km), R4(target.OrbitRadius / Km)));
                }
            return calc;
        }

        // ---- 13. Interplanetary transfer estimate ----------------------------------------------------------------

        static AcademyCalculator BuildTransfer(AcademyContext c)
        {
            AcademyBody origin = c.Find(c.PlanetId);
            if (origin == null || !origin.OrbitsStar) origin = c.Find(HomeSystemCatalog.HomeWorldId) ?? FirstPlanet(c, null);
            AcademyBody target = FirstPlanet(c, origin != null ? origin.Id : null);
            double[] defaults = TransferValues(c, origin, target);
            var calc = new AcademyCalculator(Transfer, Loc.T("Оценка межпланетного перелёта"), AcademyTopics.LaunchWindows,
                Loc.T("Гомановский переход вокруг звезды + отлёт с орбиты ожидания и захват у цели (метод сопряжённых конических сечений)."),
                new[]
                {
                    new CalculatorInput("muS", Loc.T("μ звезды"), "μ★", Loc.T("м³/с²"), 1.0, defaults[0]),
                    new CalculatorInput("r1", Loc.T("Орбита планеты отлёта"), "r₁", Loc.T("млн км"), MillionKm, defaults[1]),
                    new CalculatorInput("r2", Loc.T("Орбита планеты цели"), "r₂", Loc.T("млн км"), MillionKm, defaults[2]),
                    new CalculatorInput("mu1", Loc.T("μ планеты отлёта"), "μ₁", Loc.T("м³/с²"), 1.0, defaults[3]),
                    new CalculatorInput("R1", Loc.T("Радиус планеты отлёта"), "R₁", Loc.T("км"), Km, defaults[4]),
                    new CalculatorInput("h1", Loc.T("Высота орбиты ожидания"), "h₁", Loc.T("км"), Km, defaults[5], InputDomain.NonNegative),
                    new CalculatorInput("mu2", Loc.T("μ планеты цели"), "μ₂", Loc.T("м³/с²"), 1.0, defaults[6]),
                    new CalculatorInput("R2", Loc.T("Радиус планеты цели"), "R₂", Loc.T("км"), Km, defaults[7]),
                    new CalculatorInput("h2", Loc.T("Высота орбиты у цели"), "h₂", Loc.T("км"), Km, defaults[8], InputDomain.NonNegative),
                    new CalculatorInput("plan", Loc.T("Ваш импульс отлёта (0 — не проверять)"), Loc.T("Δv_план"), Loc.T("м/с"), 1.0, 0.0, InputDomain.NonNegative)
                },
                v =>
                {
                    double muS = v[0], r1 = v[1], r2 = v[2], mu1 = v[3], radius1 = v[4], h1 = v[5], mu2 = v[6], radius2 = v[7], h2 = v[8], plan = v[9];
                    HohmannTransfer h = TransferCalculator.Hohmann(muS, r1, r2);
                    TransferCalculator.HohmannExcessSpeeds(muS, r1, r2, out double vInfDep, out double vInfArr);
                    double departure = TransferCalculator.DepartureDeltaV(mu1, radius1 + h1, vInfDep);
                    double capture = TransferCalculator.CaptureDeltaV(mu2, radius2 + h2, vInfArr);
                    double phase = TransferCalculator.HohmannPhaseAngle(muS, r1, r2);
                    double synodic = TransferCalculator.SynodicPeriod(OrbitalMechanics.CircularPeriod(muS, r1), OrbitalMechanics.CircularPeriod(muS, r2));
                    var r = new CalculatorResult()
                        .Out(Loc.T("Большая полуось перехода"), "a", h.SemiMajorAxis, Loc.T("млн км"), MillionKm)
                        .Out(Loc.T("Избыток скорости при отлёте"), "v∞₁", vInfDep, Loc.T("м/с"))
                        .Out(Loc.T("Избыток скорости у цели"), "v∞₂", vInfArr, Loc.T("м/с"))
                        .Out(Loc.T("Импульс отлёта с орбиты ожидания"), Loc.T("Δv_отл"), departure, Loc.T("м/с"))
                        .Out(Loc.T("Импульс захвата у цели"), Loc.T("Δv_зах"), capture, Loc.T("м/с"))
                        .Out(Loc.T("Итого от орбиты до орбиты"), "Δv", departure + capture, Loc.T("м/с"))
                        .Out(Loc.T("Время перелёта"), "t", h.TransferTime, Loc.T("сут"), Day)
                        .Out(Loc.T("Фазовый угол цели при старте"), "θ", phase, "°", Degree)
                        .Out(Loc.T("Окна повторяются каждые"), Loc.T("T_син"), synodic, Loc.T("сут"), Day)
                        .Step("a = (r₁ + r₂)/2 = " + N(h.SemiMajorAxis / MillionKm) + Loc.T(" млн км"))
                        .Step("v∞₁ = |√(μ★(2/r₁ − 1/a)) − √(μ★/r₁)| = " + N(vInfDep) + Loc.T(" м/с"))
                        .Step("v∞₂ = |√(μ★/r₂) − √(μ★(2/r₂ − 1/a))| = " + N(vInfArr) + Loc.T(" м/с"))
                        .Step(Loc.T("Δv_отл = √(v∞₁² + 2μ₁/r_орб) − √(μ₁/r_орб) = ") + N(departure) + Loc.T(" м/с  (r_орб = R₁ + h₁)"))
                        .Step(Loc.T("Δv_зах = √(v∞₂² + 2μ₂/r_орб) − √(μ₂/r_орб) = ") + N(capture) + Loc.T(" м/с  (r_орб = R₂ + h₂)"))
                        .Step("t = π·√(a³/μ★) = " + N(h.TransferTime / Day) + Loc.T(" сут"))
                        .Step("θ = π − n₂·t = " + N(phase / Degree) + Loc.T("°;  T_син = 1/|1/T₁ − 1/T₂| = ") + N(synodic / Day) + Loc.T(" сут"))
                        .Note(Loc.T("Уровень A: круговые компланарные орбиты планет, мгновенные импульсы. Подъём с поверхности на орбиту ожидания сюда не входит."))
                        .Note(phase >= 0.0 ? Loc.T("θ > 0: при отлёте цель должна опережать планету отлёта на θ.") : Loc.T("θ < 0: при отлёте цель должна отставать на |θ|."));
                    if (plan > 0.0)
                    {
                        if (plan < departure)
                            r.Warn(Loc.T("Скорость отлёта недостаточна для выхода на переходную орбиту: не хватает ") + N(departure - plan) + Loc.T(" м/с."));
                        else
                            r.Note(Loc.T("Импульса отлёта хватает (запас ") + N(plan - departure) + Loc.T(" м/с). Лишняя скорость изменит время перелёта и точку встречи."));
                    }
                    return r;
                });
            if (origin != null)
                foreach (var t in c.Planets())
                {
                    if (t.Id == origin.Id) continue;
                    calc.Presets.Add(new CalculatorPreset(origin.Name + " → " + t.Name, TransferValues(c, origin, t)));
                }
            return calc;
        }

        static AcademyBody FirstPlanet(AcademyContext c, string exceptId)
        {
            foreach (var b in c.Planets())
                if (b.Id != exceptId) return b;
            return null;
        }

        static double[] TransferValues(AcademyContext c, AcademyBody origin, AcademyBody target)
        {
            const double parking = 200.0; // km
            if (origin == null || target == null || !(c.StarMu > 0.0))
                return new[] { 1.327e20, 149.6, 227.9, 3.986e14, 6371.0, parking, 4.283e13, 3390.0, parking, 0.0 };
            return new[]
            {
                R4(c.StarMu), R4(origin.OrbitRadius / MillionKm), R4(target.OrbitRadius / MillionKm),
                R4(origin.Mu), R4(origin.Radius / Km), parking, R4(target.Mu), R4(target.Radius / Km), parking, 0.0
            };
        }
    }
}
