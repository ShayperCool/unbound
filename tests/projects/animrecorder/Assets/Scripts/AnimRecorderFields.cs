using UnityEngine;

/// <summary>A script whose fields the recorder binds: a float, an int, a flag and an enum.</summary>
public class AnimRecorderFields : MonoBehaviour
{
    public enum Mode { Off, Slow, Fast }

    public float speed = 1f;
    public int count;
    public bool flag;
    public Mode mode;
}
