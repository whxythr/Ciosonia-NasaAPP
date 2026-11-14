using System;

namespace SpaceSim.Liquids
{
    public sealed partial class LiquidSimulation
    {
        // ───────────────────────────── local pressure model ─────────────────────────────
        //
        // Each cell's pressure (relative to the free surface) is local, so faces can be processed sequentially
        // (Gauss–Seidel) without a global solve:
        //   partially filled (f < 1):  p = ½·ρ·g_y·h            (mean hydrostatic pressure of its own liquid layer, h = V/A)
        //   full (f ≥ 1):              p = ½·ρ·g_y·Δy + K·(f − 1)  (artificial compressibility K = stiffness)
        // In a resting column K·(f − 1) grows with depth exactly as the weight above, which is the hydrostatic state.
        // The driving pressure across a face i → j adds the gravity work ρ·(g · d_ij).

        double LocalPressure(int c)
        {
            double vl = liquidVolume[c];
            double head = vl / floorArea;
            if (head > dy) head = dy;
            double p = 0.5 * density[c] * gyAbs * head;
            double f = (vl + solidVolume[c]) / cellVolume;
            if (f > 1.0) p += stiffness * (f - 1.0);
            return p;
        }

        /// <summary>dp/dV of a cell: the hydrostatic head slope while partially filled, the stiffness when full.</summary>
        double PressureSlope(int c)
        {
            double f = (liquidVolume[c] + solidVolume[c]) / cellVolume;
            if (f >= 1.0) return stiffness / cellVolume;
            return 0.5 * density[c] * gyAbs / floorArea + 1e-9;
        }

        /// <summary>
        /// Volumetric flow through a face for pressure difference dp: viscous slot resistance b = μ·L/(k·A) (Poiseuille,
        /// k = w²/12) in series with the inertial (Bernoulli) resistance a = ρ/(2·C_d²·A²).
        /// </summary>
        double FaceFlowRate(double dp, double mu, double rho, double area, double length)
        {
            if (!(area > 0.0) || !(rho > 0.0)) return 0.0;
            double b = mu * length / (permeability * area);
            double cd = settings.dischargeCoefficient;
            double a = rho / (2.0 * cd * cd * area * area);
            return LiquidPhysics.CombinedFlowRate(dp, b, a);
        }

        // ───────────────────────────── bulk flow ─────────────────────────────

        void FlowPass(double h)
        {
            bool reverse = (sweep & 1) == 1;

            // Faces between rows, processed from the gravity-bottom row upwards so a falling parcel moves at most one
            // cell per face visit.
            for (int r = 0; r < ny - 1; r++)
            {
                int yLow = gy <= 0.0 ? r : ny - 1 - r;
                int yHigh = gy <= 0.0 ? r + 1 : ny - 2 - r;
                double gd = gy * (yLow - yHigh) * dy; // g · (displacement upper → lower)
                for (int i = 0; i < nx; i++)
                {
                    int x = reverse ? nx - 1 - i : i;
                    FlowFace(x + yHigh * nx, x + yLow * nx, gd, floorArea, dy, false, h);
                }
            }

            // Faces between columns; sweep direction alternates every substep to avoid a directional bias.
            double gdx = gx * dx;
            for (int y = 0; y < ny; y++)
                for (int i = 0; i < nx - 1; i++)
                {
                    int x = reverse ? nx - 2 - i : i;
                    int a = x + y * nx;
                    FlowFace(a, a + 1, gdx, sideArea, dx, true, h);
                }

            if (settings.openTop) SpillPass(h);
        }

        void FlowFace(int i, int j, double gd, double faceArea, double length, bool lateral, double h)
        {
            double vi = liquidVolume[i], vj = liquidVolume[j];
            if (vi <= VolumeEpsilon && vj <= VolumeEpsilon) return;
            double rhoFace = (cellMass[i] + cellMass[j]) / (vi + vj);
            double dp = LocalPressure(i) - LocalPressure(j) + rhoFace * gd;
            int donor = i, receiver = j;
            double gdDir = gd;
            if (dp < 0.0) { donor = j; receiver = i; dp = -dp; gdDir = -gd; }
            double vd = liquidVolume[donor];
            if (vd <= VolumeEpsilon || !(dp > 0.0)) return;

            double area = faceArea * settings.openAreaFraction;
            if (lateral)
            {
                double f = (vd + solidVolume[donor]) / cellVolume;
                if (f < 1.0) area *= f; // only the wetted height of the donor's side face passes liquid
            }
            double dv = FaceFlowRate(dp, viscosity[donor] * resistance[donor], density[donor], area, length) * h;
            if (dv > vd) dv = vd;
            double equalise = Relaxation * dp / (PressureSlope(donor) + PressureSlope(receiver));
            if (dv > equalise) dv = equalise;
            double filled = liquidVolume[receiver] + solidVolume[receiver];
            if (filled < cellVolume)
            {
                double free = cellVolume - filled;
                if (dv > free) dv = free; // fill the gap first; compression starts once the cell is full
            }
            if (!(dv > 0.0)) return;
            int mode = gdDir > 0.0 ? ModeDensestFirst : (gdDir < 0.0 ? ModeLightestFirst : ModeProportional);
            Transfer(donor, receiver, dv, mode);
        }

        /// <summary>Over-full top-row cells push liquid out over the rim of an open container.</summary>
        void SpillPass(double h)
        {
            int y = ny - 1;
            double gd = gy * dy; // moving out through the top: displacement +Y
            for (int x = 0; x < nx; x++)
            {
                int c = x + y * nx;
                double vl = liquidVolume[c];
                if (vl <= VolumeEpsilon) continue;
                double dp = LocalPressure(c) + density[c] * gd;
                if (!(dp > 0.0)) continue;
                double dv = FaceFlowRate(dp, viscosity[c] * resistance[c], density[c], floorArea * settings.openAreaFraction, dy) * h;
                if (dv > vl) dv = vl;
                double equalise = Relaxation * dp / PressureSlope(c);
                if (dv > equalise) dv = equalise;
                if (!(dv > 0.0)) continue;
                Transfer(c, -1, dv, gd > 0.0 ? ModeDensestFirst : (gd < 0.0 ? ModeLightestFirst : ModeProportional));
                for (int k = 0; k < liquidCount; k++) spilledMass[k] += moveA[k];
            }
        }

        // ───────────────────────────── mass transfer primitives ─────────────────────────────

        /// <summary>Sums liquid volume and mass per phase group of a cell into the given arrays.</summary>
        void GroupVolumes(int c, double[] vol, double[] mass)
        {
            for (int g = 0; g < groupCount; g++) { vol[g] = 0.0; mass[g] = 0.0; }
            int b = c * liquidCount;
            for (int k = 0; k < liquidCount; k++)
            {
                double m = liquidMass[b + k];
                if (m <= 0.0) continue;
                int g = groupOf[k];
                vol[g] += m / componentDensity[b + k];
                mass[g] += m;
            }
        }

        /// <summary>Writes the non-empty groups into <see cref="groupOrder"/> sorted by density; returns their count.</summary>
        int SortGroups(double[] vol, double[] mass, bool densestFirst)
        {
            int n = 0;
            for (int g = 0; g < groupCount; g++)
            {
                if (!(vol[g] > 0.0)) continue;
                double rho = mass[g] / vol[g];
                int i = n++;
                while (i > 0)
                {
                    int prev = groupOrder[i - 1];
                    double rp = mass[prev] / vol[prev];
                    if (densestFirst ? rp >= rho : rp <= rho) break;
                    groupOrder[i] = prev;
                    i--;
                }
                groupOrder[i] = g;
            }
            return n;
        }

        /// <summary>
        /// Moves a liquid volume from one cell to another (to &lt; 0 = out of the container). The parcel is taken
        /// proportionally, or phase by phase starting with the densest (moving down) or lightest (moving up).
        /// The moved masses are left in <see cref="moveA"/>.
        /// </summary>
        void Transfer(int from, int to, double dv, int mode)
        {
            for (int k = 0; k < liquidCount; k++) moveA[k] = 0.0;
            double vFrom = liquidVolume[from];
            if (!(dv > 0.0) || !(vFrom > 0.0)) return;
            int b = from * liquidCount;
            if (dv >= vFrom)
            {
                for (int k = 0; k < liquidCount; k++) moveA[k] = liquidMass[b + k];
            }
            else if (mode == ModeProportional || groupCount == 1)
            {
                double frac = dv / vFrom;
                for (int k = 0; k < liquidCount; k++) moveA[k] = liquidMass[b + k] * frac;
            }
            else
            {
                GroupVolumes(from, groupVolA, groupMassA);
                int count = SortGroups(groupVolA, groupMassA, mode == ModeDensestFirst);
                double remaining = dv;
                for (int o = 0; o < count && remaining > 0.0; o++)
                {
                    int g = groupOrder[o];
                    double gv = groupVolA[g];
                    double take = remaining < gv ? remaining : gv;
                    double frac = take / gv;
                    int[] members = groupMembers[g];
                    for (int t = 0; t < members.Length; t++)
                    {
                        int k = members[t];
                        moveA[k] = liquidMass[b + k] * frac;
                    }
                    remaining -= take;
                }
            }
            MoveMass(from, to, moveA);
        }

        /// <summary>Moves the given per-liquid masses from one cell to another (or out), carrying their enthalpy.</summary>
        void MoveMass(int from, int to, double[] dm)
        {
            int bf = from * liquidCount, bt = to * liquidCount;
            double tFrom = temperature[from];
            double energy = 0.0, cap = 0.0;
            double vFrom = liquidVolume[from], mFrom = cellMass[from], cFrom = heatCapacity[from];
            for (int k = 0; k < liquidCount; k++)
            {
                double m = dm[k];
                if (!(m > 0.0)) continue;
                double have = liquidMass[bf + k];
                if (m > have) { m = have; dm[k] = m; }
                liquidMass[bf + k] = have - m;
                vFrom -= m / componentDensity[bf + k];
                mFrom -= m;
                double cm = m * cpLiquid[k];
                cFrom -= cm;
                if (to >= 0)
                {
                    liquidMass[bt + k] += m;
                    liquidVolume[to] += m / componentDensity[bt + k];
                    cellMass[to] += m;
                    energy += cm * tFrom;
                    cap += cm;
                }
            }
            SetCellTotals(from, vFrom, mFrom, cFrom);
            if (to >= 0)
            {
                // A receiver that was dry may carry a stale temperature: c0 = 0 then gives the parcel's temperature.
                if (cap > 0.0)
                {
                    double c0 = heatCapacity[to];
                    double c1 = c0 + cap;
                    temperature[to] = (c0 * temperature[to] + energy) / c1;
                    heatCapacity[to] = c1;
                }
                density[to] = liquidVolume[to] > 0.0 ? cellMass[to] / liquidVolume[to] : 0.0;
            }
        }

        void SetCellTotals(int c, double volume, double mass, double capacity)
        {
            if (!(mass > 0.0)) { mass = 0.0; volume = 0.0; }
            liquidVolume[c] = volume > 0.0 ? volume : 0.0;
            cellMass[c] = mass;
            heatCapacity[c] = capacity > 0.0 ? capacity : 0.0;
            density[c] = liquidVolume[c] > 0.0 && cellMass[c] > 0.0 ? cellMass[c] / liquidVolume[c] : 0.0;
        }

        /// <summary>
        /// Simultaneous exchange: <see cref="moveA"/> goes a → b and <see cref="moveB"/> goes b → a. Each parcel keeps
        /// its source temperature (needed for convection), and the enthalpy balance is exact.
        /// </summary>
        void Exchange(int a, int b)
        {
            int ba = a * liquidCount, bb = b * liquidCount;
            double ta = temperature[a], tb = temperature[b];
            double capAB = 0.0, capBA = 0.0;
            double va = liquidVolume[a], vb = liquidVolume[b], ma = cellMass[a], mb = cellMass[b];
            for (int k = 0; k < liquidCount; k++)
            {
                double m = moveA[k];
                if (m > 0.0)
                {
                    double have = liquidMass[ba + k];
                    if (m > have) m = have;
                    liquidMass[ba + k] = have - m;
                    liquidMass[bb + k] += m;
                    va -= m / componentDensity[ba + k]; ma -= m;
                    vb += m / componentDensity[bb + k]; mb += m;
                    capAB += m * cpLiquid[k];
                }
                m = moveB[k];
                if (m > 0.0)
                {
                    double have = liquidMass[bb + k];
                    if (m > have) m = have;
                    liquidMass[bb + k] = have - m;
                    liquidMass[ba + k] += m;
                    vb -= m / componentDensity[bb + k]; mb -= m;
                    va += m / componentDensity[ba + k]; ma += m;
                    capBA += m * cpLiquid[k];
                }
            }
            double ca = heatCapacity[a], cb = heatCapacity[b];
            double keepA = Math.Max(0.0, ca - capAB), keepB = Math.Max(0.0, cb - capBA);
            double newA = keepA + capBA, newB = keepB + capAB;
            if (newA > 0.0) temperature[a] = (keepA * ta + capBA * tb) / newA;
            if (newB > 0.0) temperature[b] = (keepB * tb + capAB * ta) / newB;
            SetCellTotals(a, va, ma, newA);
            SetCellTotals(b, vb, mb, newB);
        }

        // ───────────────────────────── stratification (buoyancy) ─────────────────────────────

        void StratificationPass(double h)
        {
            if (gyAbs > 0.0)
            {
                for (int r = 0; r < ny - 1; r++)
                {
                    int yLow = gy <= 0.0 ? r : ny - 1 - r;
                    int yHigh = gy <= 0.0 ? r + 1 : ny - 2 - r;
                    for (int x = 0; x < nx; x++)
                        StratifyFace(x + yHigh * nx, x + yLow * nx, gyAbs, floorArea, dy, h);
                }
            }
            if (gx != 0.0)
            {
                double gAbs = Math.Abs(gx);
                for (int y = 0; y < ny; y++)
                    for (int x = 0; x < nx - 1; x++)
                    {
                        int a = x + y * nx, b = a + 1;
                        double area = sideArea * Math.Min(WetFraction(a), WetFraction(b));
                        if (gx > 0.0) StratifyFace(a, b, gAbs, area, dx, h); // +X is "down"
                        else StratifyFace(b, a, gAbs, area, dx, h);
                    }
            }
        }

        double WetFraction(int c)
        {
            double f = (liquidVolume[c] + solidVolume[c]) / cellVolume;
            return f < 1.0 ? f : 1.0;
        }

        /// <summary>
        /// If the upper cell holds a phase denser than the lightest phase of the lower cell, equal volumes are swapped
        /// (heavy down, light up). Driving pressure Δρ·g·Δy; resistance as a face with half the area for each stream and
        /// the larger viscosity. Same-phase pairs (thermal or compositional convection) swap at most half to never overshoot.
        /// </summary>
        void StratifyFace(int upper, int lower, double gAlong, double area, double length, double h)
        {
            if (liquidVolume[upper] <= VolumeEpsilon || liquidVolume[lower] <= VolumeEpsilon || !(area > 0.0)) return;
            GroupVolumes(upper, groupVolA, groupMassA);
            GroupVolumes(lower, groupVolB, groupMassB);
            int heavy = -1, light = -1;
            double rhoHeavy = 0.0, rhoLight = double.MaxValue;
            for (int g = 0; g < groupCount; g++)
            {
                if (groupVolA[g] > 0.0)
                {
                    double r = groupMassA[g] / groupVolA[g];
                    if (r > rhoHeavy) { rhoHeavy = r; heavy = g; }
                }
                if (groupVolB[g] > 0.0)
                {
                    double r = groupMassB[g] / groupVolB[g];
                    if (r < rhoLight) { rhoLight = r; light = g; }
                }
            }
            if (heavy < 0 || light < 0) return;
            double drho = rhoHeavy - rhoLight;
            if (!(drho > 1e-9 * rhoHeavy)) return;

            double dp = drho * gAlong * length;
            double mu = Math.Max(viscosity[upper] * resistance[upper], viscosity[lower] * resistance[lower]);
            double dv = FaceFlowRate(dp, mu, 0.5 * (rhoHeavy + rhoLight), 0.5 * area * settings.openAreaFraction, length) * h;
            double limit = Math.Min(groupVolA[heavy], groupVolB[light]);
            if (heavy == light) limit *= 0.5;
            if (dv > limit) dv = limit;
            if (!(dv > 0.0)) return;

            double fh = dv / groupVolA[heavy], fl = dv / groupVolB[light];
            int bu = upper * liquidCount, bl = lower * liquidCount;
            for (int k = 0; k < liquidCount; k++) { moveA[k] = 0.0; moveB[k] = 0.0; }
            int[] hm = groupMembers[heavy];
            for (int t = 0; t < hm.Length; t++) moveA[hm[t]] = liquidMass[bu + hm[t]] * fh;
            int[] lm = groupMembers[light];
            for (int t = 0; t < lm.Length; t++) moveB[lm[t]] = liquidMass[bl + lm[t]] * fl;
            Exchange(upper, lower);
        }

        // ───────────────────────────── miscible diffusion ─────────────────────────────

        void DiffusionPass(double h)
        {
            for (int y = 0; y < ny - 1; y++)
                for (int x = 0; x < nx; x++)
                {
                    int a = x + y * nx;
                    DiffuseFace(a, a + nx, floorArea, dy, h);
                }
            for (int y = 0; y < ny; y++)
                for (int x = 0; x < nx - 1; x++)
                {
                    int a = x + y * nx;
                    DiffuseFace(a, a + 1, sideArea * Math.Min(WetFraction(a), WetFraction(a + 1)), dx, h);
                }
        }

        /// <summary>
        /// Fickian exchange of the members of each miscible group between two cells: the volume fraction difference of
        /// every component relaxes with rate D·A/(Δx·V); counter-flows are volume-neutral. θ ≤ ½ guarantees no overshoot.
        /// D = eddy diffusivity + Stokes–Einstein molecular diffusivity.
        /// </summary>
        void DiffuseFace(int a, int b, double area, double length, double h)
        {
            if (liquidVolume[a] <= VolumeEpsilon || liquidVolume[b] <= VolumeEpsilon || !(area > 0.0)) return;
            double d = settings.eddyDiffusivity + 0.5 * (diffusivity[a] + diffusivity[b]);
            if (!(d > 0.0)) return;
            GroupVolumes(a, groupVolA, groupMassA);
            GroupVolumes(b, groupVolB, groupMassB);
            int ba = a * liquidCount, bb = b * liquidCount;
            bool any = false;
            for (int k = 0; k < liquidCount; k++) { moveA[k] = 0.0; moveB[k] = 0.0; }
            for (int g = 0; g < groupCount; g++)
            {
                int[] members = groupMembers[g];
                if (members.Length < 2) continue;
                double va = groupVolA[g], vb = groupVolB[g];
                if (!(va > 0.0) || !(vb > 0.0)) continue;
                double vmin = va < vb ? va : vb;
                double theta = d * area * h / (length * vmin);
                if (theta > 0.5) theta = 0.5;
                for (int t = 0; t < members.Length; t++)
                {
                    int k = members[t];
                    double phiA = liquidMass[ba + k] / componentDensity[ba + k] / va;
                    double phiB = liquidMass[bb + k] / componentDensity[bb + k] / vb;
                    double dvk = theta * vmin * (phiA - phiB);
                    if (dvk > 0.0) { moveA[k] = dvk * componentDensity[ba + k]; any = true; }
                    else if (dvk < 0.0) { moveB[k] = -dvk * componentDensity[bb + k]; any = true; }
                }
            }
            if (any) Exchange(a, b);
        }

        // ───────────────────────────── drains and breaches ─────────────────────────────

        void DrainPass(double h)
        {
            double pAmb = environment.AmbientPressure;
            for (int d = 0; d < drainCount; d++)
            {
                drainRate[d] = 0.0;
                int c = drainCell[d];
                double vl = liquidVolume[c];
                if (vl <= VolumeEpsilon) continue;
                double head = vl / floorArea;
                if (head > dy) head = dy;
                // Gauge pressure at the bottom of the cell: weight of the liquid above plus this cell's own layer.
                double dp = pressure[c] - pAmb + 0.5 * density[c] * gyAbs * head;
                if (!(dp > 0.0)) continue;
                double q = LiquidPhysics.PipeFlowRate(dp, drainDiameter[d], drainLength[d], viscosity[c] * resistance[c], density[c]);
                double dv = q * h;
                if (dv > vl) dv = vl;
                if (!(dv > 0.0)) continue;
                Transfer(c, -1, dv, ModeDensestFirst);
                int bd = d * liquidCount;
                for (int k = 0; k < liquidCount; k++)
                {
                    drainedMass[k] += moveA[k];
                    drainMass[bd + k] += moveA[k];
                }
                drainRate[d] = dv / h;
            }
        }
    }
}
