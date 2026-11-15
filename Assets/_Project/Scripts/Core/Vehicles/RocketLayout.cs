using System;
using System.Collections.Generic;

namespace SpaceSim.Vehicles
{
    /// <summary>What a placed part is: decides how it is mounted and what its placeholder looks like.</summary>
    public enum PartRole
    {
        /// <summary>Thrust structure around a stage's engines: a skirt with fins on stage 1, an open truss interstage above.</summary>
        EngineSection,
        Engine,
        Tank,
        /// <summary>Filler that brings a stage up to the compiler's minimum length (a stage without engine and tanks).</summary>
        StageSpacer,
        /// <summary>A remotely controlled cabin (probe core): the bus the instruments of the payload section are mounted on.</summary>
        ProbeBus,
        /// <summary>Protection wrapped around the bus (radiation shield, acid coating): a shell, innermost first.</summary>
        Wrap,
        /// <summary>
        /// An instrument: outside on the hull (science, navigation, antennas, dishes, lights, power, collectors, drills, arm) or,
        /// when <see cref="PartPlacement.Inside"/>, stowed inside an open cargo bay.
        /// </summary>
        Instrument,
        /// <summary>Nose fairing over the whole payload section (it stays on while any stage is attached).</summary>
        Fairing,

        // ---- One piece of a stage-side set (StageBlueprint.attachments): Index = position in the list, Element = piece ----
        /// <summary>Tail fin at the bottom of the stage.</summary>
        SideFin,
        /// <summary>Grid fin at the top of the stage.</summary>
        GridFin,
        /// <summary>Reaction-control thruster block, on a ring at the top of the stage.</summary>
        RcsBlock,
        /// <summary>Landing leg, on a ring at the bottom of the stage; the slot is the stowed leg (hinge at its bottom).</summary>
        LandingLeg,

        // ---- Payload section (Index = position in RocketBlueprint.payloadModules) ----
        /// <summary>A crewed cabin: on top of the payload stack.</summary>
        Capsule,
        /// <summary>Heat shield: right under the crewed capsule (under the bus when nobody is on board).</summary>
        HeatShield,
        /// <summary>Parachute container on the nose of the stack; the canopy is drawn only when deployed.</summary>
        Parachute,
        /// <summary>Cargo bay (hollow, or a closed pressurised module): stacked above the bus.</summary>
        CargoBay,
        /// <summary>Rover frame at the bottom of the payload section.</summary>
        RoverChassis,
        /// <summary>Rover drive unit, inside the frame.</summary>
        RoverDrive,
        /// <summary>Rover wheel beside the frame; its axle is the rocket's X axis, its centre is the slot centre.</summary>
        Wheel
    }

    /// <summary>
    /// Where one part goes. The slot is an upright box <see cref="Diameter"/> wide (both horizontal axes) and <see cref="Height"/>
    /// tall, standing at <see cref="Bottom"/> on the rocket axis and pushed <see cref="RadialOffset"/> out along azimuth
    /// <see cref="AngleDegrees"/>. Rocket frame, metres: +Y along the axis (0 = bottom of stage 1), azimuth 0° = local +Z, 90° = +X.
    /// </summary>
    public sealed class PartPlacement
    {
        public PartRole Role;
        /// <summary>Catalog module drawn in this slot ("" = structure: engine section, spacer, fairing…).</summary>
        public string ModuleId = "";
        /// <summary>Stage index, −1 for the payload section.</summary>
        public int Stage = -1;
        /// <summary>
        /// Engine number in the cluster · tank index in <see cref="StageBlueprint.tanks"/> · index in
        /// <see cref="StageBlueprint.attachments"/> · index in <see cref="RocketBlueprint.payloadModules"/> (−1 for the core added
        /// automatically) · −1 for structure.
        /// </summary>
        public int Index = -1;
        /// <summary>Piece of a stage-side set (fin, leg, RCS block number 0…n−1); 0 for everything else.</summary>
        public int Element;
        public double Bottom, Height, Diameter, RadialOffset, AngleDegrees;
        /// <summary>Wrap only: thickness of the shell [m] (its outer diameter is <see cref="Diameter"/>).</summary>
        public double Thickness;
        /// <summary>Instrument stowed inside an open cargo bay (not on the hull).</summary>
        public bool Inside;

        public double Top => Bottom + Height;
        public double Center => Bottom + 0.5 * Height;
        /// <summary>Farthest reach of the slot from the rocket axis [m].</summary>
        public double OuterRadius => RadialOffset + 0.5 * Diameter;
        public bool IsModule => !string.IsNullOrEmpty(ModuleId);
        /// <summary>Slot centre across the axis: x towards azimuth 90°, z towards azimuth 0° [m].</summary>
        public double X => RadialOffset * Math.Sin(AngleDegrees * Math.PI / 180.0);
        public double Z => RadialOffset * Math.Cos(AngleDegrees * Math.PI / 180.0);
        /// <summary>A piece of a stage-side set.</summary>
        public bool IsStageSide => Role == PartRole.SideFin || Role == PartRole.GridFin || Role == PartRole.RcsBlock || Role == PartRole.LandingLeg;
    }

    /// <summary>
    /// Geometry of a rocket design: where every engine, tank, side set, instrument and structural part sits. Engine-free, so the
    /// hangar preview and the flying rocket are built from the same numbers. Stage heights are exactly the stage lengths the
    /// <see cref="BlueprintCompiler"/> gives the physics (engine length + tank lengths, at least 1 m); side sets never change them.
    /// Modules the compiler skips (unknown, not bought, wrong slot) are not placed either. Diameters are visual only — drag still
    /// uses the rocket radius.
    /// <para>Payload section, bottom to top, standing directly on the last stage's top: rover (frame, drive, wheels) · probe bus with its shells · cargo bays ·
    /// heat shield · crewed capsules · parachutes on the nose. Instruments go around the bus and the bays (or inside open bays),
    /// the fairing covers all of it.</para>
    /// </summary>
    public sealed class RocketLayout
    {
        public const double DefaultRadius = 1.85;
        /// <summary>Same minimum as <see cref="BlueprintCompiler"/>.</summary>
        public const double MinStageLength = 1.0;
        /// <summary>
        /// Stage 1 with a liquid engine: its thrust skirt is open below this share of the engine section (the bells hang out);
        /// side sets start at the skirt's lower edge.
        /// </summary>
        public const double SkirtOpening = 0.45;
        /// <summary>A deployed landing leg is turned this far [deg] from its stowed, upward position about its hinge at the slot bottom.</summary>
        public const double LegDeployAngle = 150.0;

        /// <summary>The probe bus is at most this share of the rocket diameter (room for instruments inside the fairing).</summary>
        const double BusShare = 0.5;
        const double MinWrapThickness = 0.04;
        const double MinInstrumentSize = 0.4;
        /// <summary>An instrument's slot is this many times its catalog length (visual: small boxes must stay visible).</summary>
        const double InstrumentScale = 1.4;
        /// <summary>Width [deg] of each of the two arcs (front, back) instruments are spread over; the 40° gaps at ±X hold the solar wings.</summary>
        public const double InstrumentArc = 140.0;
        /// <summary>Instruments stand this far off the bus or the outer shell, on brackets (clear of the stowed solar wings).</summary>
        const double InstrumentStandoff = 0.06;
        const double FairingClearance = 0.15;
        const double FairingHeadroom = 0.5;
        /// <summary>Bells keep this fraction of the largest diameter that would touch a neighbour or the stage wall.</summary>
        const double BellGap = 0.94;
        /// <summary>Bottom and top bands of a stage side each take at most this share of the stage height (they never meet).</summary>
        const double SideBandShare = 0.45;
        /// <summary>Hollow bays: the usable inner diameter is this share of the outer one (ribs and longerons).</summary>
        const double BayInnerShare = 0.86;
        const double ChassisHeight = 0.45;
        const double DefaultRoverFrame = 1.6;

        public readonly List<PartPlacement> Parts = new List<PartPlacement>();
        public double Radius { get; private set; }
        public double[] StageBottom { get; private set; } = new double[0];
        public double[] StageTop { get; private set; } = new double[0];
        /// <summary>Top of the last stage = bottom of the payload section [m].</summary>
        public double PayloadBottom { get; private set; }
        /// <summary>Top of the payload contents [m].</summary>
        public double PayloadTop { get; private set; }
        public double FairingRadius { get; private set; }
        public double FairingCylinderHeight { get; private set; }
        public double FairingNoseHeight { get; private set; }
        public double FairingTop => PayloadBottom + FairingCylinderHeight + FairingNoseHeight;
        /// <summary>Whole stack, bottom of stage 1 to the fairing tip [m].</summary>
        public double Height => FairingTop;
        public int StageCount => StageBottom.Length;

        /// <summary>First part with this role, stage and index (null if none).</summary>
        public PartPlacement Find(PartRole role, int stage, int index)
        {
            foreach (PartPlacement p in Parts)
                if (p.Role == role && p.Stage == stage && p.Index == index) return p;
            return null;
        }

        /// <summary>How far below the slot bottom the foot of a deployed leg reaches [m] (visual: the stowed leg turned about its hinge).</summary>
        public static double DeployedLegDrop(PartPlacement leg) =>
            leg == null ? 0.0 : -leg.Height * Math.Cos(LegDeployAngle * Math.PI / 180.0);

        /// <param name="radius">Rocket body radius [m] (<see cref="SpaceSim.Flight.RocketConfiguration.radius"/>).</param>
        /// <param name="isUnlocked">Same ownership check the compiler gets; null = everything available.</param>
        public static RocketLayout Build(RocketBlueprint blueprint, double radius = DefaultRadius, Func<string, bool> isUnlocked = null)
        {
            if (blueprint == null) throw new ArgumentNullException(nameof(blueprint));
            var layout = new RocketLayout { Radius = Math.Max(0.1, radius) };
            double r = layout.Radius;
            int n = blueprint.stages.Count;
            layout.StageBottom = new double[n];
            layout.StageTop = new double[n];

            double y = 0.0;
            for (int i = 0; i < n; i++)
            {
                StageBlueprint sb = blueprint.stages[i];
                double bottom = y, engineHeight = 0.0;
                ModuleDefinition engine = Resolve(sb.engineId, isUnlocked);
                if (engine != null && engine.IsEngine)
                {
                    double h = Math.Max(0.0, engine.length);
                    layout.Add(PartRole.EngineSection, "", i, -1, y, h, 2.0 * r);
                    int count = Math.Max(1, Math.Min(BlueprintCompiler.MaxEnginesPerStage, sb.engineCount));
                    PlaceCluster(layout, engine, count, i, y, h);
                    y += h;
                    engineHeight = h;
                }
                else engine = null;
                for (int k = 0; k < sb.tanks.Count; k++)
                {
                    ModuleDefinition tank = Resolve(sb.tanks[k].moduleId, isUnlocked);
                    if (tank == null || !tank.IsTank) continue;
                    double h = Math.Max(0.0, tank.length);
                    layout.Add(PartRole.Tank, tank.id, i, k, y, h, 2.0 * r);
                    y += h;
                }
                if (y - bottom < MinStageLength)
                {
                    layout.Add(PartRole.StageSpacer, "", i, -1, y, bottom + MinStageLength - y, 2.0 * r);
                    y = bottom + MinStageLength;
                }
                layout.StageBottom[i] = bottom;
                layout.StageTop[i] = y;
                double hullStart = i == 0 && engine != null && engine.engineKind == EngineKind.LiquidChemical ? SkirtOpening * engineHeight : 0.0;
                PlaceStageSide(layout, sb, i, bottom, bottom + hullStart, y, isUnlocked);
            }

            PlacePayload(layout, blueprint, y, isUnlocked);
            return layout;
        }

        static ModuleDefinition Resolve(string id, Func<string, bool> isUnlocked)
        {
            if (string.IsNullOrEmpty(id)) return null;
            ModuleDefinition m = ModuleCatalog.Find(id);
            if (m == null) return null;
            if (isUnlocked != null && !isUnlocked(m.id)) return null;
            return m;
        }

        PartPlacement Add(PartRole role, string moduleId, int stage, int index, double bottom, double height, double diameter,
            double radialOffset = 0.0, double angle = 0.0)
        {
            var p = new PartPlacement
            {
                Role = role, ModuleId = moduleId ?? "", Stage = stage, Index = index, Bottom = bottom, Height = height,
                Diameter = diameter, RadialOffset = radialOffset, AngleDegrees = angle
            };
            Parts.Add(p);
            return p;
        }

        static double Normalize(double angle)
        {
            angle %= 360.0;
            return angle < 0.0 ? angle + 360.0 : angle;
        }

        // ---- Engines ----------------------------------------------------------------------------------------

        /// <summary>1 engine in the centre; 2–4 on a ring; 5–9 one in the centre and the rest on a ring.</summary>
        static void PlaceCluster(RocketLayout layout, ModuleDefinition engine, int count, int stage, double bottom, double height)
        {
            bool centre = count == 1 || count >= 5;
            int ring = centre ? count - 1 : count;
            ClusterGeometry(ring, centre, layout.Radius, out double ringRadius, out double maxDiameter);
            double natural = engine.diameter > 0.0 ? engine.diameter : 0.6 * layout.Radius;
            double d = Math.Min(natural, maxDiameter);
            int index = 0;
            if (centre) layout.Add(PartRole.Engine, engine.id, stage, index++, bottom, height, d);
            for (int k = 0; k < ring; k++)
                layout.Add(PartRole.Engine, engine.id, stage, index++, bottom, height, d, ringRadius, 180.0 / ring + 360.0 * k / ring);
        }

        /// <summary>
        /// Ring radius and the largest bell that fits <paramref name="ring"/> engines on it (plus one in the centre): no bell touches
        /// a neighbour, the centre engine or the stage wall.
        /// </summary>
        static void ClusterGeometry(int ring, bool centre, double radius, out double ringRadius, out double diameter)
        {
            if (ring <= 0)
            {
                ringRadius = 0.0;
                diameter = 2.0 * radius * BellGap;
                return;
            }
            double best = 0.0, bestRho = 0.5 * radius;
            const int steps = 400;
            for (int s = 1; s < steps; s++)
            {
                double rho = radius * s / steps;
                double d = 2.0 * (radius - rho);
                if (ring >= 2) d = Math.Min(d, 2.0 * rho * Math.Sin(Math.PI / ring));
                if (centre) d = Math.Min(d, rho);
                if (d > best)
                {
                    best = d;
                    bestRho = rho;
                }
            }
            ringRadius = bestRho;
            diameter = best * BellGap;
        }

        // ---- Stage-side sets ----------------------------------------------------------------------------------

        struct SideSet
        {
            public ModuleDefinition Module;
            public PartRole Role;
            public int Index, Count;
            public double Height, Size;
        }

        /// <summary>Role, piece count and piece size [m] of a stage-side module; <paramref name="band"/> caps the height.</summary>
        static void SidePiece(ModuleDefinition m, double band, out PartRole role, out int count, out double height, out double size)
        {
            count = 4;
            double len = Math.Max(0.1, m.length);
            if (m.auxiliaryKind == AuxiliaryKind.LandingLegs)
            {
                role = PartRole.LandingLeg;
                count = Math.Max(1, m.legCount > 0 ? m.legCount : 4);
                height = Math.Min(len, band);
                size = Math.Max(0.2, 0.1 * len + 0.15);
                return;
            }
            if (m.category == ModuleCategory.Aerodynamics && m.aeroKind == AeroKind.Rcs)
            {
                role = PartRole.RcsBlock;
                size = Math.Min(Math.Max(0.25, len), band);
                height = size;
                return;
            }
            // Fins: the catalog length is the chord along the axis; the span follows from the area of one fin.
            role = m.category == ModuleCategory.Aerodynamics && m.aeroKind == AeroKind.TopFin ? PartRole.GridFin : PartRole.SideFin;
            double finArea = m.finArea > 0.0 ? m.finArea / count : 0.25 * len * len;
            height = Math.Min(Math.Max(0.3, len), band);
            size = Math.Max(0.25, finArea / len);
        }

        /// <summary>
        /// Tail fins and legs on a ring at the bottom of the stage, grid fins and RCS blocks at the top. Several sets in one band are
        /// turned against each other so their pieces alternate around the ring (the first set at 45°, between the ±X / ±Z lines).
        /// Fins start at the lower edge of the hull (<paramref name="hullBottom"/>: stage 1's skirt), legs hinge at the very bottom of
        /// the stage, beside the bells, so a deployed foot reaches below the nozzle exits.
        /// </summary>
        static void PlaceStageSide(RocketLayout layout, StageBlueprint sb, int stage, double stageBottom, double hullBottom, double top,
            Func<string, bool> isUnlocked)
        {
            if (sb.attachments == null || sb.attachments.Count == 0) return;
            double band = SideBandShare * Math.Max(0.1, top - hullBottom);
            var low = new List<SideSet>();
            var high = new List<SideSet>();
            for (int k = 0; k < sb.attachments.Count; k++)
            {
                ModuleDefinition m = Resolve(sb.attachments[k], isUnlocked);
                if (m == null || !m.IsStageAttachment) continue;
                SidePiece(m, band, out PartRole role, out int count, out double h, out double size);
                var set = new SideSet { Module = m, Role = role, Index = k, Count = count, Height = h, Size = size };
                if (role == PartRole.GridFin || role == PartRole.RcsBlock) high.Add(set);
                else low.Add(set);
            }
            PlaceBand(layout, low, stage, hullBottom, false); // legs hinge at the hull edge too: stowed they lie along the skirt, not by the bells
            PlaceBand(layout, high, stage, top, true);
        }

        static void PlaceBand(RocketLayout layout, List<SideSet> sets, int stage, double edge, bool fromTop)
        {
            if (sets.Count == 0) return;
            int most = 1;
            foreach (SideSet s in sets) most = Math.Max(most, s.Count);
            for (int j = 0; j < sets.Count; j++)
            {
                SideSet s = sets[j];
                double offset = 45.0 + 360.0 / most * j / sets.Count;
                double bottom = fromTop ? edge - s.Height : edge;
                for (int e = 0; e < s.Count; e++)
                {
                    PartPlacement p = layout.Add(s.Role, s.Module.id, stage, s.Index, bottom, s.Height, s.Size,
                        layout.Radius + 0.5 * s.Size, Normalize(offset + 360.0 * e / s.Count));
                    p.Element = e;
                }
            }
        }

        // ---- Payload section --------------------------------------------------------------------------------

        struct Item
        {
            public ModuleDefinition Module;
            public int Index;
        }

        /// <summary>A stretch of hull instruments are fixed around: bottom, top and outer diameter [m].</summary>
        struct Segment
        {
            public double Bottom, Top, Diameter;
        }

        /// <summary>What part of the payload section a module becomes.</summary>
        static PartRole PayloadRole(ModuleDefinition m)
        {
            if (m.IsCabin) return m.cabinKind == CabinKind.Crewed ? PartRole.Capsule : PartRole.ProbeBus;
            if (m.IsWrap) return PartRole.Wrap;
            if (m.auxiliaryKind == AuxiliaryKind.HeatShield) return PartRole.HeatShield;
            if (m.auxiliaryKind == AuxiliaryKind.Parachute) return PartRole.Parachute;
            if (m.category == ModuleCategory.Cargo) return PartRole.CargoBay;
            switch (m.mechanismKind)
            {
                case MechanismKind.Wheel: return PartRole.Wheel;
                case MechanismKind.RoverChassis: return PartRole.RoverChassis;
                case MechanismKind.RoverDrive: return PartRole.RoverDrive;
            }
            return PartRole.Instrument;
        }

        /// <summary>Instruments that may ride inside an open cargo bay: science, navigation computers, batteries.</summary>
        static bool CanRideInside(ModuleDefinition m) =>
            m.category == ModuleCategory.Science ||
            (m.category == ModuleCategory.Communication && m.antennaRange <= 0.0) ||
            m.powerSource == PowerSourceKind.Battery;

        static double InstrumentWanted(ModuleDefinition m) => Math.Max(MinInstrumentSize, InstrumentScale * Math.Max(m.length, m.diameter));

        /// <summary>Outer diameter of a cargo bay: a cylinder of its volume and length, at most the rocket diameter [m].</summary>
        static double BayDiameter(ModuleDefinition m, double r)
        {
            double len = Math.Max(0.3, m.length);
            double d = m.cargoVolume > 0.0 ? Math.Sqrt(4.0 * m.cargoVolume / (Math.PI * len)) : r;
            return Math.Max(0.6, Math.Min(2.0 * r, d));
        }

        static double CapsuleDiameter(ModuleDefinition m, double r) => Math.Max(0.5, m.diameter > 0.0 ? m.diameter : r);
        static double ParachuteDiameter(ModuleDefinition m) => 0.45 + 0.9 * Math.Max(0.1, m.length);
        static double ParachuteHeight(ModuleDefinition m) => Math.Max(0.2, m.length);
        static double HeatShieldHeight(ModuleDefinition m) => Math.Max(0.12, m.length);
        static double WheelDiameter(ModuleDefinition m) => Math.Max(0.2, m.diameter > 0.0 ? m.diameter : m.length);
        static double FrameLength(ModuleDefinition chassis, double r) =>
            Math.Min(1.8 * r, chassis != null ? Math.Max(1.4, chassis.length) : DefaultRoverFrame);
        static double DriveSize(ModuleDefinition m, double frame) => Math.Min(0.35 * frame, Math.Max(0.3, InstrumentScale * m.length));

        static void PlacePayload(RocketLayout layout, RocketBlueprint blueprint, double bottom, Func<string, bool> isUnlocked)
        {
            double r = layout.Radius;
            layout.PayloadBottom = bottom;
            double y = bottom;

            var remote = new List<Item>();
            var crewed = new List<Item>();
            var wraps = new List<Item>();
            var shields = new List<Item>();
            var chutes = new List<Item>();
            var bays = new List<Item>();
            var chassis = new List<Item>();
            var drives = new List<Item>();
            var wheels = new List<Item>();
            var instruments = new List<Item>();
            for (int p = 0; p < blueprint.payloadModules.Count; p++)
            {
                ModuleDefinition m = Resolve(blueprint.payloadModules[p], isUnlocked);
                if (m == null || !m.IsPayload) continue;
                var item = new Item { Module = m, Index = p };
                switch (PayloadRole(m))
                {
                    case PartRole.ProbeBus: remote.Add(item); break;
                    case PartRole.Capsule: crewed.Add(item); break;
                    case PartRole.Wrap: wraps.Add(item); break;
                    case PartRole.HeatShield: shields.Add(item); break;
                    case PartRole.Parachute: chutes.Add(item); break;
                    case PartRole.CargoBay: bays.Add(item); break;
                    case PartRole.RoverChassis: chassis.Add(item); break;
                    case PartRole.RoverDrive: drives.Add(item); break;
                    case PartRole.Wheel: wheels.Add(item); break;
                    default: instruments.Add(item); break;
                }
            }
            // The compiler adds the probe core when the design has no cabin — so is it drawn.
            if (remote.Count == 0 && crewed.Count == 0)
                remote.Add(new Item { Module = ModuleCatalog.Find(ModuleCatalog.ProbeCore), Index = -1 });

            // Rover at the very bottom: it rolls off the stage top once the vehicle has landed.
            if (chassis.Count + drives.Count + wheels.Count > 0)
            {
                double frame = FrameLength(chassis.Count > 0 ? chassis[0].Module : null, r);
                y = PlaceRover(layout, chassis, drives, wheels, y, frame);
            }

            // Shells wrap the probe bus, or the capsules when nobody flies remotely.
            double busDiameter = 0.0;
            if (remote.Count > 0)
            {
                ModuleDefinition first = remote[0].Module;
                busDiameter = Math.Min(first.diameter > 0.0 ? first.diameter : r, 2.0 * r * BusShare);
            }
            double capsuleDiameter = 0.0;
            foreach (Item c in crewed) capsuleDiameter = Math.Max(capsuleDiameter, CapsuleDiameter(c.Module, r));
            double shellCore = remote.Count > 0 ? busDiameter : capsuleDiameter;
            double outer = shellCore;
            var thickness = new List<double>();
            foreach (Item wrap in wraps)
            {
                double t = Math.Max(MinWrapThickness, 0.25 * wrap.Module.length);
                thickness.Add(t);
                outer += 2.0 * t;
            }

            // Without a crewed capsule the heat shield protects the bus from below.
            if (crewed.Count == 0) y = PlaceHeatShields(layout, shields, y, 1.04 * outer);

            double busBottom = y;
            foreach (Item core in remote)
            {
                double h = Math.Max(0.5, core.Module.length);
                layout.Add(PartRole.ProbeBus, core.Module.id, -1, core.Index, y, h, busDiameter);
                y += h;
            }
            double busTop = y;

            var segments = new List<Segment>();
            if (remote.Count > 0) segments.Add(new Segment { Bottom = busBottom, Top = busTop, Diameter = outer });

            var bayParts = new List<PartPlacement>();
            foreach (Item bay in bays)
            {
                double d = BayDiameter(bay.Module, r), h = Math.Max(0.3, bay.Module.length);
                bayParts.Add(layout.Add(PartRole.CargoBay, bay.Module.id, -1, bay.Index, y, h, d));
                segments.Add(new Segment { Bottom = y, Top = y + h, Diameter = d });
                y += h;
            }

            double capsuleBottom = y;
            if (crewed.Count > 0)
            {
                y = PlaceHeatShields(layout, shields, y, 1.04 * Math.Max(capsuleDiameter, outer));
                capsuleBottom = y;
                foreach (Item c in crewed)
                {
                    double d = CapsuleDiameter(c.Module, r), h = Math.Max(0.5, c.Module.length);
                        layout.Add(PartRole.Capsule, c.Module.id, -1, c.Index, y, h, d);
                    y += h;
                }
            }
            double capsuleTop = y;

            PlaceParachutes(layout, chutes, y);

            // Shells around the bus (or the capsule stack), innermost first, in the order they were added.
            double shellBottom = remote.Count > 0 ? busBottom : capsuleBottom;
            double shellHeight = remote.Count > 0 ? busTop - busBottom : capsuleTop - capsuleBottom;
            double grown = shellCore;
            for (int w = 0; w < wraps.Count; w++)
            {
                grown += 2.0 * thickness[w];
                layout.Add(PartRole.Wrap, wraps[w].Module.id, -1, wraps[w].Index, shellBottom, shellHeight, grown).Thickness = thickness[w];
            }
            if (segments.Count == 0 && crewed.Count > 0) // only capsules: instruments go around the capsule's base
                segments.Add(new Segment { Bottom = capsuleBottom, Top = capsuleBottom + 0.45 * (capsuleTop - capsuleBottom), Diameter = outer });

            // Science, navigation and batteries ride inside open bays while there is room; everything else goes outside.
            var outside = new List<Item>();
            var open = new List<PartPlacement>();
            foreach (PartPlacement b in bayParts)
            {
                ModuleDefinition m = ModuleCatalog.Find(b.ModuleId);
                if (m != null && !m.pressurized) open.Add(b);
            }
            int bayIndex = 0, slotInBay = 0;
            foreach (Item item in instruments)
            {
                if (bayIndex >= open.Count || !CanRideInside(item.Module))
                {
                    outside.Add(item);
                    continue;
                }
                PartPlacement bay = open[bayIndex];
                double inner = BayInnerShare * bay.Diameter;
                double cap = Math.Min(0.34 * inner, 0.9 * bay.Height);
                int levels = Math.Max(1, (int)Math.Floor(bay.Height / Math.Max(1e-6, cap)));
                double cell = bay.Height / levels;
                double size = Math.Min(Math.Min(Math.Max(0.25, InstrumentScale * item.Module.length), cap), 0.95 * cell);
                int level = slotInBay / 4, quarter = slotInBay % 4;
                double centre = bay.Bottom + cell * (level + 0.5);
                PartPlacement p = layout.Add(PartRole.Instrument, item.Module.id, -1, item.Index, centre - 0.5 * size, size, size,
                    0.25 * inner, 45.0 + 90.0 * quarter);
                p.Inside = true;
                if (++slotInBay >= 4 * levels)
                {
                    bayIndex++;
                    slotInBay = 0;
                }
            }
            PlaceOutside(layout, outside, segments);

            double top = busTop;
            foreach (PartPlacement p in layout.Parts)
                if (p.Stage == -1) top = Math.Max(top, p.Top);
            layout.PayloadTop = top;

            double reach = 0.0;
            foreach (PartPlacement p in layout.Parts)
                if (p.Stage == -1) reach = Math.Max(reach, p.OuterRadius);
            layout.FairingRadius = Math.Max(r, reach + FairingClearance);
            layout.FairingCylinderHeight = top - bottom + FairingHeadroom;
            layout.FairingNoseHeight = 1.5 * layout.FairingRadius;
            layout.Add(PartRole.Fairing, "", -1, -1, bottom, layout.FairingCylinderHeight + layout.FairingNoseHeight, 2.0 * layout.FairingRadius);
        }

        static double PlaceHeatShields(RocketLayout layout, List<Item> shields, double y, double diameter)
        {
            foreach (Item s in shields)
            {
                double h = HeatShieldHeight(s.Module);
                layout.Add(PartRole.HeatShield, s.Module.id, -1, s.Index, y, h, diameter);
                y += h;
            }
            return y;
        }

        /// <summary>One container on the axis; several on a ring, far enough apart not to touch.</summary>
        static void PlaceParachutes(RocketLayout layout, List<Item> chutes, double y)
        {
            int n = chutes.Count;
            if (n == 0) return;
            double widest = 0.0;
            foreach (Item c in chutes) widest = Math.Max(widest, ParachuteDiameter(c.Module));
            double ring = n == 1 ? 0.0 : 0.55 * widest / Math.Sin(Math.PI / n);
            for (int k = 0; k < n; k++)
            {
                ModuleDefinition m = chutes[k].Module;
                layout.Add(PartRole.Parachute, m.id, -1, chutes[k].Index, y, ParachuteHeight(m), ParachuteDiameter(m), ring, n == 1 ? 0.0 : 360.0 * k / n);
            }
        }

        /// <summary>
        /// Frames stacked on the axis, drives inside the first frame, wheels in two rows beside it (front–back along Z, axles along X).
        /// Returns the top of the rover section.
        /// </summary>
        static double PlaceRover(RocketLayout layout, List<Item> chassis, List<Item> drives, List<Item> wheels, double y, double frame)
        {
            double wheelMax = 0.0;
            foreach (Item w in wheels) wheelMax = Math.Max(wheelMax, WheelDiameter(w.Module));
            double centre = y + 0.5 * Math.Max(wheelMax, ChassisHeight);
            double frameBottom = centre - 0.5 * ChassisHeight;
            double top = y + Math.Max(wheelMax, ChassisHeight);
            for (int k = 0; k < chassis.Count; k++)
            {
                double b = frameBottom + k * ChassisHeight;
                layout.Add(PartRole.RoverChassis, chassis[k].Module.id, -1, chassis[k].Index, b, ChassisHeight, frame);
                top = Math.Max(top, b + ChassisHeight);
            }
            int nd = drives.Count;
            for (int k = 0; k < nd; k++)
            {
                double d = DriveSize(drives[k].Module, frame);
                double h = Math.Min(0.9 * ChassisHeight, d);
                double z = (k - 0.5 * (nd - 1)) * 1.1 * d;
                layout.Add(PartRole.RoverDrive, drives[k].Module.id, -1, drives[k].Index, centre - 0.5 * h, h, d, Math.Abs(z), z < 0.0 ? 180.0 : 0.0);
            }
            int perSide = (wheels.Count + 1) / 2;
            double pitch = Math.Max(1.08 * wheelMax, perSide > 1 ? 0.8 * frame / (perSide - 1) : 0.0);
            for (int i = 0; i < wheels.Count; i++)
            {
                ModuleDefinition m = wheels[i].Module;
                double d = WheelDiameter(m);
                double side = i % 2 == 0 ? 1.0 : -1.0;
                int k = i / 2;
                double x = side * (0.35 * frame + 0.5 * 0.45 * wheelMax + 0.04);
                double z = (k - 0.5 * (perSide - 1)) * pitch;
                double radial = Math.Sqrt(x * x + z * z);
                double angle = Normalize(Math.Atan2(x, z) * 180.0 / Math.PI);
                layout.Add(PartRole.Wheel, m.id, -1, wheels[i].Index, centre - 0.5 * d, d, d, radial, angle);
            }
            return top;
        }

        /// <summary>
        /// Instruments around the hull (the bus with its shells, then the cargo bays), in rows, alternating between the front arc
        /// (around azimuth 0°) and the back arc (around 180°). The ±X sides stay free: the probe's solar wings fold there.
        /// </summary>
        static void PlaceOutside(RocketLayout layout, List<Item> instruments, List<Segment> segments)
        {
            int count = instruments.Count;
            if (count == 0 || segments.Count == 0) return;
            // The usual number per row; fewer (more rows) only when a crowded hull would make neighbours touch.
            for (int perRow = Math.Max(6, Math.Min(16, (count + 1) / 2)); ; perRow--)
            {
                List<PartPlacement> rows = Rows(layout.Radius, instruments, segments, perRow);
                if (perRow <= 1 || !AnyOverlap(rows))
                {
                    layout.Parts.AddRange(rows);
                    return;
                }
            }
        }

        static List<PartPlacement> Rows(double r, List<Item> instruments, List<Segment> segments, int perRow)
        {
            int count = instruments.Count;
            var list = new List<PartPlacement>(count);
            double columnBottom = segments[0].Bottom, columnTop = segments[segments.Count - 1].Top;
            int rows = (count + perRow - 1) / perRow;
            double pitch = (columnTop - columnBottom) / rows;
            for (int i = 0; i < count; i++)
            {
                Item item = instruments[i];
                int row = i / perRow, col = i % perRow;
                int inRow = Math.Min(perRow, count - row * perRow);
                double centre = columnBottom + pitch * (row + 0.5);
                Segment seg = segments[segments.Count - 1];
                foreach (Segment s in segments)
                    if (centre <= s.Top + 1e-9)
                    {
                        seg = s;
                        break;
                    }
                double mount = 0.5 * seg.Diameter + InstrumentStandoff;
                // Big enough to read on a 50 m rocket, yet inside the rocket's outline while there is room (else the fairing grows).
                double room = r - FairingClearance - mount;
                double size = Math.Min(Math.Max(MinInstrumentSize, Math.Min(InstrumentWanted(item.Module), room)), 0.95 * pitch);
                bool front = (col % 2 == 0) == (row % 2 == 0); // odd rows start on the back arc
                int onArc = col % 2 == 0 ? (inRow + 1) / 2 : inRow / 2; // columns of this parity share the arc
                double angle = (front ? 0.0 : 180.0) + InstrumentArc * ((col / 2 + 0.5) / onArc - 0.5);
                if (angle < 0.0) angle += 360.0;
                list.Add(new PartPlacement
                {
                    Role = PartRole.Instrument, ModuleId = item.Module.id, Stage = -1, Index = item.Index, Bottom = centre - 0.5 * size,
                    Height = size, Diameter = size, RadialOffset = mount + 0.5 * size, AngleDegrees = angle
                });
            }
            return list;
        }

        /// <summary>Two slots share space: their heights overlap and their footprints (circles of the slot width) intersect.</summary>
        public static bool Overlap(PartPlacement a, PartPlacement b, double tolerance = 1e-6)
        {
            if (a.Top <= b.Bottom + tolerance || b.Top <= a.Bottom + tolerance) return false;
            double dx = a.X - b.X, dz = a.Z - b.Z;
            double reach = 0.5 * (a.Diameter + b.Diameter) - tolerance;
            return reach > 0.0 && dx * dx + dz * dz < reach * reach;
        }

        static bool AnyOverlap(List<PartPlacement> parts)
        {
            for (int i = 0; i < parts.Count; i++)
                for (int j = i + 1; j < parts.Count; j++)
                    if (Overlap(parts[i], parts[j])) return true;
            return false;
        }

        // ---- A lone module (hangar thumbnails) --------------------------------------------------------------

        /// <summary>
        /// The slot a module would get on its own (one piece of a stage-side set), at the origin: the same role and size rules as in
        /// a design. For previews of a single module.
        /// </summary>
        public static PartPlacement PreviewSlot(ModuleDefinition m, double radius = DefaultRadius)
        {
            if (m == null) return null;
            double r = Math.Max(0.1, radius);
            var p = new PartPlacement { ModuleId = m.id, Index = 0 };
            double len = Math.Max(0.1, m.length);
            if (m.IsEngine)
            {
                p.Role = PartRole.Engine;
                p.Stage = 0;
                p.Diameter = m.diameter > 0.0 ? m.diameter : 0.6 * r;
                p.Height = len;
                return p;
            }
            if (m.IsTank)
            {
                p.Role = PartRole.Tank;
                p.Stage = 0;
                p.Diameter = 2.0 * r;
                p.Height = len;
                return p;
            }
            if (m.IsStageAttachment)
            {
                SidePiece(m, double.MaxValue, out PartRole role, out _, out double h, out double size);
                p.Role = role;
                p.Stage = 0;
                p.Diameter = size;
                p.Height = h;
                return p;
            }
            p.Role = PayloadRole(m);
            switch (p.Role)
            {
                case PartRole.ProbeBus:
                    p.Diameter = Math.Min(m.diameter > 0.0 ? m.diameter : r, 2.0 * r * BusShare);
                    p.Height = Math.Max(0.5, m.length);
                    break;
                case PartRole.Capsule:
                    p.Diameter = CapsuleDiameter(m, r);
                    p.Height = Math.Max(0.5, m.length);
                    break;
                case PartRole.Wrap:
                    p.Thickness = Math.Max(MinWrapThickness, 0.25 * m.length);
                    p.Diameter = r + 2.0 * p.Thickness;
                    p.Height = 1.4 * r;
                    break;
                case PartRole.HeatShield:
                    p.Diameter = 2.2;
                    p.Height = HeatShieldHeight(m);
                    break;
                case PartRole.Parachute:
                    p.Diameter = ParachuteDiameter(m);
                    p.Height = ParachuteHeight(m);
                    break;
                case PartRole.CargoBay:
                    p.Diameter = BayDiameter(m, r);
                    p.Height = Math.Max(0.3, m.length);
                    break;
                case PartRole.RoverChassis:
                    p.Diameter = FrameLength(m, r);
                    p.Height = ChassisHeight;
                    break;
                case PartRole.RoverDrive:
                    p.Diameter = DriveSize(m, DefaultRoverFrame);
                    p.Height = Math.Min(0.9 * ChassisHeight, p.Diameter);
                    break;
                case PartRole.Wheel:
                    p.Diameter = WheelDiameter(m);
                    p.Height = p.Diameter;
                    break;
                default:
                    p.Diameter = InstrumentWanted(m);
                    p.Height = p.Diameter;
                    break;
            }
            return p;
        }
    }
}
