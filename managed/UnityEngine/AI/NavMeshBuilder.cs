using System;
using System.Collections.Generic;
using DotRecast.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using Unbound.Runtime;

namespace UnityEngine.AI
{
    /// <summary>
    /// Builds NavMeshData from sources with Recast, tile by tile on Unity's grid (tiles of
    /// <c>tileSize</c> voxels from the data's origin). The voxels are the agent's (cell size the
    /// voxel size, cell height half of it); the heightfield's floor is the sources' lowest point;
    /// spans snap their tops to the cell above the surface (as Unity does, measured); walkable
    /// spans erode by the agent radius; regions are partitioned monotonically; polygons have up to
    /// six vertices.
    /// </summary>
    public static partial class NavMeshBuilder
    {
        // ------------------------------------------------------------------ collecting sources

        public static void CollectSources(Bounds includedWorldBounds, int includedLayerMask, NavMeshCollectGeometry geometry, int defaultArea,
            List<NavMeshBuildMarkup> markups, List<NavMeshBuildSource> results) =>
            CollectSources(includedWorldBounds, includedLayerMask, geometry, defaultArea, false, markups, false, results);

        public static void CollectSources(Bounds includedWorldBounds, int includedLayerMask, NavMeshCollectGeometry geometry, int defaultArea,
            bool generateLinksByDefault, List<NavMeshBuildMarkup> markups, bool includeOnlyMarkedObjects, List<NavMeshBuildSource> results)
        {
            if (markups == null) throw new ArgumentNullException(nameof(markups));
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            foreach (var go in AllGameObjects(null))
                Collect(go, includedLayerMask, geometry, defaultArea, generateLinksByDefault, markups, includeOnlyMarkedObjects, results, includedWorldBounds);
        }

        public static void CollectSources(Transform root, int includedLayerMask, NavMeshCollectGeometry geometry, int defaultArea,
            List<NavMeshBuildMarkup> markups, List<NavMeshBuildSource> results) =>
            CollectSources(root, includedLayerMask, geometry, defaultArea, false, markups, false, results);

        public static void CollectSources(Transform root, int includedLayerMask, NavMeshCollectGeometry geometry, int defaultArea,
            bool generateLinksByDefault, List<NavMeshBuildMarkup> markups, bool includeOnlyMarkedObjects, List<NavMeshBuildSource> results)
        {
            if (markups == null) throw new ArgumentNullException(nameof(markups));
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            foreach (var go in AllGameObjects(root))
                Collect(go, includedLayerMask, geometry, defaultArea, generateLinksByDefault, markups, includeOnlyMarkedObjects, results, null);
        }

        /// <summary>CollectSources limited to the objects of one scene (an Editor stage's), as
        /// NavMeshEditorHelpers.CollectSourcesInStage asks.</summary>
        internal static void CollectSourcesInScene(Bounds? includedWorldBounds, Transform root, int includedLayerMask, NavMeshCollectGeometry geometry,
            int defaultArea, bool generateLinksByDefault, List<NavMeshBuildMarkup> markups, bool includeOnlyMarkedObjects,
            SceneManagement.Scene scene, List<NavMeshBuildSource> results)
        {
            if (markups == null) throw new ArgumentNullException(nameof(markups));
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            foreach (var go in AllGameObjects(root))
                if (go.scene == scene)
                    Collect(go, includedLayerMask, geometry, defaultArea, generateLinksByDefault, markups, includeOnlyMarkedObjects, results, includedWorldBounds);
        }

        static IEnumerable<GameObject> AllGameObjects(Transform root)
        {
            if (root != null)
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(false)) yield return t.gameObject;
                yield break;
            }
            foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
                yield return go;
        }

        /// <summary>The markup that applies to an object: its own, else the nearest ancestor's that
        /// applies to children.</summary>
        static NavMeshBuildMarkup? MarkupOf(GameObject go, List<NavMeshBuildMarkup> markups)
        {
            for (var t = go.transform; t != null; t = t.parent)
                foreach (var m in markups)
                    if (m.root == t && (t == go.transform || m.applyToChildren)) return m;
            return null;
        }

        static void Collect(GameObject go, int layerMask, NavMeshCollectGeometry geometry, int defaultArea, bool generateLinks,
            List<NavMeshBuildMarkup> markups, bool onlyMarked, List<NavMeshBuildSource> results, Bounds? bounds)
        {
            if (!go.activeInHierarchy || (layerMask & (1 << go.layer)) == 0) return;
            var markup = MarkupOf(go, markups);
            if (onlyMarked && markup == null) return;
            if (markup is NavMeshBuildMarkup ignore && ignore.overrideIgnore && ignore.ignoreFromBuild) return;
            int area = markup is NavMeshBuildMarkup m && m.overrideArea ? m.area : defaultArea;
            bool links = markup is NavMeshBuildMarkup l && l.overrideGenerateLinks ? l.generateLinks : generateLinks;
            void Add(NavMeshBuildSource source, Bounds worldBounds)
            {
                if (bounds.HasValue && !bounds.Value.Intersects(worldBounds)) return;
                source.area = area;
                source.generateLinks = links;
                results.Add(source);
            }
            if (geometry == NavMeshCollectGeometry.RenderMeshes)
            {
                var filter = go.GetComponent<MeshFilter>();
                var renderer = go.GetComponent<MeshRenderer>();
                if (filter != null && filter.sharedMesh != null && renderer != null && renderer.enabled)
                    Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = filter.sharedMesh, transform = go.transform.localToWorldMatrix, component = filter }, renderer.bounds);
                return;
            }
            foreach (var collider in go.GetComponents<Collider>())
            {
                if (!collider.enabled || collider.isTrigger) continue;
                var world = go.transform.localToWorldMatrix;
                switch (collider)
                {
                    case BoxCollider box:
                        Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = box.size, transform = world * Matrix4x4.Translate(box.center), component = box }, box.bounds);
                        break;
                    case SphereCollider sphere:
                        Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Sphere, size = Vector3.one * sphere.radius * 2f, transform = world * Matrix4x4.Translate(sphere.center), component = sphere }, sphere.bounds);
                        break;
                    case CapsuleCollider capsule:
                    {
                        var size = new Vector3(capsule.radius * 2f, capsule.height, capsule.radius * 2f);
                        var axis = capsule.direction == 0 ? Quaternion.Euler(0, 0, 90) : capsule.direction == 2 ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
                        Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Capsule, size = size, transform = world * Matrix4x4.TRS(capsule.center, axis, Vector3.one), component = capsule }, capsule.bounds);
                        break;
                    }
                    case MeshCollider meshCollider when meshCollider.sharedMesh != null:
                        Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = meshCollider.sharedMesh, transform = world, component = meshCollider }, meshCollider.bounds);
                        break;
                }
            }
        }

        // ------------------------------------------------------------------ building

        public static NavMeshData BuildNavMeshData(NavMeshBuildSettings buildSettings, List<NavMeshBuildSource> sources, Bounds localBounds, Vector3 position, Quaternion rotation)
        {
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var data = new NavMeshData(buildSettings.agentTypeID) { position = position, rotation = rotation };
            UpdateNavMeshData(data, buildSettings, sources, localBounds);
            return data;
        }

        public static bool UpdateNavMeshData(NavMeshData data, NavMeshBuildSettings buildSettings, List<NavMeshBuildSource> sources, Bounds localBounds)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            var report = buildSettings.ValidationReport(localBounds);
            if (report.Length > 0)
            {
                foreach (var line in report) Debug.LogError("NavMesh build: " + line);
                return false;
            }
            NavMeshRecast.Build(data, buildSettings, sources, localBounds);
            NavWorld.DataChanged(data);
            return true;
        }

        /// <summary>Builds at once; the operation completes on the next frame.</summary>
        public static AsyncOperation UpdateNavMeshDataAsync(NavMeshData data, NavMeshBuildSettings buildSettings, List<NavMeshBuildSource> sources, Bounds localBounds)
        {
            UpdateNavMeshData(data, buildSettings, sources, localBounds);
            var op = new AsyncOperation();
            AsyncOperations.CompleteNextFrame(op);
            return op;
        }

        [Unbound.EditorFaithful("UpdateNavMeshDataAsync builds at once, so there is never a build left to cancel")]
        public static void Cancel(NavMeshData data) { }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;
    using UnityEngine.AI;

    /// <summary>The Recast build behind NavMeshBuilder.</summary>
    internal static class NavMeshRecast
    {
        sealed class Geometry
        {
            internal readonly List<float> Verts = new List<float>();
            internal readonly List<int> Tris = new List<int>();
            internal readonly List<int> Areas = new List<int>();
            internal readonly List<(Vector3 Min, Vector3 Max, int Area)> Modifiers = new List<(Vector3, Vector3, int)>();
            internal Vector3 Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            internal Vector3 Max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            internal void Triangle(Vector3 a, Vector3 b, Vector3 c, int area)
            {
                int i = Verts.Count / 3;
                foreach (var p in new[] { a, b, c })
                {
                    Verts.Add(p.x);
                    Verts.Add(p.y);
                    Verts.Add(p.z);
                    Min = Vector3.Min(Min, p);
                    Max = Vector3.Max(Max, p);
                }
                // Recast's triangles wind the other way from Unity's.
                Tris.Add(i);
                Tris.Add(i + 2);
                Tris.Add(i + 1);
                Areas.Add(area);
            }
        }

        /// <summary>Unity area index to Recast area (0 is Recast's "not walkable").</summary>
        static int RecastArea(int area) => area == 1 ? RcRecast.RC_NULL_AREA : Mathf.Clamp(area, 0, 31) + 1;

        static Geometry Gather(List<NavMeshBuildSource> sources, Matrix4x4 worldToData)
        {
            var g = new Geometry();
            foreach (var source in sources)
            {
                var m = worldToData * source.transform;
                int area = RecastArea(source.area);
                switch (source.shape)
                {
                    case NavMeshBuildSourceShape.Mesh:
                    {
                        var mesh = source.sourceObject as Mesh;
                        if (mesh == null) break;
                        var v = mesh.vertices;
                        for (int s = 0; s < mesh.subMeshCount; s++)
                        {
                            if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                            var t = mesh.GetTriangles(s);
                            for (int i = 0; i + 2 < t.Length; i += 3)
                                g.Triangle(m.MultiplyPoint3x4(v[t[i]]), m.MultiplyPoint3x4(v[t[i + 1]]), m.MultiplyPoint3x4(v[t[i + 2]]), area);
                        }
                        break;
                    }
                    case NavMeshBuildSourceShape.Box:
                        Box(g, m, source.size, area);
                        break;
                    case NavMeshBuildSourceShape.Sphere:
                    case NavMeshBuildSourceShape.Capsule:
                        Ellipsoid(g, m, source.size, area);
                        break;
                    case NavMeshBuildSourceShape.ModifierBox:
                    {
                        var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
                        var max = -min;
                        for (int i = 0; i < 8; i++)
                        {
                            var corner = Vector3.Scale(new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f), source.size);
                            var p = m.MultiplyPoint3x4(corner);
                            min = Vector3.Min(min, p);
                            max = Vector3.Max(max, p);
                        }
                        g.Modifiers.Add((min, max, area));
                        break;
                    }
                    case NavMeshBuildSourceShape.Terrain:
                        Debug.LogError("Unbound: building a NavMesh from terrain is not supported yet; the terrain source is left out.");
                        break;
                }
            }
            return g;
        }

        static void Box(Geometry g, Matrix4x4 m, Vector3 size, int area)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = m.MultiplyPoint3x4(Vector3.Scale(new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f), size));
            int[][] faces = { new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 }, new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 } };
            foreach (var f in faces)
            {
                g.Triangle(c[f[0]], c[f[1]], c[f[2]], area);
                g.Triangle(c[f[0]], c[f[2]], c[f[3]], area);
            }
        }

        static void Ellipsoid(Geometry g, Matrix4x4 m, Vector3 size, int area)
        {
            const int rings = 8, segments = 16;
            Vector3 P(int r, int s)
            {
                float theta = Mathf.PI * r / rings, phi = 2f * Mathf.PI * s / segments;
                return m.MultiplyPoint3x4(Vector3.Scale(new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi)) * 0.5f, size));
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segments; s++)
                {
                    g.Triangle(P(r, s), P(r + 1, s), P(r + 1, s + 1), area);
                    g.Triangle(P(r, s), P(r + 1, s + 1), P(r, s + 1), area);
                }
        }

        internal static void Build(NavMeshData data, NavMeshBuildSettings settings, List<NavMeshBuildSource> sources, Bounds localBounds)
        {
            var dataToWorld = Matrix4x4.TRS(data.position, data.rotation, Vector3.one);
            var g = Gather(sources, dataToWorld.inverse);
            data.m_Settings = settings;
            data.m_Tiles.Clear();
            data.m_Version++;
            if (g.Areas.Count == 0)
            {
                data.m_SourceBounds = localBounds;
                return;
            }
            // The data covers the bounds it was built for (measured).
            data.m_SourceBounds = localBounds;
            float cs = settings.EffectiveVoxelSize;
            float ch = cs * 0.5f;
            int tileSize = settings.EffectiveTileSize;
            float tileWorld = tileSize * cs;
            int walkableHeight = (int)MathF.Ceiling(settings.agentHeight / ch);
            int walkableClimb = (int)MathF.Floor(settings.agentClimb / ch);
            int walkableRadius = (int)MathF.Ceiling(settings.agentRadius / cs);
            int border = walkableRadius + 3;
            var dump = Environment.GetEnvironmentVariable("UNBOUND_NAVMESH_DUMP");
            if (!string.IsNullOrEmpty(dump))
            {
                // The build's input, for working on the build outside the engine.
                var w = new System.Text.StringBuilder();
                w.Append("verts ").AppendLine(string.Join(" ", g.Verts.ConvertAll(f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
                w.Append("tris ").AppendLine(string.Join(" ", g.Tris));
                w.Append("areas ").AppendLine(string.Join(" ", g.Areas));
                foreach (var (min, max, area) in g.Modifiers)
                    w.AppendLine(FormattableString.Invariant($"modifier {min.x:R} {min.y:R} {min.z:R} {max.x:R} {max.y:R} {max.z:R} {area}"));
                w.AppendLine(FormattableString.Invariant($"settings {cs:R} {tileSize} {settings.agentHeight:R} {settings.agentClimb:R} {settings.agentRadius:R} {settings.agentSlope:R} {settings.minRegionArea:R} {g.Min.y:R} {g.Max.y:R}"));
                w.AppendLine(FormattableString.Invariant($"bounds {localBounds.min.x:R} {localBounds.min.y:R} {localBounds.min.z:R} {localBounds.max.x:R} {localBounds.max.y:R} {localBounds.max.z:R}"));
                System.IO.File.WriteAllText(dump + "." + data.GetInstanceID() + ".txt", w.ToString());
            }
            var verts = g.Verts.ToArray();
            var tris = g.Tris.ToArray();
            var areas = g.Areas.ToArray();
            var ctx = new RcContext();
            // Slopes too steep to walk are not walkable.
            float slopeCos = MathF.Cos(settings.agentSlope / 180f * MathF.PI);
            for (int i = 0; i < areas.Length; i++)
            {
                if (areas[i] == RcRecast.RC_NULL_AREA) continue;
                var a = new RcVec3f(verts[tris[i * 3] * 3], verts[tris[i * 3] * 3 + 1], verts[tris[i * 3] * 3 + 2]);
                var b = new RcVec3f(verts[tris[i * 3 + 1] * 3], verts[tris[i * 3 + 1] * 3 + 1], verts[tris[i * 3 + 1] * 3 + 2]);
                var c = new RcVec3f(verts[tris[i * 3 + 2] * 3], verts[tris[i * 3 + 2] * 3 + 1], verts[tris[i * 3 + 2] * 3 + 2]);
                var n = new RcVec3f();
                RcRecast.CalcTriNormal(a, b, c, ref n);
                // Either side up: meshes wind their faces either way (built-in planes and cubes differ).
                if (MathF.Abs(n.Y) <= slopeCos) areas[i] = RcRecast.RC_NULL_AREA;
            }
            var bmin = localBounds.min;
            var bmax = localBounds.max;
            int tx0 = Mathf.FloorToInt(bmin.x / tileWorld), tx1 = Mathf.FloorToInt(bmax.x / tileWorld);
            int tz0 = Mathf.FloorToInt(bmin.z / tileWorld), tz1 = Mathf.FloorToInt(bmax.z / tileWorld);
            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    var tile = BuildTile(ctx, settings, verts, tris, areas, g, tx, tz, tileWorld, tileSize, cs, ch, border, walkableHeight, walkableClimb, walkableRadius, localBounds);
                    if (tile != null) data.m_Tiles.Add(tile);
                }
        }

        static DtMeshData BuildTile(RcContext ctx, NavMeshBuildSettings settings, float[] verts, int[] tris, int[] areas, Geometry g,
            int tx, int tz, float tileWorld, int tileSize, float cs, float ch, int border, int walkableHeight, int walkableClimb, int walkableRadius, Bounds localBounds)
        {
            var tmin = new RcVec3f(tx * tileWorld, g.Min.y, tz * tileWorld);
            var tmax = new RcVec3f((tx + 1) * tileWorld, g.Max.y, (tz + 1) * tileWorld);
            // Only geometry the build bounds hold counts.
            tmin.X = Math.Max(tmin.X, localBounds.min.x) ; tmin.Z = Math.Max(tmin.Z, localBounds.min.z);
            tmax.X = Math.Min(tmax.X, localBounds.max.x); tmax.Z = Math.Min(tmax.Z, localBounds.max.z);
            if (tmin.X >= tmax.X || tmin.Z >= tmax.Z) return null;
            var hmin = new RcVec3f(tx * tileWorld - border * cs, g.Min.y, tz * tileWorld - border * cs);
            var hmax = new RcVec3f((tx + 1) * tileWorld + border * cs, g.Max.y + walkableHeight * ch, (tz + 1) * tileWorld + border * cs);
            int size = tileSize + border * 2;
            var solid = new RcHeightfield(size, size, hmin, hmax, cs, ch, border);
            RcRasterizations.RasterizeTriangles(ctx, verts, tris, areas, areas.Length, solid, walkableClimb);
            RcFilters.FilterLowHangingWalkableObstacles(ctx, walkableClimb, solid);
            // Unity's ledges are drops of more than one voxel over the climb (measured: a ramp's foot).
            RcFilters.FilterLedgeSpans(ctx, walkableHeight, walkableClimb + 1, solid);
            RcFilters.FilterWalkableLowHeightSpans(ctx, walkableHeight, solid);
            var chf = RcCompacts.BuildCompactHeightfield(ctx, walkableHeight, walkableClimb, solid);
            RcAreas.ErodeWalkableArea(ctx, walkableRadius, chf);
            // A volume marks the cells whose centre it holds (measured: a volume from x -2 to 2 gives
            // polygons from -2 to 2, not to the next cell).
            float half = cs * 0.5f;
            foreach (var (min, max, area) in g.Modifiers)
                RcAreas.MarkBoxArea(ctx, new[] { min.x + half, min.y, min.z + half }, new[] { max.x - half, max.y, max.z - half }, new RcAreaModification(area), chf);
            // Monotone regions left unmerged and Recast's 1.3-voxel contour error give the Editor's
            // polygons (measured against a baked level: its strips keep their vertices on the borders).
            int minRegion = (int)(settings.minRegionArea / (cs * cs));
            RcRegions.BuildRegionsMonotone(ctx, chf, minRegion, 0);
            var cset = RcContours.BuildContours(ctx, chf, 1.3f, 0, RcBuildContoursFlags.RC_CONTOUR_TESS_WALL_EDGES);
            if (cset.conts.Count == 0) return null;
            var pmesh = RcMeshs.BuildPolyMesh(ctx, cset, DtDetour.DT_VERTS_PER_POLYGON);
            if (pmesh.npolys == 0) return null;
            var dmesh = RcMeshDetails.BuildPolyMeshDetail(ctx, pmesh, chf, cs * 6f, ch);
            for (int i = 0; i < pmesh.npolys; i++)
            {
                pmesh.flags[i] = 1;
                pmesh.areas[i] = Math.Max(0, pmesh.areas[i] - 1);
            }
            var p = new DtNavMeshCreateParams
            {
                verts = pmesh.verts,
                vertCount = pmesh.nverts,
                polys = pmesh.polys,
                polyAreas = pmesh.areas,
                polyFlags = pmesh.flags,
                polyCount = pmesh.npolys,
                nvp = pmesh.nvp,
                detailMeshes = dmesh?.meshes,
                detailVerts = dmesh?.verts,
                detailVertsCount = dmesh?.nverts ?? 0,
                detailTris = dmesh?.tris,
                detailTriCount = dmesh?.ntris ?? 0,
                walkableHeight = settings.agentHeight,
                walkableRadius = settings.agentRadius,
                walkableClimb = settings.agentClimb,
                bmin = pmesh.bmin,
                bmax = pmesh.bmax,
                cs = cs,
                ch = ch,
                buildBvTree = true,
                tileX = tx,
                tileZ = tz,
            };
            return DtNavMeshBuilder.CreateNavMeshData(p);
        }
    }
}
