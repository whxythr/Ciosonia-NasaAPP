using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceSim.Audio;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Tests
{
    /// <summary>Soundtrack selection: every assignment, overlaps, hysteresis, star region vs deep space, the real home system.</summary>
    public class MusicSelectorTests
    {
        const double AU = 1.495978707e11;

        // A synthetic system on a plane: every body that the brief names, with simple numbers (positions star-centric).
        sealed class Body
        {
            public string Id, Parent;
            public double X, Y, Radius, Soi, A;
        }

        MusicLibrary lib;
        MusicWorld world;
        List<Body> bodies;

        [SetUp]
        public void SetUp()
        {
            lib = MusicLibrary.CreateDefault();
            bodies = new List<Body>();
            bodies.Add(P("teonan", 0.09 * AU, 0, 2e6, 1e7));
            bodies.Add(P("quoris", 0, 0.13 * AU, 1.2e6, 6e6));
            bodies.Add(P("hermeous-mora", -0.43 * AU, 0, 8e6, 4e8));
            bodies.Add(M("hermeous-mora-i", "hermeous-mora", 3e7, 1.2e5, 4e5));
            bodies.Add(P("home", 0.67 * AU, 0.1 * AU, 6.4e6, 9e8));
            bodies.Add(M("kolimpri", "home", 4e8, 4e5, 3e7));
            bodies.Add(P("tholin", 0, -0.87 * AU, 7e6, 1.2e9));
            bodies.Add(P("ciozonia", 1.2 * AU, 0, 6e6, 1e9));
            bodies.Add(M("ciozonia-a", "ciozonia", 2e8, 5e5, 2e7));
            bodies.Add(M("ciozonia-b", "ciozonia", 5e8, 4e5, 3e7));
            bodies.Add(P("colossus", 0, 1.5 * AU, 3e7, 5e9));
            bodies.Add(P("eremus", -2.05 * AU, 0, 8.2e7, 1.9e10));
            bodies.Add(M("ori", "eremus", 1.0e9, 1e6, 5e7));
            bodies.Add(M("mochi-mochi", "eremus", 4.0e8, 3e5, 1e7));
            bodies.Add(M("dotta", "eremus", 3.0e9, 3e5, 2e7));
            world = new MusicWorld { StarRegionRadius = 0.04 * AU };
            foreach (Body b in bodies)
                world.Bodies.Add(new MusicBody { Id = b.Id, Radius = b.Radius, SoiRadius = b.Soi, NearSurfaceRadius = b.Radius + 120000.0, SemiMajorAxis = b.A });
            for (int i = 0; i < bodies.Count; i++) world.Bodies[i].ParentIndex = bodies[i].Parent == null ? -1 : bodies.FindIndex(b => b.Id == bodies[i].Parent);
            world.ResolveRules(lib);
        }

        static Body P(string id, double x, double y, double r, double soi) => new Body { Id = id, X = x, Y = y, Radius = r, Soi = soi, A = Math.Sqrt(x * x + y * y) };

        Body M(string id, string parent, double a, double r, double soi)
        {
            Body p = bodies.Find(b => b.Id == parent);
            return new Body { Id = id, Parent = parent, X = p.X + a, Y = p.Y, Radius = r, Soi = soi, A = a };
        }

        Body B(string id) => bodies.Find(b => b.Id == id);

        MusicContext At(double x, double y)
        {
            var d = new double[bodies.Count];
            for (int i = 0; i < bodies.Count; i++) d[i] = Math.Sqrt((x - bodies[i].X) * (x - bodies[i].X) + (y - bodies[i].Y) * (y - bodies[i].Y));
            return new MusicContext { HasPosition = true, HeliocentricDistance = Math.Sqrt(x * x + y * y), BodyDistances = d };
        }

        /// <summary>A point at <paramref name="distance"/> from a body's centre, on the side away from the star.</summary>
        MusicContext Near(string id, double distance)
        {
            Body b = B(id);
            double r = Math.Sqrt(b.X * b.X + b.Y * b.Y);
            return At(b.X + b.X / r * distance, b.Y + b.Y / r * distance);
        }

        /// <summary>A point at <paramref name="distance"/> from a body's centre, perpendicular to the star direction.</summary>
        MusicContext Beside(string id, double distance)
        {
            Body b = B(id);
            double r = Math.Sqrt(b.X * b.X + b.Y * b.Y);
            return At(b.X - b.Y / r * distance, b.Y + b.X / r * distance);
        }

        string Track(MusicContext ctx) => MusicSelector.Step(lib, world, ctx, null, 0.0).TrackId;

        [Test]
        public void DefaultTableIsConsistent()
        {
            CollectionAssert.IsEmpty(lib.Validate());
            Assert.AreEqual(11, lib.Tracks.Count, "eleven distinct tracks (the star reuses Black Hole)");
            Assert.AreEqual(lib.Track("lokijar-black-hole").SoundCloudUrl, "https://soundcloud.com/cdad-dwdad/lokijar-black-hole");
            CollectionAssert.AreEqual(new[] { "aero-orion.ogg", "aero-orion.mp3", "aero-orion.wav" }, MusicLibrary.ExpectedFileNames("aero-orion"));
        }

        [Test]
        public void MainMenuAlwaysWins()
        {
            MusicContext ctx = Near("eremus", 2e8);
            ctx.InMainMenu = true;
            Assert.AreEqual("aero-orion", Track(ctx));
            Assert.AreEqual("aero-orion", Track(MusicContext.Menu()));
        }

        [Test]
        public void EachBodyAssignment()
        {
            Assert.AreEqual("akira-the-blithering-heights", Track(Near("teonan", 5e6)), "Teanon");
            Assert.AreEqual("akira-the-blithering-heights", Track(Near("quoris", 3e6)), "Quaris");
            Assert.AreEqual("lokijar-black-hole", Track(Beside("hermeous-mora", 2e8)), "Mora");
            Assert.AreEqual("lokijar-black-hole", Track(Beside("hermeous-mora-i", 2e5)), "Mora I");
            Assert.AreEqual("aero-flying-through-the", Track(Near("ciozonia", 6e6 + 50000.0)), "Ciozonia atmosphere");
            Assert.AreEqual("adrian-chifu-eternal-traveler", Track(Beside("ciozonia-a", 1e7)), "Ciozonia moon A");
            Assert.AreEqual("adrian-chifu-eternal-traveler", Track(Beside("ciozonia-b", 1e7)), "Ciozonia moon B");
            Assert.AreEqual("water-drips-auroras", Track(Near("colossus", 1e9)), "Colossus");
            Assert.AreEqual("vortex-mechanics-saturn-rings", Track(Beside("eremus", 5e9)), "Yeremus SOI, outside Ori's orbit");
            Assert.AreEqual("lombus-cosmic-soup", Track(Beside("eremus", 9e8)), "within Ori's orbit");
            Assert.AreEqual("fadg-1", Track(Beside("mochi-mochi", 5e6)), "Mochi-Mochi");
            Assert.AreEqual("fadg-1", Track(Beside("dotta", 5e6)), "Dotta");
            Assert.AreEqual("lombus-amino", Track(Near("tholin", 1e8)), "Tolin");
            Assert.AreEqual("lokijar-black-hole", Track(At(0.01 * AU, 0.0)), "star region");
            Assert.AreEqual("euclidea-aero-solar-protuberance", Track(At(0.3 * AU, -0.3 * AU)), "deep space");
        }

        [Test]
        public void CiozoniaAboveTheAtmosphereIsNotTheSurfaceTrack()
        {
            MusicSelectorState s = MusicSelector.Step(lib, world, Near("ciozonia", 6e7), null, 0.0);
            Assert.AreEqual(MusicSelector.UnassignedKey("ciozonia"), s.ZoneKey);
            Assert.AreEqual(MusicLibrary.DeepSpaceTrack, s.TrackId, "a SOI without a matching rule falls back to the deep-space track");
        }

        [Test]
        public void BodyWithoutRuleUsesTheUnassignedTrack()
        {
            Assert.AreEqual(MusicLibrary.DeepSpaceTrack, Track(Near("home", 1e7)));
            lib.UnassignedSoiTrackId = "aero-orion";
            Assert.AreEqual("aero-orion", Track(Near("kolimpri", 1e6)), "moon of the start planet → the configured fallback");
        }

        [Test]
        public void MoonBeatsParentAndProximity()
        {
            // Mochi-Mochi orbits inside Ori's orbit: its SOI rule beats Cosmic Soup, which beats the Eremus SOI rule.
            MusicSelectorState s = MusicSelector.Step(lib, world, Beside("mochi-mochi", 5e6), null, 0.0);
            Assert.AreEqual("fadg-1", s.TrackId);
            Assert.AreEqual(MusicSelector.SoiKey("mochi-mochi"), s.ZoneKey);
            Assert.IsTrue(s.IsInside(MusicSelector.SoiKey("eremus")));
            Assert.IsTrue(s.IsInside(MusicSelector.ProximityKey("near-eremus")));
        }

        [Test]
        public void ProximityRadiusComesFromOrisOrbit()
        {
            Assert.AreEqual(1.0e9, world.ProximityRadius["near-eremus"], 1.0);
            Assert.AreEqual("lombus-cosmic-soup", Track(Beside("eremus", 0.99e9)));
            Assert.AreEqual("vortex-mechanics-saturn-rings", Track(Beside("eremus", 1.01e9)));
        }

        [Test]
        public void ProximityFallsBackWithoutOri()
        {
            world.Bodies[bodies.FindIndex(b => b.Id == "ori")].Id = "not-ori";
            world.ResolveRules(lib);
            Assert.AreEqual(6.0 * 8.2e7, world.ProximityRadius["near-eremus"], 1.0);
            Assert.IsTrue(world.Notes.Exists(n => n.StartsWith("near-eremus")));
        }

        [Test]
        public void StarRegionBeatsEverythingButTheMenu()
        {
            // An artificial body SOI overlapping the star region still loses to it.
            world.Bodies[0].SoiRadius = 0.2 * AU;
            Assert.AreEqual("lokijar-black-hole", Track(At(0.02 * AU, 0.0)));
            Assert.AreEqual("akira-the-blithering-heights", Track(At(0.05 * AU, 0.0)));
        }

        [Test]
        public void DeepSpaceVsStarRegionBoundary()
        {
            Assert.AreEqual("lokijar-black-hole", Track(At(0.0399 * AU, 0.0)));
            Assert.AreEqual(MusicLibrary.DeepSpaceTrack, Track(At(0.0, -0.0401 * AU)));
        }

        [Test]
        public void HysteresisBandKeepsTheZone()
        {
            double soi = B("tholin").Soi;
            MusicSelectorState s = MusicSelector.Step(lib, world, Near("tholin", 0.99 * soi), null, 0.0);
            Assert.AreEqual("lombus-amino", s.TrackId);
            // Just outside the nominal sphere but inside the 5 % band: still inside.
            s = MusicSelector.Step(lib, world, Near("tholin", 1.03 * soi), s, 10.0);
            Assert.AreEqual("lombus-amino", s.TrackId);
            Assert.AreEqual("lombus-amino", s.DesiredTrackId);
            // Beyond the band: deep space is wanted, but only after the dwell time.
            s = MusicSelector.Step(lib, world, Near("tholin", 1.06 * soi), s, 1.0);
            Assert.AreEqual("lombus-amino", s.TrackId);
            Assert.AreEqual(MusicLibrary.DeepSpaceTrack, s.DesiredTrackId);
            // Coming back to 1.03 (inside the band but no longer "inside"): the sphere must be re-entered below 1.0.
            s = MusicSelector.Step(lib, world, Near("tholin", 1.03 * soi), s, 1.0);
            Assert.AreEqual(MusicLibrary.DeepSpaceTrack, s.DesiredTrackId);
            s = MusicSelector.Step(lib, world, Near("tholin", 0.98 * soi), s, 1.0);
            Assert.AreEqual("lombus-amino", s.TrackId);
            Assert.IsFalse(s.HasPending, "returning before the dwell expired cancels the change");
        }

        [Test]
        public void DwellTimeDelaysTheSwitch()
        {
            double soi = B("tholin").Soi;
            MusicSelectorState s = MusicSelector.Step(lib, world, Near("tholin", 0.5 * soi), null, 0.0);
            MusicContext outside = Near("tholin", 2.0 * soi);
            s = MusicSelector.Step(lib, world, outside, s, 0.5);
            Assert.AreEqual("lombus-amino", s.TrackId);
            Assert.AreEqual(0.0, s.PendingSeconds, 1e-12);
            s = MusicSelector.Step(lib, world, outside, s, 2.0);
            Assert.AreEqual("lombus-amino", s.TrackId);
            s = MusicSelector.Step(lib, world, outside, s, 2.0);
            Assert.AreEqual(MusicLibrary.DeepSpaceTrack, s.TrackId);
            Assert.AreEqual(MusicSelector.DeepZone, s.ZoneKey);
        }

        [Test]
        public void FlappingAcrossTheBoundaryNeverSwitches()
        {
            double soi = B("teonan").Soi;
            MusicSelectorState s = MusicSelector.Step(lib, world, Near("teonan", 0.9 * soi), null, 0.0);
            for (int i = 0; i < 100; i++)
            {
                s = MusicSelector.Step(lib, world, Near("teonan", i % 2 == 0 ? 1.2 * soi : 0.9 * soi), s, 0.5);
                Assert.AreEqual("akira-the-blithering-heights", s.TrackId, "step " + i);
            }
        }

        [Test]
        public void SameTrackAcrossZonesDoesNotChange()
        {
            // Mora → its moon: both Black Hole; the zone changes at once, the track id stays.
            MusicSelectorState s = MusicSelector.Step(lib, world, Beside("hermeous-mora", 2e8), null, 0.0);
            s = MusicSelector.Step(lib, world, Beside("hermeous-mora-i", 2e5), s, 0.1);
            Assert.AreEqual("lokijar-black-hole", s.TrackId);
            Assert.AreEqual(MusicSelector.SoiKey("hermeous-mora-i"), s.ZoneKey);
            Assert.IsFalse(s.HasPending);
        }

        [Test]
        public void MenuChangesAreImmediateAndUnknownPositionKeepsTheTrack()
        {
            MusicSelectorState s = MusicSelector.Step(lib, world, MusicContext.Menu(), null, 0.0);
            Assert.AreEqual("aero-orion", s.TrackId);
            s = MusicSelector.Step(lib, world, MusicContext.Unknown(), s, 5.0);
            Assert.AreEqual("aero-orion", s.TrackId, "scene loading: no position yet, keep playing");
            s = MusicSelector.Step(lib, world, Near("tholin", 1e8), s, 0.0);
            Assert.AreEqual("lombus-amino", s.TrackId, "leaving the menu switches at once");
            s = MusicSelector.Step(lib, world, MusicContext.Menu(), s, 0.0);
            Assert.AreEqual("aero-orion", s.TrackId);
        }

        [Test]
        public void EqualPowerCrossfade()
        {
            for (double t = 0.0; t <= 1.0; t += 0.125)
            {
                MusicCrossfade.Gains(t, out double a, out double b);
                Assert.AreEqual(1.0, a * a + b * b, 1e-12);
            }
            MusicCrossfade.Gains(-1.0, out double o0, out double i0);
            MusicCrossfade.Gains(2.0, out double o1, out double i1);
            Assert.AreEqual(1.0, o0, 1e-12);
            Assert.AreEqual(0.0, i0, 1e-12);
            Assert.AreEqual(0.0, o1, 1e-12);
            Assert.AreEqual(1.0, i1, 1e-12);
        }

        // ---- The real home system ----------------------------------------------------------------------------

        [Test]
        public void HomeSystemZones()
        {
            PatchedConicSystem sys = PatchedConicSystem.FromHomeSystem();
            MusicWorld w = MusicWorld.FromSystem(sys, lib);
            NavBody teonan = sys[sys.IndexOf("teonan")];
            double starR = sys.StarProperties.radius;
            Assert.Greater(w.StarRegionRadius, 3.0 * starR);
            Assert.Less(w.StarRegionRadius, teonan.Orbit.SemiMajorAxis * (1.0 - teonan.Orbit.Eccentricity) - teonan.SoiRadius, "star region never overlaps a planet's SOI");
            NavBody eremus = sys[sys.IndexOf("eremus")];
            Assert.AreEqual(6.0 * eremus.Radius, w.ProximityRadius["near-eremus"], 1.0, "Ori is not in the catalog yet: fallback radius");
            Assert.Less(w.ProximityRadius["near-eremus"], eremus.SoiRadius);
            foreach (string missing in new[] { "ciozonia-surface", "ciozonia-moons", "colossus", "mochi-dotta" })
                Assert.IsTrue(w.Notes.Exists(n => n.StartsWith(missing + ":")), missing);

            string At(string id, double offset)
            {
                int i = sys.IndexOf(id);
                Vector3d p = sys.Position(i, 0.0);
                Vector3d v = p + p.Normalized * offset;
                var d = new double[sys.Count];
                for (int k = 0; k < sys.Count; k++) d[k] = (v - sys.Position(k, 0.0)).Magnitude;
                var ctx = new MusicContext { HasPosition = true, HeliocentricDistance = v.Magnitude, BodyDistances = d };
                return MusicSelector.Step(lib, w, ctx, null, 0.0).TrackId;
            }

            Assert.AreEqual("akira-the-blithering-heights", At("teonan", 3e6));
            Assert.AreEqual("akira-the-blithering-heights", At("quoris", 2e6));
            Assert.AreEqual("lokijar-black-hole", At("hermeous-mora", 2e7));
            Assert.AreEqual("lokijar-black-hole", At("hermeous-mora-i", 2e5));
            Assert.AreEqual("lombus-amino", At("tholin", 2e7));
            Assert.AreEqual("lombus-cosmic-soup", At("eremus", 2e8));
            Assert.AreEqual("vortex-mechanics-saturn-rings", At("eremus", 2e9));
            Assert.AreEqual(MusicLibrary.DeepSpaceTrack, At("home", 1e7), "start planet has no rule");
            Assert.AreEqual(MusicLibrary.DeepSpaceTrack, At("eremus", 1.05 * eremus.SoiRadius));
        }
    }
}
