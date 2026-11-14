using System;

namespace SpaceSim.Liquids
{
    public sealed partial class LiquidSimulation
    {
        // ───────────────────────────── reactions ─────────────────────────────

        void ReactionPass(double h)
        {
            for (int c = 0; c < cellCount; c++)
            {
                int b = c * liquidCount;
                for (int r = 0; r < reactions.Length; r++)
                {
                    if (!reactionAllowed[r]) continue;
                    LiquidReaction rule = reactions[r];
                    int[] ri = reactantIndex[r];
                    double[] rc = reactantCoefficient[r];

                    double maxExtent = double.MaxValue;
                    bool present = true;
                    for (int t = 0; t < ri.Length; t++)
                    {
                        double m = liquidMass[b + ri[t]];
                        if (!(m > 0.0)) { present = false; break; }
                        double e = m / rc[t];
                        if (e < maxExtent) maxExtent = e;
                    }
                    if (!present) continue;

                    double temp = temperature[c];
                    if (temp < rule.minTemperature || temp > rule.maxTemperature) continue;
                    if (rule.HasPHCondition)
                    {
                        double ph = CellPH(c);
                        if (double.IsNaN(ph) || ph < rule.minPH || ph > rule.maxPH) continue;
                    }

                    double extent;
                    if (rule.instantaneous)
                    {
                        extent = maxExtent;
                    }
                    else
                    {
                        // Mass action in the reacting phase: dξ/dt = k(T)·V·Π(nᵢ/V), nᵢ = mᵢ/νᵢ [mol].
                        int g = reactionGroup[r];
                        double v = 0.0;
                        for (int k = 0; k < liquidCount; k++)
                            if (g < 0 || groupOf[k] == g) v += liquidMass[b + k] / componentDensity[b + k];
                        if (!(v > 0.0)) continue;
                        double rate = rule.RateConstant(temp) * v;
                        for (int t = 0; t < ri.Length; t++) rate *= liquidMass[b + ri[t]] / rc[t] / v;
                        extent = rate * h;
                        if (extent > maxExtent) extent = maxExtent;
                    }
                    if (!(extent > 0.0)) continue;

                    double cap = heatCapacity[c];
                    for (int t = 0; t < ri.Length; t++)
                    {
                        int k = ri[t];
                        double dm = rc[t] * extent;
                        double m = liquidMass[b + k];
                        if (dm > m) dm = m;
                        liquidMass[b + k] = m - dm;
                        cap -= dm * cpLiquid[k];
                    }
                    int[] pi = productIndex[r];
                    double[] pc = productCoefficient[r];
                    for (int t = 0; t < pi.Length; t++)
                    {
                        int k = pi[t];
                        double dm = pc[t] * extent;
                        liquidMass[b + k] += dm;
                        cap += dm * cpLiquid[k];
                    }
                    reactionGasMass += rule.gasProductKgPerMole * extent;
                    if (cap < 0.0) cap = 0.0;
                    // Composition changes at constant T, then the reaction heat −ΔH·ξ warms (or cools) the cell.
                    if (cap > 0.0) temperature[c] = temp - rule.enthalpy * extent / cap;
                    heatCapacity[c] = cap;
                    reactionExtent[r] += extent;
                }
            }
        }

        /// <summary>pH from the strong-acid/base equivalents of the aqueous liquids in a cell (NaN if none).</summary>
        double CellPH(int c)
        {
            int b = c * liquidCount;
            double eq = 0.0, v = 0.0;
            for (int k = 0; k < liquidCount; k++)
            {
                double m = liquidMass[b + k];
                if (!(m > 0.0) || !defs[k].supportsPH) continue;
                double vk = m / componentDensity[b + k];
                eq += vk * defs[k].acidityEquivalents;
                v += vk;
            }
            if (!(v > 0.0)) return double.NaN;
            return LiquidChemistry.PHFromNetAcidity(eq / v / 1000.0, temperature[c]);
        }

        // ───────────────────────────── corrosion ─────────────────────────────

        /// <summary>
        /// Wall cells lose thickness at the volume-weighted corrosion rate of the liquids touching them (Arrhenius in T),
        /// times the wetted fraction (the floor is wetted by any liquid; side walls by the fill height), divided by the
        /// material's resistance. A wall that reaches zero thickness opens a breach hole that drains the cell.
        /// </summary>
        void CorrosionPass(double h)
        {
            for (int c = 0; c < cellCount; c++)
            {
                if (!isWallCell[c] || wallBreached[c]) continue;
                int b = c * liquidCount;
                double v = 0.0, rate = 0.0;
                double temp = temperature[c];
                for (int k = 0; k < liquidCount; k++)
                {
                    double m = liquidMass[b + k];
                    if (!(m > 0.0) || !(defs[k].corrosionRate > 0.0)) { if (m > 0.0) v += m / componentDensity[b + k]; continue; }
                    double vk = m / componentDensity[b + k];
                    v += vk;
                    rate += vk * defs[k].CorrosionRateAt(temp);
                }
                if (!(v > 0.0) || !(rate > 0.0)) continue;
                double wet = isBottomWall[c] ? 1.0 : Math.Min(1.0, (v + solidVolume[c]) / cellVolume);
                double loss = rate / v * wet / settings.wallCorrosionResistance * h;
                double left = wallThicknessLeft[c] - loss;
                if (left > 0.0)
                {
                    wallThicknessLeft[c] = left;
                    continue;
                }
                wallThicknessLeft[c] = 0.0;
                wallBreached[c] = true;
                breachCount++;
                AddDrainInternal(c, settings.breachHoleDiameter, settings.wallThickness);
            }
        }
    }
}
