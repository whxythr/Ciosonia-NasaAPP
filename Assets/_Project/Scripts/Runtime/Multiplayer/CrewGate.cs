using SpaceSim.Multiplayer;
using UnityEngine;

namespace SpaceSim.Runtime.Multiplayer
{
    /// <summary>
    /// Role check at the controls of a team mission. <see cref="Allows"/> answers <see cref="CrewSession.Can"/> and, when the
    /// action belongs to another role, tells the player who does it — at most once every few seconds per action, so a held key
    /// does not flood the message line. Outside a team mission everything is allowed and nothing is shown.
    /// </summary>
    public static class CrewGate
    {
        const float NoticeInterval = 3f;
        static readonly float[] lastNotice = { -100f, -100f, -100f, -100f };

        public static bool Allows(CrewAction action, FlightStateController state)
        {
            if (CrewSession.Can(action)) return true;
            Deny(action, state);
            return false;
        }

        /// <summary>Shows «this belongs to another role» (rate-limited) without checking.</summary>
        public static void Deny(CrewAction action, FlightStateController state)
        {
            int i = (int)action;
            float now = Time.unscaledTime;
            if (i >= 0 && i < lastNotice.Length && now - lastNotice[i] < NoticeInterval) return;
            if (i >= 0 && i < lastNotice.Length) lastNotice[i] = now;
            state?.Notify(CrewSession.DeniedText(action));
        }
    }
}
