using System;
using System.Collections.Generic;

namespace SpaceSim.Science
{
    /// <summary>
    /// One layer of one body: which cells were measured (bitset) and the measured values. A cell enters <see cref="Pending"/>
    /// when an instrument's footprint covers it and becomes covered once its value has been measured (a per-update budget
    /// keeps a wide footprint under fast time warp from stalling a frame).
    /// </summary>
    public sealed class ScienceLayerMap
    {
        readonly ulong[] covered;
        readonly ulong[] pendingBits;
        readonly Queue<int> pending = new Queue<int>();
        readonly float[][] channels;

        public ScienceLayerMap(ScienceLayer layer, LatLonGrid grid)
        {
            Layer = layer;
            Grid = grid ?? LatLonGrid.Default;
            int words = (Grid.CellCount + 63) / 64;
            covered = new ulong[words];
            pendingBits = new ulong[words];
            channels = new float[ScienceLayers.Channels(layer)][];
            for (int c = 0; c < channels.Length; c++) channels[c] = new float[Grid.CellCount];
        }

        public ScienceLayer Layer { get; }
        public LatLonGrid Grid { get; }
        public int ChannelCount => channels.Length;
        /// <summary>Number of measured cells.</summary>
        public int CoveredCount { get; private set; }
        /// <summary>Measured share of the body's surface area (0..1, cells weighted by their area).</summary>
        public double Coverage { get; private set; }
        /// <summary>Cells seen by an instrument whose value is still being measured.</summary>
        public int PendingCount => pending.Count;
        /// <summary>Lowest and highest measured value of channel 0 (NaN while nothing is measured).</summary>
        public double Min { get; private set; } = double.NaN;
        public double Max { get; private set; } = double.NaN;

        public bool IsCovered(int cell) => (covered[cell >> 6] & (1UL << (cell & 63))) != 0;
        public bool IsPending(int cell) => (pendingBits[cell >> 6] & (1UL << (cell & 63))) != 0;
        public float Value(int cell, int channel = 0) => channels[channel][cell];

        /// <summary>The cell was seen: queue it for measurement unless it is measured or queued already. True if newly queued.</summary>
        public bool Request(int cell)
        {
            if (IsCovered(cell) || IsPending(cell)) return false;
            pendingBits[cell >> 6] |= 1UL << (cell & 63);
            pending.Enqueue(cell);
            return true;
        }

        public bool TryDequeue(out int cell)
        {
            while (pending.Count > 0)
            {
                cell = pending.Dequeue();
                pendingBits[cell >> 6] &= ~(1UL << (cell & 63));
                if (!IsCovered(cell)) return true;
            }
            cell = -1;
            return false;
        }

        /// <summary>Records the measured values of a cell (first measurement wins; the values are deterministic anyway).</summary>
        public bool Store(int cell, float[] values)
        {
            if (IsCovered(cell)) return false;
            for (int c = 0; c < channels.Length; c++) channels[c][cell] = values[c];
            covered[cell >> 6] |= 1UL << (cell & 63);
            CoveredCount++;
            Coverage += Grid.AreaWeight(Grid.RowOf(cell));
            double v = values[0];
            if (double.IsNaN(Min) || v < Min) Min = v;
            if (double.IsNaN(Max) || v > Max) Max = v;
            return true;
        }

        // ---- persistence access ----
        internal ulong[] CoveredBits => covered;
        internal float[] Channel(int c) => channels[c];
    }

    /// <summary>Everything mapped on one body.</summary>
    public sealed class ScienceBodyMap
    {
        readonly ScienceLayerMap[] layers = new ScienceLayerMap[ScienceLayers.Count];

        public ScienceBodyMap(string bodyId, string displayName, LatLonGrid grid = null)
        {
            BodyId = bodyId ?? "";
            DisplayName = displayName ?? "";
            Grid = grid ?? LatLonGrid.Default;
            foreach (ScienceLayer l in ScienceLayers.All) layers[(int)l] = new ScienceLayerMap(l, Grid);
        }

        public string BodyId { get; }
        public string DisplayName { get; set; }
        public LatLonGrid Grid { get; }
        /// <summary>Body radius [m] when known (for the UI); 0 = unknown.</summary>
        public double Radius { get; set; }
        /// <summary>The body had no atmosphere when it was mapped (pressure readings are vacuum, not "missing").</summary>
        public bool Airless { get; set; }

        public ScienceLayerMap this[ScienceLayer layer] => layers[(int)layer];

        /// <summary>Area share covered by at least one layer.</summary>
        public double AnyCoverage
        {
            get
            {
                double sum = 0.0;
                for (int i = 0; i < Grid.CellCount; i++)
                    if (IsAnyCovered(i)) sum += Grid.AreaWeight(Grid.RowOf(i));
                return sum;
            }
        }

        public bool IsAnyCovered(int cell)
        {
            foreach (ScienceLayerMap l in layers) if (l.IsCovered(cell)) return true;
            return false;
        }

        public int PendingCount
        {
            get
            {
                int n = 0;
                foreach (ScienceLayerMap l in layers) n += l.PendingCount;
                return n;
            }
        }
    }

    /// <summary>The science maps of every body the player has scanned.</summary>
    public sealed class ScienceMapStore
    {
        readonly Dictionary<string, ScienceBodyMap> bodies = new Dictionary<string, ScienceBodyMap>();
        readonly List<ScienceBodyMap> ordered = new List<ScienceBodyMap>();

        /// <summary>Increases whenever any cell value is stored (the map window redraws its texture then).</summary>
        public int Version { get; private set; }
        /// <summary>Set by every change, cleared by whoever persisted the store.</summary>
        public bool Dirty { get; set; }

        public IReadOnlyList<ScienceBodyMap> Bodies => ordered;

        public bool TryGet(string bodyId, out ScienceBodyMap map) => bodies.TryGetValue(bodyId ?? "", out map);

        public ScienceBodyMap GetOrCreate(string bodyId, string displayName)
        {
            if (bodies.TryGetValue(bodyId ?? "", out ScienceBodyMap m))
            {
                if (!string.IsNullOrEmpty(displayName)) m.DisplayName = displayName;
                return m;
            }
            m = new ScienceBodyMap(bodyId, displayName);
            Add(m);
            return m;
        }

        internal void Add(ScienceBodyMap map)
        {
            bodies[map.BodyId] = map;
            ordered.RemoveAll(b => b.BodyId == map.BodyId);
            ordered.Add(map);
        }

        internal void Touch()
        {
            Version++;
            Dirty = true;
        }
    }
}
