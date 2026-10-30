using UnityEngine;

public class BlendWeightTarget : MonoBehaviour
{
    public float w0, w1, w2, w3, w4, w5, w6, w7, w8;
    public float[] Weights() => new[] { w0,w1,w2,w3,w4,w5,w6,w7,w8 };
}
