using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace UnityEditor.AI
{
    /// <summary>The Editor's NavMesh helpers packages use to collect sources (NavMeshSurface does
    /// when it bakes in an Editor stage): the sources of the stage scene's objects.</summary>
    public static partial class NavMeshEditorHelpers
    {
        public static void CollectSourcesInStage(Bounds includedWorldBounds, int includedLayerMask, NavMeshCollectGeometry geometry, int defaultArea,
            bool generateLinksByDefault, List<NavMeshBuildMarkup> markups, bool includeOnlyMarkedObjects, Scene stageProxy, List<NavMeshBuildSource> results) =>
            UnityEngine.AI.NavMeshBuilder.CollectSourcesInScene(includedWorldBounds, null, includedLayerMask, geometry, defaultArea, generateLinksByDefault,
                markups, includeOnlyMarkedObjects, stageProxy, results);

        public static void CollectSourcesInStage(Bounds includedWorldBounds, int includedLayerMask, NavMeshCollectGeometry geometry, int defaultArea,
            List<NavMeshBuildMarkup> markups, Scene stageProxy, List<NavMeshBuildSource> results) =>
            CollectSourcesInStage(includedWorldBounds, includedLayerMask, geometry, defaultArea, false, markups, false, stageProxy, results);

        public static void CollectSourcesInStage(Transform root, int includedLayerMask, NavMeshCollectGeometry geometry, int defaultArea,
            bool generateLinksByDefault, List<NavMeshBuildMarkup> markups, bool includeOnlyMarkedObjects, Scene stageProxy, List<NavMeshBuildSource> results) =>
            UnityEngine.AI.NavMeshBuilder.CollectSourcesInScene(null, root, includedLayerMask, geometry, defaultArea, generateLinksByDefault,
                markups, includeOnlyMarkedObjects, stageProxy, results);

        public static void CollectSourcesInStage(Transform root, int includedLayerMask, NavMeshCollectGeometry geometry, int defaultArea,
            List<NavMeshBuildMarkup> markups, Scene stageProxy, List<NavMeshBuildSource> results) =>
            CollectSourcesInStage(root, includedLayerMask, geometry, defaultArea, false, markups, false, stageProxy, results);
    }

    /// <summary>The Editor's former NavMesh builder: its stage collection is what
    /// NavMeshEditorHelpers collects (Unity 6000.0 marks these as upgradable to it).</summary>
    [System.Obsolete("UnityEditor NavMeshBuilder has been deprecated. Use UnityEngine.AI.NavMeshBuilder instead.")]
    public sealed partial class NavMeshBuilder
    {
        const string k_UseHelpers = "Use NavMeshEditorHelpers.CollectSourcesInStage() instead. (UnityUpgradable) -> NavMeshEditorHelpers.CollectSourcesInStage(*)";

        [System.Obsolete(k_UseHelpers)]
        public static void CollectSourcesInStage(Bounds includedWorldBounds, int includedLayerMask, NavMeshCollectGeometry geometry, int defaultArea,
            bool generateLinksByDefault, List<NavMeshBuildMarkup> markups, bool includeOnlyMarkedObjects, Scene stageProxy, List<NavMeshBuildSource> results) =>
            NavMeshEditorHelpers.CollectSourcesInStage(includedWorldBounds, includedLayerMask, geometry, defaultArea, generateLinksByDefault, markups,
                includeOnlyMarkedObjects, stageProxy, results);

        [System.Obsolete(k_UseHelpers)]
        public static void CollectSourcesInStage(Bounds includedWorldBounds, int includedLayerMask, NavMeshCollectGeometry geometry, int defaultArea,
            List<NavMeshBuildMarkup> markups, Scene stageProxy, List<NavMeshBuildSource> results) =>
            NavMeshEditorHelpers.CollectSourcesInStage(includedWorldBounds, includedLayerMask, geometry, defaultArea, markups, stageProxy, results);

        [System.Obsolete(k_UseHelpers)]
        public static void CollectSourcesInStage(Transform root, int includedLayerMask, NavMeshCollectGeometry geometry, int defaultArea,
            bool generateLinksByDefault, List<NavMeshBuildMarkup> markups, bool includeOnlyMarkedObjects, Scene stageProxy, List<NavMeshBuildSource> results) =>
            NavMeshEditorHelpers.CollectSourcesInStage(root, includedLayerMask, geometry, defaultArea, generateLinksByDefault, markups,
                includeOnlyMarkedObjects, stageProxy, results);

        [System.Obsolete(k_UseHelpers)]
        public static void CollectSourcesInStage(Transform root, int includedLayerMask, NavMeshCollectGeometry geometry, int defaultArea,
            List<NavMeshBuildMarkup> markups, Scene stageProxy, List<NavMeshBuildSource> results) =>
            NavMeshEditorHelpers.CollectSourcesInStage(root, includedLayerMask, geometry, defaultArea, markups, stageProxy, results);
    }
}
