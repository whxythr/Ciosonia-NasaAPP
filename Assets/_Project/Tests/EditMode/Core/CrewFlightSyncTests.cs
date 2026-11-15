using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Multiplayer;

namespace SpaceSim.Tests
{
    public class CrewFlightSyncTests
    {
        static CrewFlightSnapshot Sample() => new CrewFlightSnapshot
        {
            Sequence = 17,
            Time = 1234.56789012345,
            Warp = 10.0,
            Rate = 9.87,
            Paused = false,
            Frame = 3,
            Landed = false,
            Position = new Vector3d(6.371e6, -12.5, 0.000123),
            Velocity = new Vector3d(7.8e3, 1.0 / 3.0, -4.0),
            LandedFixed = new Vector3d(1.0, 2.0, 3.0),
            Resets = 2,
            StageCount = 3,
            StageIndex = 1,
            EngineOn = true,
            Throttle = 0.75,
            Pitch = 42.5,
            Heading = 90.0,
            Propellant = new[] { 0.0, 12345.678, 40000.0 },
            Legs = true,
            Rcs = false,
            Parachutes = true,
            Destroyed = true,
            HasNode = true,
            NodeTime = 5000.25,
            NodePrograde = 120.5,
            NodeNormal = -3.0,
            NodeRadial = 0.125,
        };

        [Test]
        public void EncodeDecode_RoundTripsEveryField()
        {
            CrewFlightSnapshot a = Sample();
            CrewFlightSnapshot b = CrewFlightSnapshot.Decode(a.Encode());
            Assert.IsNotNull(b);
            Assert.AreEqual(a.Sequence, b.Sequence);
            Assert.AreEqual(a.Time, b.Time);
            Assert.AreEqual(a.Warp, b.Warp);
            Assert.AreEqual(a.Rate, b.Rate);
            Assert.AreEqual(a.Paused, b.Paused);
            Assert.AreEqual(a.Frame, b.Frame);
            Assert.AreEqual(a.Landed, b.Landed);
            Assert.AreEqual(a.Position.x, b.Position.x);
            Assert.AreEqual(a.Position.z, b.Position.z);
            Assert.AreEqual(a.Velocity.y, b.Velocity.y);
            Assert.AreEqual(a.LandedFixed.z, b.LandedFixed.z);
            Assert.AreEqual(a.Resets, b.Resets);
            Assert.AreEqual(a.StageCount, b.StageCount);
            Assert.AreEqual(a.StageIndex, b.StageIndex);
            Assert.AreEqual(a.EngineOn, b.EngineOn);
            Assert.AreEqual(a.Throttle, b.Throttle);
            Assert.AreEqual(a.Pitch, b.Pitch);
            Assert.AreEqual(a.Heading, b.Heading);
            CollectionAssert.AreEqual(a.Propellant, b.Propellant);
            Assert.AreEqual(a.Legs, b.Legs);
            Assert.AreEqual(a.Rcs, b.Rcs);
            Assert.AreEqual(a.Parachutes, b.Parachutes);
            Assert.AreEqual(a.Destroyed, b.Destroyed);
            Assert.AreEqual(a.HasNode, b.HasNode);
            Assert.AreEqual(a.NodeTime, b.NodeTime);
            Assert.AreEqual(a.NodePrograde, b.NodePrograde);
            Assert.AreEqual(a.NodeNormal, b.NodeNormal);
            Assert.AreEqual(a.NodeRadial, b.NodeRadial);
        }

        [Test]
        public void Encode_DoesNotDependOnTheCurrentCulture()
        {
            var old = System.Threading.Thread.CurrentThread.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
                string text = Sample().Encode();
                StringAssert.DoesNotContain(",", text);
                Assert.AreEqual(0.75, CrewFlightSnapshot.Decode(text).Throttle);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = old;
            }
        }

        [Test]
        public void Decode_RejectsGarbage()
        {
            Assert.IsNull(CrewFlightSnapshot.Decode(null));
            Assert.IsNull(CrewFlightSnapshot.Decode(""));
            Assert.IsNull(CrewFlightSnapshot.Decode("hello"));
            string good = Sample().Encode();
            Assert.IsNull(CrewFlightSnapshot.Decode("v9" + good.Substring(2)));
            Assert.IsNull(CrewFlightSnapshot.Decode(good.Replace(";0.75;", ";NaN;")));
            Assert.IsNull(CrewFlightSnapshot.Decode(good + ";5")); // one propellant value too many
            Assert.IsNull(CrewFlightSnapshot.Decode(good.Substring(0, good.LastIndexOf(';')))); // one too few
        }

        [Test]
        public void Clock_SmallDifferencesAreTolerated_LargeOnesJump()
        {
            Assert.IsFalse(CrewFlightSync.ClockNeedsJump(100.0, 100.2, 1.0));
            Assert.IsTrue(CrewFlightSync.ClockNeedsJump(100.0, 101.0, 1.0));
            // At ×1000 half a real second is 500 simulated seconds.
            Assert.IsFalse(CrewFlightSync.ClockNeedsJump(1e6, 1e6 + 400.0, 1000.0));
            Assert.IsTrue(CrewFlightSync.ClockNeedsJump(1e6, 1e6 + 600.0, 1000.0));
            Assert.IsTrue(CrewFlightSync.ClockNeedsJump(101.0, 100.0, 1.0)); // ahead counts too
        }

        [Test]
        public void PredictedTime_AddsTheLatencyAtThePilotsRate_UnlessPaused()
        {
            CrewFlightSnapshot s = Sample();
            Assert.AreEqual(s.Time + 0.1 * s.Rate, CrewFlightSync.PredictedTime(s, 0.1), 1e-9);
            s.Paused = true;
            Assert.AreEqual(s.Time, CrewFlightSync.PredictedTime(s, 0.1));
        }

        [Test]
        public void Flight_SnapsOnlyWhenTheErrorIsLargerThanTheTolerance()
        {
            var s = new CrewFlightSnapshot { Time = 10.0, Position = new Vector3d(0, 0, 0), Velocity = new Vector3d(100, 0, 0) };
            // 0.1 s later the pilot's rocket is about 10 m further on.
            Assert.IsFalse(CrewFlightSync.FlightNeedsSnap(s, new Vector3d(10.5, 0, 0), 10.1));
            Assert.IsTrue(CrewFlightSync.FlightNeedsSnap(s, new Vector3d(60.0, 0, 0), 10.1));
            // Too far apart in time to extrapolate: left to the clock jump.
            Assert.IsFalse(CrewFlightSync.FlightNeedsSnap(s, new Vector3d(1e6, 0, 0), 20.0));
        }

        [Test]
        public void Landed_MovesOnlyBeyondTheTolerance()
        {
            var s = new CrewFlightSnapshot { Landed = true, LandedFixed = new Vector3d(6.371e6, 0, 0) };
            Assert.IsFalse(CrewFlightSync.LandedNeedsMove(s, new Vector3d(6.371e6, 1.0, 0)));
            Assert.IsTrue(CrewFlightSync.LandedNeedsMove(s, new Vector3d(6.371e6, 10.0, 0)));
        }
    }
}
