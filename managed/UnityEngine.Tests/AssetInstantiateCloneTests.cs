using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>Asset resource ownership and diagnostics measured by tests/projects/assetinstantiate
    /// against the Unity 6000.0.71f1 Editor, plus retained-storage implementation controls.</summary>
    public class AssetInstantiateCloneTests : IDisposable
    {
        readonly AudioBackend m_PreviousAudio;
        readonly string m_PreviousRelease = ProjectInfo.UnityVersion;
        readonly ILogHandler m_PreviousLog = Debug.unityLogger.logHandler;
        readonly Messages m_Messages = new Messages();

        sealed class Messages : ILogHandler
        {
            internal readonly List<string> Values = new List<string>();
            public void LogFormat(LogType type, Object context, string format, params object[] args) => Values.Add(string.Format(format, args));
            public void LogException(Exception exception, Object context) => Values.Add(exception.Message);
        }
        readonly VirtualAudioBackend m_Audio = new VirtualAudioBackend();

        public AssetInstantiateCloneTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            Debug.unityLogger.logHandler = m_Messages;
            m_PreviousAudio = AudioBackend.Current;
            AudioBackend.Use(m_Audio);
        }

        public void Dispose()
        {
            AudioBackend.Use(m_PreviousAudio);
            ProjectInfo.UnityVersion = m_PreviousRelease;
            Debug.unityLogger.logHandler = m_PreviousLog;
        }

        static float[] Ramp(int count, float scale)
        {
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = scale * (i + 1) / count;
            return data;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void RuntimeAudioCloneCopiesMetadataWithoutSamplesOrCallbacks(bool stream)
        {
            int calls = 0;
            var source = AudioClip.Create("tone", 8, 2, 22050, stream, data => { calls++; Array.Fill(data, 0.5f); });
            int beforeClone = calls;
            var copy = Object.Instantiate(source);
            Assert.NotSame(source, copy);
            Assert.NotEqual(source.GetInstanceID(), copy.GetInstanceID());
            Assert.Equal("tone(Clone)", copy.name);
            Assert.False(copy.IsPersistentAsset);
            Assert.Equal(0, copy.samples);
            Assert.Equal(source.channels, copy.channels);
            Assert.Equal(source.frequency, copy.frequency);
            Assert.Equal(0f, copy.length);
            Assert.Equal(AudioClipLoadType.DecompressOnLoad, copy.loadType);
            Assert.Equal(AudioDataLoadState.Unloaded, copy.loadState);
            Assert.Equal(0ul, copy.m_Handle);
            Assert.False(copy.GetData(new float[16], 0));
            Assert.Throws<ArgumentException>(() => copy.SetData(new float[16], 0));
            Assert.True(copy.LoadAudioData());
            Assert.True(copy.UnloadAudioData());
            Assert.Equal(beforeClone, calls);
            Object.DestroyImmediate(copy);
            Assert.True(source);
            Object.DestroyImmediate(source);
        }

        [Theory]
        [InlineData(AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.PCM)]
        [InlineData(AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.Vorbis)]
        [InlineData(AudioClipLoadType.Streaming, AudioCompressionFormat.Vorbis)]
        public void ImportedAudioClipCloneSharesDecodedResourceAndKeepsSettings(AudioClipLoadType loadType, AudioCompressionFormat compression)
        {
            var guid = $"clone-audio-{loadType}";
            var samples = Ramp(3 * 10, 0.75f);
            m_Audio.Assets[guid] = (new AudioClipInfo
            {
                Frames = 10, Channels = 3, Frequency = 11025, LoadType = (int)loadType,
                Preload = 1, LoadInBackground = 0, Ambisonic = 1, CompressionFormat = (int)compression,
            }, samples);
            var source = AudioClip.FromImport(guid, "imported");

            var copy = Object.Instantiate(source);
            Assert.NotSame(source, copy);
            Assert.NotEqual(source.GetInstanceID(), copy.GetInstanceID());
            Assert.Equal("imported(Clone)", copy.name);
            Assert.True(source.IsPersistentAsset);
            Assert.False(copy.IsPersistentAsset);
            Assert.Equal(loadType, copy.loadType);
            Assert.Equal(source.m_CompressionFormat, copy.m_CompressionFormat);
            Assert.Equal(source.preloadAudioData, copy.preloadAudioData);
            Assert.Equal(source.loadInBackground, copy.loadInBackground);
            Assert.Equal(source.ambisonic, copy.ambisonic);
            Assert.Equal(source.samples, copy.samples);
            Assert.Equal(source.channels, copy.channels);
            Assert.Equal(source.frequency, copy.frequency);
            Assert.Equal(source.loadState, copy.loadState);
            Assert.Equal(source.m_Handle, copy.m_Handle);

            // The mixer holds every clip's decoded samples, whatever its load type.
            var stored = new float[samples.Length];
            Assert.True(AudioBackend.Current.ClipData(copy.m_Handle, 0, stored, write: false));
            Assert.Equal(samples, stored);
            Assert.True(AudioBackend.Current.ClipData(copy.m_Handle, 0, Ramp(samples.Length, -1f), write: true));
            Assert.True(AudioBackend.Current.ClipData(source.m_Handle, 0, stored, write: false));
            Assert.Equal(Ramp(samples.Length, -1f), stored);
            Object.DestroyImmediate(copy);
            Assert.True(source.GetInstanceID() != 0);
            Assert.True(AudioBackend.Current.ClipData(source.m_Handle, 0, stored, write: false));
            Assert.Equal(Ramp(samples.Length, -1f), stored);
            Object.DestroyImmediate(source, true);
        }

        [Fact]
        public void ImportedCloneSurvivesAssetDestructionAndReloadsItsSharedResource()
        {
            var samples = Ramp(30, 0.75f);
            m_Audio.Assets["lifetime"] = (new AudioClipInfo { Frames = 10, Channels = 3, Frequency = 11025, Preload = 1 }, samples);
            var source = AudioClip.FromImport("lifetime", "imported");
            var first = Object.Instantiate(source);
            var last = Object.Instantiate(first);
            first.SetData(Ramp(30, -1f), 0);
            Object.DestroyImmediate(first);
            var read = new float[30];
            Assert.True(last.GetData(read, 0));
            Assert.Equal(Ramp(30, -1f), read);
            Object.DestroyImmediate(source, true);
            Assert.Equal(AudioDataLoadState.Unloaded, last.loadState);
            Assert.True(last.LoadAudioData());
            Assert.Equal(AudioDataLoadState.Loaded, last.loadState);
            Assert.True(last.GetData(read, 0));
            Assert.Equal(samples, read);
            Object.DestroyImmediate(last);
        }

        [Fact]
        public void AssetResourceCloneRequiresMeasuredRelease()
        {
            var source = AudioClip.Create("tone", 8, 1, 8000, false);
            var font = new Font();
            ProjectInfo.UnityVersion = "6000.0.72f1";
            Assert.Throws<NotSupportedException>(() => Object.Instantiate(source));
            Assert.Throws<NotSupportedException>(() => Object.Instantiate(font));
            Object.DestroyImmediate(source);Object.DestroyImmediate(font);
        }

        [Fact]
        public void CustomFontCloneCopiesCharactersAndKeepsReferences()
        {
            var material = new Material(Shader.Create("GUI/Text Shader", 3000));
            var source = new Font { name = "custom", material = material, fontNames = new[] { "Custom" } };
            source.m_FontSize = 12;
            source.m_Ascent = 9;
            source.m_LineSpacing = 14;
            source.characterInfo = new[] { new CharacterInfo { index = 'A', advance = 5 }, new CharacterInfo { index = 'B', advance = 6 } };

            var copy = Object.Instantiate(source);
            Assert.NotSame(source, copy);
            Assert.NotEqual(source.GetInstanceID(), copy.GetInstanceID());
            Assert.Equal("custom(Clone)", copy.name);
            Assert.False(copy.dynamic);
            Assert.Same(material, copy.material);
            Assert.Equal(source.fontSize, copy.fontSize);
            Assert.Equal(source.ascent, copy.ascent);
            Assert.Equal(source.lineHeight, copy.lineHeight);
            Assert.Equal(source.fontNames, copy.fontNames);
            Assert.Equal(source.characterInfo, copy.characterInfo);

            copy.characterInfo = new[] { new CharacterInfo { index = 'C', advance = 7 } };
            copy.fontNames = new[] { "Other" };
            Assert.True(source.HasCharacter('A'));
            Assert.False(source.HasCharacter('C'));
            Assert.Equal(new[] { "Custom" }, source.fontNames);
            Object.DestroyImmediate(source);
            Assert.True(copy.HasCharacter('C'));
            Object.DestroyImmediate(copy);
        }

        [Fact]
        public void DynamicFontCloneReportsUnsupportedAndSharesMaterial()
        {
            var source = new Font(new Unbound.Tests.BoxGlyphSource(), 10, "Box");
            var copy = Object.Instantiate(source);
            Assert.NotSame(source, copy);
            Assert.NotEqual(source.GetInstanceID(), copy.GetInstanceID());
            Assert.Equal("Box(Clone)", copy.name);
            Assert.True(copy.dynamic);
            Assert.Same(source.m_Source, copy.m_Source);
            Assert.Equal(source.fontSize, copy.fontSize);
            Assert.Equal(source.ascent, copy.ascent);
            Assert.Equal(source.lineHeight, copy.lineHeight);
            Assert.Equal(source.fontNames, copy.fontNames);
            Assert.NotNull(copy.material);
            Assert.Same(source.material, copy.material);
            Assert.NotSame(source.Atlas.Texture, copy.Atlas.Texture);
            Assert.Same(source.Atlas.Texture, copy.material.mainTexture);
            Assert.Contains("Font Error: Cloning a dynamic font is not supported and may result in incorrect font rendering.", m_Messages.Values);

            copy.RequestCharactersInTexture("AB");
            Assert.True(copy.GetCharacterInfo('A', out var copied));
            Assert.Empty(source.characterInfo);
            source.RequestCharactersInTexture("AB");
            Assert.True(source.GetCharacterInfo('A', out var original));
            Assert.Equal(original, copied);
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(source);
        }

        [Fact]
        public void ShaderClonePreservesNameAndOwnsMutableSettings()
        {
            var source = Shader.Create("Control", 3000);
            source.maximumLOD = 200;
            source.hideFlags = HideFlags.HideAndDontSave;
            var copy = Object.Instantiate(source);
            Assert.Equal("Control", copy.name);
            Assert.NotEqual(source.GetInstanceID(), copy.GetInstanceID());
            Assert.Equal(HideFlags.None, copy.hideFlags);
            Assert.Equal(200, copy.maximumLOD);
            copy.maximumLOD = 100;
            Assert.Equal(200, source.maximumLOD);
            Object.DestroyImmediate(source);Object.DestroyImmediate(copy);
        }

        [Fact]
        public void ComputeCloneStartsWithoutRuntimeBindings()
        {
            var source = ComputeShader.FromFile("control.compute", "compute", "#pragma kernel Main");
            source.SetInt("Value", 7);
            source.EnableKeyword("RUNTIME");
            var copy = Object.Instantiate(source);
            Assert.Equal("compute(Clone)", copy.name);
            Assert.True(copy.HasKernel("Main"));
            Assert.Empty(copy.shaderKeywords);
            Assert.False(copy.IsKeywordEnabled("RUNTIME"));
            Assert.NotEqual(source.GetInstanceID(), copy.GetInstanceID());
            Object.DestroyImmediate(source);Object.DestroyImmediate(copy);
        }

        [Fact]
        public void UnreadableMeshCloneKeepsGeometryForRenderingWithoutExposingVertices()
        {
            var source = new Mesh { name = "mesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            source.UploadMeshData(true);
            var copy = Object.Instantiate(source);
            Assert.False(copy.isReadable);
            Assert.Equal(3, copy.vertexCount);
            Assert.Empty(copy.vertices);
            Assert.Contains("Not allowed to access vertices on mesh 'mesh(Clone)' (isReadable is false; Read/Write must be enabled in import settings)", m_Messages.Values);
            Object.DestroyImmediate(source);Object.DestroyImmediate(copy);
        }

        [Fact]
        public void BakingAnUnreadableMeshUsesTheRenderersRetainedGeometry()
        {
            var source = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            source.UploadMeshData(true);
            var go = new GameObject("bake");
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = source;
            var baked = new Mesh();
            renderer.BakeMesh(baked);
            Assert.Equal(new[] { Vector3.zero, Vector3.right, Vector3.up }, baked.vertices);
            Assert.Equal(new[] { 0, 1, 2 }, baked.triangles);
            Object.DestroyImmediate(go);Object.DestroyImmediate(source);Object.DestroyImmediate(baked);
        }

        static Color FaceColor(int face, int mip) => new Color32((byte)(40 * face), (byte)(60 * mip + 15), 200, 255);

        static Cubemap FilledCubemap()
        {
            var source = new Cubemap(4, TextureFormat.RGBA32, true)
            {
                name = "cube", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, anisoLevel = 3,
            };
            for (int face = 0; face < 6; face++)
                for (int mip = 0; mip < source.mipmapCount; mip++)
                {
                    var pixels = new Color[Math.Max(1, 4 >> mip) * Math.Max(1, 4 >> mip)];
                    Array.Fill(pixels, FaceColor(face, mip));
                    source.SetPixels(pixels, (CubemapFace)face, mip);
                }
            return source;
        }

        [Fact]
        public void ReadableCubemapCloneCopiesFacesMipsAndIsIndependent()
        {
            var source = FilledCubemap();
            source.Apply(false);
            source.SetPixel(CubemapFace.NegativeY, 1, 1, Color.green); // Unapplied CPU edits are copied too.

            var copy = Object.Instantiate(source);
            Assert.NotSame(source, copy);
            Assert.NotEqual(source.GetInstanceID(), copy.GetInstanceID());
            Assert.Equal("cube(Clone)", copy.name);
            Assert.False(copy.IsPersistentAsset);
            Assert.True(copy.isReadable);
            Assert.Equal(source.width, copy.width);
            Assert.Equal(source.format, copy.format);
            Assert.Equal(source.graphicsFormat, copy.graphicsFormat);
            Assert.Equal(source.mipmapCount, copy.mipmapCount);
            Assert.Equal(source.filterMode, copy.filterMode);
            Assert.Equal(source.wrapModeU, copy.wrapModeU);
            Assert.Equal(source.wrapModeV, copy.wrapModeV);
            Assert.Equal(source.wrapModeW, copy.wrapModeW);
            Assert.Equal(source.anisoLevel, copy.anisoLevel);
            for (int face = 0; face < 6; face++)
                for (int mip = 0; mip < source.mipmapCount; mip++)
                    Assert.Equal(source.GetPixels((CubemapFace)face, mip), copy.GetPixels((CubemapFace)face, mip));
            Assert.Equal(Color.green, copy.GetPixel(CubemapFace.NegativeY, 1, 1));

            copy.SetPixel(CubemapFace.PositiveZ, 0, 0, Color.magenta, 1);
            Assert.Equal(FaceColor(4, 1), source.GetPixel(CubemapFace.PositiveZ, 0, 0, 1));
            Object.DestroyImmediate(source);
            Assert.Equal(Color.magenta, copy.GetPixel(CubemapFace.PositiveZ, 0, 0, 1));
            Object.DestroyImmediate(copy);
        }

        [Fact]
        public void NonReadableCubemapCloneKeepsReadabilityAndOwnRetainedFaces()
        {
            var source = FilledCubemap();
            source.Apply(false, true);
            Assert.False(source.isReadable);

            var copy = Object.Instantiate(source);
            Assert.Equal("cube(Clone)", copy.name);
            Assert.NotEqual(source.GetInstanceID(), copy.GetInstanceID());
            Assert.False(copy.isReadable);
            Assert.Equal(source.mipmapCount, copy.mipmapCount);
            Assert.Throws<ArgumentException>(() => copy.GetPixels(CubemapFace.PositiveX));
            Assert.Contains("Instantiating a non-readable 'cube' texture is not allowed! Please mark the texture readable in the inspector or don't instantiate it.", m_Messages.Values);
            Assert.NotSame(source.m_UnreadablePixels, copy.m_UnreadablePixels);
            for (int face = 0; face < 6; face++)
                for (int mip = 0; mip < copy.mipmapCount; mip++)
                {
                    Assert.NotSame(source.m_UnreadablePixels[face][mip], copy.m_UnreadablePixels[face][mip]);
                    Assert.All(copy.m_UnreadablePixels[face][mip], c => Assert.Equal(FaceColor(face, mip), c));
                }
            Object.DestroyImmediate(source);
            Assert.Equal(FaceColor(2, 0), copy.m_UnreadablePixels[2][0][0]);
            Object.DestroyImmediate(copy);
        }

        [Fact]
        public void CubemapWithoutRetainedPixelsFailsLoudly()
        {
            var source = FilledCubemap();
            source.Apply(false, true);
            source.m_UnreadablePixels = null; // As a cubemap held only on the GPU.
            var error = Assert.Throws<ArgumentException>(() => Object.Instantiate(source));
            Assert.Contains("retained pixel data is unavailable", error.Message);
            Object.DestroyImmediate(source);
        }
    }
}
