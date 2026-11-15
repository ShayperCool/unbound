using UnityEngine;
public class RectMessageWatch : MonoBehaviour
{
    public bool moveOnRect;
    public Transform destination;
    void OnRectTransformDimensionsChange()
    {
        RectMessagesProbe.Hit(name, "rect");
        if (moveOnRect) { moveOnRect = false; transform.SetParent(destination, false); }
    }
    void OnChildRectTransformDimensionsChange() { RectMessagesProbe.Hit(name, "child"); }
}
