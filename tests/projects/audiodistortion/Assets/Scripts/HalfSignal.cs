using UnityEngine;
public class HalfSignal : MonoBehaviour
{
    void OnAudioFilterRead(float[] data, int channels)
    {
        for (int i = 0; i < data.Length; i++) data[i] *= 0.5f;
    }
}
