using UnityEngine;

/// <summary>References into the .probepair assets, one per asset and file id, in the order
/// FileIdProbe names them: untyped, as Texture2D and as ProbePair.</summary>
public class FileIdRefs : ScriptableObject
{
    public Object[] any;
    public Texture2D[] textures;
    public ProbePair[] data;
}
