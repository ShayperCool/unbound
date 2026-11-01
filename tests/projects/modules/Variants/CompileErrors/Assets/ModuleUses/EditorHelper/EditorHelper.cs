using UnityEngine;

// An Editor-only assembly compiles against every module, the disabled ones included, and exposes
// their types to the runtime assembly that references it.
public static class EditorHelper
{
    public static ScreenCapture.StereoScreenCaptureMode Mode => ScreenCapture.StereoScreenCaptureMode.LeftEye;
    public static Rigidbody Body(GameObject gameObject) => gameObject.GetComponent<Rigidbody>();
    public static int Count() => 1;
}
