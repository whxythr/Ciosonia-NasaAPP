using System;
using System.Collections.Generic;
using SpaceSim.Exploration;
using SpaceSim.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The surface robot drawn from an imported model (<see cref="RobotModelSettings.prefab"/>, e.g. the Asset Store
    /// package "Robot Sphere"): fitted to <see cref="RobotModelSettings.height"/>, stripped of the vendor's own scripts and
    /// physics, its built-in-pipeline materials swapped for URP Lit at runtime when the editor conversion has not run,
    /// shadow-casting, and animated from the <see cref="SurfaceWalker"/>: the lean tan θ = a/g, a stride bob, the landing
    /// dip, the controller's walk/run/idle states (or a legacy Animation's clips) paced to the speed, and an optional
    /// rolling ball body. In first person the parts around the eye only cast shadows.
    /// </summary>
    public sealed class RobotBodyModel : IExplorerBody
    {
        readonly RobotModelSettings settings;
        readonly GameObject root;
        readonly Transform lean, fit, rolling;
#if SPACESIM_ANIMATION
        readonly Animator animator;
        readonly Animation legacy;
#endif
        readonly List<Renderer> headRenderers = new List<Renderer>();
        readonly List<Renderer> bodyRenderers = new List<Renderer>();
        readonly List<Material> createdMaterials = new List<Material>();
        readonly float rollRadius;
#if SPACESIM_ANIMATION
        readonly int speedHash, groundedHash;
        readonly bool hasSpeed, hasGrounded;
#endif
        readonly Vector3 eyeRest;

        float swing, runBlend, airBlend, leanDeg, sideLean, dirForward = 1f, dirRight;
        float crouch, crouchSpeed;
        int touchdowns = -1;
        string playing = "";
        bool firstPerson = true;

        public Transform Root => root.transform;
        public Vector3 EyeLocal { get; private set; }
        public float LeanDegrees => leanDeg;
        public bool FirstPerson => firstPerson;
        public IReadOnlyList<Renderer> HeadRenderers => headRenderers;
        public IReadOnlyList<Renderer> BodyRenderers => bodyRenderers;
#if SPACESIM_ANIMATION
        /// <summary>The robot's Animator, if the model has one.</summary>
        public Animator Animator => animator;
#endif
        /// <summary>Animator state (or legacy clip) last asked for ("" if none matched).</summary>
        public string PlayingState => playing;

        /// <summary>Builds the robot; throws when the settings have no usable prefab (the caller falls back).</summary>
        public RobotBodyModel(RobotModelSettings robotSettings, Shader lit)
        {
            if (robotSettings == null || !robotSettings.Usable) throw new ArgumentException("no robot prefab", nameof(robotSettings));
            settings = robotSettings;
            root = new GameObject("Robot");
            lean = new GameObject("Lean").transform;
            lean.SetParent(root.transform, false);
            fit = new GameObject("Fit").transform;
            fit.SetParent(lean, false);

            // Instantiate under an inactive holder, so the vendor's scripts never wake up before they are stripped.
            var holder = new GameObject("Holder");
            holder.SetActive(false);
            holder.transform.SetParent(fit, false);
            GameObject instance;
            try
            {
                instance = UnityEngine.Object.Instantiate(settings.prefab, holder.transform, false);
            }
            catch
            {
                UnityEngine.Object.Destroy(root);
                throw;
            }
            instance.name = settings.prefab.name;
            Strip(instance);
            instance.SetActive(true);
            holder.SetActive(true);

#if SPACESIM_ANIMATION
            animator = instance.GetComponentInChildren<Animator>();
            legacy = animator == null ? instance.GetComponentInChildren<Animation>() : null;
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                speedHash = Animator.StringToHash(settings.speedParameter ?? "");
                groundedHash = Animator.StringToHash(settings.groundedParameter ?? "");
                if (animator.runtimeAnimatorController != null)
                    foreach (AnimatorControllerParameter p in animator.parameters)
                    {
                        if (p.nameHash == speedHash && p.type == AnimatorControllerParameterType.Float) hasSpeed = true;
                        if (p.nameHash == groundedHash && p.type == AnimatorControllerParameterType.Bool) hasGrounded = true;
                    }
            }
#endif

            var renderers = new List<Renderer>(instance.GetComponentsInChildren<Renderer>(true));
            renderers.RemoveAll(r => !(r is MeshRenderer || r is SkinnedMeshRenderer)); // meshes only: no particles, trails or lines
            foreach (Renderer r in renderers) FixMaterials(r, lit);

            // Fit: uniform scale to the height, feet at y = 0, centred over the origin, face along +Z.
            Bounds b = LocalBounds(renderers, root.transform);
            float s = b.size.y > 1e-4f ? settings.height / b.size.y : 1f;
            Quaternion yaw = Quaternion.Euler(0f, settings.yawOffsetDegrees, 0f);
            Vector3 c = yaw * (b.center * s);
            fit.localRotation = yaw;
            fit.localScale = Vector3.one * s;
            fit.localPosition = new Vector3(-c.x, -b.min.y * s, -c.z);

            eyeRest = new Vector3(0f, settings.height * settings.eyeHeightFraction, settings.eyeForward);
            EyeLocal = eyeRest;

            // Around the eye: shadow-only in first person (the camera sits inside the head).
            float hide = Mathf.Max(0f, settings.firstPersonHideRadius);
            Renderer nearest = null;
            float nearestD = float.PositiveInfinity;
            foreach (Renderer r in renderers)
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                r.receiveShadows = true;
                Bounds rb = LocalBounds(new List<Renderer> { r }, root.transform);
                float d = rb.SqrDistance(eyeRest);
                if (d < nearestD)
                {
                    nearestD = d;
                    nearest = r;
                }
                (d <= hide * hide ? headRenderers : bodyRenderers).Add(r);
            }
            if (headRenderers.Count == 0 && nearest != null)
            {
                bodyRenderers.Remove(nearest);
                headRenderers.Add(nearest);
            }

            if (!string.IsNullOrEmpty(settings.rollingPart))
            {
                rolling = FindDeep(instance.transform, settings.rollingPart);
                if (rolling != null)
                {
                    float radius = settings.rollingRadius;
                    if (radius <= 0f)
                    {
                        var parts = new List<Renderer>(rolling.GetComponentsInChildren<Renderer>());
                        radius = parts.Count > 0 ? LocalBounds(parts, root.transform).extents.y : settings.height * 0.5f;
                    }
                    rollRadius = Mathf.Max(0.02f, radius);
                }
            }
            SetFirstPerson(true);
        }

        public void SetFirstPerson(bool value)
        {
            firstPerson = value;
            foreach (Renderer r in headRenderers)
                if (r != null) r.shadowCastingMode = value ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
        }

        public void Destroy()
        {
            if (root != null) UnityEngine.Object.Destroy(root);
            foreach (Material m in createdMaterials)
                if (m != null) UnityEngine.Object.Destroy(m);
            createdMaterials.Clear();
        }

        public void Animate(SurfaceWalker w, Vector3d forward, Vector3d right, float dt)
        {
            dt = Mathf.Max(dt, 1e-4f);
            double g = w.Gravity;
            Vector3d horizontal = Vector3d.ProjectOnPlane(w.Velocity, w.Up);
            float v = (float)horizontal.Magnitude;
            float walk = (float)w.WalkSpeed, sprint = Mathf.Max((float)w.SprintSpeed, walk + 0.01f), transition = (float)Locomotion.TransitionSpeed(g);
            bool running = w.Gait == Gait.Running;
            float targetSwing = w.Gait == Gait.Standing || w.Gait == Gait.Airborne ? 0f
                : running ? Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((v - transition) / Mathf.Max(0.01f, sprint - transition)))
                : 0.5f * Mathf.Clamp01(v / Mathf.Max(0.05f, walk));
            swing = Smooth(swing, targetSwing, 8f, dt);
            runBlend = Smooth(runBlend, running ? 1f : 0f, 5f, dt);
            airBlend = Smooth(airBlend, w.OnGround ? 0f : 1f, 10f, dt);
            if (v > 0.05f)
            {
                dirForward = Smooth(dirForward, (float)(Vector3d.Dot(horizontal, forward) / v), 10f, dt);
                dirRight = Smooth(dirRight, (float)(Vector3d.Dot(horizontal, right) / v), 10f, dt);
            }

            // Lean into the acceleration: tan θ = a/g (forward speeding up, back braking), plus the gait's own lean.
            float aForward = (float)Vector3d.Dot(w.Acceleration, forward), aRight = (float)Vector3d.Dot(w.Acceleration, right);
            float targetLean = (float)(Locomotion.LeanAngle(aForward, g) * MathD.RadToDeg) + Mathf.Lerp(2f, 9f, runBlend) * swing;
            leanDeg = Smooth(leanDeg, Mathf.Clamp(targetLean, -20f, 32f), 6f, dt);
            sideLean = Smooth(sideLean, Mathf.Clamp(-(float)(Locomotion.LeanAngle(aRight, g) * MathD.RadToDeg) * 0.5f, -12f, 12f), 6f, dt);

            // Landing: the suspension takes the vertical speed and springs back (damped, ω = 11 rad/s).
            if (touchdowns < 0) touchdowns = w.Touchdowns;
            if (w.Touchdowns != touchdowns)
            {
                touchdowns = w.Touchdowns;
                crouchSpeed += Mathf.Min(1.5f, (float)w.LastTouchdownSpeed * 0.35f);
            }
            const float omega = 11f;
            crouchSpeed += (-omega * omega * crouch - 2f * 0.85f * omega * crouchSpeed) * dt;
            crouch = Mathf.Clamp(crouch + crouchSpeed * dt, 0f, 0.15f);

            // Stride bob: two lifts per gait cycle, higher in a run (the flight between steps).
            float phase = (float)w.StridePhase;
            float flight = Mathf.Min(0.08f, (float)Locomotion.StrideFlightHeight(v, g)) * 0.5f + 0.01f;
            float bob = swing * Mathf.Lerp(0.3f, 1f, runBlend) * flight * (1f - Mathf.Cos(4f * Mathf.PI * phase));
            Quaternion tilt = Quaternion.Euler(leanDeg, 0f, sideLean);
            lean.localPosition = new Vector3(0f, bob - crouch, 0f);
            lean.localRotation = tilt;
            EyeLocal = lean.localPosition + tilt * eyeRest;

            if (rolling != null && v > 0.01f)
            {
                var axis = new Vector3(dirForward, 0f, -dirRight); // up × motion, in the root frame
                if (axis.sqrMagnitude > 1e-4f) rolling.Rotate(root.transform.TransformDirection(axis.normalized), v * dt / rollRadius * Mathf.Rad2Deg, Space.World);
            }

            DriveAnimation(w, v, walk);
        }

        void DriveAnimation(SurfaceWalker w, float v, float walk)
        {
            string target = !w.OnGround ? settings.airState
                : w.Gait == Gait.Running ? settings.runState
                : w.Gait == Gait.Walking ? settings.walkState
                : settings.idleState;
            float clipSpeed = Mathf.Max(0.1f, settings.walkClipSpeed);
            float pace = w.Gait == Gait.Running ? Mathf.Clamp(v / (2f * clipSpeed), 0.6f, 2.5f)
                : w.Gait == Gait.Walking ? Mathf.Clamp(v / clipSpeed, 0.4f, 2f)
                : 1f;
#if SPACESIM_ANIMATION
            if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
            {
                if (hasSpeed) animator.SetFloat(speedHash, v);
                if (hasGrounded) animator.SetBool(groundedHash, w.OnGround);
                string state = Resolve(target, w.Gait);
                if (state.Length > 0 && state != playing)
                {
                    animator.CrossFadeInFixedTime(state, 0.2f, 0);
                    playing = state;
                }
                animator.speed = pace;
            }
            else if (legacy != null)
            {
                string clip = LegacyClip(w.Gait, !w.OnGround);
                if (clip.Length > 0 && clip != playing)
                {
                    legacy.CrossFade(clip, 0.2f);
                    playing = clip;
                }
                if (clip.Length > 0 && legacy[clip] != null) legacy[clip].speed = pace;
            }
#endif
        }

#if SPACESIM_ANIMATION
        /// <summary>The wanted state if the controller has it, else the next best (run → walk, air → idle).</summary>
        string Resolve(string wanted, Gait gait)
        {
            if (Has(wanted)) return wanted;
            if (gait == Gait.Running && Has(settings.walkState)) return settings.walkState;
            if (Has(settings.idleState)) return settings.idleState;
            return "";
        }

        bool Has(string state) => !string.IsNullOrEmpty(state) && animator.HasState(0, Animator.StringToHash(state));

        string LegacyClip(Gait gait, bool air)
        {
            string[] keys = air ? new[] { "jump", "air", "idle" }
                : gait == Gait.Running ? new[] { "run", "roll", "walk" }
                : gait == Gait.Walking ? new[] { "walk", "roll", "move" }
                : new[] { "idle", "stand" };
            foreach (string k in keys)
                foreach (AnimationState s in legacy)
                    if (s.name.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return s.name;
            return "";
        }
#endif

        static float Smooth(float current, float target, float rate, float dt) => Mathf.Lerp(current, target, 1f - Mathf.Exp(-rate * dt));

        /// <summary>Removes the vendor's scripts, physics and audio: the robot is a puppet of the walker.</summary>
        static void Strip(GameObject instance)
        {
            foreach (MonoBehaviour mb in instance.GetComponentsInChildren<MonoBehaviour>(true)) Kill(mb);
            foreach (Joint j in instance.GetComponentsInChildren<Joint>(true)) Kill(j);
            foreach (CharacterController c in instance.GetComponentsInChildren<CharacterController>(true)) Kill(c);
            foreach (Rigidbody r in instance.GetComponentsInChildren<Rigidbody>(true)) Kill(r);
            foreach (Collider c in instance.GetComponentsInChildren<Collider>(true)) Kill(c);
            foreach (AudioSource a in instance.GetComponentsInChildren<AudioSource>(true)) Kill(a);
            foreach (Camera cam in instance.GetComponentsInChildren<Camera>(true)) Kill(cam);
            foreach (Light l in instance.GetComponentsInChildren<Light>(true)) l.shadows = LightShadows.None;
        }

        static void Kill(Component c)
        {
            if (c == null) return;
            try
            {
                UnityEngine.Object.DestroyImmediate(c);
            }
            catch (Exception)
            {
                if (c is Behaviour b) b.enabled = false;
            }
        }

        /// <summary>
        /// A built-in-pipeline material (Standard, Legacy, or a shader URP cannot draw) would render magenta: replace it with
        /// URP Lit carrying the same colour, textures and gloss. The editor tool converts the assets themselves; this is the
        /// runtime safety net.
        /// </summary>
        void FixMaterials(Renderer r, Shader lit)
        {
            if (lit == null) return;
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!NeedsConversion(mats[i])) continue;
                mats[i] = ConvertToLit(mats[i], lit);
                createdMaterials.Add(mats[i]);
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }

        /// <summary>True for materials the Universal pipeline cannot draw.</summary>
        public static bool NeedsConversion(Material m)
        {
            if (m == null) return true;
            Shader s = m.shader;
            if (s == null || !s.isSupported) return true;
            string n = s.name;
            return n == "Standard" || n == "Standard (Specular setup)" || n == "Autodesk Interactive" || n.StartsWith("Legacy Shaders/", StringComparison.Ordinal) ||
                   n.StartsWith("Mobile/", StringComparison.Ordinal) || n == "Hidden/InternalErrorShader";
        }

        /// <summary>A URP Lit copy of a built-in material (Standard property names → URP names).</summary>
        public static Material ConvertToLit(Material source, Shader lit)
        {
            var m = new Material(lit) { name = (source != null ? source.name : "Robot") + " (URP)" };
            if (source == null)
            {
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.72f));
                return m;
            }
            CopyMaterialProperties(source, m);
            return m;
        }

        /// <summary>Maps the Standard shader's properties onto URP Lit (used at runtime and by the editor conversion).</summary>
        public static void CopyMaterialProperties(Material source, Material target)
        {
            if (source.HasProperty("_Color") && target.HasProperty("_BaseColor")) target.SetColor("_BaseColor", source.GetColor("_Color"));
            if (source.HasProperty("_MainTex") && target.HasProperty("_BaseMap"))
            {
                target.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
                target.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
                target.SetTextureOffset("_BaseMap", source.GetTextureOffset("_MainTex"));
            }
            if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null && target.HasProperty("_BumpMap"))
            {
                target.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                if (source.HasProperty("_BumpScale")) target.SetFloat("_BumpScale", source.GetFloat("_BumpScale"));
                target.EnableKeyword("_NORMALMAP");
            }
            if (source.HasProperty("_MetallicGlossMap") && source.GetTexture("_MetallicGlossMap") != null && target.HasProperty("_MetallicGlossMap"))
            {
                target.SetTexture("_MetallicGlossMap", source.GetTexture("_MetallicGlossMap"));
                target.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (source.HasProperty("_Metallic") && target.HasProperty("_Metallic")) target.SetFloat("_Metallic", source.GetFloat("_Metallic"));
            float gloss = source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : source.HasProperty("_Shininess") ? source.GetFloat("_Shininess") : 0.4f;
            if (source.HasProperty("_GlossMapScale") && source.GetTexture("_MetallicGlossMap") != null) gloss = source.GetFloat("_GlossMapScale");
            if (target.HasProperty("_Smoothness")) target.SetFloat("_Smoothness", gloss);
            if (source.HasProperty("_OcclusionMap") && source.GetTexture("_OcclusionMap") != null && target.HasProperty("_OcclusionMap"))
            {
                target.SetTexture("_OcclusionMap", source.GetTexture("_OcclusionMap"));
                target.EnableKeyword("_OCCLUSIONMAP");
            }
            bool emissive = source.IsKeywordEnabled("_EMISSION") || (source.HasProperty("_EmissionMap") && source.GetTexture("_EmissionMap") != null);
            if (emissive && target.HasProperty("_EmissionColor"))
            {
                if (source.HasProperty("_EmissionColor")) target.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
                if (source.HasProperty("_EmissionMap") && target.HasProperty("_EmissionMap")) target.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap"));
                target.EnableKeyword("_EMISSION");
                target.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }

        static Bounds LocalBounds(List<Renderer> renderers, Transform space)
        {
            bool any = false;
            var b = new Bounds();
            foreach (Renderer r in renderers)
            {
                if (r == null) continue;
                Bounds wb = r.bounds;
                if (wb.size.x == 0f && wb.size.y == 0f && wb.size.z == 0f && r is SkinnedMeshRenderer smr && smr.sharedMesh != null) wb = smr.sharedMesh.bounds;
                Vector3 c = wb.center, e = wb.extents;
                for (int i = 0; i < 8; i++)
                {
                    var corner = new Vector3(c.x + ((i & 1) != 0 ? e.x : -e.x), c.y + ((i & 2) != 0 ? e.y : -e.y), c.z + ((i & 4) != 0 ? e.z : -e.z));
                    Vector3 p = space.InverseTransformPoint(corner);
                    if (!any)
                    {
                        b = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                    else b.Encapsulate(p);
                }
            }
            return any ? b : new Bounds(Vector3.up * 0.5f, Vector3.one);
        }

        static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform f = FindDeep(t.GetChild(i), name);
                if (f != null) return f;
            }
            return null;
        }
    }
}
