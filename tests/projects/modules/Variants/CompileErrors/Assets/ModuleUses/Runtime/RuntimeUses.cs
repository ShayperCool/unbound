using UnityEngine;

// A runtime assembly using the APIs of modules the manifest leaves out (Screen Capture, Physics,
// Cloth), in each way a name can be written.
public class RuntimeUses : MonoBehaviour
{
    Rigidbody m_Body;
    UnityEngine.BoxCollider m_Box;

    void Start()
    {
        ScreenCapture.CaptureScreenshot("simple.png");
        UnityEngine.ScreenCapture.CaptureScreenshot("qualified.png");
        var mode = UnityEngine.ScreenCapture.StereoScreenCaptureMode.BothEyes;
        Debug.Log(mode);
        Debug.Log(typeof(UnityEngine.Rigidbody));
        Debug.Log(GetComponent<Cloth>());
        Debug.Log(Physics.gravity);
        Debug.Log(m_Body);
        Debug.Log(m_Box);
    }
}
