using UnityEngine;

public class Hello : MonoBehaviour
{
    public int every = 30;

    void Update()
    {
        if (Time.frameCount % every == 0)
            Debug.Log("hello " + Message());
    }

    static string Message() => "one";
}
