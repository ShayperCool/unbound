namespace UnityEngine.Internal
{
    /// <summary>Marks the sub-assets (mixer groups and snapshots) the Editor does not duplicate on
    /// their own; Object.Instantiate still copies them (tests/projects/mixerclone).</summary>
    internal partial interface ISubAssetNotDuplicatable
    {
    }
}
