namespace SpaceSim.Liquids
{
    /// <summary>
    /// Starter set of liquids and reactions with real physical data (CRC Handbook / NIST WebBook / Engineering Toolbox
    /// values near 20 °C, or at the normal boiling point for cryogens). Viscosity fits were derived from two or three
    /// tabulated points (see each entry). Use <see cref="CreateDefaultRegistry"/> and register more data as needed.
    /// </summary>
    public static class LiquidLibrary
    {
        public const string Water = "water";
        public const string SaltWater = "salt_water";
        public const string Brine = "brine";
        public const string Ethanol = "ethanol";
        public const string Glycerol = "glycerol";
        public const string CrudeOil = "crude_oil";
        public const string Kerosene = "kerosene";
        public const string Mercury = "mercury";
        public const string SulfuricAcid = "sulfuric_acid";
        public const string HydrochloricAcid = "hydrochloric_acid";
        public const string SodiumHydroxide = "sodium_hydroxide";
        public const string SodiumSulfateSolution = "sodium_sulfate_solution";
        public const string LiquidMethane = "liquid_methane";
        public const string LiquidNitrogen = "liquid_nitrogen";
        public const string LiquidAmmonia = "liquid_ammonia";
        public const string LiquidCarbonDioxide = "liquid_carbon_dioxide";

        public const string NeutralisationHClNaOH = "neutralisation_hcl_naoh";
        public const string NeutralisationH2SO4NaOH = "neutralisation_h2so4_naoh";

        /// <summary>Vogel parameters of water: μ = 2.414·10⁻⁵ · 10^(247.8/(T − 140)) Pa·s (B = 247.8·ln 10).</summary>
        const double WaterVogelA = 2.414e-5, WaterVogelB = 570.58, WaterVogelC = 140.0;

        /// <summary>A registry pre-filled with every starter liquid, the miscibility exceptions and the starter reactions.</summary>
        public static LiquidRegistry CreateDefaultRegistry()
        {
            var r = new LiquidRegistry();
            r.Register(CreateWater());
            r.Register(CreateSaltWater());
            r.Register(CreateBrine());
            r.Register(CreateEthanol());
            r.Register(CreateGlycerol());
            r.Register(CreateCrudeOil());
            r.Register(CreateKerosene());
            r.Register(CreateMercury());
            r.Register(CreateSulfuricAcid());
            r.Register(CreateHydrochloricAcid());
            r.Register(CreateSodiumHydroxide());
            r.Register(CreateSodiumSulfateSolution());
            r.Register(CreateLiquidMethane());
            r.Register(CreateLiquidNitrogen());
            r.Register(CreateLiquidAmmonia());
            r.Register(CreateLiquidCarbonDioxide());

            // Ammonia sits on the Hansen threshold (Ra ≈ 23–24.6 to non-polar liquids) but is practically insoluble in
            // hydrocarbons and cryogenic nitrogen.
            r.SetMiscibility(LiquidAmmonia, Kerosene, false);
            r.SetMiscibility(LiquidAmmonia, CrudeOil, false);
            r.SetMiscibility(LiquidAmmonia, LiquidMethane, false);
            r.SetMiscibility(LiquidAmmonia, LiquidNitrogen, false);
            // Ethanol is within the Hansen threshold of hydrocarbons (Ra ≈ 21) but blends with kerosene/diesel/crude separate
            // readily, and with any water present ethanol partitions almost entirely into the aqueous phase (log K_ow < 0).
            r.SetMiscibility(Ethanol, Kerosene, false);
            r.SetMiscibility(Ethanol, CrudeOil, false);

            r.RegisterReaction(CreateHClNaOHNeutralisation());
            r.RegisterReaction(CreateH2SO4NaOHNeutralisation());
            return r;
        }

        static LiquidDefinition Aqueous(string id, string name, string formula, double density, double viscosityFactor)
        {
            // Shared aqueous-solution template: water-like expansion, Hansen parameters, pH support.
            return new LiquidDefinition
            {
                id = id, displayName = name, formula = formula,
                referenceTemperature = 293.15, referenceDensity = density,
                thermalExpansion = 2.5e-4, thermalExpansionQuadratic = 3.5e-6, bulkModulus = 2.3e9, solidDensity = 917.0,
                viscosityModel = ViscosityModel.Vogel, viscosityA = WaterVogelA * viscosityFactor, viscosityB = WaterVogelB, viscosityC = WaterVogelC,
                surfaceTensionSlope = -1.7e-4, emissivity = 0.96,
                specificHeatSolid = 2000.0, thermalConductivitySolid = 2.2,
                criticalTemperature = 647.0,
                hansenDispersion = 15.5, hansenPolar = 16.0, hansenHydrogen = 42.3,
                supportsPH = true
            };
        }

        /// <summary>Pure water. Density fit: 1000.7 (0 °C), 998.2 (20 °C), 957 (100 °C); Antoine for the vapour pressure.</summary>
        public static LiquidDefinition CreateWater() => new LiquidDefinition
        {
            id = Water, displayName = "Вода (water)", formula = "H₂O",
            components = new[] { new ChemicalComponent("water", "H₂O", 1.0) },
            molarMass = 0.018015,
            referenceTemperature = 293.15, referenceDensity = 998.2,
            thermalExpansion = 2.07e-4, thermalExpansionQuadratic = 4.10e-6, bulkModulus = 2.2e9, solidDensity = 916.7,
            viscosityModel = ViscosityModel.Vogel, viscosityA = WaterVogelA, viscosityB = WaterVogelB, viscosityC = WaterVogelC,
            surfaceTension = 0.0728, surfaceTensionSlope = -1.74e-4, emissivity = 0.96,
            specificHeatLiquid = 4182.0, specificHeatSolid = 2050.0,
            thermalConductivityLiquid = 0.598, thermalConductivitySolid = 2.22,
            meltingPoint = 273.15, normalBoilingPoint = 373.15,
            enthalpyOfVaporization = 2.257e6, enthalpyOfFusion = 3.34e5, criticalTemperature = 647.1,
            // Antoine (1–100 °C): log10(p/mmHg) = 8.07131 − 1730.63/(233.426 + t/°C), converted to Pa and K.
            vaporPressureModel = VaporPressureModel.Antoine, antoineA = 10.19621, antoineB = 1730.63, antoineC = -39.724,
            supportsPH = true, acidityEquivalents = 0.0,
            hansenDispersion = 15.5, hansenPolar = 16.0, hansenHydrogen = 42.3,
            corrosionRate = 3.2e-12, // ≈ 0.1 mm/yr on carbon steel
            notes = "μ: Vogel fit 2.414e-5·10^(247.8/(T−140)). Density anomaly below 4 °C not modelled."
        };

        /// <summary>Salt water ≈ sea water: 0.5 mol/L NaCl (2.9 wt %). Also the product of 1 M HCl + 1 M NaOH.</summary>
        public static LiquidDefinition CreateSaltWater()
        {
            LiquidDefinition d = Aqueous(SaltWater, "Солёная вода (salt water, 0.5 M NaCl)", "H₂O + NaCl 2.9 %", 1018.6, 1.05);
            d.components = new[] { new ChemicalComponent("water", "H₂O", 0.9713), new ChemicalComponent("sodium chloride", "NaCl", 0.0287) };
            d.molarMass = 0.0184;
            d.surfaceTension = 0.0736;
            d.specificHeatLiquid = 3990.0; d.thermalConductivityLiquid = 0.596;
            d.meltingPoint = 271.4; d.normalBoilingPoint = 373.67; // ΔT_f ≈ 1.9 K, ΔT_b ≈ 0.5 K (colligative, i = 2)
            d.enthalpyOfVaporization = 2.19e6; d.enthalpyOfFusion = 3.24e5;
            d.corrosionRate = 6.3e-12; // ≈ 0.2 mm/yr
            d.notes = "Vaporisation vents the whole solution (salt residue not tracked).";
            return d;
        }

        /// <summary>Saturated brine, 26 wt % NaCl: ρ 1197, μ ≈ 1.9 mPa·s, eutectic −21 °C, boils at 108.5 °C.</summary>
        public static LiquidDefinition CreateBrine()
        {
            LiquidDefinition d = Aqueous(Brine, "Рассол (saturated brine)", "H₂O + NaCl 26 %", 1197.0, 1.90);
            d.components = new[] { new ChemicalComponent("water", "H₂O", 0.74), new ChemicalComponent("sodium chloride", "NaCl", 0.26) };
            d.molarMass = 0.022;
            d.thermalExpansion = 3.9e-4; d.thermalExpansionQuadratic = 0.0; d.bulkModulus = 3.0e9; d.solidDensity = 1100.0;
            d.surfaceTension = 0.0826; d.surfaceTensionSlope = -1.6e-4;
            d.specificHeatLiquid = 3300.0; d.thermalConductivityLiquid = 0.57; d.thermalConductivitySolid = 1.8;
            d.meltingPoint = 252.0; d.normalBoilingPoint = 381.6;
            d.enthalpyOfVaporization = 1.67e6; // per kg of brine: 0.74 kg of water evaporates
            d.enthalpyOfFusion = 2.47e5;
            d.corrosionRate = 1.0e-11;
            return d;
        }

        /// <summary>Ethanol. μ Andrade fit through 1.786 mPa·s (0 °C) and 0.826 mPa·s (40 °C) → 1.18 mPa·s at 20 °C.</summary>
        public static LiquidDefinition CreateEthanol() => new LiquidDefinition
        {
            id = Ethanol, displayName = "Этанол (ethanol)", formula = "C₂H₅OH",
            components = new[] { new ChemicalComponent("ethanol", "C₂H₅OH", 1.0) },
            molarMass = 0.04607,
            referenceTemperature = 293.15, referenceDensity = 789.3,
            thermalExpansion = 1.09e-3, bulkModulus = 0.9e9, solidDensity = 960.0,
            viscosityModel = ViscosityModel.Andrade, viscosityA = 4.267e-6, viscosityB = 1649.0,
            surfaceTension = 0.0223, surfaceTensionSlope = -8.3e-5, emissivity = 0.90,
            specificHeatLiquid = 2440.0, specificHeatSolid = 1100.0,
            thermalConductivityLiquid = 0.171, thermalConductivitySolid = 0.25,
            meltingPoint = 159.0, normalBoilingPoint = 351.44,
            enthalpyOfVaporization = 8.37e5, enthalpyOfFusion = 1.07e5, criticalTemperature = 513.9,
            // Antoine: log10(p/mmHg) = 8.20417 − 1642.89/(230.300 + t/°C).
            vaporPressureModel = VaporPressureModel.Antoine, antoineA = 10.32907, antoineB = 1642.89, antoineC = -42.85,
            hansenDispersion = 15.8, hansenPolar = 8.8, hansenHydrogen = 19.4,
            corrosionRate = 3.2e-13,
            toxicity = 2, flammability = 3, reactivity = 0,
            notes = "Volume contraction and viscosity maximum of water–ethanol mixtures are not modelled (ideal mixing)."
        };

        /// <summary>Glycerol. μ Vogel fit through 12.07 (0 °C), 1.412 (20 °C), 0.284 Pa·s (40 °C); 0.0135 at 100 °C (tab. 0.0148).</summary>
        public static LiquidDefinition CreateGlycerol() => new LiquidDefinition
        {
            id = Glycerol, displayName = "Глицерин (glycerol)", formula = "C₃H₅(OH)₃",
            components = new[] { new ChemicalComponent("glycerol", "C₃H₈O₃", 1.0) },
            molarMass = 0.09209,
            referenceTemperature = 293.15, referenceDensity = 1261.0,
            thermalExpansion = 5.0e-4, bulkModulus = 4.35e9, solidDensity = 1420.0,
            viscosityModel = ViscosityModel.Vogel, viscosityA = 4.32e-6, viscosityB = 1756.7, viscosityC = 154.8,
            surfaceTension = 0.0634, surfaceTensionSlope = -6.0e-5, emissivity = 0.93,
            specificHeatLiquid = 2410.0, specificHeatSolid = 1400.0,
            thermalConductivityLiquid = 0.285, thermalConductivitySolid = 0.4,
            meltingPoint = 290.9, normalBoilingPoint = 563.0,
            enthalpyOfVaporization = 6.62e5, enthalpyOfFusion = 1.99e5, criticalTemperature = 850.0,
            hansenDispersion = 17.4, hansenPolar = 12.1, hansenHydrogen = 29.3,
            toxicity = 1, flammability = 1, reactivity = 0,
            notes = "Real glycerol supercools readily; here it freezes at 17.8 °C. Solid density approximate."
        };

        /// <summary>Light crude oil (API ≈ 35). μ Andrade fit: 10 mPa·s at 20 °C, 4 mPa·s at 50 °C.</summary>
        public static LiquidDefinition CreateCrudeOil() => new LiquidDefinition
        {
            id = CrudeOil, displayName = "Нефть (crude oil)", formula = "CₙH₂ₙ₊₂ mix",
            components = new[] { new ChemicalComponent("alkanes", "CₙH₂ₙ₊₂", 0.75), new ChemicalComponent("aromatics", "CₙHₙ", 0.20), new ChemicalComponent("resins/asphaltenes", "—", 0.05) },
            molarMass = 0.25,
            referenceTemperature = 288.15, referenceDensity = 850.0,
            thermalExpansion = 7.5e-4, bulkModulus = 1.5e9, solidDensity = 900.0,
            viscosityModel = ViscosityModel.Andrade, viscosityA = 5.174e-7, viscosityB = 2893.0,
            surfaceTension = 0.029, surfaceTensionSlope = -7.0e-5, emissivity = 0.95,
            specificHeatLiquid = 1900.0, specificHeatSolid = 1600.0,
            thermalConductivityLiquid = 0.14, thermalConductivitySolid = 0.2,
            meltingPoint = 243.15, normalBoilingPoint = 573.15,
            enthalpyOfVaporization = 3.0e5, enthalpyOfFusion = 2.0e5, criticalTemperature = 800.0,
            hansenDispersion = 16.8, hansenPolar = 1.0, hansenHydrogen = 1.0,
            corrosionRate = 6.3e-13,
            decompositionTemperature = 673.15, decompositionRate = 1e-3, decompositionEnthalpy = 5.0e5,
            toxicity = 2, flammability = 3, reactivity = 0,
            notes = "Single pseudo-component: melting point = pour point, boiling point = mid-distillation temperature. Cracking enthalpy order of magnitude only."
        };

        /// <summary>Kerosene / Jet A-1. μ Andrade fit: 1.64 mPa·s at 20 °C, 4.3 mPa·s at −20 °C.</summary>
        public static LiquidDefinition CreateKerosene() => new LiquidDefinition
        {
            id = Kerosene, displayName = "Керосин (kerosene)", formula = "C₁₀–C₁₆",
            components = new[] { new ChemicalComponent("C10–C16 hydrocarbons", "C₁₂H₂₆ (mean)", 1.0) },
            molarMass = 0.170,
            referenceTemperature = 288.15, referenceDensity = 800.0,
            thermalExpansion = 9.9e-4, bulkModulus = 1.3e9, solidDensity = 850.0,
            viscosityModel = ViscosityModel.Andrade, viscosityA = 3.680e-6, viscosityB = 1788.0,
            surfaceTension = 0.0264, surfaceTensionSlope = -8.6e-5, emissivity = 0.90,
            specificHeatLiquid = 2010.0, specificHeatSolid = 1500.0,
            thermalConductivityLiquid = 0.145, thermalConductivitySolid = 0.2,
            meltingPoint = 226.15, normalBoilingPoint = 473.15,
            enthalpyOfVaporization = 2.51e5, enthalpyOfFusion = 2.0e5, criticalTemperature = 658.0,
            hansenDispersion = 16.0, hansenPolar = 0.0, hansenHydrogen = 0.0,
            toxicity = 2, flammability = 2, reactivity = 0,
            notes = "Freezing point of Jet A-1 (−47 °C); boiling range 150–300 °C represented by 200 °C."
        };

        /// <summary>Mercury. μ Andrade fit: 1.554 mPa·s at 20 °C, 1.24 mPa·s at 100 °C.</summary>
        public static LiquidDefinition CreateMercury() => new LiquidDefinition
        {
            id = Mercury, displayName = "Ртуть (mercury)", formula = "Hg",
            components = new[] { new ChemicalComponent("mercury", "Hg", 1.0) },
            molarMass = 0.20059,
            referenceTemperature = 293.15, referenceDensity = 13546.0,
            thermalExpansion = 1.81e-4, bulkModulus = 2.5e10, solidDensity = 14184.0,
            viscosityModel = ViscosityModel.Andrade, viscosityA = 5.423e-4, viscosityB = 308.6,
            surfaceTension = 0.4855, surfaceTensionSlope = -2.0e-4, emissivity = 0.10,
            specificHeatLiquid = 139.5, specificHeatSolid = 135.0,
            thermalConductivityLiquid = 8.3, thermalConductivitySolid = 30.0,
            meltingPoint = 234.32, normalBoilingPoint = 629.88,
            enthalpyOfVaporization = 2.947e5, enthalpyOfFusion = 1.14e4, criticalTemperature = 1750.0,
            isMetallic = true, hansenDispersion = 0.0, hansenPolar = 0.0, hansenHydrogen = 0.0,
            toxicity = 3, flammability = 0, reactivity = 0,
            notes = "Liquid metal: immiscible with all non-metals."
        };

        /// <summary>Concentrated sulphuric acid, 98 wt % (18.35 mol/L). μ Andrade fit: 27 mPa·s at 20 °C, 15 mPa·s at 40 °C.</summary>
        public static LiquidDefinition CreateSulfuricAcid() => new LiquidDefinition
        {
            id = SulfuricAcid, displayName = "Серная кислота 98 % (sulphuric acid)", formula = "H₂SO₄ 98 %",
            components = new[] { new ChemicalComponent("sulphuric acid", "H₂SO₄", 0.98), new ChemicalComponent("water", "H₂O", 0.02) },
            molarMass = 0.0901,
            referenceTemperature = 293.15, referenceDensity = 1836.0,
            thermalExpansion = 5.7e-4, bulkModulus = 3.0e9, solidDensity = 1950.0,
            viscosityModel = ViscosityModel.Andrade, viscosityA = 2.708e-6, viscosityB = 2699.0,
            surfaceTension = 0.0553, surfaceTensionSlope = -1.0e-4, emissivity = 0.95,
            specificHeatLiquid = 1390.0, specificHeatSolid = 1000.0,
            thermalConductivityLiquid = 0.27, thermalConductivitySolid = 0.4,
            meltingPoint = 276.15, normalBoilingPoint = 610.15,
            enthalpyOfVaporization = 5.11e5, enthalpyOfFusion = 1.09e5, criticalTemperature = 924.0,
            supportsPH = true, acidityEquivalents = 36700.0, // 2 × 18.35 mol/L (both protons treated as strong)
            // Hansen values estimated by analogy (H-bonding, very polar); they place it miscible with aqueous liquids.
            hansenDispersion = 17.0, hansenPolar = 13.0, hansenHydrogen = 30.0,
            corrosionRate = 1.6e-11, // ≈ 0.5 mm/yr: concentrated acid passivates carbon steel
            decompositionTemperature = 613.15, decompositionRate = 0.05, decompositionEnthalpy = 1.80e6, // → SO₃(g) + H₂O(g), +176 kJ/mol
            toxicity = 3, flammability = 0, reactivity = 2,
            notes = "Freezing point of the 98 % grade (≈ +3 °C). Dilution enthalpy is folded into its neutralisation rule."
        };

        /// <summary>Hydrochloric acid 1 mol/L (3.6 wt %): ρ 1017, relative viscosity 1.07.</summary>
        public static LiquidDefinition CreateHydrochloricAcid()
        {
            LiquidDefinition d = Aqueous(HydrochloricAcid, "Соляная кислота 1 M (hydrochloric acid)", "HCl 1 M (aq)", 1017.0, 1.07);
            d.components = new[] { new ChemicalComponent("water", "H₂O", 0.9641), new ChemicalComponent("hydrogen chloride", "HCl", 0.0359) };
            d.molarMass = 0.0180;
            d.surfaceTension = 0.0727;
            d.specificHeatLiquid = 3950.0; d.thermalConductivityLiquid = 0.59;
            d.meltingPoint = 269.6; d.normalBoilingPoint = 374.2;
            d.enthalpyOfVaporization = 2.2e6; d.enthalpyOfFusion = 3.2e5;
            d.acidityEquivalents = 1000.0;
            d.corrosionRate = 3.17e-10; // ≈ 10 mm/yr on carbon steel
            d.toxicity = 3; d.reactivity = 1;
            return d;
        }

        /// <summary>Sodium hydroxide solution 1 mol/L (3.8 wt %): ρ 1041, relative viscosity 1.25.</summary>
        public static LiquidDefinition CreateSodiumHydroxide()
        {
            LiquidDefinition d = Aqueous(SodiumHydroxide, "Гидроксид натрия 1 M (sodium hydroxide)", "NaOH 1 M (aq)", 1041.0, 1.25);
            d.components = new[] { new ChemicalComponent("water", "H₂O", 0.9616), new ChemicalComponent("sodium hydroxide", "NaOH", 0.0384) };
            d.molarMass = 0.0182;
            d.surfaceTension = 0.0745;
            d.specificHeatLiquid = 3970.0; d.thermalConductivityLiquid = 0.60;
            d.meltingPoint = 269.6; d.normalBoilingPoint = 374.2;
            d.enthalpyOfVaporization = 2.2e6; d.enthalpyOfFusion = 3.2e5;
            d.acidityEquivalents = -1000.0;
            d.corrosionRate = 1.6e-12;
            d.toxicity = 3; d.reactivity = 1;
            return d;
        }

        /// <summary>Sodium sulphate solution 0.5 mol/L (6.5 wt %), product of H₂SO₄ + 2 NaOH.</summary>
        public static LiquidDefinition CreateSodiumSulfateSolution()
        {
            LiquidDefinition d = Aqueous(SodiumSulfateSolution, "Раствор сульфата натрия (sodium sulphate solution)", "Na₂SO₄ 0.5 M (aq)", 1059.0, 1.20);
            d.components = new[] { new ChemicalComponent("water", "H₂O", 0.935), new ChemicalComponent("sodium sulphate", "Na₂SO₄", 0.065) };
            d.molarMass = 0.0190;
            d.surfaceTension = 0.0740;
            d.specificHeatLiquid = 3880.0; d.thermalConductivityLiquid = 0.59;
            d.meltingPoint = 270.9; d.normalBoilingPoint = 373.9;
            d.enthalpyOfVaporization = 2.15e6; d.enthalpyOfFusion = 3.1e5;
            d.corrosionRate = 6.0e-12;
            d.toxicity = 1;
            return d;
        }

        /// <summary>Liquid methane at its boiling point. μ Andrade fit: 0.20 mPa·s (90.7 K), 0.116 mPa·s (111.7 K).</summary>
        public static LiquidDefinition CreateLiquidMethane() => new LiquidDefinition
        {
            id = LiquidMethane, displayName = "Жидкий метан (liquid methane)", formula = "CH₄",
            components = new[] { new ChemicalComponent("methane", "CH₄", 1.0) },
            molarMass = 0.016043,
            referenceTemperature = 111.66, referenceDensity = 422.6,
            thermalExpansion = 3.3e-3, bulkModulus = 0.42e9, solidDensity = 494.0,
            viscosityModel = ViscosityModel.Andrade, viscosityA = 1.103e-5, viscosityB = 262.8,
            surfaceTension = 0.0132, surfaceTensionSlope = -2.3e-4, emissivity = 0.90,
            specificHeatLiquid = 3480.0, specificHeatSolid = 2000.0,
            thermalConductivityLiquid = 0.184, thermalConductivitySolid = 0.25,
            meltingPoint = 90.69, normalBoilingPoint = 111.66,
            enthalpyOfVaporization = 5.10e5, enthalpyOfFusion = 5.86e4, criticalTemperature = 190.56,
            hansenDispersion = 13.8, hansenPolar = 0.0, hansenHydrogen = 0.0, // δ from cohesive energy density at T_b
            toxicity = 2, flammability = 4, reactivity = 0
        };

        /// <summary>Liquid nitrogen at its boiling point. μ Andrade fit: 0.22 mPa·s (70 K), 0.158 mPa·s (77.4 K).</summary>
        public static LiquidDefinition CreateLiquidNitrogen() => new LiquidDefinition
        {
            id = LiquidNitrogen, displayName = "Жидкий азот (liquid nitrogen)", formula = "N₂",
            components = new[] { new ChemicalComponent("nitrogen", "N₂", 1.0) },
            molarMass = 0.028013,
            referenceTemperature = 77.36, referenceDensity = 806.1,
            thermalExpansion = 5.4e-3, bulkModulus = 0.5e9, solidDensity = 1026.0,
            viscosityModel = ViscosityModel.Andrade, viscosityA = 6.778e-6, viscosityB = 243.6,
            surfaceTension = 0.00885, surfaceTensionSlope = -2.25e-4, emissivity = 0.90,
            specificHeatLiquid = 2040.0, specificHeatSolid = 1500.0,
            thermalConductivityLiquid = 0.14, thermalConductivitySolid = 0.2,
            meltingPoint = 63.15, normalBoilingPoint = 77.36,
            enthalpyOfVaporization = 1.992e5, enthalpyOfFusion = 2.57e4, criticalTemperature = 126.19,
            hansenDispersion = 11.9, hansenPolar = 0.0, hansenHydrogen = 0.0,
            toxicity = 3, flammability = 0, reactivity = 0
        };

        /// <summary>Liquid (anhydrous) ammonia. μ Andrade fit: 0.255 mPa·s (−33.3 °C), 0.169 mPa·s (0 °C).</summary>
        public static LiquidDefinition CreateLiquidAmmonia() => new LiquidDefinition
        {
            id = LiquidAmmonia, displayName = "Жидкий аммиак (liquid ammonia)", formula = "NH₃",
            components = new[] { new ChemicalComponent("ammonia", "NH₃", 1.0) },
            molarMass = 0.017031,
            referenceTemperature = 239.82, referenceDensity = 682.0,
            thermalExpansion = 2.0e-3, bulkModulus = 1.2e9, solidDensity = 817.0,
            viscosityModel = ViscosityModel.Andrade, viscosityA = 8.778e-6, viscosityB = 807.9,
            surfaceTension = 0.0339, surfaceTensionSlope = -2.45e-4, emissivity = 0.90,
            specificHeatLiquid = 4440.0, specificHeatSolid = 2000.0,
            thermalConductivityLiquid = 0.55, thermalConductivitySolid = 1.0,
            meltingPoint = 195.42, normalBoilingPoint = 239.82,
            enthalpyOfVaporization = 1.371e6, enthalpyOfFusion = 3.323e5, criticalTemperature = 405.4,
            hansenDispersion = 13.7, hansenPolar = 15.7, hansenHydrogen = 17.8,
            toxicity = 3, flammability = 1, reactivity = 0,
            notes = "Weak base: the strong-electrolyte pH model does not apply (supportsPH = false)."
        };

        /// <summary>
        /// Liquid carbon dioxide (no liquid at 1 atm: it exists only between the triple point 216.58 K / 5.185 bar and the
        /// critical point 304.13 K / 73.77 bar). Vapour pressure: Antoine fitted through the triple point, 34.85 bar at
        /// 0 °C and the critical point (gives 294.9 K at 60 bar; tables 295.1 K). Latent heat 15.3 kJ/mol at the triple
        /// point, Watson-scaled to 10.3 kJ/mol at 0 °C (tables 10.2) and 0 at T_c. Density 1030 kg/m³ at 253 K and
        /// 930 at 273 K (quadratic fit through 1178 at the triple point; it overestimates density within ~10 K of T_c).
        /// μ Andrade fit: 0.140 mPa·s (−20 °C), 0.071 mPa·s (20 °C) → 0.097 mPa·s at 0 °C.
        /// </summary>
        public static LiquidDefinition CreateLiquidCarbonDioxide() => new LiquidDefinition
        {
            id = LiquidCarbonDioxide, displayName = "Жидкий CO₂ (liquid carbon dioxide)", formula = "CO₂",
            components = new[] { new ChemicalComponent("carbon dioxide", "CO₂", 1.0) },
            molarMass = 0.04401,
            referenceTemperature = 253.15, referenceDensity = 1030.0, densityReferencePressure = 1.97e6, // saturation at −20 °C
            thermalExpansion = 4.690e-3, thermalExpansionQuadratic = 3.431e-5, bulkModulus = 2.0e8, solidDensity = 1562.0,
            viscosityModel = ViscosityModel.Andrade, viscosityA = 9.66e-7, viscosityB = 1259.7,
            surfaceTension = 0.0087, surfaceTensionSlope = -2.1e-4, emissivity = 0.90,
            specificHeatLiquid = 1970.0, specificHeatSolid = 850.0,
            thermalConductivityLiquid = 0.13, thermalConductivitySolid = 0.6,
            meltingPoint = 216.58,
            normalBoilingPoint = 194.68, // 1-atm sublimation point (no liquid at 1 atm)
            enthalpyOfVaporization = 3.476e5, enthalpyOfVaporizationTemperature = 216.58, // 15.3 kJ/mol at the triple point
            enthalpyOfFusion = 1.965e5,
            criticalTemperature = 304.13, criticalPressure = 7.3773e6, triplePointPressure = 5.185e5,
            vaporPressureModel = VaporPressureModel.Antoine, antoineA = 9.81383, antoineB = 916.8, antoineC = 7.08,
            hansenDispersion = 15.6, hansenPolar = 5.2, hansenHydrogen = 5.8,
            toxicity = 2, flammability = 0, reactivity = 0,
            notes = "Only liquid between the triple point and the critical point; asphyxiant."
        };

        /// <summary>
        /// HCl(aq, 1 M) + NaOH(aq, 1 M) → NaCl(aq, 0.5 M) + heat. Per mole: 1.017 kg acid + 1.041 kg base → 2.058 kg salt
        /// water; ΔH = −57.1 kJ/mol (strong acid / strong base). Expected warming ≈ 57100 / (2.058 · 3990) ≈ 7 K.
        /// </summary>
        public static LiquidReaction CreateHClNaOHNeutralisation() => new LiquidReaction
        {
            id = NeutralisationHClNaOH, name = "HCl + NaOH → NaCl + H₂O",
            reactants = new[] { new ReactionTerm(HydrochloricAcid, 1.017), new ReactionTerm(SodiumHydroxide, 1.041) },
            products = new[] { new ReactionTerm(SaltWater, 2.058) },
            enthalpy = -57100.0, instantaneous = true
        };

        /// <summary>
        /// H₂SO₄ (98 %) + 2 NaOH(aq, 1 M) → Na₂SO₄(aq, 0.5 M). Per mole H₂SO₄: 0.100082 kg acid + 2.082 kg base →
        /// 2.182082 kg solution; ΔH = −(2·57.1 + 74) kJ/mol (neutralisation + dilution of the concentrated acid).
        /// </summary>
        public static LiquidReaction CreateH2SO4NaOHNeutralisation() => new LiquidReaction
        {
            id = NeutralisationH2SO4NaOH, name = "H₂SO₄ + 2 NaOH → Na₂SO₄ + 2 H₂O",
            reactants = new[] { new ReactionTerm(SulfuricAcid, 0.100082), new ReactionTerm(SodiumHydroxide, 2.082) },
            products = new[] { new ReactionTerm(SodiumSulfateSolution, 2.182082) },
            enthalpy = -188200.0, instantaneous = true
        };
    }
}
