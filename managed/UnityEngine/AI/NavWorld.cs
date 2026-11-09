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
    /// whenever data or links come and go; polygon references are only valid until then. A carving
    /// change replaces only the tiles it reaches (their polygon references change, the others' stay).
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
            /// <summary>The data instance whose bake generated the link (0: added by itself).</summary>
            internal int Data;
            internal OffMeshLinkType Type;
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
            /// <summary>The data instance each tile came from, by tile index.</summary>
            internal readonly Dictionary<int, int> DataOfTile = new Dictionary<int, int>();
            /// <summary>Every tile in the mesh: where it came from and where it lies.</summary>
            internal readonly List<PlacedTile> Tiles = new List<PlacedTile>();
        }

        /// <summary>A data instance's tile as placed in a surface's mesh.</summary>
        internal sealed class PlacedTile
        {
            internal DataInstance Instance;
            /// <summary>Its index in the data's tiles.</summary>
            internal int Index;
            /// <summary>Its reference in the mesh (a new one when it is cut again).</summary>
            internal long Ref;
            internal float MinX, MaxX, MinZ, MaxZ;
        }

        static readonly List<DataInstance> s_Data = new List<DataInstance>();
        static readonly List<LinkInstance> s_Links = new List<LinkInstance>();
        static readonly Dictionary<int, Surface> s_Surfaces = new Dictionary<int, Surface>();
        static int s_NextId = 1;
        static bool s_Dirty;
        /// <summary>Bumped on every rebuild (agents re-find their polygons).</summary>
        internal static int Version;
        /// <summary>Bumped when tiles are cut again in place (carving): the polygons of those tiles
        /// are new, every other polygon reference stays valid.</summary>
        internal static int TileVersion;
        /// <summary>Changes on every change of the meshes (a rebuild or tiles cut again).</summary>
        internal static int Changes => Version + TileVersion;
        /// <summary>Bumped by the NavMesh changes that make every agent's path stale
        /// (NavMeshAgent.isPathStale): area costs changed, links enabled, disabled or activated.</summary>
        internal static int PathStaleVersion;

        internal static void MarkPathsStale() => PathStaleVersion++;

        /// <summary>The id of the data instance a polygon of the surface came from; 0 for none.</summary>
        internal static int DataOfPoly(Surface surface, long polyRef) =>
            surface != null && polyRef != 0 && surface.DataOfTile.TryGetValue(DtDetour.DecodePolyIdTile(polyRef), out int id) ? id : 0;

        internal static IReadOnlyList<DataInstance> Data => s_Data;
        internal static IReadOnlyList<LinkInstance> Links => s_Links;

        internal static int AddData(NavMeshData data, Vector3 position, Quaternion rotation)
        {
            var instance = new DataInstance { Id = s_NextId++, Data = data, Position = position, Rotation = rotation };
            s_Data.Add(instance);
            // The links the bake generated come with the data, placed by its pose.
            foreach (var baked in data.m_Links)
            {
                var link = new NavMeshLinkData
                {
                    startPosition = baked.Start,
                    endPosition = baked.End,
                    bidirectional = baked.Bidirectional,
                    area = baked.Area,
                    agentTypeID = data.m_Settings.agentTypeID,
                };
                s_Links.Add(new LinkInstance
                {
                    Id = s_NextId++, Link = link, Position = position, Rotation = rotation,
                    Data = instance.Id, Type = baked.Type,
                });
            }
            s_Dirty = true;
            return instance.Id;
        }

        internal static void RemoveData(int id)
        {
            if (s_Data.RemoveAll(d => d.Id == id) > 0) s_Dirty = true;
            s_Links.RemoveAll(l => l.Data == id);
        }

        internal static void RemoveAllData()
        {
            s_Data.Clear();
            s_Links.RemoveAll(l => l.Data != 0);
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

        /// <summary>Rebuilds the NavMesh now if it changed (instead of at the next query).</summary>
        internal static void RefreshNow() => Refresh();

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
                for (int index = 0; index < instance.Data.m_Tiles.Count; index++)
                {
                    var tile = instance.Data.m_Tiles[index];
                    var placed = NavMeshCarving.Carve(Place(tile, instance.Position, instance.Rotation), instance.Data.m_Settings, ObstacleSystem.Holes);
                    AddLinks(surface, placed);
                    // Tiles of different data at the same place go on further layers.
                    surface.Mesh.CalcTileLoc(Center(placed.header), out int tx, out int ty);
                    placed.header.x = tx;
                    placed.header.y = ty;
                    int layer = 0;
                    while (surface.Mesh.GetTileRefAt(tx, ty, layer) != 0) layer++;
                    placed.header.layer = layer;
                    surface.Mesh.AddTile(placed, 0, 0, out long tileRef);
                    if (tileRef == 0) continue;
                    surface.DataOfTile[DtDetour.DecodePolyIdTile(tileRef)] = instance.Id;
                    surface.Tiles.Add(new PlacedTile
                    {
                        Instance = instance, Index = index, Ref = tileRef,
                        MinX = placed.header.bmin.X, MaxX = placed.header.bmax.X, MinZ = placed.header.bmin.Z, MaxZ = placed.header.bmax.Z,
                    });
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
                    System.IO.File.WriteAllText(polyDump + "." + surface.AgentTypeID + "." + (s_PolyDumps++) + ".txt", sb.ToString());
                }
                surface.LinkOfPoly.Clear();
                for (int i = 0; i < surface.Mesh.GetMaxTiles(); i++)
                {
                    var tile = surface.Mesh.GetTile(i);
                    if (tile?.data != null) MapLinks(surface, tile, true);
                }
            }
        }

        /// <summary>The tile's off-mesh connection polygons entered in (or removed from) the
        /// surface's links by polygon.</summary>
        static void MapLinks(Surface surface, DtMeshTile tile, bool add)
        {
            long baseRef = surface.Mesh.GetPolyRefBase(tile);
            for (int c = 0; c < tile.data.header.offMeshConCount; c++)
            {
                var con = tile.data.offMeshCons[c];
                if (add) surface.LinkOfPoly[baseRef | (long)con.poly] = con.userId;
                else surface.LinkOfPoly.Remove(baseRef | (long)con.poly);
            }
        }

        /// <summary>Carving holes changed: the tiles the changed holes (their outlines before and
        /// after) reach are cut again and replaced in their meshes, the others are kept with their
        /// polygon references. A NavMesh waiting for a rebuild is rebuilt whole instead.</summary>
        internal static void HolesChanged(IReadOnlyList<NavMeshCarving.Footprint> changed)
        {
            if (s_Dirty)
            {
                Refresh();
                return;
            }
            bool replaced = false;
            // Each changed hole's bounds, per agent radius (usually one).
            var bounds = new Dictionary<float, (float MinX, float MaxX, float MinZ, float MaxZ)[]>();
            foreach (var surface in s_Surfaces.Values)
            {
                foreach (var placed in surface.Tiles)
                {
                    var settings = placed.Instance.Data.m_Settings;
                    if (!bounds.TryGetValue(settings.agentRadius, out var holes))
                    {
                        holes = new (float, float, float, float)[changed.Count];
                        for (int i = 0; i < holes.Length; i++) holes[i] = NavMeshCarving.HoleBounds(changed[i], settings.agentRadius);
                        bounds[settings.agentRadius] = holes;
                    }
                    if (!Reaches(placed, holes)) continue;
                    var old = surface.Mesh.GetTileByRef(placed.Ref);
                    if (old?.data == null) continue;
                    var tile = NavMeshCarving.Carve(Place(placed.Instance.Data.m_Tiles[placed.Index], placed.Instance.Position, placed.Instance.Rotation),
                        settings, ObstacleSystem.Holes);
                    AddLinks(surface, tile);
                    tile.header.x = old.data.header.x;
                    tile.header.y = old.data.header.y;
                    tile.header.layer = old.data.header.layer;
                    MapLinks(surface, old, false);
                    surface.Mesh.RemoveTile(placed.Ref);
                    replaced = true;
                    surface.Mesh.AddTile(tile, 0, 0, out long tileRef);
                    placed.Ref = tileRef;
                    if (tileRef == 0)
                    {
                        // Not added back (no room): the whole NavMesh is built again instead.
                        TileVersion++;
                        s_Dirty = true;
                        Refresh();
                        return;
                    }
                    var added = surface.Mesh.GetTileByRef(tileRef);
                    surface.DataOfTile[DtDetour.DecodePolyIdTile(tileRef)] = placed.Instance.Id;
                    MapLinks(surface, added, true);
                }
            }
            if (replaced) TileVersion++;
        }

        /// <summary>A changed hole, grown by the agent radius as it is cut, reaches the tile.</summary>
        static bool Reaches(PlacedTile tile, (float MinX, float MaxX, float MinZ, float MaxZ)[] holes)
        {
            foreach (var hole in holes)
                if (hole.MaxX >= tile.MinX && hole.MinX <= tile.MaxX && hole.MaxZ >= tile.MinZ && hole.MinZ <= tile.MaxZ) return true;
            return false;
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
            // Queries walk the tile's bounding volume tree (its node order decides which of equally
            // near polygons a query finds first, as in Unity: tests/projects/navmesh and navbaked
            // `sample` on area boundaries).
            bool hasTree = tile.bvTree != null && tile.header.bvNodeCount > 0;
            copy.header.bvNodeCount = hasTree ? tile.header.bvNodeCount : 0;
            bool identity = position == Vector3.zero && rotation == Quaternion.identity;
            if (identity)
            {
                if (hasTree) copy.bvTree = tile.bvTree;
                return copy;
            }
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
            if (hasTree) copy.bvTree = MoveTree(tile, copy.header, position, rotation);
            return copy;
        }

        /// <summary>The tile's bounding volume tree moved with it: each node, in the same order, bounds
        /// its box moved into the world (in the moved tile's quantized coordinates).</summary>
        static DtBVNode[] MoveTree(DtMeshData tile, DtMeshHeader moved, Vector3 position, Quaternion rotation)
        {
            var h = tile.header;
            float q = h.bvQuantFactor;
            var nodes = new DtBVNode[h.bvNodeCount];
            for (int n = 0; n < nodes.Length; n++)
            {
                var src = tile.bvTree[n];
                var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                var max = -min;
                for (int c = 0; c < 8; c++)
                {
                    var local = new Vector3(
                        h.bmin.X + ((c & 1) != 0 ? src.bmax.X : src.bmin.X) / q,
                        h.bmin.Y + ((c & 2) != 0 ? src.bmax.Y : src.bmin.Y) / q,
                        h.bmin.Z + ((c & 4) != 0 ? src.bmax.Z : src.bmin.Z) / q);
                    var p = position + rotation * local;
                    min = Vector3.Min(min, p);
                    max = Vector3.Max(max, p);
                }
                int Low(float v, float origin) => Math.Clamp((int)MathF.Floor((v - origin) * q), 0, 0xffff);
                int High(float v, float origin) => Math.Clamp((int)MathF.Ceiling((v - origin) * q), 0, 0xffff);
                nodes[n] = new DtBVNode
                {
                    bmin = new RcVec3i(Low(min.x, moved.bmin.X), Low(min.y, moved.bmin.Y), Low(min.z, moved.bmin.Z)),
                    bmax = new RcVec3i(High(max.x, moved.bmin.X), High(max.y, moved.bmin.Y), High(max.z, moved.bmin.Z)),
                    i = src.i,
                };
            }
            return nodes;
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
        /// become its off-mesh connections (Detour connects their ends when tiles are added).
        /// A start on the edge between two tiles lies over both and is added to each; each copy's
        /// end side is classified against its own tile, so every copy lands where its tile's
        /// neighbours are. The links whose end lies over this tile (or within the snap radius of it)
        /// land in it, and a bidirectional landing takes one of this tile's link slots for the way
        /// back, so this tile reserves slots for them too, whether or not it holds any start.</summary>
        static void AddLinks(Surface surface, DtMeshData tile)
        {
            var h = tile.header;
            var add = new List<(LinkInstance Link, Vector3 Start, Vector3 End)>();
            int ends = 0;
            foreach (var link in s_Links)
            {
                if (!link.Active || link.Link.agentTypeID != surface.AgentTypeID) continue;
                var end = link.End;
                // Over the tile or within the snap radius of it: Detour lands an end on a polygon of
                // the tile up to the snap radius away (DtNavMesh.ConnectExtOffMeshLinks).
                float snap = SnapRadius(surface);
                if (!(end.x < h.bmin.X - snap || end.x > h.bmax.X + snap || end.z < h.bmin.Z - snap || end.z > h.bmax.Z + snap)) ends++;
                var start = link.Start;
                if (start.x < h.bmin.X || start.x > h.bmax.X || start.z < h.bmin.Z || start.z > h.bmax.Z) continue;
                add.Add((link, start, end));
            }
            // Three link slots per end that can land here. DotRecast's own tile builder reserves two
            // per end inside the tile for baked connections (DtNavMeshBuilder.CreateNavMeshData:
            // offMeshConLinkCount counts each end inside the tile, maxLinkCount adds it twice), but it
            // stores each connection in one tile only. Here a start on a tile edge or corner is added
            // to every tile it lies over, up to four; each copy's bidirectional landing takes a slot of
            // the landing tile for the way back, and up to three copies besides this tile's own (which
            // the start reservation below covers) can land here. So deliberately more than the builder.
            h.maxLinkCount += ends * 3;
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
                var endPos = new RcVec3f(end.x, end.y, end.z);
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
                    pos = new[] { new RcVec3f(start.x, start.y, start.z), endPos },
                    rad = SnapRadius(surface),
                    poly = basePoly + i,
                    flags = link.Link.bidirectional ? DtDetour.DT_OFFMESH_CON_BIDIR : 0,
                    // Where the end lies from this tile: 0xff over it, else the neighbour (0..7)
                    // Detour looks for the landing in. Classified as DotRecast's own tile builder
                    // does (DtNavMeshBuilder.CreateNavMeshData: the end against the tile's bounds),
                    // so an end in the next tile lands there instead of never connecting.
                    side = DtNavMeshBuilder.ClassifyOffMeshPoint(endPos, h.bmin, h.bmax),
                    userId = link.Id,
                    costModifier = link.Link.costModifier >= 0f ? link.Link.costModifier : -1f,
                    halfWidth = HalfWidth(link, start, end),
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

        /// <summary>Half a wide link's width across it: along the link's up (its rotation's) crossed
        /// with the way from start to end; zero for a link without width (or one whose up runs along
        /// it). Matches the Unity 6000.0.71f1 Editor, tests/projects/navlinks wide.</summary>
        static RcVec3f HalfWidth(LinkInstance link, Vector3 start, Vector3 end)
        {
            float width = link.Link.width;
            if (!(width > 0f)) return RcVec3f.Zero;
            var along = end - start;
            var dir = along.magnitude > Vector3.kEpsilon ? along.normalized : Vector3.forward;
            var across = Vector3.Cross(link.Rotation * Vector3.up, dir);
            if (across.sqrMagnitude <= MinAcrossSq) return RcVec3f.Zero;
            var half = across.normalized * (width * 0.5f);
            return new RcVec3f(half.x, half.y, half.z);
        }

        /// <summary>A link whose up runs (almost) along it has no width across it: FLT_EPSILON.</summary>
        const float MinAcrossSq = 1.1920929e-7f;

        static int s_PolyDumps;

        /// <summary>How far (in x, z) a link's end may lie from the NavMesh and still connect: the
        /// agent type's radius, every link alike (vertically: its climb, the tiles' walkableClimb).
        /// Matches the Unity 6000.0.71f1 Editor, tests/projects/navlinks snap, snapnear, snapfar.</summary>
        internal static float SnapRadius(Surface surface) => surface.Settings.agentRadius;
    }

    /// <summary>
    /// The NavMesh each loaded scene was baked with the legacy way (the Navigation window: the
    /// scene's NavMeshSettings.m_NavMeshData, usually <c>&lt;Scene&gt;/NavMesh.asset</c>). It is added
    /// when the scene loads, before its objects wake (an agent in the scene is on it in Awake), and
    /// removed when the scene unloads; additively loaded scenes add theirs beside the others'
    /// (measured, Unity 6000.0.71f1: tests/projects/navbaked).
    /// </summary>
    internal static class SceneNavMesh
    {
        static readonly Dictionary<int, int> s_ByScene = new Dictionary<int, int>();

        internal static void SceneLoaded(SceneData scene, YNode body, IReferenceResolver resolver)
        {
            int handle = scene?.Handle.Value ?? 0;
            SceneUnloaded(handle);
            var reference = body.Ref("m_NavMeshData");
            if (reference.IsNull) return;
            var data = resolver?.Resolve(reference, typeof(NavMeshData)) as NavMeshData;
            if (data == null)
            {
                // A deleted NavMesh asset leaves the reference behind (no error measured in Unity).
                Debug.LogWarning($"Unbound: the baked NavMesh of scene {scene?.Path} (guid {reference.Guid}, fileID {reference.FileId}) was not found; the scene has no NavMesh.");
                return;
            }
            s_ByScene[handle] = NavWorld.AddData(data, data.position, data.rotation);
        }

        internal static void SceneUnloaded(int handle)
        {
            if (s_ByScene.Remove(handle, out int id)) NavWorld.RemoveData(id);
        }
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
            return RcVec3f.Distance(pa, pb) * CostPerMetre(curTile, curPoly);
        }

        /// <summary>A polygon's cost per metre: its area's; a link's cost modifier in place of its
        /// area's when that is zero or more (AI Navigation 2.0 manual, NavMeshLink Cost Modifier;
        /// measured: tests/projects/navlinks costhigh).</summary>
        float CostPerMetre(DtMeshTile tile, DtPoly poly)
        {
            if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION && tile?.data != null)
            {
                int i = poly.index - tile.data.header.offMeshBase;
                var cons = tile.data.offMeshCons;
                if (cons != null && i >= 0 && i < cons.Length && cons[i].costModifier >= 0f) return cons[i].costModifier;
            }
            return Costs[poly.GetArea()];
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

        internal static void Read(YNode body)
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
