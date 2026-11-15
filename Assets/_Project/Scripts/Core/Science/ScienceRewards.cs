using System;
using System.Collections.Generic;
using SpaceSim.Economy;
using SpaceSim.Localization;

namespace SpaceSim.Science
{
    /// <summary>How much of one body's layer has already been paid for (part of the player profile; coverage only grows).</summary>
    [Serializable]
    public class ScienceRewardRecord
    {
        public string bodyId = "";
        /// <summary><see cref="ScienceLayer"/> as int.</summary>
        public int layer;
        /// <summary>Whole percent of the body's surface area (area-weighted coverage) already rewarded, 0..100.</summary>
        public int rewardedPercent;
    }

    /// <summary>What one <see cref="ScienceRewards.Collect"/> call paid.</summary>
    public struct ScienceRewardPayout
    {
        public int Credits;
        public double Research;
        /// <summary>Newly rewarded percent steps over all bodies and layers (also counts forfeited steps).</summary>
        public int Percent;
        /// <summary>The ledger reason ("" when nothing was paid).</summary>
        public string Reason;
        public bool Paid => Credits > 0 || Research > 0.0;
    }

    /// <summary>
    /// Career income from orbital mapping: research points and credits for every newly mapped whole percent of a body's surface,
    /// per layer, paid once. The paid share of each body/layer is kept in <see cref="PlayerProfile.scienceRewards"/> and saved
    /// together with the maps, so reloading a save never pays the same coverage twice. Sandbox earns nothing (and records
    /// nothing). Coverage gathered on a flight that used a test tool (initial conditions, an instant manoeuvre) is marked as paid
    /// without paying (<c>forfeit</c>), so a later real flight cannot cash it in either.
    /// Data-driven: <see cref="Table"/> per layer, <see cref="HomeBodyFactor"/> for the easy home planet; airless pressure maps pay nothing.
    /// </summary>
    public static class ScienceRewards
    {
        /// <summary>Payment for mapping 100 % of one layer of a body.</summary>
        public sealed class LayerRate
        {
            public double ResearchPerFullMap;
            public int CreditsPerFullMap;
        }

        /// <summary>Rates by <see cref="ScienceLayer"/> (index = layer). A full map ≈ the research of one technology tier.</summary>
        public static readonly LayerRate[] Table =
        {
            new LayerRate { ResearchPerFullMap = 15.0, CreditsPerFullMap = 2000 }, // temperature
            new LayerRate { ResearchPerFullMap = 15.0, CreditsPerFullMap = 2000 }, // pressure
            new LayerRate { ResearchPerFullMap = 25.0, CreditsPerFullMap = 4000 }, // terrain
        };

        /// <summary>The home planet is next door and well known: half pay.</summary>
        public const double HomeBodyFactor = 0.5;

        /// <summary>Whole percent of area-weighted coverage, 0..100.</summary>
        public static int PercentOf(double coverage)
        {
            if (!(coverage > 0.0)) return 0;
            return Math.Max(0, Math.Min(100, (int)Math.Floor(coverage * 100.0 + 1e-6)));
        }

        /// <summary>Multiplier of a body (0 for a pressure map of an airless body: vacuum readings are no discovery).</summary>
        public static double Factor(ScienceBodyMap map, ScienceLayer layer, string homeBodyId)
        {
            if (layer == ScienceLayer.Pressure && map.Airless) return 0.0;
            return !string.IsNullOrEmpty(homeBodyId) && map.BodyId == homeBodyId ? HomeBodyFactor : 1.0;
        }

        /// <summary>
        /// Pays the coverage gained since the last call (Career only) into <paramref name="profile"/> as one ledger line, and
        /// advances the paid shares. With <paramref name="forfeit"/> the shares advance without payment.
        /// </summary>
        public static ScienceRewardPayout Collect(PlayerProfile profile, ScienceMapStore store, string homeBodyId, bool forfeit = false)
        {
            var payout = new ScienceRewardPayout { Reason = "" };
            if (profile == null || store == null || !profile.IsCareer) return payout;
            if (profile.scienceRewards == null) profile.scienceRewards = new List<ScienceRewardRecord>();
            double research = 0.0, credits = 0.0;
            var names = new List<string>();
            foreach (ScienceBodyMap map in store.Bodies)
            {
                bool bodyPaid = false;
                foreach (ScienceLayer layer in ScienceLayers.All)
                {
                    int percent = PercentOf(map[layer].Coverage);
                    if (percent <= 0) continue;
                    ScienceRewardRecord rec = Find(profile.scienceRewards, map.BodyId, layer);
                    int already = rec != null ? rec.rewardedPercent : 0;
                    if (percent <= already) continue;
                    if (rec == null)
                    {
                        rec = new ScienceRewardRecord { bodyId = map.BodyId, layer = (int)layer };
                        profile.scienceRewards.Add(rec);
                    }
                    rec.rewardedPercent = percent;
                    int steps = percent - already;
                    payout.Percent += steps;
                    if (forfeit) continue;
                    LayerRate rate = Table[(int)layer];
                    double f = Factor(map, layer, homeBodyId) * steps / 100.0;
                    if (f <= 0.0) continue;
                    research += rate.ResearchPerFullMap * f;
                    credits += rate.CreditsPerFullMap * f;
                    bodyPaid = true;
                }
                if (bodyPaid) names.Add(Loc.T(map.DisplayName.Length > 0 ? map.DisplayName : map.BodyId));
            }
            if (forfeit || names.Count == 0) return payout;
            payout.Credits = (int)Math.Round(credits);
            payout.Research = research;
            payout.Reason = Loc.T("Орбитальное картирование: ") + string.Join(", ", names);
            if (!profile.Award(payout.Credits, payout.Research, payout.Reason))
            {
                payout.Credits = 0;
                payout.Research = 0.0;
                payout.Reason = "";
            }
            return payout;
        }

        static ScienceRewardRecord Find(List<ScienceRewardRecord> records, string bodyId, ScienceLayer layer)
        {
            foreach (ScienceRewardRecord r in records)
                if (r != null && r.bodyId == bodyId && r.layer == (int)layer) return r;
            return null;
        }
    }
}
