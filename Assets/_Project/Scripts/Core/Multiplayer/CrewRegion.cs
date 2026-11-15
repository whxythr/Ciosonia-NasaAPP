namespace SpaceSim.Multiplayer
{
    /// <summary>
    /// The Photon Cloud regions a crew can play in. A room exists in one region only, so everybody of a crew must connect to the
    /// same one: the host's choice creates the room there, and a player who cannot find the code in their region also looks
    /// in the other (<see cref="Other"/>). Codes are Photon's region tokens. Pure, no Unity.
    /// </summary>
    public static class CrewRegion
    {
        public const string Europe = "eu";
        public const string America = "us";
        public const string Default = Europe;

        public static readonly string[] All = { Europe, America };

        /// <summary>A known region token (any case, spaces trimmed), or <see cref="Default"/>.</summary>
        public static string Normalize(string region)
        {
            string r = region == null ? "" : region.Trim().ToLowerInvariant();
            foreach (string known in All)
                if (r == known) return known;
            return Default;
        }

        /// <summary>The region to try next when a room code is not found in <paramref name="region"/>.</summary>
        public static string Other(string region) => Normalize(region) == Europe ? America : Europe;

        /// <summary>«EU» / «US» for the lobby switch and the status line.</summary>
        public static string Label(string region) => Normalize(region).ToUpperInvariant();
    }
}
