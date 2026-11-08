using UnityEngine;

/// <summary>Logs the state machine callbacks of the state it is on.</summary>
public class OrderState : StateMachineBehaviour
{
    static readonly string[] Names = { "A", "B", "C" };

    static string Detail(Animator animator, AnimatorStateInfo info, int layer)
    {
        string state = "?";
        foreach (var n in Names) if (info.IsName(n)) state = n;
        return "layer=" + layer + " state=" + state + " " + AnimCallbacks.States();
    }

    static void Log(Animator animator, string e, AnimatorStateInfo info, int layer)
    {
        var o = animator.GetComponent<Observer>();
        AnimCallbacks.Log((o != null ? o.label : animator.name) + ".state." + e, Detail(animator, info, layer));
    }

    public override void OnStateEnter(Animator animator, AnimatorStateInfo info, int layer) => Log(animator, "OnStateEnter", info, layer);
    public override void OnStateUpdate(Animator animator, AnimatorStateInfo info, int layer) => Log(animator, "OnStateUpdate", info, layer);
    public override void OnStateExit(Animator animator, AnimatorStateInfo info, int layer) => Log(animator, "OnStateExit", info, layer);
    public override void OnStateMove(Animator animator, AnimatorStateInfo info, int layer) => Log(animator, "OnStateMove", info, layer);
    public override void OnStateIK(Animator animator, AnimatorStateInfo info, int layer) => Log(animator, "OnStateIK", info, layer);
}
