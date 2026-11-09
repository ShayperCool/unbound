using UnityEngine;

// The offset makes script/component order audible for both linear and nonlinear filters.
public class AffineSignal : MonoBehaviour
{
    public bool Pattern;
    public float OutputScale = 1;
    int m_Frame;

    void OnAudioFilterRead(float[] data, int channels)
    {
        for (int i = 0; i < data.Length; i++) {
            float value = data[i] * 0.5f + (i % channels == 0 ? 0.03125f : -0.015625f);
            if (Pattern) value += (((m_Frame + i / channels) * 37 % 997) - 498) / 8192f;
            data[i] = value * OutputScale;
        }
        if (Pattern) m_Frame += data.Length / channels;
    }
}
