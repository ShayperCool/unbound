using Unbound.Runtime;

namespace UnityEngine
{
    public sealed partial class Font : Instantiation.ICloneable
    {
        /// <summary>
        /// A clone copies the font's serialized data: metrics, names, character rectangles,
        /// rendering settings and fallbacks, keeping references to other objects (a custom font's
        /// material and texture, fallback fonts). A dynamic font's clone renders from the same
        /// read-only face into its own glyph texture and font material, so requests on either
        /// font never change the other's texture; glyphs requested on the source are not copied
        /// (they are runtime cache, rendered again on request). Texture-rebuild callbacks are not
        /// copied.
        /// </summary>
        Object Instantiation.ICloneable.CloneObject()
        {
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
                m_AsciiStartOffset = m_AsciiStartOffset,
                m_Tracking = m_Tracking,
                m_FallbackGuids = (string[])m_FallbackGuids.Clone(),
                m_Source = m_Source,
                m_UaEditorResourceId = m_UaEditorResourceId,
            };
            copy.m_CharacterRects.AddRange(m_CharacterRects);
            copy.m_FallbackFonts.AddRange(m_FallbackFonts);
            if (m_Source != null)
            {
                copy.EnsureMaterial();
            }
            else
            {
                copy.m_Material = m_Material;
                copy.m_Texture = m_Texture;
            }
            return copy;
        }
    }
}
