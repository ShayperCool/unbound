using UnityEngine;

/// <summary>Moves its object under <see cref="target"/> the first time its parent has changed.</summary>
public class ReparentOnAfter : MonoBehaviour
{
    public Transform target;
    bool done;

    void OnTransformParentChanged()
    {
        if (done || target == null) return;
        done = true;
        transform.SetParent(target, false);
    }
}
