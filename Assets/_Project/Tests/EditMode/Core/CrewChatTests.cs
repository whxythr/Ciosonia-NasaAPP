using NUnit.Framework;
using SpaceSim.Multiplayer;

namespace SpaceSim.Tests
{
    public class CrewChatTests
    {
        // Captain = 1, other players 2, 3, 4.
        static CrewChat Chat(int local) => new CrewChat(local, 1, new[] { 1, 2, 3, 4 });

        [Test]
        public void PlayersWriteOnlyToTheCaptain()
        {
            CrewChat c = Chat(1);
            Assert.IsTrue(c.Allowed(2, 1));
            Assert.IsTrue(c.Allowed(1, 3));
            Assert.IsFalse(c.Allowed(2, 3), "players never talk to each other");
            Assert.IsFalse(c.Allowed(1, 1));
            Assert.IsFalse(c.Allowed(2, 9), "not in the crew");
        }

        [Test]
        public void CaptainSeesOneThreadPerPlayer()
        {
            CrewChat c = Chat(1);
            CollectionAssert.AreEqual(new[] { 2, 3, 4 }, c.VisibleChannels);
            Assert.IsNotNull(c.Post(2, 1, "ракета готова"));
            Assert.IsNotNull(c.Post(1, 3, "взлёт по готовности"));
            Assert.AreEqual(1, c.Thread(2).Count);
            Assert.AreEqual(1, c.Thread(3).Count);
            Assert.AreEqual(0, c.Thread(4).Count);
            Assert.AreEqual(2, c.RecipientOf(2));
        }

        [Test]
        public void PlayerSeesOnlyTheirThread()
        {
            CrewChat c = Chat(3);
            CollectionAssert.AreEqual(new[] { 3 }, c.VisibleChannels);
            Assert.AreEqual(1, c.RecipientOf(3));
            Assert.IsNotNull(c.Post(1, 3, "привет"));
            Assert.IsNull(c.Post(1, 2, "чужое"), "a line for another player is not shown here");
            Assert.IsNull(c.Post(2, 3, "от инженера"), "forbidden by the rule");
            Assert.AreEqual(1, c.Thread(3).Count);
        }

        [Test]
        public void UnreadCountsOnlyOthersLines()
        {
            CrewChat c = Chat(1);
            c.Post(2, 1, "a");
            c.Post(2, 1, "b");
            c.Post(1, 2, "own");
            Assert.AreEqual(2, c.Unread(2));
            Assert.AreEqual(2, c.TotalUnread);
            c.MarkRead(2);
            Assert.AreEqual(0, c.TotalUnread);
        }

        [Test]
        public void CleanTrimsAndLimits()
        {
            Assert.IsNull(CrewChat.Clean("   "));
            Assert.IsNull(CrewChat.Clean(null));
            Assert.AreEqual("a b", CrewChat.Clean(" a\nb "));
            Assert.AreEqual(CrewChat.MaxLength, CrewChat.Clean(new string('x', 1000)).Length);
        }

        [Test]
        public void ThreadKeepsOnlyTheNewestLines()
        {
            CrewChat c = Chat(1);
            for (int i = 0; i < CrewChat.MaxPerThread + 10; i++) c.Post(2, 1, "m" + i);
            Assert.AreEqual(CrewChat.MaxPerThread, c.Thread(2).Count);
            Assert.AreEqual("m10", c.Thread(2)[0].Text);
        }

        [Test]
        public void CaptainHoldingTwoRolesStillChatsWithTheOtherPlayer()
        {
            // Two players: 1 = captain + engineer, 2 = pilot + robot.
            var c = new CrewChat(1, 1, new[] { 1, 2 });
            CollectionAssert.AreEqual(new[] { 2 }, c.VisibleChannels);
            Assert.IsNotNull(c.Post(2, 1, "ok"));
        }
    }
}
