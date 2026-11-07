using System.Collections.Generic;
using UnityEngine;

public class ModifyReuseLog : MonoBehaviour
{
    public static readonly List<Collision> collisions = new List<Collision>();
    public static readonly List<int> contacts = new List<int>();
    void OnCollisionEnter(Collision collision) { collisions.Add(collision); contacts.Add(collision.contactCount); }
    void OnCollisionStay(Collision collision) { collisions.Add(collision); contacts.Add(collision.contactCount); }
}
