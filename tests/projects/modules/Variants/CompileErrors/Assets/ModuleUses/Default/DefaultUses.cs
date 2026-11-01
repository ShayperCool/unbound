using UnityEngine;

// Assembly-CSharp: the project's default runtime assembly.
public class DefaultUses : MonoBehaviour
{
    void Start() => ScreenCapture.CaptureScreenshot("default.png");
}
