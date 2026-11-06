using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A scene's legacy-baked NavMesh (NavMeshSettings.m_NavMeshData) comes and goes with
    /// the scene, with the links its bake generated (the reference behaviour is
    /// tests/projects/navbaked, Unity 6000.0.71f1).</summary>
    [Collection("Frames")]
    public sealed class SceneNavMeshTests : IDisposable
    {
        const string Settings = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!196 &4\nNavMeshSettings:\n" +
            "  serializedVersion: 2\n  m_NavMeshData: {fileID: 23800000, guid: 0123456789abcdef0123456789abcdef, type: 2}\n";

        sealed class Resolver : IReferenceResolver
        {
            internal Object Data;
            public Object Resolve(ObjectRef reference, Type expected) => Data;
        }

        readonly SceneData m_Scene = new SceneData { Handle = new SceneHandle(1_000_002), Path = "Assets/Level.unity" };

        public void Dispose() => SceneNavMesh.SceneUnloaded(m_Scene.Handle.Value);

        static NavMeshBuildSource Platform(Vector3 top, float width) => new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = new Vector3(width, 1f, 6f),
            transform = Matrix4x4.Translate(top + new Vector3(0f, -0.5f, 0f)),
            area = 0,
        };

        /// <summary>A 4 m ledge 1.5 m above a ground in front of it, and the bake's one-way drop
        /// link from the ledge's edge to the ground.</summary>
        static NavMeshData Ledge()
        {
            var sources = new List<NavMeshBuildSource> { Platform(new Vector3(0f, 1.5f, 0f), 4f), Platform(new Vector3(6f, 0f, 0f), 6f) };
            var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources,
                new Bounds(new Vector3(3f, 0f, 0f), new Vector3(14f, 6f, 8f)), Vector3.zero, Quaternion.identity);
            data.m_Links.Add(new BakedLink
            {
                Start = new Vector3(1.4f, 1.5f, 0f),
                End = new Vector3(3.6f, 0f, 0f),
                Radius = 0.5f,
                Type = OffMeshLinkType.LinkTypeDropDown,
                Bidirectional = false,
            });
            return data;
        }

        [Fact]
        public void TheBakedNavMeshAndItsLinksComeAndGoWithTheScene()
        {
            var high = new Vector3(-1f, 1.5f, 0f);
            var low = new Vector3(7f, 0f, 0f);
            var document = UnityYaml.Parse(Settings)[0];
            SceneSettings.Apply(document, new Resolver { Data = Ledge() }, m_Scene);

            Assert.True(NavMesh.SamplePosition(low, out _, 0.5f, NavMesh.AllAreas));
            var link = Assert.Single(NavWorld.Links, l => l.Data != 0);
            Assert.Equal(OffMeshLinkType.LinkTypeDropDown, link.Type);
            var path = new NavMeshPath();
            Assert.True(NavMesh.CalculatePath(high, low, NavMesh.AllAreas, path));
            Assert.Equal(NavMeshPathStatus.PathComplete, path.status);
            // One way: no way back up.
            Assert.True(NavMesh.CalculatePath(low, high, NavMesh.AllAreas, path));
            Assert.Equal(NavMeshPathStatus.PathPartial, path.status);

            // Loading the scene again replaces its NavMesh instead of adding a second one.
            SceneSettings.Apply(document, new Resolver { Data = Ledge() }, m_Scene);
            Assert.Single(NavWorld.Links, l => l.Data != 0);

            SceneNavMesh.SceneUnloaded(m_Scene.Handle.Value);
            Assert.False(NavMesh.SamplePosition(low, out _, 0.5f, NavMesh.AllAreas));
            Assert.DoesNotContain(NavWorld.Links, l => l.Data != 0);
        }

        [Fact]
        public void ASceneWithoutBakedDataAddsNoNavMesh()
        {
            int before = NavWorld.Data.Count;
            var document = UnityYaml.Parse(Settings.Replace("{fileID: 23800000, guid: 0123456789abcdef0123456789abcdef, type: 2}", "{fileID: 0}"))[0];
            SceneSettings.Apply(document, new Resolver { Data = Ledge() }, m_Scene);
            Assert.Equal(before, NavWorld.Data.Count);
        }
    }
}
