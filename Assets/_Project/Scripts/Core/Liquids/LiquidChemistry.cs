using System;

namespace SpaceSim.Liquids
{
    /// <summary>Viscosity blending rule for miscible liquids.</summary>
    public enum ViscosityMixingRule
    {
        /// <summary>Arrhenius / Grunberg–Nissan without interaction term: ln μ = Σ xᵢ·ln μᵢ (mole fractions).</summary>
        Arrhenius,
        /// <summary>Refutas (petroleum blending): VBN = 14.534·ln(ln(ν + 0.8)) + 10.975 blended by mass fraction, ν in cSt.</summary>
        Refutas
    }

    /// <summary>
    /// Stateless chemistry helpers: pH from strong-electrolyte equivalents, the temperature-dependent ion product of
    /// water, Hansen-distance miscibility and mixture property rules. All methods are allocation-free.
    /// </summary>
    public static class LiquidChemistry
    {
        /// <summary>
        /// pK_w of water at temperature T (Harned–Owen fit, 0–60 °C, clamped to 0–100 °C):
        /// pK_w = 4470.99/T − 6.0875 + 0.01706·T. 25 °C → 13.995, 60 °C → 13.02.
        /// </summary>
        public static double WaterPKw(double temperature)
        {
            double t = temperature < 273.15 ? 273.15 : (temperature > 373.15 ? 373.15 : temperature);
            return 4470.99 / t - 6.0875 + 0.01706 * t;
        }

        /// <summary>
        /// pH of an aqueous solution containing a net concentration c of strong acid (c &gt; 0, mol/L of H⁺) or strong base
        /// (c &lt; 0, mol/L of OH⁻). Water autoionisation is included exactly: [H⁺] = c/2 + √(c²/4 + K_w).
        /// </summary>
        public static double PHFromNetAcidity(double netMolPerLitre, double temperature)
        {
            double kw = Math.Pow(10.0, -WaterPKw(temperature));
            double c = netMolPerLitre;
            double h;
            if (c >= 0.0)
            {
                h = 0.5 * c + Math.Sqrt(0.25 * c * c + kw);
            }
            else
            {
                // Evaluate via [OH⁻] to avoid cancellation for strong bases: [OH⁻] = |c|/2 + √(c²/4 + K_w), [H⁺] = K_w/[OH⁻].
                double oh = -0.5 * c + Math.Sqrt(0.25 * c * c + kw);
                h = kw / oh;
            }
            return -Math.Log10(h);
        }

        /// <summary>Hansen distance Ra = √(4·ΔδD² + ΔδP² + ΔδH²) [MPa^½]. Smaller = more alike = more likely miscible.</summary>
        public static double HansenDistance(LiquidDefinition a, LiquidDefinition b)
        {
            double dd = a.hansenDispersion - b.hansenDispersion;
            double dp = a.hansenPolar - b.hansenPolar;
            double dh = a.hansenHydrogen - b.hansenHydrogen;
            return Math.Sqrt(4.0 * dd * dd + dp * dp + dh * dh);
        }

        /// <summary>Density of an ideal (volume-additive) mixture: ρ = Σmᵢ / Σ(mᵢ/ρᵢ) [kg/m³].</summary>
        public static double MixtureDensity(double[] masses, double[] densities, int count)
        {
            double m = 0.0, v = 0.0;
            for (int i = 0; i < count; i++)
            {
                if (masses[i] <= 0.0) continue;
                m += masses[i];
                v += masses[i] / densities[i];
            }
            return v > 0.0 ? m / v : 0.0;
        }

        /// <summary>Arrhenius log-mixing of viscosities with mole fractions: ln μ = Σ xᵢ ln μᵢ [Pa·s].</summary>
        public static double ArrheniusViscosity(double[] moles, double[] viscosities, int count)
        {
            double n = 0.0, s = 0.0;
            for (int i = 0; i < count; i++)
            {
                if (moles[i] <= 0.0) continue;
                n += moles[i];
                s += moles[i] * Math.Log(viscosities[i]);
            }
            return n > 0.0 ? Math.Exp(s / n) : 0.0;
        }

        /// <summary>Refutas viscosity blending number of a kinematic viscosity ν [m²/s]. Valid for ν &gt; 0.2 cSt.</summary>
        public static double RefutasBlendingNumber(double kinematicViscosity)
        {
            double cSt = kinematicViscosity * 1e6;
            return 14.534 * Math.Log(Math.Log(cSt + 0.8)) + 10.975;
        }

        /// <summary>Kinematic viscosity [m²/s] for a Refutas blending number.</summary>
        public static double KinematicViscosityFromRefutas(double vbn) =>
            (Math.Exp(Math.Exp((vbn - 10.975) / 14.534)) - 0.8) * 1e-6;

        /// <summary>True when every ν is above the 0.2 cSt validity limit of the Refutas correlation.</summary>
        public static bool RefutasApplicable(double[] kinematicViscosities, int count)
        {
            for (int i = 0; i < count; i++)
                if (kinematicViscosities[i] * 1e6 <= 0.2) return false;
            return true;
        }

        /// <summary>
        /// Kinematic viscosity of a blend by the Refutas method (mass fractions) [m²/s]. Falls back to Arrhenius
        /// mass-fraction log-mixing of ν when a component is too thin for Refutas (liquid metals, cryogens).
        /// </summary>
        public static double RefutasKinematicViscosity(double[] masses, double[] kinematicViscosities, int count)
        {
            double m = 0.0;
            for (int i = 0; i < count; i++) if (masses[i] > 0.0) m += masses[i];
            if (!(m > 0.0)) return 0.0;
            bool refutas = true;
            for (int i = 0; i < count; i++)
                if (masses[i] > 0.0 && kinematicViscosities[i] * 1e6 <= 0.2) refutas = false;
            double s = 0.0;
            for (int i = 0; i < count; i++)
            {
                if (masses[i] <= 0.0) continue;
                double w = masses[i] / m;
                s += refutas ? w * RefutasBlendingNumber(kinematicViscosities[i]) : w * Math.Log(kinematicViscosities[i]);
            }
            return refutas ? KinematicViscosityFromRefutas(s) : Math.Exp(s);
        }

        /// <summary>Mass-weighted specific heat of a mixture [J/(kg·K)].</summary>
        public static double MixtureSpecificHeat(double[] masses, double[] specificHeats, int count)
        {
            double m = 0.0, c = 0.0;
            for (int i = 0; i < count; i++)
            {
                if (masses[i] <= 0.0) continue;
                m += masses[i];
                c += masses[i] * specificHeats[i];
            }
            return m > 0.0 ? c / m : 0.0;
        }

        /// <summary>Volume-fraction weighted average of a property (surface tension, conductivity, …).</summary>
        public static double VolumeWeighted(double[] volumes, double[] values, int count)
        {
            double v = 0.0, s = 0.0;
            for (int i = 0; i < count; i++)
            {
                if (volumes[i] <= 0.0) continue;
                v += volumes[i];
                s += volumes[i] * values[i];
            }
            return v > 0.0 ? s / v : 0.0;
        }

        /// <summary>
        /// pH of a mixture from the volumes [m³] and acidity equivalents [mol/m³] of its components. Only components with
        /// <c>supportsPH</c> contribute volume. Returns NaN when no aqueous volume is present.
        /// </summary>
        public static double MixturePH(double[] volumes, LiquidDefinition[] definitions, int count, double temperature)
        {
            double eq = 0.0, v = 0.0;
            for (int i = 0; i < count; i++)
            {
                if (volumes[i] <= 0.0 || !definitions[i].supportsPH) continue;
                eq += volumes[i] * definitions[i].acidityEquivalents;
                v += volumes[i];
            }
            if (!(v > 0.0)) return double.NaN;
            return PHFromNetAcidity(eq / v / 1000.0, temperature);
        }
    }
}
