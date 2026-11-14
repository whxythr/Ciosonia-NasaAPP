using System.Collections.Generic;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Small preview images of catalog modules for the hangar menu: the module's own model (library or procedural, exactly as on
    /// the rocket) rendered once by a private camera into a <see cref="Size"/>² RenderTexture, cached by id.
    /// <para>Nothing exists until the first <see cref="Get"/>: then a hidden renderer (<see cref="ModuleThumbnailRenderer"/>)
    /// draws at most <see cref="PerFrame"/> new images per frame, outside the render loop. The parts stand on an isolated layer and
    /// are active only for their own render. When no image has been asked for during <see cref="IdleSeconds"/> (the hangar is
    /// closed), every texture, material and object is released and the renderer removes itself.</para>
    /// </summary>
    public static class ModuleThumbnails
    {
        public const int Size = 128;
        /// <summary>Layer of the preview parts (31 is the orbital map's).</summary>
        public const int Layer = 30;
        public const int PerFrame = 2;
        public const float IdleSeconds = 20f;

        static readonly Dictionary<string, RenderTexture> cache = new Dictionary<string, RenderTexture>();
        static readonly List<string> queue = new List<string>();
        static readonly HashSet<string> queued = new HashSet<string>();
        static ModuleThumbnailRenderer renderer;
        internal static float LastRequest;

        /// <summary>Preview of the module, or null while it is not ready yet (the caller draws a placeholder).</summary>
        public static Texture Get(string moduleId)
        {
            if (string.IsNullOrEmpty(moduleId) || !Application.isPlaying) return null;
            LastRequest = Time.unscaledTime;
            if (cache.TryGetValue(moduleId, out RenderTexture rt))
            {
                if (rt != null && rt.IsCreated()) return rt;
                cache.Remove(moduleId); // lost with the device or destroyed: draw it again
                if (rt != null) Object.Destroy(rt);
            }
            if (ModuleCatalog.Find(moduleId) == null) return null;
            if (queued.Add(moduleId)) queue.Add(moduleId);
            if (renderer == null)
            {
                var go = new GameObject("ModuleThumbnails") { hideFlags = HideFlags.DontSave };
                renderer = go.AddComponent<ModuleThumbnailRenderer>();
            }
            return null;
        }

        /// <summary>Frees every image now (they are drawn again on demand).</summary>
        public static void ReleaseAll()
        {
            if (renderer != null)
            {
                ModuleThumbnailRenderer r = renderer;
                renderer = null;
                Object.Destroy(r.gameObject); // OnDestroy frees the rig
            }
            ClearCache();
        }

        internal static void ClearCache()
        {
            foreach (RenderTexture rt in cache.Values)
            {
                if (rt == null) continue;
                rt.Release();
                Object.Destroy(rt);
            }
            cache.Clear();
            queue.Clear();
            queued.Clear();
        }

        internal static bool TakeNext(out string id)
        {
            id = null;
            if (queue.Count == 0) return false;
            id = queue[0];
            queue.RemoveAt(0);
            queued.Remove(id);
            return true;
        }

        internal static void Store(string id, RenderTexture rt)
        {
            if (cache.TryGetValue(id, out RenderTexture old) && old != null && old != rt)
            {
                old.Release();
                Object.Destroy(old);
            }
            cache[id] = rt;
        }

        internal static void Detach(ModuleThumbnailRenderer r)
        {
            if (renderer != r) return;
            renderer = null;
            ClearCache();
        }
    }
}
