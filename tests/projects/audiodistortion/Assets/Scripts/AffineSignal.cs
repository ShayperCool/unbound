using UnityEngine;

// The offset makes script/component order audible for both linear and nonlinear filters.
public class AffineSignal : MonoBehaviour
{
    void OnAudioFilterRead(float[] data, int channels)
    {
        for (int i = 0; i < data.Length; i++)
            data[i] = data[i] * 0.5f + (i % channels == 0 ? 0.03125f : -0.015625f);
    }
}
