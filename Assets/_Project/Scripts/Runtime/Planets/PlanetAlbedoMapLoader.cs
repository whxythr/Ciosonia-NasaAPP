using System.Collections.Generic;
using SpaceSim.Procedural;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Loads the artists' colour maps of bodies (equirectangular images, Resources/PlanetMaps/&lt;key&gt;) into the Core
    /// registry <see cref="SurfaceAlbedoMaps"/>. Must run on the main thread before a surface sampler is created —
    /// <see cref="PlanetRuntime"/> calls it for its own planet. The import settings (readable, uncompressed) are set by
    /// the editor's PlanetMapImporter. A missing image is not an error: the body keeps its procedural colours.
    /// </summary>
    public static class PlanetAlbedoMapLoader
    {
        public const string ResourceFolder = "PlanetMaps/";

        /// <summary>Keys already tried (found or not), so a missing image is looked up and reported once.</summary>
        static readonly HashSet<string> tried = new HashSet<string>();

        /// <summary>Makes sure the map named in <paramref name="settings"/> is registered (if the image exists).</summary>
        public static bool Ensure(PlanetGenerationSettings settings) => settings != null && Ensure(settings.albedoMap);

        public static bool Ensure(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (SurfaceAlbedoMaps.IsRegistered(key)) return true;
            if (!tried.Add(key)) return false;

            var texture = Resources.Load<Texture2D>(ResourceFolder + key);
            if (texture == null)
            {
                Debug.Log("[SpaceSim] No colour map Resources/" + ResourceFolder + key + " — procedural colours are used.");
                return false;
            }
            if (!texture.isReadable)
            {
                Debug.LogWarning("[SpaceSim] Colour map " + key + " is not readable (enable Read/Write in its import settings) — procedural colours are used.", texture);
                return false;
            }
            Color32[] pixels = texture.GetPixels32();
            var bytes = new byte[pixels.Length * 4];
            for (int i = 0; i < pixels.Length; i++)
            {
                bytes[i * 4 + 0] = pixels[i].r;
                bytes[i * 4 + 1] = pixels[i].g;
                bytes[i * 4 + 2] = pixels[i].b;
                bytes[i * 4 + 3] = pixels[i].a;
            }
            SurfaceAlbedoMaps.Register(key, SurfaceAlbedoMap.FromSrgb32(texture.width, texture.height, bytes));
            Debug.Log("[SpaceSim] Colour map " + key + " loaded (" + texture.width + "×" + texture.height + ").");
            return true;
        }

        /// <summary>Forgets a map (e.g. after the image was replaced in the editor), so the next Ensure reloads it.</summary>
        public static void Forget(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            tried.Remove(key);
            SurfaceAlbedoMaps.Unregister(key);
        }
    }
}
