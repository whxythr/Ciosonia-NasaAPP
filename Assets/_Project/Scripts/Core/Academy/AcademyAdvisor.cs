using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Academy
{
    public enum HintSeverity
    {
        /// <summary>Something went right — with a "check yourself" question.</summary>
        Info,
        /// <summary>The flight can be improved.</summary>
        Advice,
        /// <summary>The goal cannot be reached this way.</summary>
        Warning
    }

    /// <summary>
    /// Snapshot of the flight for the advisor. The runtime fills it from the authoritative simulation
    /// (FreeFlightSimulation, RocketState, FlightTelemetry, FlightRecorder, hangar analysis) — the advisor never simulates.
    /// SI units.
    /// </summary>
    public sealed class AcademySituation
    {
        public double Time;
        /// <summary>A rocket with at least one stage left is attached.</summary>
        public bool HasRocket;
        public bool Landed;
        public bool OnLaunchPad;
        public bool EngineBurning;
        /// <summary>The current stage has propellant and an engine.</summary>
        public bool CurrentStageHasFuel;
        /// <summary>Any remaining stage still has propellant.</summary>
        public bool AnyPropellantLeft;
        public int StageIndex, StageCount;
        /// <summary>Current total mass [kg].</summary>
        public double Mass;
        /// <summary>Full-throttle thrust of the current stage at the current ambient pressure [N].</summary>
        public double AvailableThrust;
        /// <summary>Vacuum Isp of the current (or last) stage [s].</summary>
        public double StageVacuumIsp;
        public double LocalGravity;
        public double DeltaVRemaining;
        public double PitchDegrees;

        public double AltitudeAboveReference;
        /// <summary>Height above the actual ground below [m].</summary>
        public double AltitudeAboveSurface;
        public double DistanceFromCenter;
        public double RadialVelocity;
        public double HorizontalSpeed;
        public double InertialSpeed;
        public double Density;
        public double AirSpeed;
        public double DynamicPressure;
        public bool Bound;
        public double PeriapsisRadius;
        /// <summary>+∞ for unbound trajectories.</summary>
        public double ApoapsisRadius;
        public FlightPhase Phase;
        public bool AboveSpaceBoundary;

        public double DeltaVExpended, GravityLoss, DragLoss;

        public double Mu, Radius, SpaceBoundary;
        /// <summary>Ideal Δv to the standard low orbit from the surface (MissionRequirements, Level A) [m/s].</summary>
        public double OrbitRequirement;
        /// <summary>Altitude of that standard orbit [m].</summary>
        public double LowOrbitAltitude;
        /// <summary>Ideal vacuum Δv of the whole design (hangar analysis) [m/s]; 0 = unknown.</summary>
        public double DesignDeltaV;

        /// <summary>
        /// Copies the authoritative state: the simulation, its telemetry (already computed by FlightTelemetryCalculator) and the
        /// classified phase. Only display-free bookkeeping happens here — thrust comes from the rocket's own mass flow and Isp.
        /// </summary>
        public void Capture(FreeFlightSimulation sim, FlightTelemetry t, FlightPhase phase, bool onLaunchPad, AcademyContext context)
        {
            if (sim == null || t == null) throw new ArgumentNullException(sim == null ? nameof(sim) : nameof(t));
            RocketState r = sim.Rocket;
            PlanetModel model = sim.Planet;
            FlightRecorder rec = sim.Recorder;

            Time = t.Time;
            Landed = sim.IsLanded;
            OnLaunchPad = onLaunchPad;
            HasRocket = r != null && r.HasStages;
            EngineBurning = r != null && r.IsBurning;
            CurrentStageHasFuel = r != null && r.CanBurn;
            AnyPropellantLeft = false;
            if (r != null)
                for (int i = r.StageIndex; i < r.StageCount; i++)
                    if (r.Propellant[i] > 0.0) AnyPropellantLeft = true;
            StageIndex = r != null ? r.StageIndex : 0;
            StageCount = r != null ? r.StageCount : 0;
            Mass = sim.CurrentMass;
            double p0 = model.Atmosphere.Pressure(0.0);
            double pressureRatio = p0 > 0.0 ? t.Pressure / p0 : 0.0;
            AvailableThrust = r != null && r.HasStages ? RocketEquations.ThrustFromMassFlow(r.FullThrottleMassFlow, r.Isp(pressureRatio)) : 0.0;
            StageVacuumIsp = r != null && r.StageCount > 0 ? r.Configuration.stages[Math.Min(r.StageIndex, r.StageCount - 1)].vacuumIsp : 0.0;
            LocalGravity = t.GravityMagnitude;
            DeltaVRemaining = r != null ? r.DeltaVRemaining() : 0.0;
            PitchDegrees = r != null ? r.PitchDegrees : 0.0;

            AltitudeAboveReference = t.AltitudeAboveReference;
            AltitudeAboveSurface = t.AltitudeAboveSurface;
            DistanceFromCenter = t.DistanceFromCenter;
            RadialVelocity = t.RadialVelocity;
            HorizontalSpeed = t.HorizontalInertialSpeed;
            InertialSpeed = t.InertialSpeed;
            Density = t.Density;
            AirSpeed = t.AirSpeed;
            DynamicPressure = t.DynamicPressure;
            Bound = t.Orbit.IsBound;
            PeriapsisRadius = t.Orbit.PeriapsisRadius;
            ApoapsisRadius = t.Orbit.IsBound ? t.Orbit.ApoapsisRadius : double.PositiveInfinity;
            Phase = phase;
            AboveSpaceBoundary = t.AboveSpaceBoundary;

            DeltaVExpended = rec.DeltaVExpended;
            GravityLoss = rec.GravityLoss;
            DragLoss = rec.DragLoss;

            Mu = model.GravitationalParameter;
            Radius = model.Radius;
            SpaceBoundary = model.SpaceBoundaryAltitude;
            if (context != null)
            {
                LowOrbitAltitude = context.LowOrbitAltitude;
                OrbitRequirement = MissionRequirements.LowOrbit(context.Mu, context.Radius, context.LowOrbitAltitude, context.SiteRotationSpeed);
                DesignDeltaV = context.Design != null ? context.Design.TotalDeltaVVacuum : 0.0;
            }
        }
    }

    /// <summary>
    /// A contextual lesson raised by what just happened in flight. It states the problem and links the topic; hints and the
    /// worked solution (with the flight's own numbers) are revealed one by one — first all hints, then the solution steps.
    /// </summary>
    public sealed class AcademyHint
    {
        public string Id = "";
        public HintSeverity Severity;
        public string Title = "";
        public string Message = "";
        public string TopicId;
        public string CalculatorId;
        public readonly List<string> Hints = new List<string>();
        public readonly List<string> SolutionSteps = new List<string>();
        /// <summary>Simulation time when it was raised [s].</summary>
        public double RaisedAt;

        public int RevealedHints { get; private set; }
        public int RevealedSteps { get; private set; }
        public bool CanRevealHint => RevealedHints < Hints.Count;
        /// <summary>The solution opens only after every hint has been seen (no answers up front).</summary>
        public bool CanRevealStep => !CanRevealHint && RevealedSteps < SolutionSteps.Count;

        public bool RevealHint()
        {
            if (!CanRevealHint) return false;
            RevealedHints++;
            return true;
        }

        public bool RevealStep()
        {
            if (!CanRevealStep) return false;
            RevealedSteps++;
            return true;
        }
    }

    /// <summary>
    /// Watches <see cref="AcademySituation"/> snapshots and raises <see cref="AcademyHint"/>s: once when a condition starts,
    /// again only after it has cleared and a cooldown passed; some only once per flight. Numbers in hints come from the
    /// central Level A functions.
    /// </summary>
    public sealed class AcademyAdvisor
    {
        public const string TwrBelowOne = "twr-below-one";
        public const string StageEmpty = "stage-empty";
        public const string OutOfDeltaV = "out-of-dv";
        public const string NeedsInsertion = "needs-insertion";
        public const string VerticalAscent = "vertical-ascent";
        public const string MaxQ = "max-q";
        public const string OrbitAchieved = "orbit-achieved";
        public const string EscapeReached = "escape";
        public const string DesignShort = "design-short";
        public const string LandingBurn = "landing-burn";

        /// <summary>Dynamic pressure that counts as "dense air at speed" [Pa].</summary>
        public const double HighDynamicPressure = 35000.0;
        public const int HistoryLimit = 30;

        sealed class Rule
        {
            public string Id;
            public bool OncePerFlight;
            public double Cooldown = 30.0;
            public Func<AcademySituation, bool> When;
            public Func<AcademySituation, AcademyHint> Make;
        }

        readonly List<Rule> rules = new List<Rule>();
        readonly HashSet<string> active = new HashSet<string>();
        readonly HashSet<string> raisedThisFlight = new HashSet<string>();
        readonly Dictionary<string, double> lastRaised = new Dictionary<string, double>();
        readonly List<AcademyHint> history = new List<AcademyHint>();

        /// <summary>Raised hints, newest first.</summary>
        public IReadOnlyList<AcademyHint> History => history;
        public AcademyHint Latest => history.Count > 0 ? history[0] : null;

        public AcademyAdvisor()
        {
            rules.Add(new Rule { Id = Loc.T(TwrBelowOne), When = IsTwrBelowOne, Make = MakeTwrBelowOne });
            rules.Add(new Rule { Id = Loc.T(DesignShort), OncePerFlight = true, When = IsDesignShort, Make = MakeDesignShort });
            rules.Add(new Rule { Id = Loc.T(StageEmpty), Cooldown = 10.0, When = IsStageEmpty, Make = MakeStageEmpty });
            rules.Add(new Rule { Id = Loc.T(VerticalAscent), OncePerFlight = true, When = IsVerticalAscent, Make = MakeVerticalAscent });
            rules.Add(new Rule { Id = Loc.T(MaxQ), Cooldown = 60.0, When = IsMaxQ, Make = MakeMaxQ });
            rules.Add(new Rule { Id = Loc.T(NeedsInsertion), OncePerFlight = true, When = IsNeedsInsertion, Make = MakeNeedsInsertion });
            rules.Add(new Rule { Id = Loc.T(OutOfDeltaV), OncePerFlight = true, When = IsOutOfDeltaV, Make = MakeOutOfDeltaV });
            rules.Add(new Rule { Id = Loc.T(LandingBurn), Cooldown = 20.0, When = IsLandingBurn, Make = MakeLandingBurn });
            rules.Add(new Rule { Id = Loc.T(OrbitAchieved), OncePerFlight = true, When = s => !s.Landed && s.Phase == FlightPhase.OrbitalFlight, Make = MakeOrbitAchieved });
            rules.Add(new Rule { Id = Loc.T(EscapeReached), OncePerFlight = true, When = s => !s.Landed && s.Phase == FlightPhase.EscapeTrajectory, Make = MakeEscape });
        }

        /// <summary>Whether the condition behind a hint still holds.</summary>
        public bool IsActive(string id) => active.Contains(id);

        /// <summary>A new rocket on the pad: once-per-flight lessons may appear again.</summary>
        public void ResetFlight()
        {
            raisedThisFlight.Clear();
            active.Clear();
        }

        /// <summary>Evaluates all rules; returns the hints raised by this snapshot (usually none).</summary>
        public List<AcademyHint> Evaluate(AcademySituation s)
        {
            var raised = new List<AcademyHint>();
            if (s == null) return raised;
            foreach (Rule rule in rules)
            {
                bool holds;
                try
                {
                    holds = rule.When(s);
                }
                catch (ArgumentException)
                {
                    holds = false;
                }
                if (!holds)
                {
                    active.Remove(rule.Id);
                    continue;
                }
                if (active.Contains(rule.Id)) continue;
                if (rule.OncePerFlight && raisedThisFlight.Contains(rule.Id)) continue;
                if (lastRaised.TryGetValue(rule.Id, out double last) && s.Time - last < rule.Cooldown && s.Time >= last) continue;
                AcademyHint hint = rule.Make(s);
                hint.Id = rule.Id;
                hint.RaisedAt = s.Time;
                active.Add(rule.Id);
                raisedThisFlight.Add(rule.Id);
                lastRaised[rule.Id] = s.Time;
                history.Insert(0, hint);
                if (history.Count > HistoryLimit) history.RemoveAt(history.Count - 1);
                raised.Add(hint);
            }
            return raised;
        }

        // ---- Rules ---------------------------------------------------------------------------------------------

        static string N(double v) => AcademyNumber.Format(v);
        static string Km(double m) => AcademyNumber.Format(m / 1000.0) + Loc.T(" км");

        static double Twr(AcademySituation s) => RocketEquations.ThrustToWeight(s.AvailableThrust, s.Mass, s.LocalGravity);

        static AcademyHint Hint(HintSeverity severity, string title, string message, string topic, string calculator, string[] hints, string[] steps)
        {
            var h = new AcademyHint { Severity = severity, Title = title, Message = message, TopicId = topic, CalculatorId = calculator };
            h.Hints.AddRange(hints);
            h.SolutionSteps.AddRange(steps);
            return h;
        }

        static bool IsTwrBelowOne(AcademySituation s) =>
            s.HasRocket && s.Landed && s.CurrentStageHasFuel && s.Mass > 0.0 && s.LocalGravity > 0.0 && Twr(s) < 1.0;

        static AcademyHint MakeTwrBelowOne(AcademySituation s)
        {
            double twr = Twr(s);
            double maxMass = s.AvailableThrust / s.LocalGravity;
            double comfortable = s.AvailableThrust / (1.3 * s.LocalGravity);
            string message = s.EngineBurning
                ? Loc.T("Тяга/вес вашей ракеты ниже 1 (TWR = ") + N(twr) + Loc.T("). Её двигатели не могут преодолеть местную гравитацию — ракета стоит и сжигает топливо.")
                : Loc.T("Тяга/вес вашей ракеты у старта ниже 1 (TWR = ") + N(twr) + Loc.T("): если включить двигатель, она не оторвётся.");
            return Hint(HintSeverity.Warning, Loc.T("Ракета не может оторваться"), message, AcademyTopics.MassThrust, AcademyCalculators.ThrustToWeight,
                new[]
                {
                    Loc.T("TWR = F/(m·g). Какую из трёх величин вы можете изменить?"),
                    Loc.T("Массу — в ангаре (меньше топлива, модулей, груза); тягу — другим двигателем. Гравитацию планеты не изменить."),
                    Loc.T("Найдите массу, при которой TWR = 1: m_max = F/g.")
                },
                new[]
                {
                    "F = " + N(s.AvailableThrust / 1000.0) + Loc.T(" кН, m = ") + N(s.Mass / 1000.0) + Loc.T(" т, g = ") + N(s.LocalGravity) + Loc.T(" м/с²"),
                    "TWR = F/(m·g) = " + N(s.AvailableThrust) + " / (" + N(s.Mass) + " · " + N(s.LocalGravity) + ") = " + N(twr),
                    "m_max = F/g = " + N(maxMass / 1000.0) + Loc.T(" т → снимите не меньше ") + N((s.Mass - maxMass) / 1000.0) + Loc.T(" т или добавьте тяги"),
                    Loc.T("Для уверенного старта нужен TWR ≈ 1.3: масса не больше ") + N(comfortable / 1000.0) + Loc.T(" т")
                });
        }

        static bool IsDesignShort(AcademySituation s) =>
            s.OnLaunchPad && !s.EngineBurning && s.DesignDeltaV > 0.0 && s.OrbitRequirement > 0.0 && s.DesignDeltaV < s.OrbitRequirement;

        static AcademyHint MakeDesignShort(AcademySituation s)
        {
            double deficit = s.OrbitRequirement - s.DesignDeltaV;
            double isp = s.StageVacuumIsp > 0.0 ? s.StageVacuumIsp : 300.0;
            return Hint(HintSeverity.Warning, Loc.T("Этой ракете не хватит Δv на орбиту"),
                Loc.T("Идеальный Δv ракеты (") + N(s.DesignDeltaV) + Loc.T(" м/с) меньше идеального требования для орбиты ") + Km(s.LowOrbitAltitude) + " (" + N(s.OrbitRequirement) +
                Loc.T(" м/с): даже без потерь орбиты не будет. На суборбитальный подъём может хватить."),
                AcademyTopics.DeltaVBudget, AcademyCalculators.Fuel,
                new[]
                {
                    Loc.T("Идеальное требование — это минимум; реальный подъём требует ещё больше (потери)."),
                    Loc.T("Δv растёт с Isp и с отношением масс m₀/m_f: больше топлива, легче конструкция или ступени."),
                    Loc.T("Сколько топлива добавить — посчитайте по формуле Циолковского «наоборот» (калькулятор «Топливо для заданного Δv»).")
                },
                new[]
                {
                    Loc.T("Не хватает идеального Δv: ") + N(s.OrbitRequirement) + " − " + N(s.DesignDeltaV) + " = " + N(deficit) + Loc.T(" м/с (плюс потери подъёма, обычно 1–2 км/с)"),
                    Loc.T("Например, ступени с Isp ") + N(isp) + Loc.T(" с для ") + N(deficit) + Loc.T(" м/с нужно отношение масс e^(Δv/(Isp·g₀)) = ") + N(RocketEquations.MassRatio(deficit, isp))
                });
        }

        static bool IsStageEmpty(AcademySituation s) =>
            s.HasRocket && !s.Landed && !s.CurrentStageHasFuel && s.StageIndex < s.StageCount - 1;

        static AcademyHint MakeStageEmpty(AcademySituation s) =>
            Hint(HintSeverity.Advice, Loc.T("Ступень пуста"),
                Loc.T("Топливо ступени ") + (s.StageIndex + 1) + Loc.T(" кончилось. Пустые баки и двигатель — мёртвый груз: отделите ступень (Пробел) и включите следующую (Enter)."),
                AcademyTopics.Tsiolkovsky, AcademyCalculators.DeltaV,
                new[] { Loc.T("Посмотрите на формулу Циолковского: что будет с массой m₀ следующей ступени, если пустую не сбросить?") },
                new[]
                {
                    Loc.T("Сухая масса пустой ступени входит и в m₀, и в m_f следующей"),
                    Loc.T("Сбросив её, вы уменьшаете обе массы на одну и ту же величину — отношение m₀/m_f растёт, а с ним Δv")
                });

        static bool IsVerticalAscent(AcademySituation s) =>
            s.HasRocket && s.EngineBurning && !s.Landed && s.PitchDegrees < 10.0 &&
            s.AltitudeAboveReference > Math.Max(10000.0, 0.25 * s.SpaceBoundary) && s.Phase != FlightPhase.OrbitalFlight;

        static AcademyHint MakeVerticalAscent(AcademySituation s)
        {
            double vc = OrbitalMechanics.CircularVelocity(s.Mu, s.Radius + s.LowOrbitAltitude);
            return Hint(HintSeverity.Advice, Loc.T("Подъём почти вертикальный"),
                Loc.T("Вы на высоте ") + Km(s.AltitudeAboveReference) + Loc.T(" и летите почти вертикально (тангаж ") + N(s.PitchDegrees) + Loc.T("°). Для орбиты нужна горизонтальная скорость, ") +
                Loc.T("а вертикальный подъём копит гравитационные потери (уже ") + N(s.GravityLoss) + Loc.T(" м/с)."),
                AcademyTopics.OrbitalVelocity, AcademyCalculators.Orbit,
                new[]
                {
                    Loc.T("Какая скорость нужна для орбиты — вертикальная или горизонтальная? (тема «Орбитальная скорость»)"),
                    Loc.T("Наклоняйте ракету на восток (W) постепенно: чем выше и реже воздух, тем ближе к горизонту."),
                    Loc.T("Ориентир: к границе космоса тангаж около 60–80°, горизонтальная скорость растёт к круговой.")
                },
                new[]
                {
                    Loc.T("Круговая скорость на ") + Km(s.LowOrbitAltitude) + ": √(μ/r) = " + N(vc) + Loc.T(" м/с — и она горизонтальная"),
                    Loc.T("Сейчас горизонтальная (инерциальная) скорость ") + N(s.HorizontalSpeed) + Loc.T(" м/с: не хватает ") + N(Math.Max(0.0, vc - s.HorizontalSpeed)) + Loc.T(" м/с"),
                    Loc.T("Каждая секунда вертикального полёта стоит около g = ") + N(s.LocalGravity) + Loc.T(" м/с Δv")
                });
        }

        static bool IsMaxQ(AcademySituation s) => s.EngineBurning && !s.Landed && s.DynamicPressure > HighDynamicPressure;

        static AcademyHint MakeMaxQ(AcademySituation s) =>
            Hint(HintSeverity.Advice, Loc.T("Плотный воздух на большой скорости"),
                Loc.T("Скоростной напор ") + N(s.DynamicPressure / 1000.0) + Loc.T(" кПа: вы разгоняетесь в плотной атмосфере, и сопротивление съедает Δv (уже ") + N(s.DragLoss) + Loc.T(" м/с)."),
                AcademyTopics.Drag, AcademyCalculators.Drag,
                new[]
                {
                    Loc.T("q = ½·ρ·v²: сопротивление растёт как квадрат скорости, а плотность падает с высотой экспоненциально."),
                    Loc.T("Можно уменьшить тягу (Ctrl) в нижних слоях или подниматься круче, пока воздух плотный, а горизонтально разгоняться выше.")
                },
                new[]
                {
                    "q = ½ · " + N(s.Density) + " · " + N(s.AirSpeed) + "² = " + N(s.DynamicPressure) + Loc.T(" Па"),
                    Loc.T("При той же плотности вдвое меньшая скорость даёт вчетверо меньшее сопротивление"),
                    Loc.T("Плотность падает в e раз на каждую шкалу высот H — несколько километров подъёма заметно снижают q")
                });

        static bool IsNeedsInsertion(AcademySituation s) =>
            !s.Landed && !s.EngineBurning && s.Bound && s.AnyPropellantLeft &&
            s.ApoapsisRadius > s.Radius + s.SpaceBoundary && s.PeriapsisRadius < s.Radius + s.SpaceBoundary;

        static AcademyHint MakeNeedsInsertion(AcademySituation s)
        {
            double ra = s.ApoapsisRadius, rp = Math.Max(1.0, s.PeriapsisRadius);
            double a = 0.5 * (ra + rp);
            double va = OrbitalMechanics.VisVivaSpeed(s.Mu, ra, a);
            double vc = OrbitalMechanics.CircularVelocity(s.Mu, ra);
            double dv = OrbitalMechanics.CircularizationDeltaV(s.Mu, ra, rp);
            var steps = new List<string>
            {
                "r_a = " + Km(ra) + ", r_p = " + Km(rp) + Loc.T(" (от центра); a = (r_a + r_p)/2 = ") + Km(a),
                Loc.T("Скорость в апоцентре v_a = √(μ(2/r_a − 1/a)) = ") + N(va) + Loc.T(" м/с"),
                Loc.T("Круговая там v_круг = √(μ/r_a) = ") + N(vc) + Loc.T(" м/с"),
                Loc.T("Разгон в апоцентре Δv = ") + N(vc) + " − " + N(va) + " = " + N(dv) + Loc.T(" м/с (запас ракеты ") + N(s.DeltaVRemaining) + Loc.T(" м/с)")
            };
            if (s.AvailableThrust > 0.0 && s.StageVacuumIsp > 0.0 && s.Mass > 0.0)
            {
                double burn = RocketEquations.BurnTimeForDeltaV(dv, s.StageVacuumIsp, s.Mass, s.AvailableThrust);
                steps.Add(Loc.T("На полной тяге это ≈ ") + N(burn) + Loc.T(" с работы двигателя — начните примерно на ") + N(burn / 2.0) + Loc.T(" с раньше апоцентра"));
            }
            return Hint(HintSeverity.Advice, Loc.T("Это ещё не орбита"),
                Loc.T("Апоцентр ") + Km(ra - s.Radius) + Loc.T(" — выше границы космоса, но перицентр ") + Km(s.PeriapsisRadius - s.Radius) + Loc.T(". Без манёвра аппарат вернётся вниз."),
                AcademyTopics.Insertion, AcademyCalculators.Insertion,
                new[]
                {
                    Loc.T("Какая точка траектории поднимется, если разогнаться в апоцентре?"),
                    Loc.T("Разгоняйтесь у апоцентра горизонтально, по направлению движения: тангаж 90°, курс 90° (восток). У апоцентра v_r ≈ 0."),
                    Loc.T("Сколько разгоняться? Сравните круговую скорость на высоте апоцентра со скоростью, которая будет там у вас (формула vis-viva).")
                },
                steps.ToArray());
        }

        static bool IsOutOfDeltaV(AcademySituation s) =>
            !s.Landed && !s.AnyPropellantLeft && s.Phase != FlightPhase.OrbitalFlight && s.Phase != FlightPhase.EscapeTrajectory &&
            (!s.Bound || s.PeriapsisRadius < s.Radius + s.SpaceBoundary) && s.DeltaVExpended > 0.0;

        static AcademyHint MakeOutOfDeltaV(AcademySituation s)
        {
            double losses = s.GravityLoss + s.DragLoss;
            double need = s.OrbitRequirement + losses;
            double deficit = s.DesignDeltaV > 0.0 ? need - s.DesignDeltaV : need - s.DeltaVExpended;
            var steps = new List<string>
            {
                Loc.T("Идеальное требование орбиты ") + Km(s.LowOrbitAltitude) + ": " + N(s.OrbitRequirement) + Loc.T(" м/с") + (s.DesignDeltaV > 0.0 ? Loc.T("; идеальный Δv ракеты: ") + N(s.DesignDeltaV) + Loc.T(" м/с") : ""),
                Loc.T("Израсходовано ") + N(s.DeltaVExpended) + Loc.T(" м/с, из них потери: гравитационные ") + N(s.GravityLoss) + Loc.T(", сопротивление ") + N(s.DragLoss) + Loc.T(" м/с"),
                Loc.T("С такими потерями нужно ≈ ") + N(need) + Loc.T(" м/с")
            };
            if (deficit > 0.0)
            {
                steps.Add(Loc.T("Не хватает ≈ ") + N(deficit) + Loc.T(" м/с"));
                if (s.StageVacuumIsp > 0.0 && s.Mass > 0.0)
                    steps.Add(Loc.T("Последней ступени (Isp ") + N(s.StageVacuumIsp) + Loc.T(" с, без топлива ") + N(s.Mass / 1000.0) + Loc.T(" т) для этого нужно ещё ≈ ") +
                              N(RocketEquations.PropellantForDeltaV(deficit, s.StageVacuumIsp, s.Mass) / 1000.0) + Loc.T(" т топлива — и тяга, чтобы его поднять"));
            }
            else
                steps.Add(Loc.T("Δv ракеты хватало — дело в траектории: слишком большие потери. Наклоняйтесь раньше и плавнее, не разгоняйтесь в плотном воздухе."));
            return Hint(HintSeverity.Warning, Loc.T("Топливо кончилось до орбиты"),
                Loc.T("Топливо кончилось, а орбиты нет. Текущая конфигурация ракеты не даёт достаточного Δv для этой миссии (или он потрачен на потери)."),
                AcademyTopics.DeltaVBudget, AcademyCalculators.Fuel,
                new[]
                {
                    Loc.T("Сравните идеальный Δv ракеты с идеальным требованием орбиты — и вспомните о потерях."),
                    Loc.T("Панель РАКЕТА показывает, сколько ушло на гравитационные потери и сопротивление. Какие из них можно уменьшить траекторией?"),
                    Loc.T("Варианты: больше топлива (бак К-140 в магазине), выше Isp, легче груз — или более пологий подъём.")
                },
                steps.ToArray());
        }

        static double StopDistance(AcademySituation s) =>
            LandingPhysics.StoppingDistance(-s.RadialVelocity, s.Mass > 0.0 ? s.AvailableThrust / s.Mass : 0.0, s.LocalGravity);

        static bool IsLandingBurn(AcademySituation s) =>
            s.HasRocket && !s.Landed && !s.EngineBurning && s.CurrentStageHasFuel && s.RadialVelocity < -15.0 &&
            s.AltitudeAboveSurface < Math.Min(1.5 * StopDistance(s) + 300.0, 50000.0);

        static AcademyHint MakeLandingBurn(AcademySituation s)
        {
            double v = -s.RadialVelocity, a = s.Mass > 0.0 ? s.AvailableThrust / s.Mass : 0.0, d = StopDistance(s);
            bool canStop = !double.IsInfinity(d);
            double impact = LandingPhysics.ImpactSpeed(s.AltitudeAboveSurface, v, s.LocalGravity);
            return Hint(HintSeverity.Warning, canStop ? Loc.T("Пора тормозить") : Loc.T("Тяги не хватит, чтобы сесть"),
                canStop
                    ? Loc.T("Снижение ") + N(v) + Loc.T(" м/с на высоте ") + N(s.AltitudeAboveSurface) + Loc.T(" м, двигатель выключен. Без торможения удар будет на ") + N(impact) + Loc.T(" м/с (опоры держат 6 м/с).")
                    : Loc.T("Ускорение от тяги (") + N(a) + Loc.T(" м/с²) не больше местной гравитации (") + N(s.LocalGravity) + Loc.T(" м/с²): остановить падение двигателем нельзя."),
                AcademyTopics.Acceleration, AcademyCalculators.Acceleration,
                new[]
                {
                    Loc.T("Чтобы погасить скорость v при торможении с ускорением (a − g), нужен путь d = v²/(2(a − g))."),
                    Loc.T("Включите двигатель (Enter) вертикально вверх (тангаж 0°), когда высота станет близка к d — с запасом на неточность."),
                    Loc.T("Перед касанием снизьте тягу (Ctrl), чтобы не взлететь снова: вертикальная скорость у земли — до 6 м/с.")
                },
                new[]
                {
                    "v = " + N(v) + Loc.T(" м/с, a_тяги = F/m = ") + N(a) + Loc.T(" м/с², g = ") + N(s.LocalGravity) + Loc.T(" м/с²"),
                    canStop ? "d = v²/(2(a − g)) = " + N(d) + Loc.T(" м — двигатель нужен не позже этой высоты") : Loc.T("a − g ≤ 0: торможения нет — сбросьте массу (пустая ступень) или сядьте на тело с меньшей g"),
                    Loc.T("Без двигателя: v_удара = √(v² + 2gh) = ") + N(impact) + Loc.T(" м/с")
                });
        }

        static AcademyHint MakeOrbitAchieved(AcademySituation s)
        {
            double a = 0.5 * (s.PeriapsisRadius + s.ApoapsisRadius);
            double period = OrbitalMechanics.CircularPeriod(s.Mu, a);
            return Hint(HintSeverity.Info, Loc.T("Орбита!"),
                Loc.T("Перицентр ") + Km(s.PeriapsisRadius - s.Radius) + Loc.T(", апоцентр ") + Km(s.ApoapsisRadius - s.Radius) + Loc.T(". Проверьте себя: сколько минут займёт один оборот?"),
                AcademyTopics.OrbitalVelocity, AcademyCalculators.Orbit,
                new[] { Loc.T("Период эллиптической орбиты зависит только от большой полуоси: T = 2π√(a³/μ)."), Loc.T("a = (r_a + r_p)/2 — радиусы от центра планеты.") },
                new[] { "a = (" + Km(s.ApoapsisRadius) + " + " + Km(s.PeriapsisRadius) + ")/2 = " + Km(a), "T = 2π√(a³/μ) = " + N(period) + Loc.T(" с = ") + N(period / 60.0) + Loc.T(" мин") });
        }

        static AcademyHint MakeEscape(AcademySituation s)
        {
            double ve = OrbitalMechanics.EscapeVelocity(s.Mu, s.DistanceFromCenter);
            return Hint(HintSeverity.Info, Loc.T("Скорость ухода достигнута"),
                Loc.T("Удельная энергия ε ≥ 0: аппарат больше не вернётся — он уходит от планеты. Убедитесь сами, сравнив скорость со второй космической на этой высоте."),
                AcademyTopics.EscapeVelocity, AcademyCalculators.Escape,
                new[] { Loc.T("Вторая космическая на расстоянии r от центра: √(2μ/r). Высоту переведите в r = R + h.") },
                new[] { "r = " + Km(s.DistanceFromCenter) + Loc.T(", v_уход = √(2μ/r) = ") + N(ve) + Loc.T(" м/с"), Loc.T("Ваша скорость ") + N(s.InertialSpeed) + Loc.T(" м/с ≥ ") + N(ve) + Loc.T(" м/с → ε ≥ 0") });
        }
    }
}
