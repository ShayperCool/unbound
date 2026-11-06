using UnityEngine;

public class StartupReceiver : MonoBehaviour
{
    public int zero, early, later, moves;
    public float delta;
    void AtZero() => zero++;
    void AtEarly() => early++;
    void AtLater() => later++;
    void OnAnimatorMove()
    {
        var a = GetComponent<Animator>();
        moves++;
        delta = a.deltaPosition.y;
        a.ApplyBuiltinRootMotion();
    }
}
