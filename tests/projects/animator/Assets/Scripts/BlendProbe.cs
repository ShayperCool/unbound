using System;
using UnityEngine;

public partial class AnimatorProbe
{
    static void BlendTrees()
    {
        foreach (bool bundled in new[] { false, true })
        {
            var bundle = bundled ? AssetBundle.LoadFromFile(System.IO.Path.Combine(Application.dataPath, "..", "Reference", "blend-bundle", "blends")) : null;
            string prefix = bundled ? "blend.bundle." : "blend.";
            foreach (string name in new[] { "Simple", "SimpleNoCenter", "SimpleSkew", "SimpleAxis", "Directional", "Cartesian", "Direct", "DirectNormalized" })
            {
                var go = new GameObject("Blend " + name);
                var target = go.AddComponent<BlendWeightTarget>();
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = bundled ? bundle.LoadAsset<RuntimeAnimatorController>("Blend" + name) : Resources.Load<RuntimeAnimatorController>("Blend" + name);
                animator.Update(0.01f);
                if (name == "Direct" || name == "DirectNormalized")
                {
                    foreach (float input in new[] { -1f, 0f, 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f })
                    {
                        for (int i = 0; i < 9; i++) animator.SetFloat("d" + i, i == 0 ? input : i % 2 == 0 ? input * 0.5f : input * 0.25f);
                        animator.Update(0.01f);
                        Value(prefix + name + "." + F(input), "[" + string.Join(",", Array.ConvertAll(target.Weights(), F)) + "]");
                    }
                }
                else
                {
                    foreach (float x in new[] { -2f, -1.5f, -1f, -0.5f, 0f, 0.25f, 0.75f, 1f, 1.5f, 2f })
                        foreach (float y in new[] { -2f, -1.5f, -1f, -0.5f, 0f, 0.25f, 0.75f, 1f, 1.5f, 2f })
                        {
                            animator.SetFloat("x", x); animator.SetFloat("y", y);
                            animator.Update(0.01f);
                            Value(prefix + name + "." + F(x) + "." + F(y), "[" + string.Join(",", Array.ConvertAll(target.Weights(), F)) + "]");
                        }
                }
                DestroyImmediate(go);
            }
            if (bundle != null) bundle.Unload(true);
        }
    }
}
