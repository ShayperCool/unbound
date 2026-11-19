using UnityEngine;

/// <summary>A state machine behaviour with a serialized field, added to states and state machines
/// through the Editor's controller API.</summary>
public class ProbeBehaviour : StateMachineBehaviour
{
    public float weight = 0.5f;
    public string label = "probe";
}
