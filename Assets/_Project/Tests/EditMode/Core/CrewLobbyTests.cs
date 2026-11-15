using NUnit.Framework;
using SpaceSim.Multiplayer;

namespace SpaceSim.Tests
{
    public class CrewLobbyTests
    {
        static CrewLobbyState Lobby(int players)
        {
            var s = new CrewLobbyState();
            for (int i = 1; i <= players; i++) s.AddMember(i, "P" + i, i == 1);
            return s;
        }

        static void ReadyAll(CrewLobbyState s)
        {
            foreach (CrewMember m in s.Members) s.SetReady(m.Id, true);
        }

        [Test]
        public void OnePlayerCannotStart()
        {
            var s = Lobby(1);
            foreach (CrewRole r in CrewRoles.All) s.Claim(1, r);
            ReadyAll(s);
            Assert.AreEqual(CrewLobbyIssue.NotEnoughPlayers, s.Check());
        }

        [Test]
        public void TwoPlayersStartOnlyWhenAllFourRolesAreCovered()
        {
            var s = Lobby(2);
            Assert.IsTrue(s.Claim(1, CrewRole.Captain));
            Assert.IsTrue(s.Claim(2, CrewRole.Pilot));
            ReadyAll(s);
            Assert.AreEqual(CrewLobbyIssue.RolesUncovered, s.Check());
            CollectionAssert.AreEqual(new[] { CrewRole.Engineer, CrewRole.RobotOperator }, s.UncoveredRoles());

            Assert.IsTrue(s.Claim(1, CrewRole.Engineer));
            Assert.IsTrue(s.Claim(2, CrewRole.RobotOperator));
            Assert.AreEqual(CrewLobbyIssue.NotReady, s.Check(), "taking a role clears «Готов»");
            ReadyAll(s);
            Assert.IsTrue(s.CanStart);
            Assert.AreEqual(2, s.RolesOf(1).Count);
            Assert.AreEqual(2, s.RolesOf(2).Count);
        }

        [Test]
        public void ARoleHasOneOwner()
        {
            var s = Lobby(2);
            Assert.IsTrue(s.Claim(1, CrewRole.Engineer));
            Assert.IsFalse(s.Claim(2, CrewRole.Engineer));
            Assert.IsFalse(s.Release(2, CrewRole.Engineer));
            Assert.AreEqual(1, s.OwnerOf(CrewRole.Engineer));
            Assert.IsTrue(s.Toggle(1, CrewRole.Engineer));
            Assert.AreEqual(CrewLobbyState.NoOwner, s.OwnerOf(CrewRole.Engineer));
        }

        [Test]
        public void PlayerWithoutRoleBlocksStart()
        {
            var s = Lobby(2);
            foreach (CrewRole r in CrewRoles.All) s.Claim(1, r);
            ReadyAll(s);
            Assert.AreEqual(CrewLobbyIssue.MemberWithoutRole, s.Check());
        }

        [Test]
        public void AssignUncoveredSpreadsRolesEvenly()
        {
            var s = Lobby(3);
            s.Claim(1, CrewRole.Captain);
            s.Claim(1, CrewRole.Pilot);
            Assert.AreEqual(2, s.AssignUncovered());
            Assert.AreEqual(0, s.UncoveredRoles().Count);
            Assert.AreEqual(2, s.RolesOf(1).Count);
            Assert.AreEqual(1, s.RolesOf(2).Count);
            Assert.AreEqual(1, s.RolesOf(3).Count);
            ReadyAll(s);
            Assert.IsTrue(s.CanStart);
        }

        [Test]
        public void LeavingFreesRolesAndPassesHost()
        {
            var s = Lobby(3);
            s.Claim(1, CrewRole.Captain);
            s.Claim(1, CrewRole.Engineer);
            Assert.IsTrue(s.RemoveMember(1));
            Assert.IsFalse(s.IsCovered(CrewRole.Captain));
            Assert.IsFalse(s.IsCovered(CrewRole.Engineer));
            Assert.AreEqual(2, s.Host.Id);
        }

        [Test]
        public void RoomHoldsAtMostFourPlayers()
        {
            var s = Lobby(4);
            Assert.IsTrue(s.IsFull);
            Assert.IsFalse(s.AddMember(5, "P5"));
            Assert.IsFalse(s.AddMember(2, "dup"));
        }

        [Test]
        public void EncodeRoundTripsRolesAndReady()
        {
            var s = Lobby(3);
            s.Claim(1, CrewRole.Captain);
            s.Claim(3, CrewRole.Pilot);
            s.Claim(3, CrewRole.RobotOperator);
            s.SetReady(3, true);
            string wire = s.Encode();
            Assert.AreEqual("r=1,-1,3,3;ok=3", wire);

            var other = Lobby(3);
            other.ApplyEncoded(wire);
            Assert.AreEqual(1, other.OwnerOf(CrewRole.Captain));
            Assert.AreEqual(CrewLobbyState.NoOwner, other.OwnerOf(CrewRole.Engineer));
            Assert.AreEqual(3, other.OwnerOf(CrewRole.RobotOperator));
            Assert.IsTrue(other.Find(3).Ready);
            Assert.IsFalse(other.Find(1).Ready);
        }

        [Test]
        public void ApplyEncodedDropsPlayersWhoLeftAndToleratesGarbage()
        {
            var s = Lobby(2);
            s.ApplyEncoded("r=1,9,2,x;ok=9,2;junk");
            Assert.AreEqual(1, s.OwnerOf(CrewRole.Captain));
            Assert.AreEqual(CrewLobbyState.NoOwner, s.OwnerOf(CrewRole.Engineer), "player 9 is not in the room");
            Assert.AreEqual(2, s.OwnerOf(CrewRole.Pilot));
            Assert.AreEqual(CrewLobbyState.NoOwner, s.OwnerOf(CrewRole.RobotOperator));
            Assert.IsTrue(s.Find(2).Ready);
            s.ApplyEncoded(null);
            Assert.AreEqual(4, s.UncoveredRoles().Count);
        }

        [Test]
        public void CloneIsIndependent()
        {
            var s = Lobby(2);
            s.Claim(1, CrewRole.Captain);
            CrewLobbyState c = s.Clone();
            c.Claim(2, CrewRole.Pilot);
            c.SetReady(1, true);
            Assert.IsFalse(s.IsCovered(CrewRole.Pilot));
            Assert.IsFalse(s.Find(1).Ready);
            Assert.AreEqual(1, c.OwnerOf(CrewRole.Captain));
        }

        [Test]
        public void EveryRoleHasTitleAndDuties()
        {
            foreach (CrewRole r in CrewRoles.All)
            {
                CrewRoleInfo info = CrewRoles.Info(r);
                Assert.AreEqual(r, info.Role);
                Assert.IsNotEmpty(info.Title);
                Assert.IsNotEmpty(info.Initials);
                Assert.Greater(info.DutyCount, 0);
            }
        }
    }
}

namespace SpaceSim.Tests
{
    public class CrewRoomCodeTests
    {
        [Test]
        public void GeneratedCodesNormalizeToThemselves()
        {
            var rnd = new System.Random(7);
            for (int i = 0; i < 50; i++)
            {
                string code = CrewRoomCode.Generate(rnd);
                Assert.AreEqual(CrewRoomCode.Prefix.Length + CrewRoomCode.Length, code.Length);
                Assert.AreEqual(code, CrewRoomCode.Normalize(code));
            }
        }

        [TestCase("hzn-7q4k", "HZN-7Q4K")]
        [TestCase("7Q4K", "HZN-7Q4K")]
        [TestCase(" HZN 7q4k ", "HZN-7Q4K")]
        [TestCase("HZN-7Q4", null)]
        [TestCase("HZN-7Q4O", null)]
        [TestCase("", null)]
        public void NormalizeAcceptsWhatPlayersType(string typed, string expected) =>
            Assert.AreEqual(expected, CrewRoomCode.Normalize(typed));
    }
}

namespace SpaceSim.Tests
{
    public class CrewRegionTests
    {
        [NUnit.Framework.Test]
        public void Normalize_KeepsKnownRegions_AndFallsBackToEurope()
        {
            NUnit.Framework.Assert.AreEqual("eu", SpaceSim.Multiplayer.CrewRegion.Normalize("EU"));
            NUnit.Framework.Assert.AreEqual("us", SpaceSim.Multiplayer.CrewRegion.Normalize(" us "));
            NUnit.Framework.Assert.AreEqual("eu", SpaceSim.Multiplayer.CrewRegion.Normalize("asia"));
            NUnit.Framework.Assert.AreEqual("eu", SpaceSim.Multiplayer.CrewRegion.Normalize(null));
        }

        [NUnit.Framework.Test]
        public void Other_SwapsEuropeAndAmerica()
        {
            NUnit.Framework.Assert.AreEqual("us", SpaceSim.Multiplayer.CrewRegion.Other("eu"));
            NUnit.Framework.Assert.AreEqual("eu", SpaceSim.Multiplayer.CrewRegion.Other("us"));
            NUnit.Framework.Assert.AreEqual("US", SpaceSim.Multiplayer.CrewRegion.Label("us"));
        }
    }
}

namespace SpaceSim.Tests
{
    public class CrewRoomListTests
    {
        static SpaceSim.Multiplayer.CrewRoomInfo Room(string code, int players, bool open = true, int max = 4) =>
            new SpaceSim.Multiplayer.CrewRoomInfo { Code = code, Players = players, Open = open, MaxPlayers = max, Host = "H" };

        [NUnit.Framework.Test]
        public void Joinable_DropsFullClosedEmptyAndForeignRooms()
        {
            var rooms = new[]
            {
                Room("HZN-AAAA", 2),
                Room("HZN-BBBB", 4),            // full
                Room("HZN-CCCC", 1, open: false), // mission started
                Room("HZN-DDDD", 0),            // nobody left
                Room("somebody-else", 1),       // not a crew code
                null,
            };
            var list = SpaceSim.Multiplayer.CrewRoomList.Joinable(rooms);
            NUnit.Framework.Assert.AreEqual(1, list.Count);
            NUnit.Framework.Assert.AreEqual("HZN-AAAA", list[0].Code);
        }

        [NUnit.Framework.Test]
        public void Joinable_PutsTheFullestFirstThenByCode_AndCaps()
        {
            var rooms = new System.Collections.Generic.List<SpaceSim.Multiplayer.CrewRoomInfo>
            {
                Room("HZN-CCCC", 1), Room("HZN-AAAA", 1), Room("HZN-BBBB", 3),
            };
            var list = SpaceSim.Multiplayer.CrewRoomList.Joinable(rooms);
            NUnit.Framework.CollectionAssert.AreEqual(new[] { "HZN-BBBB", "HZN-AAAA", "HZN-CCCC" },
                list.ConvertAll(r => r.Code));
            NUnit.Framework.Assert.AreEqual(2, SpaceSim.Multiplayer.CrewRoomList.Joinable(rooms, 2).Count);
            NUnit.Framework.Assert.AreEqual("3/4", SpaceSim.Multiplayer.CrewRoomList.Seats(list[0]));
        }
    }
}
