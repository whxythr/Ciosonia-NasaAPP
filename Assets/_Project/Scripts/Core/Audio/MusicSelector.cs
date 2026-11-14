using System;
using System.Collections.Generic;

namespace SpaceSim.Audio
{
    /// <summary>Where the player is, as far as music cares (one snapshot).</summary>
    public sealed class MusicContext
    {
        /// <summary>The main-menu scene is showing.</summary>
        public bool InMainMenu;
        /// <summary>False while there is no vehicle (scene loading, flight stack being rebuilt): the music then stays as it is.</summary>
        public bool HasPosition;
        /// <summary>Distance from the vehicle to the star's centre [m].</summary>
        public double HeliocentricDistance;
        /// <summary>Distance from the vehicle to each body's centre [m], aligned with <see cref="MusicWorld.Bodies"/>.</summary>
        public double[] BodyDistances = Array.Empty<double>();

        public static MusicContext Menu() => new MusicContext { InMainMenu = true };
        public static MusicContext Unknown() => new MusicContext();
    }

    /// <summary>
    /// Selector memory between evaluations. Immutable: <see cref="MusicSelector.Step"/> returns a new one, so the selector is a
    /// pure function of (library, world, context, previous state, elapsed real time).
    /// </summary>
    public sealed class MusicSelectorState
    {
        public static readonly MusicSelectorState Initial = new MusicSelectorState(null, null, null, false, null, 0.0, null, false, new HashSet<string>());

        readonly HashSet<string> inside;

        internal MusicSelectorState(string track, string zone, string desiredTrack, bool hasPending, string pendingTrack, double pendingSeconds,
            string pendingZone, bool started, HashSet<string> inside)
        {
            HasPending = hasPending;
            TrackId = track;
            ZoneKey = zone;
            DesiredTrackId = desiredTrack;
            PendingTrackId = pendingTrack;
            PendingSeconds = pendingSeconds;
            PendingZoneKey = pendingZone;
            Started = started;
            this.inside = inside;
        }

        /// <summary>Track that should be playing now (null = silence).</summary>
        public string TrackId { get; }
        /// <summary>Zone that chose <see cref="TrackId"/> ("menu", "star", "soi:eremus", "near:ciozonia", "prox:near-eremus", "deep", …).</summary>
        public string ZoneKey { get; }
        /// <summary>What the geometry asks for right now, before the dwell time.</summary>
        public string DesiredTrackId { get; }
        /// <summary>A change of track (possibly to silence) is waiting out the dwell time.</summary>
        public bool HasPending { get; }
        /// <summary>A different track waiting out <see cref="MusicSelectorSettings.MinDwellSeconds"/>.</summary>
        public string PendingTrackId { get; }
        public double PendingSeconds { get; }
        public string PendingZoneKey { get; }
        /// <summary>A track has been chosen at least once.</summary>
        public bool Started { get; }
        /// <summary>Zone keys the vehicle was inside at the last evaluation (used for the hysteresis band).</summary>
        public IReadOnlyCollection<string> Inside => inside;
        public bool IsInside(string zoneKey) => inside.Contains(zoneKey);
    }

    /// <summary>
    /// Chooses the soundtrack from the player's position. Order of precedence:
    /// <list type="number">
    /// <item>main menu;</item>
    /// <item>star region (heliocentric distance &lt; <see cref="MusicWorld.StarRegionRadius"/>);</item>
    /// <item>the nested SOI chain, innermost body first (a moon beats its planet). At each body: a near-surface rule, then
    /// proximity rules anchored on it, then its SOI rule. Proximity zones lie strictly inside the anchor's SOI (Cosmic Soup inside
    /// Ori's orbit, inside Eremus' SOI), so they override only the anchor's own SOI rule, never a deeper moon's;</item>
    /// <item>a SOI with no rule → <see cref="MusicLibrary.UnassignedSoiTrackId"/> (default: the deep-space track);</item>
    /// <item>deep space.</item>
    /// </list>
    /// Anti-flapping: a zone the vehicle was in is left only beyond (1 + band)·radius, and a new track must be wanted for
    /// <see cref="MusicSelectorSettings.MinDwellSeconds"/> real seconds (menu changes and the first choice are immediate).
    /// </summary>
    public static class MusicSelector
    {
        public const string MenuZone = "menu", StarZone = "star", DeepZone = "deep";

        public static string SoiKey(string bodyId) => "soi:" + bodyId;
        public static string NearKey(string bodyId) => "near:" + bodyId;
        public static string ProximityKey(string ruleId) => "prox:" + ruleId;
        public static string UnassignedKey(string bodyId) => "free:" + bodyId;

        /// <summary>Track and zone the geometry asks for, ignoring the dwell time (still with the radius hysteresis of <paramref name="previous"/>).</summary>
        public static (string track, string zone, MusicZoneKind kind) Desired(MusicLibrary library, MusicWorld world, MusicContext ctx,
            MusicSelectorState previous, HashSet<string> insideOut)
        {
            previous ??= MusicSelectorState.Initial;
            double band = Math.Max(0.0, library.Selector.HysteresisBand);
            bool In(string key, double distance, double radius) =>
                !double.IsNaN(radius) && radius > 0.0 && distance < radius * (previous.IsInside(key) ? 1.0 + band : 1.0);

            if (ctx.InMainMenu)
            {
                insideOut?.Add(MenuZone);
                return (library.RuleOf(MusicZoneKind.MainMenu)?.TrackId, MenuZone, MusicZoneKind.MainMenu);
            }

            // The nested SOI chain, outermost first.
            var chain = new List<int>();
            int level = -1;
            for (int depth = 0; depth < 8; depth++)
            {
                int found = -1;
                double best = double.PositiveInfinity;
                for (int i = 0; i < world.Bodies.Count; i++)
                {
                    MusicBody b = world.Bodies[i];
                    if (b.ParentIndex != level || i >= ctx.BodyDistances.Length) continue;
                    double d = ctx.BodyDistances[i];
                    if (!In(SoiKey(b.Id), d, b.SoiRadius)) continue;
                    double rel = d / b.SoiRadius;
                    if (rel < best)
                    {
                        best = rel;
                        found = i;
                    }
                }
                if (found < 0) break;
                chain.Add(found);
                insideOut?.Add(SoiKey(world.Bodies[found].Id));
                level = found;
            }

            bool inStar = In(StarZone, ctx.HeliocentricDistance, world.StarRegionRadius);
            if (inStar) insideOut?.Add(StarZone);

            // Evaluate every zone of the chain (so the hysteresis memory is complete), then pick by precedence.
            (string track, string zone, MusicZoneKind kind)? pick = null;
            for (int c = chain.Count - 1; c >= 0; c--)
            {
                int index = chain[c];
                MusicBody b = world.Bodies[index];
                double d = ctx.BodyDistances[index];
                (string, string, MusicZoneKind)? near = null, prox = null, soi = null;
                foreach (MusicRule r in library.Rules)
                {
                    switch (r.Kind)
                    {
                        case MusicZoneKind.NearSurface when world.Matches(r, index):
                            if (In(NearKey(b.Id), d, b.NearSurfaceRadius))
                            {
                                insideOut?.Add(NearKey(b.Id));
                                near ??= (r.TrackId, NearKey(b.Id), r.Kind);
                            }
                            break;
                        case MusicZoneKind.Proximity when world.IndexOfAny(r.BodyIds) == index:
                            if (world.ProximityRadius.TryGetValue(r.Id, out double radius) && In(ProximityKey(r.Id), d, radius))
                            {
                                insideOut?.Add(ProximityKey(r.Id));
                                prox ??= (r.TrackId, ProximityKey(r.Id), r.Kind);
                            }
                            break;
                        case MusicZoneKind.BodySoi when world.Matches(r, index):
                            soi ??= (r.TrackId, SoiKey(b.Id), r.Kind);
                            break;
                    }
                }
                pick ??= near ?? prox ?? soi;
            }

            if (inStar) return (library.RuleOf(MusicZoneKind.StarRegion)?.TrackId, StarZone, MusicZoneKind.StarRegion);
            if (pick.HasValue) return pick.Value;
            MusicRule deep = library.RuleOf(MusicZoneKind.DeepSpace);
            if (chain.Count > 0)
            {
                string free = string.IsNullOrEmpty(library.UnassignedSoiTrackId) ? deep?.TrackId : library.UnassignedSoiTrackId;
                return (free, UnassignedKey(world.Bodies[chain[chain.Count - 1]].Id), MusicZoneKind.UnassignedSoi);
            }
            return (deep?.TrackId, DeepZone, MusicZoneKind.DeepSpace);
        }

        /// <summary>
        /// One evaluation. <paramref name="realSeconds"/> = unscaled time since the previous call (time warp does not shorten the
        /// dwell). Returns the new state; <see cref="MusicSelectorState.TrackId"/> is what should play.
        /// </summary>
        public static MusicSelectorState Step(MusicLibrary library, MusicWorld world, MusicContext ctx, MusicSelectorState previous, double realSeconds)
        {
            previous ??= MusicSelectorState.Initial;
            if (ctx == null || (!ctx.InMainMenu && !ctx.HasPosition)) return previous;

            var inside = new HashSet<string>();
            (string track, string zone, MusicZoneKind kind) = Desired(library, world, ctx, previous, inside);
            track = string.IsNullOrEmpty(track) ? null : track;

            if (!previous.Started || ctx.InMainMenu || previous.ZoneKey == MenuZone || track == previous.TrackId)
                return new MusicSelectorState(track, zone, track, false, null, 0.0, null, true, inside);

            double pending = previous.HasPending && previous.PendingTrackId == track ? previous.PendingSeconds + Math.Max(0.0, realSeconds) : 0.0;
            if (pending >= library.Selector.MinDwellSeconds)
                return new MusicSelectorState(track, zone, track, false, null, 0.0, null, true, inside);
            return new MusicSelectorState(previous.TrackId, previous.ZoneKey, track, true, track, pending, zone, true, inside);
        }
    }

    /// <summary>Equal-power crossfade: g_out = cos(πt/2), g_in = sin(πt/2), so g_out² + g_in² = 1 (constant loudness).</summary>
    public static class MusicCrossfade
    {
        public static void Gains(double t, out double fadingOut, out double fadingIn)
        {
            t = t < 0.0 ? 0.0 : t > 1.0 ? 1.0 : t;
            fadingOut = Math.Cos(0.5 * Math.PI * t);
            fadingIn = Math.Sin(0.5 * Math.PI * t);
        }
    }
}
