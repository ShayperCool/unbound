using UnityEngine;
using UnityEngine.Animations;

/// <summary>Fields an animation clip or AnimationMode can drive.</summary>
public class ProbeTarget : MonoBehaviour
{
    public float amount = 1.5f;
    public int steps = 2;
    public bool flag;
    public Vector3 offset = new Vector3(1, 2, 3);
    public Object reference;
    [DiscreteEvaluation] public int counted = 4;
    public ProbeMode mode = ProbeMode.Third;
}

public enum ProbeMode
{
    First,
    Second,
    Third,
}
