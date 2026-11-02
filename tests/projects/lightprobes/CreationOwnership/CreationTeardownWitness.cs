using UnityEngine;

/// <summary>
/// Marks an authored root of the first scene for <see cref="SceneCreationOwnership"/>: its
/// OnDestroy goes to the teardown file in Unity's actual order, so the outgoing scene's roots seen
/// inside another object's OnDestroy can be checked against what was already destroyed. It has no
/// other message and returns before touching the engine once the witness has closed its output.
/// </summary>
public class CreationTeardownWitness : MonoBehaviour
{
    void OnDestroy() => SceneCreationOwnership.Teardown(this);
}
