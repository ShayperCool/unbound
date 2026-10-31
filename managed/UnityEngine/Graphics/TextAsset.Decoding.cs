using System.Text;

namespace UnityEngine
{
    public partial class TextAsset
    {
        static readonly Encoding s_Utf8 = new UTF8Encoding(false, true);
        static readonly Encoding s_Utf16LE = new UnicodeEncoding(false, false, true);
        static readonly Encoding s_Utf16BE = new UnicodeEncoding(true, false, true);
        static readonly Encoding s_Utf32LE = new UTF32Encoding(false, false, true);
        static readonly Encoding s_Utf32BE = new UTF32Encoding(true, false, true);

        static string DecodeTextBytes(byte[] bytes)
        {
            // Native preserves bytes for both .txt and .bytes assets and all constructor paths.
            // The text view detects BOMs. If strict decoding fails, the UTF-8 replacement fallback
            // decodes the whole input, including the BOM (assetclones encoding and Message cases).
            Encoding encoding = s_Utf8;
            int skip = 0;
            if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
            { encoding = s_Utf32BE; skip = 4; }
            else if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
            { encoding = s_Utf32LE; skip = 4; }
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            { encoding = s_Utf16BE; skip = 2; }
            else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            { encoding = s_Utf16LE; skip = 2; }
            else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                skip = 3;
            try { return encoding.GetString(bytes, skip, bytes.Length - skip); }
            catch (DecoderFallbackException) { return Encoding.UTF8.GetString(bytes); }
        }
    }
}
