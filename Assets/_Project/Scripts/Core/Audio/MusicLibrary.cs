using System;
using System.Collections.Generic;

namespace SpaceSim.Audio
{
    /// <summary>
    /// One soundtrack entry. The game ships NO audio for these tracks (they are copyrighted and only published on SoundCloud):
    /// the player puts their own copy at <c>&lt;persistentDataPath&gt;/Music/&lt;Id&gt;.ogg|.mp3|.wav</c> (or StreamingAssets/Music).
    /// Without a file the track is silent and the game offers the official <see cref="SoundCloudUrl"/> instead.
    /// </summary>
    public sealed class MusicTrack
    {
        /// <summary>Stable id; also the expected file name without extension.</summary>
        public string Id = "";
        public string Artist = "";
        public string Title = "";
        /// <summary>Official SoundCloud page (opened in the system browser; never streamed or scraped).</summary>
        public string SoundCloudUrl = "";

        public string DisplayName => string.IsNullOrEmpty(Artist) ? Title : Artist + " — " + Title;
    }

    /// <summary>Kinds of music zones, in the order they are considered (see <see cref="MusicSelector"/>).</summary>
    public enum MusicZoneKind
    {
        /// <summary>The main-menu scene (always wins).</summary>
        MainMenu,
        /// <summary>Heliocentric distance below <see cref="MusicWorld.StarRegionRadius"/>.</summary>
        StarRegion,
        /// <summary>Close to a body: centre distance below radius + its space-boundary altitude ("surface and atmosphere").</summary>
        NearSurface,
        /// <summary>Within a radius of an anchor body (e.g. inside the orbit of one of its moons); overrides the anchor's own SOI rule.</summary>
        Proximity,
        /// <summary>Inside a body's sphere of influence.</summary>
        BodySoi,
        /// <summary>Inside the SOI of a body that has no rule of its own (see <see cref="MusicLibrary.UnassignedSoiTrackId"/>).</summary>
        UnassignedSoi,
        /// <summary>Heliocentric, outside every SOI and outside the star region.</summary>
        DeepSpace
    }

    /// <summary>One row of the data table "where → which track". No game code branches on body names; it all comes from here.</summary>
    public sealed class MusicRule
    {
        public string Id = "";
        public MusicZoneKind Kind;
        /// <summary>Track to play; null or empty = silence in this zone.</summary>
        public string TrackId;
        /// <summary>Bodies this rule matches (ids and aliases, case-insensitive). For <see cref="MusicZoneKind.Proximity"/>: the anchor.</summary>
        public string[] BodyIds = Array.Empty<string>();
        /// <summary>The rule also matches every body whose parent is one of these (e.g. "all moons of Ciozonia").</summary>
        public string[] ParentIds = Array.Empty<string>();
        /// <summary>Proximity only: the zone radius is the semi-major axis of this body's orbit around the anchor (real orbit data).</summary>
        public string RadiusFromOrbitOf;
        /// <summary>Proximity only: radius in anchor radii when <see cref="RadiusFromOrbitOf"/> is not in the system (0 = rule off).</summary>
        public double FallbackRadiusInBodyRadii;
        /// <summary>Where the rule applies, for the settings screen (Russian source text, translated by Loc).</summary>
        public string Label = "";
        /// <summary>Free text for the settings screen / report.</summary>
        public string Note = "";
    }

    /// <summary>Tunables of the selector (thresholds are data, not code).</summary>
    public sealed class MusicSelectorSettings
    {
        /// <summary>
        /// Geometric hysteresis: a zone the vehicle is in is left only beyond (1 + band)·radius; entering needs &lt; radius.
        /// 5 % keeps music from flapping when an orbit grazes a SOI boundary.
        /// </summary>
        public double HysteresisBand = 0.05;
        /// <summary>A different track must be wanted continuously for this long (real seconds) before the music switches.</summary>
        public double MinDwellSeconds = 4.0;
        /// <summary>
        /// Star region = heliocentric distance below this fraction of the innermost planet's closest approach to the star minus
        /// that planet's SOI. 0.5 → "closer to the star than halfway to the innermost planet": for the home system ≈ 0.044 AU
        /// ≈ 5.5 stellar radii, where the star's disc is over 20° wide. Never overlaps a planet's SOI by construction.
        /// </summary>
        public double StarRegionInnerOrbitFraction = 0.5;
        /// <summary>Lower limit of the star region in stellar radii (systems whose innermost planet hugs the star).</summary>
        public double StarRegionMinStellarRadii = 3.0;
        /// <summary>Absolute star-region radius [m]; &gt; 0 replaces the rule above.</summary>
        public double StarRegionRadiusOverride;
    }

    /// <summary>
    /// The soundtrack: tracks, the zone rules and selector settings. <see cref="CreateDefault"/> is the designer table; a
    /// runtime may edit the returned object (it is plain data).
    /// </summary>
    public sealed class MusicLibrary
    {
        public const string MenuTrack = "aero-orion";
        public const string DeepSpaceTrack = "euclidea-aero-solar-protuberance";

        public readonly List<MusicTrack> Tracks = new List<MusicTrack>();
        public readonly List<MusicRule> Rules = new List<MusicRule>();
        public MusicSelectorSettings Selector = new MusicSelectorSettings();
        /// <summary>
        /// Track inside the SOI of a body no rule names (the start planet and its moons today). Null = use the deep-space rule's
        /// track (chosen so the launch pad is not silent; the brief assigns no track there).
        /// </summary>
        public string UnassignedSoiTrackId;

        public MusicTrack Track(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (MusicTrack t in Tracks)
                if (t.Id == id) return t;
            return null;
        }

        /// <summary>First rule of a kind (menu, star region, deep space have exactly one).</summary>
        public MusicRule RuleOf(MusicZoneKind kind)
        {
            foreach (MusicRule r in Rules)
                if (r.Kind == kind) return r;
            return null;
        }

        /// <summary>File names the player can supply for a track, in lookup order (ogg, mp3, wav).</summary>
        public static readonly string[] Extensions = { ".ogg", ".mp3", ".wav" };

        public static IEnumerable<string> ExpectedFileNames(string trackId)
        {
            foreach (string e in Extensions) yield return trackId + e;
        }

        /// <summary>Problems in the table (unknown track ids, duplicate ids, missing core rules); empty when consistent.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            var ids = new HashSet<string>();
            foreach (MusicTrack t in Tracks)
            {
                if (string.IsNullOrEmpty(t.Id)) problems.Add("track without id");
                else if (!ids.Add(t.Id)) problems.Add("duplicate track id " + t.Id);
                if (!string.IsNullOrEmpty(t.SoundCloudUrl) && !t.SoundCloudUrl.StartsWith("https://soundcloud.com/", StringComparison.Ordinal))
                    problems.Add("track " + t.Id + ": not a soundcloud.com URL");
            }
            foreach (MusicRule r in Rules)
                if (!string.IsNullOrEmpty(r.TrackId) && Track(r.TrackId) == null) problems.Add("rule " + r.Id + ": unknown track " + r.TrackId);
            foreach (MusicZoneKind k in new[] { MusicZoneKind.MainMenu, MusicZoneKind.StarRegion, MusicZoneKind.DeepSpace })
                if (RuleOf(k) == null) problems.Add("no " + k + " rule");
            if (!string.IsNullOrEmpty(UnassignedSoiTrackId) && Track(UnassignedSoiTrackId) == null) problems.Add("unknown unassigned-SOI track");
            return problems;
        }

        static MusicTrack T(string id, string artist, string title, string url) =>
            new MusicTrack { Id = id, Artist = artist, Title = title, SoundCloudUrl = url };

        static MusicRule R(string id, MusicZoneKind kind, string track, string label, string[] bodies = null, string[] parents = null, string note = "") =>
            new MusicRule
            {
                Id = id, Kind = kind, TrackId = track, Label = label, BodyIds = bodies ?? Array.Empty<string>(),
                ParentIds = parents ?? Array.Empty<string>(), Note = note
            };

        /// <summary>
        /// The designer's assignment. Body ids follow HomeSystemCatalog ("teonan", "quoris", "hermeous-mora", "tholin", "eremus")
        /// with the spellings of the brief as aliases. Ciozonia, its moons, Colossus, Mochi-Mochi, Dotta and Ori are not in the
        /// system yet: their rules wait for bodies with these ids (or for their aliases to be added here).
        /// </summary>
        public static MusicLibrary CreateDefault()
        {
            var lib = new MusicLibrary();
            lib.Tracks.Add(T("akira-the-blithering-heights", "Akira", "The Blithering Heights (FM Mix)", "https://soundcloud.com/cdad-dwdad/akira-the-blithering-heights-fm-mix"));
            lib.Tracks.Add(T("lokijar-black-hole", "Lokijar", "Black Hole", "https://soundcloud.com/cdad-dwdad/lokijar-black-hole"));
            lib.Tracks.Add(T(MenuTrack, "A.E.R.O", "Orion", "https://soundcloud.com/aeromusic/a-e-r-o-orion"));
            lib.Tracks.Add(T("aero-flying-through-the", "A.E.R.O", "Flying Through The", "https://soundcloud.com/aeromusic/a-e-r-o-flying-through-the"));
            lib.Tracks.Add(T(DeepSpaceTrack, "Euclidea", "Aero Solar Protuberance", "https://soundcloud.com/euclidea/aero-solar-protuberance"));
            lib.Tracks.Add(T("adrian-chifu-eternal-traveler", "Adrian Chifu", "Eternal Traveler", "https://soundcloud.com/user-271953987/adrian-chifu-eternal-traveler"));
            lib.Tracks.Add(T("water-drips-auroras", "Water Drips", "Auroras", "https://soundcloud.com/cdad-dwdad/water-drips-auroras"));
            lib.Tracks.Add(T("vortex-mechanics-saturn-rings", "Vortex Mechanics", "Saturn Rings (Traveller SE Edition)", "https://soundcloud.com/cdad-dwdad/vortex-mechanics-saturn-rings-traveller-se-edition"));
            lib.Tracks.Add(T("fadg-1", "Lokijar", "FADG-1", "https://soundcloud.com/lokijar/fadg-1"));
            lib.Tracks.Add(T("lombus-amino", "Lombus", "Amino", "https://soundcloud.com/lombusmusic/amino"));
            lib.Tracks.Add(T("lombus-cosmic-soup", "Lombus", "Cosmic Soup", "https://soundcloud.com/lombusmusic/cosmic-soup"));

            lib.Rules.Add(R("main-menu", MusicZoneKind.MainMenu, MenuTrack, "главное меню"));
            lib.Rules.Add(R("star", MusicZoneKind.StarRegion, "lokijar-black-hole", "у звезды"));
            lib.Rules.Add(R("teonan-quoris", MusicZoneKind.BodySoi, "akira-the-blithering-heights", "Теонан и Кварис",
                new[] { "teonan", "teanon", "quoris", "quaris" }));
            lib.Rules.Add(R("mora", MusicZoneKind.BodySoi, "lokijar-black-hole", "Гермеус Мора и Мора I",
                new[] { "hermeous-mora", "hermeus-mora", "hermeous-mora-i", "mora-i" }, new[] { "hermeous-mora", "hermeus-mora" }));
            lib.Rules.Add(R("ciozonia-surface", MusicZoneKind.NearSurface, "aero-flying-through-the", "Циозония: поверхность и атмосфера",
                new[] { "ciozonia" }, note: "below radius + space-boundary altitude"));
            lib.Rules.Add(R("ciozonia-moons", MusicZoneKind.BodySoi, "adrian-chifu-eternal-traveler", "спутники Циозонии", null, new[] { "ciozonia" }));
            lib.Rules.Add(R("colossus", MusicZoneKind.BodySoi, "water-drips-auroras", "Колосс", new[] { "colossus" }));
            MusicRule near = R("near-eremus", MusicZoneKind.Proximity, "lombus-cosmic-soup", "у Еремуса (внутри орбиты Ори)", new[] { "eremus", "yeremus" },
                note: "within Ori's semi-major axis around Eremus (6 Eremus radii until Ori exists)");
            near.RadiusFromOrbitOf = "ori";
            near.FallbackRadiusInBodyRadii = 6.0;
            lib.Rules.Add(near);
            lib.Rules.Add(R("eremus", MusicZoneKind.BodySoi, "vortex-mechanics-saturn-rings", "Еремус", new[] { "eremus", "yeremus" }));
            lib.Rules.Add(R("mochi-dotta", MusicZoneKind.BodySoi, "fadg-1", "Моти-Моти и Дотта", new[] { "mochi-mochi", "mochimochi", "dotta" }));
            lib.Rules.Add(R("tholin", MusicZoneKind.BodySoi, "lombus-amino", "Золин", new[] { "tholin", "tolin" }));
            lib.Rules.Add(R("deep-space", MusicZoneKind.DeepSpace, DeepSpaceTrack, "открытый космос"));
            return lib;
        }
    }
}
