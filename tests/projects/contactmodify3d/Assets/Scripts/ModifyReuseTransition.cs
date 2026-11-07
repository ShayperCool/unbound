using System.Collections.Generic;
using UnityEngine;

public class ModifyReuseTransition : MonoBehaviour
{
    public static readonly List<string> routes = new List<string>();
    public static Collision retained;
    public static ContactPoint first;
    public static int count;
    void OnCollisionEnter(Collision collision)
    {
        routes.Add(gameObject.name);
        if (!gameObject.name.StartsWith("transition") || retained != null) return;
        retained = collision;
        first = collision.GetContact(0);
        count = collision.contactCount;
        Physics.reuseCollisionCallbacks = true;
    }
}
