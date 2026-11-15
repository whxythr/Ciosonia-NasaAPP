using SpaceSim.Localization;
using System;
using System.Collections.Generic;
using SpaceSim.Economy;
using SpaceSim.Planets;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Shop window (Milestone 3: economy). Three tabs — «Магазин» (upgrades), «Рынок» (sample trade),
    /// «Исследования» (milestones, discoveries, environment hazards).
    /// Every rule lives in the Core: prices, unlock state and purchase checks come from <see cref="Shop"/>, sale prices from
    /// <see cref="Market"/>, milestones from <see cref="ResearchMilestones"/>, hazard rules from <see cref="HazardModel"/>.
    /// All changes go through <see cref="PlayerSession"/> (which applies the Core transaction and saves). This class only
    /// formats results.
    /// </summary>
    /// <remarks>
    /// IMGUI rule followed here (same as <see cref="HangarUI"/>): the displayed data is snapshotted on the Layout event and the
    /// following event pass draws the same snapshot; anything that changes the structure (purchases, sales, tab switches)
    /// is queued with <see cref="Defer"/> and applied after the whole pass.
    /// </remarks>
    [DefaultExecutionOrder(216)]
    public sealed class ShopUI : MonoBehaviour
    {
        const float PreferredWidth = 1000f;
        const float PreferredHeight = 640f;

        enum Tab { Shop, Market, Research, Contracts, Upgrades, Talents }

        static readonly string[] TabNames = { "Магазин", "Рынок", "Исследования", "Контракты", "Улучшения", "Робот" };

        PlayerSession session;
        FlightStateController state;
        StarSystemRuntime starSystem;
        bool open;
        /// <summary>Opened mid-frame (e.g. from a HUD button): draw nothing until a Layout pass has seen the window.</summary>
        bool awaitingLayout;
        Tab tab = Tab.Shop;

        View view;
        bool dirty = true;
        int viewLanguage = -1;
        (int credits, double research, int owned, int samples, int milestones, int visited, bool readOnly, int tiers, int career) signature;
        /// <summary>Career: technology tier shown in the shop tab.</summary>
        int shopTier;

        string status = "";
        Color statusColor = Color.white;
        Vector2 shopScroll, marketScroll, researchScroll, contractScroll, upgradeScroll, talentScroll;
        float contentWidth;
        readonly List<Action> pending = new List<Action>();
        GUIStyle note, compact, cell;

        public bool IsOpen => open;

        public void Initialize(PlayerSession playerSession, FlightStateController flightState, StarSystemRuntime system)
        {
            if (session != null) session.Changed -= OnProfileChanged;
            session = playerSession;
            state = flightState;
            starSystem = system;
            if (session != null) session.Changed += OnProfileChanged;
            dirty = true;
        }

        void OnDestroy()
        {
            if (session != null) session.Changed -= OnProfileChanged;
        }

        void OnProfileChanged() => dirty = true;

        /// <summary>Opens the shop on the last used tab, or closes it.</summary>
        public void Toggle()
        {
            if (open)
            {
                Close();
                return;
            }
            if (session == null) return;
            pending.Clear();
            status = "";
            open = true;
            awaitingLayout = true;
            dirty = true;
            BuildView();
        }

        void Close()
        {
            open = false;
            pending.Clear();
            GUIUtility.keyboardControl = 0;
        }

        // ---- Transactions (always through PlayerSession, applied between IMGUI passes) -------

        void Defer(Action action)
        {
            if (action != null) pending.Add(action);
        }

        void ApplyPending()
        {
            if (pending.Count == 0) return;
            Action[] actions = pending.ToArray();
            pending.Clear();
            foreach (Action a in actions) a();
            dirty = true;
        }

        void Buy(string moduleId)
        {
            if (session == null || session.Profile == null) return;
            PurchaseResult result = session.Purchase(moduleId, out string message);
            bool ok = result == PurchaseResult.Success;
            if (ok && session.ReadOnly) message += Loc.T(" Профиль только для чтения — покупка не сохранится после выхода из игры.");
            SetStatus(message, ok ? HudStyle.Accent : HudStyle.Warning);
            if (state != null) state.Notify(Loc.T("Магазин: ") + message);
        }

        void UnlockTier(int tier)
        {
            if (session == null || session.Profile == null) return;
            PurchaseResult result = session.UnlockTier(tier, out string message);
            bool ok = result == PurchaseResult.Success;
            SetStatus(message, ok ? HudStyle.Accent : HudStyle.Warning);
            if (ok && state != null) state.Notify(Loc.T("Исследования: ") + message);
        }

        void Sell(string sampleId, string sampleName)
        {
            if (session == null || session.Profile == null) return;
            double researchBefore = session.Profile.researchPoints;
            if (!session.SellSample(sampleId, out int credits))
            {
                SetStatus(Loc.T("Образец «") + sampleName + Loc.T("» не найден — возможно, он уже продан."), HudStyle.Warning);
                return;
            }
            double gained = session.Profile.researchPoints - researchBefore;
            string message = Loc.T("Продан образец «") + sampleName + Loc.T("» за ") + Credits(credits) +
                             (gained > 0.0 ? " · +" + Points(gained) : "") + ".";
            if (session.ReadOnly) message += Loc.T(" Профиль только для чтения — продажа не сохранится после выхода из игры.");
            SetStatus(message, HudStyle.Accent);
            if (state != null) state.Notify(Loc.T("Рынок: ") + message);
        }

        void BuyUpgrade(string upgradeId)
        {
            if (session == null || session.Profile == null) return;
            PurchaseResult result = session.BuyUpgrade(upgradeId, out string message);
            bool ok = result == PurchaseResult.Success;
            SetStatus(message, ok ? HudStyle.Accent : HudStyle.Warning);
            if (ok && state != null) state.Notify(Loc.T("Улучшения: ") + message);
        }

        void LearnTalent(string talentId)
        {
            if (session == null || session.Profile == null) return;
            bool ok = session.LearnTalent(talentId, out string message);
            SetStatus(message, ok ? HudStyle.Accent : HudStyle.Warning);
            if (ok && state != null) state.Notify(Loc.T("Робот: ") + message);
        }

        void SetStatus(string text, Color color)
        {
            status = text;
            statusColor = color;
        }

        // ---- Snapshot of everything shown (rebuilt on Layout when the profile changed) -------

        sealed class ShopRow
        {
            public string Id, Title, Price, StateText, Description, Effect, BuyLabel;
            public Color StateColor;
            public bool CanBuy;
            public readonly List<KeyValuePair<string, string>> Specs = new List<KeyValuePair<string, string>>();
            public readonly List<string> SpecNotes = new List<string>();
        }

        sealed class SampleRow
        {
            public string Id, Name, Category, Planet, Mass, Rarity, Analysis, Value, Price;
            /// <summary>Back on the start planet (only then it can be sold).</summary>
            public bool Delivered;
        }

        sealed class MilestoneRow
        {
            public bool Achieved;
            public string Name, Condition, Rewards, Unlocks;
        }

        sealed class HazardRow
        {
            public string Body, Radiation, Acid, Wear, Protection;
            public bool Dangerous;
        }

        sealed class View
        {
            public bool HasProfile;
            public string Balance = "";
            public bool ReadOnly;
            public string ReadOnlyText = "";
            public readonly List<ShopRow> Shop = new List<ShopRow>();
            public readonly List<SampleRow> Samples = new List<SampleRow>();
            public string SampleSummary = "";
            public string CollectorInfo = "";
            public readonly List<MilestoneRow> Milestones = new List<MilestoneRow>();
            public string Visited = "";
            public string Lifetime = "";
            public readonly List<HazardRow> Hazards = new List<HazardRow>();
            public string HazardRules = "";
            /// <summary>Career (false = Sandbox: nothing to buy).</summary>
            public bool Career;
            public string ModeLine = "";
            public readonly List<TierRow> Tiers = new List<TierRow>();
            /// <summary>Recent income and spending, newest first.</summary>
            public readonly List<KeyValuePair<string, string>> Ledger = new List<KeyValuePair<string, string>>();
            public readonly List<ContractRow> Contracts = new List<ContractRow>();
            public string ContractSummary = "";
            public readonly List<UpgradeRow> Upgrades = new List<UpgradeRow>();
            public readonly List<TalentRow> Talents = new List<TalentRow>();
            public string TalentSummary = "";
        }

        sealed class TierRow
        {
            public int Index;
            public string Tab = "", Title = "", StateText = "", ButtonLabel = "";
            public bool Unlocked, CanResearch;
            public Color StateColor;
            public readonly List<ShopRow> Items = new List<ShopRow>();
        }

        sealed class ContractRow
        {
            public ContractState State;
            public string Title, Description, Reward, Progress, Requires;
        }

        sealed class UpgradeRow
        {
            public string Id, Title, Effect, Level, StateText, ButtonLabel;
            public Color StateColor;
            public bool CanBuy;
        }

        sealed class TalentRow
        {
            public string Id, Title, Effect, StateText;
            public TalentBranch Branch;
            public int Rank;
            public Color StateColor;
            public bool CanLearn;
        }

        bool NeedsRebuild()
        {
            if (view == null || dirty || viewLanguage != Loc.Version) return true;
            PlayerProfile p = session != null ? session.Profile : null;
            return p != null && CurrentSignature(p) != signature;
        }

        (int, double, int, int, int, int, bool, int, int) CurrentSignature(PlayerProfile p) =>
            (p.credits, p.researchPoints, p.ownedModules.Count, p.samples.Count, p.achievedMilestones.Count, p.visitedBodies.Count,
                session.ReadOnly, p.unlockedTiers != null ? p.unlockedTiers.Count : 0,
                (p.completedContracts != null ? p.completedContracts.Count : 0) * 1000000 + (p.rocketUpgrades != null ? p.rocketUpgrades.Count : 0) * 10000 +
                (p.learnedTalents != null ? p.learnedTalents.Count : 0) * 100 + p.talentPointsEarned);

        void BuildView()
        {
            viewLanguage = Loc.Version;
            dirty = false;
            var v = new View();
            view = v;
            PlayerProfile p = session != null ? session.Profile : null;
            if (p == null) return;
            signature = CurrentSignature(p);

            v.HasProfile = true;
            v.Balance = Loc.T("Кредиты: ") + N(p.credits, 0) + Loc.T(" кр · Исследования: ") + Points(p.researchPoints);
            v.ReadOnly = session.ReadOnly;
            if (v.ReadOnly)
                v.ReadOnlyText = Loc.T("Профиль только для чтения: ") + (string.IsNullOrEmpty(session.LoadMessage)
                    ? Loc.T("сохранение создано более новой версией игры — изменения не сохраняются.")
                    : session.LoadMessage);

            v.Career = p.IsCareer;
            v.ModeLine = Loc.T("Режим: ") + GameModeRules.Name(p.gameMode) + " — " + GameModeRules.Description(p.gameMode);
            if (v.Career)
            {
                foreach (TechTier t in TechTree.Tiers)
                {
                    TechTierState ts = TechTree.StateOf(p, t.Index);
                    var tr = new TierRow
                    {
                        Index = t.Index,
                        Unlocked = ts == TechTierState.Unlocked,
                        CanResearch = ts == TechTierState.Researchable,
                        Tab = (ts == TechTierState.Unlocked ? "✓ " : "○ ") + Loc.T("Ур. ") + t.Index,
                        Title = Loc.T("Уровень ") + t.Index + " · " + t.Name,
                        StateText = TechTree.Requirement(p, t.Index),
                        ButtonLabel = Loc.T("Исследовать за ") + Points(TechTree.CostFor(p, t.Index)),
                        StateColor = ts == TechTierState.Unlocked ? HudStyle.Accent
                            : ts == TechTierState.Researchable ? new Color(0.55f, 0.95f, 0.6f) : HudStyle.Warning
                    };
                    foreach (ShopItem item in SpaceSim.Economy.Shop.InTier(t.Index)) tr.Items.Add(BuildShopRow(p, item));
                    v.Tiers.Add(tr);
                }
                if (shopTier < 0 || shopTier > TechTree.MaxTier) shopTier = 0;
            }
            if (p.ledger != null)
                for (int i = p.ledger.Count - 1; i >= 0 && v.Ledger.Count < 12; i--)
                {
                    LedgerEntry e = p.ledger[i];
                    var parts = new List<string>();
                    if (e.credits != 0) parts.Add((e.credits > 0 ? "+" : "−") + Credits(Math.Abs(e.credits)));
                    if (e.research != 0.0) parts.Add((e.research > 0 ? "+" : "−") + Points(Math.Abs(e.research)));
                    v.Ledger.Add(new KeyValuePair<string, string>(e.reason, string.Join(" · ", parts)));
                }
            BuildMarket(v, p);
            BuildResearch(v, p);
            BuildHazards(v);
            BuildCareer(v, p);
        }

        void BuildCareer(View v, PlayerProfile p)
        {
            int done = 0;
            foreach (ContractDefinition c in SpaceSim.Economy.Contracts.All)
            {
                ContractState cs = SpaceSim.Economy.Contracts.StateOf(p, c);
                if (cs == ContractState.Completed) done++;
                ContractDefinition req = c.Requires != null ? SpaceSim.Economy.Contracts.Find(c.Requires) : null;
                v.Contracts.Add(new ContractRow
                {
                    State = cs,
                    Title = c.Title,
                    Description = c.Description,
                    Reward = Loc.T("Награда: +") + Credits(SpaceSim.Economy.Contracts.CreditsFor(p, c)) + " · +" + Points(c.Research) +
                             (c.TalentPoints > 0 ? " · +" + c.TalentPoints + Loc.T(" очк. талантов робота") : ""),
                    Progress = SpaceSim.Economy.Contracts.Progress(p, c),
                    Requires = req != null ? Loc.T("Откроется после «") + req.Title + "»" : ""
                });
            }
            v.ContractSummary = Loc.T("Выполнено контрактов: ") + done + Loc.T(" из ") + SpaceSim.Economy.Contracts.All.Count +
                                Loc.T(". Контракт засчитывается сам, как только условие выполнено настоящим полётом.");

            foreach (UpgradeDefinition u in RocketUpgrades.All)
            {
                int level = RocketUpgrades.LevelOf(p, u.Id);
                UpgradeState us = RocketUpgrades.StateOf(p, u);
                int price = RocketUpgrades.NextPrice(p, u);
                var row = new UpgradeRow
                {
                    Id = u.Id,
                    Title = u.Name,
                    Effect = u.EffectPerLevel,
                    Level = Loc.T("Уровень ") + level + " / " + u.MaxLevel,
                    CanBuy = p.IsCareer && us == UpgradeState.Available,
                    ButtonLabel = us == UpgradeState.Maxed ? Loc.T("Максимум") : Loc.T("Купить за ") + Credits(price)
                };
                switch (us)
                {
                    case UpgradeState.Maxed:
                        row.StateText = Loc.T("Установлен максимальный уровень.");
                        row.StateColor = HudStyle.Accent;
                        break;
                    case UpgradeState.Available:
                        row.StateText = Loc.T("Доступно");
                        row.StateColor = new Color(0.55f, 0.95f, 0.6f);
                        break;
                    case UpgradeState.TierLocked:
                        row.StateText = Loc.T("Нужен уровень техники ") + RocketUpgrades.TierFor(level + 1) + ".";
                        row.StateColor = HudStyle.Warning;
                        break;
                    default:
                        row.StateText = Loc.T("Не хватает ") + N(price - p.credits, 0) + Loc.T(" кр");
                        row.StateColor = HudStyle.Danger;
                        break;
                }
                v.Upgrades.Add(row);
            }

            foreach (TalentDefinition t in RobotTalents.All)
            {
                TalentState ts = RobotTalents.StateOf(p, t);
                var row = new TalentRow
                {
                    Id = t.Id,
                    Branch = t.Branch,
                    Title = t.Name,
                    Effect = t.EffectPerRank,
                    Rank = RobotTalents.RankOf(p, t.Id),
                    CanLearn = p.IsCareer && ts == TalentState.Learnable
                };
                switch (ts)
                {
                    case TalentState.Maxed:
                        row.StateText = Loc.T("Изучен полностью");
                        row.StateColor = HudStyle.Accent;
                        break;
                    case TalentState.Learnable:
                        row.StateText = Loc.T("Можно изучить");
                        row.StateColor = new Color(0.55f, 0.95f, 0.6f);
                        break;
                    case TalentState.PreviousLocked:
                        row.StateText = Loc.T("Сначала предыдущий талант ветки");
                        row.StateColor = HudStyle.Warning;
                        break;
                    default:
                        row.StateText = Loc.T("Нет свободных очков");
                        row.StateColor = HudStyle.Warning;
                        break;
                }
                v.Talents.Add(row);
            }
            v.TalentSummary = p.IsCareer
                ? Loc.T("Свободных очков талантов: ") + RobotTalents.FreePoints(p) + Loc.T(" · получено всего: ") + p.talentPointsEarned +
                  Loc.T(". Очки дают выполненные контракты; одно очко — один ранг.")
                : Loc.T("Песочница: все таланты робота изучены полностью.");
        }

        ShopRow BuildShopRow(PlayerProfile p, ShopItem item)
        {
            ModuleDefinition m = item.Module;
            ShopItemState s = SpaceSim.Economy.Shop.StateOf(p, item);
            var row = new ShopRow
            {
                Id = m.id,
                Title = m.name + " · " + CategoryName(m.category),
                Price = Credits(item.Price),
                Description = m.description,
                Effect = item.Effect,
                CanBuy = s == ShopItemState.Available,
                BuyLabel = Loc.T("Купить за ") + Credits(item.Price)
            };
            switch (s)
            {
                case ShopItemState.Owned:
                    row.StateText = Loc.T("Куплено — модуль доступен в ангаре.");
                    row.StateColor = HudStyle.Accent;
                    row.BuyLabel = Loc.T("Куплено");
                    break;
                case ShopItemState.Available:
                    row.StateText = Loc.T("Доступно");
                    row.StateColor = new Color(0.55f, 0.95f, 0.6f);
                    break;
                case ShopItemState.Locked:
                    row.StateText = Loc.T("Заблокировано: ") + TechTree.Requirement(p, item.Tier);
                    row.StateColor = HudStyle.Warning;
                    break;
                default:
                    row.StateText = Loc.T("Не хватает ") + N(item.Price - p.credits, 0) + Loc.T(" кр");
                    row.StateColor = HudStyle.Danger;
                    break;
            }
            AddSpecs(row, m);
            return row;
        }

        static void AddSpecs(ShopRow row, ModuleDefinition m)
        {
            void Spec(string label, string value) => row.Specs.Add(new KeyValuePair<string, string>(label, value));

            Spec(Loc.T("Масса (сухая)"), Mass(m.dryMass));
            switch (m.category)
            {
                case ModuleCategory.Engine:
                    Spec(Loc.T("Тяга в вакууме"), Force(m.vacuumThrust));
                    Spec(Loc.T("Isp в вакууме"), N(m.vacuumIsp, 0) + Loc.T(" с"));
                    Spec(Loc.T("Isp у поверхности"), N(m.seaLevelIsp, 0) + Loc.T(" с"));
                    Spec(Loc.T("Топливо"), BlueprintCompiler.Name(m.propellant));
                    break;
                case ModuleCategory.FuelTank:
                    Spec(Loc.T("Ёмкость"), Mass(m.propellantCapacity));
                    Spec(Loc.T("Топливо"), BlueprintCompiler.Name(m.propellant));
                    Spec(Loc.T("Масса с топливом"), Mass(m.dryMass + m.propellantCapacity));
                    break;
                case ModuleCategory.Auxiliary when m.IsWrap:
                    Spec(Loc.T("Радиационная защита"), Level(m.radiationShielding));
                    Spec(Loc.T("Кислотостойкость"), Level(m.acidResistance));
                    break;
                case ModuleCategory.Mechanics when m.IsSampleCollector:
                    Spec(Loc.T("Образцы, до"), m.sampleCapacity > 0.0 ? Mass(m.sampleCapacity) : "—");
                    if (m.sampleKinds != SampleKinds.None) row.SpecNotes.Add(Loc.T("Виды образцов: ") + KindList(m.sampleKinds));
                    break;
                default:
                    Spec(Loc.T("Возможности"), m.capabilities == ModuleCapability.None ? "—" : CapabilityList(m.capabilities));
                    if (m.researchMultiplier != 1.0) Spec(Loc.T("Научная ценность"), "×" + N(m.researchMultiplier, 1));
                    if (m.radiationShielding > 0) Spec(Loc.T("Радиационная защита"), Level(m.radiationShielding));
                    if (m.acidResistance > 0) Spec(Loc.T("Кислотостойкость"), Level(m.acidResistance));
                    break;
            }
            if (m.length > 0.0) Spec(Loc.T("Длина"), N(m.length, 1) + Loc.T(" м"));
        }

        void BuildMarket(View v, PlayerProfile p)
        {
            foreach (SampleRecord s in p.samples)
            {
                if (s == null) continue;
                v.Samples.Add(new SampleRow
                {
                    Id = s.id,
                    Name = string.IsNullOrEmpty(s.name) ? s.id : s.name,
                    Category = SampleCategoryName(s.category),
                    Planet = BodyName(s.planetId),
                    Mass = SampleMass(s.mass),
                    Rarity = RarityName(s.rarity),
                    Analysis = s.analysed ? Loc.T("да, ×") + N(Math.Max(1.0, s.analysisMultiplier), 1) : Loc.T("нет"),
                    Value = Points(s.scientificValue),
                    Price = Credits(Market.SellPrice(s)),
                    Delivered = s.delivered
                });
            }
            v.SampleSummary = Loc.T("Образцов: ") + p.samples.Count + Loc.T(" · общая масса ") + SampleMass(p.SampleMass()) +
                              (p.CargoMass() > 0.0 ? Loc.T(" · в ракете ") + SampleMass(p.CargoMass()) + Loc.T(" — продажа после возвращения на Четвёртую планету") : "");

            var collectors = new List<string>();
            foreach (ModuleDefinition m in ModuleCatalog.InCategory(ModuleCategory.Mechanics))
                if (m.IsSampleCollector) collectors.Add(m.name + " — " + KindList(m.sampleKinds) + Loc.T(", до ") + Mass(m.sampleCapacity) +
                               (p.Owns(m.id) ? Loc.T(" (есть)") : Loc.T(" (магазин)")));
            v.CollectorInfo = collectors.Count > 0 ? string.Join("; ", collectors) + "." : "";
        }

        void BuildResearch(View v, PlayerProfile p)
        {
            foreach (MilestoneDefinition m in ResearchMilestones.All)
            {
                var unlocks = new List<string>();
                if (p.IsCareer)
                    foreach (TechTier t in TechTree.Tiers)
                        if (t.RequiredMilestone == m.Id) unlocks.Add(Loc.T("уровень техники ") + t.Index + " «" + t.Name + "»");
                v.Milestones.Add(new MilestoneRow
                {
                    Achieved = p.HasMilestone(m.Id),
                    Name = m.Name,
                    Condition = m.Condition,
                    Rewards = Loc.T("Награда: +") + Credits(m.CreditReward) + " · +" + Points(m.ResearchReward),
                    Unlocks = unlocks.Count > 0 ? Loc.T("Открывает для исследования: ") + string.Join(", ", unlocks) : ""
                });
            }

            var names = new List<string>();
            foreach (string id in p.visitedBodies) names.Add(BodyName(id));
            int total = starSystem != null ? starSystem.Bodies.Count : 0;
            v.Visited = Loc.T("Посещённые тела") + (total > 0 ? " (" + names.Count + Loc.T(" из ") + total + ")" : "") + ": " +
                        (names.Count > 0 ? string.Join(", ", names) : Loc.T("пока нет"));
            v.Lifetime = Loc.T("Всего заработано за игру: ") + N(p.lifetimeEarnings, 0) + Loc.T(" кр");
        }

        void BuildHazards(View v)
        {
            if (starSystem != null)
            {
                foreach (StarSystemRuntime.Body b in starSystem.Bodies)
                {
                    if (b == null || b.Runtime == null || b.Runtime.Definition == null) continue;
                    EnvironmentHazards hz = b.Runtime.Definition.hazards ?? new EnvironmentHazards();
                    // Unprotected wear in the two zones of the Core rule: above dense air (vacuum) and inside it.
                    double vacuumRate = HazardModel.Evaluate(hz, 0.0, 0, 0).DegradationPerSecond;
                    double airRate = HazardModel.Evaluate(hz, 1.0, 0, 0).DegradationPerSecond;
                    var wear = new List<string>();
                    if (vacuumRate > 0.0) wear.Add(Loc.T("вне плотной атмосферы −") + PercentPerMinute(vacuumRate) + " (" + TimeToOffline(vacuumRate) + ")");
                    if (airRate > 0.0) wear.Add(Loc.T("в атмосфере −") + PercentPerMinute(airRate) + " (" + TimeToOffline(airRate) + ")");

                    var protection = new List<string>();
                    if (hz.radiationLevel > 0) protection.Add(CoverText(hz.radiationLevel, true));
                    if (hz.acidLevel > 0) protection.Add(CoverText(hz.acidLevel, false));

                    v.Hazards.Add(new HazardRow
                    {
                        Body = b.Name,
                        Radiation = Level(hz.radiationLevel),
                        Acid = Level(hz.acidLevel),
                        Wear = wear.Count > 0 ? string.Join("; ", wear) : Loc.T("безопасно"),
                        Protection = protection.Count > 0 ? string.Join(" + ", protection) : Loc.T("не нужна"),
                        Dangerous = wear.Count > 0
                    });
                }
            }

            double level1 = HazardModel.Evaluate(new EnvironmentHazards { radiationLevel = 1 }, 0.0, 0, 0).DegradationPerSecond;
            double level2 = HazardModel.Evaluate(new EnvironmentHazards { radiationLevel = 2 }, 0.0, 0, 0).DegradationPerSecond;
            var modules = new List<string>();
            foreach (ModuleDefinition m in ModuleCatalog.InCategory(ModuleCategory.Auxiliary))
            {
                var parts = new List<string>();
                if (m.radiationShielding > 0) parts.Add(Loc.T("радиация уровня ") + m.radiationShielding);
                if (m.acidResistance > 0) parts.Add(Loc.T("кислота уровня ") + m.acidResistance);
                if (parts.Count > 0) modules.Add(ShortName(m) + " = " + string.Join(", ", parts));
            }
            v.HazardRules =
                Loc.T("Радиация действует там, где воздух разрежен (ρ/ρ₀ < ") + N(HazardModel.ThinAirRatio * 100.0, 0) +
                Loc.T(" %) — плотная атмосфера экранирует. Кислота действует внутри атмосферы (ρ/ρ₀ > ") +
                UnitFormatter.Scientific(HazardModel.AcidAirRatio, 1) + Loc.T("); в тонком верхнем слое — обе опасности сразу. ") +
                Loc.T("Каждый незащищённый уровень изнашивает оборудование отсека: 1 уровень ≈ ") + PercentPerMinute(level1) +
                Loc.T(", 2 уровня ≈ ") + PercentPerMinute(level2) + Loc.T(" (время симуляции). При целостности ниже ") +
                N(HazardModel.InstrumentThreshold * 100.0, 0) + Loc.T(" % приборы отсека отключаются. ") +
                (modules.Count > 0 ? Loc.T("Защита: ") + string.Join("; ", modules) + ". " : "") +
                Loc.T("Защита слабее нужного уровня не спасает полностью, но замедляет износ.");
        }

        /// <summary>Lightest protection module that fully covers the level (judged by <see cref="HazardModel"/>), and whether it is owned.</summary>
        string CoverText(int level, bool radiation)
        {
            ModuleDefinition best = null;
            foreach (ModuleDefinition m in ModuleCatalog.All)
            {
                if (radiation ? m.radiationShielding <= 0 : m.acidResistance <= 0) continue;
                HazardExposure x = radiation
                    ? HazardModel.Evaluate(new EnvironmentHazards { radiationLevel = level }, 0.0, m.radiationShielding, 0)
                    : HazardModel.Evaluate(new EnvironmentHazards { acidLevel = level }, 1.0, 0, m.acidResistance);
                bool covers = radiation ? x.RadiationExcess == 0 : x.AcidExcess == 0;
                if (covers && (best == null || m.dryMass < best.dryMass)) best = m;
            }
            if (best == null) return (radiation ? Loc.T("радиация") : Loc.T("кислота")) + Loc.T(": модуля нет");
            bool owned = session != null && session.Owns(best.id);
            return ShortName(best) + (owned ? Loc.T(" (есть)") : Loc.T(" (магазин)"));
        }

        string BodyName(string planetId)
        {
            if (string.IsNullOrEmpty(planetId)) return "—";
            if (starSystem != null)
            {
                int i = starSystem.IndexOf(planetId);
                if (i >= 0) return starSystem.Bodies[i].Name;
            }
            return planetId;
        }

        // ---- Drawing -------------------------------------------------------------------------

        void OnGUI()
        {
            if (!open || session == null || FlightInput.PauseMenuOpen) return;
            Event e = Event.current;
            bool layout = e != null && e.type == EventType.Layout;
            if (awaitingLayout)
            {
                if (!layout) return;
                awaitingLayout = false;
            }
            if (view == null || (layout && NeedsRebuild())) BuildView();
            if (view == null) return;

            GUI.depth = -11; // in front of the HUD (and the hangar): drawn on top and gets input first
            HudStyle.Begin();
            EnsureStyles();

            Vector2 screen = HudStyle.VirtualScreen;
            float w = Mathf.Min(PreferredWidth, screen.x - 20f);
            float h = Mathf.Min(PreferredHeight, screen.y - 20f);
            var r = new Rect((screen.x - w) * 0.5f, (screen.y - h) * 0.5f, w, h);
            HudStyle.WindowFrame(r);

            float innerW = w - 20f, innerH = h - 16f;
            contentWidth = Mathf.Max(300f, innerW - 24f); // minus the vertical scrollbar
            float scrollHeight = Mathf.Max(140f, innerH - (view.ReadOnly ? 170f : 136f));

            GUILayout.BeginArea(new Rect(r.x + 10f, r.y + 8f, innerW, innerH));
            DrawHeader();
            GUILayout.Space(4);
            DrawTabs(innerW);
            GUILayout.Space(6);

            if (!view.HasProfile)
                Colored(Loc.T("Профиль игрока не загружен — магазин недоступен."), note, HudStyle.Danger);
            else
            {
                switch (tab)
                {
                    case Tab.Shop: DrawShop(scrollHeight); break;
                    case Tab.Market: DrawMarket(scrollHeight); break;
                    case Tab.Contracts: DrawContracts(scrollHeight); break;
                    case Tab.Upgrades: DrawUpgrades(scrollHeight); break;
                    case Tab.Talents: DrawTalents(scrollHeight); break;
                    default: DrawResearch(scrollHeight); break;
                }
            }

            GUILayout.FlexibleSpace();
            DrawFooter(innerW);
            GUILayout.EndArea();

            SwallowMouse(r);
            ApplyPending();
        }

        void EnsureStyles()
        {
            if (note != null && compact != null && cell != null) return;
            // White base colour so GUI.color tints give exactly HudStyle.Accent / Warning / Danger.
            note = new GUIStyle(HudStyle.Small) { fontSize = 12, wordWrap = true };
            note.normal.textColor = Color.white;
            compact = new GUIStyle(HudStyle.Small) { wordWrap = false };
            cell = new GUIStyle(HudStyle.Label) { wordWrap = true };
        }

        /// <summary>Clicks and scrolling inside the window must not reach HUD controls underneath.</summary>
        static void SwallowMouse(Rect window)
        {
            Event e = Event.current;
            if (e == null) return;
            EventType t = e.type;
            bool mouse = t == EventType.MouseDown || t == EventType.MouseUp || t == EventType.MouseDrag || t == EventType.ScrollWheel;
            if (mouse && window.Contains(e.mousePosition)) e.Use();
        }

        void DrawHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("МАГАЗИН · экономика и исследования"), HudStyle.WindowTitle);
            GUILayout.FlexibleSpace();
            Colored(view.Balance, HudStyle.Title, Color.white);
            GUILayout.EndHorizontal();
            GUILayout.Label(Loc.T("Цены, условия открытия и сделки задаёт ядро игры; каждая покупка и продажа сразу сохраняется в профиле."),
                HudStyle.Small);
            GUILayout.Label(view.ModeLine, HudStyle.Small);
            if (view.ReadOnly) Colored(view.ReadOnlyText, note, HudStyle.Danger);
        }

        void DrawTabs(float innerW)
        {
            float tw = Mathf.Min(160f, (innerW - 16f) / TabNames.Length);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < TabNames.Length; i++)
            {
                var t = (Tab)i;
                bool on = tab == t;
                string label = Loc.T(TabNames[i]);
                if (t == Tab.Market && view.Samples.Count > 0) label += " (" + view.Samples.Count + ")";
                if (t == Tab.Talents && view.Career && session.Profile != null && RobotTalents.FreePoints(session.Profile) > 0)
                    label += " (+" + RobotTalents.FreePoints(session.Profile) + ")";
                if (GUILayout.Toggle(on, label, HudStyle.Tab, GUILayout.Width(tw), GUILayout.Height(26)) && !on)
                    Defer(() => tab = t);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        // ---- Tab 1: shop ---------------------------------------------------------------------

        void DrawShop(float height)
        {
            float panelInner = contentWidth - 24f;
            float specsWidth = Mathf.Min(360f, panelInner * 0.42f);
            float textWidth = Mathf.Max(120f, panelInner - specsWidth - 12f);

            if (!view.Career)
            {
                GUILayout.BeginVertical(HudStyle.Panel, GUILayout.Width(contentWidth));
                Colored(Loc.T("ПЕСОЧНИЦА"), HudStyle.Title, HudStyle.Accent);
                GUILayout.Label(Loc.T("Все модули каталога уже доступны в ангаре — покупать ничего не нужно, бюджет нигде не проверяется. ") +
                                Loc.T("Рынок образцов и исследования работают как обычно. Режим выбирается при создании новой игры в главном меню."),
                    note);
                GUILayout.EndVertical();
                return;
            }

            // Career: technology tiers as sub-tabs; the selected tier's status (and research button) above its modules.
            float tierTab = Mathf.Min(150f, (contentWidth - 16f) / Mathf.Max(1, view.Tiers.Count));
            GUILayout.BeginHorizontal();
            foreach (TierRow t in view.Tiers)
            {
                int index = t.Index;
                bool on = shopTier == index;
                if (GUILayout.Toggle(on, t.Tab, HudStyle.Tab, GUILayout.Width(tierTab), GUILayout.Height(22)) && !on) Defer(() => shopTier = index);
            }
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            TierRow tier = shopTier >= 0 && shopTier < view.Tiers.Count ? view.Tiers[shopTier] : null;
            if (tier == null) return;
            GUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
            GUILayout.BeginVertical();
            GUILayout.Label(tier.Title, HudStyle.Title);
            Colored(tier.StateText, note, tier.StateColor);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (!tier.Unlocked)
            {
                int tierIndex = tier.Index;
                if (Button(tier.ButtonLabel, tier.CanResearch, GUILayout.Width(220), GUILayout.Height(26))) Defer(() => UnlockTier(tierIndex));
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            shopScroll = GUILayout.BeginScrollView(shopScroll, GUILayout.Height(Mathf.Max(80f, height - 70f)));
            if (tier.Items.Count == 0) GUILayout.Label(Loc.T("В магазине пока ничего нет."), HudStyle.Small);
            foreach (ShopRow row in tier.Items)
            {
                string id = row.Id;
                GUILayout.BeginVertical(HudStyle.Panel, GUILayout.Width(contentWidth));
                GUILayout.BeginHorizontal();
                GUILayout.Label(row.Title, HudStyle.Title);
                GUILayout.FlexibleSpace();
                GUILayout.Label(row.Price, HudStyle.Value, GUILayout.Width(140));
                GUILayout.EndHorizontal();
                Colored(row.StateText, note, row.StateColor);
                GUILayout.Space(2);

                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical(GUILayout.Width(specsWidth));
                foreach (KeyValuePair<string, string> spec in row.Specs) HudStyle.Row(spec.Key, spec.Value, 170f);
                foreach (string specNote in row.SpecNotes) GUILayout.Label(specNote, note);
                GUILayout.EndVertical();
                GUILayout.Space(12);
                GUILayout.BeginVertical(GUILayout.Width(textWidth));
                GUILayout.Label(row.Description, HudStyle.Small);
                GUILayout.Space(4);
                if (!string.IsNullOrEmpty(row.Effect)) Colored(Loc.T("Что даёт в игре: ") + row.Effect, note, HudStyle.Accent);
                GUILayout.EndVertical();
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.BeginHorizontal();
                if (Button(row.BuyLabel, row.CanBuy, GUILayout.Width(240), GUILayout.Height(26))) Defer(() => Buy(id));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }
            GUILayout.Space(4);
            GUILayout.Label(Loc.T("Купленные модули появляются в ангаре (кнопка АНГАР / B на стартовой площадке) — там их ставят на ракету. ") +
                            Loc.T("Покупка разовая: модуль остаётся в профиле и ставится на любое число ракет. Уровни техники открываются очками исследований после достижений."),
                HudStyle.Small);
            GUILayout.EndScrollView();
        }

        // ---- Tab 2: market -------------------------------------------------------------------

        static readonly float[] MarketColumns = { 0.20f, 0.15f, 0.13f, 0.08f, 0.11f, 0.10f, 0.09f, 0.14f };
        static readonly string[] MarketHeaders = { "Образец", "Категория", "Планета", "Масса", "Редкость", "Анализ", "Ценность", "Цена" };

        void DrawMarket(float height)
        {
            const float buttonWidth = 90f;
            float tableWidth = contentWidth - buttonWidth - 50f; // slack for the labels' default margins

            GUILayout.Label(Loc.T("Продажа образцов · ") + view.SampleSummary, HudStyle.Title);
            GUILayout.Label(Loc.T("Цена растёт с редкостью и с анализом на месте (спектрометр). При продаже начисляются и очки исследований."),
                HudStyle.Small);
            GUILayout.Space(4);

            marketScroll = GUILayout.BeginScrollView(marketScroll, GUILayout.Height(height - 44f));
            if (view.Samples.Count == 0)
            {
                GUILayout.BeginVertical(HudStyle.Panel, GUILayout.Width(contentWidth));
                GUILayout.Label(Loc.T("Образцов пока нет"), HudStyle.Title);
                GUILayout.Label(Loc.T("Образцы собирают на поверхности других планет и лун пробоотборником — это будет в этапе «Посадка и ") +
                                Loc.T("исследование» (следующие milestone). Вид пробоотборника ограничивает, что можно взять и сколько: ") +
                                view.CollectorInfo, note);
                GUILayout.Space(4);
                GUILayout.Label(Loc.T("Собранные образцы появятся здесь: их можно продать за кредиты, на которые покупаются улучшения."),
                    HudStyle.Small);
                GUILayout.EndVertical();
            }
            else
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < MarketHeaders.Length; c++)
                    GUILayout.Label(Loc.T(MarketHeaders[c]), HudStyle.Title, GUILayout.Width(tableWidth * MarketColumns[c]));
                GUILayout.EndHorizontal();

                foreach (SampleRow s in view.Samples)
                {
                    string id = s.Id, name = s.Name;
                    GUILayout.BeginHorizontal(GUILayout.Width(contentWidth));
                    Cell(s.Name, 0, tableWidth);
                    Cell(s.Category, 1, tableWidth);
                    Cell(s.Planet, 2, tableWidth);
                    Cell(s.Mass, 3, tableWidth);
                    Cell(s.Rarity, 4, tableWidth);
                    Cell(s.Analysis, 5, tableWidth);
                    Cell(s.Value, 6, tableWidth);
                    Cell(s.Price, 7, tableWidth);
                    GUILayout.FlexibleSpace();
                    if (Button(s.Delivered ? Loc.T("Продать") : Loc.T("В ракете"), s.Delivered, GUILayout.Width(buttonWidth))) Defer(() => Sell(id, name));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }

        void Cell(string text, int column, float tableWidth) =>
            GUILayout.Label(text, cell, GUILayout.Width(tableWidth * MarketColumns[column]));

        // ---- Tab 3: research -----------------------------------------------------------------

        static readonly float[] HazardColumns = { 0.17f, 0.11f, 0.11f, 0.36f, 0.25f };
        static readonly string[] HazardHeaders = { "Тело", "Радиация", "Кислота", "Износ без защиты", "Нужна защита" };

        void DrawResearch(float height)
        {
            researchScroll = GUILayout.BeginScrollView(researchScroll, GUILayout.Height(height));

            GUILayout.Label(Loc.T("Исследовательские достижения"), HudStyle.Title);
            GUILayout.Label(Loc.T("Засчитываются только по настоящему полёту (симуляция), без телепортации и тестовых инструментов."),
                HudStyle.Small);
            foreach (MilestoneRow m in view.Milestones)
            {
                GUILayout.BeginVertical(HudStyle.Panel, GUILayout.Width(contentWidth));
                GUILayout.BeginHorizontal();
                Colored((m.Achieved ? "✓ " : "○ ") + m.Name, HudStyle.Title, m.Achieved ? HudStyle.Accent : Color.white);
                GUILayout.FlexibleSpace();
                Colored(m.Achieved ? Loc.T("Достигнуто") : Loc.T("Не достигнуто"), compact, m.Achieved ? HudStyle.Accent : HudStyle.Warning,
                    GUILayout.Width(140));
                GUILayout.EndHorizontal();
                GUILayout.Label((m.Achieved ? Loc.T("Условие было: ") : Loc.T("Условие: ")) + m.Condition, note);
                GUILayout.Label(m.Rewards, HudStyle.Label);
                if (!string.IsNullOrEmpty(m.Unlocks)) GUILayout.Label(m.Unlocks, HudStyle.Small);
                GUILayout.EndVertical();
                GUILayout.Space(3);
            }

            GUILayout.Space(6);
            GUILayout.Label(Loc.T("Открытия"), HudStyle.Title);
            GUILayout.Label(view.Visited, note);
            GUILayout.Label(view.Lifetime, HudStyle.Small);
            if (view.Ledger.Count > 0)
            {
                GUILayout.Space(6);
                GUILayout.Label(Loc.T("Финансы · последние операции"), HudStyle.Title);
                foreach (KeyValuePair<string, string> line in view.Ledger) HudStyle.Row(line.Key, line.Value, Mathf.Max(200f, contentWidth - 260f));
            }

            GUILayout.Space(8);
            GUILayout.Label(Loc.T("Опасности окружения"), HudStyle.Title);
            GUILayout.Label(view.HazardRules, HudStyle.Small);
            GUILayout.Space(4);
            if (view.Hazards.Count == 0)
            {
                GUILayout.Label(Loc.T("Звёздная система недоступна — таблица опасностей появится после загрузки системы."), HudStyle.Small);
            }
            else
            {
                float tableWidth = contentWidth - 40f; // slack for the labels' default margins
                GUILayout.BeginHorizontal();
                for (int c = 0; c < HazardHeaders.Length; c++)
                    GUILayout.Label(Loc.T(HazardHeaders[c]), HudStyle.Title, GUILayout.Width(tableWidth * HazardColumns[c]));
                GUILayout.EndHorizontal();
                foreach (HazardRow row in view.Hazards)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label(row.Body, cell, GUILayout.Width(tableWidth * HazardColumns[0]));
                    GUILayout.Label(row.Radiation, cell, GUILayout.Width(tableWidth * HazardColumns[1]));
                    GUILayout.Label(row.Acid, cell, GUILayout.Width(tableWidth * HazardColumns[2]));
                    Colored(row.Wear, note, row.Dangerous ? HudStyle.Warning : HudStyle.Accent, GUILayout.Width(tableWidth * HazardColumns[3]));
                    GUILayout.Label(row.Protection, cell, GUILayout.Width(tableWidth * HazardColumns[4]));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
        }

        // ---- Tab 4: contracts ----------------------------------------------------------------

        void SandboxNote(string text)
        {
            GUILayout.BeginVertical(HudStyle.Panel, GUILayout.Width(contentWidth));
            Colored(Loc.T("ПЕСОЧНИЦА"), HudStyle.Title, HudStyle.Accent);
            GUILayout.Label(text, note);
            GUILayout.EndVertical();
        }

        void DrawContracts(float height)
        {
            if (!view.Career)
            {
                SandboxNote(Loc.T("В песочнице нет контрактов: летайте куда угодно без цели. Контракты, кредиты и таланты робота — в режиме «Карьера»."));
                return;
            }
            GUILayout.Label(view.ContractSummary, note);
            GUILayout.Space(4);
            contractScroll = GUILayout.BeginScrollView(contractScroll, GUILayout.Height(height - 30f));
            foreach (ContractState wanted in new[] { ContractState.Active, ContractState.Completed, ContractState.Locked })
            {
                bool any = false;
                foreach (ContractRow c in view.Contracts)
                {
                    if (c.State != wanted) continue;
                    if (!any)
                    {
                        GUILayout.Label(wanted == ContractState.Active ? Loc.T("Активные контракты")
                            : wanted == ContractState.Completed ? Loc.T("Выполненные") : Loc.T("Закрытые"), HudStyle.Title);
                        any = true;
                    }
                    Color tint = wanted == ContractState.Completed ? HudStyle.Accent : wanted == ContractState.Active ? Color.white : new Color(0.6f, 0.6f, 0.6f);
                    GUILayout.BeginVertical(HudStyle.Panel, GUILayout.Width(contentWidth));
                    Colored((wanted == ContractState.Completed ? "✓ " : wanted == ContractState.Active ? "○ " : "· ") + c.Title, HudStyle.Title, tint);
                    if (wanted == ContractState.Locked)
                    {
                        GUILayout.Label(c.Requires, HudStyle.Small);
                    }
                    else
                    {
                        GUILayout.Label(c.Description, note);
                        if (wanted == ContractState.Active && c.Progress.Length > 0) Colored(c.Progress, note, HudStyle.Warning);
                        GUILayout.Label(c.Reward, HudStyle.Small);
                    }
                    GUILayout.EndVertical();
                    GUILayout.Space(3);
                }
                if (any) GUILayout.Space(6);
            }
            GUILayout.EndScrollView();
        }

        // ---- Tab 5: rocket upgrades ----------------------------------------------------------

        void DrawUpgrades(float height)
        {
            if (!view.Career)
                SandboxNote(Loc.T("В песочнице все улучшения ракет уже установлены на максимум."));
            else
                GUILayout.Label(Loc.T("Улучшения покупаются за кредиты один раз и действуют на все ракеты, которые выкатываются на старт. ") +
                                Loc.T("Каждый следующий уровень требует более высокого уровня техники."), note);
            GUILayout.Space(4);
            upgradeScroll = GUILayout.BeginScrollView(upgradeScroll, GUILayout.Height(height - 40f));
            foreach (UpgradeRow u in view.Upgrades)
            {
                string id = u.Id;
                GUILayout.BeginVertical(HudStyle.Panel, GUILayout.Width(contentWidth));
                GUILayout.BeginHorizontal();
                GUILayout.Label(u.Title, HudStyle.Title);
                GUILayout.FlexibleSpace();
                GUILayout.Label(u.Level, HudStyle.Value, GUILayout.Width(140));
                GUILayout.EndHorizontal();
                GUILayout.Label(u.Effect, note);
                Colored(u.StateText, note, u.StateColor);
                if (view.Career)
                {
                    GUILayout.BeginHorizontal();
                    if (Button(u.ButtonLabel, u.CanBuy, GUILayout.Width(240), GUILayout.Height(26))) Defer(() => BuyUpgrade(id));
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
                GUILayout.Space(4);
            }
            GUILayout.EndScrollView();
        }

        // ---- Tab 6: robot talents ------------------------------------------------------------

        void DrawTalents(float height)
        {
            Colored(view.TalentSummary, note, view.Career ? Color.white : HudStyle.Accent);
            GUILayout.Space(4);
            float column = Mathf.Max(160f, (contentWidth - 24f) / 3f);
            talentScroll = GUILayout.BeginScrollView(talentScroll, GUILayout.Height(height - 30f));
            GUILayout.BeginHorizontal();
            foreach (TalentBranch branch in new[] { TalentBranch.Protection, TalentBranch.Mobility, TalentBranch.Science })
            {
                GUILayout.BeginVertical(GUILayout.Width(column));
                Colored(RobotTalents.BranchName(branch).ToUpperInvariant(), HudStyle.Title, HudStyle.Accent);
                bool first = true;
                foreach (TalentRow t in view.Talents)
                {
                    if (t.Branch != branch) continue;
                    if (!first) GUILayout.Label("\u2193", HudStyle.Small);
                    first = false;
                    string id = t.Id;
                    GUILayout.BeginVertical(HudStyle.Panel, GUILayout.Width(column));
                    GUILayout.Label(t.Title, HudStyle.Title);
                    string pips = "";
                    for (int r = 0; r < RobotTalents.MaxRank; r++) pips += r < t.Rank ? "\u25CF" : "\u25CB";
                    Colored(pips + "  " + Loc.T("ранг ") + t.Rank + " / " + RobotTalents.MaxRank, compact, t.Rank > 0 ? HudStyle.Accent : Color.white);
                    GUILayout.Label(t.Effect, note);
                    Colored(t.StateText, note, t.StateColor);
                    if (view.Career && Button(Loc.T("Изучить (1 очко)"), t.CanLearn, GUILayout.Height(24))) Defer(() => LearnTalent(id));
                    GUILayout.EndVertical();
                }
                GUILayout.EndVertical();
                GUILayout.Space(8);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
        }

        // ---- Footer --------------------------------------------------------------------------

        void DrawFooter(float innerW)
        {
            GUILayout.BeginHorizontal();
            if (!string.IsNullOrEmpty(status)) Colored(status, note, statusColor, GUILayout.Width(Mathf.Max(100f, innerW - 140f)));
            GUILayout.FlexibleSpace();
            if (Button(Loc.T("Закрыть (Esc)"), true, GUILayout.Width(120), GUILayout.Height(30))) Defer(Close);
            GUILayout.EndHorizontal();
        }

        // ---- IMGUI helpers -------------------------------------------------------------------

        static bool Button(string text, bool enabled, params GUILayoutOption[] options)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            bool clicked = GUILayout.Button(text, HudStyle.Button, options);
            GUI.enabled = previous;
            return clicked && enabled;
        }

        static void Colored(string text, GUIStyle style, Color color, params GUILayoutOption[] options)
        {
            Color c = GUI.color;
            GUI.color = color;
            GUILayout.Label(text, style, options);
            GUI.color = c;
        }

        // ---- Formatting (display only; values stay SI) ---------------------------------------

        static string N(double value, int decimals) => UnitFormatter.Number(value, decimals);

        static string Credits(int credits) => N(credits, 0) + Loc.T(" кр.");

        /// <summary>Research points: whole numbers without decimals, fractional ones with one.</summary>
        static string Points(double points) =>
            N(points, Math.Abs(points - Math.Round(points)) < 1e-6 ? 0 : 1) + Loc.T(" очк.");

        static string Mass(double kg)
        {
            double a = Math.Abs(kg);
            if (a < 1000.0) return N(kg, 0) + Loc.T(" кг");
            if (a < 10000.0) return N(kg / 1000.0, 2) + Loc.T(" т");
            return N(kg / 1000.0, 1) + Loc.T(" т");
        }

        static string SampleMass(double kg)
        {
            double a = Math.Abs(kg);
            if (a <= 0.0) return Loc.T("0 кг");
            if (a < 1.0) return N(kg * 1000.0, 0) + Loc.T(" г");
            return N(kg, 2) + Loc.T(" кг");
        }

        static string Force(double newtons) =>
            Math.Abs(newtons) >= 1.0e6 ? N(newtons / 1.0e6, 2) + Loc.T(" МН") : N(newtons / 1.0e3, 0) + Loc.T(" кН");

        static string Level(int level) => level > 0 ? Loc.T("уровень ") + level : Loc.T("нет");

        /// <summary>Integrity loss per simulated second (fraction) shown as % per minute.</summary>
        static string PercentPerMinute(double ratePerSecond)
        {
            double pm = ratePerSecond * 60.0 * 100.0;
            return N(pm, Math.Abs(pm - Math.Round(pm)) < 1e-6 ? 0 : 1) + Loc.T(" %/мин");
        }

        /// <summary>Simulated time until integrity falls from 100 % to the instrument threshold at a constant rate.</summary>
        static string TimeToOffline(double ratePerSecond)
        {
            if (ratePerSecond <= 0.0) return Loc.T("приборы не отключатся");
            double minutes = (1.0 - HazardModel.InstrumentThreshold) / ratePerSecond / 60.0;
            return Loc.T("приборы отключатся через ≈") + N(minutes, minutes < 10.0 ? 1 : 0) + Loc.T(" мин");
        }

        /// <summary>"Радиационная защита Р-1" → "Р-1" (the catalog code), otherwise the full name.</summary>
        static string ShortName(ModuleDefinition m)
        {
            if (m == null || string.IsNullOrEmpty(m.name)) return "—";
            int space = m.name.LastIndexOf(' ');
            string last = space >= 0 ? m.name.Substring(space + 1) : m.name;
            return last.IndexOf('-') > 0 ? last : m.name;
        }

        static string CategoryName(ModuleCategory c)
        {
            switch (c)
            {
                case ModuleCategory.Cabin: return Loc.T("кабина");
                case ModuleCategory.Engine: return Loc.T("двигатель");
                case ModuleCategory.FuelTank: return Loc.T("топливный бак");
                case ModuleCategory.Aerodynamics: return Loc.T("аэродинамика");
                case ModuleCategory.Science: return Loc.T("наука");
                case ModuleCategory.Auxiliary: return Loc.T("вспомогательное");
                case ModuleCategory.Communication: return Loc.T("коммуникация");
                case ModuleCategory.Mechanics: return Loc.T("механика");
                case ModuleCategory.Cargo: return Loc.T("грузовой модуль");
                default: return c.ToString();
            }
        }

        static string SampleCategoryName(SampleCategory c)
        {
            switch (c)
            {
                case SampleCategory.Rock: return Loc.T("камень");
                case SampleCategory.Mineral: return Loc.T("минерал");
                case SampleCategory.RareGeology: return Loc.T("редкая порода");
                case SampleCategory.Soil: return Loc.T("грунт");
                case SampleCategory.MineralSoil: return Loc.T("минеральный грунт");
                case SampleCategory.UnusualCompound: return Loc.T("необычное соединение");
                case SampleCategory.Water: return Loc.T("вода");
                case SampleCategory.OtherLiquid: return Loc.T("другая жидкость");
                case SampleCategory.Acid: return Loc.T("кислота");
                default: return c.ToString();
            }
        }

        static string RarityName(SampleRarity r)
        {
            switch (r)
            {
                case SampleRarity.Common: return Loc.T("обычный");
                case SampleRarity.Uncommon: return Loc.T("необычный");
                case SampleRarity.Rare: return Loc.T("редкий");
                case SampleRarity.Exceptional: return Loc.T("исключительный");
                default: return r.ToString();
            }
        }

        static string CapabilityList(ModuleCapability caps)
        {
            var parts = new List<string>();
            if ((caps & ModuleCapability.OrbitPrediction) != 0) parts.Add(Loc.T("прогноз орбиты"));
            if ((caps & ModuleCapability.InterplanetaryNavigation) != 0) parts.Add(Loc.T("межпланетная навигация"));
            if ((caps & ModuleCapability.AtmosphereAnalysis) != 0) parts.Add(Loc.T("анализ атмосферы"));
            if ((caps & ModuleCapability.SampleAnalysis) != 0) parts.Add(Loc.T("анализ образцов"));
            return parts.Count > 0 ? string.Join(", ", parts) : "—";
        }

        static string KindList(SampleKinds kinds)
        {
            var parts = new List<string>();
            if ((kinds & SampleKinds.Rock) != 0) parts.Add(Loc.T("камни"));
            if ((kinds & SampleKinds.Soil) != 0) parts.Add(Loc.T("грунт"));
            if ((kinds & SampleKinds.Liquid) != 0) parts.Add(Loc.T("жидкости"));
            if ((kinds & SampleKinds.Acid) != 0) parts.Add(Loc.T("кислоты"));
            if ((kinds & SampleKinds.Rare) != 0) parts.Add(Loc.T("редкие материалы"));
            return parts.Count > 0 ? string.Join(", ", parts) : "—";
        }
    }
}
