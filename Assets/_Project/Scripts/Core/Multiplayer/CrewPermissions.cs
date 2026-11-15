using System.Collections.Generic;
using SpaceSim.Localization;

namespace SpaceSim.Multiplayer
{
    /// <summary>What a player of a team mission may do with the shared rocket and robot.</summary>
    public enum CrewAction
    {
        /// <summary>Change the rocket in the hangar and roll it out to the pad.</summary>
        BuildRocket = 0,
        /// <summary>Manoeuvre nodes, transfers, assisted burns and warp to a trajectory point.</summary>
        PlanFlight = 1,
        /// <summary>Ignition, throttle, attitude, staging, parachutes, legs, RCS.</summary>
        FlyRocket = 2,
        /// <summary>Going outside, walking the robot, samples, the rover drive and the drill.</summary>
        WorkOnSurface = 3
    }

    /// <summary>
    /// Which role owns which action in a team mission. Every action has exactly one owning role; a player who holds several
    /// roles may do all of their actions. The captain owns no controls: they analyse the mission and talk to the crew.
    /// Outside a team mission the runtime does not ask (the single player holds every role). Pure, no Unity.
    /// </summary>
    public static class CrewPermissions
    {
        public static readonly CrewAction[] All =
            { CrewAction.BuildRocket, CrewAction.PlanFlight, CrewAction.FlyRocket, CrewAction.WorkOnSurface };

        /// <summary>The role that owns the action.</summary>
        public static CrewRole Owner(CrewAction action)
        {
            switch (action)
            {
                case CrewAction.BuildRocket: return CrewRole.Engineer;
                case CrewAction.PlanFlight:
                case CrewAction.FlyRocket: return CrewRole.Pilot;
                default: return CrewRole.RobotOperator;
            }
        }

        /// <summary>True when one of <paramref name="roles"/> owns the action.</summary>
        public static bool Allowed(CrewAction action, IEnumerable<CrewRole> roles)
        {
            if (roles == null) return false;
            CrewRole owner = Owner(action);
            foreach (CrewRole r in roles)
                if (r == owner) return true;
            return false;
        }

        /// <summary>«Ракету строит ИНЖЕНЕР (Kepler).» — shown when a player tries an action of another role.</summary>
        public static string DeniedText(CrewAction action, string holderName)
        {
            string what;
            switch (action)
            {
                case CrewAction.BuildRocket: what = Loc.T("Ракету строит"); break;
                case CrewAction.PlanFlight: what = Loc.T("Полёт планирует"); break;
                case CrewAction.FlyRocket: what = Loc.T("Ракетой управляет"); break;
                default: what = Loc.T("Роботом управляет"); break;
            }
            string role = CrewRoles.Info(Owner(action)).Title;
            return string.IsNullOrEmpty(holderName)
                ? what + " " + role + "."
                : what + " " + role + " (" + holderName + ").";
        }
    }
}
