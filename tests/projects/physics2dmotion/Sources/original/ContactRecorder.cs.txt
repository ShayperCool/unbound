using UnityEngine;

/// <summary>Records the collision and trigger messages its object receives, with the step they
/// arrived in.</summary>
public class ContactRecorder : MonoBehaviour
{
    public static int Step;
    int m_Count;

    void Record(string what, Collision2D c)
    {
        string k = name + "." + (m_Count++) + "." + what;
        ProbeLog.Value(k + ".step", Step);
        ProbeLog.Value(k + ".other", c.collider.name);
        ProbeLog.Value(k + ".contacts", c.contactCount);
        ProbeLog.Value(k + ".relativeVelocity", c.relativeVelocity);
        if (c.contactCount > 0)
        {
            var p = c.GetContact(0);
            ProbeLog.Value(k + ".point", p.point);
            ProbeLog.Value(k + ".normal", p.normal);
            ProbeLog.Value(k + ".separation", p.separation);
            ProbeLog.Value(k + ".normalImpulse", p.normalImpulse);
        }
    }

    void Record(string what, Collision c)
    {
        string k = name + "." + (m_Count++) + "." + what;
        ProbeLog.Value(k + ".step", Step);
        ProbeLog.Value(k + ".other", c.collider.name);
        ProbeLog.Value(k + ".contacts", c.contactCount);
        ProbeLog.Value(k + ".relativeVelocity", c.relativeVelocity);
        ProbeLog.Value(k + ".impulse", c.impulse);
        if (c.contactCount > 0)
        {
            var p = c.GetContact(0);
            ProbeLog.Value(k + ".point", p.point);
            ProbeLog.Value(k + ".normal", p.normal);
            ProbeLog.Value(k + ".separation", p.separation);
        }
    }

    void Trigger(string what, string other)
    {
        string k = name + "." + (m_Count++) + "." + what;
        ProbeLog.Value(k + ".step", Step);
        ProbeLog.Value(k + ".other", other);
    }

    void OnCollisionEnter2D(Collision2D c) => Record("enter2d", c);
    void OnCollisionExit2D(Collision2D c) => Record("exit2d", c);
    void OnTriggerEnter2D(Collider2D c) => Trigger("triggerEnter2d", c.name);
    void OnTriggerExit2D(Collider2D c) => Trigger("triggerExit2d", c.name);
    void OnCollisionEnter(Collision c) => Record("enter", c);
    void OnCollisionExit(Collision c) => Record("exit", c);
    void OnTriggerEnter(Collider c) => Trigger("triggerEnter", c.name);
    void OnTriggerExit(Collider c) => Trigger("triggerExit", c.name);
}
