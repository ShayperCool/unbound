using System;
using UnityEngine;
using UnityEngine.Rendering;

public partial class LightingAssetsProbe
{
    void RecordVariantSets()
    {
        var shader = Shader.Find("Probe/VariantSets");
        void Make(string name, PassType pass, params string[] keywords) =>
            Record("variant-sets", "\"name\":" + S(name) + ",\"result\":" + Try(() =>
                Variant(new ShaderVariantCollection.ShaderVariant(shader, pass, keywords))));
        Make("none", PassType.Normal);
        foreach (var pair in new[] { new[] { "MC_A", "MC_B" }, new[] { "LC_A", "LC_B" },
            new[] { "SF_A", "SF_B" }, new[] { "LF_A", "LF_B" }, new[] { "DB_A", "DB_B" } })
        {
            Make(pair[0], PassType.Normal, pair[0]);
            Make(pair[1], PassType.Normal, pair[1]);
            Make(pair[0] + "+" + pair[1], PassType.Normal, pair);
            Make(pair[0] + " duplicate", PassType.Normal, pair[0], pair[0]);
            Make(pair[0] + " other pass", PassType.ForwardBase, pair[0]);
        }
        Make("independent sets", PassType.Normal, "MC_A", "LC_A", "SF_A", "LF_A");
        Make("second normal", PassType.Normal, "SECOND_A");
        Make("two normal passes", PassType.Normal, "MC_A", "SECOND_A");
        Make("first then second normal conflict", PassType.Normal, "MC_A", "MC_B", "SECOND_A");
        Make("second normal conflict", PassType.Normal, "SECOND_A", "SECOND_B");
        Make("forward", PassType.ForwardBase, "FORWARD_A");
        Make("forward conflict", PassType.ForwardBase, "FORWARD_A", "FORWARD_B");
        Make("wrong pass", PassType.Normal, "FORWARD_A");
        Make("placeholder", PassType.Normal, "_");
        Make("placeholder and keyword", PassType.Normal, "_", "MC_A");
        Make("double placeholder", PassType.Normal, "__");
        Make("empty", PassType.Normal, "");
        Make("empty repeated", PassType.Normal, "", "");
        Make("null entry", PassType.Normal, new string[] { null });
        Make("null keywords", PassType.Normal, null);
        Make("stereo", PassType.Normal, "STEREO_INSTANCING_ON");
        Make("fog", PassType.Normal, "FOG_LINEAR");
        Make("fog conflict", PassType.Normal, "FOG_LINEAR", "FOG_EXP");
        Make("placeholder dynamic", PassType.Normal, "_", "DB_A");
        Make("placeholder second pass", PassType.Normal, "_", "SECOND_A");
        Make("two stereo", PassType.Normal, "STEREO_INSTANCING_ON", "STEREO_MULTIVIEW_ON");
    }
}
