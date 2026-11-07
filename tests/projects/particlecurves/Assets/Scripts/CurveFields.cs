using UnityEngine;

/// <summary>Script fields of type MinMaxCurve, authored by ProbeRunner.Create with values whose
/// constant and curve multiplier differ, to record how they serialize and read back.</summary>
public class CurveFields : MonoBehaviour
{
    public ParticleSystem.MinMaxCurve constant;
    public ParticleSystem.MinMaxCurve curve;
    public ParticleSystem.MinMaxCurve twoCurves;
    public ParticleSystem.MinMaxCurve twoConstants;
    public ParticleSystem.MinMaxCurve initialized = 4f;
}
