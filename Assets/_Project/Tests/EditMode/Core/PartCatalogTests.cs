using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SpaceSim.Economy;
using SpaceSim.Flight;
using SpaceSim.Physics;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    public class PartCatalogTests
    {
        static List<ModuleDefinition> In(ModuleSubgroup g) => ModuleCatalog.All.Where(m => m.Subgroup == g).ToList();
        static List<ModuleDefinition> In(ModuleCategory c) => ModuleCatalog.InCategory(c);

        [Test]
        public void EveryModule_HasAUniqueId_NameAndDescription()
        {
            var ids = new HashSet<string>();
            foreach (ModuleDefinition m in ModuleCatalog.All)
            {
                Assert.IsTrue(ids.Add(m.id), "duplicate id " + m.id);
                Assert.IsNotEmpty(m.name, m.id);
                Assert.IsNotEmpty(m.description, m.id);
                Assert.Greater(m.dryMass, 0.0, m.id);
                Assert.AreSame(m, ModuleCatalog.Find(m.id), m.id);
            }
        }

        [Test]
        public void Mounts_FollowTheCategory()
        {
            foreach (ModuleDefinition m in ModuleCatalog.All)
            {
                ModuleMount expected = m.IsEngine ? ModuleMount.StageEngine
                    : m.IsTank ? ModuleMount.StageTank
                    : m.category == ModuleCategory.Aerodynamics || m.auxiliaryKind == AuxiliaryKind.LandingLegs ? ModuleMount.StageSide
                    : ModuleMount.Payload;
                Assert.AreEqual(expected, m.mount, m.id);
            }
        }

        [Test]
        public void Catalog_HasWhatTheMenuPromises()
        {
            Assert.GreaterOrEqual(In(ModuleSubgroup.RemoteControl).Count, 2);
            Assert.AreEqual(3, In(ModuleSubgroup.Crewed).Count);
            CollectionAssert.AreEquivalent(new[] { 1, 3, 7 }, In(ModuleSubgroup.Crewed).Select(m => m.crewCapacity));
            Assert.GreaterOrEqual(In(ModuleSubgroup.LiquidEngine).Count, 5);
            Assert.AreEqual(3, In(ModuleSubgroup.SolidEngine).Count);
            Assert.AreEqual(2, In(ModuleSubgroup.NuclearEngine).Count);
            Assert.AreEqual(3, In(ModuleSubgroup.ElectricEngine).Count);
            Assert.AreEqual(ModuleCatalog.InCategory(ModuleCategory.FuelTank).Count, In(ModuleSubgroup.LiquidFuel).Count, "every tank is liquid");
            Assert.AreEqual(2, In(ModuleSubgroup.SideFin).Count);
            Assert.AreEqual(2, In(ModuleSubgroup.TopFin).Count);
            Assert.AreEqual(2, In(ModuleSubgroup.Rcs).Count);

            Assert.GreaterOrEqual(In(ModuleCategory.Science).Count, 8);
            foreach (ScienceMeasurement f in new[] { ScienceMeasurement.Temperature, ScienceMeasurement.Pressure, ScienceMeasurement.Gravity, ScienceMeasurement.Seismic,
                         ScienceMeasurement.Radiation, ScienceMeasurement.MagneticField, ScienceMeasurement.Imaging })
                Assert.IsTrue(In(ModuleCategory.Science).Exists(m => (m.measurements & f) != 0), f.ToString());

            List<ModuleDefinition> aux = In(ModuleCategory.Auxiliary);
            Assert.AreEqual(3, aux.Count(m => m.auxiliaryKind == AuxiliaryKind.Parachute));
            Assert.AreEqual(2, aux.Count(m => m.auxiliaryKind == AuxiliaryKind.LandingLegs));
            Assert.AreEqual(2, aux.Count(m => m.auxiliaryKind == AuxiliaryKind.HeatShield));
            Assert.AreEqual(2, aux.Count(m => m.auxiliaryKind == AuxiliaryKind.Light));
            Assert.AreEqual(3, aux.Count(m => m.auxiliaryKind == AuxiliaryKind.Shield));
            Assert.GreaterOrEqual(aux.Count(m => m.powerSource == PowerSourceKind.Solar), 2);
            Assert.GreaterOrEqual(aux.Count(m => m.powerSource == PowerSourceKind.Steady), 1);
            Assert.GreaterOrEqual(aux.Count(m => m.powerSource == PowerSourceKind.Battery), 1);

            List<ModuleDefinition> comm = In(ModuleCategory.Communication);
            Assert.GreaterOrEqual(comm.Count(m => m.antennaRange > 0.0), 4);
            Assert.AreEqual(1, comm.Count(m => m.relay));
            Assert.AreEqual(2, comm.Count(m => m.id.StartsWith("nav-")), "the old navigation modules moved here");

            List<ModuleDefinition> mech = In(ModuleCategory.Mechanics);
            Assert.AreEqual(2, mech.Count(m => m.mechanismKind == MechanismKind.Wheel));
            Assert.AreEqual(2, mech.Count(m => m.mechanismKind == MechanismKind.Drill));
            Assert.AreEqual(2, mech.Count(m => m.mechanismKind == MechanismKind.SampleCollector));
            Assert.AreEqual(1, mech.Count(m => m.mechanismKind == MechanismKind.RoverChassis));
            Assert.AreEqual(1, mech.Count(m => m.mechanismKind == MechanismKind.RoverDrive));
            Assert.AreEqual(1, mech.Count(m => m.mechanismKind == MechanismKind.Manipulator));

            List<ModuleDefinition> cargo = In(ModuleCategory.Cargo);
            Assert.GreaterOrEqual(cargo.Count, 3);
            Assert.AreEqual(1, cargo.Count(m => m.pressurized));
            Assert.IsTrue(cargo.All(m => m.cargoVolume > 0.0));
        }

        [Test]
        public void Subgroups_AreListedInDisplayOrder_AndNamed()
        {
            foreach (ModuleCategory c in ModuleCatalog.Categories)
            {
                IReadOnlyList<ModuleSubgroup> groups = ModuleCatalog.SubgroupsOf(c);
                foreach (ModuleSubgroup g in groups)
                {
                    Assert.IsNotEmpty(ModuleCatalog.SubgroupName(g), g.ToString());
                    Assert.IsNotEmpty(ModuleCatalog.InSubgroup(c, g), g.ToString());
                }
                Assert.IsNotEmpty(ModuleCatalog.CategoryName(c));
                int inTabs = groups.Sum(g => ModuleCatalog.InSubgroup(c, g).Count);
                Assert.AreEqual(groups.Count == 0 ? 0 : ModuleCatalog.InCategory(c).Count, inTabs, c + ": every module is in one tab");
            }
            Assert.AreEqual(ModuleSubgroup.None, ModuleCatalog.Find(ModuleCatalog.Thermometer).Subgroup);
        }

        [Test]
        public void MovedModules_AreInTheirNewCategories()
        {
            Assert.AreEqual(ModuleCategory.Cabin, ModuleCatalog.Find(ModuleCatalog.ProbeCore).category);
            Assert.AreEqual(ModuleSubgroup.RemoteControl, ModuleCatalog.Find(ModuleCatalog.ProbeCore).Subgroup);
            Assert.AreEqual(ModuleCategory.Communication, ModuleCatalog.Find(ModuleCatalog.NavComputer).category);
            Assert.AreEqual(ModuleCategory.Communication, ModuleCatalog.Find(ModuleCatalog.StarTracker).category);
            foreach (string id in new[] { ModuleCatalog.RadiationShield, ModuleCatalog.RadiationShieldHeavy, ModuleCatalog.AcidCoating })
            {
                Assert.AreEqual(ModuleCategory.Auxiliary, ModuleCatalog.Find(id).category, id);
                Assert.IsTrue(ModuleCatalog.Find(id).IsWrap, id);
            }
            foreach (string id in new[] { ModuleCatalog.CollectorBasic, ModuleCatalog.CollectorAdvanced })
                Assert.AreEqual(ModuleCategory.Mechanics, ModuleCatalog.Find(id).category, id);
            foreach (string id in new[] { ModuleCatalog.Spectrometer, ModuleCatalog.MeteoKit })
                Assert.AreEqual(ModuleCategory.Science, ModuleCatalog.Find(id).category, id);
            Assert.IsNotNull(Shop.Find(ModuleCatalog.CollectorAdvanced), "the shop sells what it sold before");
        }

        [Test]
        public void EveryEngine_HasACompatibleTankOrItsOwnGrain()
        {
            foreach (ModuleDefinition e in In(ModuleCategory.Engine))
            {
                if (e.engineKind == EngineKind.Solid)
                {
                    Assert.Greater(e.solidPropellantMass, 0.0, e.id);
                    Assert.AreEqual(PropellantType.Solid, e.propellant, e.id);
                    continue;
                }
                Assert.IsTrue(In(ModuleCategory.FuelTank).Exists(t => t.propellant == e.propellant), e.id);
            }
        }

        [Test]
        public void EnginePerformance_IsInTheRealClass()
        {
            foreach (ModuleDefinition e in In(ModuleSubgroup.NuclearEngine))
            {
                Assert.AreEqual(850.0, e.vacuumIsp, 25.0, e.id);
                Assert.Less(e.seaLevelIsp, 0.5 * e.vacuumIsp, e.id + ": weak near the ground");
                Assert.AreEqual(PropellantType.LiquidHydrogen, e.propellant, e.id);
            }
            foreach (ModuleDefinition e in In(ModuleSubgroup.ElectricEngine))
            {
                Assert.Less(e.vacuumThrust, 10.0, e.id + ": a few newtons at most");
                Assert.Greater(e.vacuumIsp, 2000.0, e.id);
                Assert.That(e.electricEfficiency, Is.InRange(0.5, 0.8), e.id);
            }
            double Power(string id)
            {
                ModuleDefinition m = ModuleCatalog.Find(id);
                return RocketEquations.ElectricPower(m.vacuumThrust, m.vacuumIsp, m.electricEfficiency);
            }
            Assert.AreEqual(13.7e3, Power(ModuleCatalog.EngineHall), 0.1e3, "Hall");
            Assert.AreEqual(6.9e3, Power(ModuleCatalog.EngineIon), 0.1e3, "ion");
            Assert.AreEqual(196e3, Power(ModuleCatalog.EngineMagnetoplasma), 1e3, "magnetoplasma");

            foreach (ModuleDefinition e in In(ModuleSubgroup.SolidEngine))
            {
                double burn = e.solidPropellantMass * e.vacuumIsp * RocketEquations.G0 / e.vacuumThrust;
                Assert.That(burn, Is.InRange(60.0, 150.0), e.id + ": real motors burn for minutes");
            }
        }

        [Test]
        public void Layout_MapsTheNewCategoriesToTheExistingRoles()
        {
            var b = RocketBlueprint.CreateDefault();
            b.payloadModules.Clear();
            b.payloadModules.Add(ModuleCatalog.CabinCrew3);
            b.payloadModules.Add(ModuleCatalog.RadiationShield);
            b.payloadModules.Add(ModuleCatalog.ParachuteMedium);
            b.payloadModules.Add(ModuleCatalog.DishSmall);
            b.payloadModules.Add(ModuleCatalog.WheelSmall);
            b.payloadModules.Add(ModuleCatalog.Cargo12);
            b.stages[0].attachments.Add(ModuleCatalog.FinSideLarge);
            RocketLayout layout = RocketLayout.Build(b);
            PartPlacement Find(string id) => layout.Parts.Find(p => p.ModuleId == id);

            Assert.AreEqual(PartRole.Capsule, Find(ModuleCatalog.CabinCrew3).Role);
            Assert.AreEqual(PartRole.Wrap, Find(ModuleCatalog.RadiationShield).Role);
            Assert.AreEqual(PartRole.Parachute, Find(ModuleCatalog.ParachuteMedium).Role);
            Assert.AreEqual(PartRole.Instrument, Find(ModuleCatalog.DishSmall).Role);
            Assert.AreEqual(PartRole.Wheel, Find(ModuleCatalog.WheelSmall).Role);
            Assert.AreEqual(PartRole.CargoBay, Find(ModuleCatalog.Cargo12).Role);
            Assert.AreEqual(PartRole.SideFin, Find(ModuleCatalog.FinSideLarge).Role);
            Assert.AreEqual(4, layout.Parts.Count(p => p.ModuleId == ModuleCatalog.FinSideLarge), "a fin set is four fins");
            Assert.AreEqual(0, layout.Parts.Count(p => p.Role == PartRole.ProbeBus), "the core is not added beside a cabin");
            Assert.AreEqual(1, layout.Parts.Count(p => p.Role == PartRole.Capsule));
        }

        [Test]
        public void Layout_OfEveryNewEngineKind_MatchesTheCompiledStage()
        {
            foreach (ModuleDefinition e in In(ModuleCategory.Engine))
            {
                var b = new RocketBlueprint();
                b.stages.Add(new StageBlueprint { engineId = e.id, engineCount = 1 });
                b.payloadModules.Add(ModuleCatalog.ProbeSmall);
                CompiledRocket c = BlueprintCompiler.Compile(b, id => true);
                RocketLayout layout = RocketLayout.Build(b, c.Configuration.radius, id => true);
                Assert.AreEqual(c.Configuration.stages[0].length, layout.StageTop[0], 1e-9, e.id);
            }
        }
    }
}
