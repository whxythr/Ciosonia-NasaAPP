using System;
using SpaceSim.Units;

namespace SpaceSim.Liquids
{
    public sealed partial class LiquidSimulation
    {
        // ───────────────────────────── heat transfer ─────────────────────────────

        void HeatPass(double h)
        {
            // Conduction between neighbours: Q = k_eff·A/Δx·ΔT, k_eff = harmonic mean (series resistances).
            for (int y = 0; y < ny - 1; y++)
                for (int x = 0; x < nx; x++)
                {
                    int a = x + y * nx;
                    ConductFace(a, a + nx, floorArea, dy, h);
                }
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx - 1; x++)
                {
                    int a = x + y * nx;
                    ConductFace(a, a + 1, sideArea * Math.Min(WetFraction(a), WetFraction(a + 1)), dx, h);
                }

            // Exchange with the surroundings. Natural convection scales as h ∝ ρ_gas^(2/3) (Gr^(1/3) law).
            double rhoGas = environment.AmbientGasDensity;
            double hConv = rhoGas > 0.0 ? settings.surfaceHeatTransferCoefficient * Math.Pow(rhoGas / 1.2, 2.0 / 3.0) : 0.0;
            double tAmb = environment.AmbientTemperature;
            double tAmb4 = tAmb * tAmb * tAmb * tAmb;
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx; x++)
                    AmbientExchange(x, y, hConv, tAmb, tAmb4, h);
        }

        void ConductFace(int a, int b, double area, double length, double h)
        {
            double ca = heatCapacity[a], cb = heatCapacity[b];
            if (!(ca > 0.0) || !(cb > 0.0) || !(area > 0.0)) return;
            double ka = conductivity[a], kb = conductivity[b];
            if (!(ka > 0.0) || !(kb > 0.0)) return;
            double k = 2.0 * ka * kb / (ka + kb);
            double dT = temperature[a] - temperature[b];
            double q = k * area / length * dT * h;
            double qMax = ca * cb / (ca + cb) * Math.Abs(dT); // exactly equalises: never overshoots
            if (q > qMax) q = qMax; else if (q < -qMax) q = -qMax;
            temperature[a] -= q / ca;
            temperature[b] += q / cb;
        }

        void AmbientExchange(int x, int y, double hConv, double tAmb, double tAmb4, double h)
        {
            int c = x + y * nx;
            double cap = heatCapacity[c];
            if (!(cap > 0.0)) return;
            double f = WetFraction(c);
            // Walls: front and back faces of the slice, the side walls, the floor; a closed lid only when touched.
            double wallArea = 2.0 * dx * dy * f;
            if (x == 0) wallArea += sideArea * f;
            if (x == nx - 1) wallArea += sideArea * f;
            if (y == 0) wallArea += floorArea;
            double surfaceArea = 0.0;
            if (y == ny - 1)
            {
                if (settings.openTop) surfaceArea = floorArea;
                else if (f >= 1.0) wallArea += floorArea;
            }
            else
            {
                int up = c + nx;
                if (liquidVolume[up] + solidVolume[up] <= VolumeEpsilon) surfaceArea = floorArea; // free surface
            }

            double t = temperature[c];
            double power = (settings.wallHeatTransferCoefficient * wallArea + hConv * surfaceArea) * (tAmb - t);
            if (settings.radiativeExchange && surfaceArea > 0.0)
                power += surfaceEmissivity[c] * PhysicalConstants.StefanBoltzmann * surfaceArea * (tAmb4 - t * t * t * t);
            double dE = power * h;
            double maxE = cap * Math.Abs(tAmb - t); // relax at most to the ambient temperature
            if (dE > maxE) dE = maxE; else if (dE < -maxE) dE = -maxE;
            temperature[c] = t + dE / cap;
        }

        // ───────────────────────────── phase changes ─────────────────────────────

        /// <summary>
        /// Converts up to <paramref name="available"/> kg across a transition at temperature tx using the sensible energy
        /// beyond tx: E = C·(T − tx)·sign ≥ 0, Δm = min(available, E/L). The remaining energy sets the new temperature
        /// with the updated heat capacity (enthalpy referenced at the transition temperature, so energy is conserved).
        /// sign = +1 for transitions absorbing heat (T above tx), −1 for transitions releasing heat (T below tx).
        /// </summary>
        static double Convert(double available, double tx, double latent, double capacityChangePerKg, int sign, ref double t, ref double cap)
        {
            double e = cap * (t - tx) * sign;
            if (!(e > 0.0) || !(available > 0.0)) return 0.0;
            double dm = e / latent;
            if (dm > available) dm = available;
            double rest = e - dm * latent;
            double cap2 = cap + dm * capacityChangePerKg;
            if (rest < 0.0) rest = 0.0;
            t = cap2 > 1e-12 ? tx + sign * rest / cap2 : tx;
            cap = cap2 > 0.0 ? cap2 : 0.0;
            return dm;
        }

        void PhasePass(double h)
        {
            bool phases = settings.enablePhaseChanges, decomposition = settings.enableDecomposition;
            for (int c = 0; c < cellCount; c++)
            {
                double cap = heatCapacity[c];
                if (!(cap > 0.0)) continue;
                double t = temperature[c];
                double p = pressure[c];
                int b = c * liquidCount;
                for (int k = 0; k < liquidCount; k++)
                {
                    double m = liquidMass[b + k], s = solidMass[b + k];
                    if (!(m > 0.0) && !(s > 0.0)) continue;
                    LiquidDefinition d = defs[k];
                    if (phases) PhaseChange(k, d, p, ref m, ref s, ref t, ref cap);
                    if (decomposition && m > 0.0 && d.decompositionTemperature > 0.0 && t > d.decompositionTemperature && d.decompositionRate > 0.0)
                    {
                        double dm = m * (1.0 - Math.Exp(-d.decompositionRate * h));
                        double dH = d.decompositionEnthalpy;
                        if (dH > 0.0)
                        {
                            // Endothermic and self-limiting: the products leave at T carrying their own heat capacity,
                            // so the remaining (cap − dm·c_p) absorbs dm·ΔH and must not drop below T_dec:
                            // dm ≤ cap·(T − T_dec) / (ΔH + c_p·(T − T_dec)).
                            double over = t - d.decompositionTemperature;
                            double limit = cap * over / (dH + cpLiquid[k] * over);
                            if (dm > limit) dm = limit;
                        }
                        if (dm > 0.0)
                        {
                            m -= dm;
                            decomposedMass[k] += dm;
                            double cap2 = cap - dm * cpLiquid[k];
                            if (cap2 > 1e-12) t -= dm * dH / cap2;
                            cap = cap2 > 0.0 ? cap2 : 0.0;
                        }
                    }
                    liquidMass[b + k] = m > 0.0 ? m : 0.0;
                    solidMass[b + k] = s > 0.0 ? s : 0.0;
                }
                temperature[c] = t;
                heatCapacity[c] = cap;
            }
        }

        /// <summary>
        /// Melting/freezing at T_m, boiling at T_b(p) (local absolute pressure, so the boiling point rises with ambient
        /// pressure and depth; capped at the critical temperature, so no liquid survives above T_c), and — below the
        /// triple-point pressure where no stable liquid exists — flash evaporation/freezing and sublimation at T_s(p).
        /// The latent heat of vaporisation follows the Watson correlation at the transition temperature.
        /// Vapour leaves the container (counted as vented).
        /// </summary>
        void PhaseChange(int k, LiquidDefinition d, double p, ref double m, ref double s, ref double t, ref double cap)
        {
            double tm = d.meltingPoint;
            double tb = d.BoilingPoint(p);
            double lf = d.enthalpyOfFusion;
            double cl = cpLiquid[k], cs = cpSolid[k];
            bool liquidPossible = tb > tm && p >= d.TriplePointPressure;
            if (liquidPossible)
            {
                if (s > 0.0 && t > tm) { double dm = Convert(s, tm, lf, cl - cs, +1, ref t, ref cap); s -= dm; m += dm; }
                if (m > 0.0 && t < tm) { double dm = Convert(m, tm, lf, cs - cl, -1, ref t, ref cap); m -= dm; s += dm; }
                if (m > 0.0 && t > tb)
                {
                    double lvb = Math.Max(1.0, d.EnthalpyOfVaporizationAt(tb));
                    double dm = Convert(m, tb, lvb, -cl, +1, ref t, ref cap);
                    m -= dm;
                    ventedMass[k] += dm;
                }
                return;
            }
            double lv = Math.Max(1.0, d.EnthalpyOfVaporizationAt(tm));

            // Below the triple point: liquid cools to T_m by evaporating, then the rest splits at T_m so that the heat
            // released by freezing exactly evaporates the remainder (water: L_f/(L_f + L_v) ≈ 13 % vaporises).
            if (m > 0.0 && t > tm) { double dm = Convert(m, tm, lv, -cl, +1, ref t, ref cap); m -= dm; ventedMass[k] += dm; }
            if (m > 0.0 && t < tm) { double dm = Convert(m, tm, lf, cs - cl, -1, ref t, ref cap); m -= dm; s += dm; }
            if (m > 0.0)
            {
                double mv = m * lf / (lf + lv);
                double mf = m - mv;
                ventedMass[k] += mv;
                s += mf;
                cap += -m * cl + mf * cs;
                if (cap < 0.0) cap = 0.0;
                m = 0.0;
            }
            double ts = d.SublimationPoint(p);
            if (s > 0.0 && t > ts) { double dm = Convert(s, ts, lv + lf, -cs, +1, ref t, ref cap); s -= dm; ventedMass[k] += dm; }
        }
    }
}
