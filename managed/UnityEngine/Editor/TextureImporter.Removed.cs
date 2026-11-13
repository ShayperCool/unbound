using System;
using Unbound.Runtime;

namespace UnityEditor
{
    public partial class TextureImporter
    {
        const string RemovedSettings = "removed texture importer settings retain their measured constants and ignore writes (defaultstate, 6000.0.71f1)";

        [Obsolete("generateMipsInLinearSpace Property deprecated. Mipmaps are always generated in linear space.")]
        public bool generateMipsInLinearSpace
        {
            [Unbound.EditorFaithful(RemovedSettings)] get => RemovedBool("UnityEditor.TextureImporter.generateMipsInLinearSpace");
            [Unbound.EditorFaithful(RemovedSettings)] set => IgnoreRemovedWrite("UnityEditor.TextureImporter.generateMipsInLinearSpace");
        }

        [Obsolete("correctGamma Property deprecated. Mipmaps are always generated in linear space.")]
        public bool correctGamma
        {
            [Unbound.EditorFaithful(RemovedSettings)] get => RemovedBool("UnityEditor.TextureImporter.correctGamma");
            [Unbound.EditorFaithful(RemovedSettings)] set => IgnoreRemovedWrite("UnityEditor.TextureImporter.correctGamma");
        }

        [Obsolete("Support for packing sprites through spritePackingTag has been removed. Please use SpriteAtlas instead.")]
        public string spritePackingTag
        {
            [Unbound.EditorFaithful(RemovedSettings)] get
            {
                if (Compat.RemovedEditorSettingsMeasured) return "";
                Stubs.NotImplemented("UnityEditor.TextureImporter.spritePackingTag"); return null;
            }
            [Unbound.EditorFaithful(RemovedSettings)] set => IgnoreRemovedWrite("UnityEditor.TextureImporter.spritePackingTag");
        }

        static bool RemovedBool(string member)
        {
            if (Compat.RemovedEditorSettingsMeasured) return true;
            Stubs.NotImplemented(member); return false;
        }

        static void IgnoreRemovedWrite(string member)
        {
            if (!Compat.RemovedEditorSettingsMeasured) Stubs.NotImplemented(member);
        }
    }
}
