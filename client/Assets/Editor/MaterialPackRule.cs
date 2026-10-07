using System.IO;
using YooAsset.Editor;

namespace Automatic.Editor
{
    /// <summary>
    /// Packs by folder like <see cref="PackDirectory"/>, but each folder's materials and textures go
    /// into bundles of their own ("Art/Zheng/x.mat" -> "assets_hotres_art_zheng_materials.bundle").
    /// Tweaking a material is the most common art hot update; this keeps it from re-shipping the
    /// meshes and clips next to it (design/08 §4). Textures are split as well so the dependencies
    /// stay one way (models / prefabs -> materials -> textures) instead of forming a cycle.
    /// </summary>
    [DisplayName("Folder, materials and textures apart")]
    public class PackDirectoryMaterialsApart : IBundlePackRule
    {
        private static readonly string[] TextureExtensions = { ".png", ".jpg", ".tga", ".psd", ".exr" };

        BundlePackRuleResult IBundlePackRule.GetPackRuleResult(BundlePackRuleData data)
        {
            var bundleName = Path.GetDirectoryName(data.AssetPath);
            var ext = Path.GetExtension(data.AssetPath).ToLowerInvariant();
            if (ext == ".mat")
                bundleName += "_materials";
            else if (System.Array.IndexOf(TextureExtensions, ext) >= 0)
                bundleName += "_textures";
            return new BundlePackRuleResult(bundleName, DefaultBundlePackRule.AssetBundleFileExtension);
        }
    }
}
