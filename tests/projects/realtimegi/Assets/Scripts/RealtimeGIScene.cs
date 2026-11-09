using UnityEngine;

/// <summary>What the probe changes in a realtime GI scene and where it samples light probes.</summary>
public class RealtimeGIScene : MonoBehaviour
{
    public Light point;
    public Light spot;
    public Renderer panel;
    public Renderer leftWall;
    public Renderer sphere;
    public Renderer[] lit;
    public Vector3[] samples;
}
