using System;
using UnityEngine;

// A serializable struct with a non-serialized member, in a field and an array whose initializers
// give that member a value, for Instantiate.
public class PairHolder : MonoBehaviour
{
    [Serializable]
    public struct Pair
    {
        public int kept;
        [NonSerialized] public int transient;
        public Inner inner;
    }

    [Serializable]
    public struct Inner
    {
        public float x;
        [NonSerialized] public float hidden;
    }

    public Pair pair = new Pair { kept = 1, transient = 9, inner = new Inner { x = 1, hidden = 4 } };
    public Pair[] pairs = { new Pair { kept = 2, transient = 8 }, new Pair { kept = 3, transient = 7 } };
}
