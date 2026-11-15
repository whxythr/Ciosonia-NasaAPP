using SpaceSim.Localization;
using SpaceSim.Multiplayer;
using SpaceSim.Runtime.Multiplayer;

namespace SpaceSim.Runtime
{
    // Team mission: only the engineer changes the rocket. The other roles may open the hangar to look at the design, its
    // analysis and its Δv, but every edit, the project name, the propellant sliders and «Вывезти на старт» are refused.
    public sealed partial class HangarUI
    {
        /// <summary>This computer may change the rocket (always true outside a team mission).</summary>
        bool CrewCanBuild => CrewSession.Can(CrewAction.BuildRocket);

        /// <summary>«Ракету строит ИНЖЕНЕР (Kepler). Ангар открыт только для просмотра.»</summary>
        static string CrewViewOnlyText => CrewSession.DeniedText(CrewAction.BuildRocket) + " " + Loc.T("Ангар открыт только для просмотра.");

        /// <summary>Guard at the top of every edit: false (and the reason in the status line) when the rocket is someone else's job.</summary>
        bool CrewEditAllowed()
        {
            if (CrewCanBuild) return true;
            SetStatus(CrewViewOnlyText, HudStyle.Warning);
            return false;
        }
    }
}
