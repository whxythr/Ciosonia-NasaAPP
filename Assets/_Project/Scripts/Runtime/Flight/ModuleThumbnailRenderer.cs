using SpaceSim.Vehicles;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Draws the queued <see cref="ModuleThumbnails"/>: builds one module on <see cref="ModuleThumbnails.Layer"/>, frames it with a
    /// disabled camera and renders it into a RenderTexture. In URP (Unity 6) the render goes through
    /// <c>RenderPipeline.SubmitRenderRequest</c> with a <c>StandardRequest</c> — the supported way to render a camera outside the
    /// pipeline's own loop; without a scriptable pipeline it falls back to <c>Camera.Render</c>. Runs in LateUpdate, never inside
    /// another camera's render. Removes itself (and everything it made) when nobody has asked for an image for a while.
    /// </summary>
    public sealed class ModuleThumbnailRenderer : MonoBehaviour
    {
        Camera cam;
        Light key, fill;
        Transform stage;
        RocketPalette palette;
        static readonly Vector3 StagePosition = new Vector3(0f, 20000f, 0f);

        void Awake()
        {
            stage = new GameObject("ThumbnailStage").transform;
            stage.SetParent(transform, false);
            stage.position = StagePosition;
            stage.gameObject.SetActive(false);

            var camGo = new GameObject("ThumbnailCamera");
            camGo.transform.SetParent(transform, false);
            camGo.layer = ModuleThumbnails.Layer;
            cam = camGo.AddComponent<Camera>();
            cam.enabled = false; // renders only on request
            cam.cullingMask = 1 << ModuleThumbnails.Layer;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.allowHDR = false;
            cam.fieldOfView = 28f;

            key = MakeLight("Key", new Color(1f, 0.97f, 0.92f), 1.25f, Quaternion.Euler(35f, 145f, 0f));
            fill = MakeLight("Fill", new Color(0.6f, 0.7f, 0.85f), 0.45f, Quaternion.Euler(-10f, -40f, 0f));

            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit == null) lit = Shader.Find("Standard");
            palette = new RocketPalette(lit);
        }

        Light MakeLight(string name, Color color, float intensity, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.rotation = rotation;
            Light l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = color;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            l.cullingMask = 1 << ModuleThumbnails.Layer;
            l.enabled = false; // on only while a thumbnail renders: the scene never sees these lights
            return l;
        }

        void LateUpdate()
        {
            if (Time.unscaledTime - ModuleThumbnails.LastRequest > ModuleThumbnails.IdleSeconds)
            {
                ModuleThumbnails.ReleaseAll();
                return;
            }
            for (int i = 0; i < ModuleThumbnails.PerFrame && ModuleThumbnails.TakeNext(out string id); i++)
            {
                RenderTexture rt = Render(id);
                if (rt != null) ModuleThumbnails.Store(id, rt);
            }
        }

        RenderTexture Render(string id)
        {
            stage.gameObject.SetActive(true);
            RocketModel model = RocketModelBuilder.BuildPreview(id, stage, palette, ModuleVisualLibrary.Active);
            RenderTexture rt = null;
            try
            {
                if (model == null || model.Root == null) return null;
                SetLayer(model.Root, ModuleThumbnails.Layer);
                if (!WorldBounds(model.Root, out Bounds b)) return null;

                float radius = Mathf.Max(0.05f, b.extents.magnitude);
                float distance = radius / Mathf.Sin(0.5f * cam.fieldOfView * Mathf.Deg2Rad) * 1.02f;
                Quaternion view = Quaternion.Euler(18f, 215f, 0f);
                cam.transform.SetPositionAndRotation(b.center - view * Vector3.forward * distance, view);
                cam.nearClipPlane = Mathf.Max(0.01f, distance - 1.5f * radius);
                cam.farClipPlane = distance + 1.5f * radius;

                rt = new RenderTexture(ModuleThumbnails.Size, ModuleThumbnails.Size, 24, RenderTextureFormat.ARGB32)
                {
                    name = "Thumbnail " + id, antiAliasing = 4, hideFlags = HideFlags.DontSave
                };
                rt.Create();
                key.enabled = fill.enabled = true;
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                }
                return rt;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[SpaceSim] Thumbnail of " + id + " failed: " + e.Message);
                if (rt != null)
                {
                    rt.Release();
                    Destroy(rt);
                }
                return null;
            }
            finally
            {
                key.enabled = fill.enabled = false;
                // Off before the frame's cameras render, gone at the end of the frame.
                stage.gameObject.SetActive(false);
                // Destroy() takes effect only at the end of the frame: hide the model now, or the next thumbnail rendered in this
                // frame would show it too (a large neighbour's hull appeared as wedges in the corners of a small part's image).
                if (model != null && model.Root != null) model.Root.gameObject.SetActive(false);
                model?.Destroy();
            }
        }

        static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform child in t) SetLayer(child, layer);
        }

        static bool WorldBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
            {
                if (!any)
                {
                    bounds = r.bounds;
                    any = true;
                }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        void OnDestroy()
        {
            ModuleThumbnails.Detach(this);
            palette?.Dispose();
            palette = null;
        }
    }
}
