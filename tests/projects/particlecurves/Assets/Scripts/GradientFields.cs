using UnityEngine;

/// <summary>Script fields of type MinMaxGradient, authored by ProbeRunner.CreateGradients with
/// values whose unused parts differ, to record how they serialize and read back.</summary>
public class GradientFields : MonoBehaviour
{
    public ParticleSystem.MinMaxGradient color;
    public ParticleSystem.MinMaxGradient twoColors;
    public ParticleSystem.MinMaxGradient gradient;
    public ParticleSystem.MinMaxGradient twoGradients;
    public ParticleSystem.MinMaxGradient randomColor;
    public ParticleSystem.MinMaxGradient initialized = new Color(0.25f, 0.5f, 0.75f, 1f);
}
