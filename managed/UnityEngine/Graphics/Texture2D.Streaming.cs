using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// Mipmap streaming's answers for a texture, as the Unity 6000.0.71f1 Editor gives them in Play
    /// mode (tests/projects/mipstreaming): a texture streams when its importer (or its asset file)
    /// says so. A texture that does not stream, or streams without mipmaps, keeps -1 for its
    /// requested, minimum, calculated and desired levels and ignores a requested level; a streaming
    /// one starts with a minimum level of 8 whatever its size (back to 8 when cleared), takes a
    /// requested or minimum level clamped to 0 and to QualitySettings.streamingMipmapsMaxLevelReduction
    /// and its last mip, and reports a
    /// requested level other than the loaded one as not loaded. With streaming active, a texture
    /// the streaming system has not seen drawn is calculated at the maximum level reduction (not
    /// clamped to its mips); the desired level is the requested one when set (whatever the
    /// minimum), else the calculated one capped by the minimum. A copy (Instantiate) streams with its source's settings but no requested level, is
    /// not one the streaming system calculates, and reports its source's level as loaded. The
    /// level a drawn texture is calculated at from its size on screen is not simulated (a
    /// notice: the maximum level reduction stands in).
    /// </summary>
    public sealed partial class Texture2D
    {
        bool m_StreamingRead, m_Streaming, m_StreamsLevels, m_StreamingCopy;
        int m_StreamingPriority;
        int m_RequestedMipmapLevel = -1, m_MinimumMipmapLevel = -1, m_LoadedMipmapLevel;

        /// <summary>A streaming texture's minimum level before a script sets one (any size measured: 3, 7 and 11 mips).</summary>
        const int k_StreamingMinimumLevel = 8;

        void ReadStreaming()
        {
            if (m_StreamingRead) return;
            m_StreamingRead = true;
            string path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(path) && UnityEditor.AssetImporter.GetAtPath(path) is UnityEditor.TextureImporter importer)
                SetStreaming(importer.streamingMipmaps, importer.streamingMipmapsPriority);
        }

        /// <summary>The streaming settings an imported or deserialized texture starts with.</summary>
        internal void SetStreaming(bool streaming, int priority)
        {
            m_StreamingRead = true;
            m_Streaming = streaming;
            m_StreamingPriority = priority;
            m_StreamsLevels = streaming && mipmapCount > 1;
            m_RequestedMipmapLevel = -1;
            m_MinimumMipmapLevel = m_StreamsLevels ? k_StreamingMinimumLevel : -1;
        }

        /// <summary>A copy's streaming state: its source's settings, no requested level, the source's
        /// level as loaded.</summary>
        void CopyStreamingTo(Texture2D copy)
        {
            ReadStreaming();
            copy.SetStreaming(m_Streaming, m_StreamingPriority);
            copy.m_StreamingCopy = true;
            bool calculated = Calculated();
            int level = calculated ? desiredMipmapLevel : m_LoadedMipmapLevel;
            // Measured: a copy of a calculated source whose requested, calculated and desired levels
            // agree. Another level of such a source, or a source that is itself a copy or not
            // calculated with a level above 0, is not.
            if (calculated ? calculatedMipmapLevel != level || (m_RequestedMipmapLevel >= 0 && m_RequestedMipmapLevel != level) : level > 0)
                Unbound.Runtime.Stubs.Ignored("Texture2D.loadedMipmapLevel of a copy", "a copy reports its source's desired level (for a source not calculated, its loaded level) as loaded (measured where the source's requested, calculated and desired levels were all the same)");
            copy.m_LoadedMipmapLevel = Mathf.Max(0, level);
        }

        public bool streamingMipmaps { get { ReadStreaming(); return m_Streaming; } }

        public int streamingMipmapsPriority { get { ReadStreaming(); return m_StreamingPriority; } }

        public int requestedMipmapLevel
        {
            get { ReadStreaming(); return m_RequestedMipmapLevel; }
            set
            {
                ReadStreaming();
                if (!m_StreamsLevels) return;
                m_RequestedMipmapLevel = Mathf.Clamp(value, 0, MostReducedLevel());
            }
        }

        public void ClearRequestedMipmapLevel()
        {
            ReadStreaming();
            if (m_StreamsLevels) m_RequestedMipmapLevel = -1;
        }

        public int minimumMipmapLevel
        {
            get { ReadStreaming(); return m_MinimumMipmapLevel; }
            set
            {
                ReadStreaming();
                if (m_StreamsLevels) m_MinimumMipmapLevel = Mathf.Clamp(value, 0, MostReducedLevel());
            }
        }

        /// <summary>Back to the level a streaming texture starts with (8, as the Editor reads it back).</summary>
        public void ClearMinimumMipmapLevel()
        {
            ReadStreaming();
            if (m_StreamsLevels) m_MinimumMipmapLevel = k_StreamingMinimumLevel;
        }

        /// <summary>The most a requested or minimum level can reduce a texture: the quality level's
        /// maximum level reduction, at most its last mip.</summary>
        int MostReducedLevel() => Mathf.Min(QualitySettings.streamingMipmapsMaxLevelReduction, Mathf.Max(0, mipmapCount - 1));

        /// <summary>Whether the streaming system calculates the texture's level now.</summary>
        bool Calculated()
        {
            ReadStreaming();
            if (m_Streaming && !m_StreamsLevels && QualitySettings.streamingMipmapsActive)
                Unbound.Runtime.Stubs.Ignored("Texture2D levels of a streaming texture without mipmaps", "while streaming is active they are not measured (the Editor's answers vary between runs): -1, as before streaming is active");
            return m_StreamsLevels && !m_StreamingCopy && QualitySettings.streamingMipmapsActive;
        }

        int CalculatedLevel()
        {
            Unbound.Runtime.Stubs.Ignored("Texture2D.calculatedMipmapLevel", "a drawn texture's level from its size on screen is not simulated: every streaming texture is calculated at the maximum level reduction, as one not drawn is");
            return QualitySettings.streamingMipmapsMaxLevelReduction;
        }

        public int calculatedMipmapLevel => Calculated() ? CalculatedLevel() : -1;

        public int desiredMipmapLevel =>
            !Calculated() ? -1 : m_RequestedMipmapLevel >= 0 ? m_RequestedMipmapLevel : Mathf.Min(CalculatedLevel(), m_MinimumMipmapLevel);

        public int loadingMipmapLevel { get { ReadStreaming(); return m_LoadedMipmapLevel; } }

        public int loadedMipmapLevel { get { ReadStreaming(); return m_LoadedMipmapLevel; } }

        public bool IsRequestedMipmapLevelLoaded()
        {
            ReadStreaming();
            return m_RequestedMipmapLevel < 0 || m_RequestedMipmapLevel == m_LoadedMipmapLevel;
        }
    }
}
