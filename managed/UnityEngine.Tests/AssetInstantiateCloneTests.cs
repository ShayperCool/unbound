using System;
using Unbound.Runtime;
using UnityEngine;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>Object.Instantiate of AudioClip, Font and Cubemap assets: a new "(Clone)" object
    /// with its own identity and a copy of the data, independent of the source afterwards.
    /// Clone load-state, dynamic-font material/texture ownership and private CPU-retention
    /// assertions below are Unbound implementation checks. These choices are unmeasured in
    /// Unity and may be revised by a Native capture.</summary>
    public class AssetInstantiateCloneTests : IDisposable
    {
        readonly AudioBackend m_PreviousAudio;
        readonly VirtualAudioBackend m_Audio = new VirtualAudioBackend();

        public AssetInstantiateCloneTests()
        {
            m_PreviousAudio = AudioBackend.Current;
            AudioBackend.Use(m_Audio);
        }

        public void Dispose() => AudioBackend.Use(m_PreviousAudio);

        static float[] Ramp(int count, float scale)
        {
            var data = new float[count];
            for (int i = 0; i < count; i++) data[i] = scale * (i + 1) / count;
            return data;
        }

        [Fact]
        public void ScriptAudioClipCloneCopiesSamplesAndIsIndependent()
        {
            var source = AudioClip.Create("tone", 8, 2, 22050, false);
            var samples = Ramp(16, 0.5f);
            Assert.True(source.SetData(samples, 0));

            var copy = Object.Instantiate(source);
            Assert.NotSame(source, copy);
            Assert.NotEqual(source.GetInstanceID(), copy.GetInstanceID());
            Assert.Equal("tone(Clone)", copy.name);
            Assert.False(copy.IsPersistentAsset);
            Assert.Equal(source.samples, copy.samples);
            Assert.Equal(source.channels, copy.channels);
            Assert.Equal(source.frequency, copy.frequency);
            Assert.Equal(source.length, copy.length);
            Assert.Equal(source.loadType, copy.loadType);
            Assert.Equal(AudioDataLoadState.Loaded, copy.loadState);
            Assert.NotEqual(source.m_Handle, copy.m_Handle);
            var read = new float[16];
            Assert.True(copy.GetData(read, 0));
            Assert.Equal(samples, read);

            Assert.True(copy.SetData(Ramp(16, -1f), 0));
            var original = new float[16];
            Assert.True(source.GetData(original, 0));
            Assert.Equal(samples, original);

            Object.DestroyImmediate(source);
            Assert.True(copy.GetData(read, 0));
            Assert.Equal(Ramp(16, -1f), read);
            Object.DestroyImmediate(copy);
        }

        [Theory]
        [InlineData(AudioClipLoadType.DecompressOnLoad, AudioCompressionFormat.PCM)]
        [InlineData(AudioClipLoadType.CompressedInMemory, AudioCompressionFormat.Vorbis)]
        [InlineData(AudioClipLoadType.Streaming, AudioCompressionFormat.Vorbis)]
        public void ImportedAudioClipCloneKeepsLoadSettingsAndEveryDecodedSample(AudioClipLoadType loadType, AudioCompressionFormat compression)
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
            Assert.NotEqual(source.m_Handle, copy.m_Handle);

            // The mixer holds every clip's decoded samples, whatever its load type.
            var stored = new float[samples.Length];
            Assert.True(AudioBackend.Current.ClipData(copy.m_Handle, 0, stored, write: false));
            Assert.Equal(samples, stored);
            Assert.True(AudioBackend.Current.ClipData(copy.m_Handle, 0, Ramp(samples.Length, -1f), write: true));
            Assert.True(AudioBackend.Current.ClipData(source.m_Handle, 0, stored, write: false));
            Assert.Equal(samples, stored);
            Object.DestroyImmediate(copy);
        }

        [Fact]
        public void StreamedScriptAudioClipCloneFailsLoudly()
        {
            var source = AudioClip.Create("reader", 8, 1, 8000, true, data => { });
            var error = Assert.Throws<ArgumentException>(() => Object.Instantiate(source));
            Assert.Contains("PCM reader", error.Message);
            Object.DestroyImmediate(source);
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
        public void DynamicFontCloneSharesFaceAndOwnsGlyphTexture()
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
            Assert.NotSame(source.material, copy.material);
            Assert.NotSame(source.Atlas.Texture, copy.Atlas.Texture);
            Assert.Same(copy.Atlas.Texture, copy.material.mainTexture);

            copy.RequestCharactersInTexture("AB");
            Assert.True(copy.GetCharacterInfo('A', out var copied));
            Assert.Empty(source.characterInfo);
            source.RequestCharactersInTexture("AB");
            Assert.True(source.GetCharacterInfo('A', out var original));
            Assert.Equal(original, copied);
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(source);
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
            Assert.Throws<UnityException>(() => copy.GetPixels(CubemapFace.PositiveX));
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
