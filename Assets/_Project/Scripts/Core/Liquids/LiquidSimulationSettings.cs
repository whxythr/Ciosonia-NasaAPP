using System;
using System.Collections.Generic;

namespace SpaceSim.Liquids
{
    /// <summary>Geometry, numerics and container properties of a <see cref="LiquidSimulation"/>.</summary>
    [Serializable]
    public class LiquidSimulationSettings
    {
        /// <summary>Cell size along the container width X [m].</summary>
        public double cellWidth = 0.1;
        /// <summary>Cell size along the container height Y [m].</summary>
        public double cellHeight = 0.1;
        /// <summary>Depth of the container slice along Z [m] (cell volume = width·height·depth).</summary>
        public double cellDepth = 0.1;

        /// <summary>
        /// Hydraulic width w of the passages between neighbouring cells [m]; 0 = the cell size (open tank). The viscous
        /// conductance of a face uses the slot permeability k = w²/12, so a small w models a porous / baffled container
        /// where viscosity dominates.
        /// </summary>
        public double passageWidth;
        /// <summary>Open fraction of each internal face (1 = no obstruction).</summary>
        public double openAreaFraction = 1.0;
        /// <summary>Discharge coefficient of the inertial (Bernoulli) resistance of a face.</summary>
        public double dischargeCoefficient = 1.0;

        /// <summary>
        /// Artificial compressibility: the bottom cell of a full container is compressed by at most this fraction. Keeps
        /// pressure local and explicit (no global pressure solve) while still letting liquid rise in U-tubes.
        /// </summary>
        public double maxCompression = 0.02;
        /// <summary>Courant number: a liquid parcel moves at most this fraction of a cell per substep.</summary>
        public double courantNumber = 0.5;
        /// <summary>Upper bound on substeps per <see cref="LiquidSimulation.Step"/>; flux limiters keep it stable beyond.</summary>
        public int maxSubsteps = 16;

        /// <summary>Turbulent/eddy diffusivity added to molecular diffusion when mixing miscible liquids [m²/s].</summary>
        public double eddyDiffusivity = 1e-6;

        /// <summary>Overall heat-transfer coefficient through the container walls to the surroundings [W/(m²·K)].</summary>
        public double wallHeatTransferCoefficient = 2.0;
        /// <summary>
        /// Natural-convection coefficient of the free surface at sea-level air density (1.2 kg/m³) [W/(m²·K)]; scaled by
        /// (ρ_gas/1.2)^(2/3) for other atmospheres (Gr^(1/3) scaling), so it vanishes in vacuum.
        /// </summary>
        public double surfaceHeatTransferCoefficient = 10.0;
        /// <summary>Grey-body radiation of the free surface to the ambient temperature.</summary>
        public bool radiativeExchange = true;
        /// <summary>Open top: liquid can spill over the top edge and the top surface exchanges heat with the gas.</summary>
        public bool openTop = true;

        /// <summary>Container wall thickness [m] (corrosion budget).</summary>
        public double wallThickness = 0.003;
        /// <summary>Corrosion resistance of the wall material relative to carbon steel (stainless ≈ 10–100).</summary>
        public double wallCorrosionResistance = 1.0;
        /// <summary>Diameter of the hole that opens where a wall corrodes through [m].</summary>
        public double breachHoleDiameter = 0.005;
        /// <summary>Maximum number of user drains (wall breaches have their own reserved slots).</summary>
        public int maxDrains = 8;

        public bool enableFlow = true;
        public bool enableHeat = true;
        public bool enablePhaseChanges = true;
        public bool enableReactions = true;
        public bool enableCorrosion = true;
        public bool enableDecomposition = true;

        public LiquidSimulationSettings Clone() => (LiquidSimulationSettings)MemberwiseClone();

        public List<string> Validate()
        {
            var issues = new List<string>();
            if (!(cellWidth > 0.0) || !(cellHeight > 0.0) || !(cellDepth > 0.0)) issues.Add("Cell dimensions must be positive.");
            if (passageWidth < 0.0) issues.Add("Passage width must not be negative.");
            if (!(openAreaFraction > 0.0) || openAreaFraction > 1.0) issues.Add("Open area fraction must be within (0, 1].");
            if (!(dischargeCoefficient > 0.0)) issues.Add("Discharge coefficient must be positive.");
            if (!(maxCompression > 0.0) || maxCompression > 0.5) issues.Add("Max compression must be within (0, 0.5].");
            if (!(courantNumber > 0.0) || courantNumber > 1.0) issues.Add("Courant number must be within (0, 1].");
            if (maxSubsteps < 1) issues.Add("At least one substep is required.");
            if (eddyDiffusivity < 0.0) issues.Add("Eddy diffusivity must not be negative.");
            if (wallHeatTransferCoefficient < 0.0 || surfaceHeatTransferCoefficient < 0.0) issues.Add("Heat-transfer coefficients must not be negative.");
            if (!(wallThickness > 0.0)) issues.Add("Wall thickness must be positive.");
            if (!(wallCorrosionResistance > 0.0)) issues.Add("Wall corrosion resistance must be positive.");
            if (!(breachHoleDiameter > 0.0)) issues.Add("Breach hole diameter must be positive.");
            if (maxDrains < 0) issues.Add("Max drains must not be negative.");
            return issues;
        }
    }
}
