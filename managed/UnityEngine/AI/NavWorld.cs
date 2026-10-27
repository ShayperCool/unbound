using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// The NavMesh world: the NavMeshData instances and links added to it, and per agent type a
    /// Detour mesh holding their tiles (in world space) with a query object. The meshes are rebuilt
    /// whenever data or links come and go; polygon references are only valid until then.
    /// </summary>
    internal static class NavWorld
    {
        internal sealed class DataInstance
        {
            internal int Id;
            internal NavMeshData Data;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal int Owner;
        }

        internal sealed class LinkInstance
        {
            internal int Id;
            internal NavMeshLinkData Link;
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal int Owner;
            internal bool Active = true;
            internal Vector3 Start => Position + Rotation * Link.startPosition;
            internal Vector3 End => Position + Rotation * Link.endPosition;
        }

        internal sealed class Surface
        {
            internal int AgentTypeID;
            internal NavMeshBuildSettings Settings;
            internal DtNavMesh Mesh;
            internal DtNavMeshQuery Query;
            /// <summary>Off-mesh connection polygons by link id.</summary>
            internal readonly Dictionary<long, int> LinkOfPoly = new Dictionary<long, int>();
        }

        static readonly List<DataInstance> s_Data = new List<DataInstance>();
        static readonly List<LinkInstance> s_Links = new List<LinkInstance>();
        static readonly Dictionary<int, Surface> s_Surfaces = new Dictionary<int, Surface>();
        static int s_NextId = 1;
        static bool s_Dirty;
        /// <summary>Bumped on every rebuild (agents re-find their polygons).</summary>
        internal static int Version;

        internal static IReadOnlyList<DataInstance> Data => s_Data;
        internal static IReadOnlyList<LinkInstance> Links => s_Links;

        internal static int AddData(NavMeshData data, Vector3 position, Quaternion rotation)
        {
            var instance = new DataInstance { Id = s_NextId++, Data = data, Position = position, Rotation = rotation };
            s_Data.Add(instance);
            s_Dirty = true;
            return instance.Id;
        }

        internal static void RemoveData(int id)
        {
            if (s_Data.RemoveAll(d => d.Id == id) > 0) s_Dirty = true;
        }

        internal static void RemoveAllData()
        {
            s_Data.Clear();
            s_Dirty = true;
        }

        internal static DataInstance FindData(int id) => s_Data.Find(d => d.Id == id);

        internal static int AddLink(NavMeshLinkData link, Vector3 position, Quaternion rotation)
        {
            var instance = new LinkInstance { Id = s_NextId++, Link = link, Position = position, Rotation = rotation };
            s_Links.Add(instance);
            s_Dirty = true;
            return instance.Id;
        }

        internal static void RemoveLink(int id)
        {
            if (s_Links.RemoveAll(l => l.Id == id) > 0) s_Dirty = true;
        }

        internal static LinkInstance FindLink(int id) => s_Links.Find(l => l.Id == id);

        internal static void MarkDirty() => s_Dirty = true;

        /// <summary>A data instance's tiles changed (rebuilt or updated in place).</summary>
        internal static void DataChanged(NavMeshData data)
        {
            foreach (var d in s_Data)
                if (ReferenceEquals(d.Data, data)) s_Dirty = true;
        }

        internal static IEnumerable<Surface> Surfaces
        {
            get
            {
                Refresh();
                return s_Surfaces.Values;
            }
        }

        internal static Surface Get(int agentTypeID)
        {
            Refresh();
            return s_Surfaces.TryGetValue(agentTypeID, out var surface) ? surface : null;
        }

        static void Refresh()
        {
            if (!s_Dirty) return;
            s_Dirty = false;
            Version++;
            s_Surfaces.Clear();
            foreach (var instance in s_Data)
            {
                if (instance.Data == null) continue;
                int agentType = instance.Data.m_Settings.agentTypeID;
                if (!s_Surfaces.TryGetValue(agentType, out var surface))
                {
                    var settings = instance.Data.m_Settings;
                    float tileWorld = settings.EffectiveTileSize * settings.EffectiveVoxelSize;
                    var param = new DtNavMeshParams
                    {
                        orig = RcVec3f.Zero,
                        tileWidth = tileWorld,
                        tileHeight = tileWorld,
                        maxTiles = 1 << 12,
                        maxPolys = 1 << 14,
                    };
                    surface = new Surface { AgentTypeID = agentType, Settings = settings, Mesh = new DtNavMesh() };
                    surface.Mesh.Init(param, DtDetour.DT_VERTS_PER_POLYGON);
                    s_Surfaces[agentType] = surface;
                }
                foreach (var tile in instance.Data.m_Tiles)
                {
                    var placed = Place(tile, instance.Position, instance.Rotation);
                    AddLinks(surface, placed);
                    // Tiles of different data at the same place go on further layers.
                    surface.Mesh.CalcTileLoc(Center(placed.header), out int tx, out int ty);
                    placed.header.x = tx;
                    placed.header.y = ty;
                    int layer = 0;
                    while (surface.Mesh.GetTileRefAt(tx, ty, layer) != 0) layer++;
                    placed.header.layer = layer;
                    surface.Mesh.AddTile(placed, 0, 0, out _);
                }
            }
            foreach (var surface in s_Surfaces.Values)
            {
                surface.Query = new DtNavMeshQuery(surface.Mesh);
                var polyDump = Environment.GetEnvironmentVariable("UNBOUND_NAVMESH_POLYS");
                if (polyDump != null)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < surface.Mesh.GetMaxTiles(); i++)
                    {
                        var tile = surface.Mesh.GetTile(i);
                        if (tile?.data == null) continue;
                        long baseRef = surface.Mesh.GetPolyRefBase(tile);
                        for (int p = 0; p < tile.data.header.polyCount; p++)
                        {
                            var poly = tile.data.polys[p];
                            sb.Append(baseRef | (long)p).Append(' ').Append(poly.GetArea()).Append(' ').Append(poly.GetPolyType());
                            for (int v = 0; v < poly.vertCount; v++)
                            {
                                int vi = poly.verts[v] * 3;
                                sb.Append(' ').Append(tile.data.verts[vi].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                                  .Append(tile.data.verts[vi + 1].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                                  .Append(tile.data.verts[vi + 2].ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                            }
                            sb.Append(" |");
                            for (int l = poly.firstLink; l != DtDetour.DT_NULL_LINK; l = tile.links[l].next)
                                sb.Append(' ').Append(tile.links[l].refs).Append(':').Append(tile.links[l].edge);
                            sb.Append('\n');
                        }
                    }
                    System.IO.File.WriteAllText(polyDump + "." + surface.AgentTypeID + ".txt", sb.ToString());
                }
                surface.LinkOfPoly.Clear();
                for (int i = 0; i < surface.Mesh.GetMaxTiles(); i++)
                {
                    var tile = surface.Mesh.GetTile(i);
                    if (tile?.data == null) continue;
                    long baseRef = surface.Mesh.GetPolyRefBase(tile);
                    for (int c = 0; c < tile.data.header.offMeshConCount; c++)
                    {
                        var con = tile.data.offMeshCons[c];
                        surface.LinkOfPoly[baseRef | (long)con.poly] = con.userId;
                    }
                }
            }
        }

        static RcVec3f Center(DtMeshHeader h) => new RcVec3f((h.bmin.X + h.bmax.X) * 0.5f, (h.bmin.Y + h.bmax.Y) * 0.5f, (h.bmin.Z + h.bmax.Z) * 0.5f);

        /// <summary>A copy of a tile moved into the world by a data instance's pose.</summary>
        static DtMeshData Place(DtMeshData tile, Vector3 position, Quaternion rotation)
        {
            var copy = new DtMeshData
            {
                header = Copy(tile.header),
                polys = new DtPoly[tile.polys.Length],
                verts = (float[])tile.verts.Clone(),
                detailMeshes = tile.detailMeshes,
                detailVerts = (float[])tile.detailVerts.Clone(),
                detailTris = tile.detailTris,
                bvTree = null,
                offMeshCons = (DtOffMeshConnection[])tile.offMeshCons.Clone(),
            };
            for (int i = 0; i < tile.polys.Length; i++)
            {
                var src = tile.polys[i];
                var poly = new DtPoly(i, DtDetour.DT_VERTS_PER_POLYGON);
                Array.Copy(src.verts, poly.verts, poly.verts.Length);
                Array.Copy(src.neis, poly.neis, poly.neis.Length);
                poly.flags = src.flags;
                poly.vertCount = src.vertCount;
                poly.areaAndtype = src.areaAndtype;
                copy.polys[i] = poly;
            }
            copy.header.bvNodeCount = 0;
            bool identity = position == Vector3.zero && rotation == Quaternion.identity;
            if (identity) return copy;
            void Move(float[] v)
            {
                for (int i = 0; i + 2 < v.Length; i += 3)
                {
                    var p = position + rotation * new Vector3(v[i], v[i + 1], v[i + 2]);
                    v[i] = p.x;
                    v[i + 1] = p.y;
                    v[i + 2] = p.z;
                }
            }
            Move(copy.verts);
            Move(copy.detailVerts);
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = -min;
            for (int i = 0; i < 8; i++)
            {
                var h = tile.header;
                var corner = new Vector3((i & 1) != 0 ? h.bmax.X : h.bmin.X, (i & 2) != 0 ? h.bmax.Y : h.bmin.Y, (i & 4) != 0 ? h.bmax.Z : h.bmin.Z);
                var p = position + rotation * corner;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            copy.header.bmin = new RcVec3f(min.x, min.y, min.z);
            copy.header.bmax = new RcVec3f(max.x, max.y, max.z);
            return copy;
        }

        static DtMeshHeader Copy(DtMeshHeader h) => new DtMeshHeader
        {
            magic = h.magic,
            version = h.version,
            x = h.x,
            y = h.y,
            layer = h.layer,
            userId = h.userId,
            polyCount = h.polyCount,
            vertCount = h.vertCount,
            maxLinkCount = h.maxLinkCount,
            detailMeshCount = h.detailMeshCount,
            detailVertCount = h.detailVertCount,
            detailTriCount = h.detailTriCount,
            bvNodeCount = h.bvNodeCount,
            offMeshConCount = h.offMeshConCount,
            offMeshBase = h.offMeshBase,
            walkableHeight = h.walkableHeight,
            walkableRadius = h.walkableRadius,
            walkableClimb = h.walkableClimb,
            bmin = h.bmin,
            bmax = h.bmax,
            bvQuantFactor = h.bvQuantFactor,
        };

        /// <summary>The active links of the surface's agent type whose start lies over this tile
        /// become its off-mesh connections (Detour connects their ends when tiles are added).</summary>
        static void AddLinks(Surface surface, DtMeshData tile)
        {
            var h = tile.header;
            var add = new List<(LinkInstance Link, Vector3 Start, Vector3 End)>();
            foreach (var link in s_Links)
            {
                if (!link.Active || link.Link.agentTypeID != surface.AgentTypeID) continue;
                var start = link.Start;
                if (start.x < h.bmin.X || start.x > h.bmax.X || start.z < h.bmin.Z || start.z > h.bmax.Z) continue;
                add.Add((link, start, link.End));
            }
            if (add.Count == 0) return;
            int baseVert = h.vertCount, basePoly = h.polyCount;
            var verts = new float[(h.vertCount + add.Count * 2) * 3];
            Array.Copy(tile.verts, verts, tile.verts.Length);
            var polys = new DtPoly[h.polyCount + add.Count];
            Array.Copy(tile.polys, polys, tile.polys.Length);
            var cons = new DtOffMeshConnection[tile.offMeshCons.Length + add.Count];
            Array.Copy(tile.offMeshCons, cons, tile.offMeshCons.Length);
            for (int i = 0; i < add.Count; i++)
            {
                var (link, start, end) = add[i];
                int v = baseVert + i * 2;
                verts[v * 3] = start.x; verts[v * 3 + 1] = start.y; verts[v * 3 + 2] = start.z;
                verts[v * 3 + 3] = end.x; verts[v * 3 + 4] = end.y; verts[v * 3 + 5] = end.z;
                var poly = new DtPoly(basePoly + i, DtDetour.DT_VERTS_PER_POLYGON);
                poly.verts[0] = v;
                poly.verts[1] = v + 1;
                poly.vertCount = 2;
                poly.flags = 1;
                poly.SetArea(link.Link.area);
                poly.SetPolyType(DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION);
                polys[basePoly + i] = poly;
                var con = new DtOffMeshConnection
                {
                    pos = new[] { new RcVec3f(start.x, start.y, start.z), new RcVec3f(end.x, end.y, end.z) },
                    rad = LinkSnapRadius,
                    poly = basePoly + i,
                    flags = link.Link.bidirectional ? DtDetour.DT_OFFMESH_CON_BIDIR : 0,
                    side = 0xff,
                    userId = link.Id,
                };
                cons[tile.offMeshCons.Length + i] = con;
            }
            tile.verts = verts;
            tile.polys = polys;
            tile.offMeshCons = cons;
            h.offMeshBase = h.offMeshConCount == 0 ? basePoly : h.offMeshBase;
            h.vertCount += add.Count * 2;
            h.polyCount += add.Count;
            h.offMeshConCount += add.Count;
            h.maxLinkCount += add.Count * 4;
            // The detail meshes cover the ground polygons only (Detour skips connection polygons).
        }

        /// <summary>How far a link's end may lie from the NavMesh and still connect.</summary>
        internal const float LinkSnapRadius = 0.1f;
    }

    /// <summary>Unity's area mask and costs as a Detour filter.</summary>
    internal sealed class AreaFilter : IDtQueryFilter
    {
        internal int Mask = -1;
        internal float[] Costs;

        internal static AreaFilter Of(int mask, float[] costs = null)
        {
            NavMeshAreas.Ensure();
            return new AreaFilter { Mask = mask, Costs = costs ?? NavMeshAreas.Costs };
        }

        public bool PassFilter(long refs, DtMeshTile tile, DtPoly poly) => (Mask & (1 << poly.GetArea())) != 0;

        public float GetCost(RcVec3f pa, RcVec3f pb, long prevRef, DtMeshTile prevTile, DtPoly prevPoly, long curRef, DtMeshTile curTile,
            DtPoly curPoly, long nextRef, DtMeshTile nextTile, DtPoly nextPoly)
        {
            return RcVec3f.Distance(pa, pb) * Costs[curPoly.GetArea()];
        }
    }

    /// <summary>The project's NavMesh areas and agent types (ProjectSettings/NavMeshAreas.asset).</summary>
    internal static class NavMeshAreas
    {
        internal static readonly string[] Names = new string[32];
        internal static readonly float[] Costs = new float[32];
        internal static readonly List<(string Name, NavMeshBuildSettings Settings)> Agents = new List<(string, NavMeshBuildSettings)>();
        static bool s_Loaded;

        internal static void Ensure()
        {
            if (s_Loaded) return;
            s_Loaded = true;
            for (int i = 0; i < 32; i++)
            {
                Names[i] = "";
                Costs[i] = 1f;
            }
            Names[0] = "Walkable";
            Names[1] = "Not Walkable";
            Names[2] = "Jump";
            Costs[2] = 2f;
            try
            {
                if (Native.IsReady && !string.IsNullOrEmpty(ProjectInfo.ProjectPath))
                {
                    var path = System.IO.Path.Combine(ProjectInfo.ProjectPath, "ProjectSettings", "NavMeshAreas.asset");
                    if (System.IO.File.Exists(path))
                        foreach (var doc in Native.LoadDocuments(path))
                            if (doc.ClassId == 126) Read(doc.Body);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: reading NavMeshAreas: {e.Message}");
            }
            if (Agents.Count == 0) Agents.Add(("Humanoid", NavMeshBuildSettings.Default));
        }

        static void Read(YNode body)
        {
            int i = 0;
            foreach (var area in body.Get("areas")?.Seq() ?? Array.Empty<YNode>())
            {
                if (i >= 32) break;
                Names[i] = area.Str("name");
                Costs[i] = area.Float("cost", 1f);
                i++;
            }
            var names = new List<string>();
            foreach (var n in body.Get("m_SettingNames")?.Seq() ?? Array.Empty<YNode>()) names.Add(n.Scalar);
            int k = 0;
            foreach (var s in body.Get("m_Settings")?.Seq() ?? Array.Empty<YNode>())
            {
                Agents.Add((k < names.Count ? names[k] : "", NavMeshBuildSettings.Read(s)));
                k++;
            }
        }
    }
}
