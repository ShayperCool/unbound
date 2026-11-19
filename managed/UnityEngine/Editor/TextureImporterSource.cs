using System.Text.Json;
using Unbound.Runtime;

namespace UnityEditor
{
    public sealed partial class TextureImporter
    {
        /// <summary>The source image as the import read it: its size before non-power-of-two
        /// scaling and the max-size clamp, whether some pixel is not opaque, the imported size and
        /// the block-compressed format it imported to (none when stored uncompressed): the import's
        /// output, as the Editor's SerializedObject shows it under <c>m_Output</c> (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/importerserialized).</summary>
        internal (int Width, int Height, bool Alpha, int ImportedWidth, int ImportedHeight, int? CompressedFormat)? SourceInfo()
        {
            var guid = m_Guid;
            if (string.IsNullOrEmpty(guid)) return null;
            string json;
            try { json = Native.Call("texture-source", JsonSerializer.Serialize(new { guid })); }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogWarning($"Unbound: the source of {assetPath} could not be read: {e.Message}");
                return null;
            }
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            return (r.GetProperty("width").GetInt32(), r.GetProperty("height").GetInt32(), r.GetProperty("alpha").GetBoolean(),
                r.GetProperty("importedWidth").GetInt32(), r.GetProperty("importedHeight").GetInt32(),
                r.TryGetProperty("compressedFormat", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : (int?)null);
        }

        /// <summary>The source image's size (the 2D sprite data provider sizes a Single sprite by it).</summary>
        internal void GetWidthAndHeight(ref int width, ref int height)
        {
            CheckImporterState("GetWidthAndHeight");
            if (SourceInfo() is { } s) { width = s.Width; height = s.Height; }
        }

        public void GetSourceTextureWidthAndHeight(out int width, out int height)
        {
            CheckImporterState("GetSourceTextureWidthAndHeight");
            width = 0;
            height = 0;
            if (SourceInfo() is { } s) { width = s.Width; height = s.Height; }
        }

        public bool DoesSourceTextureHaveAlpha()
        {
            CheckImporterState("DoesSourceTextureHaveAlpha");
            return SourceInfo() is { } s && s.Alpha;
        }
    }
}
