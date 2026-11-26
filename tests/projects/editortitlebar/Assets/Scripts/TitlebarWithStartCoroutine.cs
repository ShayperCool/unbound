using UnityEngine;

/// <summary>A behaviour with only a coroutine Start() (the probe records whether its titlebar has the enabled toggle).</summary>
public class TitlebarWithStartCoroutine : MonoBehaviour
{
    System.Collections.IEnumerator Start() { yield break; }
}
