using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class Nested
{
    public GameObject target;
}

/// <summary>Serialized object references the scene leaves unassigned (fileID 0), with a few
/// controls: a non-serialized field and a field the scene assigns.</summary>
public class Holder : MonoBehaviour
{
    public GameObject gameObjectField;
    public Transform transformField;
    public Rigidbody rigidbodyField;
    public Collider colliderField;
    public Marker markerField;
    public Data dataField;
    public Material materialField;
    public Texture2D textureField;
    public UnityEngine.Object objectField;
    [SerializeField] GameObject privateField;
    public GameObject[] arrayField;
    public List<Transform> listField;
    public Nested nestedField;
    [NonSerialized] public GameObject nonSerializedField;
    public Transform assignedField;
    public MonoBehaviour behaviourBaseField;
    public ScriptableObject scriptableBaseField;
    public Component componentField;
    public Behaviour behaviourField;

    public GameObject PrivateField => privateField;
}
