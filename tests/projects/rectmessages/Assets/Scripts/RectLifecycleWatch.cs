using UnityEngine;
public class RectLifecycleWatch : MonoBehaviour
{
    bool awake;
    void Awake() { RectMessagesProbe.Hit(name, "awake"); awake = true; }
    void OnEnable() { RectMessagesProbe.Hit(name, "enable"); }
    void OnRectTransformDimensionsChange() { RectMessagesProbe.Hit(name, awake ? "rect-after-awake" : "rect-before-awake"); }
}
