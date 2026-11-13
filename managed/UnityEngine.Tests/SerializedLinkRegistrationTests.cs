using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine.AI;
using Xunit;

namespace UnityEngine.Tests;

#pragma warning disable CS0618
public class SerializedLinkRegistrationTests
{
    [Fact]
    public void LegacyNavigationLinkLoadsFromItsSerializedClass()
    {
        Assert.Equal(typeof(NavMeshObstacle), EngineTypes.ForClassId(208, "NavMeshObstacle", warn: false));
        const string yaml = """
            %YAML 1.1
            %TAG !u! tag:unity3d.com,2011:
            --- !u!1 &1
            GameObject:
              m_Name: serialized-link
              m_IsActive: 0
              m_Component:
              - component: {fileID: 4}
              - component: {fileID: 191}
            --- !u!4 &4
            Transform:
              m_GameObject: {fileID: 1}
              m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
              m_LocalPosition: {x: 0, y: 0, z: 0}
              m_LocalScale: {x: 1, y: 1, z: 1}
            --- !u!191 &191
            OffMeshLink:
              m_GameObject: {fileID: 1}
              m_Enabled: 0
              m_Activated: 0
            """;
        var file = ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "serialized-link.unity", null,
            ObjectGraphBuilder.Kind.Scene, null);
        try
        {
            var owner = Assert.IsType<GameObject>(file.Objects[1]);
            Assert.True(file.Objects.TryGetValue(191, out var component), "The serialized OffMeshLink document was not loaded.");
            var link = Assert.IsType<OffMeshLink>(component);
            Assert.Equal(typeof(OffMeshLink), EngineTypes.ForClassId(191, "OffMeshLink", warn: false));
            Assert.Same(owner, link.gameObject);
            Assert.Same(link, owner.GetComponent<OffMeshLink>());
            Assert.False(link.enabled);
        }
        finally
        {
            if (file.Objects.TryGetValue(1, out var owner)) Object.DestroyImmediate(owner);
        }
    }
}
#pragma warning restore CS0618
