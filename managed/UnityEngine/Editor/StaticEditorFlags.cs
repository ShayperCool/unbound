namespace UnityEditor
{
    /// <summary>A GameObject's static flags (m_StaticEditorFlags), as the release declares them.</summary>
    [System.Flags]
    public enum StaticEditorFlags
    {
        [System.ComponentModel.Description("Contribute Global Illumination")] ContributeGI = 1,
        OccluderStatic = 2,
        OccludeeStatic = 16,
        BatchingStatic = 4,
        [System.Obsolete("Enum member StaticEditorFlags.NavigationStatic has been deprecated. The precise selection of the objects is now done using NavMeshBuilder.CollectSources() and NavMeshBuildMarkup.", false)] NavigationStatic = 8,
        [System.Obsolete("Enum member StaticEditorFlags.OffMeshLinkGeneration has been deprecated. You can now use NavMeshBuilder.CollectSources() and NavMeshBuildMarkup to nominate the objects that will generate Off-Mesh Links.", false)] OffMeshLinkGeneration = 32,
        ReflectionProbeStatic = 64,
        [System.Obsolete("Enum member StaticEditorFlags.LightmapStatic has been deprecated. Use StaticEditorFlags.ContributeGI instead. (UnityUpgradable) -> ContributeGI", false)] LightmapStatic = 1,
    }
}
