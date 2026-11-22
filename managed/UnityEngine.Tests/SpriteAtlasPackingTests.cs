using System;
using UnityEngine.U2D;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A sprite bound to a packed atlas page (UVs mirrored as the packer flipped it, a variant's
    /// scale), late binding through SpriteAtlasManager, and Sprites.DataUtility's rect rules (the
    /// values the 6000.0.71f1 Editor gives in tests/projects/spriteatlas; the sprites here are
    /// stand-ins of their shapes).
    /// </summary>
    public class SpriteAtlasPackingTests
    {
        /// <summary>A V1 atlas file a Play-entry pack stores is left as it is only when its text is
        /// the same: one that differs only in CRLF line endings is written (with LF), as in the
        /// Editor (tests/projects/spriteatlaseditor v1wholewritecrlf).</summary>
        [Fact]
        public void AFileThatDiffersOnlyInItsLineEndingsIsWritten()
        {
            const string written = "%YAML 1.1\n--- !u!687078895 &1\nSpriteAtlas:\n  m_Name: A\n";
            Assert.True(Unbound.Runtime.AssetFiles.Unchanged(written, written));
            Assert.False(Unbound.Runtime.AssetFiles.Unchanged(written.Replace("\n", "\r\n"), written));
            Assert.False(Unbound.Runtime.AssetFiles.Unchanged(written + "\n", written));
        }

        static Sprite Quad() => Sprite.Create(new Texture2D(8, 4), new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);

        [Fact]
        public void BindingMovesTheUvsToThePageAndMirrorsFlippedSprites()
        {
            var page = new Texture2D(16, 8);
            // The quad's left edge is at -2 units; the page rect starts at 10, so the UV transform's
            // offset is 12 (the pivot 2 pixels in).
            var transform = new Vector4(1f, 12f, 1f, 4f);
            var plain = Quad();
            plain.BindAtlas(page, new Rect(10, 2, 4, 4), transform, SpritePackingMode.Rectangle, SpritePackingRotation.None, new Vector2(16, 8), 1f);
            Assert.True(plain.packed);
            Assert.Same(page, plain.texture);
            Assert.Equal(new Rect(10, 2, 4, 4), plain.textureRect);
            Assert.Equal(10f / 16f, plain.uv[0].x);
            Assert.Equal(6f / 8f, plain.uv[0].y);
            var flipped = Quad();
            flipped.BindAtlas(page, new Rect(10, 2, 4, 4), transform, SpritePackingMode.Tight, SpritePackingRotation.Rotate180, new Vector2(16, 8), 1f);
            Assert.Equal(SpritePackingMode.Tight, flipped.packingMode);
            Assert.Equal(SpritePackingRotation.Rotate180, flipped.packingRotation);
            // Mirrored within the page rect on both axes: the top-left corner samples the bottom-right.
            Assert.Equal(14f / 16f, flipped.uv[0].x, 6);
            Assert.Equal(2f / 8f, flipped.uv[0].y, 6);
            // A variant's half-size page keeps the master's rects and UVs.
            var half = Quad();
            half.BindAtlas(new Texture2D(8, 4), new Rect(10, 2, 4, 4), transform, SpritePackingMode.Rectangle, SpritePackingRotation.None, new Vector2(16, 8), 0.5f);
            Assert.Equal(plain.uv, half.uv);
            Assert.Equal(0.5f, half.spriteAtlasTextureScale);
            Assert.Equal(UnityEngine.Sprites.DataUtility.GetOuterUV(plain), UnityEngine.Sprites.DataUtility.GetOuterUV(half));
        }

        [Fact]
        public void LateBindingSpritesHaveNoTextureAndZeroUvs()
        {
            var sprite = Quad();
            sprite.UnbindTexture();
            Assert.Null(sprite.texture);
            Assert.All(sprite.uv, uv => Assert.Equal(Vector2.zero, uv));
            Assert.False(sprite.packed);
            Assert.Equal(new Vector4(0, 0, 1, 1), UnityEngine.Sprites.DataUtility.GetOuterUV(sprite));
            Assert.Equal(new Vector4(0, 0, 1, 1), UnityEngine.Sprites.DataUtility.GetInnerUV(sprite));
        }

        [Fact]
        public void AtlasRequestsReachListenersAndRegisterTheGivenAtlas()
        {
            Assert.False(SpriteAtlasManager.RequestAtlas("Nobody"));
            string requested = null;
            SpriteAtlas registered = null;
            Action<string, Action<SpriteAtlas>> listener = (tag, register) => { requested = tag; register(null); };
            Action<SpriteAtlas> onRegistered = atlas => registered = atlas;
            SpriteAtlasManager.atlasRequested += listener;
            SpriteAtlasManager.atlasRegistered += onRegistered;
            try
            {
                Assert.True(SpriteAtlasManager.RequestAtlas("Late"));
                Assert.Equal("Late", requested);
                Assert.Null(registered);
            }
            finally
            {
                SpriteAtlasManager.atlasRequested -= listener;
                SpriteAtlasManager.atlasRegistered -= onRegistered;
            }
        }

        [Fact]
        public void PaddingAndOuterUvFollowTheTextureRect()
        {
            // A sprite whose texture rect is its rect: no padding, the rect's UVs.
            var sprite = Quad();
            Assert.Equal(Vector4.zero, UnityEngine.Sprites.DataUtility.GetPadding(sprite));
            Assert.Equal(new Vector4(0, 0, 0.5f, 1f), UnityEngine.Sprites.DataUtility.GetOuterUV(sprite));
        }

        [Fact]
        public void BuiltInMultiAtlasSelectionPrefersMasterThenUnityGuidWordOrder()
        {
            // Unbound's deterministic order among several built-in atlases (Unity leaves it open;
            // it agrees with every case of tests/projects/spriteatlasmultibind).
            // Nibble-reversed 32-bit word order: "90000001..." -> word0 0x10000009 < "10000009..." -> word0 0x90000001,
            // even though ASCII order puts "10000009..." first.
            Assert.True(SpriteAtlasPacking.CompareUnityGuid(
                "90000001000000000000000000000001",
                "10000009000000000000000000000001") < 0);
            Assert.True(SpriteAtlasPacking.CompareUnityGuid(
                "10000009000000000000000000000001",
                "90000001000000000000000000000001") > 0);
            // Case-insensitive equality and tie-break in word 3 (last 8 hex digits).
            Assert.Equal(0, SpriteAtlasPacking.CompareUnityGuid(
                "a1b2c3d4e5f60718293a4b5c6d7e8f90",
                "A1B2C3D4E5F60718293A4B5C6D7E8F90"));
            Assert.True(SpriteAtlasPacking.CompareUnityGuid(
                "000000000000000000000000f0000001",
                "00000000000000000000000010000002") < 0);

            // Master (isVariant == false) always beats variant (isVariant == true) regardless of GUID.
            Assert.True(SpriteAtlasPacking.CompareBuiltInAtlas(
                aIsVariant: false, aGuid: "1000000f000000000000000000000003",
                bIsVariant: true, bGuid: "10000001000000000000000000000003") < 0);

            // Two variants break ties by UnityGUID word order.
            Assert.True(SpriteAtlasPacking.CompareBuiltInAtlas(
                aIsVariant: true, aGuid: "90000002000000000000000000000004",
                bIsVariant: true, bGuid: "10000008000000000000000000000004") < 0);
        }

        sealed class WarningCapture : ILogHandler
        {
            internal readonly System.Collections.Generic.List<string> Warnings = new System.Collections.Generic.List<string>();
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning) Warnings.Add(args == null || args.Length == 0 ? format : string.Format(format, args));
            }
            public void LogException(Exception exception, Object context) { }
        }

        [Fact]
        public void OnlyAFirstPlayModeEntryPacksAndALaterOneWarnsWhenAtlasesWerePacked()
        {
            var previous = Debug.unityLogger.logHandler;
            var capture = new WarningCapture();
            Debug.unityLogger.logHandler = capture;
            try
            {
                // Nothing packed before: the entry packs and stores, silently.
                Assert.True(SpriteAtlasPacking.FirstPlayModePack(null, cancelled: false));
                Assert.Empty(capture.Warnings);
                // Packed before (an earlier entry, or an Edit-mode pack): nothing is stored again.
                Assert.False(SpriteAtlasPacking.FirstPlayModePack(3, cancelled: false));
                Assert.Single(capture.Warnings);
                Assert.Contains("does not pack V1 sprite atlases again", capture.Warnings[0]);
                // No atlas packed, or a cancelled pack: nothing stored and nothing to warn about.
                Assert.False(SpriteAtlasPacking.FirstPlayModePack(0, cancelled: false));
                Assert.False(SpriteAtlasPacking.FirstPlayModePack(3, cancelled: true));
                Assert.Single(capture.Warnings);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
        }

        [Fact]
        public void APackIsStoredOnlyWhenCompleteAndOverNoUnsavedChanges()
        {
            var previous = Debug.unityLogger.logHandler;
            var capture = new WarningCapture();
            Debug.unityLogger.logHandler = capture;
            try
            {
                Assert.True(SpriteAtlasPacking.MayStorePack("A", complete: true, unsavedChanges: false));
                Assert.Empty(capture.Warnings);
                Assert.False(SpriteAtlasPacking.MayStorePack("B", complete: false, unsavedChanges: false));
                Assert.False(SpriteAtlasPacking.MayStorePack("C", complete: true, unsavedChanges: true));
                Assert.Equal(2, capture.Warnings.Count);
                Assert.Contains("sprite atlas B is packed only in part", capture.Warnings[0]);
                Assert.Contains("sprite atlas C has unsaved changes", capture.Warnings[1]);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
            // A pack whose JSON does not say it is complete is not.
            Assert.True(SpriteAtlasPacking.PackJsonIsComplete("{\"guid\":\"a\",\"complete\":true}"));
            Assert.False(SpriteAtlasPacking.PackJsonIsComplete("{\"guid\":\"a\",\"complete\":false}"));
            Assert.False(SpriteAtlasPacking.PackJsonIsComplete("{\"guid\":\"a\"}"));
        }
    }
}
