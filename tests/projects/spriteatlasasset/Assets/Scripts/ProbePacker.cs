using UnityEditor.U2D;

public class ProbePacker : ScriptablePacker
{
    public override bool Pack(
        SpriteAtlasPackingSettings config,
        SpriteAtlasTextureSettings setting,
        PackerData input) => false;
}
