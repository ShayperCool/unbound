using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// Material variants and material operations, quality settings (soft particles, shader globals and
// keywords they set, skin weights, the texture mipmap limit), raster states (alpha to coverage,
// depth clamping, wireframe, sRGB writes), additional vertex streams, billboards, occlusion
// components, static batching, and camera settings (HDR, the culling matrix, CopyFrom, the scene
// camera's).
public partial class FxProbe
{
    void MaterialValues(string prefix, Material m)
    {
        Try(prefix + ".isVariant", () => B(m.isVariant));
        Try(prefix + ".parent", () => N(m.parent));
        Try(prefix + ".color", () => C(m.color));
        Try(prefix + ".metallic", () => F(m.GetFloat("_Metallic")));
        Try(prefix + ".glossiness", () => F(m.GetFloat("_Glossiness")));
        Try(prefix + ".mainTexture", () => N(m.mainTexture));
        Try(prefix + ".mainTextureScale", () => V(m.mainTextureScale));
        Try(prefix + ".renderQueue", () => I(m.renderQueue));
        Try(prefix + ".keywords", () => "[" + string.Join(",", System.Array.ConvertAll(m.shaderKeywords, S)) + "]");
        Try(prefix + ".enableInstancing", () => B(m.enableInstancing));
        Try(prefix + ".doubleSidedGI", () => B(m.doubleSidedGI));
        Try(prefix + ".globalIlluminationFlags", () => S(m.globalIlluminationFlags.ToString()));
    }

    void Materials()
    {
        var parent = Resources.Load<Material>("Parent");
        var variant = Resources.Load<Material>("Variant");
        MaterialValues("material.parent", parent);
        MaterialValues("material.variant", variant);
        // A runtime change of the parent.
        parent.SetFloat("_Metallic", 0.9f);
        Try("material.variant.metallicAfterParentChange", () => F(variant.GetFloat("_Metallic")));
        parent.SetFloat("_Metallic", 0.3f);
        // A runtime variant.
        var made = Add(new Material(Shader.Find("Standard")));
        made.SetFloat("_Metallic", 0.05f);
        Try("material.made.parentSet", () =>
        {
            made.parent = parent;
            return B(made.isVariant);
        });
        MaterialValues("material.made", made);

        // Lerp between two materials.
        var a = Standard(new Color(1f, 0f, 0f, 1f), 0.2f);
        a.mainTexture = Checker();
        a.mainTexture.name = "checker a";
        a.mainTextureScale = new Vector2(1f, 1f);
        var b = Standard(new Color(0f, 0f, 1f, 0.5f), 0.8f, 1f);
        b.mainTexture = Checker(16, 2);
        b.mainTexture.name = "checker b";
        b.mainTextureScale = new Vector2(3f, 2f);
        b.mainTextureOffset = new Vector2(0.5f, 0.25f);
        var lerp = Standard(Color.white);
        lerp.Lerp(a, b, 0.25f);
        Value("material.lerp.color", C(lerp.color));
        Value("material.lerp.glossiness", F(lerp.GetFloat("_Glossiness")));
        Value("material.lerp.metallic", F(lerp.GetFloat("_Metallic")));
        Value("material.lerp.mainTexture", N(lerp.mainTexture));
        Value("material.lerp.st", V(new Vector4(lerp.mainTextureScale.x, lerp.mainTextureScale.y, lerp.mainTextureOffset.x, lerp.mainTextureOffset.y)));
        var lerpOther = Unlit(Color.white);
        lerpOther.Lerp(a, b, 0.5f);
        Value("material.lerp_other_shader.color", C(lerpOther.color));

        // Matching properties only.
        var copy = Unlit(Color.gray);
        copy.CopyMatchingPropertiesFromMaterial(b);
        Value("material.copy_matching.color", C(copy.color));
        Value("material.copy_matching.shader", S(copy.shader.name));
        Value("material.copy_matching.hasMainTex", B(copy.HasProperty("_MainTex")));
        var copyAll = Unlit(Color.gray);
        copyAll.CopyPropertiesFromMaterial(b);
        Value("material.copy_all.color", C(copyAll.color));
        Value("material.copy_all.glossiness", F(copyAll.GetFloat("_Glossiness")));

        // CRCs: the same settings give the same CRC; a change changes it.
        var c1 = Standard(new Color(0.5f, 0.5f, 0.5f));
        var c2 = Standard(new Color(0.5f, 0.5f, 0.5f));
        Value("material.crc.same", B(c1.ComputeCRC() == c2.ComputeCRC()));
        int before = c1.ComputeCRC();
        c1.SetFloat("_Glossiness", 0.9f);
        Value("material.crc.changedByFloat", B(before != c1.ComputeCRC()));
        before = c2.ComputeCRC();
        c2.EnableKeyword("_EMISSION");
        Value("material.crc.changedByKeyword", B(before != c2.ComputeCRC()));
        before = c2.ComputeCRC();
        c2.renderQueue = 2001;
        Value("material.crc.changedByQueue", B(before != c2.ComputeCRC()));
        Value("material.crc.values", "[" + I(parent.ComputeCRC()) + "," + I(variant.ComputeCRC()) + "," + I(Unlit(Color.red).ComputeCRC()) + "]");
        Clear();

        // The variant drawn.
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        Primitive(PrimitiveType.Sphere, new Vector3(-0.7f, 0.5f, 0f), parent);
        Primitive(PrimitiveType.Sphere, new Vector3(0.7f, 0.5f, 0f), variant);
        Capture(MakeCamera(), "material_variant");
        Clear();
    }

    /// <summary>A built-in global or keyword a shader sees, by a Probe/Globals pass.</summary>
    Color GlobalSeen(int pass)
    {
        var rt = Add(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear));
        Graphics.Blit(null, rt, Add(new Material(Shader.Find("Probe/Globals"))), pass);
        return Pixel(rt, 2, 2);
    }

    void Quality()
    {
        Value("quality.globals.shadowColorDefault", C(GlobalSeen(0)));
        Value("quality.settings.subtractiveShadowColor", C(RenderSettings.subtractiveShadowColor));
        RenderSettings.subtractiveShadowColor = new Color(0.3f, 0.5f, 0.7f, 1f);
        var camera = MakeCamera();
        Primitive(PrimitiveType.Quad, new Vector3(0f, 0f, 2f), Add(new Material(Shader.Find("Probe/Globals"))), 0.5f);
        Value("quality.globals.shadowColorBlit", C(GlobalSeen(0)));
        camera.Render();
        Value("quality.globals.shadowColorCamera", C(GlobalSeen(0)));
        RenderSettings.subtractiveShadowColor = new Color(0.42f, 0.478f, 0.627f, 1f);
        bool faceCamera = QualitySettings.billboardsFaceCameraPosition;
        Value("quality.billboardsFaceCameraPosition", B(faceCamera));
        Value("quality.globals.billboardKeyword", C(GlobalSeen(1)));
        QualitySettings.billboardsFaceCameraPosition = !faceCamera;
        Value("quality.globals.billboardKeywordToggled", C(GlobalSeen(1)));
        Value("quality.globals.billboardKeywordEnabled", B(Shader.IsKeywordEnabled("BILLBOARD_FACE_CAMERA_POS")));
        QualitySettings.billboardsFaceCameraPosition = faceCamera;
        Clear();

        // Soft particles: a particle quad crossing the ground.
        Primitive(PrimitiveType.Plane, Vector3.zero, Standard(new Color(0.5f, 0.5f, 0.5f))).transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        var particle = Add(new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended")));
        particle.SetColor("_TintColor", new Color(1f, 0.5f, 0.2f, 0.5f));
        particle.SetFloat("_InvFade", 1f);
        var quad = Primitive(PrimitiveType.Quad, new Vector3(0f, 0.2f, 0f), particle, 2f);
        quad.transform.rotation = Quaternion.Euler(20f, 0f, 0f);
        camera = MakeCamera();
        camera.depthTextureMode = DepthTextureMode.Depth;
        bool soft = QualitySettings.softParticles;
        QualitySettings.softParticles = true;
        Capture(camera, "soft_particles_on");
        QualitySettings.softParticles = false;
        Capture(camera, "soft_particles_off");
        QualitySettings.softParticles = true;
        camera.depthTextureMode = DepthTextureMode.None;
        Capture(camera, "soft_particles_no_depth");
        QualitySettings.softParticles = soft;
        Clear();

        // The texture mipmap limit: stripes whose first mip is gray, drawn texel for texel.
        var stripes = Resources.Load<Texture2D>("Stripes");
        var material = Add(new Material(Shader.Find("Unlit/Texture")));
        material.mainTexture = stripes;
        Primitive(PrimitiveType.Quad, new Vector3(0f, 0f, 1f), material);
        var go = Add(new GameObject("mip camera"));
        camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 0.5f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.blue;
        camera.targetTexture = Target(64, 64);
        int limit = QualitySettings.globalTextureMipmapLimit;
        Value("quality.mipmapLimit", I(limit));
        Value("quality.stripes", "[" + I(stripes.width) + "," + I(stripes.mipmapCount) + "]");
        Capture(camera, "mip_limit_0");
        QualitySettings.globalTextureMipmapLimit = 1;
        Value("quality.stripesLimited", "[" + I(stripes.width) + "," + I(stripes.mipmapCount) + "," + I(stripes.activeMipmapLimit) + "]");
        Capture(camera, "mip_limit_1");
        QualitySettings.globalTextureMipmapLimit = limit;
        Clear();
    }

    /// <summary>Skin weights: a strip skinned to four bones with four weights per vertex, drawn
    /// after the player loop skins it.</summary>
    System.Collections.IEnumerator SkinWeightsCase()
    {
        // Skin weights: a strip skinned to four bones with four weights per vertex.
        var mesh = Add(new Mesh());
        var vertices = new Vector3[18];
        var weights = new BoneWeight[18];
        var triangles = new System.Collections.Generic.List<int>();
        for (int i = 0; i < 9; i++)
        {
            float y = i * 0.25f;
            vertices[2 * i] = new Vector3(-0.3f, y, 0f);
            vertices[2 * i + 1] = new Vector3(0.3f, y, 0f);
            float t = i / 8f;
            var w = new BoneWeight
            {
                boneIndex0 = 0, weight0 = 0.4f,
                boneIndex1 = 1, weight1 = 0.3f,
                boneIndex2 = 2, weight2 = 0.2f,
                boneIndex3 = 3, weight3 = 0.1f,
            };
            weights[2 * i] = weights[2 * i + 1] = w;
            if (i < 8) triangles.AddRange(new[] { 2 * i, 2 * i + 2, 2 * i + 1, 2 * i + 1, 2 * i + 2, 2 * i + 3 });
        }
        mesh.vertices = vertices;
        mesh.SetTriangles(triangles, 0);
        mesh.boneWeights = weights;
        var bones = new Transform[4];
        var bindposes = new Matrix4x4[4];
        var root = Add(new GameObject("skinned"));
        var offsets = new[] { new Vector3(0f, 0f, 0f), new Vector3(1.5f, 0f, 0f), new Vector3(-1.5f, 0.5f, 0f), new Vector3(0f, -1.5f, 0f) };
        for (int i = 0; i < 4; i++)
        {
            bones[i] = new GameObject("bone" + i).transform;
            bones[i].SetParent(root.transform, false);
            bones[i].localPosition = offsets[i];
            bindposes[i] = Matrix4x4.identity;
        }
        mesh.bindposes = bindposes;
        mesh.RecalculateBounds();
        var skinned = root.AddComponent<SkinnedMeshRenderer>();
        skinned.sharedMesh = mesh;
        skinned.bones = bones;
        skinned.sharedMaterial = Unlit(new Color(1f, 0.8f, 0.2f));
        skinned.updateWhenOffscreen = true;
        var camera = MakeCamera();
        camera.transform.position = new Vector3(0f, 1f, -5f);
        camera.transform.rotation = Quaternion.identity;
        var skin = QualitySettings.skinWeights;
        foreach (var setting in new[] { SkinWeights.OneBone, SkinWeights.TwoBones, SkinWeights.FourBones })
        {
            QualitySettings.skinWeights = setting;
            yield return null;
            yield return null;
            Capture(camera, "skin_weights_" + setting);
        }
        skinned.quality = SkinQuality.Bone2;
        yield return null;
        yield return null;
        Capture(camera, "skin_quality_bone2");
        QualitySettings.skinWeights = skin;
        Clear();

    }

    void Raster()
    {
        // Alpha to coverage under the quality level's MSAA, and without MSAA.
        var go = Add(new GameObject("ortho"));
        go.transform.position = new Vector3(0f, 0f, -10f);
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 1f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.targetTexture = Target();
        var quad = Primitive(PrimitiveType.Quad, Vector3.zero, Unlit(new Color(1f, 0.5f, 0.2f), "Probe/AlphaToMask"), 1.8f);
        quad.transform.rotation = Quaternion.Euler(0f, 0f, 15f);
        Value("raster.antiAliasing", I(QualitySettings.antiAliasing));
        Capture(camera, "alpha_to_mask");
        camera.allowMSAA = false;
        Capture(camera, "alpha_to_mask_no_msaa");
        Clear();

        // Depth clamping: two spheres crossing the far plane.
        camera = MakeCamera();
        camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -5f), Quaternion.identity);
        camera.farClipPlane = 5.5f;
        Primitive(PrimitiveType.Sphere, new Vector3(-0.9f, 0f, 0.5f), Unlit(new Color(1f, 0.2f, 0.2f), "Probe/ZClipOff"), 1.6f);
        Primitive(PrimitiveType.Sphere, new Vector3(0.9f, 0f, 0.5f), Unlit(new Color(0.2f, 1f, 0.2f)), 1.6f);
        Capture(camera, "zclip");
        Clear();

        // Wireframe, from GL and from a command buffer around the opaque objects.
        ColorStage();
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        camera = MakeCamera();
        GL.wireframe = true;
        Capture(camera, "wireframe");
        Value("raster.wireframeSet", B(GL.wireframe));
        GL.wireframe = false;
        var on = new CommandBuffer { name = "wire on" };
        on.SetWireframe(true);
        var off = new CommandBuffer { name = "wire off" };
        off.SetWireframe(false);
        camera.AddCommandBuffer(CameraEvent.BeforeForwardOpaque, on);
        camera.AddCommandBuffer(CameraEvent.AfterForwardOpaque, off);
        Capture(camera, "cmd_wireframe");
        Value("raster.wireframeAfterCommandBuffer", B(GL.wireframe));
        camera.RemoveAllCommandBuffers();
        Clear();

        // sRGB writes into an sRGB target of a linear project.
        Value("raster.sRGBWriteDefault", B(GL.sRGBWrite));
        var srgb = Add(new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB));
        var globals = Add(new Material(Shader.Find("Probe/Globals")));
        foreach (var write in new[] { true, false })
        {
            GL.sRGBWrite = write;
            Graphics.Blit(null, srgb, globals, 0);
            var t = Read(srgb);
            Value("raster.sRGBWrite_" + write, C(t.GetPixel(2, 2)));
            DestroyImmediate(t);
        }
        GL.sRGBWrite = true;
        Clear();
    }

    void Streams()
    {
        var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var cubeMesh = source.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(source);
        Mesh ColorStream(System.Func<Vector3, Color> color)
        {
            var stream = Add(new Mesh { name = "stream" });
            var v = cubeMesh.vertices;
            var colors = new Color[v.Length];
            for (int i = 0; i < v.Length; i++) colors[i] = color(v[i]);
            stream.vertices = v;
            stream.colors = colors;
            return stream;
        }
        var material = Add(new Material(Shader.Find("Probe/VertexColor")));
        var plain = Primitive(PrimitiveType.Cube, new Vector3(-1.2f, 0.6f, 0f), material);
        var streamed = Primitive(PrimitiveType.Cube, new Vector3(0f, 0.6f, 0f), material);
        streamed.GetComponent<MeshRenderer>().additionalVertexStreams = ColorStream(p => new Color(p.x + 0.5f, p.y + 0.5f, p.z + 0.5f, 1f));
        var own = Add(Instantiate(cubeMesh));
        var reds = new Color[own.vertexCount];
        for (int i = 0; i < reds.Length; i++) reds[i] = Color.red;
        own.colors = reds;
        var overridden = Primitive(PrimitiveType.Cube, new Vector3(1.2f, 0.6f, 0f), material);
        overridden.GetComponent<MeshFilter>().sharedMesh = own;
        overridden.GetComponent<MeshRenderer>().additionalVertexStreams = ColorStream(p => p.y > 0f ? Color.cyan : Color.blue);
        foreach (var go in new[] { plain, streamed, overridden }) go.transform.rotation = Quaternion.Euler(20f, 30f, 0f);
        Value("streams.roundTrip", N(streamed.GetComponent<MeshRenderer>().additionalVertexStreams));
        Capture(MakeCamera(), "vertex_streams");
        Clear();
    }

    void Components()
    {
        // A billboard asset made in script and one loaded.
        var loaded = Resources.Load<BillboardAsset>("Billboard");
        Value("billboard.loaded", loaded == null ? "null" : "[" + F(loaded.width) + "," + F(loaded.height) + "," + F(loaded.bottom) + "," + I(loaded.imageCount) + "," + I(loaded.vertexCount) + "," + I(loaded.indexCount) + "]");
        var asset = Add(new BillboardAsset { width = 1.2f, height = 1.6f, bottom = -0.1f });
        asset.SetImageTexCoords(new[] { new Vector4(0f, 0f, 1f, 1f) });
        asset.SetVertices(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
        asset.SetIndices(new ushort[] { 0, 1, 2, 0, 2, 3 });
        var billboardMaterial = Add(new Material(Shader.Find("Nature/SpeedTree Billboard")));
        billboardMaterial.mainTexture = Checker();
        asset.material = billboardMaterial;
        Primitive(PrimitiveType.Plane, Vector3.zero, Standard(new Color(0.5f, 0.5f, 0.5f))).transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        var billboard = Add(new GameObject("billboard"));
        billboard.transform.position = new Vector3(0.3f, 0f, 0.2f);
        var renderer = billboard.AddComponent<BillboardRenderer>();
        renderer.billboard = asset;
        Value("billboard.bounds", "[" + V3(renderer.bounds.center) + "," + V3(renderer.bounds.size) + "]");
        Value("billboard.material", N(renderer.sharedMaterial));
        Capture(MakeCamera(), "billboard");
        // The same shape wound the other way round.
        asset.SetIndices(new ushort[] { 0, 2, 1, 0, 3, 2 });
        Capture(MakeCamera(), "billboard_reversed");
        Clear();

        // Occlusion components.
        var holder = Add(new GameObject("occlusion"));
        var area = holder.AddComponent<OcclusionArea>();
        Value("occlusion.area.defaults", "[" + V3(area.center) + "," + V3(area.size) + "]");
        var portal = holder.AddComponent<OcclusionPortal>();
        Value("occlusion.portal.found", B(holder.GetComponent<OcclusionPortal>() != null));
        Value("occlusion.portal.open", B(portal.open));
        portal.open = false;
        Value("occlusion.portal.closed", B(portal.open));
        var scene = SceneObject("Occlusion");
        if (scene != null)
        {
            var a = scene.GetComponent<OcclusionArea>();
            var p = scene.GetComponent<OcclusionPortal>();
            Value("occlusion.scene", "[" + (a == null ? "null" : V3(a.center) + "," + V3(a.size)) + (p == null ? ",null" : "," + B(p.open)) + "]");
        }
        Clear();

        // Static batching: the colored stage under a root, combined, then the root moved.
        var stage = ColorStage();
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        var root = Add(new GameObject("root"));
        foreach (var go in stage) go.transform.SetParent(root.transform, true);
        StaticBatchingUtility.Combine(root);
        var flags = new StringBuilder("[");
        foreach (var go in stage) flags.Append(flags.Length > 1 ? "," : "").Append(B(go.GetComponent<Renderer>().isPartOfStaticBatch));
        Value("static_batching.isPartOfStaticBatch", flags.Append(']').ToString());
        Value("static_batching.mesh", S(stage[1].GetComponent<MeshFilter>().sharedMesh.name));
        var camera = MakeCamera();
        Capture(camera, "static_batching");
        root.transform.position = new Vector3(0.5f, 0f, 0f);
        Capture(camera, "static_batching_root_moved");
        stage[1].transform.position += new Vector3(0f, 0.8f, 0f);
        Capture(camera, "static_batching_child_moved");
        Clear();

        // Which renderers a combine takes: a ground of uniform scale, one sharing a sphere's
        // material, a scaled cube.
        string Batched(params GameObject[] objects)
        {
            var r = Add(new GameObject("batch root"));
            foreach (var go in objects) go.transform.SetParent(r.transform, true);
            StaticBatchingUtility.Combine(r);
            var sb = new StringBuilder("[");
            foreach (var go in objects) sb.Append(sb.Length > 1 ? "," : "").Append(B(go.GetComponent<Renderer>().isPartOfStaticBatch));
            return sb.Append(']').ToString();
        }
        var gray = Standard(new Color(0.6f, 0.6f, 0.6f));
        Value("static_batching.uniformGround", Batched(Primitive(PrimitiveType.Plane, Vector3.zero, gray), Primitive(PrimitiveType.Sphere, Vector3.up, Standard(Color.red))));
        var scaledGround = Primitive(PrimitiveType.Plane, Vector3.zero, gray);
        scaledGround.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        Value("static_batching.sharedMaterial", Batched(scaledGround, Primitive(PrimitiveType.Sphere, Vector3.up, gray)));
        var scaledCube = Primitive(PrimitiveType.Cube, Vector3.zero, gray);
        scaledCube.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        Value("static_batching.scaledCube", Batched(scaledCube, Primitive(PrimitiveType.Sphere, Vector3.up, Standard(Color.green))));
        Value("static_batching.planeAlone", Batched(Primitive(PrimitiveType.Plane, Vector3.zero, gray)));
        Value("static_batching.twoPlanes", Batched(Primitive(PrimitiveType.Plane, Vector3.zero, gray), Primitive(PrimitiveType.Plane, Vector3.right * 12f, gray)));
        Clear();
    }

    /// <summary>Whether the red sphere of the color stage was visible to the last render.</summary>
    static bool SceneSphereVisible()
    {
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (r.transform.position == new Vector3(-1f, 0.5f, 0f)) return r.isVisible;
        return false;
    }

    void Cameras()
    {
        // HDR without image effects: two additive quads (0.8 each) under a multiply (0.5).
        var camera = MakeCamera();
        camera.transform.SetPositionAndRotation(new Vector3(0f, 0f, -5f), Quaternion.identity);
        camera.backgroundColor = Color.black;
        Primitive(PrimitiveType.Quad, new Vector3(-0.3f, 0f, 0.2f), Unlit(new Color(0.8f, 0.6f, 0.3f), "Probe/Add"), 1.5f);
        Primitive(PrimitiveType.Quad, new Vector3(0.3f, 0f, 0.1f), Unlit(new Color(0.8f, 0.6f, 0.3f), "Probe/Add"), 1.5f);
        Primitive(PrimitiveType.Quad, new Vector3(0f, 0.2f, -0.5f), Unlit(new Color(0.5f, 0.5f, 0.5f), "Probe/Mul"), 1.2f);
        Capture(camera, "hdr_on");
        camera.allowHDR = false;
        Capture(camera, "hdr_off");
        camera.allowHDR = true;
        camera.allowMSAA = false;
        Capture(camera, "hdr_on_no_msaa");
        camera.allowMSAA = true;
        camera.targetTexture = Target(Width, Height, RenderTextureFormat.ARGBHalf);
        Capture(camera, "hdr_half_target");
        Clear();

        // A narrow culling matrix.
        ColorStage();
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        camera = MakeCamera();
        var narrow = Matrix4x4.Perspective(12f, 1f, 0.3f, 20f) * camera.worldToCameraMatrix;
        camera.cullingMatrix = narrow;
        Value("culling_matrix.get", B(camera.cullingMatrix == narrow));
        Capture(camera, "culling_matrix");
        Value("culling_matrix.sphereVisible", B(FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Length > 0 && SceneSphereVisible()));
        Value("culling_matrix.getAfterRender", B(camera.cullingMatrix == narrow));
        camera.ResetCullingMatrix();
        Capture(camera, "culling_matrix_reset");
        Clear();

        // CopyFrom.
        var source = MakeCamera();
        source.transparencySortMode = TransparencySortMode.CustomAxis;
        source.transparencySortAxis = new Vector3(1f, 0f, 0f);
        var distances = new float[32];
        distances[3] = 7f;
        source.layerCullDistances = distances;
        source.layerCullSpherical = true;
        source.useOcclusionCulling = false;
        source.allowDynamicResolution = true;
        source.forceIntoRenderTexture = true;
        source.renderingPath = RenderingPath.DeferredShading;
        source.opaqueSortMode = OpaqueSortMode.NoDistanceSort;
        source.depthTextureMode = DepthTextureMode.Depth;
        source.clearStencilAfterLightingPass = true;
        source.eventMask = 5;
        source.cullingMatrix = Matrix4x4.Scale(new Vector3(2f, 2f, 2f));
        var copy = MakeCamera();
        copy.CopyFrom(source);
        Value("copy.transparencySortMode", S(copy.transparencySortMode.ToString()));
        Value("copy.transparencySortAxis", V3(copy.transparencySortAxis) + "");
        Value("copy.layerCullDistances3", F(copy.layerCullDistances[3]));
        Value("copy.layerCullSpherical", B(copy.layerCullSpherical));
        Value("copy.useOcclusionCulling", B(copy.useOcclusionCulling));
        Value("copy.allowDynamicResolution", B(copy.allowDynamicResolution));
        Value("copy.forceIntoRenderTexture", B(copy.forceIntoRenderTexture));
        Value("copy.renderingPath", S(copy.renderingPath.ToString()));
        Value("copy.opaqueSortMode", S(copy.opaqueSortMode.ToString()));
        Value("copy.depthTextureMode", S(copy.depthTextureMode.ToString()));
        Value("copy.clearStencilAfterLightingPass", B(copy.clearStencilAfterLightingPass));
        Value("copy.eventMask", I(copy.eventMask));
        Value("copy.cullingMatrix", M(copy.cullingMatrix));
        Clear();

        var scene = SceneObject("SceneCamera");
        if (scene != null)
        {
            var c = scene.GetComponent<Camera>();
            Value("scene_camera.useOcclusionCulling", B(c.useOcclusionCulling));
            Value("scene_camera.allowDynamicResolution", B(c.allowDynamicResolution));
            Value("scene_camera.forceIntoRenderTexture", B(c.forceIntoRenderTexture));
            Value("scene_camera.skybox", B(scene.GetComponent<Skybox>() != null));
            Value("scene_camera.components", I(scene.GetComponents<Component>().Length));
        }
    }
}
