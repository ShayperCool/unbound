using System.Collections.Generic;
using UnityEngine;

/// Counts OnParticleCollision calls on its GameObject and records the first call's events.
public class HitRecorder : MonoBehaviour
{
    static readonly List<ParticleCollisionEvent> s_Events = new List<ParticleCollisionEvent>();
    public int calls;
    public string first;
    // Which objects reported to this one (the order of messages within a frame is not specified).
    public readonly SortedSet<string> from = new SortedSet<string>(System.StringComparer.Ordinal);

    void OnParticleCollision(GameObject other)
    {
        calls++;
        from.Add(other.name);
        var ps = other.GetComponent<ParticleSystem>();
        if (ps == null || first != null) return;
        int safe = ps.GetSafeCollisionEventSize();
        int n = ps.GetCollisionEvents(gameObject, s_Events);
        var one = new ParticleCollisionEvent[1];
        int truncated = ps.GetCollisionEvents(gameObject, one);
        var c = n > 0 ? s_Events[0].colliderComponent : null;
        first = "events " + n + " safe " + safe + " array1 " + truncated
            + " collider " + (c == null ? "null" : c.GetType().Name + " " + c.name);
    }
}
