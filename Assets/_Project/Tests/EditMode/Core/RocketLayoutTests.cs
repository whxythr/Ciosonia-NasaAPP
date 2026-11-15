using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    /// <summary>Geometry of a design (hangar preview and flight visual): it must agree with what the compiler gives the physics.</summary>
    public class RocketLayoutTests
    {
        static List<PartPlacement> All(RocketLayout layout, PartRole role, int stage = int.MinValue)
        {
            var list = new List<PartPlacement>();
            foreach (PartPlacement p in layout.Parts)
                if (p.Role == role && (stage == int.MinValue || p.Stage == stage)) list.Add(p);
            return list;
        }

        [Test]
        public void StageHeights_AreTheCompiledStageLengths()
        {
            var b = RocketBlueprint.CreateDefault();
            b.stages.Add(new StageBlueprint { name = "Пустая", engineId = "", engineCount = 1 }); // no engine, no tanks → 1 m
            var compiled = BlueprintCompiler.Compile(b);
            var layout = RocketLayout.Build(b, compiled.Configuration.radius);
            Assert.AreEqual(3, layout.StageCount);
            double expectedBottom = 0.0;
            for (int i = 0; i < layout.StageCount; i++)
            {
                Assert.AreEqual(expectedBottom, layout.StageBottom[i], 1e-9, "stages are stacked without gaps");
                Assert.AreEqual(compiled.Configuration.stages[i].length, layout.StageTop[i] - layout.StageBottom[i], 1e-9, "stage " + i);
                expectedBottom = layout.StageTop[i];
            }
            Assert.AreEqual(36.0, layout.StageTop[0], 1e-9, "Сокол-1 (3 m) + 3 × K-100 (11 m)");
            Assert.AreEqual(1, All(layout, PartRole.StageSpacer, 2).Count, "the empty stage still has its 1 m of structure");
            Assert.AreEqual(layout.StageTop[2], layout.PayloadBottom, 1e-9);
            Assert.Greater(layout.FairingTop, layout.PayloadTop);
        }

        [Test]
        public void EveryModuleOfTheDesign_HasExactlyOneSlot()
        {
            var b = RocketBlueprint.CreateDefault();
            b.payloadModules.Add(ModuleCatalog.NavComputer);
            b.payloadModules.Add(ModuleCatalog.RadiationShield);
            b.payloadModules.Add(ModuleCatalog.Spectrometer);
            b.payloadModules.Add(ModuleCatalog.AcidCoating);
            b.payloadModules.Add(ModuleCatalog.CollectorBasic);
            var layout = RocketLayout.Build(b);

            Assert.AreEqual(4, All(layout, PartRole.Engine, 0).Count, "4 × Сокол-1");
            Assert.AreEqual(1, All(layout, PartRole.Engine, 1).Count, "Вега-В");
            Assert.AreEqual(3, All(layout, PartRole.Tank, 0).Count);
            for (int t = 0; t < 3; t++) Assert.IsNotNull(layout.Find(PartRole.Tank, 0, t), "tank index = position in the stage");
            for (int p = 0; p < b.payloadModules.Count; p++)
            {
                int found = 0;
                foreach (PartPlacement part in layout.Parts)
                    if (part.Stage == -1 && part.Index == p && part.IsModule) found++;
                Assert.AreEqual(1, found, b.payloadModules[p]);
            }
            Assert.AreEqual(ModuleCatalog.RadiationShield, layout.Find(PartRole.Wrap, -1, 2).ModuleId);
            Assert.AreEqual(ModuleCatalog.CollectorBasic, layout.Find(PartRole.Instrument, -1, 5).ModuleId);
            double firstAngle = layout.Find(PartRole.Instrument, -1, 1).AngleDegrees;
            Assert.IsTrue(firstAngle <= 70.0 || firstAngle >= 290.0, "the first instrument is on the front arc: " + firstAngle);
            var single = RocketBlueprint.CreateDefault();
            single.payloadModules.Add(ModuleCatalog.StarTracker);
            Assert.AreEqual(0.0, RocketLayout.Build(single).Find(PartRole.Instrument, -1, 1).AngleDegrees, 1e-9, "a lone instrument faces the front");
            Assert.LessOrEqual(layout.Find(PartRole.Instrument, -1, 5).OuterRadius, layout.Radius, "a collector fits inside the rocket outline");
        }

        [Test]
        public void EngineClusters_FitInsideTheStage_WithoutOverlap()
        {
            for (int n = 1; n <= BlueprintCompiler.MaxEnginesPerStage; n++)
            {
                var b = RocketBlueprint.CreateDefault();
                b.stages[0].engineCount = n;
                var layout = RocketLayout.Build(b);
                List<PartPlacement> engines = All(layout, PartRole.Engine, 0);
                Assert.AreEqual(n, engines.Count);
                for (int i = 0; i < engines.Count; i++)
                {
                    Assert.LessOrEqual(engines[i].OuterRadius, layout.Radius + 1e-9, n + " engines: inside the stage wall");
                    Assert.AreEqual(0.0, engines[i].Bottom, 1e-12, "bells end at the bottom of the stage");
                    for (int j = i + 1; j < engines.Count; j++)
                    {
                        double dx = engines[i].X - engines[j].X, dz = engines[i].Z - engines[j].Z;
                        Assert.GreaterOrEqual(Math.Sqrt(dx * dx + dz * dz), engines[i].Diameter, n + " engines: bells " + i + "/" + j + " overlap");
                    }
                }
            }
            var single = RocketLayout.Build(RocketBlueprint.CreateDefault());
            Assert.AreEqual(2.4, All(single, PartRole.Engine, 1)[0].Diameter, 1e-9, "one Вега-В keeps its own bell");
            var many = RocketBlueprint.CreateDefault();
            many.stages[0].engineCount = 20;
            Assert.AreEqual(BlueprintCompiler.MaxEnginesPerStage, All(RocketLayout.Build(many), PartRole.Engine, 0).Count, "clamped like the compiler");
        }

        [Test]
        public void ShieldsWrapTheBus_InstrumentsSitOutside_FairingCoversAll()
        {
            var b = RocketBlueprint.CreateDefault();
            b.payloadModules.Add(ModuleCatalog.RadiationShield);
            b.payloadModules.Add(ModuleCatalog.RadiationShieldHeavy);
            for (int i = 0; i < 9; i++) b.payloadModules.Add(i % 2 == 0 ? ModuleCatalog.StarTracker : ModuleCatalog.CollectorAdvanced);
            var layout = RocketLayout.Build(b);

            PartPlacement bus = All(layout, PartRole.ProbeBus)[0];
            List<PartPlacement> wraps = All(layout, PartRole.Wrap);
            Assert.AreEqual(2, wraps.Count);
            Assert.Greater(wraps[0].Diameter, bus.Diameter);
            Assert.Greater(wraps[1].Diameter, wraps[0].Diameter, "the second shield goes around the first");
            Assert.AreEqual(bus.Bottom, wraps[0].Bottom, 1e-9);
            Assert.AreEqual(bus.Height, wraps[0].Height, 1e-9);

            var used = new HashSet<string>();
            foreach (PartPlacement p in All(layout, PartRole.Instrument))
            {
                Assert.GreaterOrEqual(p.RadialOffset - 0.5 * p.Diameter, 0.5 * wraps[1].Diameter - 1e-9, "outside the outer shield");
                Assert.GreaterOrEqual(p.Bottom, bus.Bottom - 1e-9);
                Assert.LessOrEqual(p.Top, bus.Top + 1e-9, "rows stay on the bus");
                Assert.Greater(Math.Abs(p.AngleDegrees - 90.0), 20.0, "the +X side is kept for the solar wing");
                Assert.Greater(Math.Abs(p.AngleDegrees - 270.0), 20.0, "the −X side is kept for the solar wing");
                Assert.IsTrue(used.Add(Math.Round(p.AngleDegrees, 3) + "@" + Math.Round(p.Center, 3)), "two instruments in one spot");
            }
            foreach (PartPlacement p in layout.Parts)
            {
                if (p.Stage != -1 || p.Role == PartRole.Fairing) continue;
                Assert.Less(p.OuterRadius, layout.FairingRadius, p.ModuleId + " inside the fairing");
                Assert.Less(p.Top, layout.PayloadBottom + layout.FairingCylinderHeight, p.ModuleId + " under the nose");
            }
        }

        [Test]
        public void SkippedModules_AreNotDrawn_AndTheCoreIsAlwaysThere()
        {
            var b = RocketBlueprint.CreateDefault();
            b.payloadModules.Clear();                             // the compiler adds the core itself
            b.payloadModules.Add(ModuleCatalog.CollectorAdvanced); // shop module, not bought
            b.payloadModules.Add("no-such-module");
            b.stages[0].tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK140, propellantLoaded = 1 }); // not bought
            Func<string, bool> owns = id => id != ModuleCatalog.CollectorAdvanced && id != ModuleCatalog.TankK140; // only the shop parts are missing
            var compiled = BlueprintCompiler.Compile(b, owns);
            var layout = RocketLayout.Build(b, RocketLayout.DefaultRadius, owns);

            Assert.AreEqual(compiled.Configuration.stages[0].length, layout.StageTop[0], 1e-9, "a locked tank adds no length");
            Assert.AreEqual(3, All(layout, PartRole.Tank, 0).Count);
            Assert.AreEqual(0, All(layout, PartRole.Instrument).Count);
            List<PartPlacement> bus = All(layout, PartRole.ProbeBus);
            Assert.AreEqual(1, bus.Count);
            Assert.AreEqual(ModuleCatalog.ProbeCore, bus[0].ModuleId);
            Assert.AreEqual(-1, bus[0].Index, "added automatically, not from the list");

            var bought = RocketLayout.Build(b, RocketLayout.DefaultRadius, id => true);
            Assert.AreEqual(1, All(bought, PartRole.Instrument).Count, "bought → installed and drawn");
            Assert.AreEqual(4, All(bought, PartRole.Tank, 0).Count);
        }
        // ---- Stage-side sets and the new payload parts ----------------------------------------------------------

        /// <summary>Every catalog module where it belongs: side sets on the stages, the rest in the payload section.</summary>
        static RocketBlueprint KitchenSink()
        {
            var b = RocketBlueprint.CreateDefault();
            b.payloadModules.Clear();
            foreach (ModuleDefinition m in ModuleCatalog.All)
            {
                if (m.IsPayload) b.payloadModules.Add(m.id);
                else if (m.IsStageAttachment) b.stages[m.aeroKind == AeroKind.TopFin || m.aeroKind == AeroKind.Rcs ? 1 : 0].attachments.Add(m.id);
            }
            return b;
        }

        static int Pieces(ModuleDefinition m) => m.auxiliaryKind == AuxiliaryKind.LandingLegs ? m.legCount : 4;

        static bool Allowed(PartPlacement a, PartPlacement b)
        {
            bool Pair(PartRole x, PartRole y) => (a.Role == x && b.Role == y) || (a.Role == y && b.Role == x);
            if (a.Role == PartRole.Fairing || b.Role == PartRole.Fairing) return true;
            if (Pair(PartRole.EngineSection, PartRole.Engine) && a.Stage == b.Stage) return true;
            if (a.Role == PartRole.Wrap && (b.Role == PartRole.Wrap || b.Role == PartRole.ProbeBus || b.Role == PartRole.Capsule)) return true;
            if (b.Role == PartRole.Wrap && (a.Role == PartRole.ProbeBus || a.Role == PartRole.Capsule)) return true;
            if (Pair(PartRole.RoverChassis, PartRole.RoverDrive) || Pair(PartRole.RoverChassis, PartRole.Wheel) || Pair(PartRole.RoverChassis, PartRole.RoverChassis)) return true;
            if (Pair(PartRole.Wheel, PartRole.RoverDrive)) return true;
            if (a.Role == PartRole.Instrument && a.Inside && b.Role == PartRole.CargoBay) return true;
            if (b.Role == PartRole.Instrument && b.Inside && a.Role == PartRole.CargoBay) return true;
            return false;
        }

        static void AssertNoForbiddenOverlap(RocketLayout layout, string what)
        {
            for (int i = 0; i < layout.Parts.Count; i++)
                for (int j = i + 1; j < layout.Parts.Count; j++)
                {
                    PartPlacement a = layout.Parts[i], b = layout.Parts[j];
                    if (Allowed(a, b)) continue;
                    Assert.IsFalse(RocketLayout.Overlap(a, b), what + ": " + a.Role + " " + a.ModuleId + "#" + a.Element + " overlaps " + b.Role + " " + b.ModuleId + "#" + b.Element);
                }
        }

        static void AssertFairingCoversPayload(RocketLayout layout, string what)
        {
            foreach (PartPlacement p in layout.Parts)
            {
                if (p.Stage != -1 || p.Role == PartRole.Fairing) continue;
                Assert.GreaterOrEqual(p.Bottom, layout.PayloadBottom - 1e-9, what + ": " + p.ModuleId + " above the last stage");
                Assert.Less(p.OuterRadius, layout.FairingRadius, what + ": " + p.ModuleId + " inside the fairing");
                Assert.Less(p.Top, layout.PayloadBottom + layout.FairingCylinderHeight, what + ": " + p.ModuleId + " under the nose");
            }
        }

        [Test]
        public void EveryInstalledPart_GetsASlot_IncludingSideSets()
        {
            RocketBlueprint b = KitchenSink();
            var layout = RocketLayout.Build(b);
            for (int p = 0; p < b.payloadModules.Count; p++)
            {
                int found = 0;
                foreach (PartPlacement part in layout.Parts)
                    if (part.Stage == -1 && part.Index == p && part.IsModule) found++;
                Assert.AreEqual(1, found, b.payloadModules[p]);
            }
            for (int s = 0; s < b.stages.Count; s++)
                for (int k = 0; k < b.stages[s].attachments.Count; k++)
                {
                    ModuleDefinition m = ModuleCatalog.Find(b.stages[s].attachments[k]);
                    var elements = new HashSet<int>();
                    foreach (PartPlacement part in layout.Parts)
                        if (part.Stage == s && part.Index == k && part.IsStageSide)
                        {
                            Assert.AreEqual(m.id, part.ModuleId);
                            Assert.IsTrue(elements.Add(part.Element), m.id + ": piece numbers are unique");
                        }
                    Assert.AreEqual(Pieces(m), elements.Count, m.id + ": one slot per fin / leg / block");
                }
            Assert.AreEqual(2, All(layout, PartRole.Wheel).Count, "one slot per wheel module");
        }

        [Test]
        public void SideSets_SitWhereTheyBelong_AndDoNotChangeStageLengths()
        {
            var b = RocketBlueprint.CreateDefault();
            b.stages[0].attachments.Add(ModuleCatalog.FinSideLarge);
            b.stages[0].attachments.Add(ModuleCatalog.LegsHeavy);
            b.stages[0].attachments.Add(ModuleCatalog.FinTopLarge);
            b.stages[0].attachments.Add(ModuleCatalog.RcsLarge);
            b.stages[1].attachments.Add(ModuleCatalog.FinSideSmall);
            b.stages[1].attachments.Add(ModuleCatalog.RcsSmall);
            b.stages[1].attachments.Add(ModuleCatalog.Spectrometer);   // payload module on a stage: skipped
            b.payloadModules.Add(ModuleCatalog.FinSideSmall);          // side set in the payload: skipped
            var compiled = BlueprintCompiler.Compile(b);
            var layout = RocketLayout.Build(b, compiled.Configuration.radius);

            var plain = RocketLayout.Build(RocketBlueprint.CreateDefault(), compiled.Configuration.radius);
            for (int i = 0; i < layout.StageCount; i++)
            {
                Assert.AreEqual(compiled.Configuration.stages[i].length, layout.StageTop[i] - layout.StageBottom[i], 1e-9, "stage " + i);
                Assert.AreEqual(plain.StageTop[i], layout.StageTop[i], 1e-9, "side sets add no length");
            }
            Assert.AreEqual(0, layout.Parts.FindAll(p => p.ModuleId == ModuleCatalog.Spectrometer).Count, "wrong mount is not drawn");
            Assert.AreEqual(0, layout.Parts.FindAll(p => p.Stage == -1 && p.ModuleId == ModuleCatalog.FinSideSmall).Count);

            double r = layout.Radius;
            foreach (PartPlacement p in layout.Parts)
            {
                if (!p.IsStageSide) continue;
                double bottom = layout.StageBottom[p.Stage], top = layout.StageTop[p.Stage], mid = 0.5 * (bottom + top);
                Assert.AreEqual(r + 0.5 * p.Diameter, p.RadialOffset, 1e-9, p.ModuleId + " stands on the hull");
                Assert.GreaterOrEqual(p.Bottom, bottom - 1e-9);
                Assert.LessOrEqual(p.Top, top + 1e-9);
                switch (p.Role)
                {
                    case PartRole.SideFin: Assert.Less(p.Top, mid, "tail fins low"); break;
                    case PartRole.LandingLeg: Assert.AreEqual(p.Stage == 0 ? RocketLayout.SkirtOpening * 3.0 : bottom, p.Bottom, 1e-9, "legs hinge at the skirt edge, like the fins"); break;
                    case PartRole.GridFin:
                    case PartRole.RcsBlock: Assert.AreEqual(top, p.Top, 1e-9, "at the top of the stage"); break;
                }
            }
            PartPlacement fin = All(layout, PartRole.SideFin, 0)[0];
            Assert.AreEqual(RocketLayout.SkirtOpening * 3.0, fin.Bottom, 1e-9, "stage-1 fins start at the skirt edge");
            PartPlacement heavyLeg = All(layout, PartRole.LandingLeg, 0)[0];
            Assert.Less(heavyLeg.Bottom - RocketLayout.DeployedLegDrop(heavyLeg), layout.StageBottom[0], "a deployed heavy leg reaches below the bells");
            Assert.AreEqual(4, All(layout, PartRole.LandingLeg, 0).Count);
            AssertNoForbiddenOverlap(layout, "side sets");
        }

        [Test]
        public void CrewedStack_CapsuleOnTop_ShieldBelow_ChuteOnTheNose()
        {
            var b = RocketBlueprint.CreateDefault();
            b.payloadModules.Clear();
            b.payloadModules.Add(ModuleCatalog.ParachuteMedium);
            b.payloadModules.Add(ModuleCatalog.CabinCrew3);
            b.payloadModules.Add(ModuleCatalog.Cargo12);
            b.payloadModules.Add(ModuleCatalog.HeatShieldHeavy);
            b.payloadModules.Add(ModuleCatalog.Thermometer);
            b.payloadModules.Add(ModuleCatalog.DishSmall);
            var layout = RocketLayout.Build(b);

            Assert.AreEqual(0, All(layout, PartRole.ProbeBus).Count, "a crewed cabin: no probe core added");
            PartPlacement capsule = All(layout, PartRole.Capsule)[0];
            PartPlacement shield = All(layout, PartRole.HeatShield)[0];
            PartPlacement bay = All(layout, PartRole.CargoBay)[0];
            PartPlacement chute = All(layout, PartRole.Parachute)[0];
            Assert.AreEqual(layout.PayloadBottom, bay.Bottom, 1e-9, "the bay stands right on the last stage");
            Assert.AreEqual(bay.Top, shield.Bottom, 1e-9);
            Assert.AreEqual(shield.Top, capsule.Bottom, 1e-9, "the shield is right under the capsule");
            Assert.GreaterOrEqual(shield.Diameter, capsule.Diameter);
            Assert.AreEqual(capsule.Top, chute.Bottom, 1e-9, "the chute is on the nose");
            Assert.AreEqual(0.0, chute.RadialOffset, 1e-12);
            PartPlacement therm = layout.Find(PartRole.Instrument, -1, 4);
            Assert.IsTrue(therm.Inside, "science rides inside an open bay");
            Assert.LessOrEqual(therm.OuterRadius, 0.5 * bay.Diameter);
            PartPlacement dish = layout.Find(PartRole.Instrument, -1, 5);
            Assert.IsFalse(dish.Inside, "a dish goes outside");
            Assert.GreaterOrEqual(dish.RadialOffset - 0.5 * dish.Diameter, 0.5 * bay.Diameter);
            AssertNoForbiddenOverlap(layout, "crewed");
            AssertFairingCoversPayload(layout, "crewed");
        }

        [Test]
        public void Rover_WheelsBesideTheFrame_AxlesAcross_NoTouching()
        {
            var b = RocketBlueprint.CreateDefault();
            b.payloadModules.Add(ModuleCatalog.RoverChassis);
            b.payloadModules.Add(ModuleCatalog.RoverDrive);
            for (int i = 0; i < 6; i++) b.payloadModules.Add(ModuleCatalog.WheelLarge);
            b.payloadModules.Add(ModuleCatalog.DrillLarge);
            b.payloadModules.Add(ModuleCatalog.Manipulator);
            var layout = RocketLayout.Build(b);

            PartPlacement frame = All(layout, PartRole.RoverChassis)[0];
            PartPlacement bus = All(layout, PartRole.ProbeBus)[0];
            List<PartPlacement> wheels = All(layout, PartRole.Wheel);
            Assert.AreEqual(6, wheels.Count);
            int left = 0, right = 0;
            foreach (PartPlacement w in wheels)
            {
                Assert.GreaterOrEqual(w.Bottom, layout.PayloadBottom - 1e-9, "on the last stage, not in it");
                Assert.LessOrEqual(w.Top, bus.Bottom + 1e-9, "the rover is under the probe");
                Assert.AreEqual(frame.Center, w.Center, 1e-9, "axles at the frame's height");
                if (w.X > 0.0) right++;
                else left++;
            }
            Assert.AreEqual(3, left);
            Assert.AreEqual(3, right);
            Assert.AreEqual(1, All(layout, PartRole.RoverDrive).Count);
            AssertNoForbiddenOverlap(layout, "rover");
            AssertFairingCoversPayload(layout, "rover");
        }

        [Test]
        public void KitchenSink_NoForbiddenOverlap_FairingCoversAll()
        {
            RocketBlueprint b = KitchenSink();
            var layout = RocketLayout.Build(b);
            AssertNoForbiddenOverlap(layout, "all parts");
            AssertFairingCoversPayload(layout, "all parts");
            var compiled = BlueprintCompiler.Compile(b);
            for (int i = 0; i < layout.StageCount; i++)
                Assert.AreEqual(compiled.Configuration.stages[i].length, layout.StageTop[i] - layout.StageBottom[i], 1e-9);

            // Many instruments on a lone probe: rows are added rather than letting neighbours touch.
            var crowded = RocketBlueprint.CreateDefault();
            for (int i = 0; i < 30; i++) crowded.payloadModules.Add(i % 2 == 0 ? ModuleCatalog.Camera : ModuleCatalog.Gravimeter);
            AssertNoForbiddenOverlap(RocketLayout.Build(crowded), "crowded");
        }

        [Test]
        public void PreviewSlot_ExistsForEveryModule()
        {
            foreach (ModuleDefinition m in ModuleCatalog.All)
            {
                PartPlacement p = RocketLayout.PreviewSlot(m);
                Assert.IsNotNull(p, m.id);
                Assert.AreEqual(m.id, p.ModuleId);
                Assert.Greater(p.Diameter, 0.0, m.id);
                Assert.Greater(p.Height, 0.0, m.id);
                Assert.AreEqual(0.0, p.RadialOffset, m.id);
            }
            Assert.AreEqual(PartRole.Capsule, RocketLayout.PreviewSlot(ModuleCatalog.Find(ModuleCatalog.CabinCrew1)).Role);
            Assert.AreEqual(PartRole.GridFin, RocketLayout.PreviewSlot(ModuleCatalog.Find(ModuleCatalog.FinTopSmall)).Role);
            Assert.AreEqual(PartRole.Wheel, RocketLayout.PreviewSlot(ModuleCatalog.Find(ModuleCatalog.WheelSmall)).Role);
        }

        // ---- Old designs keep their layout ------------------------------------------------------------------

        [Test]
        public void OldDesigns_KeepTheirLayout()
        {
            var designs = new List<RocketBlueprint>();
            designs.Add(RocketBlueprint.CreateDefault());
            var a = RocketBlueprint.CreateDefault();
            a.payloadModules.Add(ModuleCatalog.NavComputer);
            a.payloadModules.Add(ModuleCatalog.RadiationShield);
            a.payloadModules.Add(ModuleCatalog.Spectrometer);
            a.payloadModules.Add(ModuleCatalog.AcidCoating);
            a.payloadModules.Add(ModuleCatalog.CollectorBasic);
            designs.Add(a);
            var c = RocketBlueprint.CreateDefault();
            c.payloadModules.Add(ModuleCatalog.RadiationShield);
            c.payloadModules.Add(ModuleCatalog.RadiationShieldHeavy);
            for (int i = 0; i < 9; i++) c.payloadModules.Add(i % 2 == 0 ? ModuleCatalog.StarTracker : ModuleCatalog.CollectorAdvanced);
            designs.Add(c);
            var d = RocketBlueprint.CreateDefault();
            d.stages[0].engineCount = 9;
            d.stages[1].engineId = ModuleCatalog.EngineHydra;
            d.stages[1].tanks[0].moduleId = ModuleCatalog.TankH20;
            d.stages.Add(new StageBlueprint { name = "3", engineId = ModuleCatalog.EngineIskra, engineCount = 2, attachments = null });
            d.stages[2].tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankN2, propellantLoaded = 2000 });
            d.payloadModules.Add(ModuleCatalog.MeteoKit);
            d.payloadModules.Add(ModuleCatalog.StarTracker);
            designs.Add(d);
            var e = RocketBlueprint.CreateDefault();
            e.stages.Add(new StageBlueprint { name = "Пустая", engineId = "", engineCount = 1 });
            e.payloadModules.Clear();
            e.payloadModules.Add(ModuleCatalog.NavComputer);
            designs.Add(e);

            for (int k = 0; k < designs.Count; k++)
            {
                RocketLayout now = RocketLayout.Build(designs[k]);
                List<PartPlacement> before = LegacyLayout.Build(designs[k], RocketLayout.DefaultRadius, out double fairingRadius, out double fairingCylinder);
                Assert.AreEqual(before.Count, now.Parts.Count, "design " + k + ": same parts");
                for (int i = 0; i < before.Count; i++)
                {
                    PartPlacement x = before[i], y = now.Parts[i];
                    string what = "design " + k + " part " + i + " " + x.Role + " " + x.ModuleId;
                    Assert.AreEqual(x.Role, y.Role, what);
                    Assert.AreEqual(x.ModuleId, y.ModuleId, what);
                    Assert.AreEqual(x.Stage, y.Stage, what);
                    Assert.AreEqual(x.Index, y.Index, what);
                    Assert.AreEqual(x.Bottom, y.Bottom, 1e-9, what);
                    Assert.AreEqual(x.Height, y.Height, 1e-9, what);
                    Assert.AreEqual(x.Diameter, y.Diameter, 1e-9, what);
                    Assert.AreEqual(x.RadialOffset, y.RadialOffset, 1e-9, what);
                    Assert.AreEqual(x.AngleDegrees, y.AngleDegrees, 1e-9, what);
                    Assert.AreEqual(x.Thickness, y.Thickness, 1e-9, what);
                }
                Assert.AreEqual(fairingRadius, now.FairingRadius, 1e-9);
                Assert.AreEqual(fairingCylinder, now.FairingCylinderHeight, 1e-9);
            }
        }

        /// <summary>The layout as it was before stage-side sets and the new payload parts (reference for old designs).</summary>
        static class LegacyLayout
        {
            const double BusShare = 0.5, MinWrapThickness = 0.04, MinInstrumentSize = 0.4, InstrumentScale = 1.4, InstrumentArc = 140.0,
                InstrumentStandoff = 0.06, FairingClearance = 0.15, FairingHeadroom = 0.5, BellGap = 0.94;

            static PartPlacement P(List<PartPlacement> list, PartRole role, string id, int stage, int index, double bottom, double height, double diameter,
                double radial = 0.0, double angle = 0.0)
            {
                var p = new PartPlacement { Role = role, ModuleId = id, Stage = stage, Index = index, Bottom = bottom, Height = height, Diameter = diameter,
                    RadialOffset = radial, AngleDegrees = angle };
                list.Add(p);
                return p;
            }

            public static List<PartPlacement> Build(RocketBlueprint blueprint, double radius, out double fairingRadius, out double fairingCylinder)
            {
                var parts = new List<PartPlacement>();
                double r = radius, y = 0.0;
                for (int i = 0; i < blueprint.stages.Count; i++)
                {
                    StageBlueprint sb = blueprint.stages[i];
                    double bottom = y;
                    ModuleDefinition engine = ModuleCatalog.Find(sb.engineId ?? "");
                    if (engine != null && engine.IsEngine)
                    {
                        double h = Math.Max(0.0, engine.length);
                        P(parts, PartRole.EngineSection, "", i, -1, y, h, 2.0 * r);
                        int count = Math.Max(1, Math.Min(BlueprintCompiler.MaxEnginesPerStage, sb.engineCount));
                        bool centre = count == 1 || count >= 5;
                        int ring = centre ? count - 1 : count;
                        double ringRadius, maxD;
                        if (ring <= 0)
                        {
                            ringRadius = 0.0;
                            maxD = 2.0 * r * BellGap;
                        }
                        else
                        {
                            double best = 0.0, bestRho = 0.5 * r;
                            for (int s = 1; s < 400; s++)
                            {
                                double rho = r * s / 400;
                                double dd = 2.0 * (r - rho);
                                if (ring >= 2) dd = Math.Min(dd, 2.0 * rho * Math.Sin(Math.PI / ring));
                                if (centre) dd = Math.Min(dd, rho);
                                if (dd > best)
                                {
                                    best = dd;
                                    bestRho = rho;
                                }
                            }
                            ringRadius = bestRho;
                            maxD = best * BellGap;
                        }
                        double d = Math.Min(engine.diameter > 0.0 ? engine.diameter : 0.6 * r, maxD);
                        int index = 0;
                        if (centre) P(parts, PartRole.Engine, engine.id, i, index++, y, h, d);
                        for (int k = 0; k < ring; k++) P(parts, PartRole.Engine, engine.id, i, index++, y, h, d, ringRadius, 180.0 / ring + 360.0 * k / ring);
                        y += h;
                    }
                    for (int k = 0; k < sb.tanks.Count; k++)
                    {
                        ModuleDefinition tank = ModuleCatalog.Find(sb.tanks[k].moduleId);
                        if (tank == null || !tank.IsTank) continue;
                        P(parts, PartRole.Tank, tank.id, i, k, y, tank.length, 2.0 * r);
                        y += tank.length;
                    }
                    if (y - bottom < 1.0)
                    {
                        P(parts, PartRole.StageSpacer, "", i, -1, y, bottom + 1.0 - y, 2.0 * r);
                        y = bottom + 1.0;
                    }
                }

                double payloadBottom = y;
                double busBottom = y;
                var cores = new List<(ModuleDefinition, int)>();
                var wraps = new List<(ModuleDefinition, int)>();
                var instruments = new List<(ModuleDefinition, int)>();
                for (int p = 0; p < blueprint.payloadModules.Count; p++)
                {
                    ModuleDefinition m = ModuleCatalog.Find(blueprint.payloadModules[p]);
                    if (m == null || !m.IsPayload) continue;
                    if (m.IsCabin) cores.Add((m, p));
                    else if (m.IsWrap) wraps.Add((m, p));
                    else instruments.Add((m, p));
                }
                if (cores.Count == 0) cores.Add((ModuleCatalog.Find(ModuleCatalog.ProbeCore), -1));
                ModuleDefinition first = cores[0].Item1;
                double busDiameter = Math.Min(first.diameter > 0.0 ? first.diameter : r, 2.0 * r * BusShare);
                double yy = busBottom;
                foreach (var core in cores)
                {
                    double h = Math.Max(0.5, core.Item1.length);
                    P(parts, PartRole.ProbeBus, core.Item1.id, -1, core.Item2, yy, h, busDiameter);
                    yy += h;
                }
                double busTop = yy, busHeight = busTop - busBottom, outer = busDiameter;
                foreach (var wrap in wraps)
                {
                    double t = Math.Max(MinWrapThickness, 0.25 * wrap.Item1.length);
                    outer += 2.0 * t;
                    P(parts, PartRole.Wrap, wrap.Item1.id, -1, wrap.Item2, busBottom, busHeight, outer).Thickness = t;
                }
                double top = busTop;
                int total = instruments.Count;
                if (total > 0)
                {
                    int perRow = Math.Max(6, Math.Min(16, (total + 1) / 2));
                    int rows = (total + perRow - 1) / perRow;
                    double pitch = busHeight / rows, mount = 0.5 * outer + InstrumentStandoff, room = r - FairingClearance - mount;
                    for (int i = 0; i < total; i++)
                    {
                        int row = i / perRow, col = i % perRow, inRow = Math.Min(perRow, total - row * perRow);
                        double wanted = Math.Max(MinInstrumentSize, InstrumentScale * instruments[i].Item1.length);
                        double size = Math.Min(Math.Max(MinInstrumentSize, Math.Min(wanted, room)), 0.95 * pitch);
                        bool front = (col % 2 == 0) == (row % 2 == 0);
                        int onArc = col % 2 == 0 ? (inRow + 1) / 2 : inRow / 2;
                        double angle = (front ? 0.0 : 180.0) + InstrumentArc * ((col / 2 + 0.5) / onArc - 0.5);
                        if (angle < 0.0) angle += 360.0;
                        double centre = busBottom + pitch * (row + 0.5);
                        PartPlacement p = P(parts, PartRole.Instrument, instruments[i].Item1.id, -1, instruments[i].Item2, centre - 0.5 * size, size, size,
                            mount + 0.5 * size, angle);
                        top = Math.Max(top, p.Top);
                    }
                }
                double reach = 0.0;
                foreach (PartPlacement p in parts)
                    if (p.Stage == -1) reach = Math.Max(reach, p.OuterRadius);
                fairingRadius = Math.Max(r, reach + FairingClearance);
                fairingCylinder = top - payloadBottom + FairingHeadroom;
                P(parts, PartRole.Fairing, "", -1, -1, payloadBottom, fairingCylinder + 1.5 * fairingRadius, 2.0 * fairingRadius);
                return parts;
            }
        }
    }
}
