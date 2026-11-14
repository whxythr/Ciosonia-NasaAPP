using SpaceSim.Localization;
using System.Collections.Generic;
using SpaceSim.Economy;
using SpaceSim.Vehicles;

namespace SpaceSim.Runtime
{
    // Game mode in the hangar: Career refuses to roll out a design with parts the player does not own; Sandbox never checks.
    public sealed partial class HangarUI
    {
        PlayerProfile ModeProfile => session != null ? session.Profile : null;

        /// <summary>The save is a Career (false without a profile: nothing is checked then, as before game modes).</summary>
        bool IsCareer => ModeProfile != null && ModeProfile.IsCareer;

        /// <summary>"КАРЬЕРА" / "ПЕСОЧНИЦА" for the hangar footer.</summary>
        string ModeTag => ModeProfile != null ? GameModeRules.Name(ModeProfile.gameMode).ToUpperInvariant() : "";

        /// <summary>
        /// Career: the design may go to the pad only when every part is owned. Otherwise the status line names the missing parts
        /// and their total price and the shop opens. Always true in Sandbox.
        /// </summary>
        bool CheckOwnedForLaunch()
        {
            if (!IsCareer || body == null || design == null) return true;
            List<string> missing = GameModeRules.UnavailableModules(design, body.OwnsModule);
            if (missing.Count == 0) return true;
            var names = new List<string>();
            foreach (string id in missing)
            {
                ModuleDefinition m = ModuleCatalog.Find(id);
                if (m != null) names.Add(m.name);
            }
            long price = GameModeRules.PriceToBuy(design, body.OwnsModule);
            SetStatus(Loc.T("Карьера: на ракете есть некупленные детали (") + string.Join(", ", names) + Loc.T(") — купите их в магазине за ") +
                      Credits(price) + Loc.T(" или снимите с ракеты."), HudStyle.Danger);
            if (shop != null && !shop.IsOpen) shop.Toggle();
            return false;
        }

        /// <summary>Text on a locked catalog tile: the price, or the closed technology tier.</summary>
        string LockLabel(ModuleDefinition m)
        {
            PlayerProfile p = ModeProfile;
            if (p != null && !p.TierUnlockedFor(m.id)) return Loc.T("УР. ") + ModuleCatalog.TierOf(m.id) + Loc.T(" ЗАКРЫТ");
            return Price(m.price);
        }

        /// <summary>Card line for a part the player may not use: why, and what opens it.</summary>
        string LockNote(ModuleDefinition m)
        {
            PlayerProfile p = ModeProfile;
            if (p == null) return Loc.T(" · не куплено");
            if (!p.TierUnlockedFor(m.id)) return Loc.T(" · уровень ") + ModuleCatalog.TierOf(m.id) + Loc.T(" закрыт");
            return Loc.T(" · не куплено");
        }

        /// <summary>Card hint for a locked part: the full requirement of its closed tier, or that it only needs buying.</summary>
        string LockHint(ModuleDefinition m)
        {
            PlayerProfile p = ModeProfile;
            if (p != null && !p.TierUnlockedFor(m.id)) return TechTree.Requirement(p, ModuleCatalog.TierOf(m.id)) + ".";
            return Loc.T("деталь ещё не куплена.");
        }
    }
}
