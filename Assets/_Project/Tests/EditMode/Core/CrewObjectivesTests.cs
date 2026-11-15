using System.Collections.Generic;
using NUnit.Framework;
using SpaceSim.Exploration;
using SpaceSim.Multiplayer;
using SpaceSim.Planets;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    public class CrewObjectivesTests
    {
        static CrewGoal Goal(CrewObjectives o, string id) => new List<CrewGoal>(o.Goals).Find(g => g.Id == id);
        static int Index(CrewObjectives o, string id) => new List<CrewGoal>(o.Goals).FindIndex(g => g.Id == id);

        [Test]
        public void MoraLander_GivesEveryRoleItsJob()
        {
            CrewObjectives o = CrewObjectives.For("mora-lander", "HZN-TEST");
            // Engineer: camera, sample collector, legs.
            CollectionAssert.Contains(Goal(o, "eng-camera").Modules, ModuleCatalog.Camera);
            Assert.IsNotNull(Goal(o, "eng-collector"));
            CollectionAssert.AreEquivalent(new[] { ModuleCatalog.LegsLight, ModuleCatalog.LegsHeavy }, Goal(o, "eng-legs").Modules);
            // Pilot: fly to the moon and land there.
            Assert.IsNotNull(Goal(o, "pilot-reach"));
            Assert.IsNotNull(Goal(o, "pilot-land"));
            Assert.IsNull(Goal(o, "pilot-orbit"));
            // Robot operator: photos, samples, return.
            Assert.AreEqual(o.Survey.Photos, Goal(o, "robot-photos").Count);
            Assert.AreEqual(o.Survey.Samples, Goal(o, "robot-samples").Count);
            Assert.IsNotNull(Goal(o, "robot-return"));
            foreach (CrewGoal g in o.Goals) Assert.AreNotEqual(CrewRole.Captain, g.Role);
        }

        [Test]
        public void HomeOrbitMission_HasNoTravelGoal_AndCapsOrbitPhotos()
        {
            CrewObjectives o = CrewObjectives.For("weather-watch", "HZN-TEST");
            Assert.IsNull(Goal(o, "pilot-reach"));
            Assert.IsNotNull(Goal(o, "pilot-orbit"));
            Assert.IsNull(Goal(o, "eng-legs"));
            Assert.LessOrEqual(Goal(o, "robot-photos").Count, CrewObjectives.MaxOrbitPhotos);
            Assert.IsNull(Goal(o, "robot-return"));
        }

        [Test]
        public void SameMissionAndRoom_GiveTheSameObjectivesEverywhere()
        {
            CrewObjectives a = CrewObjectives.For("mora-lander", "HZN-7Q4K"), b = CrewObjectives.For("mora-lander", "HZN-7Q4K");
            Assert.AreEqual(a.Goals.Count, b.Goals.Count);
            for (int i = 0; i < a.Goals.Count; i++)
            {
                Assert.AreEqual(a.Goals[i].Id, b.Goals[i].Id);
                Assert.AreEqual(a.Goals[i].Count, b.Goals[i].Count);
            }
        }

        [Test]
        public void EngineerGoal_IsMetByTheModuleOnTheRocket()
        {
            CrewObjectives o = CrewObjectives.For("weather-watch", "R");
            int cam = Index(o, "eng-camera");
            var design = RocketBlueprint.CreateDefault();
            o.Update(new CrewMissionFacts { RocketModules = CrewObjectives.ModulesOf(design) });
            Assert.IsFalse(o.IsDone(cam));
            design.payloadModules.Add(ModuleCatalog.Camera);
            List<int> now = o.Update(new CrewMissionFacts { RocketModules = CrewObjectives.ModulesOf(design) });
            Assert.IsTrue(o.IsDone(cam));
            CollectionAssert.Contains(now, cam);
        }

        [Test]
        public void PilotGoals_NeedTheTargetFrame_AndStayDone()
        {
            CrewObjectives o = CrewObjectives.For("mora-lander", "R");
            int reach = Index(o, "pilot-reach"), land = Index(o, "pilot-land");
            o.Update(new CrewMissionFacts { FrameBodyId = HomeSystemCatalog.HomeWorldId, Landed = true });
            Assert.IsFalse(o.IsDone(reach));
            Assert.IsFalse(o.IsDone(land)); // landed, but at home
            o.Update(new CrewMissionFacts { FrameBodyId = HomeSystemCatalog.MoraMoonId });
            Assert.IsTrue(o.IsDone(reach));
            o.Update(new CrewMissionFacts { FrameBodyId = HomeSystemCatalog.MoraMoonId, Landed = true, Destroyed = true });
            Assert.IsFalse(o.IsDone(land)); // a crash is not a landing
            o.Update(new CrewMissionFacts { FrameBodyId = HomeSystemCatalog.MoraMoonId, Landed = true });
            Assert.IsTrue(o.IsDone(land));
            o.Update(new CrewMissionFacts { FrameBodyId = HomeSystemCatalog.HomeWorldId });
            Assert.IsTrue(o.IsDone(reach) && o.IsDone(land));
        }

        [Test]
        public void OrbitGoal_NeedsAStableOrbitInFlight()
        {
            CrewObjectives o = CrewObjectives.For("quoris-mapper", "R");
            int orbit = Index(o, "pilot-orbit");
            o.Update(new CrewMissionFacts { FrameBodyId = HomeSystemCatalog.QuorisId, StableOrbit = false });
            Assert.IsFalse(o.IsDone(orbit));
            o.Update(new CrewMissionFacts { FrameBodyId = HomeSystemCatalog.QuorisId, StableOrbit = true });
            Assert.IsTrue(o.IsDone(orbit));
        }

        [Test]
        public void RobotReturn_CountsOnlyAfterPhotosAndSamples()
        {
            CrewObjectives o = CrewObjectives.For("mora-lander", "R");
            int photos = Index(o, "robot-photos"), samples = Index(o, "robot-samples"), back = Index(o, "robot-return");
            var f = new CrewMissionFacts { FrameBodyId = HomeSystemCatalog.MoraMoonId, Landed = true, RobotOutside = false };
            o.Update(f);
            Assert.IsFalse(o.IsDone(back));
            f.Photos = o.Goals[photos].Count;
            f.SamplesOnTarget = o.Goals[samples].Count;
            f.RobotOutside = true;
            o.Update(f);
            Assert.IsTrue(o.IsDone(photos) && o.IsDone(samples));
            Assert.IsFalse(o.IsDone(back));
            f.RobotOutside = false;
            o.Update(f);
            Assert.IsTrue(o.IsDone(back));
            Assert.IsTrue(o.AllDone || o.DoneCount(CrewRole.RobotOperator) == o.CountOf(CrewRole.RobotOperator));
        }

        [Test]
        public void Progress_IsCappedAndNeverGoesBack()
        {
            CrewObjectives o = CrewObjectives.For("mora-lander", "R");
            int photos = Index(o, "robot-photos");
            o.Update(new CrewMissionFacts { Photos = 2 });
            Assert.AreEqual(2, o.Progress(photos));
            o.Update(new CrewMissionFacts { Photos = 0 });
            Assert.AreEqual(2, o.Progress(photos));
            o.Update(new CrewMissionFacts { Photos = 999 });
            Assert.AreEqual(o.Goals[photos].Count, o.Progress(photos));
        }

        [Test]
        public void PhotoRules_SurfaceNeedsTheRobotOutsideAndANewPlace()
        {
            var surface = CrewMissionSurvey.TargetOf("mora-lander");
            string moon = HomeSystemCatalog.MoraMoonId;
            Assert.IsNotEmpty(CrewPhotoRules.Check(surface, HomeSystemCatalog.HomeWorldId, true, true, false, true, 100, 100));
            Assert.IsNotEmpty(CrewPhotoRules.Check(surface, moon, false, true, false, true, 100, 100));
            Assert.IsNotEmpty(CrewPhotoRules.Check(surface, moon, true, true, false, true, 3, 100));
            Assert.IsEmpty(CrewPhotoRules.Check(surface, moon, true, true, false, false, 12, 0));
        }

        [Test]
        public void PhotoRules_OrbitNeedsCameraOrbitAndTimeBetweenShots()
        {
            var orbit = CrewMissionSurvey.TargetOf("quoris-mapper");
            string q = HomeSystemCatalog.QuorisId;
            Assert.IsNotEmpty(CrewPhotoRules.Check(orbit, q, false, false, true, false, 0, 100)); // no camera
            Assert.IsNotEmpty(CrewPhotoRules.Check(orbit, q, false, false, false, true, 0, 100)); // no orbit
            Assert.IsNotEmpty(CrewPhotoRules.Check(orbit, q, false, false, true, true, 0, 5)); // too soon
            Assert.IsEmpty(CrewPhotoRules.Check(orbit, q, false, false, true, true, 0, 31));
        }
    }
}
