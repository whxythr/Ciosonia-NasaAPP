using System;
using SpaceSim.Localization;

namespace SpaceSim.Multiplayer
{
    /// <summary>The four seats of a team mission. Every role must be held by someone before the mission starts.</summary>
    public enum CrewRole
    {
        /// <summary>Analyses the mission and hands every other role what it needs: robot mass, rocket specs, robot tasks, recovery.</summary>
        Captain = 0,
        /// <summary>Builds the rocket for the mission: distance, payload and a margin for human error.</summary>
        Engineer = 1,
        /// <summary>Plans the flight with the finished rocket and flies it.</summary>
        Pilot = 2,
        /// <summary>Builds the surface robot from the satellite images (battery, leg pads, shielding) and drives it.</summary>
        RobotOperator = 3
    }

    /// <summary>Display data and responsibilities of one <see cref="CrewRole"/>.</summary>
    public sealed class CrewRoleInfo
    {
        internal CrewRoleInfo(CrewRole role, string title, string initials, string owns, string description, string[] duties,
            byte r, byte g, byte b)
        {
            Role = role;
            rawTitle = title;
            rawInitials = initials;
            rawOwns = owns;
            rawDescription = description;
            rawDuties = duties;
            R = r;
            G = g;
            B = b;
        }

        readonly string rawTitle, rawInitials, rawOwns, rawDescription;
        readonly string[] rawDuties;

        public CrewRole Role { get; }
        public string Title => Loc.T(rawTitle);
        /// <summary>Two letters for the avatar circle and the crew chips.</summary>
        public string Initials => Loc.T(rawInitials);
        /// <summary>What the role signs off («Ракета · Δv · Запас»).</summary>
        public string Owns => Loc.T(rawOwns);
        public string Description => Loc.T(rawDescription);
        public int DutyCount => rawDuties.Length;
        public string Duty(int i) => Loc.T(rawDuties[i]);
        /// <summary>Accent colour, 0–255 sRGB.</summary>
        public byte R { get; }
        public byte G { get; }
        public byte B { get; }
    }

    /// <summary>The role catalogue (order = <see cref="CrewRole"/> values = card order in the lobby).</summary>
    public static class CrewRoles
    {
        public const int Count = 4;

        public static readonly CrewRole[] All = { CrewRole.Captain, CrewRole.Engineer, CrewRole.Pilot, CrewRole.RobotOperator };

        static readonly CrewRoleInfo[] infos =
        {
            new CrewRoleInfo(CrewRole.Captain, "КАПИТАН", "КП", "Миссия · Связь ролей",
                "Разбирает миссию, выбранную командой, и раздаёт каждому то, что ему нужно.",
                new[]
                {
                    "Вес и задачи робота — водителю робота",
                    "Характеристики ракеты — инженеру и пилоту",
                    "Как робота заберут после миссии",
                }, 240, 168, 64),
            new CrewRoleInfo(CrewRole.Engineer, "ИНЖЕНЕР", "ИН", "Ракета · Δv · Запас",
                "Строит ракету под миссию: дистанция, полезная нагрузка и запас на ошибку человека.",
                new[]
                {
                    "Ракета под дистанцию и массу робота",
                    "Запас топлива на ошибку пилота",
                    "Предупреждения системы о нехватке запаса",
                }, 98, 140, 255),
            new CrewRoleInfo(CrewRole.Pilot, "ПИЛОТ", "ПЛ", "План полёта · Управление",
                "Планирует полёт на готовой ракете и управляет ей.",
                new[]
                {
                    "План полёта и манёвры",
                    "Взлёт, переход и посадка",
                    "Доставка робота к цели",
                }, 170, 226, 110),
            new CrewRoleInfo(CrewRole.RobotOperator, "ВОДИТЕЛЬ РОБОТА", "ВР", "Робот · Батарея · Защита",
                "Собирает робота по снимкам со спутника и управляет им на поверхности.",
                new[]
                {
                    "Батарея и накладки на ноги",
                    "Защита от радиации и температуры",
                    "Управление роботом и его состоянием",
                }, 255, 104, 92),
        };

        public static CrewRoleInfo Info(CrewRole role)
        {
            int i = (int)role;
            if (i < 0 || i >= infos.Length) throw new ArgumentOutOfRangeException(nameof(role));
            return infos[i];
        }
    }
}
