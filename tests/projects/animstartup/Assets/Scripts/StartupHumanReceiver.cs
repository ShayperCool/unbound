using UnityEngine;

public class StartupHumanReceiver : MonoBehaviour
{
    public int moves, ik;
    void OnAnimatorMove()
    {
        moves++;
        GetComponent<Animator>().ApplyBuiltinRootMotion();
    }
    void OnAnimatorIK(int layer) => ik++;
}

public class StartupHumanIKReceiver : MonoBehaviour
{
    public int ik;
    void OnAnimatorIK(int layer) => ik++;
}
