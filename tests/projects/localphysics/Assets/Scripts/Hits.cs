using UnityEngine;

// Records contact and trigger messages with the probe's current phase.
public class Hits : MonoBehaviour
{
    void Add(string kind, Component other) => LocalPhysicsProbe.Events.Add(LocalPhysicsProbe.Phase + ":" + kind + ":" + name + "<" + other.name);

    // What the static queries find from inside a collision callback: the default scene's floor.
    void AddWithQuery(string kind, Component other)
    {
        string found;
        if (kind.EndsWith("2D"))
        {
            var hit = Physics2D.Raycast(new Vector2(-20, 10), Vector2.down, 100);
            found = hit.collider == null ? "none" : hit.collider.name;
        }
        else found = Physics.Raycast(new Vector3(-20, 10, 0), Vector3.down, out var hit3, 100) ? hit3.collider.name : "none";
        LocalPhysicsProbe.Events.Add(LocalPhysicsProbe.Phase + ":" + kind + ":" + name + "<" + other.name + "|static:" + found);
    }
    void OnCollisionEnter(Collision c) => AddWithQuery("CollisionEnter", c.collider);
    void OnCollisionExit(Collision c) => Add("CollisionExit", c.collider);
    void OnTriggerEnter(Collider c) => Add("TriggerEnter", c);
    void OnTriggerExit(Collider c) => Add("TriggerExit", c);
    void OnCollisionEnter2D(Collision2D c) => AddWithQuery("CollisionEnter2D", c.collider);
    void OnCollisionExit2D(Collision2D c) => Add("CollisionExit2D", c.collider);
    void OnTriggerEnter2D(Collider2D c) => Add("TriggerEnter2D", c);
    void OnTriggerExit2D(Collider2D c) => Add("TriggerExit2D", c);
}
