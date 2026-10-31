using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class CaptureClockProbe : MonoBehaviour
{
    readonly StringBuilder output = new StringBuilder();
    int frame;
    float requested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install() => new GameObject("Capture clock").AddComponent<CaptureClockProbe>();

    void Awake()
    {
        requested = float.Parse(Environment.GetEnvironmentVariable("CAPTURE_CLOCK_DURATION"), CultureInfo.InvariantCulture);
        Time.timeScale = float.Parse(Environment.GetEnvironmentVariable("CAPTURE_CLOCK_SCALE") ?? "1", CultureInfo.InvariantCulture);
        Time.captureDeltaTime = requested;
    }

    void Update()
    {
        output.Append("{\"frame\":").Append(frame)
            .Append(",\"requestedBits\":").Append(BitConverter.SingleToInt32Bits(requested))
            .Append(",\"durationBits\":").Append(BitConverter.SingleToInt32Bits(Time.captureDeltaTime))
            .Append(",\"fps\":").Append(Time.captureFramerate)
            .Append(",\"scaleBits\":").Append(BitConverter.SingleToInt32Bits(Time.timeScale))
            .Append(",\"timeBits\":").Append(BitConverter.SingleToInt32Bits(Time.time))
            .Append(",\"timeDoubleBits\":").Append(BitConverter.DoubleToInt64Bits(Time.timeAsDouble))
            .Append(",\"deltaBits\":").Append(BitConverter.SingleToInt32Bits(Time.deltaTime))
            .Append("}\n");
        if (++frame != 3) return;
        File.WriteAllText(Environment.GetEnvironmentVariable("CAPTURE_CLOCK_OUT"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
