using UnityEngine;
using UnityEngine.Animations;

/// <summary>Fields of several types, for curves and property modifications that set numbers,
/// enums and object references.</summary>
public class TypedTarget : MonoBehaviour
{
    public float f = 1.5f;
    public int i = 2;
    public bool b;
    public string label = "start";
    public Color color = Color.red;
    public ProbeMode mode = ProbeMode.Third;
    public ProbeMode mode2 = ProbeMode.Third;
    public ProbeMode mode3 = ProbeMode.Third;
    [DiscreteEvaluation] public int counted = 4;
    public Vector3 offset = new Vector3(1, 2, 3);
    public Object anyRef;
    public AnimationClip clipRef;
    public Motion motion;
    public Material material;
    public Texture texture;
    public Texture2D texture2D;
    public ScriptableObject so;
    public ProbeData data;
    public GameObject gameObjectRef;
    public Transform transformRef;
    public Component component;
    public Behaviour behaviour;
    public MonoBehaviour script;
    public ProbeTarget probe;
    public Mesh mesh;
    public Shader shader;
}
