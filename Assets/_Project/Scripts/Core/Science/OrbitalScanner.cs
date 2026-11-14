using System;
using System.Collections.Generic;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Science
{
    /// <summary>What one scanning update did (for the HUD and the map window).</summary>
    public sealed class ScanReport
    {
        /// <summary>Sub-satellite point at the end of the update [rad] (planet-fixed: the planet's rotation is accounted for).</summary>
        public double Latitude, Longitude;
        /// <summary>Altitude above the reference radius at the end of the update [m].</summary>
        public double Altitude;
        /// <summary>Footprint angular radius of each layer's best working instrument [rad] (0 = not scanning).</summary>
        public readonly double[] FootprintAngle = new double[ScienceLayers.Count];
        /// <summary>Status of every instrument, same order as the scanner list passed in.</summary>
        public readonly List<ScannerStatus> Status = new List<ScannerStatus>();
        /// <summary>Ground-track samples taken between the two states.</summary>
        public int SubSteps;
        /// <summary>Cells newly seen (queued for measurement) / measured during this update.</summary>
        public int Queued, Measured;
        /// <summary>The sampling was capped (extreme time warp): the ground track may have gaps.</summary>
        public bool Capped;

        public bool AnyScanning
        {
            get
            {
                foreach (ScannerStatus s in Status) if (s == ScannerStatus.Scanning || s == ScannerStatus.Landed) return true;
                return false;
            }
        }

        internal void Reset()
        {
            Latitude = Longitude = Altitude = 0.0;
            for (int i = 0; i < FootprintAngle.Length; i++) FootprintAngle[i] = 0.0;
            Status.Clear();
            SubSteps = Queued = Measured = 0;
            Capped = false;
        }
    }

    /// <summary>
    /// Orbital mapping. Every update covers the interval between the previous and the current vehicle state: the ground track
    /// is re-sampled in between on the two-body conic through the previous state (exact while coasting, which is all time warp
    /// allows), so fast warp leaves no gaps. Each sample is turned into the body-fixed frame at its own time — the planet turns
    /// under the orbit — and every working instrument marks the cells of the lat/lon grid inside its footprint. Their values are
    /// then measured at the cell centres from the planet model (<see cref="PlanetScienceModel"/>), within a per-update budget.
    /// </summary>
    public sealed class OrbitalScanner
    {
        /// <summary>Most ground-track samples per update.</summary>
        public int MaxSubSteps = 720;
        /// <summary>Most cell values measured per update (the rest wait in the queue for the next update).</summary>
        public int MeasureBudget = 3000;
        /// <summary>Samples are at most this share of the smallest working footprint apart along the track.</summary>
        public double SampleSpacing = 0.5;

        readonly float[] buffer = new float[3];
        readonly ScanReport report = new ScanReport();

        /// <summary>
        /// Scans from state (r0, v0, t0) to (r1, t1). Positions are relative to the body's centre in the inertial frame [m, m/s, s].
        /// <paramref name="layerOn"/> (optional) switches layers off; <paramref name="powered"/> = false stops every instrument.
        /// </summary>
        public ScanReport Scan(ScienceMapStore store, ScienceBodyMap map, PlanetScienceModel env, IReadOnlyList<ScannerSpec> scanners,
            Vector3d r0, Vector3d v0, double t0, Vector3d r1, double t1, bool powered = true, bool[] layerOn = null)
        {
            report.Reset();
            if (map == null || env == null || scanners == null) return report;
            PlanetModel planet = env.Model;
            double radius = planet.Radius, mu = planet.GravitationalParameter;
            double cellSize = map.Grid.CellSize(radius);

            Vector3d fixedEnd = planet.Rotation.InertialToFixed(r1, t1);
            PlanetRotation.LatLonFromDirection(fixedEnd, out report.Latitude, out report.Longitude);
            report.Altitude = r1.Magnitude - radius;

            // Which instruments work at all, and the smallest footprint among them (sets the sampling density).
            double smallest = double.PositiveInfinity;
            for (int i = 0; i < scanners.Count; i++)
            {
                ScannerSpec s = scanners[i];
                ScannerStatus st = !powered ? ScannerStatus.NoPower
                    : layerOn != null && !layerOn[(int)s.Layer] ? ScannerStatus.Off
                    : s.Check(report.Altitude, cellSize);
                report.Status.Add(st);
                if (st == ScannerStatus.Scanning)
                {
                    double a = ScanGeometry.FootprintAngle(radius, report.Altitude, s.HalfAngle);
                    smallest = Math.Min(smallest, a);
                    report.FootprintAngle[(int)s.Layer] = Math.Max(report.FootprintAngle[(int)s.Layer], a);
                }
            }

            bool anyUsable = powered;
            if (anyUsable)
            {
                anyUsable = false;
                for (int i = 0; i < scanners.Count; i++)
                    if (report.Status[i] != ScannerStatus.Off) anyUsable = true;
            }

            if (anyUsable)
            {
                double dt = t1 - t0;
                int n = 1;
                if (dt > 0.0 && r0.Magnitude > 0.0)
                {
                    // Upper bound of the ground-track angular rate: orbital rate + the planet's spin.
                    double rate = v0.Magnitude / r0.Magnitude + Math.Abs(planet.Rotation.AngularSpeed);
                    double step = SampleSpacing * Math.Max(double.IsInfinity(smallest) ? 0.0 : smallest, map.Grid.CellAngle);
                    double wanted = Math.Ceiling(rate * dt / step);
                    if (wanted > MaxSubSteps)
                    {
                        report.Capped = true;
                        wanted = MaxSubSteps;
                    }
                    n = Math.Max(1, (int)wanted);
                }
                bool conic = mu > 0.0 && dt > 0.0 && v0.IsFinite && r0.Magnitude > radius * 0.5;
                for (int k = 1; k <= n; k++)
                {
                    double t = k == n || dt <= 0.0 ? t1 : t0 + dt * k / n;
                    Vector3d r;
                    if (k == n || dt <= 0.0) r = r1;
                    else if (conic) ConicPropagator.Propagate(mu, r0, v0, t - t0, out r, out _);
                    else r = Vector3d.Lerp(r0, r1, (double)k / n);
                    SampleAt(map, planet, scanners, r, t, cellSize, powered, layerOn);
                }
                report.SubSteps = n;
            }

            report.Measured = ProcessPending(store, map, env, MeasureBudget);
            return report;
        }

        void SampleAt(ScienceBodyMap map, PlanetModel planet, IReadOnlyList<ScannerSpec> scanners, Vector3d r, double t, double cellSize,
            bool powered, bool[] layerOn)
        {
            if (!r.IsFinite) return;
            double altitude = r.Magnitude - planet.Radius;
            Vector3d f = planet.Rotation.InertialToFixed(r, t);
            PlanetRotation.LatLonFromDirection(f, out double lat, out double lon);
            for (int i = 0; i < scanners.Count; i++)
            {
                ScannerSpec s = scanners[i];
                if (!powered || (layerOn != null && !layerOn[(int)s.Layer])) continue;
                if (s.Check(altitude, cellSize) != ScannerStatus.Scanning) continue;
                double angle = ScanGeometry.FootprintAngle(planet.Radius, altitude, s.HalfAngle);
                report.Queued += MarkFootprint(map[s.Layer], lat, lon, angle);
            }
        }

        /// <summary>
        /// Queues every cell whose centre lies within central angle <paramref name="angle"/> of (lat0, lon0), plus the cell under
        /// the point itself (a footprint smaller than one cell still samples that cell). Returns the number of newly queued cells.
        /// </summary>
        public static int MarkFootprint(ScienceLayerMap layer, double lat0, double lon0, double angle)
        {
            LatLonGrid g = layer.Grid;
            int queued = layer.Request(g.CellOf(lat0, lon0)) ? 1 : 0;
            if (!(angle > 0.0)) return queued;
            double halfPi = 0.5 * Math.PI;
            int rowFirst = g.Row(Math.Max(-halfPi, lat0 - angle)), rowLast = g.Row(Math.Min(halfPi, lat0 + angle));
            double cosA = Math.Cos(angle), s0 = Math.Sin(lat0), c0 = Math.Cos(lat0), cell = g.CellAngle;
            for (int row = rowFirst; row <= rowLast; row++)
            {
                double phi = g.CenterLatitude(row);
                double num = cosA - Math.Sin(phi) * s0, den = Math.Cos(phi) * c0;
                double half;
                if (den < 1e-12)
                {
                    if (num > 0.0) continue; // at a pole the distance depends on latitude only
                    half = Math.PI;
                }
                else
                {
                    double c = num / den;
                    if (c > 1.0) continue;
                    half = c <= -1.0 ? Math.PI : Math.Acos(c);
                }
                int first = (int)Math.Ceiling((lon0 - half + Math.PI) / cell - 0.5);
                int last = (int)Math.Floor((lon0 + half + Math.PI) / cell - 0.5);
                if (half >= Math.PI - 1e-12 || last - first + 1 >= g.Columns)
                {
                    first = 0;
                    last = g.Columns - 1;
                }
                for (int col = first; col <= last; col++)
                {
                    int c = ((col % g.Columns) + g.Columns) % g.Columns;
                    if (layer.Request(g.Index(row, c))) queued++;
                }
            }
            return queued;
        }

        /// <summary>Measures queued cells (at most <paramref name="budget"/>), spreading the budget over the layers. Returns how many.</summary>
        public int ProcessPending(ScienceMapStore store, ScienceBodyMap map, PlanetScienceModel env, int budget)
        {
            int done = 0;
            bool progress = true;
            while (done < budget && progress)
            {
                progress = false;
                foreach (ScienceLayer l in ScienceLayers.All)
                {
                    ScienceLayerMap layer = map[l];
                    if (!layer.TryDequeue(out int cell)) continue;
                    MeasureCell(layer, env, cell);
                    done++;
                    progress = true;
                    if (done >= budget) break;
                }
            }
            if (done > 0) store?.Touch();
            return done;
        }

        void MeasureCell(ScienceLayerMap layer, PlanetScienceModel env, int cell)
        {
            LatLonGrid g = layer.Grid;
            Vector3d dir = PlanetRotation.DirectionFromLatLon(g.CenterLatitude(g.RowOf(cell)), g.CenterLongitude(g.ColumnOf(cell)));
            env.Measure(layer.Layer, dir, buffer);
            layer.Store(cell, buffer);
        }

        /// <summary>
        /// In-situ measurement by a landed instrument (thermometer, barometer, camera…): the cell under the vehicle, measured at once.
        /// <paramref name="fixedPosition"/> is planet-fixed. True if the cell was new.
        /// </summary>
        public bool MeasureInSitu(ScienceMapStore store, ScienceBodyMap map, PlanetScienceModel env, ScienceLayer layer, Vector3d fixedPosition)
        {
            if (map == null || env == null || fixedPosition.SqrMagnitude <= 0.0) return false;
            PlanetRotation.LatLonFromDirection(fixedPosition, out double lat, out double lon);
            ScienceLayerMap l = map[layer];
            int cell = l.Grid.CellOf(lat, lon);
            if (l.IsCovered(cell)) return false;
            MeasureCell(l, env, cell);
            store?.Touch();
            return true;
        }
    }
}
