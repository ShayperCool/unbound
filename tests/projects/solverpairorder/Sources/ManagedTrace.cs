using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

// Opt-in read-only inspection of the shared managed world. This records actual
// allocator IDs and edge order; it never changes bodies, proxies or contacts.
public static class ManagedTrace
{
    public static void RecordState(int step, string phase, List<Rigidbody2D> bodies)
    {
        string path = Environment.GetEnvironmentVariable("SOLVER_PAIR_MANAGED_OUT");
        if (string.IsNullOrEmpty(path)) return;
        var json = new StringBuilder("{\"event\":\"body-state\",\"step\":").Append(step)
            .Append(",\"phase\":\"").Append(phase).Append("\",\"bodies\":[");
        bool first = true;
        foreach (var rb in bodies)
        {
            if (!first) json.Append(','); first = false;
            var body = Member(rb, "m_Body");
            var rotation = Member(Member(body, "Transform"), "Rotation");
            json.Append("{\"name\":\"").Append(rb.name).Append('"');
            foreach (string field in new[] { "Sin", "Cos" })
                json.Append(",\"").Append(field.ToLowerInvariant()).Append("\":")
                    .Append(((float)Member(rotation, field)).ToString("R", CultureInfo.InvariantCulture));
            json.Append('}');
        }
        File.AppendAllText(path, json.Append("]}\n").ToString());
    }
    public static void RecordScenePairs(int step, string scenario, string phase)
    {
        string path = Environment.GetEnvironmentVariable("SOLVER_PAIR_MANAGED_OUT");
        if (string.IsNullOrEmpty(path)) return;
        var type = typeof(Rigidbody2D).Assembly.GetType("Unbound.Runtime.Physics2DWorld");
        if (type == null) return;
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var field = type.GetField("s_ContactPairs", flags) ?? type.GetField("s_PublicContactPairs", flags);
        if (field == null) return;
        var json = new StringBuilder("{\"event\":\"scene-pairs\",\"step\":").Append(step)
            .Append(",\"scenario\":\"").Append(scenario).Append("\",\"phase\":\"").Append(phase)
            .Append("\",\"pairs\":[");
        bool first = true;
        foreach (var pair in (IEnumerable)field.GetValue(null))
        {
            if (!first) json.Append(','); first = false;
            json.Append("{\"a\":\"").Append(((Collider2D)Member(pair, "A")).name)
                .Append("\",\"b\":\"").Append(((Collider2D)Member(pair, "B")).name)
                .Append("\",\"touching\":").Append((int)Member(pair, "Touching")).Append('}');
        }
        File.AppendAllText(path, json.Append("]}\n").ToString());
    }
    static object Member(object value, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = value.GetType();
        var field = type.GetField(name, flags);
        if (field != null) return field.GetValue(value);
        var property = type.GetProperty(name, flags);
        if (property != null) return property.GetValue(value, null);
        throw new MissingMemberException(type.FullName, name);
    }
    public static void Record(int step, string scenario, List<Rigidbody2D> bodies)
    {
        string path = Environment.GetEnvironmentVariable("SOLVER_PAIR_MANAGED_OUT");
        if (string.IsNullOrEmpty(path)) return;
        var json = new StringBuilder("{\"step\":").Append(step).Append(",\"scenario\":\"")
            .Append(scenario).Append("\",\"bodies\":[");
        bool first = true;
        foreach (var rb in bodies)
        {
            if (!first) json.Append(','); first = false;
            var body = Member(rb, "m_Body");
            json.Append("{\"name\":\"").Append(rb.name).Append("\",\"proxyIDs\":[");
            bool proxyFirst = true;
            foreach (var fixture in (IEnumerable)Member(body, "FixtureList"))
            {
                int count = (int)Member(fixture, "ProxyCount");
                var proxies = (Array)Member(fixture, "Proxies");
                for (int i = 0; i < count; i++)
                {
                    if (!proxyFirst) json.Append(','); proxyFirst = false;
                    json.Append((int)Member(proxies.GetValue(i), "ProxyId"));
                }
            }
            json.Append("],\"neighbours\":[");
            bool edgeFirst = true;
            foreach (var edge in (IEnumerable)Member(body, "ContactEdges"))
            {
                if (!edgeFirst) json.Append(','); edgeFirst = false;
                var other = (UnityEngine.Object)Member(Member(edge, "Other"), "UserData");
                json.Append('"').Append(other.name).Append('"');
            }
            json.Append("]}");
        }
        File.AppendAllText(path, json.Append("]}\n").ToString());
    }
}
