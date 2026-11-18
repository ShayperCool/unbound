using UnityEngine;

// Records contact and trigger messages with the probe's current phase.
public class Hits : MonoBehaviour
{
    void Add(string kind, Component other) => LocalPhysicsProbe.Events.Add(LocalPhysicsProbe.Phase + ":" + kind + ":" + name + "<" + other.name);
    void OnCollisionEnter(Collision c) => Add("CollisionEnter", c.collider);
    void OnCollisionExit(Collision c) => Add("CollisionExit", c.collider);
    void OnTriggerEnter(Collider c) => Add("TriggerEnter", c);
    void OnTriggerExit(Collider c) => Add("TriggerExit", c);
    void OnCollisionEnter2D(Collision2D c) => Add("CollisionEnter2D", c.collider);
    void OnCollisionExit2D(Collision2D c) => Add("CollisionExit2D", c.collider);
    void OnTriggerEnter2D(Collider2D c) => Add("TriggerEnter2D", c);
    void OnTriggerExit2D(Collider2D c) => Add("TriggerExit2D", c);
}
