using System;
using System.Collections.Generic;
using SpaceSim.Units;

namespace SpaceSim.Liquids
{
    /// <summary>How dynamic viscosity depends on temperature.</summary>
    public enum ViscosityModel
    {
        /// <summary>Andrade / Arrhenius: μ(T) = A·exp(B/T). A [Pa·s], B [K].</summary>
        Andrade,
        /// <summary>Vogel–Fulcher–Tammann: μ(T) = A·exp(B/(T − C)). A [Pa·s], B [K], C [K]. Better for water and glass formers.</summary>
        Vogel
    }

    /// <summary>How the vapour-pressure curve (and therefore the boiling point at a given pressure) is computed.</summary>
    public enum VaporPressureModel
    {
        /// <summary>Clausius–Clapeyron with a constant enthalpy of vaporisation, anchored at the normal boiling point.</summary>
        ClausiusClapeyron,
        /// <summary>Antoine equation in SI form: log10(p / Pa) = A − B / (C + T / K).</summary>
        Antoine
    }

    /// <summary>Physical constants used by the liquid model that are not in <see cref="PhysicalConstants"/>.</summary>
    public static class LiquidConstants
    {
        /// <summary>Boltzmann constant k_B [J/K] (exact, SI 2019).</summary>
        public const double Boltzmann = 1.380649e-23;
        /// <summary>Avogadro constant N_A [1/mol] (exact, SI 2019).</summary>
        public const double Avogadro = 6.02214076e23;
        /// <summary>Reference pressure of the liquid data (1 standard atmosphere) [Pa].</summary>
        public const double ReferencePressure = 101325.0;
        /// <summary>Reference temperature of corrosion rates [K] (25 °C).</summary>
        public const double CorrosionReferenceTemperature = 298.15;
    }

    /// <summary>One chemical species in a liquid (for display and bookkeeping; physics uses the bulk properties).</summary>
    [Serializable]
    public struct ChemicalComponent
    {
        public string name;
        public string formula;
        /// <summary>Mass fraction of this species in the liquid [0..1].</summary>
        public double massFraction;

        public ChemicalComponent(string name, string formula, double massFraction)
        {
            this.name = name;
            this.formula = formula;
            this.massFraction = massFraction;
        }
    }

    /// <summary>
    /// Data describing one liquid (pure substance or fixed-composition solution). Liquids are pure data: the simulation
    /// never contains per-liquid code, so a new liquid is added by filling in one of these and registering it in a
    /// <see cref="LiquidRegistry"/>. All values are SI (kg, m, s, K, Pa, J, mol).
    /// </summary>
    [Serializable]
    public class LiquidDefinition
    {
        // ───────────── identity / composition ─────────────
        public string id = "";
        public string displayName = "";
        /// <summary>Chemical formula of the main species or a short composition string, e.g. "H₂O" or "H₂SO₄ 98 %".</summary>
        public string formula = "";
        public ChemicalComponent[] components = new ChemicalComponent[0];
        /// <summary>Molar mass M [kg/mol]; the mean molar mass for mixtures/solutions.</summary>
        public double molarMass = 0.018015;

        // ───────────── density / compressibility ─────────────
        /// <summary>Temperature at which <see cref="referenceDensity"/> is given [K].</summary>
        public double referenceTemperature = 293.15;
        /// <summary>Liquid density at <see cref="referenceTemperature"/> and 1 atm [kg/m³].</summary>
        public double referenceDensity = 998.2;
        /// <summary>Volumetric thermal expansion β [1/K]: ρ(T) = ρ_ref / (1 + β·ΔT + β₂·ΔT²).</summary>
        public double thermalExpansion = 2.07e-4;
        /// <summary>Quadratic expansion term β₂ [1/K²] (0 for a linear law).</summary>
        public double thermalExpansionQuadratic = 0.0;
        /// <summary>Isothermal bulk modulus K [Pa]: ρ(p) = ρ(p_ref)·exp((p − p_ref)/K).</summary>
        public double bulkModulus = 2.2e9;
        /// <summary>
        /// Pressure p_ref at which <see cref="referenceDensity"/> was measured [Pa] (1 atm by default; the saturation
        /// pressure for liquids such as CO₂ that do not exist at 1 atm).
        /// </summary>
        public double densityReferencePressure = LiquidConstants.ReferencePressure;
        /// <summary>Density of the frozen (solid) phase [kg/m³].</summary>
        public double solidDensity = 917.0;

        // ───────────── viscosity / flow ─────────────
        public ViscosityModel viscosityModel = ViscosityModel.Andrade;
        /// <summary>A [Pa·s] of the viscosity law.</summary>
        public double viscosityA = 1e-3;
        /// <summary>B [K] of the viscosity law.</summary>
        public double viscosityB = 0.0;
        /// <summary>C [K] of the Vogel law (unused by Andrade).</summary>
        public double viscosityC = 0.0;
        /// <summary>
        /// Dimensionless multiplier (≥ 1) on viscous flow resistance, for behaviour a Newtonian viscosity does not capture
        /// (waxy crude, gels, suspended solids). 1 = plain Newtonian liquid.
        /// </summary>
        public double flowResistanceFactor = 1.0;

        // ───────────── surface ─────────────
        /// <summary>Surface tension against its vapour/air at <see cref="referenceTemperature"/> [N/m].</summary>
        public double surfaceTension = 0.0728;
        /// <summary>dσ/dT [N/(m·K)] (negative for practically every liquid).</summary>
        public double surfaceTensionSlope = -1.7e-4;
        /// <summary>Thermal emissivity of the free surface [0..1].</summary>
        public double emissivity = 0.95;

        // ───────────── thermal ─────────────
        /// <summary>Specific heat capacity of the liquid c_p [J/(kg·K)].</summary>
        public double specificHeatLiquid = 4182.0;
        /// <summary>Specific heat capacity of the solid [J/(kg·K)].</summary>
        public double specificHeatSolid = 2050.0;
        /// <summary>Thermal conductivity of the liquid [W/(m·K)].</summary>
        public double thermalConductivityLiquid = 0.598;
        /// <summary>Thermal conductivity of the solid [W/(m·K)].</summary>
        public double thermalConductivitySolid = 2.2;

        // ───────────── phase changes ─────────────
        /// <summary>Melting (freezing) point [K] (pressure dependence neglected).</summary>
        public double meltingPoint = 273.15;
        /// <summary>
        /// Boiling point at 1 atm [K]; anchor of the Clausius–Clapeyron curve. For substances with no liquid at 1 atm
        /// (CO₂) this is the 1-atm sublimation temperature and the vapour pressure must come from the Antoine model.
        /// </summary>
        public double normalBoilingPoint = 373.15;
        /// <summary>Specific enthalpy of vaporisation L_v [J/kg] at <see cref="enthalpyOfVaporizationTemperature"/>.</summary>
        public double enthalpyOfVaporization = 2.257e6;
        /// <summary>Temperature at which <see cref="enthalpyOfVaporization"/> is given [K]; 0 = the normal boiling point.</summary>
        public double enthalpyOfVaporizationTemperature;
        /// <summary>Specific enthalpy of fusion L_f [J/kg].</summary>
        public double enthalpyOfFusion = 3.34e5;
        /// <summary>Critical temperature T_c [K]: above it no liquid exists at any pressure, and L_v → 0 (Watson).</summary>
        public double criticalTemperature = 647.1;
        /// <summary>Critical pressure p_c [Pa] (0 = unknown). Informational; the critical temperature bounds the liquid.</summary>
        public double criticalPressure;
        /// <summary>
        /// Triple-point pressure [Pa]. Below it no stable liquid exists (the solid sublimates). 0 = derive it from the
        /// vapour-pressure curve at the melting point.
        /// </summary>
        public double triplePointPressure;
        public VaporPressureModel vaporPressureModel = VaporPressureModel.ClausiusClapeyron;
        /// <summary>Antoine A (SI form: log10(p/Pa) = A − B/(C + T/K)).</summary>
        public double antoineA;
        /// <summary>Antoine B [K].</summary>
        public double antoineB;
        /// <summary>Antoine C [K].</summary>
        public double antoineC;

        // ───────────── chemistry ─────────────
        /// <summary>
        /// Net strong-acid equivalents per volume [mol/m³]: + for H⁺ (acids), − for OH⁻ (bases), 0 for neutral liquids.
        /// 1 M HCl = +1000, 1 M NaOH = −1000, 1 M H₂SO₄ = +2000. Mixture pH is derived from the sum of these.
        /// </summary>
        public double acidityEquivalents;
        /// <summary>True when the liquid is an aqueous (ionising) medium, so pH is defined for it.</summary>
        public bool supportsPH;
        /// <summary>Hansen solubility parameter, dispersion part δ_D [MPa^½].</summary>
        public double hansenDispersion = 15.5;
        /// <summary>Hansen solubility parameter, polar part δ_P [MPa^½].</summary>
        public double hansenPolar = 16.0;
        /// <summary>Hansen solubility parameter, hydrogen-bond part δ_H [MPa^½].</summary>
        public double hansenHydrogen = 42.3;
        /// <summary>Liquid metal: immiscible with every non-metal regardless of solubility parameters.</summary>
        public bool isMetallic;
        /// <summary>Uniform corrosion (wall thinning) rate on carbon steel at 25 °C, fully wetted [m/s].</summary>
        public double corrosionRate;
        /// <summary>Activation energy of the corrosion rate [J/mol] (≈ 40 kJ/mol: rate doubles roughly every 13 K).</summary>
        public double corrosionActivationEnergy = 40000.0;
        /// <summary>Temperature above which the liquid decomposes chemically [K]; ≤ 0 = chemically stable.</summary>
        public double decompositionTemperature;
        /// <summary>First-order decomposition rate constant above <see cref="decompositionTemperature"/> [1/s].</summary>
        public double decompositionRate;
        /// <summary>Heat absorbed per kg decomposed [J/kg] (positive = endothermic). Products are assumed to leave as gas.</summary>
        public double decompositionEnthalpy;
        /// <summary>NFPA 704 health hazard 0–4.</summary>
        public int toxicity;
        /// <summary>NFPA 704 flammability 0–4.</summary>
        public int flammability;
        /// <summary>NFPA 704 instability / reactivity 0–4.</summary>
        public int reactivity;
        /// <summary>Data sources and approximations.</summary>
        public string notes = "";

        // ───────────── evaluated properties ─────────────

        /// <summary>Liquid density at temperature T and 1 atm [kg/m³].</summary>
        public double Density(double temperature)
        {
            double dT = temperature - referenceTemperature;
            double expansion = 1.0 + thermalExpansion * dT + thermalExpansionQuadratic * dT * dT;
            if (expansion < 0.05) expansion = 0.05; // far outside the fitted range: keep the law finite and positive
            return referenceDensity / expansion;
        }

        /// <summary>Liquid density at temperature T and absolute pressure p [kg/m³] (compressibility via the bulk modulus).</summary>
        public double Density(double temperature, double pressure)
        {
            double rho = Density(temperature);
            return bulkModulus > 0.0 ? rho * Math.Exp((pressure - densityReferencePressure) / bulkModulus) : rho;
        }

        /// <summary>ln μ(T) with μ in Pa·s. Cheaper than <see cref="DynamicViscosity"/> and used for log-mixing.</summary>
        public double LnDynamicViscosity(double temperature)
        {
            double t = temperature > 1.0 ? temperature : 1.0;
            if (viscosityModel == ViscosityModel.Vogel)
            {
                double d = t - viscosityC;
                if (d < 1.0) d = 1.0; // the VFT law diverges at T = C; clamp to a very viscous but finite value
                return Math.Log(viscosityA) + viscosityB / d;
            }
            return Math.Log(viscosityA) + viscosityB / t;
        }

        /// <summary>Dynamic viscosity μ(T) [Pa·s].</summary>
        public double DynamicViscosity(double temperature) => Math.Exp(LnDynamicViscosity(temperature));

        /// <summary>Kinematic viscosity ν = μ/ρ [m²/s].</summary>
        public double KinematicViscosity(double temperature) => DynamicViscosity(temperature) / Density(temperature);

        /// <summary>Surface tension σ(T) [N/m], linear in T, zero at and above the critical temperature.</summary>
        public double SurfaceTension(double temperature)
        {
            if (criticalTemperature > 0.0 && temperature >= criticalTemperature) return 0.0;
            double s = surfaceTension + surfaceTensionSlope * (temperature - referenceTemperature);
            return s > 0.0 ? s : 0.0;
        }

        /// <summary>Molar enthalpy of vaporisation at the reference temperature [J/mol] (constant-ΔH Clausius–Clapeyron).</summary>
        public double MolarEnthalpyOfVaporization => enthalpyOfVaporization * molarMass;

        /// <summary>
        /// Latent heat of vaporisation at temperature T [J/kg], Watson correlation
        /// L(T) = L_ref·((T_c − T)/(T_c − T_ref))^0.38, which falls to zero at the critical point.
        /// Without a critical temperature the reference value is returned.
        /// </summary>
        public double EnthalpyOfVaporizationAt(double temperature)
        {
            double tRef = enthalpyOfVaporizationTemperature > 0.0 ? enthalpyOfVaporizationTemperature : normalBoilingPoint;
            if (!(criticalTemperature > tRef)) return enthalpyOfVaporization;
            if (temperature >= criticalTemperature) return 0.0;
            return enthalpyOfVaporization * Math.Pow((criticalTemperature - temperature) / (criticalTemperature - tRef), 0.38);
        }

        /// <summary>Saturated vapour pressure at temperature T [Pa].</summary>
        public double VaporPressure(double temperature)
        {
            if (!(temperature > 0.0)) return 0.0;
            if (vaporPressureModel == VaporPressureModel.Antoine && antoineB > 0.0)
            {
                double denom = antoineC + temperature;
                if (denom <= 0.0) return 0.0;
                return Math.Pow(10.0, antoineA - antoineB / denom);
            }
            double h = MolarEnthalpyOfVaporization;
            return LiquidConstants.ReferencePressure *
                   Math.Exp(-h / PhysicalConstants.MolarGasConstant * (1.0 / temperature - 1.0 / normalBoilingPoint));
        }

        /// <summary>
        /// Boiling point at absolute pressure p [K] — the inverse of <see cref="VaporPressure"/>, capped at the critical
        /// temperature. Below the triple-point pressure the result is lower than the melting point (no stable liquid).
        /// p ≤ 0 (hard vacuum) returns 0: the liquid boils at any temperature.
        /// </summary>
        public double BoilingPoint(double pressure)
        {
            if (!(pressure > 0.0)) return 0.0;
            double t;
            if (vaporPressureModel == VaporPressureModel.Antoine && antoineB > 0.0)
            {
                double denom = antoineA - Math.Log10(pressure);
                t = denom > 0.0 ? antoineB / denom - antoineC : double.PositiveInfinity;
            }
            else
            {
                double inv = 1.0 / normalBoilingPoint -
                             PhysicalConstants.MolarGasConstant * Math.Log(pressure / LiquidConstants.ReferencePressure) / MolarEnthalpyOfVaporization;
                t = inv > 0.0 ? 1.0 / inv : double.PositiveInfinity;
            }
            if (criticalTemperature > 0.0 && t > criticalTemperature) t = criticalTemperature;
            return t > 0.0 ? t : 0.0;
        }

        /// <summary>
        /// Triple-point pressure [Pa]: the explicit <see cref="triplePointPressure"/> when given, otherwise the value
        /// implied by the vapour-pressure curve at the melting point.
        /// </summary>
        public double TriplePointPressure => triplePointPressure > 0.0 ? triplePointPressure : VaporPressure(meltingPoint);

        /// <summary>
        /// True when a stable liquid phase can exist at pressure p: at or above the triple-point pressure and with a
        /// boiling point above the melting point.
        /// </summary>
        public bool IsLiquidPossibleAt(double pressure) =>
            pressure >= TriplePointPressure && BoilingPoint(pressure) > meltingPoint;

        /// <summary>
        /// True when the substance is a stable liquid at temperature T and pressure p: liquid is possible at p,
        /// T_m ≤ T &lt; T_b(p) and T &lt; T_c. Never true above the critical temperature or below the triple point.
        /// </summary>
        public bool IsLiquidStableAt(double temperature, double pressure)
        {
            if (!IsLiquidPossibleAt(pressure)) return false;
            if (criticalTemperature > 0.0 && temperature >= criticalTemperature) return false;
            return temperature >= meltingPoint && temperature < BoilingPoint(pressure);
        }

        /// <summary>
        /// Sublimation temperature of the solid at pressure p below the triple point [K], from Clausius–Clapeyron
        /// anchored at the triple point with L_s = L_v(T_m) + L_f. Above the triple-point pressure the solid melts first
        /// and the melting point is returned.
        /// </summary>
        public double SublimationPoint(double pressure)
        {
            if (!(pressure > 0.0)) return 0.0;
            double pt = TriplePointPressure;
            if (pressure >= pt) return meltingPoint;
            double hs = (EnthalpyOfVaporizationAt(meltingPoint) + enthalpyOfFusion) * molarMass;
            double inv = 1.0 / meltingPoint - PhysicalConstants.MolarGasConstant * Math.Log(pressure / pt) / hs;
            return 1.0 / inv;
        }

        /// <summary>Hildebrand solubility parameter δ = √(δ_D² + δ_P² + δ_H²) [MPa^½].</summary>
        public double HildebrandParameter =>
            Math.Sqrt(hansenDispersion * hansenDispersion + hansenPolar * hansenPolar + hansenHydrogen * hansenHydrogen);

        /// <summary>Effective molecular radius from the molar volume, r = (3·M / (4π·ρ·N_A))^⅓ [m].</summary>
        public double MolecularRadius =>
            Math.Pow(3.0 * molarMass / (4.0 * Math.PI * referenceDensity * LiquidConstants.Avogadro), 1.0 / 3.0);

        /// <summary>Stokes–Einstein self-diffusion estimate D = k_B·T / (6π·μ·r) [m²/s].</summary>
        public double MolecularDiffusivity(double temperature) =>
            LiquidConstants.Boltzmann * temperature / (6.0 * Math.PI * DynamicViscosity(temperature) * MolecularRadius);

        /// <summary>pH of the pure liquid at temperature T, or NaN when pH is not defined for it.</summary>
        public double PH(double temperature) =>
            supportsPH ? LiquidChemistry.PHFromNetAcidity(acidityEquivalents / 1000.0, temperature) : double.NaN;

        /// <summary>Corrosion (wall thinning) rate at temperature T [m/s] with an Arrhenius temperature dependence.</summary>
        public double CorrosionRateAt(double temperature)
        {
            if (!(corrosionRate > 0.0) || !(temperature > 0.0)) return 0.0;
            return corrosionRate * Math.Exp(-corrosionActivationEnergy / PhysicalConstants.MolarGasConstant *
                                            (1.0 / temperature - 1.0 / LiquidConstants.CorrosionReferenceTemperature));
        }

        /// <summary>True when the liquid does not decompose at temperature T.</summary>
        public bool IsChemicallyStable(double temperature) =>
            !(decompositionTemperature > 0.0) || temperature < decompositionTemperature;

        public LiquidDefinition Clone()
        {
            var c = (LiquidDefinition)MemberwiseClone();
            c.components = components != null ? (ChemicalComponent[])components.Clone() : new ChemicalComponent[0];
            return c;
        }

        /// <summary>Returns human-readable problems; empty when the data is usable by the simulation.</summary>
        public List<string> Validate()
        {
            var issues = new List<string>();
            if (string.IsNullOrEmpty(id)) issues.Add("Liquid id must not be empty.");
            if (!(molarMass > 0.0)) issues.Add("Molar mass must be positive.");
            if (!(referenceTemperature > 0.0)) issues.Add("Reference temperature must be positive.");
            if (!(referenceDensity > 0.0)) issues.Add("Reference density must be positive.");
            if (!(solidDensity > 0.0)) issues.Add("Solid density must be positive.");
            if (bulkModulus < 0.0) issues.Add("Bulk modulus must not be negative.");
            if (!(viscosityA > 0.0)) issues.Add("Viscosity coefficient A must be positive.");
            if (flowResistanceFactor < 1.0) issues.Add("Flow resistance factor must be ≥ 1.");
            if (!(specificHeatLiquid > 0.0) || !(specificHeatSolid > 0.0)) issues.Add("Specific heats must be positive.");
            if (thermalConductivityLiquid < 0.0 || thermalConductivitySolid < 0.0) issues.Add("Thermal conductivities must not be negative.");
            if (!(meltingPoint > 0.0)) issues.Add("Melting point must be positive.");
            if (!(normalBoilingPoint > 0.0)) issues.Add("Normal boiling point must be positive.");
            if (vaporPressureModel == VaporPressureModel.ClausiusClapeyron && !(normalBoilingPoint > meltingPoint))
                issues.Add("Clausius–Clapeyron needs a normal boiling point above the melting point (use the Antoine model for liquids that do not exist at 1 atm).");
            if (!(enthalpyOfVaporization > 0.0) || !(enthalpyOfFusion > 0.0)) issues.Add("Latent heats must be positive.");
            if (criticalTemperature > 0.0 && criticalTemperature <= meltingPoint) issues.Add("Critical temperature must be above the melting point.");
            if (criticalTemperature > 0.0 && enthalpyOfVaporizationTemperature >= criticalTemperature) issues.Add("Latent-heat reference temperature must be below the critical temperature.");
            if (triplePointPressure < 0.0 || criticalPressure < 0.0) issues.Add("Triple/critical pressures must not be negative.");
            if (triplePointPressure > 0.0 && criticalPressure > 0.0 && triplePointPressure >= criticalPressure) issues.Add("Triple-point pressure must be below the critical pressure.");
            if (!(densityReferencePressure >= 0.0)) issues.Add("Density reference pressure must not be negative.");
            if (vaporPressureModel == VaporPressureModel.Antoine && !(antoineB > 0.0)) issues.Add("Antoine model needs B > 0.");
            if (emissivity < 0.0 || emissivity > 1.0) issues.Add("Emissivity must be within 0..1.");
            if (corrosionRate < 0.0) issues.Add("Corrosion rate must not be negative.");
            if (decompositionRate < 0.0) issues.Add("Decomposition rate must not be negative.");
            if (components != null)
            {
                double sum = 0.0;
                for (int i = 0; i < components.Length; i++) sum += components[i].massFraction;
                if (components.Length > 0 && Math.Abs(sum - 1.0) > 0.01) issues.Add("Component mass fractions must add up to 1.");
            }
            return issues;
        }

        public override string ToString() => $"{displayName} [{id}]";
    }
}
