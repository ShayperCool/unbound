using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The errors of Resources, SceneManager, AssetBundle, Mesh, Texture2D and a bare Animator for
    /// bad arguments as the Unity 6000.0.71f1 Editor gives them (tests/projects/apierrors): what is
    /// thrown, logged (with its type) and answered, next to a valid call as a control.
    /// </summary>
    public sealed class ApiErrorsTests : IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly List<Object> m_Owned = new List<Object>();

        public ApiErrorsTests() => Application.logMessageReceived += Record;

        public void Dispose()
        {
            Application.logMessageReceived -= Record;
            foreach (var o in m_Owned) if (o != null) Object.DestroyImmediate(o);
        }

        void Record(string message, string stack, LogType type) => m_Logs.Add(type + ": " + message);

        string[] Logs(Action action)
        {
            m_Logs.Clear();
            action();
            return m_Logs.ToArray();
        }

        T Own<T>(T o) where T : Object
        {
            m_Owned.Add(o);
            return o;
        }

        [Fact]
        public void ResourcesRefuseMissingArgumentsAndObjectsThatAreNoAssets()
        {
            Assert.Equal("path", Assert.Throws<ArgumentNullException>(() => Resources.LoadAll(null)).ParamName);
            Assert.Equal("systemTypeInstance", Assert.Throws<ArgumentNullException>(() => Resources.LoadAll("x", null)).ParamName);
            Assert.Equal("systemTypeInstance", Assert.Throws<ArgumentNullException>(() => Resources.Load("x", null)).ParamName);
            Assert.Equal(new[] { "Error: FindAllObjectsOfType: Invalid Type" }, Logs(() => Assert.Null(Resources.FindObjectsOfTypeAll(null))));
            Assert.Equal(new[] { "Error: FindAllObjectsOfType: The type has to be derived from UnityEngine.Object. Type is String." },
                Logs(() => Assert.Null(Resources.FindObjectsOfTypeAll(typeof(string)))));
            var go = Own(new GameObject("Runtime"));
            // An object made at runtime is no asset, whatever its class.
            Assert.Equal(new[] { "Error: UnloadAsset can only be used on assets;" }, Logs(() => Resources.UnloadAsset(go)));
            Assert.Equal(new[] { "Error: UnloadAsset can only be used on assets;" }, Logs(() => Resources.UnloadAsset(go.transform)));
            // Control: a valid type finds the object without a log.
            Assert.Empty(Logs(() => Assert.Contains(go, Resources.FindObjectsOfTypeAll(typeof(GameObject)))));
        }

        [Fact]
        public void SceneLookupsAndCreationThrowTheEnginesErrors()
        {
            var created = new List<Scene>();
            try
            {
                int count = SceneManager.sceneCount;
                Assert.Equal($"Scene index \"{count}\" is out of range.", Assert.Throws<IndexOutOfRangeException>(() => SceneManager.GetSceneAt(count)).Message);
                Assert.Equal("Scene index \"-1\" is out of range.", Assert.Throws<IndexOutOfRangeException>(() => SceneManager.GetSceneAt(-1)).Message);
                Assert.Equal("GetSceneByBuildIndex: Invalid build index: 99\nTo add a scene to the build settings use the menu File->Build Settings...",
                    Assert.Throws<ArgumentException>(() => SceneManager.GetSceneByBuildIndex(99)).Message);
                Assert.Equal("sceneName", Assert.Throws<ArgumentNullException>(() => SceneManager.CreateScene(null)).ParamName);
                Assert.Equal("The input scene name cannot be empty", Assert.Throws<ArgumentException>(() => SceneManager.CreateScene("")).Message);
                var scene = SceneManager.CreateScene("ApiErrorsScene");
                created.Add(scene);
                Assert.Equal("Scene with name \"ApiErrorsScene\" already exists",
                    Assert.Throws<ArgumentException>(() => created.Add(SceneManager.CreateScene("ApiErrorsScene"))).Message);
                Assert.Equal("SceneManager.SetActiveScene failed; invalid scene", Assert.Throws<ArgumentException>(() => SceneManager.SetActiveScene(default)).Message);
                Assert.Equal("Source scene is invalid", Assert.Throws<ArgumentException>(() => SceneManager.MergeScenes(default, scene)).Message);
                var root = Own(new GameObject("Root"));
                Assert.Equal("Destination scene is not valid", Assert.Throws<ArgumentException>(() => SceneManager.MoveGameObjectToScene(root, default)).Message);
                Assert.Equal("go", Assert.Throws<ArgumentNullException>(() => SceneManager.MoveGameObjectToScene(null, scene)).ParamName);
                // Unloading by a build index outside the list is refused as an invalid scene.
                Assert.Equal("Scene to unload is invalid", Assert.Throws<ArgumentException>(() => SceneManager.UnloadSceneAsync(99)).Message);
                // Control: a valid destination takes the object.
                SceneManager.MoveGameObjectToScene(root, scene);
                Assert.Equal(scene, root.scene);
            }
            finally
            {
                foreach (var scene in created) if (scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            }
        }

        [Fact]
        public void SceneLoadsTheBuildListCannotServeLogTheEnginesErrors()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            const string hint = "\nTo add a scene to the active build profile or shared scene list use the menu File->Build Profiles";
            Assert.Equal(new[] { "Error: Scene 'NoSuchScene' couldn't be loaded because it has not been added to the active build profile or shared scene list or the AssetBundle has not been loaded." + hint },
                Logs(() => Assert.Null(SceneManager.LoadSceneAsync("NoSuchScene"))));
            Assert.Equal(new[] { "Error: Scene with build index: 99 couldn't be loaded because it has not been added to the active build profile or shared scene list." + hint },
                Logs(() => Assert.Null(SceneManager.LoadSceneAsync(99))));
            Assert.Equal(new[] { "Error: Cannot load scene: Invalid scene name (empty string) and invalid build index -1" },
                Logs(() => Assert.Null(SceneManager.LoadSceneAsync(-1))));
            Assert.Equal(new[] { "Error: Cannot load scene: Invalid scene name (empty string) and invalid build index -1" },
                Logs(() => Assert.Null(SceneManager.LoadSceneAsync(""))));
            // Control: a release without the measurement keeps the build settings wording.
            using (TestRelease.Pin(TestRelease.Unmeasured))
            {
                Assert.Equal(new[] { "Error: Scene with build index: 99 couldn't be loaded because it has not been added to the build settings." },
                    Logs(() => Assert.Null(SceneManager.LoadSceneAsync(99))));
                Assert.Equal(new[] { "Error: Scene with build index: -1 couldn't be loaded because it has not been added to the build settings." },
                    Logs(() => Assert.Null(SceneManager.LoadSceneAsync(-1))));
                Assert.Equal(new[] { "Error: Scene 'NoSuchScene' couldn't be loaded because it has not been added to the build settings or the AssetBundle has not been loaded." },
                    Logs(() => Assert.Null(SceneManager.LoadSceneAsync("NoSuchScene"))));
            }
        }

        [Fact]
        public void AssetBundleLoadsWithoutAnArchiveLogWhatTheEngineCouldNotRead()
        {
            Assert.Equal(new[] { "Error: Unable to open archive file: NoSuchFolder/no_such_bundle", "Error: Failed to read data for the AssetBundle 'no_such_bundle'." },
                Logs(() => Assert.Null(AssetBundle.LoadFromFile("NoSuchFolder/no_such_bundle"))));
            Assert.Equal(new[] { "Error: Failed to decompress data for the AssetBundle 'Memory'." },
                Logs(() => Assert.Null(AssetBundle.LoadFromMemory(new byte[16]))));
            Assert.Equal(new[] { "Error: Unable to read header from archive file: ", "Error: Failed to read data for the AssetBundle 'IO.Stream'." },
                Logs(() => Assert.Null(AssetBundle.LoadFromStream(new MemoryStream(new byte[16])))));
            var garbage = Path.Combine(Path.GetTempPath(), $"unbound-garbage-{Guid.NewGuid():N}.bundle");
            File.WriteAllBytes(garbage, new byte[16]);
            try
            {
                Assert.Equal(new[] { $"Error: Unable to read header from archive file: {garbage}", $"Error: Failed to read data for the AssetBundle '{Path.GetFileName(garbage)}'." },
                    Logs(() => Assert.Null(AssetBundle.LoadFromFile(garbage))));
            }
            finally { File.Delete(garbage); }
            Assert.Empty(Logs(() => Assert.Null(AssetBundle.LoadFromMemory(null))));
            // No data makes no request.
            Assert.Empty(Logs(() => Assert.Null(AssetBundle.LoadFromMemoryAsync(null))));
            // The release's reference assemblies pass the message as the parameter name.
            Assert.Equal("ManagedStream object must be non-null", Assert.Throws<ArgumentNullException>(() => AssetBundle.LoadFromStream(null)).ParamName);
        }

        Mesh Triangle()
        {
            var mesh = Own(new Mesh());
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            return mesh;
        }

        [Fact]
        public void MeshSubmeshAndIndexErrorsLogOrThrowAsTheReleaseDoes()
        {
            var mesh = Triangle();
            const string outOfRange = "Specified sub mesh is out of range. Must be greater or equal to 0 and less than subMeshCount.";
            Assert.Equal(new[] { "Error: Failed getting indices. Submesh index is out of bounds." }, Logs(() => Assert.Empty(mesh.GetIndices(5))));
            Assert.Equal(new[] { "Error: Failed getting triangles. Submesh index is out of bounds." }, Logs(() => Assert.Empty(mesh.GetTriangles(-1))));
            Assert.Equal(new[] { "Error: Failed getting topology. Submesh index is out of bounds." }, Logs(() => Assert.Equal(MeshTopology.Triangles, mesh.GetTopology(5))));
            Assert.Equal(outOfRange, Assert.Throws<IndexOutOfRangeException>(() => mesh.GetIndexStart(5)).Message);
            Assert.Equal(outOfRange, Assert.Throws<IndexOutOfRangeException>(() => mesh.GetIndexCount(5)).Message);
            Assert.Equal(outOfRange, Assert.Throws<IndexOutOfRangeException>(() => mesh.GetBaseVertex(5)).Message);
            Assert.Equal(outOfRange, Assert.Throws<IndexOutOfRangeException>(() => mesh.GetTriangles(new List<int>(), 5)).Message);
            Assert.Equal("triangles", Assert.Throws<ArgumentNullException>(() => mesh.GetTriangles(null, 0)).ParamName);
            Assert.Equal("Invalid submesh index - got -1, should be [0,1)", Assert.Throws<ArgumentException>(() => mesh.GetSubMesh(-1)).Message);
            Assert.Equal("The uv index is invalid. Must be in the range 0 to 7.", Assert.Throws<IndexOutOfRangeException>(() => mesh.GetUVs(9, new List<Vector2>())).Message);
            Assert.Equal("uvs", Assert.Throws<ArgumentNullException>(() => mesh.GetUVs(0, (List<Vector2>)null)).ParamName);
            Assert.Equal("uvIndex", Assert.Throws<ArgumentOutOfRangeException>(() => mesh.SetUVs(8, new Vector2[3])).ParamName);
            // The setters log and leave the mesh as it was.
            Assert.Equal(new[] { "Error: Failed getting triangles. Submesh index is out of bounds." }, Logs(() => mesh.SetTriangles(new[] { 0, 1, 2 }, 5)));
            Assert.Equal(new[] { "Error: Failed getting indices. Submesh index is out of bounds." }, Logs(() => mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 5)));
            Assert.Equal(new[] { "Error: Failed setting triangles. The number of supplied triangle indices must be a multiple of 3." },
                Logs(() => mesh.SetIndices(new[] { 0, 1, 2, 0 }, MeshTopology.Triangles, 0)));
            Assert.Equal(new[] { "Error: Failed setting triangles. Some indices are referencing out of bounds vertices. IndexCount: 4, VertexCount: 3" },
                Logs(() => mesh.SetIndices(new[] { 0, 1, 2, 7 }, MeshTopology.Lines, 0)));
            Assert.Equal(new[] { "Error: subMeshCount can't be set to negative value" }, Logs(() => mesh.subMeshCount = -1));
            Assert.Equal(new[] { 0, 1, 2 }, mesh.triangles);
            Assert.Equal(1, mesh.subMeshCount);
            // The triangles setter makes one submesh even when it then refuses the triangles.
            mesh.subMeshCount = 2;
            Assert.Single(Logs(() => mesh.triangles = new[] { 0, 1 }));
            Assert.Equal(1, mesh.subMeshCount);
            Assert.Equal(3u, mesh.GetIndexCount(0));
            // A null color list clears the channel.
            mesh.colors = new Color[3];
            mesh.SetColors((List<Color>)null);
            Assert.Empty(mesh.colors);
            // Controls: lines of an odd count are accepted; a valid submesh answers without logs.
            Assert.Empty(Logs(() => mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Lines, 0)));
            Assert.Empty(Logs(() => Assert.Equal(MeshTopology.Lines, mesh.GetTopology(0))));
        }

        const string NegativeSideAssert = "Assert: Assertion failed on expression: '!((width >> minSizeLog2) < 1 && (height >> minSizeLog2) < 1 && (depth >> minSizeLog2) < 1)'";

        [Fact]
        public void TextureSizesMipsAndBlocksFailWithTheEnginesTexts()
        {
            Assert.Equal(new[] { NegativeSideAssert, "Error: Texture must have width greater than 0." },
                Logs(() => Assert.Equal("Failed to create texture because of invalid parameters.", Assert.Throws<UnityException>(() => new Texture2D(-1, 4)).Message)));
            Assert.Equal(new[] { "Error: Texture must have width greater than 0." }, Logs(() => Assert.Throws<UnityException>(() => new Texture2D(0, 4))));
            Assert.Equal(new[] { NegativeSideAssert, "Error: Texture must have height greater than 0." }, Logs(() => Assert.Throws<UnityException>(() => new Texture2D(4, -1))));
            var t = Own(new Texture2D(4, 4, TextureFormat.RGBA32, true));
            Assert.Equal(new[] { NegativeSideAssert, "Error: Texture must have height greater than 0." }, Logs(() => Assert.False(t.Reinitialize(2, -1))));
            Assert.Equal((4, 4), (t.width, t.height));
            Assert.Equal("Texture2D.GetPixels: invalid mipmap level. (Texture '')", Assert.Throws<ArgumentException>(() => t.GetPixels(9)).Message);
            Assert.Equal("Texture2D.SetPixels32: invalid mipmap level. (Texture '')", Assert.Throws<ArgumentException>(() => t.SetPixels32(new Color32[16], 9)).Message);
            Assert.Equal("Texture2D.GetPixels: the size of data to be written is outside the target buffer bounds. (Texture '')",
                Assert.Throws<ArgumentException>(() => t.GetPixels(-1, 0, 2, 2)).Message);
            Assert.Equal("Texture2D.SetPixels: size of data to be filled was larger than the size of data available in the source array. (Texture '')",
                Assert.Throws<ArgumentException>(() => t.SetPixels(null)).Message);
            Assert.Equal("The passed in miplevel 9 is invalid. It needs to be in the range 0 and 2",
                Assert.Throws<ArgumentException>(() => t.GetPixelData<byte>(9)).Message);
            Assert.Equal(new[] { "Error: GetPixel called on an undefined mip (valid values are 0 - 2" }, Logs(() => Assert.Equal(Color.white, t.GetPixel(0, 0, 9))));
            Assert.Equal(new[] { "Error: GetPixelBilinear called on an undefined mip (valid values are 0 - 2" }, Logs(() => t.GetPixelBilinear(0f, 0f, 9)));
            // Controls: an empty texture and a valid size are made without logs.
            Assert.Empty(Logs(() => Own(new Texture2D(0, 0))));
            Assert.Empty(Logs(() => Assert.True(t.Reinitialize(2, 2))));
        }

        [Fact]
        public void ACopyRegionOutsideEitherTextureCopiesNothing()
        {
            var source = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false));
            var destination = Own(new Texture2D(4, 4, TextureFormat.RGBA32, false));
            source.SetPixel(0, 0, Color.red);
            source.Apply();
            destination.SetPixel(0, 0, Color.blue);
            destination.Apply();
            Assert.Equal(new[] { "Error: Graphics.CopyTexture called with region not fitting in source element: trying to copy from region on GPU (x:2, y:2, width:4, height:4) of mip 0 (size: 4x4)" },
                Logs(() => Graphics.CopyTexture(source, 0, 0, 2, 2, 4, 4, destination, 0, 0, 0, 0)));
            Assert.Equal(new[] { "Error: Graphics.CopyTexture called with region not fitting in destination element: trying to copy to region on GPU (x:2, y:2, width:4, height:4) of mip 0 (size: 4x4)" },
                Logs(() => Graphics.CopyTexture(source, 0, 0, 0, 0, 4, 4, destination, 0, 0, 2, 2)));
            Assert.Equal(Color.blue, destination.GetPixel(0, 0));
            // Control: a region inside both copies.
            Assert.Empty(Logs(() => Graphics.CopyTexture(source, 0, 0, 0, 0, 2, 2, destination, 0, 0, 0, 0)));
            Assert.Equal(Color.red, destination.GetPixel(0, 0));
        }

        [Fact]
        public void AWholeCopyNeedsMatchingSizesWhileALevelCopyTakesTheBytesInOrder()
        {
            var wide = Own(new Texture2D(8, 4, TextureFormat.RGBA32, false));
            var tall = Own(new Texture2D(4, 8, TextureFormat.RGBA32, false));
            wide.SetPixels32(new Color32[32]);
            wide.SetPixel(0, 0, Color.red);
            wide.SetPixel(3, 3, Color.green);
            wide.SetPixel(7, 3, Color.white);
            wide.Apply();
            tall.SetPixel(0, 0, Color.blue);
            tall.Apply();
            Assert.Equal(new[] { "Error: Graphics.CopyTexture called with mismatching texture sizes (src 8x4x1 (array length 0), dst 4x8x1 (array length 0))" },
                Logs(() => Graphics.CopyTexture(wide, tall)));
            Assert.Equal(Color.blue, tall.GetPixel(0, 0));
            // A level copy of as many bytes takes them in order: texel i of the source is texel i
            // of the destination (tests/projects/apierrors texture.copy_case.element_same_bytes_*).
            Assert.Empty(Logs(() => Graphics.CopyTexture(wide, 0, 0, tall, 0, 0)));
            Assert.Equal(Color.red, tall.GetPixel(0, 0));
            Assert.Equal(Color.green, tall.GetPixel(3, 6));
            Assert.Equal(Color.white, tall.GetPixel(3, 7));
            Assert.Equal(new Color(0, 0, 0, 0), tall.GetPixel(3, 3));
            // Control: a whole copy of the same size copies.
            var same = Own(new Texture2D(8, 4, TextureFormat.RGBA32, false));
            Assert.Empty(Logs(() => Graphics.CopyTexture(wide, same)));
            Assert.Equal(Color.white, same.GetPixel(7, 3));
        }

        [Fact]
        public void ProceduralStandInsKeepTheirWholeTriangles()
        {
            string[] logs = null;
            Mesh mesh = null;
            logs = Logs(() => mesh = Unbound.Runtime.ProceduralMeshes.Get(MeshTopology.Triangles, Unbound.Runtime.ProceduralMeshes.IndirectVertices));
            Assert.Empty(logs);
            Assert.Equal(65535u, mesh.GetIndexCount(0));
            Assert.Equal(4u, Unbound.Runtime.ProceduralMeshes.Get(MeshTopology.Points, 4).GetIndexCount(0));
            Assert.Equal(3u, Unbound.Runtime.ProceduralMeshes.Get(MeshTopology.Triangles, 4).GetIndexCount(0));
        }

        [Fact]
        public void AnAnimatorWithoutAControllerWarnsForEachLookup()
        {
            var animator = Own(new GameObject("Bare")).AddComponent<Animator>();
            const string notPlaying = "Warning: Animator is not playing an AnimatorController";
            Assert.Equal(new[] { notPlaying }, Logs(() => Assert.Equal(0, animator.layerCount)));
            Assert.Equal(new[] { notPlaying }, Logs(() => Assert.Equal(0, animator.parameterCount)));
            Assert.Equal(new[] { notPlaying }, Logs(() => Assert.Equal("", animator.GetLayerName(0))));
            Assert.Equal(new[] { notPlaying }, Logs(() => Assert.Equal(-1f, animator.GetLayerWeight(0))));
            Assert.Equal(new[] { notPlaying }, Logs(() => Assert.False(animator.IsInTransition(0))));
            Assert.Equal(new[] { notPlaying, notPlaying },
                Logs(() => Assert.Equal("Index must be between 0 and 0", Assert.Throws<IndexOutOfRangeException>(() => animator.GetParameter(0)).Message)));
            Assert.Equal(new[] { "Warning: Animator does not have an AnimatorController" }, Logs(() => animator.Play("x")));
            Assert.Equal(new[] { "Warning: Animator does not have an AnimatorController" }, Logs(() => animator.CrossFade("x", 0.1f)));
            // Control: with logWarnings off nothing is written.
            animator.logWarnings = false;
            Assert.Empty(Logs(() => animator.Play("x")));
        }
    }
}
