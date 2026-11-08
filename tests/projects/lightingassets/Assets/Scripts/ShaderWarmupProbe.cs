using UnityEngine;
using UnityEngine.Rendering;

public partial class LightingAssetsProbe
{
    void RecordShaderWarmup()
    {
        var shader = Shader.Find("Probe/Variants");
        var collection = new ShaderVariantCollection();
        void State(string step, bool? result = null) => Record("shader-warmup", "\"step\":" + S(step)
            + ",\"result\":" + (result.HasValue ? B(result.Value) : "null") + ",\"state\":" + Describe(collection));
        ShaderVariantCollection.ShaderVariant V(params string[] keywords) =>
            new ShaderVariantCollection.ShaderVariant(shader, PassType.Normal, keywords);
        State("empty");
        State("empty zero", collection.WarmUpProgressively(0));
        collection.WarmUp();
        State("empty full");
        collection.Add(V());
        collection.Add(V("PLAIN_ON"));
        collection.Add(V("RED_ON"));
        collection.Add(V("PLAIN_ON", "RED_ON"));
        State("four cold");
        State("negative", collection.WarmUpProgressively(-1));
        State("one", collection.WarmUpProgressively(1));
        State("zero", collection.WarmUpProgressively(0));
        State("two", collection.WarmUpProgressively(2));
        State("remaining", collection.WarmUpProgressively(1));
        State("already warm", collection.WarmUpProgressively(1));
        collection.Remove(V("PLAIN_ON"));
        State("remove resets");
        collection.WarmUp();
        State("full");
        collection.Add(V("ONE_A"));
        State("add resets");
        State("one before max", collection.WarmUpProgressively(1));
        State("max", collection.WarmUpProgressively(int.MaxValue));
        collection.Clear();
        State("clear");
        DestroyImmediate(collection);
    }
}
