using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// Mipmap streaming's answers for a texture, as the Unity 6000.0.71f1 Editor gives them in Play
    /// mode (tests/projects/mipstreaming): a texture streams when its importer says so; every mip
    /// is loaded (loading and loaded level 0). A texture that does not stream keeps -1 for its
    /// requested, minimum, calculated and desired levels and ignores a requested level; a streaming
    /// one starts with a minimum level of 8, takes a requested level clamped to
    /// QualitySettings.streamingMipmapsMaxLevelReduction (3 reads back 2), and reports a requested
    /// level other than the loaded one as not loaded. The levels the streaming system calculates
    /// with streaming active are not simulated (a notice).
    /// </summary>
    public sealed partial class Texture2D
    {
        bool m_StreamingRead, m_Streaming;
        int m_StreamingPriority;
        int m_RequestedMipmapLevel = -1, m_MinimumMipmapLevel = -1;

        void ReadStreaming()
        {
            if (m_StreamingRead) return;
            m_StreamingRead = true;
            string path = UnityEditor.AssetDatabase.GetAssetPath(this);
            if (!string.IsNullOrEmpty(path) && UnityEditor.AssetImporter.GetAtPath(path) is UnityEditor.TextureImporter importer)
                SetStreaming(importer.streamingMipmaps, importer.streamingMipmapsPriority);
        }

        /// <summary>The importer's streaming settings, as an imported texture starts with them.</summary>
        internal void SetStreaming(bool streaming, int priority)
        {
            m_StreamingRead = true;
            m_Streaming = streaming;
            m_StreamingPriority = priority;
            m_MinimumMipmapLevel = streaming ? 8 : -1;
        }

        public bool streamingMipmaps { get { ReadStreaming(); return m_Streaming; } }

        public int streamingMipmapsPriority { get { ReadStreaming(); return m_StreamingPriority; } }

        public int requestedMipmapLevel
        {
            get { ReadStreaming(); return m_RequestedMipmapLevel; }
            set
            {
                ReadStreaming();
                if (!m_Streaming) return;
                int most = Mathf.Min(QualitySettings.streamingMipmapsMaxLevelReduction, Mathf.Max(0, mipmapCount - 1));
                m_RequestedMipmapLevel = Mathf.Clamp(value, 0, most);
            }
        }

        public void ClearRequestedMipmapLevel()
        {
            ReadStreaming();
            if (m_Streaming) m_RequestedMipmapLevel = -1;
        }

        public int minimumMipmapLevel
        {
            get { ReadStreaming(); return m_MinimumMipmapLevel; }
            set
            {
                ReadStreaming();
                if (m_Streaming) m_MinimumMipmapLevel = value;
            }
        }

        public void ClearMinimumMipmapLevel()
        {
            ReadStreaming();
            if (m_Streaming)
                Unbound.Runtime.Stubs.Ignored("Texture2D.ClearMinimumMipmapLevel", "the level a streaming texture's cleared minimum reads back is not measured: it keeps its value");
        }

        int StreamedLevel(string member)
        {
            ReadStreaming();
            if (!m_Streaming || !QualitySettings.streamingMipmapsActive) return -1;
            Unbound.Runtime.Stubs.Ignored("Texture2D." + member, "the mip level the texture streaming system calculates is not simulated: it answers the requested level");
            return m_RequestedMipmapLevel;
        }

        public int calculatedMipmapLevel => StreamedLevel("calculatedMipmapLevel");

        public int desiredMipmapLevel => StreamedLevel("desiredMipmapLevel");

        // The Editor keeps every mip loaded: both levels read 0 at every stage of
        // tests/projects/mipstreaming, streaming on or off, active or not, with a requested level
        // it reports as not loaded.
        [Unbound.EditorFaithful("the Editor keeps every mip loaded: 0 at every stage of tests/projects/mipstreaming")]
        public int loadingMipmapLevel => 0;

        [Unbound.EditorFaithful("the Editor keeps every mip loaded: 0 at every stage of tests/projects/mipstreaming")]
        public int loadedMipmapLevel => 0;

        public bool IsRequestedMipmapLevelLoaded()
        {
            ReadStreaming();
            return m_RequestedMipmapLevel < 0 || m_RequestedMipmapLevel == loadedMipmapLevel;
        }
    }
}
