using System;
using System.Text;

namespace SpaceSim.Multiplayer
{
    /// <summary>
    /// Room codes players read to each other: <c>HZN-7Q4K</c>. The alphabet leaves out look-alikes (0/O, 1/I/L), so a code read
    /// aloud or typed in lower case still finds the room. The code is the Photon room name.
    /// </summary>
    public static class CrewRoomCode
    {
        public const string Prefix = "HZN-";
        public const int Length = 4;
        public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

        public static string Generate(Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            var sb = new StringBuilder(Prefix, Prefix.Length + Length);
            for (int i = 0; i < Length; i++) sb.Append(Alphabet[random.Next(Alphabet.Length)]);
            return sb.ToString();
        }

        /// <summary>
        /// What the player typed → the canonical code, or null when it cannot be one. Accepts «hzn-7q4k», «7Q4K», «HZN 7Q4K».
        /// </summary>
        public static string Normalize(string typed)
        {
            if (string.IsNullOrWhiteSpace(typed)) return null;
            var body = new StringBuilder();
            foreach (char ch in typed.Trim().ToUpperInvariant())
                if (char.IsLetterOrDigit(ch)) body.Append(ch);
            string s = body.ToString();
            string bare = Prefix.Substring(0, Prefix.Length - 1);
            if (s.Length == bare.Length + Length && s.StartsWith(bare, StringComparison.Ordinal)) s = s.Substring(bare.Length);
            if (s.Length != Length) return null;
            foreach (char ch in s)
                if (Alphabet.IndexOf(ch) < 0) return null;
            return Prefix + s;
        }
    }
}
