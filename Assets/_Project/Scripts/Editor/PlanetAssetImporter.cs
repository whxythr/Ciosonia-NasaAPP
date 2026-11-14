using SpaceSim.Runtime;
using UnityEditor;
using UnityEngine;

namespace SpaceSim.Editor
{
    /// <summary>
    /// Import rules for the artists' planet assets:
    /// • Resources/PlanetMaps/*  — equirectangular colour maps of bodies. Read by the CPU surface generator, so they must
    ///   be readable and uncompressed (the generator decodes sRGB itself); no mipmaps, wrap in longitude.
    /// • Models/Planets/*        — planet models from Blender (e.g. Teonan.fbx): axis conversion baked, no cameras,
    ///   lights or animation; textures are searched project-wide, so a map in PlanetMaps is found by the model's material too.
    /// </summary>
    public sealed class PlanetAssetImporter : AssetPostprocessor
    {
        const string MapFolder = "/Resources/" + PlanetAlbedoMapLoader.ResourceFolder;
        const string ModelFolder = "/Models/Planets/";

        static bool IsPlanetMap(string path) => path.Replace('\\', '/').Contains(MapFolder);

        void OnPreprocessTexture()
        {
            if (!IsPlanetMap(assetPath)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 8192;
            importer.wrapModeU = TextureWrapMode.Repeat;
            importer.wrapModeV = TextureWrapMode.Clamp;
        }

        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').Contains(ModelFolder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.bakeAxisConversion = true;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importBlendShapes = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.materialSearch = ModelImporterMaterialSearch.Everywhere;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = false;
            importer.addCollider = false;
        }

        /// <summary>A replaced map takes effect on the next surface rebuild (no editor restart needed).</summary>
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported) Forget(path);
            foreach (string path in deleted) Forget(path);
        }

        static void Forget(string path)
        {
            if (!IsPlanetMap(path)) return;
            PlanetAlbedoMapLoader.Forget(System.IO.Path.GetFileNameWithoutExtension(path));
        }
    }
}
