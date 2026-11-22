using System;
using System.Collections;
using System.Runtime.CompilerServices;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public sealed class StackMessageReturns : MonoBehaviour
{
    sealed class Routine : IEnumerator
    {
        public object Current => null;
        public bool MoveNext() { Moves++; return false; }
        public void Reset() { }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("STACK_MESSAGES_OUT")))
            new GameObject("StackMessageReturns").AddComponent<StackMessageReturns>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    Routine Start()
    {
        StackMessages.Report("start");
        return new Routine();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    int LateUpdate()
    {
        StackMessages.Report("discard");
        return 41;
    }

    internal static int Moves;
}
