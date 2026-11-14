using System;
using System.Collections.Generic;
using SpaceSim.Physics;

namespace SpaceSim.Audio
{
    /// <summary>What the music selector needs to know about one body (fixed for a star system).</summary>
    public sealed class MusicBody
    {
        public string Id = "";
        /// <summary>Index of the parent in <see cref="MusicWorld.Bodies"/>; −1 = the star.</summary>
        public int ParentIndex = -1;
        public double Radius;
        public double SoiRadius;
        /// <summary>Centre distance below which the vehicle is "at the surface or in the atmosphere" [m].</summary>
        public double NearSurfaceRadius;
        /// <summary>Semi-major axis of the body's own orbit around its parent [m].</summary>
        public double SemiMajorAxis;
    }

    /// <summary>
    /// The geometry of the music zones for one star system: bodies, SOI radii, the star region and the resolved radius of every
    /// proximity rule. Built once from the navigation model (<see cref="FromSystem"/>) or by hand in tests.
    /// </summary>
    public sealed class MusicWorld
    {
        public readonly List<MusicBody> Bodies = new List<MusicBody>();
        /// <summary>Heliocentric radius of the star region [m].</summary>
        public double StarRegionRadius;
        /// <summary>Resolved radius of each proximity rule by rule id [m] (absent or NaN = rule inactive).</summary>
        public readonly Dictionary<string, double> ProximityRadius = new Dictionary<string, double>();
        /// <summary>Human-readable notes: rules whose bodies are not in this system, fallbacks used.</summary>
        public readonly List<string> Notes = new List<string>();

        public int IndexOf(string id)
        {
            for (int i = 0; i < Bodies.Count; i++)
                if (string.Equals(Bodies[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        public int IndexOfAny(string[] ids)
        {
            if (ids == null) return -1;
            foreach (string id in ids)
            {
                int i = IndexOf(id);
                if (i >= 0) return i;
            }
            return -1;
        }

        /// <summary>True when rule <paramref name="r"/> applies to body <paramref name="index"/> (by id, alias or parent).</summary>
        public bool Matches(MusicRule r, int index)
        {
            if (index < 0 || index >= Bodies.Count) return false;
            if (Contains(r.BodyIds, Bodies[index].Id)) return true;
            int parent = Bodies[index].ParentIndex;
            return parent >= 0 && Contains(r.ParentIds, Bodies[parent].Id);
        }

        static bool Contains(string[] ids, string id)
        {
            if (ids == null) return false;
            foreach (string s in ids)
                if (string.Equals(s, id, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Star region radius: max(minRadii·R★, fraction·min over planets (periapsis − SOI)), or the override. See
        /// <see cref="MusicSelectorSettings.StarRegionInnerOrbitFraction"/> for the reasoning.
        /// </summary>
        public static double StarRegion(MusicSelectorSettings s, double starRadius, IEnumerable<(double periapsis, double soi)> planets)
        {
            if (s.StarRegionRadiusOverride > 0.0) return s.StarRegionRadiusOverride;
            double inner = double.PositiveInfinity;
            foreach ((double periapsis, double soi) in planets) inner = Math.Min(inner, periapsis - soi);
            double floor = Math.Max(0.0, s.StarRegionMinStellarRadii) * starRadius;
            if (double.IsInfinity(inner) || inner <= 0.0) return floor;
            return Math.Max(floor, s.StarRegionInnerOrbitFraction * inner);
        }

        /// <summary>Builds the zones from the navigation model the game already uses (same SOI radii and orbits).</summary>
        public static MusicWorld FromSystem(PatchedConicSystem system, MusicLibrary library)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            if (library == null) throw new ArgumentNullException(nameof(library));
            var w = new MusicWorld();
            var planets = new List<(double, double)>();
            for (int i = 0; i < system.Count; i++)
            {
                NavBody b = system[i];
                w.Bodies.Add(new MusicBody
                {
                    Id = b.Id, ParentIndex = b.ParentIndex, Radius = b.Radius, SoiRadius = b.SoiRadius,
                    NearSurfaceRadius = b.Radius + Math.Max(0.0, b.Physical != null ? b.Physical.spaceBoundaryAltitude : 0.0),
                    SemiMajorAxis = b.Orbit != null ? b.Orbit.SemiMajorAxis : 0.0
                });
                if (b.ParentIndex < 0 && b.Orbit != null)
                    planets.Add((b.Orbit.SemiMajorAxis * (1.0 - b.Orbit.Eccentricity), b.SoiRadius));
            }
            w.StarRegionRadius = StarRegion(library.Selector, system.StarProperties.radius, planets);
            w.ResolveRules(library);
            return w;
        }

        /// <summary>Resolves proximity radii and records rules whose bodies are absent. Call after filling <see cref="Bodies"/>.</summary>
        public void ResolveRules(MusicLibrary library)
        {
            ProximityRadius.Clear();
            Notes.Clear();
            foreach (MusicRule r in library.Rules)
            {
                switch (r.Kind)
                {
                    case MusicZoneKind.Proximity:
                    {
                        int anchor = IndexOfAny(r.BodyIds);
                        if (anchor < 0)
                        {
                            Notes.Add(r.Id + ": anchor " + string.Join("/", r.BodyIds) + " not in system");
                            break;
                        }
                        int moon = string.IsNullOrEmpty(r.RadiusFromOrbitOf) ? -1 : IndexOf(r.RadiusFromOrbitOf);
                        if (moon >= 0 && Bodies[moon].ParentIndex == anchor && Bodies[moon].SemiMajorAxis > 0.0)
                            ProximityRadius[r.Id] = Bodies[moon].SemiMajorAxis;
                        else if (r.FallbackRadiusInBodyRadii > 0.0)
                        {
                            ProximityRadius[r.Id] = r.FallbackRadiusInBodyRadii * Bodies[anchor].Radius;
                            Notes.Add(r.Id + ": " + r.RadiusFromOrbitOf + " not in system, radius = " + r.FallbackRadiusInBodyRadii + " × " + Bodies[anchor].Id + " radius");
                        }
                        else Notes.Add(r.Id + ": " + r.RadiusFromOrbitOf + " not in system, rule inactive");
                        break;
                    }
                    case MusicZoneKind.BodySoi:
                    case MusicZoneKind.NearSurface:
                    {
                        bool any = false;
                        for (int i = 0; i < Bodies.Count && !any; i++) any = Matches(r, i);
                        if (!any)
                        {
                            var names = new List<string>(r.BodyIds);
                            foreach (string p in r.ParentIds) names.Add("moons of " + p);
                            Notes.Add(r.Id + ": no body " + string.Join("/", names) + " in system (rule waits for it)");
                        }
                        break;
                    }
                }
            }
        }
    }
}
