using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using SpaceSim.Localization;

namespace SpaceSim.Exploration
{
    /// <summary>The slots of the surface robot assembled before a team mission (one option per slot, instruments are a set).</summary>
    public enum RobotSlot
    {
        Battery = 0,
        /// <summary>Накладки на ноги: what the feet stand on (rock, dust, ice); «нет» for a robot that never lands.</summary>
        Pads = 1,
        Thermal = 2,
        Radiation = 3,
        Coating = 4,
        Hull = 5
    }

    /// <summary>Ground under the landing site, read off the satellite images.</summary>
    public enum SurfaceKind
    {
        Rock = 0,
        Dust = 1,
        Ice = 2,
        /// <summary>No landing: the robot works from orbit.</summary>
        None = 3
    }

    /// <summary>What the mission wants the robot to do (several at once).</summary>
    [Flags]
    public enum RobotTask
    {
        None = 0,
        Photos = 1,
        Samples = 2,
        IceRadar = 4,
        Relay = 8
    }

    /// <summary>One choice for a slot: mass, and what it gives (only the fields that matter for its slot are set).</summary>
    public sealed class RobotOption
    {
        internal RobotOption(RobotSlot slot, int index, string name, string note, double massKg)
        {
            Slot = slot;
            Index = index;
            rawName = name;
            rawNote = note;
            MassKg = massKg;
        }

        readonly string rawName, rawNote;
        public RobotSlot Slot { get; }
        public int Index { get; }
        public string Name => Loc.T(rawName);
        /// <summary>One line on what it is for.</summary>
        public string Note => Loc.T(rawNote);
        public double MassKg { get; }

        /// <summary>Battery: stored energy [Wh].</summary>
        public double EnergyWh { get; internal set; }
        /// <summary>Pads: the ground they are made for (<see cref="SurfaceKind.None"/> = no pads).</summary>
        public SurfaceKind Ground { get; internal set; } = SurfaceKind.None;
        /// <summary>Thermal: damage-free range [K].</summary>
        public double MinSafeK { get; internal set; }
        public double MaxSafeK { get; internal set; }
        /// <summary>Radiation shield / coating level covered.</summary>
        public int Level { get; internal set; }
        /// <summary>Hull: damage-free ambient pressure [Pa].</summary>
        public double SafePressure { get; internal set; }
    }

    /// <summary>A science instrument the robot may carry.</summary>
    public sealed class RobotInstrument
    {
        internal RobotInstrument(RobotTask task, string name, string note, double massKg, double energyPerUseWh)
        {
            Task = task;
            rawName = name;
            rawNote = note;
            MassKg = massKg;
            EnergyPerUseWh = energyPerUseWh;
        }

        readonly string rawName, rawNote;
        public RobotTask Task { get; }
        public string Name => Loc.T(rawName);
        public string Note => Loc.T(rawNote);
        public double MassKg { get; }
        /// <summary>Energy of one use (a photo, a sample, a radar pass, the relay set-up) [Wh].</summary>
        public double EnergyPerUseWh { get; }
    }

    /// <summary>The parts catalogue of the team-mission robot. Data, not code: balance numbers live here.</summary>
    public static class RobotParts
    {
        /// <summary>Frame, wheels, motors and computer: always on board [kg].</summary>
        public const double FrameMassKg = 38.0;
        /// <summary>Driving on flat ground under 1 g with a 100 kg robot and the right pads [Wh/km].</summary>
        public const double DriveWhPerKm = 90.0;
        /// <summary>Electronics idling (orbit work, waiting) [W].</summary>
        public const double IdleWatts = 25.0;
        /// <summary>Reserve the robot pilot should keep on top of the plan (share of the need).</summary>
        public const double RecommendedReserve = 0.2;

        static readonly RobotOption[][] options =
        {
            new[]
            {
                new RobotOption(RobotSlot.Battery, 0, "Малая батарея", "2 кВт·ч — короткий выезд", 9.0) { EnergyWh = 2000.0 },
                new RobotOption(RobotSlot.Battery, 1, "Средняя батарея", "5 кВт·ч — несколько километров", 19.0) { EnergyWh = 5000.0 },
                new RobotOption(RobotSlot.Battery, 2, "Большая батарея", "10 кВт·ч — дальний маршрут", 36.0) { EnergyWh = 10000.0 },
                new RobotOption(RobotSlot.Battery, 3, "Сверхъёмкая батарея", "18 кВт·ч — очень тяжёлая", 62.0) { EnergyWh = 18000.0 },
            },
            new[]
            {
                new RobotOption(RobotSlot.Pads, 0, "Без накладок", "Только для работы с орбиты", 0.0) { Ground = SurfaceKind.None },
                new RobotOption(RobotSlot.Pads, 1, "Жёсткие накладки", "Скальный грунт, камни", 4.0) { Ground = SurfaceKind.Rock },
                new RobotOption(RobotSlot.Pads, 2, "Широкие накладки", "Пыль, песок, реголит — не вязнут", 6.0) { Ground = SurfaceKind.Dust },
                new RobotOption(RobotSlot.Pads, 3, "Шипованные накладки", "Лёд — не скользят", 5.0) { Ground = SurfaceKind.Ice },
            },
            new[]
            {
                new RobotOption(RobotSlot.Thermal, 0, "Базовая терморегуляция", "−183 … +207 °C", 0.0) { MinSafeK = 90.0, MaxSafeK = 480.0 },
                new RobotOption(RobotSlot.Thermal, 1, "Утеплённый корпус", "−233 … +127 °C: подогрев для холода", 7.0) { MinSafeK = 40.0, MaxSafeK = 400.0 },
                new RobotOption(RobotSlot.Thermal, 2, "Жаростойкий корпус", "−123 … +427 °C: экраны и охлаждение", 10.0) { MinSafeK = 150.0, MaxSafeK = 700.0 },
            },
            new[]
            {
                new RobotOption(RobotSlot.Radiation, 0, "Без радиационной защиты", "Только там, где радиации нет", 0.0) { Level = 0 },
                new RobotOption(RobotSlot.Radiation, 1, "Лёгкий экран", "Защищает от радиации 1-го уровня", 5.0) { Level = 1 },
                new RobotOption(RobotSlot.Radiation, 2, "Тяжёлый экран", "Защищает от радиации 2-го уровня", 15.0) { Level = 2 },
            },
            new[]
            {
                new RobotOption(RobotSlot.Coating, 0, "Обычное покрытие", "Без защиты от кислот", 0.0) { Level = 0 },
                new RobotOption(RobotSlot.Coating, 1, "Кислотостойкое покрытие", "Держит кислотную среду 1-го уровня", 3.0) { Level = 1 },
                new RobotOption(RobotSlot.Coating, 2, "Керамическое покрытие", "Держит кислотную среду 2-го уровня", 8.0) { Level = 2 },
            },
            new[]
            {
                new RobotOption(RobotSlot.Hull, 0, "Лёгкий корпус", "Давление до 2 атм", 0.0) { SafePressure = 2.0 * 101325.0 },
                new RobotOption(RobotSlot.Hull, 1, "Усиленный корпус", "Давление до 10 атм", 14.0) { SafePressure = 10.0 * 101325.0 },
                new RobotOption(RobotSlot.Hull, 2, "Батискаф", "Давление до 100 атм", 40.0) { SafePressure = 100.0 * 101325.0 },
            },
        };

        static readonly RobotInstrument[] instruments =
        {
            new RobotInstrument(RobotTask.Photos, "Камера высокого разрешения", "Снимки цели", 2.5, 4.0),
            new RobotInstrument(RobotTask.Samples, "Бур и контейнеры", "Пробы грунта", 11.0, 150.0),
            new RobotInstrument(RobotTask.IceRadar, "Подповерхностный радар", "Ищет лёд под грунтом", 7.0, 300.0),
            new RobotInstrument(RobotTask.Relay, "Антенна-ретранслятор", "Связь с центром управления", 6.0, 200.0),
        };

        public static readonly RobotSlot[] Slots =
            { RobotSlot.Battery, RobotSlot.Pads, RobotSlot.Thermal, RobotSlot.Radiation, RobotSlot.Coating, RobotSlot.Hull };

        public static IReadOnlyList<RobotOption> Options(RobotSlot slot) => options[(int)slot];
        public static RobotOption Option(RobotSlot slot, int index)
        {
            RobotOption[] list = options[(int)slot];
            return list[Math.Max(0, Math.Min(list.Length - 1, index))];
        }

        public static IReadOnlyList<RobotInstrument> Instruments => instruments;
        public static RobotInstrument Instrument(RobotTask task) => Array.Find(instruments, i => i.Task == task);

        public static string SlotName(RobotSlot slot)
        {
            switch (slot)
            {
                case RobotSlot.Battery: return Loc.T("БАТАРЕЯ");
                case RobotSlot.Pads: return Loc.T("НАКЛАДКИ НА НОГИ");
                case RobotSlot.Thermal: return Loc.T("ТЕРМОЗАЩИТА");
                case RobotSlot.Radiation: return Loc.T("ЗАЩИТА ОТ РАДИАЦИИ");
                case RobotSlot.Coating: return Loc.T("ПОКРЫТИЕ");
                default: return Loc.T("КОРПУС");
            }
        }

        public static string GroundName(SurfaceKind g)
        {
            switch (g)
            {
                case SurfaceKind.Rock: return Loc.T("скальный грунт");
                case SurfaceKind.Dust: return Loc.T("пыль и реголит");
                case SurfaceKind.Ice: return Loc.T("лёд");
                default: return Loc.T("без посадки");
            }
        }

        public static string TaskName(RobotTask t)
        {
            switch (t)
            {
                case RobotTask.Photos: return Loc.T("снимки");
                case RobotTask.Samples: return Loc.T("пробы грунта");
                case RobotTask.IceRadar: return Loc.T("поиск льда");
                case RobotTask.Relay: return Loc.T("ретрансляция связи");
                default: return "";
            }
        }
    }

    /// <summary>
    /// The robot as the robot pilot assembled it: one option index per <see cref="RobotSlot"/> plus a set of instruments.
    /// Encodes to a short string for the network («b=1;p=2;t=0;r=1;c=0;h=0;i=3»).
    /// </summary>
    public sealed class RobotDesign
    {
        readonly int[] choice = new int[RobotParts.Slots.Length];

        public RobotDesign()
        {
            choice[(int)RobotSlot.Battery] = 1;
            choice[(int)RobotSlot.Pads] = 1;
        }

        public RobotTask Instruments { get; set; } = RobotTask.Photos;

        public int this[RobotSlot slot]
        {
            get => choice[(int)slot];
            set => choice[(int)slot] = Math.Max(0, Math.Min(RobotParts.Options(slot).Count - 1, value));
        }

        public RobotOption Part(RobotSlot slot) => RobotParts.Option(slot, choice[(int)slot]);

        public bool Has(RobotTask task) => (Instruments & task) == task && task != RobotTask.None;

        public void Toggle(RobotTask task) => Instruments ^= task;

        public double MassKg
        {
            get
            {
                double m = RobotParts.FrameMassKg;
                foreach (RobotSlot s in RobotParts.Slots) m += Part(s).MassKg;
                foreach (RobotInstrument i in RobotParts.Instruments)
                    if (Has(i.Task)) m += i.MassKg;
                return m;
            }
        }

        public RobotDesign Clone()
        {
            var c = new RobotDesign { Instruments = Instruments };
            Array.Copy(choice, c.choice, choice.Length);
            return c;
        }

        static readonly char[] keys = { 'b', 'p', 't', 'r', 'c', 'h' };

        public string Encode()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < keys.Length; i++) sb.Append(keys[i]).Append('=').Append(choice[i]).Append(';');
            sb.Append("i=").Append(((int)Instruments).ToString(CultureInfo.InvariantCulture));
            return sb.ToString();
        }

        /// <summary>Reads <see cref="Encode"/>; unknown or broken parts keep their defaults. Null for empty input.</summary>
        public static RobotDesign Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var d = new RobotDesign();
            foreach (string part in text.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq != 1 || !int.TryParse(part.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) continue;
                char k = part[0];
                if (k == 'i')
                {
                    d.Instruments = (RobotTask)(v & 15);
                    continue;
                }
                int slot = Array.IndexOf(keys, k);
                if (slot >= 0) d[(RobotSlot)slot] = v;
            }
            return d;
        }

        /// <summary>
        /// Puts the assembled protection into the robot's wear model: hull pressure, thermal range, shielding and coating
        /// (the talents of a career profile are already in <paramref name="rates"/>; the parts replace the envelope, the
        /// talents' rate reductions stay).
        /// </summary>
        public void ApplyTo(RobotDamageRates rates)
        {
            if (rates == null) return;
            RobotOption hull = Part(RobotSlot.Hull), thermal = Part(RobotSlot.Thermal);
            rates.safePressure = hull.SafePressure;
            rates.maxExitPressure = Math.Max(hull.SafePressure * 1.5, 2.0e5);
            rates.minSafeTemperature = thermal.MinSafeK;
            rates.maxSafeTemperature = thermal.MaxSafeK;
            rates.minExitTemperature = Math.Max(3.0, thermal.MinSafeK - 40.0);
            rates.maxExitTemperature = thermal.MaxSafeK + 150.0;
            int rad = Part(RobotSlot.Radiation).Level, acid = Part(RobotSlot.Coating).Level;
            rates.visorRadiationPerLevel = Shift(rates.visorRadiationPerLevel, rad);
            rates.chemicalPerLevel = Shift(rates.chemicalPerLevel, acid);
            rates.maxExitAcidLevel = Math.Max(rates.maxExitAcidLevel, acid + 1);
            rates.maxExitRadiationLevel = Math.Max(rates.maxExitRadiationLevel, rad + 2);
        }

        /// <summary>A shield of level n takes away the first n levels of a per-level table.</summary>
        static double[] Shift(double[] table, int levels)
        {
            if (table == null || levels <= 0) return table;
            var r = new double[table.Length];
            for (int i = 0; i < r.Length; i++) r[i] = i > levels ? table[i - levels] : 0.0;
            return r;
        }
    }

    /// <summary>What the satellite images told about the robot's work place. Same on every computer (built from the mission).</summary>
    public sealed class RobotSurvey
    {
        public string BodyId = "";
        public string BodyName = "";
        /// <summary>True when the robot lands and drives; false when it works from orbit.</summary>
        public bool Surface;
        public SurfaceKind Ground = SurfaceKind.None;
        /// <summary>[m/s²]</summary>
        public double Gravity;
        /// <summary>[Pa] at the work place (0 in orbit).</summary>
        public double Pressure;
        /// <summary>Day / night temperature range at the work place [K].</summary>
        public double MinTemperature, MaxTemperature;
        /// <summary>Hazard levels that reach the robot there.</summary>
        public int Radiation, Acid;
        /// <summary>Surface: one-way drive from the landing point to the target [km].</summary>
        public double DistanceKm;
        /// <summary>Surface: the robot has to come back to the rocket.</summary>
        public bool Return;
        /// <summary>Climb on the route [m] (costs energy on the way up).</summary>
        public double ClimbM;
        /// <summary>Orbit: hours the robot must work.</summary>
        public double WorkHours;
        public RobotTask Tasks;
        /// <summary>How many uses each task needs (photos, samples, radar passes, relay set-ups), indexed by bit.</summary>
        public int Photos, Samples, RadarPasses, RelaySetups;
        /// <summary>Picks the look of the satellite pictures (same seed on every computer).</summary>
        public int Seed;
        /// <summary>Latitude of the target [deg].</summary>
        public double Latitude;

        public int UsesOf(RobotTask t)
        {
            switch (t)
            {
                case RobotTask.Photos: return Photos;
                case RobotTask.Samples: return Samples;
                case RobotTask.IceRadar: return RadarPasses;
                case RobotTask.Relay: return RelaySetups;
                default: return 0;
            }
        }
    }

    public enum RobotCheckLevel
    {
        Ok = 0,
        Warning = 1,
        /// <summary>The mission cannot be done with this robot.</summary>
        Critical = 2
    }

    public readonly struct RobotCheck
    {
        public RobotCheck(RobotCheckLevel level, string text)
        {
            Level = level;
            Text = text;
        }

        public RobotCheckLevel Level { get; }
        public string Text { get; }
    }

    /// <summary>The robot measured against the survey: mass, energy budget and one line per requirement.</summary>
    public sealed class RobotReport
    {
        public double MassKg;
        public double EnergyWh;
        public double EnergyNeededWh;
        /// <summary>Energy of the drive per km with this robot on this ground [Wh/km] (0 in orbit).</summary>
        public double DriveWhPerKm;
        /// <summary>How far the battery takes the robot after the instrument work, there and back counted [km].</summary>
        public double RangeKm;
        public readonly List<RobotCheck> Checks = new List<RobotCheck>();

        public RobotCheckLevel Worst
        {
            get
            {
                RobotCheckLevel w = RobotCheckLevel.Ok;
                foreach (RobotCheck c in Checks)
                    if (c.Level > w) w = c.Level;
                return w;
            }
        }

        public int Count(RobotCheckLevel level)
        {
            int n = 0;
            foreach (RobotCheck c in Checks)
                if (c.Level == level) n++;
            return n;
        }
    }

    /// <summary>Checks a <see cref="RobotDesign"/> against a <see cref="RobotSurvey"/>. Pure, no Unity: covered by tests.</summary>
    public static class RobotAssessment
    {
        /// <summary>Drive energy multiplier for pads on a ground (rows: pads Rock/Dust/Ice/None; columns: ground Rock/Dust/Ice).</summary>
        static readonly double[,] padFactor =
        {
            { 1.0, 1.7, 1.5 },   // hard pads: sink in dust, slide on ice
            { 1.2, 1.0, 1.4 },   // wide pads
            { 1.3, 1.5, 1.0 },   // spiked pads
            { 2.5, 3.0, 3.0 },   // no pads at all
        };

        public static double PadFactor(SurfaceKind pads, SurfaceKind ground)
        {
            if (ground == SurfaceKind.None) return 1.0;
            int row = pads == SurfaceKind.None ? 3 : (int)pads;
            return padFactor[row, (int)ground];
        }

        static string C(double kelvin) => Math.Round(kelvin - 273.15).ToString("0", CultureInfo.InvariantCulture) + " °C";
        static string Atm(double pa) => (pa / 101325.0).ToString(pa < 101325.0 * 10 ? "0.##" : "0", CultureInfo.InvariantCulture) + Loc.T(" атм");
        static string Kwh(double wh) => (wh / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + Loc.T(" кВт·ч");
        static string Km(double km) => km.ToString("0.0", CultureInfo.InvariantCulture) + Loc.T(" км");

        public static RobotReport Evaluate(RobotDesign d, RobotSurvey s)
        {
            var r = new RobotReport { MassKg = d.MassKg, EnergyWh = d.Part(RobotSlot.Battery).EnergyWh };
            if (s == null) return r;

            // ---- instruments for the tasks ----
            double taskWh = 0.0;
            foreach (RobotInstrument ins in RobotParts.Instruments)
            {
                bool need = (s.Tasks & ins.Task) != 0;
                bool has = d.Has(ins.Task);
                if (need && !has)
                    r.Checks.Add(new RobotCheck(RobotCheckLevel.Critical, Loc.T("Нет прибора для задачи «") + RobotParts.TaskName(ins.Task) + "»: " + ins.Name + "."));
                else if (need)
                    taskWh += ins.EnergyPerUseWh * Math.Max(1, s.UsesOf(ins.Task));
                else if (has)
                    r.Checks.Add(new RobotCheck(RobotCheckLevel.Warning, ins.Name + Loc.T(" не нужен в этой миссии: лишние ") +
                        ins.MassKg.ToString("0.#", CultureInfo.InvariantCulture) + Loc.T(" кг.")));
            }

            // ---- energy ----
            double driveKm = 0.0;
            if (s.Surface)
            {
                SurfaceKind pads = d.Part(RobotSlot.Pads).Ground;
                double gravity = Math.Max(0.05, s.Gravity / 9.81);
                // Rolling resistance grows with weight (m·g); climbing costs m·g·h at ~35 % drive efficiency.
                r.DriveWhPerKm = RobotParts.DriveWhPerKm * (r.MassKg / 100.0) * gravity * PadFactor(pads, s.Ground);
                driveKm = s.DistanceKm * (s.Return ? 2.0 : 1.0);
                double climbWh = r.MassKg * s.Gravity * Math.Max(0.0, s.ClimbM) / 3600.0 / 0.35;
                r.EnergyNeededWh = driveKm * r.DriveWhPerKm + climbWh + taskWh;
                double leftForDrive = r.EnergyWh - taskWh - climbWh;
                r.RangeKm = r.DriveWhPerKm > 0.0 ? Math.Max(0.0, leftForDrive) / r.DriveWhPerKm : 0.0;

                if (pads == SurfaceKind.None)
                    r.Checks.Add(new RobotCheck(RobotCheckLevel.Critical, Loc.T("Нет накладок на ноги: по грунту робот почти не проедет.")));
                else if (pads != s.Ground)
                    r.Checks.Add(new RobotCheck(RobotCheckLevel.Warning, Loc.T("Накладки не под грунт: на снимках ") + RobotParts.GroundName(s.Ground) +
                        Loc.T(". Расход энергии ×") + PadFactor(pads, s.Ground).ToString("0.0", CultureInfo.InvariantCulture) + "."));
            }
            else
            {
                r.EnergyNeededWh = s.WorkHours * RobotParts.IdleWatts + taskWh;
                if (d.Part(RobotSlot.Pads).Ground != SurfaceKind.None)
                    r.Checks.Add(new RobotCheck(RobotCheckLevel.Warning, Loc.T("Посадки нет — накладки на ноги не нужны (лишняя масса).")));
            }

            if (r.EnergyWh < r.EnergyNeededWh)
            {
                string what = s.Surface
                    ? Loc.T("Батареи не хватит: нужно ") + Kwh(r.EnergyNeededWh) + Loc.T(" (маршрут ") + Km(driveKm) +
                      (s.Return ? Loc.T(" туда и обратно") : "") + Loc.T("), а в батарее ") + Kwh(r.EnergyWh) + Loc.T(". Робот проедет ") + Km(r.RangeKm) + "."
                    : Loc.T("Батареи не хватит: нужно ") + Kwh(r.EnergyNeededWh) + Loc.T(", а в батарее ") + Kwh(r.EnergyWh) + ".";
                r.Checks.Add(new RobotCheck(RobotCheckLevel.Critical, what));
            }
            else if (r.EnergyWh < r.EnergyNeededWh * (1.0 + RobotParts.RecommendedReserve))
                r.Checks.Add(new RobotCheck(RobotCheckLevel.Warning, Loc.T("Запас энергии меньше 20 %: любая ошибка водителя — и робот встанет.")));
            else if (r.EnergyWh > r.EnergyNeededWh * 3.0 && d[RobotSlot.Battery] > 0)
                r.Checks.Add(new RobotCheck(RobotCheckLevel.Warning, Loc.T("Батарея с большим избытком: можно взять меньше и облегчить ракету.")));

            // ---- environment ----
            RobotOption thermal = d.Part(RobotSlot.Thermal);
            if (s.MaxTemperature > thermal.MaxSafeK)
                r.Checks.Add(new RobotCheck(s.MaxTemperature > thermal.MaxSafeK + 150.0 ? RobotCheckLevel.Critical : RobotCheckLevel.Warning,
                    Loc.T("Перегрев: до ") + C(s.MaxTemperature) + Loc.T(", корпус держит до ") + C(thermal.MaxSafeK) + "."));
            if (s.MinTemperature > 0.0 && s.MinTemperature < thermal.MinSafeK)
                r.Checks.Add(new RobotCheck(s.MinTemperature < thermal.MinSafeK - 40.0 ? RobotCheckLevel.Critical : RobotCheckLevel.Warning,
                    Loc.T("Переохлаждение: до ") + C(s.MinTemperature) + Loc.T(", корпус держит от ") + C(thermal.MinSafeK) + "."));

            int rad = d.Part(RobotSlot.Radiation).Level;
            if (s.Radiation > rad)
                r.Checks.Add(new RobotCheck(s.Radiation - rad >= 2 ? RobotCheckLevel.Critical : RobotCheckLevel.Warning,
                    Loc.T("Радиация ") + s.Radiation + Loc.T("-го уровня, защита только ") + rad + Loc.T("-го: камера и электроника будут разрушаться.")));
            else if (rad > s.Radiation)
                r.Checks.Add(new RobotCheck(RobotCheckLevel.Warning, Loc.T("Радиационная защита сильнее, чем нужно: лишняя масса.")));

            int acid = d.Part(RobotSlot.Coating).Level;
            if (s.Acid > acid)
                r.Checks.Add(new RobotCheck(s.Acid - acid >= 2 ? RobotCheckLevel.Critical : RobotCheckLevel.Warning,
                    Loc.T("Кислотная среда ") + s.Acid + Loc.T("-го уровня, покрытие держит ") + acid + Loc.T("-й: корпус разъест.")));

            if (s.Surface)
            {
                double safe = d.Part(RobotSlot.Hull).SafePressure;
                if (s.Pressure > safe)
                    r.Checks.Add(new RobotCheck(s.Pressure > safe * 1.5 ? RobotCheckLevel.Critical : RobotCheckLevel.Warning,
                        Loc.T("Давление ") + Atm(s.Pressure) + Loc.T(", корпус рассчитан на ") + Atm(safe) + "."));
                else if (d[RobotSlot.Hull] > 0 && s.Pressure < RobotParts.Option(RobotSlot.Hull, d[RobotSlot.Hull] - 1).SafePressure)
                    r.Checks.Add(new RobotCheck(RobotCheckLevel.Warning, Loc.T("Корпус прочнее, чем нужно: давление всего ") + Atm(s.Pressure) + "."));
            }

            if (r.Checks.Count == 0)
                r.Checks.Add(new RobotCheck(RobotCheckLevel.Ok, Loc.T("Робот подходит под миссию.")));
            return r;
        }
    }
}
