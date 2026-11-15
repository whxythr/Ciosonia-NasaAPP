using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using SpaceSim.Localization;
using UnityEngine;

namespace SpaceSim.Tests
{
    /// <summary>
    /// Regression: menus, the pause screen, settings and the HUD stayed Russian in English and Kazakh because their texts
    /// were never added to Resources/Localization. Every Russian string literal in the game scripts must have an entry
    /// (same rules as <see cref="Loc.T"/>: punctuation around the text ignored, letter case folded) in en.txt and kk.txt.
    /// </summary>
    public class LocalizationCoverageTests
    {
        static readonly Regex Literal = new Regex("\"((?:[^\"\\\\\\n]|\\\\.)*)\"");
        static readonly Regex Cyrillic = new Regex("[А-Яа-яЁё]");

        [TestCase("en")]
        [TestCase("kk")]
        public void EveryRussianLiteralHasATranslation(string language)
        {
            var asset = Resources.Load<TextAsset>("Localization/" + language);
            Assert.IsNotNull(asset, "Resources/Localization/" + language + ".txt");
            var table = new HashSet<string>(Loc.Parse(asset.text).Keys, System.StringComparer.OrdinalIgnoreCase);

            string scripts = Path.Combine(Application.dataPath, "_Project", "Scripts");
            var missing = new StringBuilder();
            int count = 0;
            foreach (string file in Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Replace('\\', '/').Contains("/Editor/")) continue;
                string[] lines = File.ReadAllLines(file);
                for (int n = 0; n < lines.Length; n++)
                {
                    string line = lines[n];
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("//") || trimmed.StartsWith("*") || trimmed.StartsWith("[") ||
                        line.Contains("Debug.Log") || line.Contains("Exception(")) continue;
                    foreach (Match m in Literal.Matches(line))
                    {
                        string text = m.Groups[1].Value;
                        if (!Cyrillic.IsMatch(text)) continue;
                        string core = Core(text.Replace("\\n", "\n").Replace("\\\"", "\""));
                        if (core.Length == 0 || table.Contains(core)) continue;
                        count++;
                        if (count <= 40) missing.AppendLine(Path.GetFileName(file) + ":" + (n + 1) + "  " + core);
                    }
                }
            }
            Assert.AreEqual(0, count, language + ".txt lacks " + count + " texts:\n" + missing);
        }

        /// <summary>The part of a literal <see cref="Loc.T"/> looks up: from the first letter to the last letter or digit.</summary>
        static string Core(string s)
        {
            int first = 0, last = s.Length - 1;
            while (first < s.Length && !char.IsLetter(s[first])) first++;
            while (last >= first && !char.IsLetter(s[last]) && !char.IsNumber(s[last])) last--;
            return first > last ? "" : s.Substring(first, last - first + 1);
        }
    }
}
