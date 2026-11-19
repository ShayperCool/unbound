using System;
using UnityEngine;

/// <summary>Calls <see cref="Late"/> in every LateUpdate (after the frame's animation update) with
/// the number of frames since the hook was made.</summary>
public class FrameHook : MonoBehaviour
{
    public static Action<int> Late;
    int m_Frame;

    void LateUpdate()
    {
        m_Frame++;
        Late?.Invoke(m_Frame);
    }
}
