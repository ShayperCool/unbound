using UnityEngine;

public class Marker : MonoBehaviour
{
    public int value;

    void Awake()
    {
        string path = ProbeLog.Path(transform);
        ProbeLog.Put("marker-awake/" + path + "#" + ProbeLog.Next("marker-awake/" + path), value.ToString());
    }
}
