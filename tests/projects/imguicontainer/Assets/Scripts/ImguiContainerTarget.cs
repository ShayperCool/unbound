using System;
using UnityEngine;

[Serializable] public class ImguiDrawn { public int value = 5; }

[Serializable] public class ImguiNested { public float inner = 1.5f; }

public class ImguiTaggedAttribute : PropertyAttribute { }

public class ImguiContainerTarget : ScriptableObject
{
    public ImguiDrawn drawn = new ImguiDrawn();
    public ImguiNested nested = new ImguiNested();
    [ImguiTagged] public float ranged = 0.5f;
    [Space(20)] [ImguiTagged] public float spaced = 0.25f;
    [Space(20)] public float plainSpaced = 0.75f;
    [Space(20)] [ImguiTagged] public float spacedImgui = 0.35f;
}
