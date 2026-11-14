using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Vehicles;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>How one engine type looks: flame colour and shape, what it leaves in the air. Pure presentation data.</summary>
    public struct ExhaustStyle
    {
        /// <summary>HDR linear flame colours.</summary>
        public Vector4 Core, Edge;
        public float Brightness, LengthPerRadius, SeaSpread, VacuumSpread, VacuumBrightness, Diamonds, Turbulence;
        /// <summary>Exit radius as a fraction of the nozzle's (electric thrusters are a thin beam).</summary>
        public float RadiusScale;
        /// <summary>Smoke albedo (linear). Soot: how dark the fresh smoke is, fading with age.</summary>
        public Color SmokeColor;
        public float Soot;
        /// <summary>Puffs per second over the pad, their opacity and base radius [m]; zero rate = no smoke at all.</summary>
        public float GroundRate, GroundOpacity, GroundSize;
        /// <summary>The trail in flight: rate cap [1/s], opacity, base radius [m], life [s].</summary>
        public float TrailRate, TrailOpacity, TrailSize, TrailLife;
        /// <summary>Air density ρ/ρ₀ below which no trail is left.</summary>
        public float MinDensityRatio;
        public Color GlowColor;
        public float GlowPower;

        public bool HasSmoke => GroundRate > 0f || TrailRate > 0f;

        public static ExhaustStyle For(ModuleDefinition engine)
        {
            if (engine != null && engine.engineKind == EngineKind.Solid) return SolidBooster;
            if (engine != null && engine.engineKind == EngineKind.Nuclear) return NuclearThermal;
            if (engine != null && engine.engineKind == EngineKind.Electric) return ElectricThruster;
            switch (engine != null ? engine.propellant : PropellantType.Kerolox)
            {
                case PropellantType.Hydrolox: return Hydrogen;
                case PropellantType.Hypergolic: return Hypergol;
                default: return Kerosene;
            }
        }

        /// <summary>Kerosene: orange, sooty, with bright shock diamonds.</summary>
        static readonly ExhaustStyle Kerosene = new ExhaustStyle
        {
            Core = new Vector4(2.3f, 1.65f, 0.92f, 1f), Edge = new Vector4(1.25f, 0.46f, 0.11f, 1f),
            Brightness = 1f, LengthPerRadius = 42f, SeaSpread = 0.6f, VacuumSpread = 11f, VacuumBrightness = 0.25f,
            Diamonds = 1f, Turbulence = 1f, RadiusScale = 1f,
            SmokeColor = new Color(0.46f, 0.42f, 0.38f), Soot = 0.7f,
            GroundRate = 26f, GroundOpacity = 0.7f, GroundSize = 6f,
            TrailRate = 22f, TrailOpacity = 0.65f, TrailSize = 6f, TrailLife = 38f, MinDensityRatio = 2e-3f,
            GlowColor = new Color(1f, 0.55f, 0.22f), GlowPower = 30f
        };

        /// <summary>Hydrogen: almost invisible flame, white water vapour.</summary>
        static readonly ExhaustStyle Hydrogen = new ExhaustStyle
        {
            Core = new Vector4(0.42f, 0.50f, 0.95f, 1f), Edge = new Vector4(0.13f, 0.17f, 0.42f, 1f),
            Brightness = 0.7f, LengthPerRadius = 55f, SeaSpread = 0.5f, VacuumSpread = 7f, VacuumBrightness = 0.3f,
            Diamonds = 1f, Turbulence = 0.45f, RadiusScale = 1f,
            SmokeColor = new Color(0.92f, 0.93f, 0.96f), Soot = 0f,
            GroundRate = 20f, GroundOpacity = 0.5f, GroundSize = 7f,
            TrailRate = 16f, TrailOpacity = 0.22f, TrailSize = 5f, TrailLife = 24f, MinDensityRatio = 6e-3f,
            GlowColor = new Color(0.6f, 0.7f, 1f), GlowPower = 10f
        };

        /// <summary>Storable hypergols: pale flame, thin reddish-brown fumes.</summary>
        static readonly ExhaustStyle Hypergol = new ExhaustStyle
        {
            Core = new Vector4(1.3f, 0.86f, 0.68f, 1f), Edge = new Vector4(0.55f, 0.23f, 0.14f, 1f),
            Brightness = 0.8f, LengthPerRadius = 34f, SeaSpread = 0.7f, VacuumSpread = 5f, VacuumBrightness = 0.35f,
            Diamonds = 0.6f, Turbulence = 0.8f, RadiusScale = 1f,
            SmokeColor = new Color(0.62f, 0.40f, 0.28f), Soot = 0.15f,
            GroundRate = 14f, GroundOpacity = 0.38f, GroundSize = 5f,
            TrailRate = 10f, TrailOpacity = 0.2f, TrailSize = 4f, TrailLife = 20f, MinDensityRatio = 6e-3f,
            GlowColor = new Color(1f, 0.6f, 0.4f), GlowPower = 14f
        };

        /// <summary>Solid booster: blinding flame, a dense white column of smoke that lingers.</summary>
        static readonly ExhaustStyle SolidBooster = new ExhaustStyle
        {
            Core = new Vector4(4.0f, 3.5f, 2.8f, 1f), Edge = new Vector4(1.9f, 1.1f, 0.38f, 1f),
            Brightness = 1.25f, LengthPerRadius = 40f, SeaSpread = 0.9f, VacuumSpread = 5f, VacuumBrightness = 0.55f,
            Diamonds = 0.35f, Turbulence = 1.2f, RadiusScale = 1f,
            SmokeColor = new Color(0.93f, 0.92f, 0.90f), Soot = 0f,
            GroundRate = 42f, GroundOpacity = 0.9f, GroundSize = 7.5f,
            TrailRate = 30f, TrailOpacity = 0.8f, TrailSize = 8f, TrailLife = 55f, MinDensityRatio = 1e-3f,
            GlowColor = new Color(1f, 0.75f, 0.45f), GlowPower = 45f
        };

        /// <summary>Nuclear thermal: faint blue-white hot hydrogen, no smoke.</summary>
        static readonly ExhaustStyle NuclearThermal = new ExhaustStyle
        {
            Core = new Vector4(0.33f, 0.39f, 0.46f, 1f), Edge = new Vector4(0.08f, 0.11f, 0.18f, 1f),
            Brightness = 0.6f, LengthPerRadius = 60f, SeaSpread = 0.35f, VacuumSpread = 4f, VacuumBrightness = 0.6f,
            Diamonds = 0.2f, Turbulence = 0.2f, RadiusScale = 1f,
            SmokeColor = Color.white, Soot = 0f,
            GlowColor = new Color(0.6f, 0.75f, 1f), GlowPower = 6f
        };

        /// <summary>Electric thruster: a thin blue beam, no smoke.</summary>
        static readonly ExhaustStyle ElectricThruster = new ExhaustStyle
        {
            Core = new Vector4(0.63f, 0.84f, 1.4f, 1f), Edge = new Vector4(0.09f, 0.14f, 0.35f, 1f),
            Brightness = 1f, LengthPerRadius = 90f, SeaSpread = 0.05f, VacuumSpread = 0.25f, VacuumBrightness = 1f,
            Diamonds = 0f, Turbulence = 0.08f, RadiusScale = 0.35f,
            SmokeColor = Color.white, Soot = 0f,
            GlowColor = new Color(0.5f, 0.7f, 1f), GlowPower = 3f
        };
    }

    /// <summary>
    /// The flame and the smoke of the active rocket: one plume per engine (merging into a single one in thin air) and soft smoke particles that
    /// spread over the pad at lift-off and hang as a trail in the air afterwards, thinning with altitude and gone in vacuum. Purely visual:
    /// it reads the flight state and never writes it. Particles live in the planet-fixed frame in double precision, so they stay where they
    /// were made, whatever the floating origin does.
    /// </summary>
    [DefaultExecutionOrder(126)]
    public sealed class RocketExhaustEffects : MonoBehaviour
    {
        /// <summary>What the rocket does this frame (simulation frame, render time).</summary>
        public struct Frame
        {
            public Vector3d Position, Axis;
            /// <summary>0 at sea level … 1 in vacuum.</summary>
            public double Vacuum;
            /// <summary>Nozzle exit along the rocket's axis, relative to the reference point [m].</summary>
            public float NozzleY;
        }

        struct Puff
        {
            public Vector3d Position, Velocity; // planet-fixed
            public float Age, Life, Size, Growth, Opacity, Seed, Rise, DragTime, Soot;
            public Color Albedo;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct GpuParticle
        {
            public Vector4 PositionSize, Misc, Color;
        }

        sealed class Plume
        {
            public Transform Transform;
            public Vector3 Offset;
            public float ExitRadius;
        }

        /// <summary>Upper bound of live puffs: the frame cost of smoke (CPU update, sort, overdraw) scales with it.</summary>
        const int MaxPuffs = 1400;
        const float PlumeMergeStart = 0.35f, PlumeMergeEnd = 0.85f;
        static readonly int ParticlesId = Shader.PropertyToID("_Particles");
        static readonly int GlowPositionId = Shader.PropertyToID("_GlowPosition");
        static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        static readonly int GlowRangeId = Shader.PropertyToID("_GlowRange");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");
        static readonly int ExitRadiusId = Shader.PropertyToID("_ExitRadius");
        static readonly int DiamondsId = Shader.PropertyToID("_Diamonds");
        static readonly int FalloffId = Shader.PropertyToID("_Falloff");
        static readonly int TurbulenceId = Shader.PropertyToID("_Turbulence");
        static readonly int RadiusWorldId = Shader.PropertyToID("_RadiusWorld");

        static Mesh quad, plumeProxy;

        FlightBody body;
        PlanetRuntime planet;
        SimulationController simulation;
        FloatingOrigin origin;
        AtmosphereRenderer atmosphere;
        Transform visual;
        RocketModel model;

        Material engineMaterial, mergedMaterial, smokeMaterial;
        readonly List<Plume> plumes = new List<Plume>();
        Transform mergedPlume;
        int plumeStage = -2;
        ExhaustStyle style;
        float mergedRadius;
        Light flameLight;

        readonly Puff[] puffs = new Puff[MaxPuffs];
        int count;
        readonly GpuParticle[] gpu = new GpuParticle[MaxPuffs];
        readonly Vector3[] positions = new Vector3[MaxPuffs];
        readonly float[] sortKeys = new float[MaxPuffs];
        readonly int[] sortOrder = new int[MaxPuffs];
        GraphicsBuffer buffer;
        uint seed = 12345u;
        double groundBudget, trailDistance;
        Vector3d lastNozzle;
        bool hasLastNozzle;
        Vector3 glowPosition;
        Color glowColor;
        float glowRange = 60f;
        bool smokeReady;

        public void Initialize(FlightBody flightBody, PlanetRuntime planetRuntime, SimulationController sim, FloatingOrigin floatingOrigin)
        {
            body = flightBody;
            planet = planetRuntime;
            simulation = sim;
            origin = floatingOrigin;
            atmosphere = planetRuntime.GetComponentInChildren<AtmosphereRenderer>();
            engineMaterial = ProceduralMeshes.EffectMaterial("SpaceSim/EnginePlume");
            mergedMaterial = ProceduralMeshes.EffectMaterial("SpaceSim/EnginePlume");
            smokeMaterial = ProceduralMeshes.EffectMaterial("SpaceSim/Smoke");
            if (plumeProxy == null) plumeProxy = ProceduralMeshes.Frustum(18, 1f, 1f, true, true, "PlumeProxy");
            if (quad == null)
            {
                quad = new Mesh
                {
                    name = "SmokeQuad",
                    vertices = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f) },
                    triangles = new[] { 0, 2, 1, 0, 3, 2 },
                    bounds = new Bounds(Vector3.zero, Vector3.one * 2e7f)
                };
            }
            smokeReady = smokeMaterial != null && SystemInfo.supportsInstancing;
            if (smokeReady)
            {
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxPuffs, Marshal.SizeOf(typeof(GpuParticle)));
                smokeMaterial.SetBuffer(ParticlesId, buffer);
            }
        }

        /// <summary>The rocket model was rebuilt: everything parented to it is gone.</summary>
        public void Attach(Transform visualRoot, RocketModel rocketModel)
        {
            visual = visualRoot;
            model = rocketModel;
            plumes.Clear();
            mergedPlume = null;
            plumeStage = -2;
            hasLastNozzle = false;
            flameLight = null;
            groundBudget = trailDistance = 0.0;
        }

        void OnDisable() => count = 0;

        void OnDestroy()
        {
            if (engineMaterial != null) Destroy(engineMaterial);
            if (mergedMaterial != null) Destroy(mergedMaterial);
            if (smokeMaterial != null) Destroy(smokeMaterial);
            buffer?.Dispose();
        }

        float Random01()
        {
            seed = seed * 1664525u + 1013904223u;
            return (seed >> 8) / 16777216f;
        }

        /// <summary>Everything except the draw call; FlightBody calls this from its LateUpdate.</summary>
        public void Tick(in Frame frame)
        {
            if (planet == null || planet.Model == null) return;
            RocketState rocket = body.Rocket;
            bool burning = rocket != null && rocket.IsBurning && !body.Destroyed;
            int stage = rocket != null && rocket.HasStages ? rocket.StageIndex : -1;
            if (stage != plumeStage) BuildPlumes(stage);

            float throttle = rocket != null ? (float)rocket.Throttle : 0f;
            UpdatePlumes(burning, throttle, frame);

            float dt = simulation.Paused ? 0f : Mathf.Min(Time.deltaTime * (float)Math.Max(1.0, simulation.TimeWarp), 0.25f);
            if (smokeReady && (burning || count > 0)) UpdateSmoke(burning, throttle, frame, dt);
        }

        // ---- Flame ------------------------------------------------------------------------------------------------------------

        void BuildPlumes(int stage)
        {
            foreach (Plume p in plumes)
                if (p.Transform != null) Destroy(p.Transform.gameObject);
            plumes.Clear();
            if (mergedPlume != null) Destroy(mergedPlume.gameObject);
            mergedPlume = null;
            plumeStage = stage;
            if (flameLight != null) Destroy(flameLight.gameObject);
            flameLight = null;
            if (stage < 0 || model == null || visual == null || engineMaterial == null || mergedMaterial == null) return;

            RocketBlueprint design = body.Blueprint;
            if (stage >= design.stages.Count) return;
            ModuleDefinition engine = ModuleCatalog.Find(design.stages[stage].engineId);
            if (engine == null || !engine.IsEngine) return;
            style = ExhaustStyle.For(engine);

            mergedRadius = 0f;
            foreach (PartPlacement part in model.Layout.Parts)
            {
                if (part.Role != PartRole.Engine || part.Stage != stage) continue;
                float exit = Mathf.Max(0.12f, 0.42f * (float)part.Diameter) * style.RadiusScale;
                GameObject go = ProceduralMeshes.MeshObject(plumeProxy, "Plume", visual, engineMaterial);
                DisableShadows(go);
                go.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                plumes.Add(new Plume { Transform = go.transform, Offset = new Vector3((float)part.X, 0f, (float)part.Z), ExitRadius = exit });
                mergedRadius = Mathf.Max(mergedRadius, (float)part.RadialOffset + exit);
            }
            if (plumes.Count == 0) return;
            if (plumes.Count > 1)
            {
                GameObject go = ProceduralMeshes.MeshObject(plumeProxy, "PlumeMerged", visual, mergedMaterial);
                DisableShadows(go);
                go.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
                mergedPlume = go.transform;
            }

            var lightGo = new GameObject("FlameLight");
            lightGo.transform.SetParent(visual, false);
            flameLight = lightGo.AddComponent<Light>();
            flameLight.type = LightType.Point;
            flameLight.shadows = LightShadows.None;
            flameLight.color = style.GlowColor;
            flameLight.enabled = false;
        }

        static void DisableShadows(GameObject go)
        {
            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void UpdatePlumes(bool burning, float throttle, in Frame f)
        {
            foreach (Plume p in plumes) p.Transform.gameObject.SetActive(burning);
            if (mergedPlume != null) mergedPlume.gameObject.SetActive(burning);
            glowColor = Color.black;
            if (!burning || plumes.Count == 0)
            {
                if (flameLight != null) flameLight.enabled = false;
                return;
            }

            float vac = Mathf.Clamp01((float)f.Vacuum);
            float spread = Mathf.Lerp(style.SeaSpread, style.VacuumSpread, Mathf.Pow(vac, 0.7f));
            float length = style.LengthPerRadius * (0.55f + 0.45f * throttle) * (1f + 0.7f * vac);
            float intensity = style.Brightness * (0.35f + 0.65f * throttle) * Mathf.Lerp(1f, style.VacuumBrightness, vac);
            // Thin air: the plumes of a cluster run together into one.
            float merge = mergedPlume != null ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PlumeMergeStart, PlumeMergeEnd, vac)) : 0f;

            SetPlumeMaterial(engineMaterial, intensity * (1f - merge), spread, plumes[0].ExitRadius, vac);
            foreach (Plume p in plumes)
            {
                float rmax = p.ExitRadius * (1f + spread);
                p.Transform.localPosition = p.Offset + new Vector3(0f, f.NozzleY, 0f);
                p.Transform.localScale = new Vector3(rmax, length * p.ExitRadius, rmax);
            }
            if (mergedPlume != null)
            {
                mergedPlume.gameObject.SetActive(merge > 0.01f);
                float rmax = mergedRadius * (1f + spread);
                mergedPlume.localPosition = new Vector3(0f, f.NozzleY, 0f);
                mergedPlume.localScale = new Vector3(rmax, length * mergedRadius * 1.15f, rmax);
                SetPlumeMaterial(mergedMaterial, intensity * merge * Mathf.Sqrt(plumes.Count), spread, mergedRadius, vac);
            }

            float dip = 0.5f + 0.5f * Mathf.Sin(Time.time * 47f);
            float power = style.GlowPower * (0.4f + 0.6f * throttle) * (1f - 0.8f * vac) * (0.85f + 0.15f * dip) * plumes.Count;
            glowPosition = origin.ToUnityPosition(f.Position + f.Axis * (f.NozzleY - 2.0));
            glowColor = style.GlowColor * (0.004f * power);
            glowRange = 28f + 2.5f * power;
            if (flameLight != null)
            {
                flameLight.enabled = power > 0.1f;
                flameLight.transform.localPosition = new Vector3(0f, f.NozzleY - 2f, 0f);
                flameLight.intensity = power;
                flameLight.range = glowRange;
            }
        }

        void SetPlumeMaterial(Material m, float intensity, float spread, float exit, float vac)
        {
            m.SetFloat(IntensityId, intensity);
            m.SetVector(CoreColorId, style.Core);
            m.SetVector(EdgeColorId, style.Edge);
            m.SetFloat(ExitRadiusId, 1f / (1f + spread));
            // Mach diamonds are a feature of dense air only; a vacuum plume expands smoothly.
            m.SetFloat(DiamondsId, style.Diamonds * (1f - vac) * (1f - vac));
            m.SetFloat(FalloffId, Mathf.Lerp(1.1f, 2.4f, vac));
            m.SetFloat(TurbulenceId, style.Turbulence * (1f - 0.7f * vac));
            m.SetFloat(RadiusWorldId, exit);
        }

        // ---- Smoke ------------------------------------------------------------------------------------------------------------

        void UpdateSmoke(bool burning, float throttle, in Frame f, float dt)
        {
            var m = planet.Model;
            double t = simulation.RenderTime;
            Vector3d pos = m.ToFixed(f.Position, t);
            Vector3d axis = (m.ToFixed(m.Position + f.Axis, t)).Normalized;
            Vector3d nozzle = pos + axis * f.NozzleY;
            Vector3d dir = pos.Normalized;
            PlanetRotation.LocalBasis(dir, out Vector3d east, out Vector3d north);

            double alt = nozzle.Magnitude - m.Radius;
            double ratio = m.Atmosphere.ReferenceDensity > 0.0 ? m.Atmosphere.Density(Math.Max(alt, 0.0)) / m.Atmosphere.ReferenceDensity : 0.0;

            if (burning && style.HasSmoke && dt > 0f && ratio > style.MinDensityRatio)
            {
                double ground = m.Radius + m.Surface.SurfaceHeight(dir);
                double height = alt + m.Radius - ground;
                float dense = (float)Math.Pow(ratio, 0.3);
                SpawnGround(throttle, dt, dir, east, north, ground, height);
                SpawnTrail(throttle, dense, nozzle, axis, east, north);
            }
            if (!burning || dt <= 0f || ratio <= style.MinDensityRatio) hasLastNozzle = false;

            // Wind is visual, turns and strengthens with height; the rocket's local axes are good enough for the whole trail.
            for (int i = count - 1; i >= 0; i--)
            {
                ref Puff p = ref puffs[i];
                p.Age += dt;
                if (p.Age >= p.Life)
                {
                    puffs[i] = puffs[--count];
                    continue;
                }
                p.Velocity *= Math.Exp(-dt / p.DragTime);
                double h = Math.Max(0.0, p.Position.Magnitude - m.Radius);
                double wind = 3.0 + 26.0 * (1.0 - Math.Exp(-h / 5000.0));
                double angle = 0.9 + h * 7e-5;
                Vector3d windV = (east * Math.Cos(angle) + north * Math.Sin(angle)) * wind;
                p.Position += (p.Velocity + windV + p.Position.Normalized * p.Rise) * dt;
            }

            Vector3 camera = origin.ToUnityPosition(origin.ViewerPosition);
            for (int i = 0; i < count; i++)
            {
                positions[i] = origin.ToUnityPosition(m.ToInertial(puffs[i].Position, t));
                sortKeys[i] = -(positions[i] - camera).sqrMagnitude; // farthest first
                sortOrder[i] = i;
            }
            Array.Sort(sortKeys, sortOrder, 0, count);

            for (int i = 0; i < count; i++)
            {
                int s = sortOrder[i];
                ref Puff p = ref puffs[s];
                float k = p.Age / p.Life;
                float fadeIn = Mathf.Clamp01(k / 0.04f);
                float fadeOut = Mathf.Pow(1f - k, 1.3f);
                float radius = p.Size * (0.8f + p.Growth * Mathf.Pow(k, 0.55f));
                gpu[i] = new GpuParticle
                {
                    PositionSize = new Vector4(positions[s].x, positions[s].y, positions[s].z, radius),
                    Misc = new Vector4(k, p.Seed, p.Opacity * fadeIn * fadeOut, 0f),
                    Color = new Vector4(p.Albedo.r, p.Albedo.g, p.Albedo.b, p.Soot)
                };
            }
        }

        void SpawnGround(float throttle, float dt, Vector3d dir, Vector3d east, Vector3d north, double ground, double height)
        {
            if (style.GroundRate <= 0f) return;
            double reach = 140.0;
            if (height > reach) return;
            double effect = 1.0 - Math.Min(1.0, height / reach);
            groundBudget += dt * style.GroundRate * throttle * effect;
            while (groundBudget >= 1.0)
            {
                groundBudget -= 1.0;
                double a = Random01() * 6.2831853;
                double c = Math.Cos(a), s = Math.Sin(a);
                Vector3d outward = east * c + north * s;
                double start = 2.0 + 6.0 * Random01();
                double speed = (32.0 + 48.0 * Random01()) * Math.Sqrt(effect);
                AddPuff(new Puff
                {
                    Position = dir * (ground + 2.0) + outward * start,
                    Velocity = outward * speed + dir * (1.0 + 3.0 * Random01()),
                    Life = 13f + 11f * Random01(),
                    Size = style.GroundSize * (0.75f + 0.5f * Random01()),
                    Growth = 3.6f,
                    Opacity = style.GroundOpacity,
                    Seed = Random01(),
                    Rise = 0.6f + 2.2f * Random01(),
                    DragTime = 2.4f,
                    Soot = style.Soot * 0.3f,
                    Albedo = style.SmokeColor
                });
            }
        }

        void SpawnTrail(float throttle, float dense, Vector3d nozzle, Vector3d axis, Vector3d east, Vector3d north)
        {
            if (style.TrailRate <= 0f) return;
            if (!hasLastNozzle)
            {
                lastNozzle = nozzle;
                hasLastNozzle = true;
                return;
            }
            Vector3d step = nozzle - lastNozzle;
            double moved = step.Magnitude;
            lastNozzle = nozzle;
            trailDistance += moved;
            // Spacing grows with speed, so the rate (and with it the particle budget) stays capped.
            double speed = moved / Math.Max(Time.deltaTime * Math.Max(1.0, simulation.TimeWarp), 1e-3); // simulated speed, also under time warp
            double spacing = Math.Max(style.TrailSize * 0.5, speed / style.TrailRate);
            int guard = 0;
            while (trailDistance >= spacing && guard++ < 48)
            {
                trailDistance -= spacing;
                double back = moved > 1e-6 ? trailDistance / moved : 0.0;
                Vector3d point = nozzle - step * Math.Min(1.0, back);
                double a = Random01() * 6.2831853;
                Vector3d lateral = (east * Math.Cos(a) + north * Math.Sin(a)) * (1.5 + 3.5 * Random01());
                float expand = 1f + 4f * (1f - dense);
                AddPuff(new Puff
                {
                    Position = point + lateral,
                    Velocity = axis * -(25.0 + 35.0 * Random01()) + lateral,
                    Life = style.TrailLife * (0.75f + 0.5f * Random01()),
                    Size = style.TrailSize * expand * (0.8f + 0.4f * Random01()),
                    Growth = 2.4f,
                    Opacity = style.TrailOpacity * dense * Mathf.Sqrt(throttle),
                    Seed = Random01(),
                    Rise = 0.3f,
                    DragTime = 1.4f,
                    Soot = style.Soot,
                    Albedo = style.SmokeColor
                });
            }
            if (guard >= 48) trailDistance = 0.0;
        }

        void AddPuff(Puff p)
        {
            if (count >= MaxPuffs)
            {
                // Out of budget: the most faded puff makes room.
                int oldest = 0;
                float worst = -1f;
                for (int i = 0; i < count; i++)
                {
                    float k = puffs[i].Age / puffs[i].Life;
                    if (k > worst)
                    {
                        worst = k;
                        oldest = i;
                    }
                }
                puffs[oldest] = p;
                return;
            }
            puffs[count++] = p;
        }

        /// <summary>After the sky has its uniforms for this frame (order 120), so the smoke is lit by them.</summary>
        void LateUpdate()
        {
            if (!smokeReady || count == 0 || atmosphere == null) return;
            buffer.SetData(gpu, 0, 0, count);
            atmosphere.ApplyLighting(smokeMaterial);
            smokeMaterial.SetVector(GlowPositionId, glowPosition);
            smokeMaterial.SetVector(GlowColorId, new Vector4(glowColor.r, glowColor.g, glowColor.b, 0f));
            smokeMaterial.SetFloat(GlowRangeId, glowRange);
            var bounds = new Bounds(Vector3.zero, Vector3.one * 4e7f);
            Graphics.DrawMeshInstancedProcedural(quad, 0, smokeMaterial, bounds, count, null, ShadowCastingMode.Off, false, gameObject.layer);
        }
    }
}
