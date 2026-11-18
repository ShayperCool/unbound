using UnityEditor;
class PPVersioned : AssetPostprocessor
{
    public override uint GetVersion() => 1;
    void OnPreprocessTexture() => PPLog.Event(this, "OnPreprocessTexture", assetPath, "v1");
}
