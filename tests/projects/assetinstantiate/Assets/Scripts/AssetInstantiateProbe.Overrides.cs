using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

public partial class AssetInstantiateProbe
{
    void OverrideEnumeration()
    {
        var controller = Resources.Load<RuntimeAnimatorController>("FollowupGraph");
        var clip = controller.animationClips[0];
        var replacement = Resources.Load<AnimationClip>("FollowupReplacement");
        var source = new AnimatorOverrideController(controller);
        var clone = Object.Instantiate(source);
        OverrideValues("unset.source", source, clip, null);
        OverrideValues("unset.clone", clone, clip, null);
        clone[clip] = replacement;
        OverrideValues("set.source", source, clip, null);
        OverrideValues("set.clone", clone, clip, replacement);
        clone[clip] = null;
        OverrideValues("clear.clone", clone, clip, null);
        Object.DestroyImmediate(clone); Object.DestroyImmediate(source);
    }
    void OverrideValues(string key, AnimatorOverrideController controller, AnimationClip clip, AnimationClip replacement)
    {
        V(key + ".indexer", controller[clip] == (replacement != null ? replacement : clip));
        V(key + ".name_indexer", controller[clip.name] == (replacement != null ? replacement : clip));
        var values = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        controller.GetOverrides(values);
        V(key + ".count", values.Count); V(key + ".original", values[0].Key == clip);
        V(key + ".raw", values[0].Value == replacement);
#pragma warning disable CS0618
        var pairs = controller.clips;
#pragma warning restore CS0618
        V(key + ".pairs.count", pairs.Length); V(key + ".pairs.original", pairs[0].originalClip == clip);
        V(key + ".pairs.raw", pairs[0].overrideClip == replacement);
    }
}
