using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

// Rigidbody2D.SlideResults: which backing fields are serialized fields, and what JsonUtility,
// FromJsonOverwrite and Instantiate do with its hits (RaycastHit2D is not a serializable type).
// Instantiate of a struct field's non-serialized member.
// VisualElement.Hierarchy: its interfaces and equality through the default comparer.
public class DeclarationExtrasProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("DeclarationExtrasProbe").AddComponent<DeclarationExtrasProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    [Serializable] public class Holder { public Rigidbody2D.SlideResults results; public int after = 3; }
    StreamWriter output;

    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("DECLARATION_EXTRAS_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector2 v) => F(v.x) + "/" + F(v.y);
    static string Hit(RaycastHit2D h) => V(h.point) + "," + V(h.normal) + "," + F(h.distance) + "," + F(h.fraction) + "," + V(h.centroid);
    static string State(Rigidbody2D.SlideResults r) =>
        V(r.remainingVelocity) + "|" + V(r.position) + "|" + r.iterationsUsed + "|" + Hit(r.slideHit) + "|" + Hit(r.surfaceHit);

    static RaycastHit2D MakeHit(float seed) => new RaycastHit2D
    {
        point = new Vector2(seed, seed + 1), normal = new Vector2(0, 1), distance = seed + 2, fraction = 0.25f, centroid = new Vector2(seed + 3, seed + 4),
    };

    static Rigidbody2D.SlideResults Make(float seed) => new Rigidbody2D.SlideResults
    {
        remainingVelocity = new Vector2(seed, -seed), position = new Vector2(seed * 2, 1), iterationsUsed = (int)seed,
        slideHit = MakeHit(seed + 10), surfaceHit = MakeHit(seed + 20),
    };

    void Record()
    {
        Put("slide.fields", string.Join(",", typeof(Rigidbody2D.SlideResults).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => f.Name + ":" + f.IsDefined(typeof(SerializeField), false))));
        Put("hit.serializable", typeof(RaycastHit2D).IsSerializable);

        var holder = new Holder { results = Make(5) };
        string json = JsonUtility.ToJson(holder);
        Put("json", json);
        Put("editorJson", EditorJsonUtility.ToJson(holder));
        Put("json.fromJson", State(JsonUtility.FromJson<Holder>(json).results));
        // Over a value whose hits differ: the text does not name them.
        var target = new Holder { results = Make(40) };
        JsonUtility.FromJsonOverwrite(json, target);
        Put("json.overwrite", State(target.results));

        // Instantiate copies the serialized fields only.
        var component = new GameObject("SlideHolder").AddComponent<SlideHolder>();
        component.results = Make(7);
        var copy = Instantiate(component);
        Put("clone", State(copy.results));
        Put("clone.original", State(component.results));

        // A struct's non-serialized member in the copy: the source's, the default or the copy's own.
        var pairs = new GameObject("PairHolder").AddComponent<PairHolder>();
        pairs.pair = new PairHolder.Pair { kept = 5, transient = 42, inner = new PairHolder.Inner { x = 6, hidden = 43 } };
        pairs.pairs = new[] { new PairHolder.Pair { kept = 6, transient = 43 } };
        var pairsCopy = Instantiate(pairs);
        Put("clone.pair", pairsCopy.pair.kept + "/" + pairsCopy.pair.transient + "/" + F(pairsCopy.pair.inner.x) + "/" + F(pairsCopy.pair.inner.hidden) + "|" + string.Join(",", pairsCopy.pairs.Select(p => p.kept + "/" + p.transient)));

        Put("hierarchy.interfaces", string.Join(",", typeof(VisualElement.Hierarchy).GetInterfaces().Select(i => i.FullName)));
        var a = new VisualElement();
        var b = new VisualElement();
        var comparer = EqualityComparer<VisualElement.Hierarchy>.Default;
        Put("hierarchy.equality", comparer.Equals(a.hierarchy, a.hierarchy) + "|" + comparer.Equals(a.hierarchy, b.hierarchy) + "|" +
            a.hierarchy.Equals(a.hierarchy) + "|" + a.hierarchy.Equals((object)b.hierarchy) + "|" + a.hierarchy.Equals(null) + "|" +
            (comparer.GetHashCode(a.hierarchy) == a.GetHashCode()) + "|" + comparer.Equals(default, default));
        Put("hierarchy.set", new HashSet<VisualElement.Hierarchy> { a.hierarchy, a.hierarchy, b.hierarchy, default }.Count);
    }
}
