using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Multiplayer;

namespace SpaceSim.Tests
{
    public class CrewRobotSyncTests
    {
        static CrewRobotSnapshot Sample() => new CrewRobotSnapshot
        {
            Sequence = 5,
            Frame = 2,
            Eva = true,
            Position = new Vector3d(6.371e6, 12.25, -3.5),
            Velocity = new Vector3d(0.0, 1.4, 1.0 / 3.0),
            Yaw = 271.5,
            Forward = 1.0,
            Right = -1.0,
            Sprint = true,
            Jumps = 3,
            Photos = 4,
            RoverActive = false,
            RoverFixed = new Vector3d(6.371e6, 0.0, 0.0),
            Collected = new[] { "home:12", "home:40" },
        };

        [Test]
        public void EncodeDecode_RoundTrips()
        {
            CrewRobotSnapshot a = Sample();
            CrewRobotSnapshot b = CrewRobotSnapshot.Decode(a.Encode());
            Assert.IsNotNull(b);
            Assert.AreEqual(a.Sequence, b.Sequence);
            Assert.AreEqual(a.Frame, b.Frame);
            Assert.AreEqual(a.Eva, b.Eva);
            Assert.AreEqual(a.Position.y, b.Position.y);
            Assert.AreEqual(a.Velocity.z, b.Velocity.z);
            Assert.AreEqual(a.Yaw, b.Yaw);
            Assert.AreEqual(a.Forward, b.Forward);
            Assert.AreEqual(a.Right, b.Right);
            Assert.AreEqual(a.Sprint, b.Sprint);
            Assert.AreEqual(a.Jumps, b.Jumps);
            Assert.AreEqual(a.Photos, b.Photos);
            Assert.AreEqual(a.RoverActive, b.RoverActive);
            Assert.AreEqual(a.RoverFixed.x, b.RoverFixed.x);
            CollectionAssert.AreEqual(a.Collected, b.Collected);
        }

        [Test]
        public void EmptyCollectedListRoundTrips()
        {
            CrewRobotSnapshot a = Sample();
            a.Collected = new string[0];
            CollectionAssert.IsEmpty(CrewRobotSnapshot.Decode(a.Encode()).Collected);
        }

        [Test]
        public void IdsWithSeparatorsAreNotSent()
        {
            CrewRobotSnapshot a = Sample();
            a.Collected = new[] { "ok", "bad;id", "bad|id", "" };
            CollectionAssert.AreEqual(new[] { "ok" }, CrewRobotSnapshot.Decode(a.Encode()).Collected);
        }

        [Test]
        public void Decode_RejectsGarbageAndClampsInput()
        {
            Assert.IsNull(CrewRobotSnapshot.Decode(null));
            Assert.IsNull(CrewRobotSnapshot.Decode("r2;1;2"));
            Assert.IsNull(CrewRobotSnapshot.Decode(Sample().Encode().Replace("r2;", "v1;")));
            string text = Sample().Encode().Replace(";271.5;1;-1;", ";271.5;7;-9;");
            CrewRobotSnapshot s = CrewRobotSnapshot.Decode(text);
            Assert.AreEqual(1.0, s.Forward);
            Assert.AreEqual(-1.0, s.Right);
        }

        [Test]
        public void RobotIsPlacedOnlyBeyondDriftTolerance()
        {
            CrewRobotSnapshot s = Sample(); // ≈1.5 m/s
            Vector3d p = s.Position;
            Assert.IsFalse(CrewRobotSync.RobotNeedsPlace(s, p + new Vector3d(0, 0.4, 0), 0.05));
            Assert.IsFalse(CrewRobotSync.RobotNeedsPlace(s, p + new Vector3d(0, 0.6, 0), 0.1)); // 0.5 + 1.5·0.1
            Assert.IsTrue(CrewRobotSync.RobotNeedsPlace(s, p + new Vector3d(0, 2.0, 0), 0.1));
        }

        [Test]
        public void RoverMovesTheRocketOnlyWhileDrivingAndBeyondTolerance()
        {
            CrewRobotSnapshot s = Sample();
            Vector3d far = s.RoverFixed + new Vector3d(0, 5, 0);
            Assert.IsFalse(CrewRobotSync.RoverNeedsMove(s, far)); // not driving
            s.RoverActive = true;
            Assert.IsTrue(CrewRobotSync.RoverNeedsMove(s, far));
            Assert.IsFalse(CrewRobotSync.RoverNeedsMove(s, s.RoverFixed + new Vector3d(0, 0.5, 0)));
        }

        [Test]
        public void JumpStartsWhenTheCounterMovesOn()
        {
            CrewRobotSnapshot s = Sample();
            Assert.IsFalse(CrewRobotSync.StartsJump(3, s));
            Assert.IsTrue(CrewRobotSync.StartsJump(2, s));
        }
    }
}
