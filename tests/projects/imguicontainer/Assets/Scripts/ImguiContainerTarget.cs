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
    [Header("Stats")] public int headed = 1;
    [TextArea] public string note = "a";
    [TextArea(2, 5)] public string longNote = "one\ntwo\nthree\nfour";
    [TextArea(2, 5)] public string shortNote = "";
    [Space(12)] public bool flag;
    public int[] numbers = { 1, 2 };
    public Vector2 point;
    [Range(0f, 1f)] public float ratio = 0.5f;
    [Multiline(3)] public string multi = "m";
}
