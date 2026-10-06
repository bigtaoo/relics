using System.Linq;
using UnityEditor;

namespace Automatic.Editor
{
    /// <summary>
    /// Import rules for character FBX files exported by tools/art/blender (design/08 §1):
    /// Generic rig, no imported materials (units use the Relics/Toon material),
    /// Blender's "rig|clip" take names trimmed to "clip", and "idle" looping.
    /// </summary>
    public class ArtPostprocessor : AssetPostprocessor
    {
        private const string Root = "Assets/HotRes/Art/";
        private static readonly string[] LoopClips = { "idle" };

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            importer.importAnimation = true;
            importer.importCameras = false;
            importer.importLights = false;
        }

        private void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Root)) return;
            var importer = (ModelImporter)assetImporter;
            importer.clipAnimations = importer.defaultClipAnimations.Select(c =>
            {
                c.name = c.name.Split('|').Last();
                c.loopTime = LoopClips.Contains(c.name);
                return c;
            }).ToArray();
        }
    }
}
