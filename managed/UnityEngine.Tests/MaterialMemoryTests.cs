using System;
using Unbound.Runtime;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class MaterialMemoryTests : IDisposable
    {
        const string PlainShader = "Shader \"Tests/MaterialMemory\" { Properties { _Parameter (\"P\", Float) = 0.5 } SubShader { Pass {} } }";
        readonly GraphicsDeviceProfile m_PreviousDevice = GraphicsDeviceProfile.Current;
        readonly string m_PreviousThreading = GraphicsDeviceProfile.CurrentThreadingMode.ToString();

        public MaterialMemoryTests() => GraphicsDeviceProfile.Select("Metal");

        public void Dispose() => GraphicsDeviceProfile.Select(m_PreviousDevice.Type.ToString(), m_PreviousThreading);

        static Shader CreateShader(string source)
        {
            var shader = Shader.Create("Tests/MaterialMemory", 2000);
            BuiltinShaders.ApplySource(shader, source);
            return shader;
        }

        [Fact]
        public void AnUnmeasuredWriteLeavesTheExistingPropertyBehaviorAndReportsUnknownSize()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var shader = CreateShader(PlainShader);
            var material = new Material(shader);
            try
            {
                Assert.Equal(1864, Profiler.GetRuntimeMemorySizeLong(material));
                material.SetFloat("_Parameter", 8f);
                Assert.Equal(8f, material.GetFloat("_Parameter"));
                Assert.Equal(1864, Profiler.GetRuntimeMemorySizeLong(material));
                var matrix = Matrix4x4.Translate(new Vector3(1, 2, 3));
                material.SetMatrix("_UnmeasuredMatrix", matrix);
                Assert.Equal(matrix, material.GetMatrix("_UnmeasuredMatrix"));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(material));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [Theory]
        [InlineData("Shader \"Tests/MaterialMemory\" { Properties { _Known (\"P\", Float) = 0.5 _Unknown (\"Q\", CustomType) = 3 } SubShader { Pass {} } }")]
        [InlineData("Shader \"Tests/MaterialMemory\" { Properties { _Known (\"P\", Float) = 0.5")]
        public void APartialShaderPropertyLayoutIsNotUsedForTheProfiler(string source)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var shader = CreateShader(source);
            var material = new Material(shader);
            try
            {
                Assert.Equal(0.5f, material.GetFloat("_Known"));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(material));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [Theory]
        [InlineData("Shader \"Tests/MaterialMemory\" { Properties { _T (\"T\", 2D) = \"white\" {} _T_ST (\"ST\", Vector) = (1, 1, 0, 0) } SubShader { Pass {} } }")]
        [InlineData("Shader \"Tests/MaterialMemory\" { SubShader { Pass { HLSLPROGRAM\n#if 0\nProperties { _Bogus (\"Bogus\", Float) = 1 }\n#endif\nENDHLSL\n} } }")]
        public void AnAmbiguousPropertyLayoutDoesNotReportAnEstimatedSize(string source)
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var shader = CreateShader(source);
            var material = new Material(shader);
            try { Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(material)); }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void AssigningAndClearingLocalKeywordsLeavesTheReservationUnmeasured()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var shader = CreateShader(PlainShader);
            var material = new Material(shader);
            try
            {
                Assert.Equal(1864, Profiler.GetRuntimeMemorySizeLong(material));
                var keyword = new LocalKeyword(shader, "MATERIAL_MEMORY_KEYWORD");
                material.enabledKeywords = new[] { keyword };
                Assert.True(material.IsKeywordEnabled(keyword));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(material));
                material.enabledKeywords = Array.Empty<LocalKeyword>();
                Assert.False(material.IsKeywordEnabled(keyword));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(material));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void APropertyBlockDrawCopyHasNoProfilerModelBeforeOrAfterReset()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            const string source = "Shader \"Tests/MaterialMemory\" { Properties { _Integer (\"I\", Integer) = 2 } SubShader { Pass {} } }";
            var shader = CreateShader(source);
            var original = new Material(shader);
            var owner = new GameObject();
            try
            {
                var renderer = owner.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = original;
                var block = new MaterialPropertyBlock();
                block.SetInteger("_Integer", 7);
                renderer.SetPropertyBlock(block);
                var copy = renderer.DrawnMaterial(0, original);
                Assert.NotSame(original, copy);
                Assert.Equal(7, copy.GetInteger("_Integer"));
                Assert.Equal(1864, Profiler.GetRuntimeMemorySizeLong(original));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(copy));
                block.SetInteger("_Integer", 11);
                renderer.SetPropertyBlock(block);
                Assert.Same(copy, renderer.DrawnMaterial(0, original));
                Assert.Equal(11, copy.GetInteger("_Integer"));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(copy));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void AReparsedShaderLayoutIsOnlyMeasuredForNewMaterials()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var shader = CreateShader(PlainShader);
            var original = new Material(shader);
            try
            {
                Assert.Equal(1864, Profiler.GetRuntimeMemorySizeLong(original));
                const string updated = "Shader \"Tests/MaterialMemory\" { Properties { _Parameter (\"P\", Color) = (0, 0, 0, 1) } SubShader { Pass {} } }";
                BuiltinShaders.ApplySource(shader, updated);
                var fresh = new Material(shader);
                try
                {
                    Assert.Equal(1888, Profiler.GetRuntimeMemorySizeLong(fresh));
                    Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(original));
                }
                finally { Object.DestroyImmediate(fresh); }
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void ARendererOwnedCopyKeepsItsOwnValueAndAnUnmeasuredAllocation()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var shader = CreateShader(PlainShader);
            var source = new Material(shader);
            var owner = new GameObject();
            try
            {
                source.SetFloat("_Parameter", 8f);
                var renderer = owner.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = source;
                var own = renderer.material;
                Assert.NotSame(source, own);
                Assert.Equal(8f, own.GetFloat("_Parameter"));
                own.SetFloat("_Parameter", 10f);
                Assert.Equal(8f, source.GetFloat("_Parameter"));
                Assert.Equal(1864, Profiler.GetRuntimeMemorySizeLong(source));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(own));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void CollectingAPropertyBlockDrawLeavesTheSourcePending()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var shader = CreateShader(PlainShader);
            var source = new Material(shader);
            var instance = Object.Instantiate(source);
            var owner = new GameObject();
            try
            {
                var renderer = owner.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = instance;
                var block = new MaterialPropertyBlock();
                block.SetFloat("_Parameter", 4f);
                renderer.SetPropertyBlock(block);
                var drawn = renderer.DrawnMaterial(0, instance);
                Assert.Equal(4f, drawn.GetFloat("_Parameter"));
                Assert.Equal(1416, Profiler.GetRuntimeMemorySizeLong(instance));
                drawn.Sync(profileDraw: false);
                Assert.Equal(1416, Profiler.GetRuntimeMemorySizeLong(instance));
                drawn.MarkProfilerDrawEmitted();
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(instance));
                Assert.Equal(0.5f, instance.GetFloat("_Parameter"));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(instance));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void AVisibleTilemapDrawConsumesThePendingMaterialBehindItsBlock()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var shader = CreateShader(PlainShader);
            var source = new Material(shader);
            var material = Object.Instantiate(source);
            var texture = new Texture2D(8, 8);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 8, 8), Vector2.zero, 8, 0, SpriteMeshType.FullRect);
            var tile = ScriptableObject.CreateInstance<Tile>();
            var grid = new GameObject("Grid", typeof(Grid));
            var owner = new GameObject("Tilemap", typeof(Tilemap), typeof(TilemapRenderer)) { layer = 28 };
            owner.transform.SetParent(grid.transform, false);
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var target = new RenderTexture(8, 8, 16);
            try
            {
                var renderer = owner.GetComponent<TilemapRenderer>();
                renderer.sharedMaterial = material;
                var block = new MaterialPropertyBlock();
                block.SetFloat("_Parameter", 4f);
                renderer.SetPropertyBlock(block);
                tile.sprite = sprite;
                owner.GetComponent<Tilemap>().SetTile(Vector3Int.zero, tile);
                var camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                cameraObject.transform.position = new Vector3(0f, 0f, -5f);
                camera.targetTexture = target;
                camera.cullingMask = 0;
                camera.Render();
                Assert.Equal(1416, Profiler.GetRuntimeMemorySizeLong(material));
                camera.cullingMask = 1 << owner.layer;
                camera.Render();
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(material));
                Assert.Equal(0.5f, material.GetFloat("_Parameter"));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(material));
            }
            finally
            {
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(grid);
                Object.DestroyImmediate(tile);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void ShaderLodCanRejectAnUploadedMaterialBeforeItsFirstDraw()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            const string declarations = "{\"found\":true,\"subshaders_known\":true,\"subshaders\":[{\"tags\":[],\"passes\":[{\"name\":\"P\",\"tags\":[]}],\"lod\":100,\"restricted\":false}]}";
            const string noPasses = "{\"found\":false,\"known\":true,\"shader\":\"\",\"tags\":{},\"passes\":[]}";
            const string onePass = "{\"found\":true,\"known\":true,\"shader\":\"Tests/MaterialMemory\",\"tags\":{},\"passes\":[{\"name\":\"P\",\"tags\":{}}]}";
            ShaderDeclarations.ResetForTests();
            ShaderPassInfo.ResetForTests();
            ShaderDeclarations.s_FrameForTests = 1;
            ShaderDeclarations.s_CallForTests = (_, _) => declarations;
            ShaderPassInfo.s_CallForTests = name => name.EndsWith("|lod=50", StringComparison.Ordinal) ? noPasses : onePass;
            var shader = CreateShader(PlainShader);
            var source = new Material(shader);
            var material = Object.Instantiate(source);
            var direct = Object.Instantiate(source);
            var owner = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var cameraObject = new GameObject("Camera", typeof(Camera));
            var target = new RenderTexture(8, 8, 16);
            try
            {
                owner.layer = 28;
                owner.GetComponent<MeshRenderer>().sharedMaterial = material;
                var camera = cameraObject.GetComponent<Camera>();
                camera.enabled = false;
                cameraObject.transform.position = new Vector3(0f, 0f, -5f);
                camera.targetTexture = target;
                camera.cullingMask = 1 << owner.layer;
                shader.maximumLOD = 50;
                Assert.True(shader.IsLodHidden);
                camera.Render();
                Assert.Equal(1416, Profiler.GetRuntimeMemorySizeLong(material));
                _ = direct.SetPass(0);
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(direct));
                shader.maximumLOD = 100;
                Assert.False(shader.IsLodHidden);
                camera.Render();
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(material));
            }
            finally
            {
                Immediate.PassMaterial = null;
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(direct);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(shader);
                ShaderDeclarations.s_CallForTests = null;
                ShaderDeclarations.s_FrameForTests = null;
                ShaderDeclarations.ResetForTests();
                ShaderPassInfo.ResetForTests();
            }
        }

        [Fact]
        public void AnInstantiatedMaterialKeepsItsHeaderUntilAPropertyQuery()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var shader = CreateShader(PlainShader);
            var source = new Material(shader);
            source.SetFloat("_Parameter", 8f);
            var instance = Object.Instantiate(source);
            try
            {
                Assert.Equal(1416, Profiler.GetRuntimeMemorySizeLong(instance));
                instance.renderQueue = 2200;
                Assert.Same(shader, instance.shader);
                Assert.Equal(1416, Profiler.GetRuntimeMemorySizeLong(instance));
                Assert.True(instance.HasFloat("_Parameter"));
                Assert.Equal(1864, Profiler.GetRuntimeMemorySizeLong(instance));
                Assert.Equal(8f, instance.GetFloat("_Parameter"));
                Assert.Equal(1864, Profiler.GetRuntimeMemorySizeLong(source));
            }
            finally
            {
                Object.DestroyImmediate(instance);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(shader);
            }
        }

        [Fact]
        public void AnUnmeasuredReleaseKeepsMaterialAllocationUnknown()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var shader = CreateShader(PlainShader);
            var material = new Material(shader);
            try
            {
                Assert.Equal(0.5f, material.GetFloat("_Parameter"));
                Assert.Equal(0, Profiler.GetRuntimeMemorySizeLong(material));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(shader);
            }
        }
    }
}
