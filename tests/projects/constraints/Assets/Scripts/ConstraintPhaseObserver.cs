using System;
using UnityEngine;
public class ConstraintPhaseObserver : MonoBehaviour
{
    public Action<int> Step,Late;
    public int Frame;
    void Update()=>Step?.Invoke(++Frame);
    void LateUpdate()=>Late?.Invoke(Frame);
}
