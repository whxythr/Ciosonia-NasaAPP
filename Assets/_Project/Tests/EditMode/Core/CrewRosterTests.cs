using NUnit.Framework;
using SpaceSim.Crew;
using SpaceSim.Economy;

namespace SpaceSim.Tests
{
    /// <summary>Crew roster (Tab screen): starting crew, boarding by seats, training, hiring and per-flight statistics.</summary>
    public class CrewRosterTests
    {
        static CrewRosterData Fresh()
        {
            var d = new CrewRosterData();
            CrewRoster.EnsureRoster(d);
            return d;
        }

        [Test]
        public void NewProfileHasStartingCrewAndApplicants()
        {
            PlayerProfile p = PlayerProfile.CreateNew("home");
            Assert.AreEqual(8, CrewRoster.Count(p.crew, CrewStatus.Available));
            Assert.AreEqual(3, CrewRoster.Count(p.crew, CrewStatus.Applicant));
            Assert.IsFalse(CrewRoster.EnsureRoster(p.crew), "an existing roster is never refilled");
        }

        [Test]
        public void OldProfileGetsRosterOnMigrationWithoutBeingMarkedMigrated()
        {
            var p = new PlayerProfile { crew = null };
            Assert.AreNotEqual(MigrationResult.TooNew, ProfileMigration.Migrate(p));
            Assert.IsNotNull(p.crew);
            Assert.Greater(p.crew.members.Count, 0);
        }

        [Test]
        public void ProbeFlightTakesNobodyButCounts()
        {
            CrewRosterData d = Fresh();
            int flight = CrewRoster.BeginFlight(d, 0);
            Assert.AreEqual(1, flight);
            Assert.AreEqual(0, CrewRoster.Aboard(d).Count);
        }

        [Test]
        public void ThreeSeatsTakeOneOfEachRolePilotFirst()
        {
            CrewRosterData d = Fresh();
            CrewRoster.BeginFlight(d, 3);
            var aboard = CrewRoster.Aboard(d);
            Assert.AreEqual(3, aboard.Count);
            Assert.IsTrue(aboard.Exists(m => m.role == CrewRole.Pilot));
            Assert.IsTrue(aboard.Exists(m => m.role == CrewRole.Engineer));
            Assert.IsTrue(aboard.Exists(m => m.role == CrewRole.Scientist));
            // The most experienced pilot (9 flights) is picked and the flight is counted.
            Assert.IsTrue(aboard.Exists(m => m.name == "София Рейес" && m.flights == 10));
        }

        [Test]
        public void EndFlightReleasesCrewAndNewFlightReleasesForgottenOne()
        {
            CrewRosterData d = Fresh();
            CrewRoster.BeginFlight(d, 1);
            CrewRoster.BeginFlight(d, 1); // the first one never ended (crash, quit)
            Assert.AreEqual(1, CrewRoster.Aboard(d).Count);
            Assert.AreEqual(2, d.activeFlight);
            CrewRoster.EndFlight(d);
            Assert.AreEqual(0, CrewRoster.Aboard(d).Count);
            Assert.AreEqual(0, d.activeFlight);
        }

        [Test]
        public void TimeInSpaceVisitsAndEvasGoToTheCrewAboard()
        {
            CrewRosterData d = Fresh();
            CrewRoster.BeginFlight(d, 2);
            CrewRoster.AddTimeInSpace(d, 3600.0);
            CrewRoster.VisitBody(d, "tromos");
            CrewRoster.VisitBody(d, "tromos");
            Assert.IsTrue(CrewRoster.RecordEva(d));
            foreach (CrewMember m in CrewRoster.Aboard(d))
            {
                Assert.AreEqual(3600.0, m.secondsInSpace, 1e-9);
                Assert.AreEqual(1, m.bodiesVisited.Count);
            }
            CrewRoster.EndFlight(d);
            Assert.IsFalse(CrewRoster.RecordEva(d), "nobody aboard");
        }

        [Test]
        public void TrainingSkipsOneLaunchAndRaisesTheMainSkill()
        {
            CrewRosterData d = Fresh();
            CrewMember pilot = d.members.Find(m => m.name == "София Рейес");
            int before = pilot.piloting, level = pilot.Level;
            Assert.IsTrue(CrewRoster.SendToTraining(d, pilot.id));
            Assert.IsFalse(CrewRoster.SendToTraining(d, pilot.id), "already training");
            CrewRoster.BeginFlight(d, 7);
            Assert.AreEqual(CrewStatus.Available, pilot.status, "the course ends at the next launch, without boarding it");
            Assert.AreEqual(System.Math.Min(100, before + CrewRoster.TrainingGain), pilot.piloting);
            Assert.AreEqual(1, pilot.trainings);
            Assert.GreaterOrEqual(pilot.Level, level);
        }

        [Test]
        public void CrewInFlightCannotTrain()
        {
            CrewRosterData d = Fresh();
            CrewRoster.BeginFlight(d, 1);
            CrewMember aboard = CrewRoster.Aboard(d)[0];
            Assert.IsFalse(CrewRoster.SendToTraining(d, aboard.id));
        }

        [Test]
        public void HiringCostsCreditsAndNeedsEnough()
        {
            PlayerProfile p = PlayerProfile.CreateNew("home", GameMode.Career); // Sandbox hires for free (GameModeTests)
            CrewMember applicant = p.crew.members.Find(m => m.status == CrewStatus.Applicant);
            p.credits = CrewRoster.HireCost - 1;
            Assert.IsFalse(CrewRoster.Hire(p, p.crew, applicant.id, out _));
            p.credits = CrewRoster.HireCost + 5;
            Assert.IsTrue(CrewRoster.Hire(p, p.crew, applicant.id, out _));
            Assert.AreEqual(5, p.credits);
            Assert.AreEqual(CrewStatus.Available, applicant.status);
            Assert.IsFalse(CrewRoster.Hire(p, p.crew, applicant.id, out _), "already hired");
        }
    }
}
