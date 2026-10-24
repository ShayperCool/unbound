using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Unbound.Runtime;

namespace UnityEngine.AI
{
    /// <summary>
    /// The NavMesh world and its queries, on Detour (the library Unity's NavMesh is built on).
    /// Positions snap to the NavMesh within an agent-sized box (radius, height, radius), as the
    /// Editor's queries do; heights come from the tiles' detail meshes.
    /// </summary>
    public static partial class NavMesh
    {
        public const int AllAreas = -1;
        const int MaxPath = 4096;

        public delegate void OnNavMeshPreUpdate();

        public static OnNavMeshPreUpdate onPreUpdate;

        static float s_AvoidancePredictionTime = 2f;
        static int s_PathfindingIterationsPerFrame = 100;

        public static float avoidancePredictionTime
        {
            get => s_AvoidancePredictionTime;
            set => s_AvoidancePredictionTime = Mathf.Max(0f, value);
        }

        public static int pathfindingIterationsPerFrame
        {
            get => s_PathfindingIterationsPerFrame;
            set => s_PathfindingIterationsPerFrame = Math.Max(1, value);
        }

        internal static RcVec3f V(Vector3 v) => new RcVec3f(v.x, v.y, v.z);
        internal static Vector3 V(RcVec3f v) => new Vector3(v.X, v.Y, v.Z);

        /// <summary>The box a position snaps to the NavMesh within for an agent type.</summary>
        internal static RcVec3f AgentExtents(int agentTypeID)
        {
            var s = GetSettingsByID(agentTypeID);
            return new RcVec3f(s.agentRadius, s.agentHeight, s.agentRadius);
        }

        internal static bool Nearest(NavWorld.Surface surface, Vector3 position, RcVec3f extents, IDtQueryFilter filter, out long polyRef, out Vector3 point)
        {
            polyRef = 0;
            point = position;
            if (surface?.Query == null) return false;
            surface.Query.FindNearestPoly(V(position), extents, filter, out polyRef, out var nearest, out _);
            point = V(nearest);
            return polyRef != 0;
        }

        static int AreaOf(NavWorld.Surface surface, long polyRef)
        {
            surface.Mesh.GetPolyArea(polyRef, out int area);
            return area;
        }

        // ------------------------------------------------------------------ queries

        public static bool SamplePosition(Vector3 sourcePosition, out NavMeshHit hit, float maxDistance, int areaMask) =>
            SamplePosition(sourcePosition, out hit, maxDistance, AreaFilter.Of(areaMask), -1);

        public static bool SamplePosition(Vector3 sourcePosition, out NavMeshHit hit, float maxDistance, NavMeshQueryFilter filter) =>
            SamplePosition(sourcePosition, out hit, maxDistance, AreaFilter.Of(filter.areaMask, filter.costs), filter.agentTypeID);

        /// <summary>The nearest point within the distance on the NavMesh of an agent type (any
        /// type when negative: the nearest over all).</summary>
        static bool SamplePosition(Vector3 sourcePosition, out NavMeshHit hit, float maxDistance, AreaFilter filter, int agentTypeID)
        {
            hit = default;
            hit.position = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
            hit.distance = float.PositiveInfinity;
            bool found = false;
            foreach (var surface in NavWorld.Surfaces)
            {
                if (agentTypeID >= 0 && surface.AgentTypeID != agentTypeID) continue;
                if (!Nearest(surface, sourcePosition, new RcVec3f(maxDistance, maxDistance, maxDistance), filter, out var polyRef, out var point)) continue;
                float d = Vector3.Distance(sourcePosition, point);
                if (d > maxDistance || d >= hit.distance) continue;
                hit.position = point;
                hit.distance = d;
                hit.mask = 1 << AreaOf(surface, polyRef);
                hit.normal = Vector3.zero;
                hit.hit = true;
                found = true;
            }
            return found;
        }

        public static bool Raycast(Vector3 sourcePosition, Vector3 targetPosition, out NavMeshHit hit, int areaMask) =>
            Raycast(sourcePosition, targetPosition, out hit, AreaFilter.Of(areaMask), 0);

        public static bool Raycast(Vector3 sourcePosition, Vector3 targetPosition, out NavMeshHit hit, NavMeshQueryFilter filter) =>
            Raycast(sourcePosition, targetPosition, out hit, AreaFilter.Of(filter.areaMask, filter.costs), filter.agentTypeID);

        /// <summary>Walks from the source towards the target over the NavMesh; true when an edge
        /// stops it before the target (the hit is where, with the edge's normal).</summary>
        internal static bool Raycast(Vector3 sourcePosition, Vector3 targetPosition, out NavMeshHit hit, AreaFilter filter, int agentTypeID)
        {
            hit = default;
            var surface = NavWorld.Get(agentTypeID);
            if (!Nearest(surface, sourcePosition, AgentExtents(agentTypeID), filter, out var startRef, out var start))
            {
                hit.position = sourcePosition;
                hit.hit = true;
                return true;
            }
            var path = new long[MaxPath];
            surface.Query.Raycast(startRef, V(start), V(targetPosition), filter, out float t, out var normal, path, out int count, MaxPath);
            long lastRef = count > 0 ? path[count - 1] : startRef;
            bool blocked = t < 1f;
            var end = blocked ? start + (targetPosition - start) * t : targetPosition;
            // The hit is on the NavMesh: its height there (an edge point may fall outside the
            // polygon's strict inside, so the closest point of the polygon gives it then).
            if (surface.Query.GetPolyHeight(lastRef, V(end), out float h).Succeeded()) end.y = h;
            else if (surface.Query.ClosestPointOnPoly(lastRef, V(end), out var onPoly, out _).Succeeded()) end.y = onPoly.Y;
            hit.position = end;
            hit.normal = blocked ? V(normal) : Vector3.zero;
            // Measured from the position asked for, not the start on the NavMesh.
            hit.distance = Vector3.Distance(sourcePosition, end);
            // A blocked ray reports no area (measured).
            hit.mask = blocked ? 0 : 1 << AreaOf(surface, lastRef);
            hit.hit = blocked;
            return blocked;
        }

        public static bool FindClosestEdge(Vector3 sourcePosition, out NavMeshHit hit, int areaMask) =>
            FindClosestEdge(sourcePosition, out hit, AreaFilter.Of(areaMask), 0);

        public static bool FindClosestEdge(Vector3 sourcePosition, out NavMeshHit hit, NavMeshQueryFilter filter) =>
            FindClosestEdge(sourcePosition, out hit, AreaFilter.Of(filter.areaMask, filter.costs), filter.agentTypeID);

        internal static bool FindClosestEdge(Vector3 sourcePosition, out NavMeshHit hit, AreaFilter filter, int agentTypeID)
        {
            hit = default;
            var surface = NavWorld.Get(agentTypeID);
            if (!Nearest(surface, sourcePosition, AgentExtents(agentTypeID), filter, out var startRef, out var start)) return false;
            var status = surface.Query.FindDistanceToWall(startRef, V(start), 1e5f, filter, out float distance, out var position, out var normal);
            if (status.Failed()) return false;
            hit.position = V(position);
            hit.normal = V(normal);
            hit.distance = distance;
            hit.mask = 1 << AreaOf(surface, startRef);
            hit.hit = true;
            return true;
        }

        public static bool CalculatePath(Vector3 sourcePosition, Vector3 targetPosition, int areaMask, NavMeshPath path) =>
            CalculatePath(sourcePosition, targetPosition, AreaFilter.Of(areaMask), 0, path);

        public static bool CalculatePath(Vector3 sourcePosition, Vector3 targetPosition, NavMeshQueryFilter filter, NavMeshPath path) =>
            CalculatePath(sourcePosition, targetPosition, AreaFilter.Of(filter.areaMask, filter.costs), filter.agentTypeID, path);

        /// <summary>A path between two positions snapped to the NavMesh: Detour's A* over the
        /// polygons (area costs), then the straight path through their portals. A target off the
        /// reachable mesh gives a partial path to the nearest reachable point.</summary>
        internal static bool CalculatePath(Vector3 sourcePosition, Vector3 targetPosition, AreaFilter filter, int agentTypeID, NavMeshPath path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            path.ClearCorners();
            var search = PathSearch.Begin(NavWorld.Get(agentTypeID), sourcePosition, targetPosition, filter, agentTypeID);
            if (search == null) return false;
            int budget = int.MaxValue;
            search.Step(ref budget);
            var result = search.Result;
            if (result == null) return false;
            result.CopyTo(path);
            return true;
        }

        /// <summary>A path search, stepped by A* iterations (agents share a budget of
        /// <see cref="pathfindingIterationsPerFrame"/> per frame, as in Unity).</summary>
        internal sealed class PathSearch
        {
            NavWorld.Surface m_Surface;
            DtNavMeshQuery m_Query;
            AreaFilter m_Filter;
            long m_StartRef, m_EndRef;
            Vector3 m_Start, m_End;
            bool m_Done;
            internal NavMeshPath Result;

            internal static PathSearch Begin(NavWorld.Surface surface, Vector3 source, Vector3 target, AreaFilter filter, int agentTypeID)
            {
                var extents = AgentExtents(agentTypeID);
                if (!Nearest(surface, source, extents, filter, out var startRef, out var start)) return null;
                if (!Nearest(surface, target, extents, filter, out var endRef, out var end)) return null;
                var search = new PathSearch
                {
                    m_Surface = surface,
                    m_Query = new DtNavMeshQuery(surface.Mesh),
                    m_Filter = filter,
                    m_StartRef = startRef,
                    m_EndRef = endRef,
                    m_Start = start,
                    m_End = end,
                };
                search.m_Query.InitSlicedFindPath(startRef, endRef, V(start), V(end), filter, 0);
                return search;
            }

            /// <summary>Runs up to the budget's iterations (taking them from it); true when done.</summary>
            internal bool Step(ref int budget)
            {
                if (m_Done) return true;
                var status = m_Query.UpdateSlicedFindPath(Math.Max(1, budget), out int done);
                budget = Math.Max(0, budget - done);
                if (status.InProgress()) return false;
                m_Done = true;
                Finish();
                return true;
            }

            void Finish()
            {
                var polys = new long[MaxPath];
                int count;
                var end = m_End;
                // Whether the search reached the target's polygon; if not, the reachable polygon
                // nearest to the target ends the path (found before finalizing, which reverses the
                // search's parent links).
                bool complete = false;
                DtNode best = null;
                float bestDistance = float.MaxValue;
                foreach (var node in m_Query.GetNodePool().AsEnumerable())
                {
                    if (node == null || node.id == 0 || (node.flags & (DtNodeFlags.DT_NODE_OPEN | DtNodeFlags.DT_NODE_CLOSED)) == 0) continue;
                    if (node.id == m_EndRef) complete = true;
                    m_Query.ClosestPointOnPoly(node.id, V(m_End), out var p, out _);
                    float d = RcVec3f.DistanceSquared(p, V(m_End));
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        best = node;
                    }
                }
                if (complete || best == null)
                {
                    var status = m_Query.FinalizeSlicedFindPath(polys, out count, MaxPath);
                    if (status.Failed() || count == 0) return;
                    complete = polys[count - 1] == m_EndRef;
                }
                else
                {
                    m_Query.GetPathToNode(best, polys, out count, MaxPath);
                    if (count == 0) return;
                }
                if (!complete)
                {
                    m_Query.ClosestPointOnPoly(polys[count - 1], V(m_End), out var closest, out _);
                    end = V(closest);
                }
                var straight = new DtStraightPath[MaxPath];
                m_Query.FindStraightPath(V(m_Start), V(end), polys, count, straight, out int corners, MaxPath, 0);
                var result = new NavMeshPath();
                result.m_Corners = new Vector3[corners];
                for (int i = 0; i < corners; i++) result.m_Corners[i] = V(straight[i].pos);
                result.m_Polygons = new long[count];
                Array.Copy(polys, result.m_Polygons, count);
                result.m_Status = complete ? NavMeshPathStatus.PathComplete : NavMeshPathStatus.PathPartial;
                Result = result;
            }
        }

        public static NavMeshTriangulation CalculateTriangulation()
        {
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var areas = new List<int>();
            foreach (var surface in NavWorld.Surfaces)
            {
                for (int i = 0; i < surface.Mesh.GetMaxTiles(); i++)
                {
                    var tile = surface.Mesh.GetTile(i);
                    if (tile?.data == null) continue;
                    var v = tile.data.verts;
                    foreach (var poly in tile.data.polys)
                    {
                        if (poly.GetPolyType() == DtPolyTypes.DT_POLYTYPE_OFFMESH_CONNECTION) continue;
                        int first = vertices.Count;
                        for (int k = 0; k < poly.vertCount; k++)
                        {
                            int vi = poly.verts[k] * 3;
                            vertices.Add(new Vector3(v[vi], v[vi + 1], v[vi + 2]));
                        }
                        for (int k = 2; k < poly.vertCount; k++)
                        {
                            indices.Add(first);
                            indices.Add(first + k - 1);
                            indices.Add(first + k);
                            areas.Add(poly.GetArea());
                        }
                    }
                }
            }
            return new NavMeshTriangulation { vertices = vertices.ToArray(), indices = indices.ToArray(), areas = areas.ToArray() };
        }

        [Obsolete("use NavMesh.CalculateTriangulation() instead.")]
        public static void Triangulate(out Vector3[] vertices, out int[] indices)
        {
            var t = CalculateTriangulation();
            vertices = t.vertices;
            indices = t.indices;
        }

        // ------------------------------------------------------------------ areas

        public static void SetAreaCost(int areaIndex, float cost)
        {
            NavMeshAreas.Ensure();
            if (areaIndex < 0 || areaIndex >= 32) return;
            NavMeshAreas.Costs[areaIndex] = cost;
        }

        public static float GetAreaCost(int areaIndex)
        {
            NavMeshAreas.Ensure();
            return areaIndex >= 0 && areaIndex < 32 ? NavMeshAreas.Costs[areaIndex] : 0f;
        }

        public static int GetAreaFromName(string areaName)
        {
            NavMeshAreas.Ensure();
            for (int i = 0; i < 32; i++)
                if (!string.IsNullOrEmpty(NavMeshAreas.Names[i]) && NavMeshAreas.Names[i] == areaName) return i;
            return -1;
        }

        public static string[] GetAreaNames()
        {
            NavMeshAreas.Ensure();
            var names = new List<string>();
            foreach (var n in NavMeshAreas.Names) if (!string.IsNullOrEmpty(n)) names.Add(n);
            return names.ToArray();
        }

        [Obsolete("Use SetAreaCost instead.")]
        public static void SetLayerCost(int layer, float cost) => SetAreaCost(layer, cost);

        [Obsolete("Use GetAreaCost instead.")]
        public static float GetLayerCost(int layer) => GetAreaCost(layer);

        [Obsolete("Use GetAreaFromName instead.")]
        public static int GetNavMeshLayerFromName(string layerName) => GetAreaFromName(layerName);

        // ------------------------------------------------------------------ agent types

        public static NavMeshBuildSettings CreateSettings()
        {
            NavMeshAreas.Ensure();
            int id = 1;
            while (NavMeshAreas.Agents.Exists(a => a.Settings.agentTypeID == id)) id++;
            var settings = NavMeshBuildSettings.Default;
            settings.agentTypeID = id;
            NavMeshAreas.Agents.Add(("", settings));
            return settings;
        }

        public static void RemoveSettings(int agentTypeID)
        {
            NavMeshAreas.Ensure();
            if (agentTypeID != 0) NavMeshAreas.Agents.RemoveAll(a => a.Settings.agentTypeID == agentTypeID);
        }

        public static NavMeshBuildSettings GetSettingsByID(int agentTypeID)
        {
            NavMeshAreas.Ensure();
            foreach (var a in NavMeshAreas.Agents)
                if (a.Settings.agentTypeID == agentTypeID) return a.Settings;
            var none = new NavMeshBuildSettings();
            none.agentTypeID = -1;
            return none;
        }

        public static int GetSettingsCount()
        {
            NavMeshAreas.Ensure();
            return NavMeshAreas.Agents.Count;
        }

        public static NavMeshBuildSettings GetSettingsByIndex(int index)
        {
            NavMeshAreas.Ensure();
            if (index < 0 || index >= NavMeshAreas.Agents.Count) return GetSettingsByID(-1);
            return NavMeshAreas.Agents[index].Settings;
        }

        public static string GetSettingsNameFromID(int agentTypeID)
        {
            NavMeshAreas.Ensure();
            foreach (var a in NavMeshAreas.Agents)
                if (a.Settings.agentTypeID == agentTypeID) return a.Name;
            return null;
        }

        // ------------------------------------------------------------------ data

        public static NavMeshDataInstance AddNavMeshData(NavMeshData navMeshData) =>
            new NavMeshDataInstance { id = AddNavMeshDataInternal(navMeshData) };

        public static NavMeshDataInstance AddNavMeshData(NavMeshData navMeshData, Vector3 position, Quaternion rotation) =>
            new NavMeshDataInstance { id = AddNavMeshDataTransformedInternal(navMeshData, position, rotation) };

        public static void RemoveNavMeshData(NavMeshDataInstance handle) => RemoveNavMeshDataInternal(handle.id);

        public static void RemoveAllNavMeshData() => NavWorld.RemoveAllData();

        internal static bool IsValidNavMeshDataHandle(int handle) => NavWorld.FindData(handle) != null;

        internal static bool IsValidLinkHandle(int handle) => NavWorld.FindLink(handle) != null;

        internal static Object InternalGetOwner(int dataID) => NavWorld.FindData(dataID) is NavWorld.DataInstance d ? ObjectRegistry.FindById(d.Owner) : null;

        internal static bool InternalSetOwner(int dataID, int ownerID)
        {
            var d = NavWorld.FindData(dataID);
            if (d == null) return false;
            d.Owner = ownerID;
            return true;
        }

        internal static Object InternalGetLinkOwner(int linkID) => NavWorld.FindLink(linkID) is NavWorld.LinkInstance l ? ObjectRegistry.FindById(l.Owner) : null;

        internal static bool InternalSetLinkOwner(int linkID, int ownerID)
        {
            var l = NavWorld.FindLink(linkID);
            if (l == null) return false;
            l.Owner = ownerID;
            return true;
        }

        internal static int AddNavMeshDataInternal(NavMeshData navMeshData)
        {
            if (navMeshData == null) throw new ArgumentNullException(nameof(navMeshData));
            return NavWorld.AddData(navMeshData, navMeshData.position, navMeshData.rotation);
        }

        internal static int AddNavMeshDataTransformedInternal(NavMeshData navMeshData, Vector3 position, Quaternion rotation)
        {
            if (navMeshData == null) throw new ArgumentNullException(nameof(navMeshData));
            return NavWorld.AddData(navMeshData, position, rotation);
        }

        internal static void RemoveNavMeshDataInternal(int handle) => NavWorld.RemoveData(handle);

        // ------------------------------------------------------------------ links

        public static NavMeshLinkInstance AddLink(NavMeshLinkData link) =>
            new NavMeshLinkInstance { id = AddLinkInternal(link, Vector3.zero, Quaternion.identity) };

        public static NavMeshLinkInstance AddLink(NavMeshLinkData link, Vector3 position, Quaternion rotation) =>
            new NavMeshLinkInstance { id = AddLinkInternal(link, position, rotation) };

        public static void RemoveLink(NavMeshLinkInstance handle) => RemoveLinkInternal(handle.id);

        public static bool IsLinkActive(NavMeshLinkInstance handle) => IsOffMeshConnectionActive(handle.id);

        public static void SetLinkActive(NavMeshLinkInstance handle, bool value) => SetOffMeshConnectionActive(handle.id, value);

        public static bool IsLinkOccupied(NavMeshLinkInstance handle) => IsOffMeshConnectionOccupied(handle.id);

        public static bool IsLinkValid(NavMeshLinkInstance handle) => IsValidLinkHandle(handle.id);

        public static Object GetLinkOwner(NavMeshLinkInstance handle) => InternalGetLinkOwner(handle.id);

        public static void SetLinkOwner(NavMeshLinkInstance handle, Object owner)
        {
            int ownerID = owner != null ? owner.GetInstanceID() : 0;
            if (!InternalSetLinkOwner(handle.id, ownerID))
                Debug.LogError("Cannot set 'owner' on an invalid NavMeshLinkInstance");
        }

        internal static int AddLinkInternal(NavMeshLinkData link, Vector3 position, Quaternion rotation) => NavWorld.AddLink(link, position, rotation);

        internal static void RemoveLinkInternal(int handle) => NavWorld.RemoveLink(handle);

        internal static bool IsOffMeshConnectionOccupied(int handle) => NavMeshAgent.LinkOccupied(handle);

        internal static bool IsOffMeshConnectionActive(int linkHandle) => NavWorld.FindLink(linkHandle)?.Active ?? false;

        internal static void SetOffMeshConnectionActive(int linkHandle, bool activated)
        {
            var link = NavWorld.FindLink(linkHandle);
            if (link == null || link.Active == activated) return;
            link.Active = activated;
            NavWorld.MarkDirty();
        }

        [Obsolete("AddOffMeshLinks has no effect and is deprecated.")]
        public static void AddOffMeshLinks() { }

        [Obsolete("RestoreNavMesh has no effect and is deprecated.")]
        public static void RestoreNavMesh() { }
    }
}
