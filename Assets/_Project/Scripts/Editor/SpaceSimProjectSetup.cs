using System.IO;
using SpaceSim.Planets;
using SpaceSim.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
#if SPACESIM_URP
using UnityEngine.Rendering.Universal;
#endif

namespace SpaceSim.Editor
{
    /// <summary>One-click project setup: linear colour space, URP asset with depth texture, Earth asset, Phase 1 scene.</summary>
    public static class SpaceSimProjectSetup
    {
        const string Root = "Assets/_Project";
        const string EarthPath = Root + "/ScriptableObjects/Planets/Earth.asset";
        const string ScenePath = Root + "/Scenes/Phase1_Planet.unity";
        const string UrpPath = Root + "/Settings/SpaceSim_URP.asset";
        const string UrpRendererPath = Root + "/Settings/SpaceSim_URP_Renderer.asset";

        [MenuItem("SpaceSim/Setup Project (URP + Phase 1 scene)", priority = 0)]
        public static void SetupAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("SpaceSim Setup", "Остановите Play mode, затем запустите Setup снова.\nStop Play mode first.", "OK");
                return;
            }
            ConfigurePlayer();
            ConfigureUrp();
            CreatePhase1Scene(EnsureHomeSystemAssets());
            CreateMainMenuScene();
        }

        public const string MainMenuScenePath = Root + "/Scenes/MainMenu.unity";
        const string UiPanelPath = Root + "/Resources/UI/HorizonPanel.asset";
        const string UiThemePath = Root + "/Resources/UI/HorizonTheme.tss";

        /// <summary>
        /// PanelSettings template of the HORIZON menus (main menu, in-game Esc menu) with the empty HORIZON theme, so a
        /// runtime panel never warns "No Theme Style Sheet set to PanelSettings". Existing asset is kept.
        /// </summary>
        public static void EnsureUiPanelAsset()
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(UiPanelPath) != null) return;
            AssetDatabase.ImportAsset(UiThemePath);
            var panel = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(UiThemePath);
            panel.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = UnityEngine.UIElements.PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            AssetDatabase.CreateAsset(panel, UiPanelPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SpaceSim] UI panel template created: " + UiPanelPath + (panel.themeStyleSheet == null ? " (theme NOT found!)" : ""));
        }

        /// <summary>Main menu scene (UI Toolkit, <see cref="SpaceSim.Runtime.Menu.MainMenuController"/>), first in Build Settings.</summary>
        [MenuItem("SpaceSim/Create Main Menu Scene", priority = 42)]
        public static void CreateMainMenuScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("SpaceSim", "Остановите Play mode. / Stop Play mode first.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildMainMenuScene();
        }

        /// <summary>
        /// Creates and saves the main menu scene and puts it first in Build Settings (the game scene stays after it, so
        /// the menu can load it). No dialogs: the caller must have saved the open scenes.
        /// </summary>
        public static void BuildMainMenuScene()
        {
            EnsureUiPanelAsset();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // UI Toolkit draws on its own; the camera only clears the screen (and silences "No cameras rendering").
            var cam = new GameObject("MenuCamera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.cullingMask = 0;

            new GameObject("MainMenu").AddComponent<SpaceSim.Runtime.Menu.MainMenuController>();

            Directory.CreateDirectory(Path.GetDirectoryName(MainMenuScenePath));
            EditorSceneManager.SaveScene(scene, MainMenuScenePath);
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == MainMenuScenePath);
            scenes.Insert(0, new EditorBuildSettingsScene(MainMenuScenePath, true));
            if (!scenes.Exists(s => s.path == ScenePath) && File.Exists(ScenePath)) scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[SpaceSim] Scene created: " + MainMenuScenePath + " — press Play.");
        }

        const string HomeSystemDir = Root + "/ScriptableObjects/StarSystems/Home";
        const string HomeSystemPath = HomeSystemDir + "/HomeSystem.asset";

        /// <summary>
        /// Saves the home system (star + Teonan, Quoris, Hermeous Mora, moon) as editable assets, so every parameter
        /// can be tuned in the Inspector. Existing assets are kept (your edits are not overwritten).
        /// </summary>
        [MenuItem("SpaceSim/Create Home System Assets", priority = 39)]
        public static StarSystemDefinition EnsureHomeSystemAssets()
        {
            var existing = AssetDatabase.LoadAssetAtPath<StarSystemDefinition>(HomeSystemPath);
            if (existing != null)
            {
                bool current = existing.bodies.Exists(b => b != null && b.planetId == HomeSystemCatalog.HomeWorldId);
                if (current) return existing;
                // Generated by an older version (no start planet): regenerate the whole folder.
                AssetDatabase.DeleteAsset(HomeSystemDir);
                Debug.Log("[SpaceSim] Outdated home system assets replaced (they lacked the start planet).");
            }
            Directory.CreateDirectory(HomeSystemDir);
            var system = StarSystemDefinition.CreateHomeSystem();
            for (int i = 0; i < system.bodies.Count; i++)
            {
                PlanetDefinition body = system.bodies[i];
                AssetDatabase.CreateAsset(body, HomeSystemDir + "/" + body.planetId + ".asset");
            }
            AssetDatabase.CreateAsset(system, HomeSystemPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SpaceSim] Home system assets created in " + HomeSystemDir);
            return system;
        }

        [MenuItem("SpaceSim/Configure Player (linear colour space)", priority = 20)]
        public static void ConfigurePlayer()
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear)
            {
                PlayerSettings.colorSpace = ColorSpace.Linear;
                Debug.Log("[SpaceSim] Colour space set to Linear.");
            }
        }

        const string ModuleLibraryPath = Root + "/Resources/SpaceSim/ModuleVisualLibrary.asset";

        [MenuItem("SpaceSim/Module Models Library (create or update)", priority = 43)]
        public static void ModuleLibraryMenu()
        {
            string result = CreateModuleLibrary();
            Debug.Log("[SpaceSim] " + result);
            var lib = AssetDatabase.LoadAssetAtPath<ModuleVisualLibrary>(ModuleLibraryPath);
            if (lib != null)
            {
                Selection.activeObject = lib;
                EditorGUIUtility.PingObject(lib);
            }
        }

        /// <summary>
        /// The asset that maps rocket modules to 3D models (Resources/SpaceSim/ModuleVisualLibrary). Adds an entry for every catalog
        /// module that has none yet; models already assigned are kept. Tanks and protection shells are stretched to their slot.
        /// </summary>
        public static string CreateModuleLibrary()
        {
            var lib = AssetDatabase.LoadAssetAtPath<ModuleVisualLibrary>(ModuleLibraryPath);
            bool created = false;
            if (lib == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ModuleLibraryPath));
                lib = ScriptableObject.CreateInstance<ModuleVisualLibrary>();
                AssetDatabase.CreateAsset(lib, ModuleLibraryPath);
                created = true;
            }
            int added = 0;
            foreach (SpaceSim.Vehicles.ModuleDefinition m in SpaceSim.Vehicles.ModuleCatalog.All)
            {
                if (lib.entries.Exists(e => e != null && e.moduleId == m.id)) continue;
                lib.entries.Add(new ModuleVisualLibrary.Entry
                {
                    moduleId = m.id,
                    fit = ModuleVisualLibrary.DefaultFit(m)
                });
                added++;
            }
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            int withModels = lib.entries.FindAll(e => e != null && e.model != null).Count;
            return (created ? "created " : "updated ") + ModuleLibraryPath + ": " + lib.entries.Count + " entries (+" + added + "), with models: " + withModels;
        }

        [MenuItem("SpaceSim/Configure URP (depth texture, shadows)", priority = 21)]
        public static void ConfigureUrp()
        {
#if SPACESIM_URP
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpPath);
            if (asset == null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(UrpPath));
                var renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(renderer, UrpRendererPath);
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, UrpPath);
            }
            asset.supportsCameraDepthTexture = true; // the atmosphere pass needs scene depth
            // …of THIS frame: the atmosphere is a transparent pass, so depth must be copied after the opaques. With
            // "after transparents" it saw no geometry and hazed the ground and the rocket as if they were open sky.
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(UrpRendererPath);
            if (rendererData != null && rendererData.copyDepthMode != CopyDepthMode.AfterOpaques)
            {
                rendererData.copyDepthMode = CopyDepthMode.AfterOpaques;
                EditorUtility.SetDirty(rendererData);
            }
            // The brightness setting (ScreenBrightness) uses a post-exposure Volume; without post-process data URP skips it.
            // Post-processing stays off on the camera while brightness is at its default, so the image is unchanged then.
            if (rendererData != null && rendererData.postProcessData == null)
            {
                rendererData.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(
                    "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset");
                EditorUtility.SetDirty(rendererData);
            }
            asset.supportsHDR = true;
            asset.hdrColorBufferPrecision = HDRColorBufferPrecision._64Bits; // 11-bit float bands visibly in sky gradients
            asset.shadowDistance = 400f;
            asset.shadowCascadeCount = 4;
            // The astronaut on foot: a sharp first cascade (12 m) at 4096² and soft (filtered) edges, so a 15 cm leg still
            // casts a clean shadow next to the boots; the rocket and the tower use the outer cascades.
            asset.cascade4Split = new Vector3(0.03f, 0.12f, 0.4f);
            asset.mainLightShadowmapResolution = 4096;
            var serialized = new SerializedObject(asset); // supportsSoftShadows has an internal setter
            SerializedProperty soft = serialized.FindProperty("m_SoftShadowsSupported");
            if (soft != null && !soft.boolValue)
            {
                soft.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            // The hangar's ceiling spotlights cast the rocket's shadow on the floor (the only additional lights in the game).
            SerializedProperty extraShadows = serialized.FindProperty("m_AdditionalLightShadowsSupported");
            if (extraShadows != null && !extraShadows.boolValue)
            {
                extraShadows.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            EditorUtility.SetDirty(asset);
            GraphicsSettings.defaultRenderPipeline = asset;
            QualitySettings.renderPipeline = asset;
            AssetDatabase.SaveAssets();
            Debug.Log("[SpaceSim] URP asset assigned: " + UrpPath);
#else
            Debug.LogWarning("[SpaceSim] URP package not installed — SpaceSim's shaders are written for URP and will not render.");
#endif
        }

        const string ShaderDir = Root + "/Shaders/";
        /// <summary>
        /// Shaders that are only looked up by name (<see cref="Shader.Find"/>) from code: without this a player build strips them and the clouds,
        /// flame, smoke and heating glow silently vanish. (The planet surface, sky, stars and Sun are referenced by the scene.)
        /// </summary>
        static readonly string[] ShadersFoundByName = { "Clouds", "EnginePlume", "Smoke", "HeatGlow", "MilkyWay" };

        [InitializeOnLoadMethod]
        static void IncludeShadersOnLoad() => EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling) EnsureShadersInBuild();
        };

        /// <summary>Adds <see cref="ShadersFoundByName"/> to Project Settings ▸ Graphics ▸ Always Included Shaders. Idempotent.</summary>
        [MenuItem("SpaceSim/Include Effect Shaders in Build", priority = 22)]
        public static void EnsureShadersInBuild()
        {
            var settings = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            SerializedProperty list = settings.FindProperty("m_AlwaysIncludedShaders");
            bool changed = false;
            foreach (string name in ShadersFoundByName)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + name + ".shader");
                if (shader == null) continue;
                bool present = false;
                for (int i = 0; i < list.arraySize; i++)
                    if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) present = true;
                if (present) continue;
                int at = list.arraySize;
                list.InsertArrayElementAtIndex(at);
                list.GetArrayElementAtIndex(at).objectReferenceValue = shader;
                changed = true;
            }
            if (!changed) return;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            Debug.Log("[SpaceSim] Effect shaders added to Always Included Shaders.");
        }

        [MenuItem("SpaceSim/Create Earth Definition", priority = 40)]
        public static PlanetDefinition EnsureEarthDefinition()
        {
            var def = AssetDatabase.LoadAssetAtPath<PlanetDefinition>(EarthPath);
            if (def != null) return def;
            Directory.CreateDirectory(Path.GetDirectoryName(EarthPath));
            def = ScriptableObject.CreateInstance<PlanetDefinition>();
            AssetDatabase.CreateAsset(def, EarthPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SpaceSim] Created " + EarthPath);
            return def;
        }

        [MenuItem("SpaceSim/Create Phase 1 Scene", priority = 41)]
        public static void CreatePhase1SceneMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("SpaceSim", "Остановите Play mode. / Stop Play mode first.", "OK");
                return;
            }
            CreatePhase1Scene(EnsureHomeSystemAssets());
        }

        static void CreatePhase1Scene(StarSystemDefinition system)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("SpaceSim");
            var boot = go.AddComponent<SpaceSimBootstrap>();
            var so = new SerializedObject(boot);
            so.FindProperty("starSystem").objectReferenceValue = system;
            so.FindProperty("planetSurfaceShader").objectReferenceValue = Shader.Find("SpaceSim/PlanetSurface");
            so.FindProperty("atmosphereShader").objectReferenceValue = Shader.Find("SpaceSim/Atmosphere");
            so.FindProperty("starfieldShader").objectReferenceValue = Shader.Find("SpaceSim/Starfield");
            so.FindProperty("sunDiskShader").objectReferenceValue = Shader.Find("SpaceSim/SunDisk");
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            so.FindProperty("litShader").objectReferenceValue = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == ScenePath);
            // Right after the main menu (the build starts there), otherwise first.
            scenes.Insert(scenes.FindIndex(s => s.path == MainMenuScenePath) + 1, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[SpaceSim] Scene created: " + ScenePath + " — press Play.");
        }

        [MenuItem("SpaceSim/Create Random Planet Definition", priority = 60)]
        public static void CreateRandomPlanet()
        {
            // Randomness only at creation time; the seeds are then stored in the asset and never change.
            int visualSeed = System.Environment.TickCount & 0x7FFFFFFF;
            int physicalSeed = (visualSeed * 1103515245 + 12345) & 0x7FFFFFFF;
            var def = ScriptableObject.CreateInstance<PlanetDefinition>();
            def.planetId = "planet-" + visualSeed.ToString("x8");
            def.displayName = "Планета " + visualSeed.ToString("x4").ToUpperInvariant();
            def.generation.seed = visualSeed;
            def.GeneratePhysicalProperties(physicalSeed);
            if (!def.physical.atmosphere.hasAtmosphere)
            {
                def.generation.hasOcean = false;
                def.generation.craterCount = 400;
                def.generation.continentAmplitude = 2500;
                def.generation.oceanDepthAmplitude = 2500;
                def.generation.grassland = def.generation.rock;
                def.generation.forest = def.generation.rock;
                def.generation.desert = new SpaceSim.Mathematics.ColorRGB(0.45f, 0.42f, 0.40f);
            }
            string dir = Root + "/ScriptableObjects/Planets";
            Directory.CreateDirectory(dir);
            string path = AssetDatabase.GenerateUniqueAssetPath(dir + "/" + def.planetId + ".asset");
            AssetDatabase.CreateAsset(def, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = def;
            Debug.Log("[SpaceSim] Created " + path + " (visual seed " + visualSeed + ", physical seed " + physicalSeed +
                      "). Assign it to the SpaceSimBootstrap to fly there.");
        }

        [MenuItem("SpaceSim/Reveal Planet Save Folder", priority = 80)]
        public static void RevealSaves()
        {
            Directory.CreateDirectory(PlanetSaveService.Directory);
            EditorUtility.RevealInFinder(PlanetSaveService.Directory);
        }
    }
}
