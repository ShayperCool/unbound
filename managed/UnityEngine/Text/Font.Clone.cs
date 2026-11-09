using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class Font : Instantiation.ICloneable
    {
        /// <summary>Serialized font data is copied, material and texture references are shared.
        /// Dynamic fonts report the Editor's unsupported-clone diagnostic (assetinstantiate).</summary>
        Object Instantiation.ICloneable.CloneObject()
        {
            if (!Compat.AssetCloneResourcesMeasured)
                throw new System.NotSupportedException("Font instantiation is measured only for Unity 6000.0.71f1.");
            if (m_Dynamic)
                Debug.LogError("Font Error: Cloning a dynamic font is not supported and may result in incorrect font rendering.");
            var copy = new Font
            {
                m_FontSize = m_FontSize,
                m_Ascent = m_Ascent,
                m_Descent = m_Descent,
                m_LineSpacing = m_LineSpacing,
                m_FontNames = (string[])m_FontNames.Clone(),
                m_Dynamic = m_Dynamic,
                m_ShouldRoundAdvanceValue = m_ShouldRoundAdvanceValue,
                m_CharacterPadding = m_CharacterPadding,
                m_RenderingMode = m_RenderingMode,
                m_AsciiStartOffset = m_AsciiStartOffset,
                m_Tracking = m_Tracking,
                m_FallbackGuids = (string[])m_FallbackGuids.Clone(),
                m_Source = m_Source,
                m_UaEditorResourceId = m_UaEditorResourceId,
            };
            copy.m_CharacterRects.AddRange(m_CharacterRects);
            copy.m_FallbackFonts.AddRange(m_FallbackFonts);
            copy.m_Material = m_Material;
            copy.m_Texture = m_Texture;
            if (m_Source != null) copy.m_Atlas = new FontAtlas(copy);
            return copy;
        }
    }
}
