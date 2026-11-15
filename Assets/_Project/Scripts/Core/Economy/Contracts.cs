using System;
using System.Collections.Generic;
using SpaceSim.Localization;
using SpaceSim.Planets;
using SpaceSim.Science;

namespace SpaceSim.Economy
{
    /// <summary>What a career contract asks for. Every kind is checked against saved profile data only (never the live flight).</summary>
    public enum ContractKind
    {
        /// <summary>A research milestone (<see cref="ResearchMilestones"/>).</summary>
        Milestone,
        /// <summary>Enter the sphere of influence of a body (<see cref="PlayerProfile.visitedBodies"/>).</summary>
        Visit,
        /// <summary>Land on a body (<see cref="PlayerProfile.landedBodies"/>).</summary>
        Land,
        /// <summary>Bring a sample from a body back home (<see cref="PlayerProfile.returnedSampleBodies"/>).</summary>
        ReturnSample,
        /// <summary>Map a share of a body's science layer from orbit (<see cref="PlayerProfile.scienceRewards"/>).</summary>
        Map
    }

    public enum ContractState
    {
        /// <summary>A required contract is not done yet.</summary>
        Locked,
        /// <summary>On the board: completes by itself as soon as its condition is met.</summary>
        Active,
        Completed
    }

    /// <summary>One task of the contract board: condition, prerequisites and reward.</summary>
    public sealed class ContractDefinition
    {
        public ContractDefinition(string id, string title, string description, ContractKind kind, string target, int credits, double research,
            int talentPoints, string requires = null, ScienceLayer layer = ScienceLayer.Terrain, int percent = 0)
        {
            Id = id;
            rawTitle = title;
            rawDescription = description;
            Kind = kind;
            Target = target;
            Credits = credits;
            Research = research;
            TalentPoints = talentPoints;
            Requires = requires;
            Layer = layer;
            Percent = percent;
        }

        readonly string rawTitle, rawDescription;
        public string Id { get; }
        public string Title => Loc.T(rawTitle);
        public string Description => Loc.T(rawDescription);
        public ContractKind Kind { get; }
        /// <summary>Milestone id (<see cref="ContractKind.Milestone"/>) or body id (every other kind).</summary>
        public string Target { get; }
        public int Credits { get; }
        public double Research { get; }
        /// <summary>Points for the robot's talent tree (<see cref="RobotTalents"/>).</summary>
        public int TalentPoints { get; }
        /// <summary>Contract that must be completed first (null = on the board from the start).</summary>
        public string Requires { get; }
        public ScienceLayer Layer { get; }
        /// <summary><see cref="ContractKind.Map"/>: whole percent of the layer that must be mapped.</summary>
        public int Percent { get; }
    }

    /// <summary>
    /// The career contract board: research tasks that pay credits, research points and robot talent points. Contracts open in
    /// chains (<see cref="ContractDefinition.Requires"/>) and complete automatically when the saved profile shows the condition met
    /// (<see cref="CheckCompletions"/>, run on every profile save). Sandbox has no contracts.
    /// </summary>
    public static class Contracts
    {
        const string H = HomeSystemCatalog.HomeWorldId;

        static readonly List<ContractDefinition> all = new List<ContractDefinition>
        {
            new ContractDefinition("c-reach-space", "Первый пуск", "Поднимите ракету выше условной границы космоса Циосонии.",
                ContractKind.Milestone, ResearchMilestones.ReachSpace, 3000, 5, 1),
            new ContractDefinition("c-home-weather", "Погода над домом", "Нанесите на карту 25 % температуры поверхности Циосонии с орбиты.",
                ContractKind.Map, H, 4000, 5, 1, "c-reach-space", ScienceLayer.Temperature, 25),
            new ContractDefinition("c-first-orbit", "На орбите", "Выйдите на устойчивую орбиту вокруг Циосонии.",
                ContractKind.Milestone, ResearchMilestones.FirstOrbit, 6000, 10, 1, "c-reach-space"),
            new ContractDefinition("c-home-pressure", "Барометр планеты", "Нанесите на карту 50 % давления у поверхности Циосонии.",
                ContractKind.Map, H, 5000, 5, 1, "c-home-weather", ScienceLayer.Pressure, 50),
            new ContractDefinition("c-home-terrain", "Рельеф Циосонии", "Нанесите на карту 50 % рельефа Циосонии.",
                ContractKind.Map, H, 6000, 10, 1, "c-first-orbit", ScienceLayer.Terrain, 50),
            new ContractDefinition("c-kolimpri-visit", "К Колимпри", "Долетите до сферы влияния внутренней луны Колимпри.",
                ContractKind.Visit, HomeSystemCatalog.KolimpriId, 10000, 15, 1, "c-first-orbit"),
            new ContractDefinition("c-tromos-visit", "К Тромосу", "Долетите до сферы влияния внешней луны Тромос.",
                ContractKind.Visit, HomeSystemCatalog.TromosId, 10000, 15, 1, "c-first-orbit"),
            new ContractDefinition("c-kolimpri-map", "Карта Колимпри", "Нанесите на карту 30 % рельефа Колимпри.",
                ContractKind.Map, HomeSystemCatalog.KolimpriId, 12000, 15, 1, "c-kolimpri-visit", ScienceLayer.Terrain, 30),
            new ContractDefinition("c-kolimpri-land", "Посадка на Колимпри", "Посадите ракету на поверхность Колимпри.",
                ContractKind.Land, HomeSystemCatalog.KolimpriId, 20000, 25, 2, "c-kolimpri-visit"),
            new ContractDefinition("c-tromos-land", "Посадка на Тромос", "Посадите ракету на поверхность Тромоса.",
                ContractKind.Land, HomeSystemCatalog.TromosId, 20000, 25, 2, "c-tromos-visit"),
            new ContractDefinition("c-first-sample", "Первый образец", "Возьмите роботом первый образец грунта, породы или жидкости.",
                ContractKind.Milestone, ResearchMilestones.FirstSample, 8000, 10, 1, "c-first-orbit"),
            new ContractDefinition("c-kolimpri-sample", "Грунт Колимпри", "Доставьте на Циосонию образец с Колимпри.",
                ContractKind.ReturnSample, HomeSystemCatalog.KolimpriId, 25000, 30, 2, "c-kolimpri-land"),
            new ContractDefinition("c-tromos-sample", "Грунт Тромоса", "Доставьте на Циосонию образец с Тромоса.",
                ContractKind.ReturnSample, HomeSystemCatalog.TromosId, 25000, 30, 2, "c-tromos-land"),
            new ContractDefinition("c-escape", "Прочь от дома", "Наберите скорость убегания от Циосонии.",
                ContractKind.Milestone, ResearchMilestones.EscapeHome, 15000, 20, 1, "c-first-orbit"),
            new ContractDefinition("c-teonan-visit", "К Теонану", "Долетите до сферы влияния Теонана.",
                ContractKind.Visit, HomeSystemCatalog.TeonanId, 30000, 40, 2, "c-escape"),
            new ContractDefinition("c-quoris-visit", "К Кварису", "Долетите до сферы влияния Квариса.",
                ContractKind.Visit, HomeSystemCatalog.QuorisId, 30000, 40, 2, "c-escape"),
            new ContractDefinition("c-quoris-map", "Климат Квариса", "Нанесите на карту 40 % температуры поверхности Квариса.",
                ContractKind.Map, HomeSystemCatalog.QuorisId, 35000, 40, 2, "c-quoris-visit", ScienceLayer.Temperature, 40),
            new ContractDefinition("c-mora-visit", "К Гермеус Море", "Долетите до сферы влияния Гермеус Моры.",
                ContractKind.Visit, HomeSystemCatalog.HermeousMoraId, 40000, 50, 2, "c-escape"),
            new ContractDefinition("c-aegis-land", "Посадка на Эгиду", "Посадите ракету на Эгиду, спутник Гермеус Моры.",
                ContractKind.Land, HomeSystemCatalog.MoraMoonId, 60000, 80, 3, "c-mora-visit"),
            new ContractDefinition("c-tholin-visit", "К Золину", "Долетите до сферы влияния Золина.",
                ContractKind.Visit, HomeSystemCatalog.TholinId, 50000, 60, 2, "c-escape"),
            new ContractDefinition("c-tholin-sample", "Образец с Золина", "Доставьте на Циосонию образец с Золина.",
                ContractKind.ReturnSample, HomeSystemCatalog.TholinId, 80000, 100, 3, "c-tholin-visit"),
            new ContractDefinition("c-eremus-visit", "К Еремусу", "Долетите до сферы влияния Еремуса.",
                ContractKind.Visit, HomeSystemCatalog.EremusId, 60000, 80, 2, "c-escape"),
        };

        public static IReadOnlyList<ContractDefinition> All => all;

        public static ContractDefinition Find(string id) => all.Find(c => c.Id == id);

        public static bool IsCompleted(PlayerProfile p, string id) => p != null && p.completedContracts != null && p.completedContracts.Contains(id);

        public static ContractState StateOf(PlayerProfile p, ContractDefinition c)
        {
            if (IsCompleted(p, c.Id)) return ContractState.Completed;
            if (c.Requires != null && !IsCompleted(p, c.Requires)) return ContractState.Locked;
            return ContractState.Active;
        }

        /// <summary>True when the profile shows the contract's condition met (prerequisites not checked).</summary>
        public static bool ConditionMet(PlayerProfile p, ContractDefinition c)
        {
            if (p == null || c == null) return false;
            switch (c.Kind)
            {
                case ContractKind.Milestone: return p.HasMilestone(c.Target);
                case ContractKind.Visit: return p.visitedBodies != null && p.visitedBodies.Contains(c.Target);
                case ContractKind.Land: return p.landedBodies != null && p.landedBodies.Contains(c.Target);
                case ContractKind.ReturnSample: return p.returnedSampleBodies != null && p.returnedSampleBodies.Contains(c.Target);
                case ContractKind.Map: return MappedPercent(p, c.Target, c.Layer) >= c.Percent;
                default: return false;
            }
        }

        /// <summary>Whole percent of a body's layer already mapped (and paid) in this career.</summary>
        public static int MappedPercent(PlayerProfile p, string bodyId, ScienceLayer layer)
        {
            if (p == null || p.scienceRewards == null) return 0;
            foreach (ScienceRewardRecord r in p.scienceRewards)
                if (r != null && r.bodyId == bodyId && r.layer == (int)layer) return r.rewardedPercent;
            return 0;
        }

        /// <summary>Credits the contract pays this profile (the robot's «Отчётность» talent adds a share).</summary>
        public static int CreditsFor(PlayerProfile p, ContractDefinition c) =>
            (int)Math.Round(c.Credits * RobotTalents.ContractRewardFactor(p));

        /// <summary>"Карта: 12 % из 30 %" style progress line for the board ("" when there is nothing to count).</summary>
        public static string Progress(PlayerProfile p, ContractDefinition c)
        {
            if (c.Kind != ContractKind.Map) return "";
            return Loc.T("Нанесено на карту: ") + Math.Min(MappedPercent(p, c.Target, c.Layer), c.Percent) + Loc.T(" % из ") + c.Percent + " %";
        }

        /// <summary>
        /// Career: completes every active contract whose condition is met and pays its reward (credits, research, talent points).
        /// Repeats until nothing changes, so a chain whose conditions are all already met completes at once.
        /// Returns the newly completed contracts (empty in Sandbox).
        /// </summary>
        public static List<ContractDefinition> CheckCompletions(PlayerProfile p)
        {
            var done = new List<ContractDefinition>();
            if (p == null || !p.IsCareer) return done;
            if (p.completedContracts == null) p.completedContracts = new List<string>();
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (ContractDefinition c in all)
                {
                    if (StateOf(p, c) != ContractState.Active || !ConditionMet(p, c)) continue;
                    p.completedContracts.Add(c.Id);
                    p.Award(CreditsFor(p, c), c.Research, Loc.T("Контракт «") + c.Title + "»");
                    p.talentPointsEarned += c.TalentPoints;
                    done.Add(c);
                    changed = true;
                }
            }
            return done;
        }
    }
}
