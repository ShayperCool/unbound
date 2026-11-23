using System;
using UnityEngine;

public sealed class TransformMemoryCallbacks : MonoBehaviour
{
    internal Action ChildrenChanged;
    internal Action BeforeParentChanged;
    internal Action AfterParentChanged;
    internal Action Destroying;

    void OnTransformChildrenChanged() => ChildrenChanged?.Invoke();
    void OnBeforeTransformParentChanged() => BeforeParentChanged?.Invoke();
    void OnTransformParentChanged() => AfterParentChanged?.Invoke();
    void OnDestroy() => Destroying?.Invoke();
}
