using System.Globalization;
using UnityEngine;
public class RectWorldWatch : MonoBehaviour
{
    public Transform leaf, sibling;
    public bool capture;
    void OnRectTransformDimensionsChange()
    {
        if (!capture) return;
        var a = leaf.localToWorldMatrix.MultiplyPoint3x4(Vector3.zero);
        var b = sibling.localToWorldMatrix.MultiplyPoint3x4(Vector3.zero);
        RectMessagesProbe.Hit(name, string.Format(CultureInfo.InvariantCulture, "world-leaf={0:R},{1:R},{2:R}", a.x, a.y, a.z));
        RectMessagesProbe.Hit(name, string.Format(CultureInfo.InvariantCulture, "world-sibling={0:R},{1:R},{2:R}", b.x, b.y, b.z));
    }
}
