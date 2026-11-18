using UnityEngine;

/// <summary>Moves its object under <see cref="target"/> the first time it hears that its parent is
/// about to change (a reparent from inside the notification of another).</summary>
public class ReparentOnBefore : MonoBehaviour
{
    public Transform target;
    bool done;

    void OnBeforeTransformParentChanged()
    {
        if (done || target == null) return;
        done = true;
        transform.SetParent(target, false);
    }
}
