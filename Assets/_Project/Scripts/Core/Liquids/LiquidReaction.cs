using System;
using System.Collections.Generic;
using SpaceSim.Units;

namespace SpaceSim.Liquids
{
    /// <summary>One reactant or product of a reaction: which liquid and how many kg of it per mole of reaction extent.</summary>
    [Serializable]
    public struct ReactionTerm
    {
        public string liquidId;
        /// <summary>Mass of this liquid consumed (reactant) or formed (product) per mole of reaction extent ξ [kg/mol].</summary>
        public double kgPerMole;

        public ReactionTerm(string liquidId, double kgPerMole)
        {
            this.liquidId = liquidId;
            this.kgPerMole = kgPerMole;
        }
    }

    /// <summary>
    /// Data-driven chemical reaction rule between registered liquids. Stoichiometry is expressed in kg of each liquid per
    /// mole of extent ξ, which keeps the rule exact for solutions (e.g. 1.017 kg of 1 M HCl contains 1 mol HCl).
    /// The rule must conserve mass: Σ reactants = Σ products + gas.
    ///
    /// Rate (when not instantaneous) follows mass-action kinetics with an Arrhenius constant,
    /// dξ/dt = k(T)·V·Π cᵢ, k = A·exp(−Ea/(R·T)), cᵢ = (mᵢ / νᵢ) / V [mol/m³], V = volume of the reacting phase.
    /// </summary>
    [Serializable]
    public class LiquidReaction
    {
        public string id = "";
        public string name = "";
        public ReactionTerm[] reactants = new ReactionTerm[0];
        public ReactionTerm[] products = new ReactionTerm[0];
        /// <summary>Mass released as gas (vented, e.g. CO₂) per mole of extent [kg/mol].</summary>
        public double gasProductKgPerMole;
        public string gasProductFormula = "";
        /// <summary>Reaction enthalpy ΔH per mole of extent [J/mol]; negative = exothermic (heats the liquid).</summary>
        public double enthalpy;
        /// <summary>Diffusion-limited reaction (e.g. H⁺ + OH⁻): proceeds to completion of the limiting reactant within a step.</summary>
        public bool instantaneous;
        /// <summary>Arrhenius pre-exponential factor A [(m³/mol)^(n−1)/s] for n reactants.</summary>
        public double preExponentialFactor;
        /// <summary>Arrhenius activation energy Ea [J/mol].</summary>
        public double activationEnergy;
        /// <summary>The reaction only runs within this temperature window [K].</summary>
        public double minTemperature;
        public double maxTemperature = 1e9;
        /// <summary>The reaction only runs within this pH window of the cell (ignored when ±100, the defaults).</summary>
        public double minPH = -100.0;
        public double maxPH = 100.0;
        /// <summary>When true all reactants must be mutually miscible (one liquid phase); otherwise the rule never fires across phases.</summary>
        public bool requiresSamePhase = true;

        public bool HasPHCondition => minPH > -100.0 || maxPH < 100.0;

        /// <summary>Arrhenius rate constant k(T).</summary>
        public double RateConstant(double temperature)
        {
            if (!(temperature > 0.0)) return 0.0;
            return preExponentialFactor * Math.Exp(-activationEnergy / (PhysicalConstants.MolarGasConstant * temperature));
        }

        public double ReactantMassPerMole
        {
            get
            {
                double s = 0.0;
                if (reactants != null) for (int i = 0; i < reactants.Length; i++) s += reactants[i].kgPerMole;
                return s;
            }
        }

        public double ProductMassPerMole
        {
            get
            {
                double s = gasProductKgPerMole;
                if (products != null) for (int i = 0; i < products.Length; i++) s += products[i].kgPerMole;
                return s;
            }
        }

        public LiquidReaction Clone()
        {
            var c = (LiquidReaction)MemberwiseClone();
            c.reactants = reactants != null ? (ReactionTerm[])reactants.Clone() : new ReactionTerm[0];
            c.products = products != null ? (ReactionTerm[])products.Clone() : new ReactionTerm[0];
            return c;
        }

        /// <summary>Returns human-readable problems (independent of any registry); empty when usable.</summary>
        public List<string> Validate()
        {
            var issues = new List<string>();
            if (string.IsNullOrEmpty(id)) issues.Add("Reaction id must not be empty.");
            if (reactants == null || reactants.Length == 0) issues.Add("A reaction needs at least one reactant.");
            if (reactants != null)
                for (int i = 0; i < reactants.Length; i++)
                    if (string.IsNullOrEmpty(reactants[i].liquidId) || !(reactants[i].kgPerMole > 0.0))
                        issues.Add($"Reactant {i} needs a liquid id and a positive kg/mol.");
            if (products != null)
                for (int i = 0; i < products.Length; i++)
                    if (string.IsNullOrEmpty(products[i].liquidId) || !(products[i].kgPerMole > 0.0))
                        issues.Add($"Product {i} needs a liquid id and a positive kg/mol.");
            if (gasProductKgPerMole < 0.0) issues.Add("Gas product mass must not be negative.");
            double inMass = ReactantMassPerMole, outMass = ProductMassPerMole;
            if (inMass > 0.0 && Math.Abs(inMass - outMass) > 1e-3 * inMass)
                issues.Add($"Mass is not conserved: reactants {inMass} kg/mol vs products {outMass} kg/mol.");
            if (!instantaneous && !(preExponentialFactor > 0.0)) issues.Add("A kinetic reaction needs a positive pre-exponential factor.");
            if (activationEnergy < 0.0) issues.Add("Activation energy must not be negative.");
            if (maxTemperature < minTemperature) issues.Add("Temperature window is empty.");
            if (maxPH < minPH) issues.Add("pH window is empty.");
            return issues;
        }
    }
}
