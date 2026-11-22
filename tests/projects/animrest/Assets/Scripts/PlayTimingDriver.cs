using System;
using System.Collections;
using UnityEngine;

public sealed class PlayTimingDriver : MonoBehaviour
{
    public Action OnFixed;
    public Action OnUpdate;
    public Action OnLate;
    public Action OnEndOfFrame;

    void FixedUpdate() => OnFixed?.Invoke();
    void Update() => OnUpdate?.Invoke();
    void LateUpdate() => OnLate?.Invoke();

    public void ScheduleAfterUpdate() => StartCoroutine(RunAfterUpdate());

    IEnumerator RunAfterUpdate()
    {
        yield return null;
        OnEndOfFrame?.Invoke();
    }
}
