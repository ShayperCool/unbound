using UnityEngine;

/// <summary>A behaviour with fields for tests/projects/presets.</summary>
public class Tuned : MonoBehaviour
{
    public int count = 3;
    public float speed = 1.5f;
    public string label = "tuned";
    public Color tint = Color.red;
    public Transform target;
    [SerializeField] int hidden = 7;
}
