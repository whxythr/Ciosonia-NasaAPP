using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using SpaceSim.Runtime;
using UnityEditor;
#if SPACESIM_ANIMATION
using UnityEditor.Animations;
#endif
using UnityEngine;

namespace SpaceSim.Editor
{
    /// <summary>
    /// Wires the Asset Store package "Robot Sphere" (content id 136226) in as the EVA robot: finds its prefab wherever the
    /// import put it, converts the package's built-in-pipeline materials to URP Lit, reads the Animator controller's
    /// parameters and states, and stores all of it in Resources/<see cref="RobotModelSettings.ResourcePath"/>. Runs from
    /// the menu, and by itself once after an import that brings a robot prefab while no robot is set up yet. Without the
    /// package nothing changes and the EVA keeps the procedural astronaut.
    /// </summary>
    public static class RobotSphereSetup
    {
        public const string SettingsAssetPath = "Assets/_Project/Resources/Robot/RobotModelSettings.asset";
        const string ProjectRoot = "Assets/_Project/";

        [MenuItem("SpaceSim/Robot/Set up Robot Sphere model", priority = 60)]
        static void SetupMenu() => Setup(true);

        [MenuItem("SpaceSim/Robot/Use procedural astronaut (clear robot)", priority = 61)]
        static void ClearMenu()
        {
            RobotModelSettings s = AssetDatabase.LoadAssetAtPath<RobotModelSettings>(SettingsAssetPath);
            if (s == null) return;
            s.prefab = null;
            EditorUtility.SetDirty(s);
            AssetDatabase.SaveAssets();
            RobotModelSettings.ClearCache();
            Debug.Log("SpaceSim: robot cleared, the EVA uses the procedural astronaut.");
        }

        /// <summary>Finds the robot, converts its materials and fills the settings; returns a report (or null if not found).</summary>
        public static string Setup(bool interactive)
        {
            string path = FindRobotPrefab();
            if (path == null)
            {
                const string missing = "Robot Sphere is not in the project. Import it: Window ▸ Package Manager ▸ My Assets ▸ Robot Sphere ▸ Download ▸ Import. " +
                                       "Until then the EVA uses the procedural astronaut.";
                if (interactive) EditorUtility.DisplayDialog("SpaceSim · Robot", missing, "OK");
                else Debug.Log("SpaceSim: " + missing);
                return null;
            }

            var report = new StringBuilder();
            report.AppendLine("Robot prefab: " + path);
            string vendor = VendorFolder(path);
            int converted = ConvertMaterials(vendor, report);
            report.AppendLine("Materials converted to URP Lit in " + vendor + ": " + converted);

            RobotModelSettings settings = LoadOrCreateSettings();
            settings.prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            DetectAnimation(settings, report);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            RobotModelSettings.ClearCache();
            report.AppendLine("Settings: " + SettingsAssetPath + " (height " + settings.height + " m, eye at " + settings.eyeHeightFraction + " of it — adjust there).");
            Debug.Log("SpaceSim: robot set up.\n" + report);
            if (interactive) EditorUtility.DisplayDialog("SpaceSim · Robot", report.ToString(), "OK");
            return report.ToString();
        }

        /// <summary>The best robot prefab (or model) outside the project's own folder, or null.</summary>
        public static string FindRobotPrefab()
        {
            string best = null;
            int bestScore = int.MinValue;
            var seen = new HashSet<string>();
            foreach (string filter in new[] { "t:Prefab", "t:Model" })
                foreach (string guid in AssetDatabase.FindAssets(filter))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (!seen.Add(p) || p.StartsWith(ProjectRoot, StringComparison.OrdinalIgnoreCase) || !p.StartsWith("Assets/", StringComparison.Ordinal)) continue;
                    int score = Score(p);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = p;
                    }
                }
            return bestScore > 0 ? best : null;
        }

        /// <summary>How much a path looks like the robot: "robot" and "sphere" in the name or folder, a prefab with an Animator.</summary>
        static int Score(string path)
        {
            string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            string folder = (Path.GetDirectoryName(path) ?? "").Replace('\\', '/').ToLowerInvariant();
            bool robot = file.Contains("robot") || folder.Contains("robot");
            if (!robot) return int.MinValue;
            int s = 1;
            if (file.Contains("robot")) s += 3;
            if (file.Contains("sphere")) s += 4;
            if (folder.Contains("sphere")) s += 1;
            bool prefab = path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
            if (prefab) s += 2;
            foreach (string bad in new[] { "demo", "scene", "camera", "light", "ragdoll", "env", "floor", "ground", "fx", "effect", "particle", "bullet", "projectile" })
                if (file.Contains(bad)) s -= 4;
            if (prefab)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null)
                {
                    if (go.GetComponentsInChildren<Renderer>(true).Length == 0) return int.MinValue;
#if SPACESIM_ANIMATION
                    Animator a = go.GetComponentInChildren<Animator>(true);
                    if (a != null && a.runtimeAnimatorController != null) s += 2;
#endif
                }
            }
            return s;
        }

        /// <summary>The package's top folder (Assets/&lt;Vendor&gt;), where its materials live.</summary>
        static string VendorFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            return parts.Length > 2 ? parts[0] + "/" + parts[1] : Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? "Assets";
        }

        /// <summary>Switches built-in Standard/Legacy materials of the package to URP Lit, keeping colours, maps and gloss.</summary>
        public static int ConvertMaterials(string folder, StringBuilder report)
        {
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null)
            {
                report?.AppendLine("URP Lit shader not found — materials left as they are (the runtime converts them on the fly).");
                return 0;
            }
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { folder }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(p);
                if (m == null || !RobotBodyModel.NeedsConversion(m)) continue;
                var source = new Material(m);
                float mode = source.HasProperty("_Mode") ? source.GetFloat("_Mode") : 0f;
                m.shader = lit;
                RobotBodyModel.CopyMaterialProperties(source, m);
                if (mode >= 2f) MakeTransparent(m); // Standard: 2 = Fade, 3 = Transparent
                else if (mode >= 1f && m.HasProperty("_AlphaClip")) // 1 = Cutout
                {
                    m.SetFloat("_AlphaClip", 1f);
                    if (source.HasProperty("_Cutoff") && m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", source.GetFloat("_Cutoff"));
                    m.EnableKeyword("_ALPHATEST_ON");
                }
                UnityEngine.Object.DestroyImmediate(source);
                EditorUtility.SetDirty(m);
                report?.AppendLine("  " + p);
                n++;
            }
            if (n > 0) AssetDatabase.SaveAssets();
            return n;
        }

        static void MakeTransparent(Material m)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        static RobotModelSettings LoadOrCreateSettings()
        {
            RobotModelSettings s = AssetDatabase.LoadAssetAtPath<RobotModelSettings>(SettingsAssetPath);
            if (s != null) return s;
            EnsureFolder(Path.GetDirectoryName(SettingsAssetPath)?.Replace('\\', '/'));
            s = ScriptableObject.CreateInstance<RobotModelSettings>();
            AssetDatabase.CreateAsset(s, SettingsAssetPath);
            return s;
        }

        static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        /// <summary>Picks the controller's speed/grounded parameters and its idle/walk/run/air states by name.</summary>
        static void DetectAnimation(RobotModelSettings s, StringBuilder report)
        {
#if SPACESIM_ANIMATION
            Animator animator = s.prefab != null ? s.prefab.GetComponentInChildren<Animator>(true) : null;
            RuntimeAnimatorController rac = animator != null ? animator.runtimeAnimatorController : null;
            if (rac is AnimatorOverrideController o) rac = o.runtimeAnimatorController;
            var controller = rac as AnimatorController;
            if (controller == null)
            {
                report.AppendLine(animator == null ? "No Animator: the robot moves by the procedural lean and bob only."
                                                   : "Animator without an editable controller: states left at their defaults.");
                return;
            }
            foreach (AnimatorControllerParameter p in controller.parameters)
            {
                string n = p.name.ToLowerInvariant();
                if (p.type == AnimatorControllerParameterType.Float && (n.Contains("speed") || n.Contains("velocity") || n == "move" || n.Contains("forward"))) s.speedParameter = p.name;
                if (p.type == AnimatorControllerParameterType.Bool && n.Contains("ground")) s.groundedParameter = p.name;
            }
            var states = new List<string>();
            if (controller.layers.Length > 0) CollectStates(controller.layers[0].stateMachine, states);
            string idle = Pick(states, "idle", "stand"), walk = Pick(states, "walk", "move", "roll"), run = Pick(states, "run", "sprint", "fast", "roll");
            string air = Pick(states, "jump", "fall", "air");
            if (idle != null) s.idleState = idle;
            if (walk != null) s.walkState = walk;
            s.runState = run ?? s.walkState;
            if (air != null) s.airState = air;
            report.AppendLine("Animator states: " + string.Join(", ", states) + " → idle '" + s.idleState + "', walk '" + s.walkState + "', run '" + s.runState +
                              "', air '" + s.airState + "'; speed parameter '" + s.speedParameter + "'.");
#else
            report.AppendLine("The Animation module is disabled: animator states were not read.");
#endif
        }
#if SPACESIM_ANIMATION

        static void CollectStates(AnimatorStateMachine machine, List<string> into)
        {
            foreach (ChildAnimatorState c in machine.states) into.Add(c.state.name);
            foreach (ChildAnimatorStateMachine sub in machine.stateMachines) CollectStates(sub.stateMachine, into);
        }
#endif

        static string Pick(List<string> states, params string[] keys)
        {
            foreach (string k in keys)
                foreach (string s in states)
                    if (s.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0) return s;
            return null;
        }
    }

    /// <summary>Runs <see cref="RobotSphereSetup.Setup"/> once after a package import brings a robot while none is set up.</summary>
    sealed class RobotSphereImportWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (Application.isBatchMode) return;
            bool robot = false;
            foreach (string p in imported)
            {
                if (p.StartsWith("Assets/_Project/", StringComparison.OrdinalIgnoreCase)) continue;
                if ((p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)) &&
                    p.IndexOf("robot", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    robot = true;
                    break;
                }
            }
            if (!robot) return;
            var s = AssetDatabase.LoadAssetAtPath<RobotModelSettings>(RobotSphereSetup.SettingsAssetPath);
            if (s != null && s.prefab != null) return;
            EditorApplication.delayCall += () => RobotSphereSetup.Setup(false);
        }
    }
}
