using SpaceSim.Localization;
using System.Collections.Generic;
using SpaceSim.Vehicles;

namespace SpaceSim.Economy
{
    public enum ShopItemState
    {
        Owned,
        Available,
        /// <summary>Requirement (its technology tier) not met yet.</summary>
        Locked,
        /// <summary>Unlocked but the player cannot afford it.</summary>
        Unaffordable
    }

    public enum PurchaseResult
    {
        Success,
        AlreadyOwned,
        NotForSale,
        InsufficientFunds,
        RequirementsNotMet,
        /// <summary>The profile is read-only (saved by a newer game version).</summary>
        ReadOnlyProfile,
        /// <summary>Sandbox: nothing is bought, everything is already available.</summary>
        SandboxMode,
        /// <summary>Not enough research points (opening a technology tier).</summary>
        InsufficientResearch
    }

    /// <summary>A purchasable module and its unlock requirement (its career technology tier).</summary>
    public sealed class ShopItem
    {
        public ShopItem(ModuleDefinition module, string requiredMilestone, string effect)
        {
            Module = module;
            RequiredMilestone = requiredMilestone;
            Effect = effect;
        }

        public ModuleDefinition Module { get; }
        /// <summary>Milestone of the module's technology tier (null for tier 0).</summary>
        public string RequiredMilestone { get; }
        /// <summary>What buying it changes in the game (hand-written for the classic upgrades, otherwise empty).</summary>
        public string Effect { get; }
        public int Price => Module.price;
        /// <summary>Career technology tier of the module.</summary>
        public int Tier => System.Math.Max(0, Module.techTier);
    }

    /// <summary>
    /// The module shop. Career: every catalog module is sold once (buy-once: it stays in the profile and may be installed on any number
    /// of rockets — the same rule the shop always had), at its catalog price (<see cref="ModuleDefinition.price"/>, data-driven in
    /// <see cref="ModuleCatalog"/>), once its technology tier is open (<see cref="TechTree"/>). Sandbox: everything counts as owned
    /// and nothing can be bought — no money checks.
    /// Every item's effect is real: it changes mass/Δv when installed (compiler), protects against hazards
    /// (<see cref="SpaceSim.Planets.HazardModel"/>) or extends sampling.
    /// </summary>
    public static class Shop
    {
        static List<ShopItem> items;
        static IReadOnlyList<ModuleDefinition> itemsBuiltFrom;

        /// <summary>Every catalog module, in catalog order (rebuilt when the catalog is rebuilt, e.g. after a language switch).</summary>
        public static IReadOnlyList<ShopItem> Items
        {
            get
            {
                IReadOnlyList<ModuleDefinition> all = ModuleCatalog.All;
                if (items == null || !ReferenceEquals(itemsBuiltFrom, all))
                {
                    items = Build(all);
                    itemsBuiltFrom = all;
                }
                return items;
            }
        }

        static List<ShopItem> Build(IReadOnlyList<ModuleDefinition> all)
        {
            // Hand-written effects of the classic upgrades (the shop before game modes).
            var effects = new Dictionary<string, string>
            {
                { ModuleCatalog.TankK140, Loc.T("Больше топлива в ступени → выше идеальный Δv; но и стартовая масса больше (ниже тяга/вес).") },
                { ModuleCatalog.EngineSokolM, Loc.T("Больше тяги и Isp первой ступени: выше тяга/вес и Δv.") },
                { ModuleCatalog.RadiationShield, Loc.T("Защита от радиации уровня 1: оборудование не деградирует у Квариса и Моры I; у Теонана — медленнее.") },
                { ModuleCatalog.RadiationShieldHeavy, Loc.T("Защита от радиации уровня 2: безопасная работа даже у Теонана, ближайшей к звезде планеты.") },
                { ModuleCatalog.AcidCoating, Loc.T("Кислотостойкость уровня 1: оборудование выдерживает сернокислотную атмосферу Гермеус Моры.") },
                { ModuleCatalog.CollectorAdvanced, Loc.T("Пробоотборник ПБ-2: 30 кг, жидкости, кислоты и редкие материалы.") },
            };
            var list = new List<ShopItem>(all.Count);
            foreach (ModuleDefinition m in all)
            {
                string milestone = TechTree.Get(System.Math.Max(0, m.techTier)).RequiredMilestone;
                list.Add(new ShopItem(m, milestone, effects.TryGetValue(m.id, out string e) ? e : ""));
            }
            return list;
        }

        public static ShopItem Find(string moduleId)
        {
            foreach (var i in Items)
                if (i.Module.id == moduleId) return i;
            return null;
        }

        /// <summary>Items of one technology tier, in catalog order.</summary>
        public static List<ShopItem> InTier(int tier)
        {
            var list = new List<ShopItem>();
            foreach (ShopItem i in Items)
                if (i.Tier == tier) list.Add(i);
            return list;
        }

        public static ShopItemState StateOf(PlayerProfile profile, ShopItem item)
        {
            if (profile.IsSandbox || profile.ownedModules.Contains(item.Module.id)) return ShopItemState.Owned;
            if (!TechTree.IsUnlocked(profile, item.Tier)) return ShopItemState.Locked;
            return profile.credits >= item.Price ? ShopItemState.Available : ShopItemState.Unaffordable;
        }

        /// <summary>Buys a module (Career). Atomic: on any failure the profile is unchanged.</summary>
        public static PurchaseResult TryPurchase(PlayerProfile profile, string moduleId, out string message)
        {
            ShopItem item = Find(moduleId);
            if (item == null)
            {
                message = Loc.T("Этот модуль не продаётся.");
                return PurchaseResult.NotForSale;
            }
            if (profile.IsSandbox)
            {
                message = Loc.T("Песочница: ") + item.Module.name + Loc.T(" уже доступен — покупать ничего не нужно.");
                return PurchaseResult.SandboxMode;
            }
            switch (StateOf(profile, item))
            {
                case ShopItemState.Owned:
                    message = item.Module.name + Loc.T(" уже куплен.");
                    return PurchaseResult.AlreadyOwned;
                case ShopItemState.Locked:
                    message = item.Module.name + ": " + TechTree.Requirement(profile, item.Tier) + ".";
                    return PurchaseResult.RequirementsNotMet;
                case ShopItemState.Unaffordable:
                    message = Loc.T("Не хватает ") + (item.Price - profile.credits) + Loc.T(" кредитов на ") + item.Module.name + ".";
                    return PurchaseResult.InsufficientFunds;
            }
            profile.credits -= item.Price;
            profile.ownedModules.Add(item.Module.id);
            profile.Record(Loc.T("Покупка: ") + item.Module.name, -item.Price, 0.0);
            message = Loc.T("Куплено: ") + item.Module.name + Loc.T(" за ") + item.Price + Loc.T(" кр. Доступно в ангаре.");
            return PurchaseResult.Success;
        }
    }
}
