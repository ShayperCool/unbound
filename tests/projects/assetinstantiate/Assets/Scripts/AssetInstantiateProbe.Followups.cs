using System;
using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public partial class AssetInstantiateProbe
{
    IEnumerator Followups()
    {
        Case("controller.graph", ControllerGraph);
        foreach (bool readable in new[] { true, false })
            Case("cube.gpu." + readable, () => GpuCube(readable));
        Case("cube.builtin", () => {
            var source = ReflectionProbe.defaultTexture as Cubemap;
            V("cube.builtin.present", source != null);
            if (source == null) return;
            V("cube.builtin.readable", source.isReadable);
            var clone = Clone("cube.builtin", source);
            if (clone == null) return;
            V("cube.builtin.clone.readable", clone.isReadable);
            V("cube.builtin.width", clone.width); V("cube.builtin.mips", clone.mipmapCount);
            V("cube.builtin.format", clone.format);
            for (int mip = 0; mip < source.mipmapCount; mip++)
                EqualImage("cube.builtin.gpu.mip." + mip, CubeImage(source, mip), CubeImage(clone, mip));
            var before = CubeImage(source);
            var write = new RenderTexture(source.width, source.width, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            write.Create(); RenderTexture.active = write; GL.Clear(false, true, Color.magenta); RenderTexture.active = null;
            Graphics.CopyTexture(write, 0, 0, clone, 0, 0);
            EqualImage("cube.builtin.source.after_write", before, CubeImage(source));
            EqualImage("cube.builtin.clone.after_write", before, CubeImage(clone));
            Object.DestroyImmediate(write);
            Object.DestroyImmediate(clone);
        });
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = 48000; config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        AudioSettings.Reset(config); Time.captureDeltaTime = 1024f / 48000;
        var listener = new GameObject("followup.listener"); listener.AddComponent<AudioListener>();
        V("audio.matrix.render.start", AudioRenderer.Start());
        foreach (int load in new[] { 0, 1, 2 })
        foreach (bool preload in new[] { false, true })
        foreach (bool background in new[] { false, true })
        {
            string key = "audio.matrix." + load + "." + preload + "." + background;
            var source = Resources.Load<AudioClip>("Followup" + load + (preload ? "1" : "0") + (background ? "1" : "0"));
            V(key + ".source.initial", source.loadState);
            var clone = Object.Instantiate(source);
            V(key + ".clone.initial", clone.loadState);
            V(key + ".type", clone.loadType); V(key + ".preload", clone.preloadAudioData); V(key + ".background", clone.loadInBackground);
            V(key + ".samples", clone.samples); V(key + ".frequency", clone.frequency); V(key + ".channels", clone.channels);
            V(key + ".load", clone.LoadAudioData());
            for (int wait = 0; wait < 120 && clone.loadState == AudioDataLoadState.Loading; wait++) yield return new WaitForSecondsRealtime(.02f);
            V(key + ".loaded", clone.loadState); V(key + ".source.after_load", source.loadState);
            var playing = new GameObject("followup.audio").AddComponent<AudioSource>();
            playing.clip = clone; playing.loop = true; playing.spatialBlend = 0; playing.Play();
            bool audible = false;
            for (int i = 0; i < 12; i++)
            {
                yield return null;
                var data = new NativeArray<float>(2048, Allocator.Temp);
                V(key + ".render." + i, AudioRenderer.Render(data));
                for (int j = 0; j < data.Length; j++) if (Math.Abs(data[j]) > .001f) audible = true;
                data.Dispose();
            }
            V(key + ".playing", playing.isPlaying); V(key + ".audible", audible);
            playing.Stop(); Object.DestroyImmediate(playing.gameObject);
            V(key + ".unload", clone.UnloadAudioData()); V(key + ".unloaded", clone.loadState);
            V(key + ".source.after_unload", source.loadState);
            Object.DestroyImmediate(source, true);
            V(key + ".survivor.load", clone.LoadAudioData());
            for (int wait = 0; wait < 120 && clone.loadState == AudioDataLoadState.Loading; wait++) yield return new WaitForSecondsRealtime(.02f);
            V(key + ".survivor.loaded", clone.loadState);
            Object.DestroyImmediate(clone);
        }
        var owner = Resources.Load<AudioClip>("FollowupOwner");
        owner.LoadAudioData();
        var first = Object.Instantiate(owner);
        V("audio.owners.original.unload", owner.UnloadAudioData());
        V("audio.owners.original.state", owner.loadState); V("audio.owners.clone.state", first.loadState);
        V("audio.owners.clone.unload", first.UnloadAudioData());
        V("audio.owners.clone.after_unload", first.loadState);
        Object.DestroyImmediate(first); Object.DestroyImmediate(owner, true);
        AudioRenderer.Stop(); Object.DestroyImmediate(listener);
    }
    void ControllerGraph()
    {
        var original = Resources.Load<RuntimeAnimatorController>("FollowupGraph");
        var clone = Object.Instantiate(original);
        V("graph.clips", clone.animationClips.Length);
        V("graph.source.clips", original.animationClips.Length);
        V("graph.first_clip.shared", original.animationClips[0] == clone.animationClips[0]);
        var a = new GameObject("graph.original").AddComponent<Animator>();
        var b = new GameObject("graph.clone").AddComponent<Animator>();
        a.runtimeAnimatorController = original; b.runtimeAnimatorController = clone;
        a.Update(0); b.Update(0);
        V("graph.layers.original", a.layerCount); V("graph.layers.clone", b.layerCount);
        V("graph.parameters.original", a.parameterCount); V("graph.parameters.clone", b.parameterCount);
        b.SetLayerWeight(1, .25f); V("graph.layer.clone", b.GetLayerWeight(1)); V("graph.layer.original", a.GetLayerWeight(1));
        b.SetFloat("Value", .75f); V("graph.parameter.clone", b.GetFloat("Value")); V("graph.parameter.original", a.GetFloat("Value"));
        b.Play("Other", 0, .5f); b.Update(0);
        V("graph.state.clone.other", b.GetCurrentAnimatorStateInfo(0).IsName("Other"));
        V("graph.state.original.base", a.GetCurrentAnimatorStateInfo(0).IsName("Base"));
        var replacement = Resources.Load<AnimationClip>("FollowupReplacement");
        var overrideSource = new AnimatorOverrideController(clone);
        overrideSource[clone.animationClips[0]] = replacement;
        var overrideClone = Object.Instantiate(overrideSource);
        V("graph.override.clips", overrideClone.animationClips.Length);
        V("graph.override.initial.shared", overrideClone[clone.animationClips[0]] == replacement);
        overrideClone[clone.animationClips[0]] = null;
        V("graph.override.original.preserved", overrideSource[clone.animationClips[0]] == replacement);
        V("graph.override.clone.reset", overrideClone[clone.animationClips[0]] == clone.animationClips[0]);
        // The runtime returns clip references and a copied array, not writable controller layers.
        var clips = clone.animationClips; clips[0] = replacement;
        V("graph.clip_array.independent", clone.animationClips[0] != replacement);
        V("graph.original.clip_array.independent", original.animationClips[0] != replacement);
        Object.DestroyImmediate(a.gameObject); Object.DestroyImmediate(b.gameObject);
        Object.DestroyImmediate(overrideClone); Object.DestroyImmediate(overrideSource); Object.DestroyImmediate(clone);
    }
    byte[] CubeImage(Cubemap cube, int mip = 0)
    {
        var result = new byte[6 * 4 * 4 * 4];
        var target = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        target.Create();
        var material = new Material(Shader.Find("Hidden/AssetCloneCube"));
        material.SetTexture("_Cube", cube); material.SetFloat("_Mip", mip);
        var directions = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        for (int face = 0; face < 6; face++)
        {
            material.SetVector("_Direction", directions[face]);
            Graphics.Blit(null, target, material);
            var data = ReadTarget(target);
            Array.Copy(data, 0, result, face * data.Length, data.Length);
        }
        RenderTexture.active = null; Object.DestroyImmediate(target); Object.DestroyImmediate(material);
        return result;
    }
    void GpuCube(bool readable)
    {
        string key = "cube.gpu." + readable;
        var cube = new Cubemap(4, TextureFormat.RGBA32, false) { name = "gpu-cube" };
        var red = new Color[16]; for (int i = 0; i < red.Length; i++) red[i] = Color.red;
        for (int face = 0; face < 6; face++) cube.SetPixels(red, (CubemapFace)face);
        cube.Apply(false, !readable);
        var target = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); target.Create();
        RenderTexture.active = target; GL.Clear(false, true, Color.green); RenderTexture.active = null;
        for (int face = 0; face < 6; face++) Graphics.CopyTexture(target, 0, 0, cube, face, 0);
        var clone = Clone(key, cube);
        if (clone != null)
        {
            V(key + ".readable", clone.isReadable);
            var sourcePixels = CubeImage(cube); var clonePixels = CubeImage(clone);
            for (int channel = 0; channel < 4; channel++)
            { V(key + ".source.channel." + channel, sourcePixels[channel]); V(key + ".clone.channel." + channel, clonePixels[channel]); }
            var before = CubeImage(cube); Object.DestroyImmediate(clone);
            EqualImage(key + ".source.after_destroy", before, CubeImage(cube));
        }
        Object.DestroyImmediate(cube); Object.DestroyImmediate(target);
    }
}
