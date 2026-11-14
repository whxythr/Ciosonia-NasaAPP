using System.Collections;
using SpaceSim.Localization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using static SpaceSim.Runtime.Menu.MenuKit;

namespace SpaceSim.Runtime.Menu
{
    /// <summary>
    /// Full-screen HORIZON loading screen between the main menu and the flight scene. Lives on a DontDestroyOnLoad object,
    /// loads the scene asynchronously behind an opaque panel and fades out only when the flight scene is really ready:
    /// <see cref="SpaceSimBootstrap"/> found with its rocket and camera, and the procedural surface around the pad settled
    /// (no chunk jobs pending, the displayed chunk set unchanged for <see cref="StableFramesRequired"/> frames), plus a few frames.
    /// A timeout (<see cref="TimeoutSeconds"/>) always lets the player in. Start it with <see cref="Begin"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class LoadingScreen : MonoBehaviour
    {
        /// <summary>The overlay is never shown longer than this, whatever the scene reports [s].</summary>
        public const float TimeoutSeconds = 20f;
        /// <summary>Frames the surface must stay unchanged with no jobs pending before it counts as generated.</summary>
        public const int StableFramesRequired = 10;
        /// <summary>Extra frames after readiness so the first fully generated frame is rendered before the fade.</summary>
        const int SettleFrames = 3;
        const float FadeSeconds = 0.45f;

        /// <summary>True from <see cref="Begin"/> until the overlay is gone (the pause menu ignores Esc meanwhile).</summary>
        public static bool IsShowing { get; private set; }

        static string pendingScene, pendingEyebrow, pendingTitle, pendingGoal;

        static readonly string[] Tips =
        {
            "Совет: Esc в полёте открывает меню паузы — там же сохранение игры и настройки.",
            "Совет: Tab открывает экран экипажа.",
            "Совет: чем выше орбита, тем больше видно — но тем дороже до неё долететь.",
            "Совет: следите за запасом топлива ступени — пустая ступень только мешает.",
            "Совет: на высоте плотность атмосферы падает, а с ней и сопротивление.",
            "Совет: образцы продаются только после доставки на стартовую планету.",
        };

        UIDocument document;
        PanelSettings panel;
        VisualElement root, fill;
        Label step, percent;
        float shownProgress;

        /// <summary>
        /// Puts the loading screen up and loads <paramref name="scene"/> behind it. <paramref name="eyebrow"/>, <paramref name="title"/>
        /// and <paramref name="goal"/> are already localised. Returns false if a load is already running.
        /// </summary>
        public static bool Begin(string scene, string eyebrow, string title, string goal)
        {
            if (IsShowing) return false;
            IsShowing = true;
            pendingScene = scene;
            pendingEyebrow = eyebrow ?? "";
            pendingTitle = title ?? "";
            pendingGoal = goal ?? "";
            var go = new GameObject("LoadingScreen");
            DontDestroyOnLoad(go);
            go.AddComponent<UIDocument>();
            go.AddComponent<LoadingScreen>();
            return true;
        }

        void Awake()
        {
            document = GetComponent<UIDocument>();
            panel = CreatePanel("LoadingPanel (runtime)", opaque: true, sortingOrder: 1000);
            document.panelSettings = panel;
        }

        void OnEnable()
        {
            if (document == null) return;
            Build(document.rootVisualElement);
        }

        void Start() => StartCoroutine(Run());

        void OnDestroy()
        {
            IsShowing = false;
            ReleaseInput();
            if (panel != null) Destroy(panel);
        }

        void Build(VisualElement documentRoot)
        {
            documentRoot.Clear();
            StretchRoot(documentRoot);
            AddStyleSheet(documentRoot, MenuStyleSheet);
            root = Add(documentRoot, El("loading"));
            root.Add(Text("HORIZON  ·  " + Loc.T("КОСМИЧЕСКАЯ ПРОГРАММА"), "loading__brand", "t-mono"));
            var body = Add(root, El("loading__body"));
            body.Add(Text(pendingEyebrow, "loading__eyebrow", "t-mono"));
            body.Add(Text(pendingTitle, "loading__title", "t-strong"));
            if (!string.IsNullOrEmpty(pendingGoal)) body.Add(Text(pendingGoal, "loading__goal"));
            var track = Add(body, El("loading__track"));
            fill = Add(track, El("loading__fill"));
            var status = Add(body, El("loading__status"));
            step = Add(status, Text("", "loading__step", "t-mono"));
            percent = Add(status, Text("", "loading__percent", "t-mono"));
            root.Add(Text(Tips[Random.Range(0, Tips.Length)], "loading__tip"));
            ApplyRootFonts(root);
            SetProgress(0f, "ЗАГРУЗКА СЦЕНЫ");
        }

        void SetProgress(float value, string stepText)
        {
            // Never moves backwards, eases towards the target so the bar does not jump.
            shownProgress = Mathf.Max(shownProgress, Mathf.Lerp(shownProgress, Mathf.Clamp01(value), 0.25f));
            if (fill != null) fill.style.width = Length.Percent(shownProgress * 100f);
            if (percent != null) percent.text = Mathf.RoundToInt(shownProgress * 100f) + " %";
            if (step != null && stepText != null) step.text = Loc.T(stepText);
        }

        IEnumerator Run()
        {
            float start = Time.realtimeSinceStartup;
            // Two frames so the opaque overlay is on screen before the heavy work starts.
            yield return null;
            yield return null;

            AsyncOperation op = SceneManager.LoadSceneAsync(pendingScene);
            if (op == null)
            {
                Debug.LogWarning($"[LoadingScreen] Scene '{pendingScene}' could not be loaded.");
                yield return Fade();
                yield break;
            }
            while (!op.isDone)
            {
                SetProgress(0.6f * Mathf.Clamp01(op.progress / 0.9f), "ЗАГРУЗКА СЦЕНЫ");
                yield return null;
            }

            // The flight scene is active: its bootstrap built everything in Awake; wait for the surface around the pad.
            int stable = 0, lastChunks = -1, lastNodes = -1, lastLevel = -1;
            bool timedOut = false;
            while (true)
            {
                if (Time.realtimeSinceStartup - start > TimeoutSeconds)
                {
                    timedOut = true;
                    break;
                }
                SpaceSimBootstrap boot = FindFirstObjectByType<SpaceSimBootstrap>();
                bool sceneBuilt = boot != null && boot.Body != null && boot.CameraController != null && boot.Session != null;
                if (!sceneBuilt)
                {
                    stable = 0;
                    SetProgress(0.62f, "ПОДГОТОВКА СТАРТОВОГО КОМПЛЕКСА");
                    yield return null;
                    continue;
                }

                // Under the overlay the flight keys must not fire (Space would launch): the input facade treats it as a pause screen.
                FlightInput.PauseMenuOpen = true;
                holdingInput = true;

                ProceduralPlanetGenerator surface = Surface(boot);
                if (surface == null) stable = StableFramesRequired; // reference sphere / imported model: nothing to stream
                else
                {
                    int chunks = surface.DisplayedChunkCount, nodes = surface.NodeCount, level = surface.DeepestDisplayedLevel;
                    bool settled = surface.PendingJobs == 0 && chunks > 0 && chunks == lastChunks && nodes == lastNodes && level == lastLevel;
                    stable = settled ? stable + 1 : 0;
                    lastChunks = chunks;
                    lastNodes = nodes;
                    lastLevel = level;
                }
                if (stable >= StableFramesRequired) break;
                // 60 % … 95 %: half from the settled frames, half creeping with time so it keeps moving while chunks stream.
                float elapsed = Mathf.Clamp01((Time.realtimeSinceStartup - start) / TimeoutSeconds);
                SetProgress(0.6f + 0.35f * Mathf.Max((float)stable / StableFramesRequired, elapsed), "ГЕНЕРАЦИЯ ПОВЕРХНОСТИ");
                yield return null;
            }
            if (timedOut) Debug.LogWarning($"[LoadingScreen] Flight scene not ready after {TimeoutSeconds:0} s — showing it anyway.");

            SetProgress(1f, "ГОТОВО");
            for (int i = 0; i < SettleFrames; i++)
            {
                SetProgress(1f, null);
                yield return null;
            }
            yield return Fade();
        }

        /// <summary>The streaming surface of the active planet, or null when it has none (or the scene is not wired yet).</summary>
        static ProceduralPlanetGenerator Surface(SpaceSimBootstrap boot)
        {
            try
            {
                PlanetVisualController visual = boot.PlanetVisual;
                return visual != null ? visual.Procedural : null;
            }
            catch (System.NullReferenceException)
            {
                return null;
            }
        }

        bool holdingInput;

        void ReleaseInput()
        {
            if (!holdingInput) return;
            holdingInput = false;
            FlightInput.PauseMenuOpen = false;
        }

        IEnumerator Fade()
        {
            ReleaseInput();
            root?.AddToClassList("loading--hidden");
            float end = Time.realtimeSinceStartup + FadeSeconds;
            while (Time.realtimeSinceStartup < end) yield return null;
            Destroy(gameObject);
        }
    }
}
