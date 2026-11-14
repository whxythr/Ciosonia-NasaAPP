using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SpaceSim.Localization
{
    public enum Language { Russian, English, Kazakh }

    /// <summary>
    /// Interface translation. The source text in the code is the key: <c>Loc.T("Закрыть")</c> looks it up in the table of the
    /// current language and falls back to the source when there is no translation. Spaces, brackets and punctuation around the
    /// text are kept, so fragments of composed lines (<c>" · шаг "</c>) can share one table entry.
    /// Pure text — never used by the physics.
    /// </summary>
    public static class Loc
    {
        static readonly Dictionary<string, string>[] tables = new Dictionary<string, string>[3];
        static ConcurrentDictionary<string, string> cache = new ConcurrentDictionary<string, string>();
        static readonly Func<string, string> translate = Translate;
        static Language current = Language.Russian;

        public static Language Current => current;

        /// <summary>Increases on every language change: cached texts built from <see cref="T"/> must be rebuilt.</summary>
        public static int Version { get; private set; }

        public static event Action Changed;

        public static void SetLanguage(Language language)
        {
            if (language == current) return;
            current = language;
            cache = new ConcurrentDictionary<string, string>();
            Version++;
            Changed?.Invoke();
        }

        /// <summary>Installs a table parsed with <see cref="Parse"/> for a language.</summary>
        public static void SetTable(Language language, Dictionary<string, string> table)
        {
            tables[(int)language] = table;
            cache = new ConcurrentDictionary<string, string>();
        }

        /// <summary>Translates <paramref name="source"/> into the current language (the source itself if there is no entry).</summary>
        public static string T(string source)
        {
            if (string.IsNullOrEmpty(source)) return source;
            Dictionary<string, string> table = tables[(int)current];
            if (table == null || table.Count == 0) return source;
            return cache.GetOrAdd(source, translate);
        }

        static string Translate(string source)
        {
            Dictionary<string, string> table = tables[(int)current];
            int first = 0, last = source.Length - 1;
            while (first < source.Length && !char.IsLetter(source[first])) first++;
            while (last >= first && !char.IsLetter(source[last]) && !char.IsNumber(source[last])) last--;
            if (first > last) return source;
            string core = source.Substring(first, last - first + 1);
            if (!table.TryGetValue(core, out string translated)) return source;
            return source.Substring(0, first) + translated + source.Substring(last + 1);
        }

        /// <summary>Reads "source&lt;TAB&gt;translation" lines; \n in either column stands for a line break.</summary>
        public static Dictionary<string, string> Parse(string text)
        {
            var table = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(text)) return table;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                int tab = line.IndexOf('\t');
                if (tab <= 0 || tab == line.Length - 1) continue;
                table[line.Substring(0, tab).Replace("\\n", "\n")] = line.Substring(tab + 1).Replace("\\n", "\n");
            }
            return table;
        }
    }
}
