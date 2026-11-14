using System;
using System.Collections.Generic;

namespace SpaceSim.Liquids
{
    /// <summary>
    /// Multi-liquid container simulation on a vertical 2D cell grid (X = width, Y = height; the slice has a finite depth
    /// Z, so every cell is a real volume). Each cell stores, per registered liquid, a liquid mass and a frozen (solid)
    /// mass, plus one temperature. Per substep, in this order:
    /// <list type="number">
    /// <item>properties: component densities ρ(T, p), mixture density/viscosity, heat capacity, hydrostatic pressure;</item>
    /// <item>flow: pressure/gravity-driven exchange through every face with a viscous + inertial resistance law;</item>
    /// <item>stratification: counter-current exchange of a denser phase above a lighter one (buoyancy);</item>
    /// <item>diffusion: mixing of miscible liquids between neighbouring cells;</item>
    /// <item>drains / wall breaches;</item>
    /// <item>heat: conduction between cells, wall/surface convection and radiation to the environment;</item>
    /// <item>phase changes (melting, freezing, boiling at the local pressure, sublimation) and decomposition;</item>
    /// <item>chemical reactions from the registry's rule table;</item>
    /// <item>wall corrosion.</item>
    /// </list>
    /// All arrays are allocated in the constructor; <see cref="Step"/> performs no heap allocation and costs
    /// O(cells × liquids) per substep. Mass is conserved exactly up to round-off (sinks are tracked: vented vapour,
    /// spilled, drained, decomposed and reaction gas).
    /// </summary>
    public sealed partial class LiquidSimulation
    {
        const double VolumeEpsilon = 1e-15;     // [m³] below this a cell is treated as dry
        const double Relaxation = 0.5;          // fraction of the pressure-equalising volume a face may move per substep
        const int ModeProportional = 0, ModeDensestFirst = 1, ModeLightestFirst = 2;

        readonly LiquidSimulationSettings settings;
        readonly LiquidDefinition[] defs;
        readonly string[] liquidIds;
        readonly Dictionary<string, int> liquidIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        readonly int nx, ny, cellCount, liquidCount, groupCount;
        readonly double dx, dy, dz, cellVolume, floorArea, sideArea, permeability;

        // per-liquid constants (flattened from the definitions for the hot loop)
        readonly double[] cpLiquid, cpSolid, kLiquid, kSolid, solidDensity, molarMass, molecularRadius, emissivity, flowResistance;
        readonly int[] groupOf;
        readonly int[][] groupMembers;
        readonly bool anyMiscibleGroup;

        // state
        readonly double[] liquidMass;   // [cell * L + liquid] kg
        readonly double[] solidMass;    // [cell * L + liquid] kg
        readonly double[] temperature;  // [cell] K

        // derived per cell (refreshed each substep, kept consistent by the transfer routines)
        readonly double[] componentDensity; // [cell * L + liquid] kg/m³ at the cell's T and p
        readonly double[] liquidVolume, solidVolume, cellMass, density, viscosity, resistance, heatCapacity;
        readonly double[] pressure, conductivity, diffusivity, surfaceEmissivity;
        double stiffness;
        bool propertiesDirty = true;

        // container walls and drains
        readonly bool[] isWallCell, isBottomWall, wallBreached;
        readonly double[] wallThicknessLeft;
        readonly int drainCapacity;
        readonly int[] drainCell;
        readonly double[] drainDiameter, drainLength, drainRate, drainMass;
        int drainCount, userDrainCount, breachCount;

        // mass that left the liquid inventory
        readonly double[] ventedMass, spilledMass, drainedMass, decomposedMass;
        double reactionGasMass;

        // compiled reactions
        readonly LiquidReaction[] reactions;
        readonly int[][] reactantIndex, productIndex;
        readonly double[][] reactantCoefficient, productCoefficient;
        readonly bool[] reactionAllowed;
        readonly int[] reactionGroup;
        readonly double[] reactionExtent;

        // scratch (no per-step allocation)
        readonly double[] groupVolA, groupMassA, groupVolB, groupMassB, moveA, moveB, scratchL1, scratchL2;
        readonly int[] groupOrder;

        LiquidEnvironment environment;
        double gx, gy, gyAbs;
        int sweep, lastSubsteps;
        double time;

        /// <param name="registry">Liquid data and reaction rules.</param>
        /// <param name="width">Number of cells along X.</param>
        /// <param name="height">Number of cells along Y (row 0 is the container bottom).</param>
        /// <param name="settings">Geometry and numerics (copied).</param>
        /// <param name="environment">Gravity / ambient conditions.</param>
        /// <param name="liquids">Ids of the registered liquids this container can hold (reactions whose reactants and
        /// products are all in this list are active).</param>
        public LiquidSimulation(LiquidRegistry registry, int width, int height, LiquidSimulationSettings settings,
                                LiquidEnvironment environment, params string[] liquids)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (environment == null) throw new ArgumentNullException(nameof(environment));
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "Grid needs at least one cell.");
            if (liquids == null || liquids.Length == 0) throw new ArgumentException("At least one liquid id is required.", nameof(liquids));
            this.settings = (settings ?? new LiquidSimulationSettings()).Clone();
            List<string> issues = this.settings.Validate();
            if (issues.Count > 0) throw new ArgumentException(string.Join(" ", issues), nameof(settings));

            nx = width; ny = height; cellCount = nx * ny;
            liquidCount = liquids.Length;
            dx = this.settings.cellWidth; dy = this.settings.cellHeight; dz = this.settings.cellDepth;
            cellVolume = dx * dy * dz;
            floorArea = dx * dz;
            sideArea = dy * dz;
            double w = this.settings.passageWidth > 0.0 ? this.settings.passageWidth : Math.Min(dx, dy);
            permeability = w * w / 12.0; // plane Poiseuille slot

            defs = new LiquidDefinition[liquidCount];
            liquidIds = new string[liquidCount];
            cpLiquid = new double[liquidCount]; cpSolid = new double[liquidCount];
            kLiquid = new double[liquidCount]; kSolid = new double[liquidCount];
            solidDensity = new double[liquidCount]; molarMass = new double[liquidCount];
            molecularRadius = new double[liquidCount]; emissivity = new double[liquidCount]; flowResistance = new double[liquidCount];
            for (int k = 0; k < liquidCount; k++)
            {
                string id = liquids[k];
                if (liquidIndex.ContainsKey(id)) throw new ArgumentException($"Liquid '{id}' listed twice.", nameof(liquids));
                LiquidDefinition d = registry.Get(id);
                defs[k] = d; liquidIds[k] = id; liquidIndex.Add(id, k);
                cpLiquid[k] = d.specificHeatLiquid; cpSolid[k] = d.specificHeatSolid;
                kLiquid[k] = d.thermalConductivityLiquid; kSolid[k] = d.thermalConductivitySolid;
                solidDensity[k] = d.solidDensity; molarMass[k] = d.molarMass;
                molecularRadius[k] = d.MolecularRadius; emissivity[k] = d.emissivity; flowResistance[k] = d.flowResistanceFactor;
            }

            // Phase groups (computed once): complete-linkage clustering of the miscibility graph. Liquids share a phase only
            // if EVERY pair in it is miscible — a co-solvent miscible with two mutually immiscible liquids (water / oil) must not
            // bridge them into one phase, as connected components would. The most compatible pairs (smallest Hansen distance)
            // merge first; the result does not depend on the order the liquids are listed in.
            var miscible = new bool[liquidCount, liquidCount];
            var pairs = new List<(double distance, int a, int b)>();
            for (int a = 0; a < liquidCount; a++)
                for (int b = a + 1; b < liquidCount; b++)
                {
                    if (!registry.AreMiscible(liquidIds[a], liquidIds[b])) continue;
                    miscible[a, b] = miscible[b, a] = true;
                    pairs.Add((LiquidChemistry.HansenDistance(defs[a], defs[b]), a, b));
                }
            pairs.Sort((p, q) => p.distance != q.distance ? p.distance.CompareTo(q.distance)
                                 : p.a != q.a ? p.a.CompareTo(q.a) : p.b.CompareTo(q.b));
            var cluster = new int[liquidCount];
            for (int k = 0; k < liquidCount; k++) cluster[k] = k;
            foreach (var (_, a, b) in pairs)
            {
                int ca = cluster[a], cb = cluster[b];
                if (ca == cb) continue;
                bool compatible = true;
                for (int i = 0; i < liquidCount && compatible; i++)
                    if (cluster[i] == ca)
                        for (int j = 0; j < liquidCount; j++)
                            if (cluster[j] == cb && !miscible[i, j]) { compatible = false; break; }
                if (!compatible) continue;
                for (int k = 0; k < liquidCount; k++) if (cluster[k] == cb) cluster[k] = ca;
            }
            groupOf = new int[liquidCount];
            var rootToGroup = new int[liquidCount];
            for (int k = 0; k < liquidCount; k++) rootToGroup[k] = -1;
            int groups = 0;
            for (int k = 0; k < liquidCount; k++)
            {
                int root = cluster[k];
                if (rootToGroup[root] < 0) rootToGroup[root] = groups++;
                groupOf[k] = rootToGroup[root];
            }
            groupCount = groups;
            groupMembers = new int[groupCount][];
            for (int g = 0; g < groupCount; g++)
            {
                int n = 0;
                for (int k = 0; k < liquidCount; k++) if (groupOf[k] == g) n++;
                groupMembers[g] = new int[n];
                n = 0;
                for (int k = 0; k < liquidCount; k++) if (groupOf[k] == g) groupMembers[g][n++] = k;
                if (n > 1) anyMiscibleGroup = true;
            }

            int nl = cellCount * liquidCount;
            liquidMass = new double[nl]; solidMass = new double[nl]; componentDensity = new double[nl];
            temperature = new double[cellCount];
            liquidVolume = new double[cellCount]; solidVolume = new double[cellCount]; cellMass = new double[cellCount];
            density = new double[cellCount]; viscosity = new double[cellCount]; resistance = new double[cellCount];
            heatCapacity = new double[cellCount]; pressure = new double[cellCount]; conductivity = new double[cellCount];
            diffusivity = new double[cellCount]; surfaceEmissivity = new double[cellCount];
            for (int c = 0; c < cellCount; c++)
            {
                temperature[c] = environment.AmbientTemperature;
                pressure[c] = environment.AmbientPressure;
                resistance[c] = 1.0;
            }

            isWallCell = new bool[cellCount]; isBottomWall = new bool[cellCount]; wallBreached = new bool[cellCount];
            wallThicknessLeft = new double[cellCount];
            int wallCells = 0;
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    int c = x + y * nx;
                    bool wall = x == 0 || x == nx - 1 || y == 0 || (y == ny - 1 && !this.settings.openTop);
                    isWallCell[c] = wall;
                    isBottomWall[c] = y == 0;
                    wallThicknessLeft[c] = wall ? this.settings.wallThickness : 0.0;
                    if (wall) wallCells++;
                }
            drainCapacity = this.settings.maxDrains + wallCells;
            drainCell = new int[drainCapacity];
            drainDiameter = new double[drainCapacity]; drainLength = new double[drainCapacity]; drainRate = new double[drainCapacity];
            drainMass = new double[drainCapacity * liquidCount];

            ventedMass = new double[liquidCount]; spilledMass = new double[liquidCount];
            drainedMass = new double[liquidCount]; decomposedMass = new double[liquidCount];

            // Reactions whose every reactant and product is held by this container.
            var active = new List<LiquidReaction>();
            IReadOnlyList<LiquidReaction> all = registry.Reactions;
            for (int r = 0; r < all.Count; r++)
            {
                bool ok = true;
                for (int t = 0; t < all[r].reactants.Length && ok; t++) ok = liquidIndex.ContainsKey(all[r].reactants[t].liquidId);
                for (int t = 0; t < all[r].products.Length && ok; t++) ok = liquidIndex.ContainsKey(all[r].products[t].liquidId);
                if (ok) active.Add(all[r]);
            }
            reactions = active.ToArray();
            reactantIndex = new int[reactions.Length][]; reactantCoefficient = new double[reactions.Length][];
            productIndex = new int[reactions.Length][]; productCoefficient = new double[reactions.Length][];
            reactionAllowed = new bool[reactions.Length]; reactionGroup = new int[reactions.Length];
            reactionExtent = new double[reactions.Length];
            for (int r = 0; r < reactions.Length; r++)
            {
                LiquidReaction rule = reactions[r];
                reactantIndex[r] = new int[rule.reactants.Length]; reactantCoefficient[r] = new double[rule.reactants.Length];
                for (int t = 0; t < rule.reactants.Length; t++)
                {
                    reactantIndex[r][t] = liquidIndex[rule.reactants[t].liquidId];
                    reactantCoefficient[r][t] = rule.reactants[t].kgPerMole;
                }
                productIndex[r] = new int[rule.products.Length]; productCoefficient[r] = new double[rule.products.Length];
                for (int t = 0; t < rule.products.Length; t++)
                {
                    productIndex[r][t] = liquidIndex[rule.products[t].liquidId];
                    productCoefficient[r][t] = rule.products[t].kgPerMole;
                }
                int g0 = groupOf[reactantIndex[r][0]];
                bool samePhase = true;
                for (int t = 1; t < reactantIndex[r].Length; t++) samePhase &= groupOf[reactantIndex[r][t]] == g0;
                reactionAllowed[r] = samePhase || !rule.requiresSamePhase;
                reactionGroup[r] = samePhase ? g0 : -1;
            }

            groupVolA = new double[groupCount]; groupMassA = new double[groupCount];
            groupVolB = new double[groupCount]; groupMassB = new double[groupCount];
            groupOrder = new int[groupCount];
            moveA = new double[liquidCount]; moveB = new double[liquidCount];
            scratchL1 = new double[liquidCount]; scratchL2 = new double[liquidCount];

            Environment = environment;
        }

        // ───────────────────────────── configuration ─────────────────────────────

        public int Width => nx;
        public int Height => ny;
        public int LiquidCount => liquidCount;
        /// <summary>Number of distinct liquid phases (groups of mutually miscible liquids).</summary>
        public int PhaseGroupCount => groupCount;
        public double CellVolume => cellVolume;
        /// <summary>Simulated time [s].</summary>
        public double Time => time;
        /// <summary>Substeps used by the last <see cref="Step"/>.</summary>
        public int LastSubstepCount => lastSubsteps;
        public int ActiveReactionCount => reactions.Length;
        public LiquidSimulationSettings Settings => settings.Clone();

        /// <summary>Ambient conditions; may be changed at any time (e.g. the container is carried to another planet).</summary>
        public LiquidEnvironment Environment
        {
            get => environment;
            set
            {
                environment = value ?? throw new ArgumentNullException(nameof(value));
                gx = value.Gravity.x;
                gy = value.Gravity.y;
                gyAbs = Math.Abs(gy);
                propertiesDirty = true;
            }
        }

        public string GetLiquidId(int index) => liquidIds[index];
        public LiquidDefinition GetDefinition(string id) => defs[Index(id)];
        public bool Holds(string id) => id != null && liquidIndex.ContainsKey(id);

        /// <summary>True when the two liquids form one phase in this container (they mix instead of layering).</summary>
        public bool ShareLiquidPhase(string a, string b) => groupOf[Index(a)] == groupOf[Index(b)];

        int Index(string id)
        {
            if (id == null || !liquidIndex.TryGetValue(id, out int k)) throw new KeyNotFoundException($"Liquid '{id}' is not part of this simulation.");
            return k;
        }

        int Cell(int x, int y)
        {
            if (x < 0 || x >= nx || y < 0 || y >= ny) throw new ArgumentOutOfRangeException(nameof(x), $"Cell ({x}, {y}) is outside the {nx}×{ny} grid.");
            return x + y * nx;
        }

        // ───────────────────────────── editing ─────────────────────────────

        /// <summary>Pours a volume [m³] of liquid at temperature T into a cell (mass = V·ρ(T, p_ambient)). Mixes enthalpy with the cell.</summary>
        public void AddLiquid(int x, int y, string id, double volume, double temperatureK)
        {
            if (volume < 0.0) throw new ArgumentOutOfRangeException(nameof(volume));
            int k = Index(id);
            AddLiquidMass(x, y, id, volume * defs[k].Density(temperatureK, environment.AmbientPressure), temperatureK);
        }

        /// <summary>Adds a mass [kg] of liquid at temperature T into a cell.</summary>
        public void AddLiquidMass(int x, int y, string id, double mass, double temperatureK)
        {
            if (mass < 0.0) throw new ArgumentOutOfRangeException(nameof(mass));
            if (!(temperatureK > 0.0)) throw new ArgumentOutOfRangeException(nameof(temperatureK));
            int c = Cell(x, y), k = Index(id);
            MixInHeat(c, mass * cpLiquid[k], temperatureK);
            liquidMass[c * liquidCount + k] += mass;
            propertiesDirty = true;
        }

        /// <summary>Adds a mass [kg] of a liquid's frozen phase (e.g. ice) at temperature T.</summary>
        public void AddSolid(int x, int y, string id, double mass, double temperatureK)
        {
            if (mass < 0.0) throw new ArgumentOutOfRangeException(nameof(mass));
            if (!(temperatureK > 0.0)) throw new ArgumentOutOfRangeException(nameof(temperatureK));
            int c = Cell(x, y), k = Index(id);
            MixInHeat(c, mass * cpSolid[k], temperatureK);
            solidMass[c * liquidCount + k] += mass;
            propertiesDirty = true;
        }

        /// <summary>Fills a rectangle of cells (inclusive bounds) to the given volume fraction with one liquid.</summary>
        public void FillRegion(int x0, int y0, int x1, int y1, string id, double fillFraction, double temperatureK)
        {
            for (int y = Math.Min(y0, y1); y <= Math.Max(y0, y1); y++)
                for (int x = Math.Min(x0, x1); x <= Math.Max(x0, x1); x++)
                    AddLiquid(x, y, id, fillFraction * cellVolume, temperatureK);
        }

        /// <summary>Sets the temperature of a cell's contents [K].</summary>
        public void SetTemperature(int x, int y, double temperatureK)
        {
            if (!(temperatureK > 0.0)) throw new ArgumentOutOfRangeException(nameof(temperatureK));
            temperature[Cell(x, y)] = temperatureK;
            propertiesDirty = true;
        }

        void MixInHeat(int c, double addedCapacity, double addedTemperature)
        {
            double cap = CurrentHeatCapacity(c);
            double total = cap + addedCapacity;
            if (total > 0.0) temperature[c] = (cap * temperature[c] + addedCapacity * addedTemperature) / total;
        }

        double CurrentHeatCapacity(int c)
        {
            int b = c * liquidCount;
            double cap = 0.0;
            for (int k = 0; k < liquidCount; k++) cap += liquidMass[b + k] * cpLiquid[k] + solidMass[b + k] * cpSolid[k];
            return cap;
        }

        /// <summary>
        /// Adds a drain pipe of diameter d and length L [m] that empties a cell to the outside. Flow follows
        /// <see cref="LiquidPhysics.PipeFlowRate"/> driven by the local gauge pressure. Returns the drain index.
        /// </summary>
        public int AddDrain(int x, int y, double diameter, double length)
        {
            if (!(diameter > 0.0)) throw new ArgumentOutOfRangeException(nameof(diameter));
            if (length < 0.0) throw new ArgumentOutOfRangeException(nameof(length));
            if (userDrainCount >= settings.maxDrains) throw new InvalidOperationException($"At most {settings.maxDrains} drains (settings.maxDrains).");
            userDrainCount++;
            return AddDrainInternal(Cell(x, y), diameter, length);
        }

        int AddDrainInternal(int c, double diameter, double length)
        {
            int d = drainCount++;
            drainCell[d] = c;
            drainDiameter[d] = diameter;
            drainLength[d] = length;
            drainRate[d] = 0.0;
            return d;
        }

        // ───────────────────────────── stepping ─────────────────────────────

        /// <summary>Advances the simulation by dt seconds, split into stable substeps.</summary>
        public void Step(double dt)
        {
            if (double.IsNaN(dt) || double.IsInfinity(dt) || dt < 0.0) throw new ArgumentOutOfRangeException(nameof(dt));
            if (dt == 0.0) return;
            if (propertiesDirty) UpdateProperties();
            int n = SubstepCount(dt);
            double h = dt / n;
            for (int s = 0; s < n; s++)
            {
                if (s > 0) UpdateProperties();
                if (settings.enableFlow)
                {
                    FlowPass(h);
                    StratificationPass(h);
                    if (anyMiscibleGroup) DiffusionPass(h);
                    DrainPass(h);
                }
                if (settings.enableHeat) HeatPass(h);
                if (settings.enablePhaseChanges || settings.enableDecomposition) PhasePass(h);
                if (settings.enableReactions && reactions.Length > 0) ReactionPass(h);
                if (settings.enableCorrosion) CorrosionPass(h);
                time += h;
                sweep++;
            }
            lastSubsteps = n;
            UpdateProperties(); // leaves the diagnostics consistent with the new state
        }

        /// <summary>
        /// CFL-style substep count: the fastest gravity-driven speed is the free-fall speed over the container,
        /// u = √(2·g·H); a parcel may cross at most <c>courantNumber</c> of a cell per substep.
        /// </summary>
        int SubstepCount(double dt)
        {
            double g = Math.Sqrt(gx * gx + gy * gy);
            double extent = Math.Max(nx * dx, ny * dy);
            double u = Math.Sqrt(2.0 * g * extent);
            if (!(u > 0.0)) return 1;
            double dtMax = settings.courantNumber * Math.Min(dx, dy) / u;
            double n = Math.Ceiling(dt / dtMax);
            if (n < 1.0) n = 1.0;
            if (n > settings.maxSubsteps) n = settings.maxSubsteps;
            return (int)n;
        }

        void UpdateProperties()
        {
            double maxRho = 0.0;
            for (int c = 0; c < cellCount; c++)
            {
                double t = temperature[c], p = pressure[c];
                int b = c * liquidCount;
                double vl = 0.0, vs = 0.0, mass = 0.0, cap = 0.0, moles = 0.0, lnMu = 0.0, kv = 0.0, rv = 0.0, ev = 0.0, fr = 0.0;
                for (int k = 0; k < liquidCount; k++)
                {
                    double rho = defs[k].Density(t, p);
                    componentDensity[b + k] = rho;
                    double m = liquidMass[b + k];
                    if (m > 0.0)
                    {
                        double v = m / rho;
                        vl += v; mass += m; cap += m * cpLiquid[k];
                        double n = m / molarMass[k];
                        moles += n;
                        lnMu += n * defs[k].LnDynamicViscosity(t);
                        kv += v * kLiquid[k]; rv += v * molecularRadius[k]; ev += v * emissivity[k]; fr += v * flowResistance[k];
                    }
                    double s = solidMass[b + k];
                    if (s > 0.0)
                    {
                        double v = s / solidDensity[k];
                        vs += v; cap += s * cpSolid[k]; kv += v * kSolid[k]; ev += v * emissivity[k];
                    }
                }
                liquidVolume[c] = vl;
                solidVolume[c] = vs;
                cellMass[c] = mass;
                heatCapacity[c] = cap;
                double rhoMix = vl > 0.0 ? mass / vl : 0.0;
                density[c] = rhoMix;
                if (rhoMix > maxRho) maxRho = rhoMix;
                double mu = moles > 0.0 ? Math.Exp(lnMu / moles) : 0.0; // Arrhenius log-mixing (mole fractions)
                viscosity[c] = mu;
                resistance[c] = vl > 0.0 ? fr / vl : 1.0;
                double vt = vl + vs;
                conductivity[c] = vt > 0.0 ? kv / vt : 0.0;
                surfaceEmissivity[c] = vt > 0.0 ? ev / vt : 0.0;
                diffusivity[c] = vl > 0.0 && mu > 0.0
                    ? LiquidConstants.Boltzmann * t / (6.0 * Math.PI * mu * (rv / vl))
                    : 0.0;
            }

            // Hydrostatic pressure, integrated down each column from the free surface (weight of the liquid above).
            double pAmb = environment.AmbientPressure;
            for (int x = 0; x < nx; x++)
            {
                double pTop = pAmb;
                for (int r = 0; r < ny; r++)
                {
                    int y = gy <= 0.0 ? ny - 1 - r : r;
                    int c = x + y * nx;
                    double weight = cellMass[c] * gyAbs / floorArea;
                    pressure[c] = pTop + 0.5 * weight;
                    pTop += weight;
                }
            }

            // Artificial bulk modulus: the deepest possible column compresses the bottom cell by maxCompression.
            double g = Math.Sqrt(gx * gx + gy * gy);
            if (g < 1.0) g = 1.0; // still lets over-filled cells relax in free fall
            stiffness = (maxRho > 0.0 ? maxRho : 1000.0) * g * Math.Max(nx * dx, ny * dy) / settings.maxCompression;
            propertiesDirty = false;
        }

        void EnsureProperties()
        {
            if (propertiesDirty) UpdateProperties();
        }

        // ───────────────────────────── queries ─────────────────────────────

        public double GetLiquidMass(int x, int y, string id) => liquidMass[Cell(x, y) * liquidCount + Index(id)];
        public double GetSolidMass(int x, int y, string id) => solidMass[Cell(x, y) * liquidCount + Index(id)];

        /// <summary>Volume of one liquid in a cell at the cell's temperature and pressure [m³].</summary>
        public double GetLiquidVolume(int x, int y, string id)
        {
            EnsureProperties();
            int i = Cell(x, y) * liquidCount + Index(id);
            return liquidMass[i] / componentDensity[i];
        }

        /// <summary>Total liquid volume in a cell [m³].</summary>
        public double GetCellLiquidVolume(int x, int y) { EnsureProperties(); return liquidVolume[Cell(x, y)]; }

        /// <summary>(liquid + solid volume) / cell volume; slightly above 1 in compressed (deep) cells.</summary>
        public double GetFillFraction(int x, int y)
        {
            EnsureProperties();
            int c = Cell(x, y);
            return (liquidVolume[c] + solidVolume[c]) / cellVolume;
        }

        /// <summary>Volume fraction of a liquid among the liquids of a cell [0..1] (0 for a dry cell).</summary>
        public double GetVolumeFraction(int x, int y, string id)
        {
            EnsureProperties();
            int c = Cell(x, y);
            if (!(liquidVolume[c] > 0.0)) return 0.0;
            int i = c * liquidCount + Index(id);
            return liquidMass[i] / componentDensity[i] / liquidVolume[c];
        }

        public double GetTemperature(int x, int y) => temperature[Cell(x, y)];

        /// <summary>Absolute hydrostatic pressure at the middle of the liquid in a cell [Pa].</summary>
        public double GetPressure(int x, int y) { EnsureProperties(); return pressure[Cell(x, y)]; }

        /// <summary>Mean density of all liquid in a cell [kg/m³] (0 when dry).</summary>
        public double GetMixtureDensity(int x, int y) { EnsureProperties(); return density[Cell(x, y)]; }

        /// <summary>Arrhenius log-mixed viscosity of all liquid in a cell [Pa·s] (0 when dry).</summary>
        public double GetMixtureViscosity(int x, int y) { EnsureProperties(); return viscosity[Cell(x, y)]; }

        /// <summary>Density of the liquid phase (miscible group) that contains <paramref name="id"/> in a cell [kg/m³]; NaN when absent.</summary>
        public double GetPhaseDensity(int x, int y, string id)
        {
            EnsureProperties();
            int c = Cell(x, y), g = groupOf[Index(id)];
            GroupVolumes(c, groupVolA, groupMassA);
            return groupVolA[g] > 0.0 ? groupMassA[g] / groupVolA[g] : double.NaN;
        }

        /// <summary>
        /// Viscosity of the phase containing <paramref name="id"/> in a cell [Pa·s] with the chosen blending rule
        /// (Arrhenius: mole fractions of μ; Refutas: mass fractions of ν, times the phase density). NaN when absent.
        /// </summary>
        public double GetPhaseViscosity(int x, int y, string id, ViscosityMixingRule rule = ViscosityMixingRule.Arrhenius)
        {
            EnsureProperties();
            int c = Cell(x, y), g = groupOf[Index(id)];
            int b = c * liquidCount;
            int[] members = groupMembers[g];
            double t = temperature[c];
            double mass = 0.0, vol = 0.0;
            for (int i = 0; i < members.Length; i++)
            {
                int k = members[i];
                double m = liquidMass[b + k];
                scratchL1[i] = rule == ViscosityMixingRule.Arrhenius ? m / molarMass[k] : m;
                scratchL2[i] = rule == ViscosityMixingRule.Arrhenius
                    ? defs[k].DynamicViscosity(t)
                    : defs[k].DynamicViscosity(t) / componentDensity[b + k];
                if (m > 0.0) { mass += m; vol += m / componentDensity[b + k]; }
            }
            if (!(mass > 0.0)) return double.NaN;
            if (rule == ViscosityMixingRule.Arrhenius) return LiquidChemistry.ArrheniusViscosity(scratchL1, scratchL2, members.Length);
            return LiquidChemistry.RefutasKinematicViscosity(scratchL1, scratchL2, members.Length) * (mass / vol);
        }

        /// <summary>pH of the aqueous liquids in a cell (NaN when the cell has none).</summary>
        public double GetPH(int x, int y) { EnsureProperties(); return CellPH(Cell(x, y)); }

        public double TotalLiquidMass(string id)
        {
            int k = Index(id);
            double s = 0.0;
            for (int c = 0; c < cellCount; c++) s += liquidMass[c * liquidCount + k];
            return s;
        }

        public double TotalSolidMass(string id)
        {
            int k = Index(id);
            double s = 0.0;
            for (int c = 0; c < cellCount; c++) s += solidMass[c * liquidCount + k];
            return s;
        }

        /// <summary>Total liquid volume of one liquid at the current temperatures/pressures [m³].</summary>
        public double TotalLiquidVolume(string id)
        {
            EnsureProperties();
            int k = Index(id);
            double s = 0.0;
            for (int c = 0; c < cellCount; c++) s += liquidMass[c * liquidCount + k] / componentDensity[c * liquidCount + k];
            return s;
        }

        /// <summary>Mass of a liquid that boiled or sublimated away [kg].</summary>
        public double VentedMass(string id) => ventedMass[Index(id)];
        /// <summary>Mass that overflowed the open top [kg].</summary>
        public double SpilledMass(string id) => spilledMass[Index(id)];
        /// <summary>Mass that left through drains and wall breaches [kg].</summary>
        public double DrainedMass(string id) => drainedMass[Index(id)];
        /// <summary>Mass destroyed by thermal decomposition (products leave as gas) [kg].</summary>
        public double DecomposedMass(string id) => decomposedMass[Index(id)];
        /// <summary>Mass released as gas by reactions [kg].</summary>
        public double ReactionGasMass => reactionGasMass;

        /// <summary>Cumulative extent ξ of a reaction in this container [mol] (0 when the reaction is not active here).</summary>
        public double ReactionExtent(string reactionId)
        {
            for (int r = 0; r < reactions.Length; r++)
                if (string.Equals(reactions[r].id, reactionId, StringComparison.Ordinal)) return reactionExtent[r];
            return 0.0;
        }

        /// <summary>Everything in the container plus everything that left it [kg]; constant up to round-off.</summary>
        public double TotalAccountedMass()
        {
            double s = reactionGasMass;
            for (int i = 0; i < liquidMass.Length; i++) s += liquidMass[i] + solidMass[i];
            for (int k = 0; k < liquidCount; k++) s += ventedMass[k] + spilledMass[k] + drainedMass[k] + decomposedMass[k];
            return s;
        }

        /// <summary>Mass-weighted mean height of a liquid above the container bottom [m]; NaN when none is present.</summary>
        public double LiquidCentroidHeight(string id)
        {
            int k = Index(id);
            double m = 0.0, my = 0.0;
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                {
                    double v = liquidMass[(x + y * nx) * liquidCount + k];
                    m += v;
                    my += v * (y + 0.5) * dy;
                }
            return m > 0.0 ? my / m : double.NaN;
        }

        /// <summary>Remaining wall thickness / initial thickness of a boundary cell (1 for interior cells).</summary>
        public double WallIntegrity(int x, int y)
        {
            int c = Cell(x, y);
            return isWallCell[c] ? wallThicknessLeft[c] / settings.wallThickness : 1.0;
        }

        /// <summary>Weakest wall cell's integrity [0..1].</summary>
        public double ContainerIntegrity
        {
            get
            {
                double min = 1.0;
                for (int c = 0; c < cellCount; c++)
                    if (isWallCell[c])
                    {
                        double f = wallThicknessLeft[c] / settings.wallThickness;
                        if (f < min) min = f;
                    }
                return min;
            }
        }

        public bool IsBreached => breachCount > 0;
        public int BreachCount => breachCount;
        public int DrainCount => drainCount;

        /// <summary>Volumetric flow through a drain (or breach) during the last substep [m³/s].</summary>
        public double DrainFlowRate(int drain) => drainRate[drain];

        /// <summary>Mass of a liquid that left through one drain [kg].</summary>
        public double DrainedMassThrough(int drain, string id)
        {
            if (drain < 0 || drain >= drainCount) throw new ArgumentOutOfRangeException(nameof(drain));
            return drainMass[drain * liquidCount + Index(id)];
        }
    }
}
