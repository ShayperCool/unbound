using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Unbound.Runtime;

namespace UnityEngine.AI
{
    /// <summary>
    /// A built NavMesh: Detour tiles in the data's own space, the settings it was built with and
    /// the bounds of its sources. The Editor's baked assets hold Unity's tile format (read by
    /// <see cref="UnityNavMeshTiles"/>); runtime builds make tiles with Recast.
    /// </summary>
    [ClassId(238)]
    public sealed partial class NavMeshData : Object, ISerializedAsset
    {
        internal readonly List<DtMeshData> m_Tiles = new List<DtMeshData>();
        internal NavMeshBuildSettings m_Settings = NavMeshBuildSettings.Default;
        internal Bounds m_SourceBounds;
        internal Vector3 m_Position;
        internal Quaternion m_Rotation = Quaternion.identity;
        /// <summary>Bumped when the tiles change (instances of the data rebuild their world).</summary>
        internal int m_Version;

        public NavMeshData() : this(0) { }

        public NavMeshData(int agentTypeID)
        {
            m_Settings = NavMesh.GetSettingsByID(agentTypeID);
            m_Settings.agentTypeID = agentTypeID;
        }

        public Bounds sourceBounds => m_SourceBounds;

        public Vector3 position
        {
            get => m_Position;
            set => m_Position = value;
        }

        public Quaternion rotation
        {
            get => m_Rotation;
            set => m_Rotation = value;
        }

        internal bool hasHeightMeshData => false;

        internal NavMeshBuildSettings buildSettings => m_Settings;

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Settings = NavMeshBuildSettings.Read(body.Get("m_NavMeshBuildSettings"));
            m_Settings.agentTypeID = body.Int("m_AgentTypeID", m_Settings.agentTypeID);
            var bounds = body.Get("m_SourceBounds");
            if (bounds != null) m_SourceBounds = new Bounds(bounds.Vector3("m_Center"), bounds.Vector3("m_Extent") * 2f);
            m_Position = body.Vector3("m_Position");
            m_Rotation = body.Quaternion("m_Rotation");
            m_Tiles.Clear();
            foreach (var tile in body.Get("m_NavMeshTiles")?.Seq() ?? Array.Empty<YNode>())
            {
                var hex = tile.Str("m_MeshData");
                if (string.IsNullOrEmpty(hex)) continue;
                try
                {
                    m_Tiles.Add(UnityNavMeshTiles.Read(Convert.FromHexString(hex), m_Settings));
                }
                catch (Exception e)
                {
                    Debug.LogError($"Unbound: NavMeshData '{m_Name}': a tile could not be read ({e.Message}); it is left out.");
                }
            }
            if ((body.Get("m_OffMeshLinks")?.Seq() is IEnumerable<YNode> links) && links.GetEnumerator().MoveNext())
                Debug.LogWarning($"Unbound: NavMeshData '{m_Name}' has baked off-mesh links, which are not supported yet; they are left out.");
            m_Version++;
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;
    using UnityEngine.AI;

    /// <summary>
    /// Unity's NavMesh tile format (version 16, as the 6000.0 Editor bakes it), read into Detour
    /// tile data: a header of counts and bounds (no link or off-mesh counts), vertices, polygons
    /// of 32 bytes (6 vertex and 6 neighbour indices, flags, vertex count, area), detail meshes
    /// (bases and 16-bit counts), detail vertices, detail triangles of four 16-bit values, and a
    /// bounding-volume tree (rebuilt by Detour's linear search here: it is left out).
    /// </summary>
    internal static class UnityNavMeshTiles
    {
        const int Magic = 'D' << 24 | 'N' << 16 | 'A' << 8 | 'V';
        const int Version = 16;
        const int PolySize = 32;

        internal static DtMeshData Read(byte[] b, NavMeshBuildSettings settings)
        {
            int o = 0;
            int I() { int v = BitConverter.ToInt32(b, o); o += 4; return v; }
            float F() { float v = BitConverter.ToSingle(b, o); o += 4; return v; }
            ushort U() { ushort v = BitConverter.ToUInt16(b, o); o += 2; return v; }
            if (I() != Magic) throw new FormatException("not a NavMesh tile");
            int version = I();
            if (version != Version) throw new FormatException($"tile format version {version} (only {Version} is known)");
            var header = new DtMeshHeader
            {
                magic = DtDetour.DT_NAVMESH_MAGIC,
                version = DtDetour.DT_NAVMESH_VERSION,
                x = I(),
                y = I(),
                layer = I(),
                polyCount = I(),
                vertCount = I(),
                detailMeshCount = I(),
                detailVertCount = I(),
                detailTriCount = I(),
                bvNodeCount = I(),
            };
            header.bmin = new RcVec3f(F(), F(), F());
            header.bmax = new RcVec3f(F(), F(), F());
            header.bvQuantFactor = F();
            header.walkableHeight = settings.agentHeight;
            header.walkableRadius = settings.agentRadius;
            header.walkableClimb = settings.agentClimb;
            var data = new DtMeshData { header = header };
            data.verts = new float[header.vertCount * 3];
            for (int i = 0; i < data.verts.Length; i++) data.verts[i] = F();
            data.polys = new DtPoly[header.polyCount];
            int edges = 0;
            for (int i = 0; i < header.polyCount; i++)
            {
                int start = o;
                var poly = new DtPoly(i, DtDetour.DT_VERTS_PER_POLYGON);
                for (int k = 0; k < 6; k++) poly.verts[k] = U();
                for (int k = 0; k < 6; k++) poly.neis[k] = U();
                poly.flags = BitConverter.ToInt32(b, start + 24);
                poly.vertCount = b[start + 28];
                poly.areaAndtype = b[start + 29];
                edges += poly.vertCount;
                data.polys[i] = poly;
                o = start + PolySize;
            }
            header.maxLinkCount = edges * 4 + 16;
            data.detailMeshes = new DtPolyDetail[header.detailMeshCount];
            for (int i = 0; i < header.detailMeshCount; i++)
            {
                int vertBase = I(), triBase = I();
                int vertCount = U(), triCount = U();
                data.detailMeshes[i] = new DtPolyDetail(vertBase, triBase, (byte)vertCount, (byte)triCount);
            }
            data.detailVerts = new float[header.detailVertCount * 3];
            for (int i = 0; i < data.detailVerts.Length; i++) data.detailVerts[i] = F();
            data.detailTris = new int[header.detailTriCount * 4];
            for (int i = 0; i < data.detailTris.Length; i++) data.detailTris[i] = U();
            // The bounding volume tree (Detour's nodes: quantized bounds, then the polygon index or a
            // negative escape): queries walk it, so polygons found together come in Unity's order.
            data.bvTree = null;
            if (header.bvNodeCount > 0 && o + header.bvNodeCount * 16 <= b.Length)
            {
                var nodes = new DtBVNode[header.bvNodeCount];
                bool valid = true;
                for (int i = 0; i < nodes.Length; i++)
                {
                    var node = new DtBVNode();
                    node.bmin = new RcVec3i(U(), U(), U());
                    node.bmax = new RcVec3i(U(), U(), U());
                    node.i = I();
                    if (node.i >= header.polyCount || node.i < -nodes.Length) valid = false;
                    nodes[i] = node;
                }
                if (valid) data.bvTree = nodes;
            }
            if (data.bvTree == null) header.bvNodeCount = 0;
            data.offMeshCons = Array.Empty<DtOffMeshConnection>();
            header.offMeshConCount = 0;
            header.offMeshBase = header.polyCount;
            return data;
        }
    }
}
