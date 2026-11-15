using System;
using System.Collections.Generic;
using SpaceSim.Exploration;
using SpaceSim.Localization;

namespace SpaceSim.Economy
{
    public enum TalentBranch
    {
        Protection = 0,
        Mobility = 1,
        Science = 2
    }

    public enum TalentState
    {
        /// <summary>All ranks learned.</summary>
        Maxed,
        /// <summary>A rank can be learned now.</summary>
        Learnable,
        /// <summary>The previous talent of the branch has no rank yet.</summary>
        PreviousLocked,
        /// <summary>No free talent points.</summary>
        NoPoints
    }

    /// <summary>One talent of the robot: a branch, its place in the branch (0 = root) and the effect of each rank.</summary>
    public sealed class TalentDefinition
    {
        public TalentDefinition(string id, TalentBranch branch, int order, string name, string effectPerRank)
        {
            Id = id;
            Branch = branch;
            Order = order;
            rawName = name;
            rawEffect = effectPerRank;
        }

        readonly string rawName, rawEffect;
        public string Id { get; }
        public TalentBranch Branch { get; }
        /// <summary>Position in the branch: talent N needs at least one rank of talent N−1.</summary>
        public int Order { get; }
        public string Name => Loc.T(rawName);
        /// <summary>What one rank gives ("−20 % износа корпуса за ранг").</summary>
        public string EffectPerRank => Loc.T(rawEffect);
    }

    /// <summary>
    /// The surface robot's talent tree: three branches of three talents, three ranks each. Points come from career contracts
    /// (<see cref="ContractDefinition.TalentPoints"/>); one point buys one rank. A talent opens once the previous talent of its
    /// branch has a rank. Learned ranks are stored as repeated ids in <see cref="PlayerProfile.learnedTalents"/>.
    /// Sandbox: every talent at its top rank, nothing to learn.
    /// </summary>
    public static class RobotTalents
    {
        public const int MaxRank = 3;

        public const string Armor = "t-armor", Thermal = "t-thermal", Chemical = "t-chemical";
        public const string Drives = "t-drives", Jets = "t-jets", Shocks = "t-shocks";
        public const string Sampling = "t-sampling", Analysis = "t-analysis", Reports = "t-reports";

        static readonly List<TalentDefinition> all = new List<TalentDefinition>
        {
            new TalentDefinition(Armor, TalentBranch.Protection, 0, "Усиленный корпус", "−20 % износа корпуса от давления и ударов за ранг"),
            new TalentDefinition(Thermal, TalentBranch.Protection, 1, "Терморегуляция", "−20 % износа от жары и холода за ранг"),
            new TalentDefinition(Chemical, TalentBranch.Protection, 2, "Химзащита", "−20 % износа от кислот и радиации за ранг"),
            new TalentDefinition(Drives, TalentBranch.Mobility, 0, "Мощные приводы", "+10 % скорости ходьбы и бега за ранг"),
            new TalentDefinition(Jets, TalentBranch.Mobility, 1, "Прыжковые ускорители", "+15 % скорости прыжка за ранг"),
            new TalentDefinition(Shocks, TalentBranch.Mobility, 2, "Амортизаторы", "+1 м/с безопасной скорости удара за ранг"),
            new TalentDefinition(Sampling, TalentBranch.Science, 0, "Аккуратный отбор", "+15 % к цене образцов за ранг"),
            new TalentDefinition(Analysis, TalentBranch.Science, 1, "Полевой анализ", "+20 % к научной ценности образцов за ранг"),
            new TalentDefinition(Reports, TalentBranch.Science, 2, "Отчётность", "+10 % кредитов за контракты за ранг"),
        };

        public static IReadOnlyList<TalentDefinition> All => all;

        public static TalentDefinition Find(string id) => all.Find(t => t.Id == id);

        public static string BranchName(TalentBranch b)
        {
            switch (b)
            {
                case TalentBranch.Protection: return Loc.T("Защита");
                case TalentBranch.Mobility: return Loc.T("Мобильность");
                default: return Loc.T("Наука");
            }
        }

        public static int RankOf(PlayerProfile p, string id)
        {
            if (p == null || !p.IsCareer) return MaxRank;
            if (p.learnedTalents == null) return 0;
            int n = 0;
            foreach (string t in p.learnedTalents) if (t == id) n++;
            return Math.Min(MaxRank, n);
        }

        /// <summary>Talent points not spent yet.</summary>
        public static int FreePoints(PlayerProfile p)
        {
            if (p == null || !p.IsCareer) return 0;
            return Math.Max(0, p.talentPointsEarned - (p.learnedTalents != null ? p.learnedTalents.Count : 0));
        }

        static TalentDefinition Previous(TalentDefinition t) =>
            t.Order == 0 ? null : all.Find(o => o.Branch == t.Branch && o.Order == t.Order - 1);

        public static TalentState StateOf(PlayerProfile p, TalentDefinition t)
        {
            if (RankOf(p, t.Id) >= MaxRank) return TalentState.Maxed;
            TalentDefinition prev = Previous(t);
            if (prev != null && RankOf(p, prev.Id) == 0) return TalentState.PreviousLocked;
            return FreePoints(p) > 0 ? TalentState.Learnable : TalentState.NoPoints;
        }

        /// <summary>Learns one rank for one talent point. Atomic: on failure the profile is unchanged.</summary>
        public static bool TryLearn(PlayerProfile p, string id, out string message)
        {
            TalentDefinition t = Find(id);
            if (p == null || t == null)
            {
                message = Loc.T("Нет такого таланта.");
                return false;
            }
            if (!p.IsCareer)
            {
                message = Loc.T("Песочница: все таланты робота уже изучены.");
                return false;
            }
            switch (StateOf(p, t))
            {
                case TalentState.Maxed:
                    message = Loc.T("Талант «") + t.Name + Loc.T("» уже изучен полностью.");
                    return false;
                case TalentState.PreviousLocked:
                    message = Loc.T("Сначала изучите «") + Previous(t).Name + "».";
                    return false;
                case TalentState.NoPoints:
                    message = Loc.T("Нет свободных очков талантов — выполняйте контракты.");
                    return false;
            }
            if (p.learnedTalents == null) p.learnedTalents = new List<string>();
            p.learnedTalents.Add(t.Id);
            message = Loc.T("Робот изучил «") + t.Name + Loc.T("», ранг ") + RankOf(p, t.Id) + ".";
            return true;
        }

        // ───────────── effects ─────────────

        static double Reduction(PlayerProfile p, string id) => Math.Max(0.2, 1.0 - 0.2 * RankOf(p, id));

        public static double SpeedFactor(PlayerProfile p) => 1.0 + 0.10 * RankOf(p, Drives);
        public static double JumpFactor(PlayerProfile p) => 1.0 + 0.15 * RankOf(p, Jets);
        public static double SamplePriceFactor(PlayerProfile p) => 1.0 + 0.15 * RankOf(p, Sampling);
        public static double SampleScienceFactor(PlayerProfile p) => 1.0 + 0.20 * RankOf(p, Analysis);
        /// <summary>Contracts only exist in a career, so Sandbox needs no bonus here.</summary>
        public static double ContractRewardFactor(PlayerProfile p) => p != null && p.IsCareer ? 1.0 + 0.10 * RankOf(p, Reports) : 1.0;

        /// <summary>
        /// Sets the wear rates the talents change (relative to <see cref="RobotDamageRates.Default"/>); other fields are left alone,
        /// so calling it again (a new rocket, a talent learned) never compounds.
        /// </summary>
        public static void Configure(RobotDamageRates rates, PlayerProfile p)
        {
            if (rates == null) return;
            RobotDamageRates d = RobotDamageRates.Default;
            double armor = Reduction(p, Armor), thermal = Reduction(p, Thermal), chemical = Reduction(p, Chemical);
            rates.integrityPerPascalSecond = d.integrityPerPascalSecond * armor;
            rates.integrityPerImpactSpeed = d.integrityPerImpactSpeed * armor;
            rates.thermalPerKelvinSecond = d.thermalPerKelvinSecond * thermal;
            rates.chemicalPerLevel = Scaled(d.chemicalPerLevel, chemical);
            rates.visorRadiationPerLevel = Scaled(d.visorRadiationPerLevel, chemical);
            rates.safeImpactSpeed = d.safeImpactSpeed + RankOf(p, Shocks);
        }

        static double[] Scaled(double[] source, double factor)
        {
            var r = new double[source.Length];
            for (int i = 0; i < r.Length; i++) r[i] = source[i] * factor;
            return r;
        }

        /// <summary>A sample just taken: the science branch raises its price and research value.</summary>
        public static void ApplyToSample(SampleRecord s, PlayerProfile p)
        {
            if (s == null) return;
            s.basePrice = (int)Math.Round(s.basePrice * SamplePriceFactor(p));
            s.scientificValue *= SampleScienceFactor(p);
        }
    }
}
