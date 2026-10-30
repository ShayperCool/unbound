using UnityEngine;
public class CanvasLifecycleEvents : MonoBehaviour
{
    void Awake()=>CanvasLifecycleProbe.Event(this,"Awake");
    void OnEnable()=>CanvasLifecycleProbe.Event(this,"OnEnable");
    void OnDisable()=>CanvasLifecycleProbe.Event(this,"OnDisable");
    void OnRectTransformDimensionsChange()=>CanvasLifecycleProbe.Event(this,"dimensions");
    void OnTransformParentChanged()=>CanvasLifecycleProbe.Event(this,"parent");
    void OnCanvasHierarchyChanged()=>CanvasLifecycleProbe.Event(this,"canvasHierarchy");
}
