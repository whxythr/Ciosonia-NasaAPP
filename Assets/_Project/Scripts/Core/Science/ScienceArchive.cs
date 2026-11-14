using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace SpaceSim.Science
{
    /// <summary>
    /// Saved form of the science maps, part of the player profile (JSON). Additive: profiles written before it simply have none.
    /// Each layer stores its covered-cell bitset and its values, both deflated and Base64-encoded (see <see cref="ScienceArchiveCodec"/>).
    /// </summary>
    [Serializable]
    public class ScienceArchive
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public List<ScienceBodyRecord> bodies = new List<ScienceBodyRecord>();
    }

    [Serializable]
    public class ScienceBodyRecord
    {
        public string bodyId = "";
        public string displayName = "";
        public double radius;
        public bool airless;
        /// <summary>Grid cell size [deg] the record was written with.</summary>
        public double cellDegrees = LatLonGrid.DefaultCellDegrees;
        public List<ScienceLayerRecord> layers = new List<ScienceLayerRecord>();
    }

    [Serializable]
    public class ScienceLayerRecord
    {
        /// <summary><see cref="ScienceLayer"/> as int.</summary>
        public int layer;
        public int coveredCells;
        /// <summary>Deflate + Base64 of the covered-cell bitset (64 cells per little-endian word).</summary>
        public string coverage = "";
        /// <summary>Deflate + Base64 of the quantised channels (16-bit codes, delta-coded along the cell index).</summary>
        public string values = "";
    }

    /// <summary>
    /// Converts <see cref="ScienceMapStore"/> ⇄ <see cref="ScienceArchive"/>. Values are quantised to 16 bits:
    /// temperature 0.1 K; pressure logarithmic, 1/4000 decade (≈ 0.06 %) from 10⁻⁴ Pa, code 0 = vacuum; elevation 2 m around 0
    /// (±65 km); albedo 10⁻⁴; surface code as is. Records that cannot be decoded, or use another grid, are skipped (never thrown).
    /// </summary>
    public static class ScienceArchiveCodec
    {
        public static ScienceArchive Write(ScienceMapStore store)
        {
            var archive = new ScienceArchive();
            if (store == null) return archive;
            foreach (ScienceBodyMap map in store.Bodies)
            {
                var body = new ScienceBodyRecord
                {
                    bodyId = map.BodyId, displayName = map.DisplayName, radius = map.Radius, airless = map.Airless,
                    cellDegrees = map.Grid.CellDegrees
                };
                foreach (ScienceLayer l in ScienceLayers.All)
                {
                    ScienceLayerMap layer = map[l];
                    if (layer.CoveredCount == 0) continue;
                    body.layers.Add(new ScienceLayerRecord
                    {
                        layer = (int)l,
                        coveredCells = layer.CoveredCount,
                        coverage = Pack(BitsToBytes(layer.CoveredBits)),
                        values = Pack(EncodeChannels(layer))
                    });
                }
                archive.bodies.Add(body);
            }
            return archive;
        }

        public static ScienceMapStore Read(ScienceArchive archive)
        {
            var store = new ScienceMapStore();
            if (archive == null || archive.bodies == null || archive.version > ScienceArchive.CurrentVersion) return store;
            foreach (ScienceBodyRecord rec in archive.bodies)
            {
                if (rec == null || string.IsNullOrEmpty(rec.bodyId)) continue;
                if (Math.Abs(rec.cellDegrees - LatLonGrid.DefaultCellDegrees) > 1e-9) continue; // another grid: not this build's map
                var map = new ScienceBodyMap(rec.bodyId, rec.displayName) { Radius = rec.radius, Airless = rec.airless };
                if (rec.layers != null)
                    foreach (ScienceLayerRecord lr in rec.layers)
                    {
                        if (lr == null || lr.layer < 0 || lr.layer >= ScienceLayers.Count) continue;
                        try
                        {
                            Restore(map[(ScienceLayer)lr.layer], Unpack(lr.coverage), Unpack(lr.values));
                        }
                        catch (Exception)
                        {
                            // A damaged layer is dropped; the rest of the save stays usable.
                        }
                    }
                store.Add(map);
            }
            store.Dirty = false;
            return store;
        }

        static void Restore(ScienceLayerMap layer, byte[] bits, byte[] values)
        {
            int cells = layer.Grid.CellCount, channels = layer.ChannelCount;
            if (bits.Length < (cells + 63) / 64 * 8 || values.Length < cells * channels * 2) throw new InvalidDataException("short layer");
            var buffer = new float[channels];
            var codes = new ushort[channels][];
            for (int c = 0; c < channels; c++)
            {
                codes[c] = new ushort[cells];
                ushort prev = 0;
                for (int i = 0; i < cells; i++)
                {
                    int o = (c * cells + i) * 2;
                    prev = (ushort)(prev + (values[o] | (values[o + 1] << 8)));
                    codes[c][i] = prev;
                }
            }
            for (int i = 0; i < cells; i++)
            {
                if ((bits[i >> 3] & (1 << (i & 7))) == 0) continue;
                for (int c = 0; c < channels; c++) buffer[c] = Decode(layer.Layer, c, codes[c][i]);
                layer.Store(i, buffer);
            }
        }

        static byte[] EncodeChannels(ScienceLayerMap layer)
        {
            int cells = layer.Grid.CellCount, channels = layer.ChannelCount;
            var bytes = new byte[cells * channels * 2];
            for (int c = 0; c < channels; c++)
            {
                float[] v = layer.Channel(c);
                ushort prev = 0;
                for (int i = 0; i < cells; i++)
                {
                    ushort code = layer.IsCovered(i) ? Encode(layer.Layer, c, v[i]) : (ushort)0;
                    ushort delta = (ushort)(code - prev);
                    prev = code;
                    int o = (c * cells + i) * 2;
                    bytes[o] = (byte)delta;
                    bytes[o + 1] = (byte)(delta >> 8);
                }
            }
            return bytes;
        }

        const double PressureFloorLog = -4.0, PressureStepsPerDecade = 4000.0;

        public static ushort Encode(ScienceLayer layer, int channel, double v)
        {
            double code;
            switch (layer)
            {
                case ScienceLayer.Temperature: code = Math.Round(v * 10.0); break;
                case ScienceLayer.Pressure:
                    if (!(v > 0.0)) return 0;
                    code = 1.0 + Math.Round((Math.Log10(v) - PressureFloorLog) * PressureStepsPerDecade);
                    if (code < 1.0) code = 1.0;
                    break;
                default:
                    if (channel == ScienceLayers.ElevationChannel) code = Math.Round(v * 0.5) + 32768.0;
                    else if (channel == ScienceLayers.AlbedoChannel) code = Math.Round(v * 10000.0);
                    else code = Math.Round(v);
                    break;
            }
            if (double.IsNaN(code)) return 0;
            return (ushort)Math.Max(0.0, Math.Min(65535.0, code));
        }

        public static float Decode(ScienceLayer layer, int channel, ushort code)
        {
            switch (layer)
            {
                case ScienceLayer.Temperature: return code * 0.1f;
                case ScienceLayer.Pressure:
                    return code == 0 ? 0f : (float)Math.Pow(10.0, (code - 1) / PressureStepsPerDecade + PressureFloorLog);
                default:
                    if (channel == ScienceLayers.ElevationChannel) return (code - 32768) * 2f;
                    if (channel == ScienceLayers.AlbedoChannel) return code * 1e-4f;
                    return code;
            }
        }

        static byte[] BitsToBytes(ulong[] words)
        {
            var b = new byte[words.Length * 8];
            for (int i = 0; i < words.Length; i++)
                for (int k = 0; k < 8; k++) b[i * 8 + k] = (byte)(words[i] >> (8 * k));
            return b;
        }

        static string Pack(byte[] raw)
        {
            using (var ms = new MemoryStream())
            {
                using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        static byte[] Unpack(string text)
        {
            if (string.IsNullOrEmpty(text)) throw new InvalidDataException("empty");
            using (var input = new MemoryStream(Convert.FromBase64String(text)))
            using (var z = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                z.CopyTo(output);
                return output.ToArray();
            }
        }
    }
}
