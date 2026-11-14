using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Vehicles;

namespace SpaceSim.Economy
{
    /// <summary>
    /// How a save plays. Chosen once, when a new game starts (main menu), stored in the profile and never changed afterwards.
    /// The numeric values are what the save file holds: never renumber. Saves without the field (format v1 and older) are Sandbox.
    /// </summary>
    public enum GameMode
    {
        /// <summary>Every module available, nothing is bought, no money checks anywhere.</summary>
        Sandbox = 0,
        /// <summary>Starting budget, each module bought once, technology tiers opened by milestones and research.</summary>
        Career = 1
    }

    /// <summary>One career technology tier: which milestone opens it and what researching it costs.</summary>
    public sealed class TechTier
    {
        public TechTier(int index, string name, string requiredMilestone, double researchCost, double earlyResearchCost)
        {
            Index = index;
            rawName = name;
            RequiredMilestone = requiredMilestone;
            ResearchCost = researchCost;
            EarlyResearchCost = earlyResearchCost;
        }

        readonly string rawName;
        public int Index { get; }
        public string Name => Loc.T(rawName);
        /// <summary>Milestone after which the tier can be researched for <see cref="ResearchCost"/>; null for tier 0.</summary>
        public string RequiredMilestone { get; }
        /// <summary>Research points to open the tier once its milestone is achieved.</summary>
        public double ResearchCost { get; }
        /// <summary>Research points to open the tier before its milestone (independent research): several times dearer.</summary>
        public double EarlyResearchCost { get; }
    }

    public enum TechTierState
    {
        Unlocked,
        /// <summary>The previous tier is closed: tiers open in order.</summary>
        PreviousTierLocked,
        /// <summary>Can be researched now (enough research points).</summary>
        Researchable,
        /// <summary>Not enough research points yet.</summary>
        NotEnoughResearch
    }

    /// <summary>
    /// Career technology progression. Tier 0 is open from the start. Tier N (N ≥ 1) opens in order, for research points: the price
    /// is <see cref="TechTier.ResearchCost"/> once its milestone is achieved (equal to that milestone's research reward, so a real
    /// flight always pays for the next tier) or <see cref="TechTier.EarlyResearchCost"/> without it (extra science from samples and
    /// instruments lets a player skip ahead). Modules of a tier can be bought only after the tier is open.
    /// Sandbox: every tier counts as open.
    /// </summary>
    public static class TechTree
    {
        public const int MaxTier = ModuleCatalog.MaxTechTier;

        static readonly TechTier[] tiers =
        {
            new TechTier(0, "Основы ракетостроения", null, 0, 0),
            new TechTier(1, "Суборбитальные полёты", ResearchMilestones.ReachSpace, 10, 40),
            new TechTier(2, "Орбитальная техника", ResearchMilestones.FirstOrbit, 30, 120),
            new TechTier(3, "Межпланетные полёты", ResearchMilestones.EscapeHome, 50, 200),
            new TechTier(4, "Дальний космос", ResearchMilestones.Interplanetary, 100, 400),
        };

        public static IReadOnlyList<TechTier> Tiers => tiers;

        public static TechTier Get(int index) => tiers[Math.Max(0, Math.Min(MaxTier, index))];

        public static bool IsUnlocked(PlayerProfile profile, int tier)
        {
            if (tier <= 0 || profile.gameMode != GameMode.Career) return true;
            return profile.unlockedTiers != null && profile.unlockedTiers.Contains(tier);
        }

        /// <summary>Highest open tier (tiers open in order).</summary>
        public static int HighestUnlocked(PlayerProfile profile)
        {
            int t = 0;
            while (t < MaxTier && IsUnlocked(profile, t + 1)) t++;
            return t;
        }

        /// <summary>Research points the tier costs for this profile right now (depends on its milestone).</summary>
        public static double CostFor(PlayerProfile profile, int tier)
        {
            TechTier t = Get(tier);
            if (t.RequiredMilestone == null) return 0.0;
            return profile.HasMilestone(t.RequiredMilestone) ? t.ResearchCost : t.EarlyResearchCost;
        }

        public static TechTierState StateOf(PlayerProfile profile, int tier)
        {
            if (IsUnlocked(profile, tier)) return TechTierState.Unlocked;
            if (!IsUnlocked(profile, tier - 1)) return TechTierState.PreviousTierLocked;
            return profile.researchPoints + 1e-9 >= CostFor(profile, tier) ? TechTierState.Researchable : TechTierState.NotEnoughResearch;
        }

        /// <summary>What opening the tier needs, for the shop and the hangar ("Уровень 2 «…»: нужно «Первая орбита» и 30 очк.").</summary>
        public static string Requirement(PlayerProfile profile, int tier)
        {
            TechTier t = Get(tier);
            string head = Loc.T("Уровень ") + t.Index + " «" + t.Name + "»";
            if (IsUnlocked(profile, tier)) return head + Loc.T(" открыт");
            MilestoneDefinition m = ResearchMilestones.Find(t.RequiredMilestone);
            string milestone = m != null ? m.Name : t.RequiredMilestone;
            string text = profile.HasMilestone(t.RequiredMilestone)
                ? head + Loc.T(": исследовать за ") + Points(t.ResearchCost)
                : head + Loc.T(": требуется «") + milestone + Loc.T("» и ") + Points(t.ResearchCost) +
                  Loc.T(" (без достижения — ") + Points(t.EarlyResearchCost) + ")";
            if (!IsUnlocked(profile, tier - 1)) text += Loc.T("; сначала — уровень ") + (tier - 1);
            return text;
        }

        /// <summary>Opens a tier for research points. Atomic: on any failure the profile is unchanged.</summary>
        public static PurchaseResult TryUnlock(PlayerProfile profile, int tier, out string message)
        {
            if (profile.gameMode != GameMode.Career)
            {
                message = Loc.T("Песочница: все уровни техники уже открыты.");
                return PurchaseResult.SandboxMode;
            }
            if (tier < 0 || tier > MaxTier)
            {
                message = Loc.T("Нет такого уровня техники.");
                return PurchaseResult.NotForSale;
            }
            TechTier t = Get(tier);
            switch (StateOf(profile, tier))
            {
                case TechTierState.Unlocked:
                    message = Loc.T("Уровень ") + tier + Loc.T(" уже открыт.");
                    return PurchaseResult.AlreadyOwned;
                case TechTierState.PreviousTierLocked:
                    message = Loc.T("Сначала откройте уровень ") + (tier - 1) + ".";
                    return PurchaseResult.RequirementsNotMet;
                case TechTierState.NotEnoughResearch:
                    message = Loc.T("Не хватает очков исследований: нужно ") + Points(CostFor(profile, tier)) + Loc.T(", есть ") + Points(profile.researchPoints) + ".";
                    return PurchaseResult.InsufficientResearch;
            }
            double cost = CostFor(profile, tier);
            profile.researchPoints -= cost;
            profile.unlockedTiers.Add(tier);
            profile.Record(Loc.T("Исследован уровень ") + tier + " «" + t.Name + "»", 0, -cost);
            message = Loc.T("Открыт уровень ") + tier + " «" + t.Name + Loc.T("» за ") + Points(cost) + Loc.T(" — его модули можно покупать.");
            return PurchaseResult.Success;
        }

        static string Points(double p) => Math.Round(p).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + Loc.T(" очк.");
    }

    /// <summary>Rules that depend on the game mode (shared by the hangar, the shop and the tests).</summary>
    public static class GameModeRules
    {
        /// <summary>Starting budget of a new career [credits].</summary>
        public const int CareerStartingCredits = 15000;

        public static string Name(GameMode mode) => mode == GameMode.Career ? Loc.T("Карьера") : Loc.T("Песочница");

        public static string Description(GameMode mode) => mode == GameMode.Career
            ? Loc.T("Стартовый бюджет, каждый модуль покупается один раз, уровни техники открываются достижениями и очками исследований.")
            : Loc.T("Все модули доступны сразу и бесплатно, без покупок и проверок бюджета.");

        /// <summary>
        /// Distinct ids of the modules of a design that the player may not use (not owned / tier closed), in design order.
        /// Empty in Sandbox. The hangar refuses to roll such a design out to the pad.
        /// </summary>
        public static List<string> UnavailableModules(RocketBlueprint design, Func<string, bool> owns)
        {
            var result = new List<string>();
            if (design == null || owns == null) return result;
            void Check(string id)
            {
                if (string.IsNullOrEmpty(id) || result.Contains(id) || ModuleCatalog.Find(id) == null) return;
                if (!owns(id)) result.Add(id);
            }
            if (design.stages != null)
                foreach (StageBlueprint s in design.stages)
                {
                    if (s == null) continue;
                    Check(s.engineId);
                    if (s.tanks != null) foreach (TankLoad t in s.tanks) if (t != null) Check(t.moduleId);
                    if (s.attachments != null) foreach (string a in s.attachments) Check(a);
                }
            if (design.payloadModules != null) foreach (string p in design.payloadModules) Check(p);
            return result;
        }

        /// <summary>Total price of the modules still to buy for a design (each module once).</summary>
        public static long PriceToBuy(RocketBlueprint design, Func<string, bool> owns)
        {
            long sum = 0;
            foreach (string id in UnavailableModules(design, owns))
            {
                ModuleDefinition m = ModuleCatalog.Find(id);
                if (m != null) sum += m.price;
            }
            return sum;
        }
    }
}
