using SpaceSim.Localization;
using System;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Exploration
{
    /// <summary>The four independent condition values of the surface robot.</summary>
    public enum RobotCondition
    {
        /// <summary>Frame, wheels and joints: falls, impacts, crushing pressure, crashes of the carrying vehicle.</summary>
        Integrity = 0,
        /// <summary>Thermal protection: surface temperature outside the operating range.</summary>
        Thermal = 1,
        /// <summary>Coatings and seals: acid / corrosive chemistry.</summary>
        Chemical = 2,
        /// <summary>Optics (the robot's eye): radiation, impacts and chemicals. At 0 the robot is blind.</summary>
        Visor = 3
    }

    /// <summary>
    /// What the robot's surroundings do to it at one place (SI): pressure [Pa], temperature [K] (0 = unknown, no thermal
    /// load), and the hazard levels that actually reach the ground (<see cref="HazardModel"/>: radiation only where the
    /// air is thin, acid only inside the air).
    /// </summary>
    public readonly struct RobotEnvironment
    {
        public RobotEnvironment(double pressure, double temperature, int acidLevel, int radiationLevel)
        {
            Pressure = Math.Max(0.0, pressure);
            Temperature = Math.Max(0.0, temperature);
            AcidLevel = Math.Max(0, acidLevel);
            RadiationLevel = Math.Max(0, radiationLevel);
        }

        /// <summary>[Pa]</summary>
        public double Pressure { get; }
        /// <summary>[K]; 0 = no data (no thermal load).</summary>
        public double Temperature { get; }
        public int AcidLevel { get; }
        public int RadiationLevel { get; }

        /// <summary>
        /// The environment on the ground of a body: the atmosphere's surface pressure and temperature, and the body's hazard
        /// levels filtered through <see cref="HazardModel.Evaluate"/> with the surface density ratio (1 under an atmosphere,
        /// 0 on an airless body) and no protection of its own.
        /// </summary>
        public static RobotEnvironment AtSurface(PlanetPhysicalProperties planet, EnvironmentHazards hazards)
        {
            AtmosphereProperties a = planet != null ? planet.atmosphere : null;
            bool air = a != null && a.hasAtmosphere && a.surfacePressure > 0.0;
            double p = air ? a.surfacePressure : 0.0;
            double t = a != null ? a.surfaceTemperature : 0.0;
            HazardExposure e = HazardModel.Evaluate(hazards, air ? 1.0 : 0.0, 0, 0);
            return new RobotEnvironment(p, t, e.Acid, e.Radiation);
        }
    }

    /// <summary>
    /// Limits and damage rates of the surface robot — data, not code: every number the status model uses lives here (SI,
    /// condition values as fractions of 1, rates per second of simulated time). The defaults are game balance with stated
    /// reasons: inside the old suit envelope (≤ 2 atm, 90–480 K, no acid) nothing wears; outside it the robot may still go
    /// out up to the exit limits, but the matching condition drains.
    /// </summary>
    [Serializable]
    public sealed class RobotDamageRates
    {
        // ---- Exit limits: beyond these the robot is not let out at all -----------------------------------------
        /// <summary>Highest ambient pressure the hull is rated for [Pa] (10 atm).</summary>
        public double maxExitPressure = 10.0 * 101325.0;
        /// <summary>Exit temperature range [K].</summary>
        public double minExitTemperature = 60.0, maxExitTemperature = 700.0;
        /// <summary>Highest hazard levels at the surface that still allow an exit.</summary>
        public int maxExitAcidLevel = 1, maxExitRadiationLevel = 2;

        // ---- Damage-free operating envelope ---------------------------------------------------------------------
        /// <summary>Pressure up to which the hull takes no load [Pa] (the suit's 2 atm).</summary>
        public double safePressure = 2.0e5;
        /// <summary>Temperatures between these take nothing from the thermal protection [K] (the suit's 90–480 K).</summary>
        public double minSafeTemperature = 90.0, maxSafeTemperature = 480.0;
        /// <summary>Touchdown or collision speed absorbed without damage [m/s].</summary>
        public double safeImpactSpeed = 3.0;

        // ---- Rates -----------------------------------------------------------------------------------------------
        /// <summary>Integrity loss per pascal above <see cref="safePressure"/> per second: 10 atm ≈ 3 min.</summary>
        public double integrityPerPascalSecond = 7.0e-9;
        /// <summary>Thermal loss per kelvin outside the safe range per second: 100 K beyond ≈ 2.4 min.</summary>
        public double thermalPerKelvinSecond = 7.0e-5;
        /// <summary>Chemical loss per second, indexed by the acid level (0, 1, 2, 3+): level 1 ≈ 5 min, level 2 ≈ 1.5 min.</summary>
        public double[] chemicalPerLevel = { 0.0, 1.0 / 300.0, 1.0 / 90.0, 1.0 / 30.0 };
        /// <summary>Visor loss per second from radiation, indexed by level: level 1 ≈ 1 h, level 2 ≈ 5 min.</summary>
        public double[] visorRadiationPerLevel = { 0.0, 1.0 / 3600.0, 1.0 / 300.0, 1.0 / 60.0 };
        /// <summary>Share of the chemical rate that also etches the visor.</summary>
        public double visorChemicalShare = 0.5;
        /// <summary>Integrity loss per m/s of impact speed above <see cref="safeImpactSpeed"/>.</summary>
        public double integrityPerImpactSpeed = 0.06;
        /// <summary>Share of an impact's integrity damage that also cracks the visor.</summary>
        public double visorImpactShare = 0.5;
        /// <summary>Share of the carrying vehicle's mechanical damage (hard landing, crash) the robot inside takes.</summary>
        public double vehicleDamageShare = 1.0;

        public static RobotDamageRates Default => new RobotDamageRates();

        public RobotDamageRates Clone()
        {
            var c = (RobotDamageRates)MemberwiseClone();
            c.chemicalPerLevel = (double[])chemicalPerLevel.Clone();
            c.visorRadiationPerLevel = (double[])visorRadiationPerLevel.Clone();
            return c;
        }

        internal static double ByLevel(double[] table, int level)
        {
            if (table == null || table.Length == 0 || level <= 0) return 0.0;
            return table[Math.Min(level, table.Length - 1)];
        }
    }

    /// <summary>
    /// Condition of the surface robot: four independent values in [0, 1] (1 = intact), worn down by its surroundings and
    /// by mechanical shocks. Integrity, thermal or chemical at 0 disables the robot; a visor at 0 leaves it blind but
    /// still able to move. Pure model: the runtime feeds it the environment, the walker's impact speeds and the vehicle's
    /// landings, and draws the result.
    /// </summary>
    public sealed class RobotStatus
    {
        public const int ConditionCount = 4;

        readonly double[] values = new double[ConditionCount];
        readonly double[] rates = new double[ConditionCount];

        public RobotStatus(RobotDamageRates damageRates = null)
        {
            Rates = damageRates ?? RobotDamageRates.Default;
            Reset();
        }

        public RobotDamageRates Rates { get; }

        public double this[RobotCondition c] => values[(int)c];
        public double Integrity => values[(int)RobotCondition.Integrity];
        public double Thermal => values[(int)RobotCondition.Thermal];
        public double Chemical => values[(int)RobotCondition.Chemical];
        public double Visor => values[(int)RobotCondition.Visor];

        /// <summary>Loss per second of each condition during the last <see cref="Step"/> (fraction of 1 per second).</summary>
        public double RateOf(RobotCondition c) => rates[(int)c];

        /// <summary>Integrity, thermal protection or chemical protection is gone: the robot cannot work outside.</summary>
        public bool Disabled => Integrity <= 0.0 || Thermal <= 0.0 || Chemical <= 0.0;
        /// <summary>The visor is gone: the camera sees nothing.</summary>
        public bool Blind => Visor <= 0.0;
        /// <summary>The weakest of the three values that disable the robot.</summary>
        public double Weakest => Math.Min(Integrity, Math.Min(Thermal, Chemical));

        public int Impacts { get; private set; }
        /// <summary>Speed [m/s] and integrity damage of the last impact that did harm.</summary>
        public double LastImpactSpeed { get; private set; }
        public double LastImpactDamage { get; private set; }

        public void Reset()
        {
            for (int i = 0; i < ConditionCount; i++)
            {
                values[i] = 1.0;
                rates[i] = 0.0;
            }
            Impacts = 0;
            LastImpactSpeed = 0.0;
            LastImpactDamage = 0.0;
        }

        /// <summary>Sets one value directly (save games, tests, the development bridge); clamped to [0, 1].</summary>
        public void Set(RobotCondition c, double value) => values[(int)c] = Math.Max(0.0, Math.Min(1.0, value));

        /// <summary>Removes <paramref name="fraction"/> from one value (never below 0, never repairs).</summary>
        public void Damage(RobotCondition c, double fraction)
        {
            if (!(fraction > 0.0)) return;
            values[(int)c] = Math.Max(0.0, values[(int)c] - fraction);
        }

        /// <summary>Loss rates [1/s] of the four values in an environment (no state change).</summary>
        public static void EnvironmentRates(RobotEnvironment env, RobotDamageRates r, double[] result)
        {
            if (result == null || result.Length < ConditionCount) throw new ArgumentException("needs " + ConditionCount + " slots", nameof(result));
            r = r ?? RobotDamageRates.Default;
            double overPressure = Math.Max(0.0, env.Pressure - r.safePressure);
            double overHeat = 0.0;
            if (env.Temperature > 0.0)
                overHeat = Math.Max(0.0, env.Temperature - r.maxSafeTemperature) + Math.Max(0.0, r.minSafeTemperature - env.Temperature);
            double chemical = RobotDamageRates.ByLevel(r.chemicalPerLevel, env.AcidLevel);
            result[(int)RobotCondition.Integrity] = overPressure * r.integrityPerPascalSecond;
            result[(int)RobotCondition.Thermal] = overHeat * r.thermalPerKelvinSecond;
            result[(int)RobotCondition.Chemical] = chemical;
            result[(int)RobotCondition.Visor] = RobotDamageRates.ByLevel(r.visorRadiationPerLevel, env.RadiationLevel) + chemical * r.visorChemicalShare;
        }

        /// <summary>Wears the four values for <paramref name="dt"/> seconds in <paramref name="env"/>.</summary>
        public void Step(RobotEnvironment env, double dt)
        {
            EnvironmentRates(env, Rates, rates);
            if (!(dt > 0.0)) return;
            for (int i = 0; i < ConditionCount; i++)
                if (rates[i] > 0.0) values[i] = Math.Max(0.0, values[i] - rates[i] * dt);
        }

        /// <summary>
        /// A shock at <paramref name="speed"/> [m/s] (touchdown after a fall, running into the tether's end or the shore):
        /// above the safe speed the frame loses <see cref="RobotDamageRates.integrityPerImpactSpeed"/> per m/s and the visor
        /// a share of it. Returns the integrity damage.
        /// </summary>
        public double Impact(double speed)
        {
            double excess = speed - Rates.safeImpactSpeed;
            if (!(excess > 0.0)) return 0.0;
            double damage = excess * Rates.integrityPerImpactSpeed;
            Damage(RobotCondition.Integrity, damage);
            Damage(RobotCondition.Visor, damage * Rates.visorImpactShare);
            Impacts++;
            LastImpactSpeed = speed;
            LastImpactDamage = damage;
            return damage;
        }

        /// <summary>The carrying vehicle took mechanical damage (hard landing, crash) with the robot aboard.</summary>
        public void VehicleDamage(double fraction)
        {
            double d = Math.Max(0.0, fraction) * Rates.vehicleDamageShare;
            Damage(RobotCondition.Integrity, d);
            Damage(RobotCondition.Visor, d * Rates.visorImpactShare);
        }

        /// <summary>
        /// Seconds until the robot is disabled at the current loss rates (∞ when nothing disabling wears). The visor is not
        /// counted: a blind robot still works.
        /// </summary>
        public double TimeToDisabled()
        {
            double t = double.PositiveInfinity;
            for (int i = 0; i < (int)RobotCondition.Visor; i++)
                if (rates[i] > 0.0) t = Math.Min(t, values[i] / rates[i]);
            return t;
        }

        /// <summary>Seconds until the visor is gone at the current rate (∞ when it does not wear).</summary>
        public double TimeToBlind()
        {
            double r = rates[(int)RobotCondition.Visor];
            return r > 0.0 ? Visor / r : double.PositiveInfinity;
        }
    }

    /// <summary>Exit rules of the surface robot (it replaced the suited astronaut, with wider but still finite limits).</summary>
    public static class RobotEva
    {
        /// <summary>
        /// Whether the robot may go out here: the hull's pressure and temperature ratings, acid and radiation at the
        /// surface, and its own condition. Beyond the damage-free envelope it may go but wears (<see cref="RobotStatus"/>).
        /// </summary>
        public static CollectionCheck CanExit(PlanetPhysicalProperties planet, EnvironmentHazards hazards, RobotStatus status = null, RobotDamageRates rates = null)
        {
            RobotDamageRates r = rates ?? (status != null ? status.Rates : RobotDamageRates.Default);
            if (status != null && status.Disabled)
                return new CollectionCheck(false, Loc.T("Робот неисправен: ") + DisabledReason(status) + Loc.T(" — нужна новая ракета."));
            RobotEnvironment env = RobotEnvironment.AtSurface(planet, hazards);
            double p = env.Pressure, t = env.Temperature;
            if (p > r.maxExitPressure)
                return new CollectionCheck(false, Loc.T("Давление ") + Academy.AcademyNumber.Format(p / 101325.0) + Loc.T(" атм — корпус робота не выдержит (предел ") +
                                                  Academy.AcademyNumber.Format(r.maxExitPressure / 101325.0) + Loc.T(" атм)."));
            if (t > r.maxExitTemperature)
                return new CollectionCheck(false, Loc.T("Температура ") + Academy.AcademyNumber.Format(t - 273.15) + Loc.T(" °C — робот перегреется (предел ") +
                                                  Academy.AcademyNumber.Format(r.maxExitTemperature - 273.15) + " °C).");
            if (t > 0.0 && t < r.minExitTemperature)
                return new CollectionCheck(false, Loc.T("Температура ") + Academy.AcademyNumber.Format(t - 273.15) + Loc.T(" °C — ниже предела робота (") +
                                                  Academy.AcademyNumber.Format(r.minExitTemperature - 273.15) + " °C).");
            if (env.AcidLevel > r.maxExitAcidLevel)
                return new CollectionCheck(false, Loc.T("Кислотность уровня ") + env.AcidLevel + Loc.T(" — покрытие робота растворится за секунды."));
            if (env.RadiationLevel > r.maxExitRadiationLevel)
                return new CollectionCheck(false, Loc.T("Радиация уровня ") + env.RadiationLevel + Loc.T(" — электроника робота откажет."));
            return new CollectionCheck(true, "");
        }

        /// <summary>What the environment will do outside (empty when nothing wears): shown before and during the exit.</summary>
        public static string Warning(RobotEnvironment env, RobotDamageRates rates = null)
        {
            var r = new double[RobotStatus.ConditionCount];
            RobotStatus.EnvironmentRates(env, rates, r);
            string s = "";
            for (int i = 0; i < RobotStatus.ConditionCount; i++)
            {
                if (r[i] <= 0.0) continue;
                if (s.Length > 0) s += ", ";
                s += ConditionName((RobotCondition)i) + Loc.T(" −") + Academy.AcademyNumber.Format(r[i] * 6000.0) + Loc.T(" %/мин");
            }
            return s;
        }

        public static string ConditionName(RobotCondition c)
        {
            switch (c)
            {
                case RobotCondition.Integrity: return Loc.T("Корпус");
                case RobotCondition.Thermal: return Loc.T("Терморегуляция");
                case RobotCondition.Chemical: return Loc.T("Химзащита");
                default: return Loc.T("Визор");
            }
        }

        static string DisabledReason(RobotStatus s)
        {
            if (s.Integrity <= 0.0) return Loc.T("корпус разрушен");
            if (s.Thermal <= 0.0) return Loc.T("терморегуляция отказала");
            return Loc.T("химзащита разрушена");
        }
    }
}
