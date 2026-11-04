using Unbound.Runtime;
using UnityEngine.Rendering;
using Xunit;

namespace UnityEngine.Tests
{
    // A mesh in Unity's own serialization with a 32-bit index buffer and two sub-meshes, read
    // through the asset reader. Documented basis, Unity 6000.0:
    // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Mesh-indexFormat.html
    //   "Changing indexFormat sets Mesh.subMeshCount to one and clears the index buffer."
    // A reader that sets subMeshCount before switching a new mesh to UInt32 loses its sub-meshes,
    // so every serialized sub-mesh must still be there with its own indices, topology and base
    // vertex. No test uploads a mesh.
    public class MeshSerializedUInt32SubmeshTests
    {
        // Six positions (float3, stream 0): (0,0,0) (1,0,0) (0,1,0) (1,1,0) (2,0,0) (2,1,0).
        // UInt32 indices: sub-mesh 0 = triangle 0 1 2 at byte 0; sub-mesh 1 = lines 0 1 1 2 at
        // byte 12 with base vertex 3.
        const string WideTwoSubmeshMesh = @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!43 &4300000
Mesh:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_Name: WideTwoSubmeshes
  serializedVersion: 11
  m_SubMeshes:
  - serializedVersion: 2
    firstByte: 0
    indexCount: 3
    topology: 0
    baseVertex: 0
    firstVertex: 0
    vertexCount: 3
    localAABB:
      m_Center: {x: 0.5, y: 0.5, z: 0}
      m_Extent: {x: 0.5, y: 0.5, z: 0}
  - serializedVersion: 2
    firstByte: 12
    indexCount: 4
    topology: 3
    baseVertex: 3
    firstVertex: 3
    vertexCount: 3
    localAABB:
      m_Center: {x: 1.5, y: 0.5, z: 0}
      m_Extent: {x: 0.5, y: 0.5, z: 0}
  m_Shapes:
    vertices: []
    shapes: []
    channels: []
    fullWeights: []
  m_BindPose: []
  m_BoneNameHashes:
  m_RootBoneNameHash: 0
  m_BonesAABB: []
  m_VariableBoneCountWeights:
    m_Data:
  m_MeshCompression: 0
  m_IsReadable: 1
  m_KeepVertices: 1
  m_KeepIndices: 1
  m_IndexFormat: 1
  m_IndexBuffer: 00000000010000000200000000000000010000000100000002000000
  m_VertexData:
    serializedVersion: 3
    m_VertexCount: 6
    m_Channels:
    - stream: 0
      offset: 0
      format: 0
      dimension: 3
    m_DataSize: 72
    _typelessdata: 0000000000000000000000000000803f0000000000000000000000000000803f000000000000803f0000803f00000000000000400000000000000000000000400000803f00000000
  m_LocalAABB:
    m_Center: {x: 1, y: 0.5, z: 0}
    m_Extent: {x: 1, y: 0.5, z: 0}
  m_MeshUsageFlags: 0
  m_BakedConvexCollisionMesh:
  m_BakedTriangleCollisionMesh:
";

        [Fact]
        public void SerializedUInt32MeshKeepsEverySubmeshIndicesTopologyAndBaseVertex()
        {
            var mesh = new Mesh();
            try
            {
                ((ISerializedAsset)mesh).ReadSerialized(TestYaml.DocumentBody(WideTwoSubmeshMesh), new NullResolver());

                Assert.Equal(IndexFormat.UInt32, mesh.indexFormat);
                Assert.Equal(6, mesh.vertexCount);
                Assert.Equal(new Vector3(2, 1, 0), mesh.vertices[5]);
                Assert.Equal(2, mesh.subMeshCount);

                Assert.Equal(MeshTopology.Triangles, mesh.GetTopology(0));
                Assert.Equal(0u, mesh.GetBaseVertex(0));
                Assert.Equal(new[] { 0, 1, 2 }, mesh.GetIndices(0, false));

                Assert.Equal(MeshTopology.Lines, mesh.GetTopology(1));
                Assert.Equal(3u, mesh.GetBaseVertex(1));
                Assert.Equal(new[] { 0, 1, 1, 2 }, mesh.GetIndices(1, false));
                Assert.Equal(new[] { 3, 4, 4, 5 }, mesh.GetIndices(1));

                // The rest of the document is read after the indices.
                Assert.Equal(new Vector3(1, 0.5f, 0), mesh.bounds.center);
                Assert.Equal(new Vector3(1, 0.5f, 0), mesh.bounds.extents);
            }
            finally { Object.DestroyImmediate(mesh); }
        }
    }
}
