using UnityEngine;
public class JointBreakObserver : MonoBehaviour
{
    void OnJointBreak2D(Joint2D joint)=>Joint2DProbe.Event(joint.GetType().Name+":"+joint.enabled+":"+joint.breakAction);
}
