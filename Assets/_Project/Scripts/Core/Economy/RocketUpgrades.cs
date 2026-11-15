using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Localization;

namespace SpaceSim.Economy
{
    public enum UpgradeState
    {
        Maxed,
        Available,
        /// <summary>The next level needs a higher technology tier.</summary>
        TierLocked,
        InsufficientFunds
    }

    /// <summary>A rocket-wide improvement bought for credits in levels; it applies to every rocket the player builds.</summary>
    public sealed class UpgradeDefinition
    {
        public UpgradeDefinition(string id, string name, string effectPerLevel, double stepPerLevel, int[] prices)
        {
            Id = id;
            rawName = name;
            rawEffect = effectPerLevel;
            Step = stepPerLevel;
            Prices = prices;
        }

        readonly string rawName, rawEffect;
        public string Id { get; }
        public string Name => Loc.T(rawName);
        public string EffectPerLevel => Loc.T(rawEffect);
        /// <summary>Relative change per level (0.04 = 4 %).</summary>
        public double Step { get; }
        /// <summary>Price of level 1, 2, … [credits].</summary>
        public int[] Prices { get; }
        public int MaxLevel => Prices.Length;
    }

    /// <summary>
    /// Career rocket upgrades: engine efficiency, thrust, lighter structure, cleaner aerodynamics. Bought level by level for
    /// credits; level L needs technology tier L−1 open (<see cref="TechTree"/>). Applied to the compiled rocket
    /// (<see cref="Apply"/>) in the hangar and on the pad. Owned levels are stored as repeated ids in
    /// <see cref="PlayerProfile.rocketUpgrades"/>. Sandbox: every upgrade at its top level.
    /// </summary>
    public static class RocketUpgrades
    {
        public const string Efficiency = "u-isp", Thrust = "u-thrust", LightTanks = "u-dry-mass", Aero = "u-drag";

        static readonly List<UpgradeDefinition> all = new List<UpgradeDefinition>
        {
            new UpgradeDefinition(Efficiency, "Оптимизация сопел", "+3 % удельного импульса двигателей за уровень", 0.03, new[] { 6000, 15000, 35000 }),
            new UpgradeDefinition(Thrust, "Форсирование двигателей", "+5 % тяги двигателей за уровень", 0.05, new[] { 5000, 12000, 30000 }),
            new UpgradeDefinition(LightTanks, "Облегчённые конструкции", "−5 % сухой массы ступеней за уровень", 0.05, new[] { 7000, 18000, 40000 }),
            new UpgradeDefinition(Aero, "Аэродинамика обтекателей", "−6 % аэродинамического сопротивления за уровень", 0.06, new[] { 4000, 10000, 25000 }),
        };

        public static IReadOnlyList<UpgradeDefinition> All => all;

        public static UpgradeDefinition Find(string id) => all.Find(u => u.Id == id);

        public static int LevelOf(PlayerProfile p, string id)
        {
            UpgradeDefinition u = Find(id);
            if (u == null) return 0;
            if (p == null || !p.IsCareer) return u.MaxLevel;
            if (p.rocketUpgrades == null) return 0;
            int n = 0;
            foreach (string s in p.rocketUpgrades) if (s == id) n++;
            return Math.Min(u.MaxLevel, n);
        }

        /// <summary>Technology tier the given level (1-based) needs.</summary>
        public static int TierFor(int level) => Math.Max(0, level - 1);

        public static UpgradeState StateOf(PlayerProfile p, UpgradeDefinition u)
        {
            int level = LevelOf(p, u.Id);
            if (level >= u.MaxLevel) return UpgradeState.Maxed;
            if (!TechTree.IsUnlocked(p, TierFor(level + 1))) return UpgradeState.TierLocked;
            return p.credits >= u.Prices[level] ? UpgradeState.Available : UpgradeState.InsufficientFunds;
        }

        /// <summary>Price of the next level (0 when maxed).</summary>
        public static int NextPrice(PlayerProfile p, UpgradeDefinition u)
        {
            int level = LevelOf(p, u.Id);
            return level >= u.MaxLevel ? 0 : u.Prices[level];
        }

        /// <summary>Buys the next level. Atomic: on failure the profile is unchanged.</summary>
        public static PurchaseResult TryBuy(PlayerProfile p, string id, out string message)
        {
            UpgradeDefinition u = Find(id);
            if (p == null || u == null)
            {
                message = Loc.T("Нет такого улучшения.");
                return PurchaseResult.NotForSale;
            }
            if (!p.IsCareer)
            {
                message = Loc.T("Песочница: все улучшения уже установлены.");
                return PurchaseResult.SandboxMode;
            }
            int level = LevelOf(p, id);
            switch (StateOf(p, u))
            {
                case UpgradeState.Maxed:
                    message = Loc.T("«") + u.Name + Loc.T("» уже на максимальном уровне.");
                    return PurchaseResult.AlreadyOwned;
                case UpgradeState.TierLocked:
                    message = Loc.T("Нужен уровень техники ") + TierFor(level + 1) + ".";
                    return PurchaseResult.RequirementsNotMet;
                case UpgradeState.InsufficientFunds:
                    message = Loc.T("Не хватает кредитов: нужно ") + u.Prices[level] + Loc.T(" кр.");
                    return PurchaseResult.InsufficientFunds;
            }
            int price = u.Prices[level];
            p.credits -= price;
            if (p.rocketUpgrades == null) p.rocketUpgrades = new List<string>();
            p.rocketUpgrades.Add(id);
            p.Record(Loc.T("Улучшение «") + u.Name + Loc.T("», уровень ") + (level + 1), -price, 0.0);
            message = Loc.T("Установлено «") + u.Name + Loc.T("», уровень ") + (level + 1) + Loc.T(" — действует на все новые ракеты.");
            return PurchaseResult.Success;
        }

        /// <summary>Total relative change of an upgrade for this profile (0.09 = +9 %).</summary>
        public static double Bonus(PlayerProfile p, string id)
        {
            UpgradeDefinition u = Find(id);
            return u == null ? 0.0 : u.Step * LevelOf(p, id);
        }

        /// <summary>Applies the profile's upgrades to a compiled rocket configuration (in place). Null profile = nothing.</summary>
        public static void Apply(RocketConfiguration config, PlayerProfile p)
        {
            if (config == null || p == null || config.stages == null) return;
            double isp = 1.0 + Bonus(p, Efficiency);
            double thrust = 1.0 + Bonus(p, Thrust);
            double dry = Math.Max(0.5, 1.0 - Bonus(p, LightTanks));
            double drag = Math.Max(0.5, 1.0 - Bonus(p, Aero));
            foreach (RocketStage s in config.stages)
            {
                if (s == null) continue;
                s.vacuumIsp *= isp;
                s.seaLevelIsp *= isp;
                s.vacuumThrust *= thrust;
                s.dryMass *= dry;
            }
            config.dragCoefficient *= drag;
        }
    }
}
