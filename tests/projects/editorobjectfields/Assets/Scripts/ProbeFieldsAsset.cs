using System;
using UnityEngine;

[Flags]
public enum ProbeBits { None = 0, A = 1, B = 2, C = 4 }

public enum ProbeKind { First, Second, Third }

/// <summary>The properties EditorObjectFieldsProbe draws through SerializedProperty forms.</summary>
public sealed class ProbeFieldsAsset : ScriptableObject
{
    public Color color = new Color(0.25f, 0.5f, 0.75f, 1f);
    public Material material;
    public Texture2D texture;
    public Transform target;
    public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);
    public Gradient gradient = new Gradient();
    public LayerMask layers = 1 << 8;
    public ProbeKind kind = ProbeKind.Second;
    public ProbeBits bits = ProbeBits.A | ProbeBits.C;
    public Vector2 v2 = new Vector2(1, 2);
    public Vector3 v3 = new Vector3(1, 2, 3);
    public Vector4 v4 = new Vector4(1, 2, 3, 4);
    public Rect rect = new Rect(1, 2, 3, 4);
    public Bounds bounds = new Bounds(new Vector3(1, 2, 3), new Vector3(2, 4, 6));
    public Vector2Int v2i = new Vector2Int(1, 2);
    public Vector3Int v3i = new Vector3Int(1, 2, 3);
    public RectInt recti = new RectInt(1, 2, 3, 4);
    public BoundsInt boundsi = new BoundsInt(1, 2, 3, 4, 5, 6);
    public char letter = 'q';
}
