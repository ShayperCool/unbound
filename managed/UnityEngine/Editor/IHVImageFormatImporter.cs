using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// The importer of DDS, KTX and ASTC files, which keep their own format and mips: its settings
    /// are its .meta section's (`textureSettings`, `isReadable`, `sRGBTexture`, streaming and
    /// mipmap-limit settings), defaults as the Editor writes them for a new file (matches the
    /// Unity 6000.0.71f1 Editor, tests/projects/ihvsources importer.*, meta.*).
    /// </summary>
    public sealed partial class IHVImageFormatImporter : AssetImporter
    {
        public IHVImageFormatImporter() { }

        int Int(string path, int fallback) => MetaSettings.Int(m_Settings, path, fallback);

        void SetInt(string path, long value)
        {
            if (m_Settings == null) return;
            MetaSettings.SetInt(m_Settings, path, value);
            Changed();
        }

        public bool isReadable
        {
            get { CheckImporterState("get_isReadable"); return Int("isReadable", 0) != 0; }
            set { CheckImporterState("set_isReadable"); SetInt("isReadable", value ? 1 : 0); }
        }

        public FilterMode filterMode
        {
            get { CheckImporterState("get_filterMode"); return (FilterMode)Mathf.Clamp(Int("textureSettings.filterMode", 1), 0, 2); }
            set { CheckImporterState("set_filterMode"); SetInt("textureSettings.filterMode", (int)value); }
        }

        /// <summary>The U wrap mode (the setter sets all three, as TextureImporter.wrapMode does).</summary>
        public TextureWrapMode wrapMode
        {
            get { CheckImporterState("get_wrapMode"); return (TextureWrapMode)Int("textureSettings.wrapU", 0); }
            set
            {
                CheckImporterState("set_wrapMode");
                SetInt("textureSettings.wrapU", (int)value);
                SetInt("textureSettings.wrapV", (int)value);
                SetInt("textureSettings.wrapW", (int)value);
            }
        }

        public TextureWrapMode wrapModeU
        {
            get { CheckImporterState("get_wrapModeU"); return (TextureWrapMode)Int("textureSettings.wrapU", 0); }
            set { CheckImporterState("set_wrapModeU"); SetInt("textureSettings.wrapU", (int)value); }
        }

        public TextureWrapMode wrapModeV
        {
            get { CheckImporterState("get_wrapModeV"); return (TextureWrapMode)Int("textureSettings.wrapV", 0); }
            set { CheckImporterState("set_wrapModeV"); SetInt("textureSettings.wrapV", (int)value); }
        }

        public TextureWrapMode wrapModeW
        {
            get { CheckImporterState("get_wrapModeW"); return (TextureWrapMode)Int("textureSettings.wrapW", 0); }
            set { CheckImporterState("set_wrapModeW"); SetInt("textureSettings.wrapW", (int)value); }
        }

        public bool streamingMipmaps
        {
            get { CheckImporterState("get_streamingMipmaps"); return Int("streamingMipmaps", 0) != 0; }
            set { CheckImporterState("set_streamingMipmaps"); SetInt("streamingMipmaps", value ? 1 : 0); }
        }

        public int streamingMipmapsPriority
        {
            get { CheckImporterState("get_streamingMipmapsPriority"); return Int("streamingMipmapsPriority", 0); }
            set { CheckImporterState("set_streamingMipmapsPriority"); SetInt("streamingMipmapsPriority", value); }
        }

        public bool ignoreMipmapLimit
        {
            get { CheckImporterState("get_ignoreMipmapLimit"); return Int("ignoreMipmapLimit", 0) != 0; }
            set { CheckImporterState("set_ignoreMipmapLimit"); SetInt("ignoreMipmapLimit", value ? 1 : 0); }
        }

        public string mipmapLimitGroupName
        {
            get { CheckImporterState("get_mipmapLimitGroupName"); return MetaSettings.Find(m_Settings, "mipmapLimitGroupName")?.AsString() ?? ""; }
            set
            {
                CheckImporterState("set_mipmapLimitGroupName");
                if (m_Settings == null) return;
                MetaSettings.Set(m_Settings, "mipmapLimitGroupName", value ?? "");
                Changed();
            }
        }
    }
}
