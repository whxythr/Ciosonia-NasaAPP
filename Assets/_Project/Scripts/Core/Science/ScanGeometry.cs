using System;

namespace SpaceSim.Science
{
    /// <summary>
    /// Geometry of a nadir-pointing conical field of view over a spherical body of radius R, seen from altitude h.
    ///
    /// A ray leaving the instrument at angle θ from nadir meets the sphere where the law of sines in the triangle
    /// (centre, instrument, ground point) gives sin(θ + λ) = (R + h)/R · sin θ, so the footprint's central angle (angular radius
    /// on the body) is λ = asin((R + h)/R · sin θ) − θ. When (R + h)/R · sin θ ≥ 1 the cone reaches past the limb and the
    /// footprint is the whole visible cap, λ = acos(R / (R + h)). For h ≪ R this reduces to λ ≈ h·tan θ / R.
    /// </summary>
    public static class ScanGeometry
    {
        /// <summary>Central angle of the horizon seen from altitude h [rad]: acos(R/(R+h)).</summary>
        public static double HorizonAngle(double radius, double altitude)
        {
            if (!(radius > 0.0) || !(altitude > 0.0)) return 0.0;
            return Math.Acos(radius / (radius + altitude));
        }

        /// <summary>Angular radius of the footprint on the body (central angle) [rad].</summary>
        public static double FootprintAngle(double radius, double altitude, double halfAngle)
        {
            if (!(radius > 0.0) || !(altitude > 0.0) || !(halfAngle > 0.0)) return 0.0;
            double horizon = HorizonAngle(radius, altitude);
            double k = (radius + altitude) / radius * Math.Sin(halfAngle);
            if (k >= 1.0) return horizon;
            return Math.Min(horizon, Math.Asin(k) - halfAngle);
        }

        /// <summary>Footprint radius measured along the surface [m] = R·λ.</summary>
        public static double FootprintRadius(double radius, double altitude, double halfAngle) =>
            radius * FootprintAngle(radius, altitude, halfAngle);

        /// <summary>Ground size of one detector element at nadir [m] = h·IFOV.</summary>
        public static double GroundSampleDistance(double altitude, double ifov) => Math.Max(0.0, altitude) * Math.Max(0.0, ifov);

        /// <summary>Great-circle angle between two points given by latitude/longitude [rad].</summary>
        public static double CentralAngle(double lat1, double lon1, double lat2, double lon2)
        {
            double c = Math.Sin(lat1) * Math.Sin(lat2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Cos(lon2 - lon1);
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, c)));
        }
    }

    /// <summary>
    /// Equirectangular latitude/longitude grid. Row 0 is the southernmost band (−90° …), column 0 starts at −180° longitude.
    /// Cell index = row·Columns + column.
    /// </summary>
    public sealed class LatLonGrid
    {
        /// <summary>The grid all science maps use: 2° cells (180 × 90). Saved maps record it; a different grid is not loaded.</summary>
        public const double DefaultCellDegrees = 2.0;

        public static readonly LatLonGrid Default = new LatLonGrid(DefaultCellDegrees);

        public LatLonGrid(double cellDegrees)
        {
            if (!(cellDegrees > 0.0) || 180.0 % cellDegrees > 1e-9) throw new ArgumentException("Cell size must divide 180°.", nameof(cellDegrees));
            CellDegrees = cellDegrees;
            Rows = (int)Math.Round(180.0 / cellDegrees);
            Columns = 2 * Rows;
            CellAngle = cellDegrees * Math.PI / 180.0;
            weights = new double[Rows];
            double total = 0.0;
            for (int r = 0; r < Rows; r++)
            {
                double south = (-90.0 + r * cellDegrees) * Math.PI / 180.0, north = south + CellAngle;
                weights[r] = (Math.Sin(north) - Math.Sin(south)) / (2.0 * Columns); // share of the sphere's area
                total += weights[r] * Columns;
            }
            for (int r = 0; r < Rows; r++) weights[r] /= total;
        }

        readonly double[] weights;

        public double CellDegrees { get; }
        /// <summary>Cell size [rad].</summary>
        public double CellAngle { get; }
        public int Rows { get; }
        public int Columns { get; }
        public int CellCount => Rows * Columns;

        /// <summary>Share of the body's surface area in one cell of row <paramref name="row"/> (all cells sum to 1).</summary>
        public double AreaWeight(int row) => weights[row];

        public int Index(int row, int column) => row * Columns + column;
        public int RowOf(int index) => index / Columns;
        public int ColumnOf(int index) => index % Columns;

        public int Row(double latitude)
        {
            int r = (int)Math.Floor((latitude * 180.0 / Math.PI + 90.0) / CellDegrees);
            return r < 0 ? 0 : (r >= Rows ? Rows - 1 : r);
        }

        public int Column(double longitude)
        {
            double deg = longitude * 180.0 / Math.PI + 180.0;
            deg %= 360.0;
            if (deg < 0.0) deg += 360.0;
            int c = (int)Math.Floor(deg / CellDegrees);
            return c >= Columns ? Columns - 1 : c;
        }

        /// <summary>Cell containing the point [rad].</summary>
        public int CellOf(double latitude, double longitude) => Index(Row(latitude), Column(longitude));

        /// <summary>Latitude of a row's centre [rad].</summary>
        public double CenterLatitude(int row) => (-90.0 + (row + 0.5) * CellDegrees) * Math.PI / 180.0;

        /// <summary>Longitude of a column's centre [rad], in (−π, π).</summary>
        public double CenterLongitude(int column) => (-180.0 + (column + 0.5) * CellDegrees) * Math.PI / 180.0;

        /// <summary>Width of one cell at the equator on a body of radius <paramref name="radius"/> [m].</summary>
        public double CellSize(double radius) => radius * CellAngle;
    }
}
