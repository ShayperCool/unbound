using System;
using UnityEngine;
[DefaultExecutionOrder(1000)]
public class ForceLateObserver : MonoBehaviour
{
    public Action Step;
    void FixedUpdate()=>Step?.Invoke();
}
