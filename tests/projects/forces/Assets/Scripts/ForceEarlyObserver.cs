using System;
using UnityEngine;
[DefaultExecutionOrder(-1000)]
public class ForceEarlyObserver : MonoBehaviour
{
    public Action Step;
    void FixedUpdate()=>Step?.Invoke();
}
