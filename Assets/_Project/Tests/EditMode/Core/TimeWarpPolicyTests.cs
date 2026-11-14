using NUnit.Framework;
using SpaceSim.Flight;

namespace SpaceSim.Tests
{
    public class TimeWarpPolicyTests
    {
        const double Radius = 6.0e6, Zone = 140000.0;

        static double Max(double altitude, bool burning = false, bool safe = false, bool landed = false, bool interplanetary = false) =>
            TimeWarpPolicy.Evaluate(new TimeWarpSituation
            {
                Altitude = altitude,
                BodyRadius = Radius,
                ZoneAltitude = Zone,
                EngineBurning = burning,
                SafeTrajectory = safe,
                Landed = landed,
                Interplanetary = interplanetary
            }).MaxWarp;

        [Test]
        public void TakeOff_IsLimitedToFour()
        {
            Assert.AreEqual(4.0, Max(0.0, landed: true));
            Assert.AreEqual(4.0, Max(30000.0, burning: true));
        }

        [Test]
        public void GrowsWithHeightOnTheWayToOrbit()
        {
            Assert.AreEqual(16.0, Max(100000.0, burning: true));
            Assert.AreEqual(100.0, Max(200000.0, burning: true));
            Assert.AreEqual(100.0, Max(200000.0));
        }

        [Test]
        public void SafeTrajectoryAllowsRails()
        {
            Assert.AreEqual(100000.0, Max(300000.0, safe: true));
            Assert.AreEqual(1000000.0, Max(Radius * 2.0, safe: true));
            Assert.AreEqual(1000000.0, Max(0.0, interplanetary: true));
        }

        [Test]
        public void TrajectoryIntoTheZone_StaysNumeric()
        {
            Assert.AreEqual(1000.0, Max(Radius * 2.0));
        }

        [Test]
        public void Engine_LimitsEvenFarAway()
        {
            Assert.AreEqual(100.0, Max(Radius * 2.0, burning: true, safe: true));
            Assert.AreEqual(100.0, Max(0.0, burning: true, interplanetary: true));
        }

        [Test]
        public void ZoneTakesPriorityOverSafeTrajectory()
        {
            Assert.AreEqual(4.0, Max(1000.0, safe: true));
        }

        [TestCase(0.0, 100.0, 1.0)]
        [TestCase(1.0, 100.0, 100.0)]
        [TestCase(0.5, 100.0, 10.0)]
        [TestCase(1.0, 4.0, 4.0)]
        [TestCase(0.5, 4.0, 2.0)]
        [TestCase(0.5, 1000000.0, 1000.0)]
        public void Slider_IsLogarithmicAndHitsBothEnds(double position, double max, double expected)
        {
            Assert.AreEqual(expected, TimeWarpPolicy.SliderToWarp(position, max), expected * 1e-9);
        }

        [Test]
        public void Slider_NeverExceedsMaximumAndIsMonotonic()
        {
            double previous = 1.0;
            for (int i = 0; i <= 1000; i++)
            {
                double w = TimeWarpPolicy.SliderToWarp(i / 1000.0, 7000.0);
                Assert.GreaterOrEqual(w, previous);
                Assert.LessOrEqual(w, 7000.0);
                previous = w;
            }
        }

        static TimeWarpZone ZoneOf(Planets.PlanetModel planet, double altitude, Mathematics.Vector3d velocity)
        {
            FreeFlightSimulation sim = TestPlanets.Simulation(planet);
            sim.SetInertialState(new Mathematics.Vector3d(planet.Radius + altitude, 0, 0), velocity, 0.0);
            TimeWarpSituation s = sim.WarpSituation();
            return TimeWarpPolicy.Evaluate(s).Zone;
        }

        [Test]
        public void Simulation_ReportsZoneFromItsRealState()
        {
            Planets.PlanetModel earth = TestPlanets.Earth(rotating: false);
            double zone = Physics.PatchedConicSystem.NumericZoneAltitude(earth.Atmosphere, earth.Surface.MaxSurfaceHeight);
            double r = earth.Radius + zone + 50000.0;
            double circular = System.Math.Sqrt(earth.GravitationalParameter / r);
            double escape = System.Math.Sqrt(2.0 * earth.GravitationalParameter / (3.0 * earth.Radius));
            var up = new Mathematics.Vector3d(1, 0, 0);
            var sideways = new Mathematics.Vector3d(0, 1, 0);

            Assert.AreEqual(TimeWarpZone.Surface, ZoneOf(earth, zone * 0.25, up * 500.0));
            Assert.AreEqual(TimeWarpZone.UpperAtmosphere, ZoneOf(earth, zone * 0.75, up * 500.0));
            Assert.AreEqual(TimeWarpZone.LowOrbit, ZoneOf(earth, zone + 50000.0, sideways * circular));
            Assert.AreEqual(TimeWarpZone.Suborbital, ZoneOf(earth, zone + 50000.0, up * 1000.0), "goes up, but falls back");
            Assert.AreEqual(TimeWarpZone.HighOrbit, ZoneOf(earth, earth.Radius * 2.0, up * (escape * 1.5)), "leaving for good");
            Assert.AreEqual(TimeWarpZone.HighArc, ZoneOf(earth, earth.Radius * 2.0, up * (-escape * 1.5)), "the same speed towards the planet");
        }

        [Test]
        public void HomeWorld_ParkingOrbitIsAboveTheZone()
        {
            Planets.CelestialBodyPreset home = Planets.HomeSystemCatalog.Bodies().Find(b => b.Id == Planets.HomeSystemCatalog.HomeWorldId);
            double zone = Physics.PatchedConicSystem.NumericZoneAltitude(Physics.AtmosphericModel.Create(home.Physical.atmosphere), 15000.0);
            TestContext.Progress.WriteLine("Home world: radius " + home.Physical.meanRadius + " m, zone " + zone + " m, ×4 below " + zone * 0.5 + " m");
            Assert.Less(zone, 300000.0);
        }

        [Test]
        public void Slider_RoundTripsThePosition()
        {
            Assert.AreEqual(0.5, TimeWarpPolicy.WarpToSlider(10.0, 100.0), 1e-12);
            Assert.AreEqual(0.0, TimeWarpPolicy.WarpToSlider(1.0, 100.0));
            Assert.AreEqual(1.0, TimeWarpPolicy.WarpToSlider(500.0, 100.0));
            Assert.AreEqual(0.0, TimeWarpPolicy.WarpToSlider(3.0, 1.0));
        }
    }
}
