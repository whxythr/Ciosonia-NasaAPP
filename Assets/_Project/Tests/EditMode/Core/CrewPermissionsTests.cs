using NUnit.Framework;
using SpaceSim.Multiplayer;

namespace SpaceSim.Tests
{
    public class CrewPermissionsTests
    {
        [Test]
        public void EveryActionHasOneOwnerAndTheCaptainOwnsNone()
        {
            Assert.AreEqual(CrewRole.Engineer, CrewPermissions.Owner(CrewAction.BuildRocket));
            Assert.AreEqual(CrewRole.Pilot, CrewPermissions.Owner(CrewAction.PlanFlight));
            Assert.AreEqual(CrewRole.Pilot, CrewPermissions.Owner(CrewAction.FlyRocket));
            Assert.AreEqual(CrewRole.RobotOperator, CrewPermissions.Owner(CrewAction.WorkOnSurface));
            foreach (CrewAction a in CrewPermissions.All)
                Assert.IsFalse(CrewPermissions.Allowed(a, new[] { CrewRole.Captain }), a.ToString());
        }

        [Test]
        public void EachRoleMayDoOnlyItsOwnActions()
        {
            Assert.IsTrue(CrewPermissions.Allowed(CrewAction.BuildRocket, new[] { CrewRole.Engineer }));
            Assert.IsFalse(CrewPermissions.Allowed(CrewAction.FlyRocket, new[] { CrewRole.Engineer }));
            Assert.IsFalse(CrewPermissions.Allowed(CrewAction.WorkOnSurface, new[] { CrewRole.Pilot }));
            Assert.IsFalse(CrewPermissions.Allowed(CrewAction.BuildRocket, new[] { CrewRole.RobotOperator }));
        }

        [Test]
        public void SeveralRolesAddUp()
        {
            var roles = new[] { CrewRole.Captain, CrewRole.Pilot, CrewRole.RobotOperator };
            Assert.IsTrue(CrewPermissions.Allowed(CrewAction.FlyRocket, roles));
            Assert.IsTrue(CrewPermissions.Allowed(CrewAction.PlanFlight, roles));
            Assert.IsTrue(CrewPermissions.Allowed(CrewAction.WorkOnSurface, roles));
            Assert.IsFalse(CrewPermissions.Allowed(CrewAction.BuildRocket, roles));
        }

        [Test]
        public void NoRolesMeansNothingIsAllowed()
        {
            foreach (CrewAction a in CrewPermissions.All)
            {
                Assert.IsFalse(CrewPermissions.Allowed(a, new CrewRole[0]));
                Assert.IsFalse(CrewPermissions.Allowed(a, null));
            }
        }

        [Test]
        public void DeniedTextNamesTheRoleAndItsHolder()
        {
            string withName = CrewPermissions.DeniedText(CrewAction.BuildRocket, "Kepler");
            StringAssert.Contains(CrewRoles.Info(CrewRole.Engineer).Title, withName);
            StringAssert.Contains("Kepler", withName);
            StringAssert.DoesNotContain("(", CrewPermissions.DeniedText(CrewAction.FlyRocket, null));
        }
    }
}
