using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using Unbound.Runtime;
using UnityEngine.Rendering;

namespace UnityEngine
{
    /// <summary>
    /// Screenshots of the game view (measured in the Play mode Editor: tests/projects/screencapture).
    /// The texture and render texture forms read the screen when they are called: its stored 8-bit
    /// bytes (sRGB-encoded in Linear color space) with their alpha. <c>CaptureScreenshotAsTexture</c>
    /// returns a readable RGBA32 texture with the linear flag and one mip; superSize N renders the
    /// screen cameras again N * N times at the screen's size, tile (i, j) sampling the screen at
    /// (x + i / N, y + j / N), and interleaves the tiles: pixel x * N + i is tile i's pixel x.
    /// <c>CaptureScreenshotIntoRenderTexture</c> draws the screen into the target at one texel per
    /// pixel from its corner (cropped to the smaller size, the rest kept; decoded into linear and float
    /// targets). <c>CaptureScreenshot</c> writes the frame's final image, after the end-of-frame
    /// callbacks, as an 8-bit RGB PNG (whatever the name's extension; the last request of a frame
    /// wins; relative names are under the working directory). Stereo modes capture the screen.
    /// </summary>
    public static partial class ScreenCapture
    {
        public enum StereoScreenCaptureMode
        {
            LeftEye = 1,
            RightEye = 2,
            BothEyes = 3,
            MotionVectors = 4,
        }

        static string s_PendingPath;
        static int s_PendingSize;
        static bool s_WarnedOverlay;

        public static void CaptureScreenshot(string filename) => CaptureScreenshot(filename, 1);

        public static void CaptureScreenshot(string filename, StereoScreenCaptureMode stereoCaptureMode) => CaptureScreenshot(filename, 1);

        public static void CaptureScreenshot(string filename, int superSize)
        {
            if (superSize < 1)
            {
                Debug.LogError("CaptureScreenshot() the superSize argument must be greater than or equal to 1.");
                return;
            }
            if (filename == null) throw new ArgumentNullException(nameof(filename));
            s_PendingPath = Path.GetFullPath(filename);
            s_PendingSize = superSize;
        }

        public static Texture2D CaptureScreenshotAsTexture() => CaptureScreenshotAsTexture(1);

        public static Texture2D CaptureScreenshotAsTexture(StereoScreenCaptureMode stereoCaptureMode) => CaptureScreenshotAsTexture(1);

        public static Texture2D CaptureScreenshotAsTexture(int superSize)
        {
            if (superSize < 1)
            {
                Debug.LogAssertion("Assertion failed on expression: 'superSize > 0'");
                if (superSize < 0)
                {
                    Debug.LogAssertion("Image invalid width!");
                    Debug.LogAssertion("Image invalid height!");
                }
                Debug.LogError("Texture must have width greater than 0.");
                Debug.LogError("No texture data is available to upload.");
                return Texture2D.EmptyCapture();
            }
            var bytes = ReadScreen(superSize, out int width, out int height);
            var texture = new Texture2D(Math.Max(1, width), Math.Max(1, height), TextureFormat.RGBA32, false, true);
            if (bytes != null && bytes.Length == width * height * 4)
            {
                texture.LoadRawTextureData(bytes);
                texture.Apply(false);
            }
            return texture;
        }

        public static void CaptureScreenshotIntoRenderTexture(RenderTexture renderTexture)
        {
            if (renderTexture == null)
            {
                Debug.LogError("CaptureScreenshotIntoRenderTexture() The RenderTexture passed in argument is null. Please allocate it prior to calling this function.");
                return;
            }
            int width = Math.Min(Screen.width, renderTexture.width), height = Math.Min(Screen.height, renderTexture.height);
            if (width <= 0 || height <= 0) return;
            // The screen's bytes as an sRGB texture, drawn into the target's corner one texel per
            // pixel: sampling decodes them, an sRGB target encodes them again.
            var saved = GfxStream.Current;
            var savedActive = RenderTexture.ActiveTarget;
            bool wireframe = GL.wireframe;
            bool srgbWrite = GL.sRGBWrite;
            RenderTexture copy = null;
            try
            {
                GfxStream.Scissor(null);
                GL.sRGBWrite = true;
                copy = RenderTexture.GetTemporary(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                copy.filterMode = FilterMode.Point;
                ulong source = copy.EnsureCreated();
                GfxStream.CopyTexture(ScreenHandle, 0, 0, new RectInt(0, 0, Screen.width, Screen.height), source, 0, 0, 0, 0);
                var material = Immediate.BlitCopy;
                material.SetTexture(MainTex, copy);
                if (wireframe) GfxStream.SetGlFlag(GfxStream.CameraWireframe, false);
                GfxStream.SetTarget(renderTexture.EnsureCreated());
                GfxStream.Viewport(new Rect(0, 0, width, height));
                GfxStream.Camera(Matrix4x4.identity, Immediate.Ortho);
                s_Properties.Clear();
                s_Properties[MainTexST] = new Vector4((float)width / Screen.width, (float)height / Screen.height, 0, 0);
                GfxStream.Draw(Immediate.BlitQuad.Sync(), 0, material.Sync(), 0, Matrix4x4.identity, Color.white, properties: s_Properties);
            }
            finally
            {
                try
                {
                    if (wireframe) GfxStream.SetGlFlag(GfxStream.CameraWireframe, true);
                    GL.sRGBWrite = srgbWrite;
                    RenderTexture.SetActiveQuietly(savedActive);
                    GfxStream.Restore(saved);
                }
                finally { if (copy != null) RenderTexture.ReleaseTemporary(copy); }
            }
        }

        static readonly int MainTex = Shader.PropertyToID("_MainTex");
        static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");
        static readonly Dictionary<int, object> s_Properties = new Dictionary<int, object>();

        // During a screen camera callback the logical screen has not been presented yet.
        static ulong ScreenHandle => RenderTexture.ScreenStandIn?.EnsureCreated() ?? 0;

        // A requested screenshot holds what the end-of-frame callbacks drew (tests/projects/screencapture).
        static ScreenCapture() =>
            PlayerLoopDriver.RegisterFrameCleanup(WritePending);

        /// <summary>Writes the frame's requested screenshot (after the end-of-frame callbacks).</summary>
        static void WritePending()
        {
            if (s_PendingPath == null) return;
            string path = s_PendingPath;
            int superSize = s_PendingSize;
            s_PendingPath = null;
            var bytes = ReadScreen(superSize, out int width, out int height);
            if (bytes == null || bytes.Length != width * height * 4)
            {
                Debug.LogWarning("Unbound: ScreenCapture.CaptureScreenshot has no GPU to read from (headless run); no file is written: " + path);
                return;
            }
            try { File.WriteAllBytes(path, RgbPng(bytes, width, height)); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                Debug.LogError("Failed to store screen shot (" + path + "): " + e.Message);
            }
        }

        /// <summary>The screen's bytes (RGBA rows, bottom first) at superSize times its size.</summary>
        static byte[] ReadScreen(int superSize, out int width, out int height)
        {
            int screenWidth = Screen.width, screenHeight = Screen.height;
            width = screenWidth * superSize;
            height = screenHeight * superSize;
            if (superSize == 1) return Native.ReadPixels(ScreenHandle, 0, 0, width, height, false);
            if (Camera.current != null)
                throw new NotSupportedException("Unbound: supersized screen capture inside a camera callback is not supported; capture after the camera finishes rendering.");
            WarnOverlays();
            var tile = RenderTexture.GetTemporary(screenWidth, screenHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var savedActive = RenderTexture.ActiveTarget;
            var saved = GfxStream.Current;
            bool srgbWrite = GL.sRGBWrite;
            var settings = default(SupersampleSettings);
            try
            {
                settings = SupersampleSettings.Capture();
                settings.Apply(superSize);
                GfxStream.Scissor(null);
                GL.sRGBWrite = true;
                var cameras = Camera.CollectActive();
                cameras.RemoveAll(c => c.RendersOffscreen || c.targetDisplay != 0);
                var output = new byte[width * height * 4];
                int rowBytes = screenWidth * 4;
                for (int j = 0; j < superSize; j++)
                {
                    for (int i = 0; i < superSize; i++)
                    {
                        RenderTexture.active = tile;
                        GL.Clear(true, true, Color.black);
                        RenderTile(tile, cameras, (float)i / superSize - 0.5f, (float)j / superSize - 0.5f);
                        var bytes = Native.ReadPixels(tile.EnsureCreated(), 0, 0, screenWidth, screenHeight, false);
                        if (bytes == null || bytes.Length != rowBytes * screenHeight) return null;
                        Interleave(bytes, screenWidth, screenHeight, output, superSize, i, j);
                    }
                }
                return output;
            }
            finally
            {
                try
                {
                    settings.Restore();
                    GL.sRGBWrite = srgbWrite;
                    RenderTexture.SetActiveQuietly(savedActive);
                    GfxStream.Restore(saved);
                }
                finally { RenderTexture.ReleaseTemporary(tile); }
            }
        }

        /// <summary>Copies tile (i, j) of an N * N supersized capture into the output: its pixel
        /// (x, y) is the output's pixel (x * N + i, y * N + j) (RGBA bytes, rows bottom first).</summary>
        internal static void Interleave(byte[] tile, int tileWidth, int tileHeight, byte[] output, int superSize, int i, int j)
        {
            var source = MemoryMarshal.Cast<byte, uint>(tile.AsSpan(0, tileWidth * tileHeight * 4));
            var target = MemoryMarshal.Cast<byte, uint>(output.AsSpan());
            int width = tileWidth * superSize;
            for (int y = 0; y < tileHeight; y++)
            {
                int from = y * tileWidth, to = (y * superSize + j) * width + i;
                for (int x = 0; x < tileWidth; x++, to += superSize) target[to] = source[from + x];
            }
        }

        static void WarnOverlays()
        {
            if (s_WarnedOverlay) return;
            foreach (var canvas in Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (canvas.isActiveAndEnabled && canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    s_WarnedOverlay = true;
                    Debug.LogWarning("Unbound: ScreenCapture with superSize > 1 renders the cameras only; screen-space overlay canvases and OnGUI are not in the image.");
                    break;
                }
            }
        }

        /// <summary>
        /// One tile of a supersized capture: the screen cameras, in their order, at the screen's
        /// size into the tile, each projection moved by (offsetX, offsetY) of the camera's pixels,
        /// so that the tile's pixel x samples the screen at x + 0.5 + offsetX (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/screencapture).
        /// </summary>
        static void RenderTile(RenderTexture tile, List<Camera> cameras, float offsetX, float offsetY)
        {
            foreach (var camera in cameras)
            {
                var projection = camera.projectionMatrix;
                bool custom = camera.HasCustomProjection;
                try
                {
                    camera.targetTexture = tile;
                    camera.projectionMatrix = TileProjection(projection, camera.orthographic, camera.pixelRect, offsetX, offsetY);
                    camera.Render();
                }
                finally
                {
                    camera.targetTexture = null;
                    if (custom) camera.projectionMatrix = projection;
                    else camera.ResetProjectionMatrix();
                }
            }
        }

        /// <summary>A camera's projection moved by (offsetX, offsetY) of the pixels of its pixel
        /// rect: added to the third column for a camera that is not orthographic, taken from the
        /// fourth for an orthographic one. The camera's flag decides, not its matrix: a camera
        /// that is not orthographic but has an orthographic matrix set gets the perspective move,
        /// as in the Editor (tests/projects/screencapture <c>proj.customOrtho</c>).</summary>
        internal static Matrix4x4 TileProjection(Matrix4x4 projection, bool orthographic, Rect pixelRect, float offsetX, float offsetY)
        {
            float x = offsetX / (pixelRect.width * 0.5f), y = offsetY / (pixelRect.height * 0.5f);
            if (orthographic)
            {
                projection.m03 -= x;
                projection.m13 -= y;
            }
            else
            {
                projection.m02 += x;
                projection.m12 += y;
            }
            return projection;
        }

        /// <summary>
        /// The texture settings of a supersized capture (Unity 6000.0.71f1 Editor,
        /// tests/projects/screencapture <c>mip.*</c>): for its duration every texture filters at
        /// anisotropic level 16 whatever <see cref="QualitySettings.anisotropicFiltering"/> and
        /// the global limits say (but a texture of level 0, as always, at 1), and every 2D texture
        /// with mipmaps samples with its mip bias lowered by 1 + log2(superSize). The public
        /// settings are untouched; the renderer gets them back afterwards, and the biases too.
        /// </summary>
        internal readonly struct SupersampleSettings
        {
            /// <summary>The anisotropic level of every texture during the capture.</summary>
            const int CaptureAnisotropy = 16;

            readonly Texture[] m_Textures;
            readonly float[] m_Biases;

            SupersampleSettings(Texture[] textures, float[] biases)
            {
                m_Textures = textures;
                m_Biases = biases;
            }

            /// <summary>The textures the capture biases and their biases to put back; nothing is
            /// changed yet.</summary>
            internal static SupersampleSettings Capture()
            {
                var textures = new List<Texture>();
                var biases = new List<float>();
                foreach (var texture in Resources.FindObjectsOfTypeAll<Texture>())
                {
                    if (texture.dimension != TextureDimension.Tex2D || texture.mipmapCount <= 1) continue;
                    textures.Add(texture);
                    biases.Add(texture.mipMapBias);
                }
                return new SupersampleSettings(textures.ToArray(), biases.ToArray());
            }

            internal void Apply(int superSize)
            {
                // Forced on, with the forced minimum and the maximum both at the capture's level.
                // A script changing the anisotropic settings during the capture sends them again
                // and ends this for the remaining tiles (not measured in the Editor).
                GfxStream.Anisotropy((int)AnisotropicFiltering.ForceEnable, CaptureAnisotropy, CaptureAnisotropy);
                float shift = -1f - MathF.Log2(superSize);
                for (int k = 0; k < m_Textures.Length; k++) m_Textures[k].mipMapBias = m_Biases[k] + shift;
            }

            /// <summary>Puts back what <see cref="Capture"/> recorded (nothing for a default value).</summary>
            internal void Restore()
            {
                if (m_Textures == null) return;
                for (int k = 0; k < m_Textures.Length; k++)
                    if (m_Textures[k] != null) m_Textures[k].mipMapBias = m_Biases[k];
                Texture.EmitAnisotropicFiltering();
            }
        }

        static readonly uint[] s_Crc = BuildCrc();

        static uint[] BuildCrc()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }

        /// <summary>An 8-bit RGB PNG (IHDR, one IDAT, IEND) of RGBA rows given bottom first.</summary>
        internal static byte[] RgbPng(byte[] rgba, int width, int height)
        {
            var raw = new byte[(width * 3 + 1) * height];
            for (int y = 0; y < height; y++)
            {
                int row = (height - 1 - y) * width * 4, at = y * (width * 3 + 1);
                raw[at++] = 0;
                for (int x = 0; x < width; x++, row += 4)
                {
                    raw[at++] = rgba[row];
                    raw[at++] = rgba[row + 1];
                    raw[at++] = rgba[row + 2];
                }
            }
            var data = new MemoryStream();
            using (var z = new ZLibStream(data, System.IO.Compression.CompressionLevel.Optimal, true)) z.Write(raw, 0, raw.Length);
            var png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
            var header = new byte[13];
            BigEndian(header, 0, (uint)width);
            BigEndian(header, 4, (uint)height);
            header[8] = 8;
            header[9] = 2;
            Chunk(png, "IHDR", header);
            Chunk(png, "IDAT", data.ToArray());
            Chunk(png, "IEND", Array.Empty<byte>());
            return png.ToArray();
        }

        static void BigEndian(byte[] buffer, int at, uint value)
        {
            buffer[at] = (byte)(value >> 24);
            buffer[at + 1] = (byte)(value >> 16);
            buffer[at + 2] = (byte)(value >> 8);
            buffer[at + 3] = (byte)value;
        }

        static void Chunk(Stream png, string type, byte[] data)
        {
            var bytes = new byte[12 + data.Length];
            BigEndian(bytes, 0, (uint)data.Length);
            for (int i = 0; i < 4; i++) bytes[4 + i] = (byte)type[i];
            Buffer.BlockCopy(data, 0, bytes, 8, data.Length);
            uint crc = 0xFFFFFFFFu;
            for (int i = 4; i < 8 + data.Length; i++) crc = s_Crc[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);
            BigEndian(bytes, 8 + data.Length, crc ^ 0xFFFFFFFFu);
            png.Write(bytes, 0, bytes.Length);
        }
    }
}
