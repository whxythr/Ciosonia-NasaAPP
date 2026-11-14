using NUnit.Framework;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    public class CabinRulesTests
    {
        [Test]
        public void OnlyCrewedCabinsCanBeEntered()
        {
            Assert.IsTrue(CabinRules.CanEnter(ModuleCatalog.Find(ModuleCatalog.CabinCrew1)));
            Assert.IsTrue(CabinRules.CanEnter(ModuleCatalog.Find("cab-crew-3")));
            Assert.IsTrue(CabinRules.CanEnter(ModuleCatalog.Find("cab-crew-7")));
            Assert.IsFalse(CabinRules.CanEnter(ModuleCatalog.Find(ModuleCatalog.ProbeCore)), "probe core: remote control");
            Assert.IsFalse(CabinRules.CanEnter(ModuleCatalog.Find("cab-rc-small")));
            Assert.IsFalse(CabinRules.CanEnter(ModuleCatalog.Find("tank-k10")), "not a cabin");
            Assert.IsFalse(CabinRules.CanEnter(null));
        }

        [Test]
        public void InteriorFollowsSeatCount()
        {
            Assert.AreEqual(CockpitInterior.Single, CabinRules.InteriorOf(ModuleCatalog.Find(ModuleCatalog.CabinCrew1)));
            Assert.AreEqual(CockpitInterior.Triple, CabinRules.InteriorOf(ModuleCatalog.Find("cab-crew-3")));
            Assert.AreEqual(CockpitInterior.Seven, CabinRules.InteriorOf(ModuleCatalog.Find("cab-crew-7")));
            Assert.AreEqual(CockpitInterior.None, CabinRules.InteriorOf(ModuleCatalog.Find(ModuleCatalog.ProbeCore)));
            foreach (string id in new[] { "cab-crew-1", "cab-crew-3", "cab-crew-7" })
            {
                ModuleDefinition m = ModuleCatalog.Find(id);
                Assert.AreEqual(m.crewCapacity, CabinRules.SeatCount(CabinRules.InteriorOf(m)), id + ": one seat per crew member");
            }
            Assert.AreEqual(0, CabinRules.SeatCount(CockpitInterior.None));
        }

        [Test]
        public void CrewedCabin_PicksTheLargestCrewedOne()
        {
            var probe = RocketBlueprint.CreateDefault();
            RocketLayout plain = RocketLayout.Build(probe);
            Assert.IsNull(CabinRules.CrewedCabin(plain.Parts), "default rocket flies the probe core: no interior");

            var crewed = RocketBlueprint.CreateDefault();
            crewed.payloadModules.Add(ModuleCatalog.CabinCrew1);
            crewed.payloadModules.Add("cab-crew-3");
            RocketLayout layout = RocketLayout.Build(crewed);
            PartPlacement cabin = CabinRules.CrewedCabin(layout.Parts);
            Assert.IsNotNull(cabin);
            Assert.AreEqual("cab-crew-3", cabin.ModuleId);
            Assert.AreEqual(PartRole.Capsule, cabin.Role);
            Assert.AreEqual(-1, cabin.Stage, "cabins sit in the payload section: staging never drops them");
        }

        [Test]
        public void HiddenFromInside_HullWrapsAndFairingOnly()
        {
            // A crewed-only payload: the radiation shield wraps the capsule itself.
            var bp = RocketBlueprint.CreateDefault();
            bp.payloadModules.Clear();
            bp.payloadModules.Add(ModuleCatalog.CabinCrew1);
            bp.payloadModules.Add("prot-rad1");
            bp.payloadModules.Add("sci-therm");
            RocketLayout layout = RocketLayout.Build(bp);
            PartPlacement cabin = CabinRules.CrewedCabin(layout.Parts);
            Assert.IsNotNull(cabin);
            int wraps = 0;
            foreach (PartPlacement p in layout.Parts)
            {
                bool hidden = CabinRules.HiddenFromInside(p, cabin);
                if (p == cabin || p.Role == PartRole.Fairing) Assert.IsTrue(hidden, p.Role.ToString());
                else if (p.Role == PartRole.Wrap)
                {
                    wraps++;
                    Assert.IsTrue(hidden, "the shield wrapped round the cabin would cover the windows");
                }
                else Assert.IsFalse(hidden, p.Role + " " + p.ModuleId);
            }
            Assert.AreEqual(1, wraps);

            // With a probe core on board the shield wraps the bus instead: the capsule's windows stay clear of it.
            var mixed = RocketBlueprint.CreateDefault();
            mixed.payloadModules.Add(ModuleCatalog.CabinCrew1);
            mixed.payloadModules.Add("prot-rad1");
            RocketLayout second = RocketLayout.Build(mixed);
            PartPlacement capsule = CabinRules.CrewedCabin(second.Parts);
            foreach (PartPlacement p in second.Parts)
                if (p.Role == PartRole.Wrap)
                    Assert.AreEqual(p.Bottom < capsule.Top && p.Top > capsule.Bottom, CabinRules.HiddenFromInside(p, capsule), "only a shell round the capsule is hidden");
        }

        [Test]
        public void ZoomGate_NeedsDeliberatePushAtMinimum()
        {
            var gate = new CabinRules.ZoomGate();
            Assert.IsFalse(gate.Update(false, 3.0, 0.0), "not at the minimum yet: just zooming");
            Assert.IsFalse(gate.Update(true, 1.0, 1.0), "one notch is not enough");
            Assert.IsTrue(gate.Update(true, 1.0, 1.3), "second notch soon after: go inside");
            Assert.AreEqual(0.0, gate.Pushed);

            Assert.IsFalse(gate.Update(true, 1.0, 5.0));
            Assert.IsFalse(gate.Update(true, 1.0, 5.0 + CabinRules.PushMemorySeconds + 0.5), "a long pause forgets the first push");
            Assert.IsFalse(gate.Update(true, -1.0, 7.0), "zooming out resets");
            Assert.IsFalse(gate.Update(true, 1.0, 7.1));
            Assert.IsFalse(gate.Update(false, 1.0, 7.2), "moved away from the minimum resets");
            Assert.IsFalse(gate.Update(true, 1.0, 7.3));
            Assert.IsTrue(gate.Update(true, 0.6, 7.4));
        }

        [Test]
        public void AtMinimum_UsesTolerance()
        {
            Assert.IsTrue(CabinRules.AtMinimum(4.0, 4.0));
            Assert.IsTrue(CabinRules.AtMinimum(4.2, 4.0));
            Assert.IsFalse(CabinRules.AtMinimum(5.0, 4.0));
        }

        [Test]
        public void HeadTurnIsLimited()
        {
            double yaw = 170.0, pitch = -80.0;
            CabinRules.ClampHead(ref yaw, ref pitch);
            Assert.AreEqual(CabinRules.MaxHeadYaw, yaw);
            Assert.AreEqual(CabinRules.MinHeadPitch, pitch);
            yaw = -30.0;
            pitch = 20.0;
            CabinRules.ClampHead(ref yaw, ref pitch);
            Assert.AreEqual(-30.0, yaw);
            Assert.AreEqual(20.0, pitch);
        }

        [Test]
        public void SeatLoadAndHeadMotion()
        {
            Assert.AreEqual(1.0, CabinRules.SeatLoad(true, 9.80665, 50.0, 0.0), 1e-12, "on the ground: gravity, thrust ignored");
            Assert.AreEqual(0.0, CabinRules.SeatLoad(false, 9.8, 0.0, 0.0), 1e-12, "coasting: free fall");
            Assert.AreEqual(3.0, CabinRules.SeatLoad(false, 9.8, 3.5 * 9.80665, 0.5 * 9.80665), 1e-12);
            Assert.AreEqual(0.012, CabinRules.HeadSag(1.0), 1e-12);
            Assert.AreEqual(0.06, CabinRules.HeadSag(20.0), 1e-12, "capped");
            Assert.AreEqual(-0.01, CabinRules.HeadSag(-5.0), 1e-12);
            Assert.AreEqual(0.0, CabinRules.ShakeAmplitude(false, 0.0, 0.0));
            double weak = CabinRules.ShakeAmplitude(true, 1e-4, 0.0), strong = CabinRules.ShakeAmplitude(true, 30.0, 0.0);
            Assert.Less(weak, 0.01, "an ion engine barely hums");
            Assert.Greater(strong, weak);
            Assert.Greater(CabinRules.ShakeAmplitude(false, 0.0, 30000.0), 0.3, "max-q buffeting");
            Assert.LessOrEqual(CabinRules.ShakeAmplitude(true, 1000.0, 1e6), 1.2);
        }

        [Test]
        public void CommLink_SignalAndLightDelay()
        {
            var home = new CommLinkStatus(2e6, 0.0);
            Assert.IsTrue(home.Connected);
            Assert.AreEqual(4, home.Bars);
            Assert.AreEqual(0.0, home.OneWayDelay);

            var near = new CommLinkStatus(2e6, 1e6);
            Assert.AreEqual(1.0, near.Strength, 1e-12);
            Assert.AreEqual(4.0, near.Margin, 1e-12);
            Assert.AreEqual(2, near.Bars);
            Assert.IsFalse(near.Weak);

            var edge = new CommLinkStatus(2e6, 1.8e6);
            Assert.IsTrue(edge.Weak);
            Assert.AreEqual(1, edge.Bars);

            var far = new CommLinkStatus(2e6, 4e6);
            Assert.IsFalse(far.Connected);
            Assert.AreEqual(0.25, far.Strength, 1e-12, "(R/d)²");
            Assert.AreEqual(0, far.Bars);

            // One astronomical unit: about 499 s of light time.
            var au = new CommLinkStatus(5e12, 1.495978707e11);
            Assert.AreEqual(499.0, au.OneWayDelay, 0.1);
            Assert.AreEqual(2.0 * au.OneWayDelay, au.RoundTripDelay, 1e-9);

            Assert.IsFalse(new CommLinkStatus(0.0, 10.0).Connected, "no antenna");
            Assert.AreEqual(0.0, CommLinkStatus.StationDistance(6.0e6, 6.4e6), "on the home world");
            Assert.AreEqual(1.0e6, CommLinkStatus.StationDistance(7.4e6, 6.4e6), 1e-6);
        }
    }
}
