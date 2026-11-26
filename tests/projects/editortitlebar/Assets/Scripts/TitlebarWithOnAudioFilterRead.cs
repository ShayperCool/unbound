using UnityEngine;

/// <summary>A behaviour with only OnAudioFilterRead() (the probe records whether its titlebar has the enabled toggle).</summary>
public class TitlebarWithOnAudioFilterRead : MonoBehaviour
{
    void OnAudioFilterRead(float[] data, int channels) { }
}
