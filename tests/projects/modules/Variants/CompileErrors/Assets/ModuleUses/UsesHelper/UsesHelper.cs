using UnityEngine;

// A runtime assembly reaching a disabled module's types only through an Editor-only assembly.
public class UsesHelper : MonoBehaviour
{
    void Start()
    {
        Debug.Log(EditorHelper.Count());
        Debug.Log(EditorHelper.Mode);
        Debug.Log(EditorHelper.Body(gameObject).mass);
    }
}
