using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

public sealed partial class RenderApiProbe
{
    public static Action<GameObject> selectGizmoObject;

    void WriteMesh(Mesh mesh, string key, bool structured, bool commands = false)
    {
        var compute = Resources.Load<ComputeShader>("MeshWrite");
        int kernel = compute.FindKernel(structured ? "Structured" : "Raw");
        using (var vertices = mesh.GetVertexBuffer(0))
        using (var indices = mesh.GetIndexBuffer())
        {
            if (commands)
            {
                using (var cb = new CommandBuffer())
                {
                    cb.SetComputeBufferParam(compute, kernel, structured ? "StructuredVertices" : "RawVertices", vertices);
                    cb.SetComputeBufferParam(compute, kernel, "RawIndices", indices);
                    cb.SetComputeIntParam(compute, "Stride", mesh.GetVertexBufferStride(0));
                    cb.SetComputeIntParam(compute, "Count", mesh.vertexCount);
                    cb.DispatchCompute(compute, kernel, 1, 1, 1);
                    Graphics.ExecuteCommandBuffer(cb);
                }
            }
            else
            {
                compute.SetBuffer(kernel, structured ? "StructuredVertices" : "RawVertices", vertices);
                compute.SetBuffer(kernel, "RawIndices", indices);
                compute.SetInt("Stride", mesh.GetVertexBufferStride(0));
                compute.SetInt("Count", mesh.vertexCount);
                compute.Dispatch(kernel, 1, 1, 1);
            }
            Buffer(key + ".vertices", vertices);
            Buffer(key + ".indices", indices);
        }
    }

    void PackedCases()
    {
        var mesh = new Mesh();
        mesh.SetVertexBufferParams(4,
            new VertexAttributeDescriptor(VertexAttribute.Position,VertexAttributeFormat.Float16,4,0),
            new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.UNorm8,4,1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0,VertexAttributeFormat.Float16,4,2),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord5,VertexAttributeFormat.SNorm16,4,3));
        mesh.SetVertexBufferData(new ushort[] {0xb800,0xb800,0,0x3c00, 0x3800,0xb800,0,0x3c00, 0x3800,0x3800,0,0x3c00, 0xb800,0x3800,0,0x3c00},0,0,16,0);
        mesh.SetVertexBufferData(new uint[] {0xffffffff,0xffffffff,0xffffffff,0xffffffff},0,0,4,1);
        mesh.SetVertexBufferData(new ushort[] {0x3800,0x3800,0,0,0x3800,0x3800,0,0,0x3800,0x3800,0,0,0x3800,0x3800,0,0},0,0,16,2);
        mesh.SetVertexBufferData(new short[] {16384,0,0,0,16384,0,0,0,16384,0,0,0,16384,0,0,0},0,0,16,3);
        mesh.SetIndices(new[] {0,1,2,3},MeshTopology.Quads,0);
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw; mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        var material = new Material(Resources.Load<Shader>("PackedRead"));
        var go = new GameObject("packed GPU mesh"); go.AddComponent<MeshFilter>().sharedMesh=mesh; go.AddComponent<MeshRenderer>().sharedMaterial=material;
        Image("packed.mesh.before");
        Try("packed.mesh.write",()=>
        {
            var compute=Resources.Load<ComputeShader>("MeshWrite"); int kernel=compute.FindKernel("Packed");
            for(int stream=0;stream<4;stream++)
            {
                using(var b=mesh.GetVertexBuffer(stream)) compute.SetBuffer(kernel,new[] {"RawVertices","PackedColors","PackedUvs","PackedExtra"}[stream],b);
            }
            using(var b=mesh.GetIndexBuffer())compute.SetBuffer(kernel,"RawIndices",b);
            compute.Dispatch(kernel,1,1,1);
        });
        Image("packed.mesh.after");
        using(var b=mesh.GetIndexBuffer())Buffer("quadOrder.current",b);
        mesh.SetVertexBufferData(new uint[] {0xffff0000,0xffff0000,0xffff0000,0xffff0000},0,0,4,1);
        Image("packed.mesh.colorOnly");
        DestroyImmediate(go); DestroyImmediate(mesh); DestroyImmediate(material);
    }

    IEnumerator SkinNormalCases()
    {
        foreach (bool scaled in new[] {false,true})
        {
            var go=new GameObject("skinned lighting buffer control");
            go.transform.localScale=scaled ? new Vector3(2,3,4) : Vector3.one;
            go.transform.localRotation=Quaternion.Euler(0,0,17);
            var mesh=Make();mesh.vertices=new[] {Vertices[0]*.35f,Vertices[1]*.35f,Vertices[2]*.35f};mesh.triangles=new[] {0,2,1};
            var normal=new Vector3(1,1,-1).normalized;
            mesh.normals=new[] {normal,normal,normal};
            mesh.boneWeights=new[] {new BoneWeight {weight0=1},new BoneWeight {weight0=1},new BoneWeight {weight0=1}};
            mesh.bindposes=new[] {Matrix4x4.identity};
            var skin=go.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.bones=new[] {go.transform};skin.updateWhenOffscreen=true;
            var material=new Material(Resources.Load<Shader>("SkinNormal"));skin.sharedMaterial=material;
            var baked=new Mesh();string key="skinNormal."+scaled;
            yield return null;Image(key+".before");
            foreach(bool useScale in new[] {false,true}) {skin.BakeMesh(baked,useScale);Value(key+".bakeBefore."+useScale,V(baked.normals[0]));}
            using(var buffer=skin.GetVertexBuffer()) Buffer(key+".buffer",buffer,false);
            Image(key+".after");
            foreach(bool useScale in new[] {false,true}) {skin.BakeMesh(baked,useScale);Value(key+".bakeAfter."+useScale,V(baked.normals[0]));}
            yield return null;Image(key+".next");
            DestroyImmediate(go);DestroyImmediate(mesh);DestroyImmediate(material);DestroyImmediate(baked);
        }
    }

    void PositionlessStreamCases()
    {
        var mesh=Make();mesh.colors32=new[] {new Color32(255,255,255,255),new Color32(255,255,255,255),new Color32(255,255,255,255)};
        mesh.uv=new[] {new Vector2(.25f,.5f),new Vector2(.25f,.5f),new Vector2(.25f,.5f)};mesh.vertexBufferTarget|=GraphicsBuffer.Target.Raw;
        var extra=new Mesh();extra.SetVertexBufferParams(3,new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.UNorm8,4,0),new VertexAttributeDescriptor(VertexAttribute.TexCoord2,VertexAttributeFormat.Float32,2,1));
        extra.SetVertexBufferData(new uint[] {0xffffffff,0xffffffff,0xffffffff},0,0,3,0);
        extra.SetVertexBufferData(new[] {new Vector2(.2f,.8f),new Vector2(.2f,.8f),new Vector2(.2f,.8f)},0,0,3,1);extra.vertexBufferTarget|=GraphicsBuffer.Target.Raw;
        Bool("streamOnly.position",extra.HasVertexAttribute(VertexAttribute.Position));
        var go=new GameObject("positionless override streams");go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.additionalVertexStreams=extra;
        var material=new Material(Resources.Load<Shader>("StreamRead"));renderer.sharedMaterial=material;Image("streamOnly.before");
        var compute=Resources.Load<ComputeShader>("MeshWrite");int uv=compute.FindKernel("SourceUV"),position=compute.FindKernel("Skin");
        using(var b=mesh.GetVertexBuffer(0))
        {
            compute.SetInt("Count",3);compute.SetInt("Stride",b.stride);compute.SetBuffer(position,"RawVertices",b);compute.Dispatch(position,1,1,1);
            compute.SetBuffer(uv,"RawVertices",b);compute.SetInt("ColorOffset",mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord0));compute.Dispatch(uv,1,1,1);
        }
        Image("streamOnly.baseGpu");
        using(var b=extra.GetVertexBuffer(1))
        {
            Buffer("streamOnly.uv",b,false);compute.SetInt("Stride",b.stride);compute.SetBuffer(uv,"RawVertices",b);compute.SetInt("ColorOffset",0);compute.Dispatch(uv,1,1,1);
        }
        Image("streamOnly.overrideGpu");
        extra.vertices=new[] {Vertices[0]+Vector3.left*.1f,Vertices[1]+Vector3.left*.1f,Vertices[2]+Vector3.left*.1f};
        Bool("streamOnly.addedPosition",extra.HasVertexAttribute(VertexAttribute.Position));Image("streamOnly.addedPosition");
        DestroyImmediate(go);DestroyImmediate(mesh);DestroyImmediate(extra);DestroyImmediate(material);
    }

    void AdditionalStreamCases()
    {
        var mesh=Make();mesh.colors32=new[] {new Color32(255,255,255,255),new Color32(255,255,255,255),new Color32(255,255,255,255)};
        var extra=Make();extra.vertexBufferTarget|=GraphicsBuffer.Target.Raw;
        extra.colors32=new[] {new Color32(0,255,0,255),new Color32(0,255,0,255),new Color32(0,255,0,255)};
        extra.vertices=new[] {Vertices[0]+Vector3.right*.2f,Vertices[1]+Vector3.right*.2f,Vertices[2]+Vector3.right*.2f};
        var go=new GameObject("additional GPU streams");go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();renderer.additionalVertexStreams=extra;
        var material=new Material(Resources.Load<Shader>("PackedRead"));material.SetFloat("_UseUvs",0);renderer.sharedMaterial=material;
        Image("streamExtra.before");
        using(var b=extra.GetVertexBuffer(0))
        {
            var compute=Resources.Load<ComputeShader>("MeshWrite");int color=compute.FindKernel("SourceColor"),position=compute.FindKernel("Skin");
            compute.SetBuffer(color,"RawVertices",b);compute.SetInt("Stride",b.stride);compute.SetInt("Count",extra.vertexCount);compute.SetInt("ColorOffset",extra.GetVertexAttributeOffset(VertexAttribute.Color));compute.Dispatch(color,1,1,1);
            compute.SetBuffer(position,"RawVertices",b);compute.Dispatch(position,1,1,1);
        }
        Image("streamExtra.gpu");Value("streamExtra.cpu",V(extra.vertices[0]));
        extra.SetVertices(new[] {Vertices[0]+Vector3.right*.2f,Vertices[1]+Vector3.right*.2f,Vertices[2]+Vector3.right*.2f});
        Image("streamExtra.cpuEdit");
        DestroyImmediate(go);DestroyImmediate(mesh);DestroyImmediate(extra);DestroyImmediate(material);
    }

    IEnumerator SkinExtraCases()
    {
        var mesh=Make();mesh.triangles=new[] {0,2,1};mesh.colors32=new[] {new Color32(0,255,0,255),new Color32(0,255,0,255),new Color32(0,255,0,255)};
        mesh.boneWeights=new[] {new BoneWeight {weight0=1},new BoneWeight {weight0=1},new BoneWeight {weight0=1}};mesh.bindposes=new[] {Matrix4x4.identity};
        mesh.vertexBufferTarget|=GraphicsBuffer.Target.Raw;mesh.indexBufferTarget|=GraphicsBuffer.Target.Raw;
        var go=new GameObject("skin shared channels");var skin=go.AddComponent<SkinnedMeshRenderer>();skin.sharedMesh=mesh;skin.bones=new[] {go.transform};skin.updateWhenOffscreen=true;
        var material=new Material(Resources.Load<Shader>("PackedRead"));material.SetFloat("_UseUvs",0);skin.sharedMaterial=material;
        yield return null;Image("skinExtra.before");
        using(var b=mesh.GetVertexBuffer(0))
        {
            var compute=Resources.Load<ComputeShader>("MeshWrite");int kernel=compute.FindKernel("SourceColor");compute.SetBuffer(kernel,"RawVertices",b);
            compute.SetInt("Stride",b.stride);compute.SetInt("Count",mesh.vertexCount);compute.SetInt("ColorOffset",mesh.GetVertexAttributeOffset(VertexAttribute.Color));compute.Dispatch(kernel,1,1,1);
        }
        Image("skinExtra.color");yield return null;Image("skinExtra.nextColor");
        Value("skinExtra.cpuColor",C(mesh.colors[0]));
        using(var b=mesh.GetIndexBuffer())b.SetData(new ushort[] {0,0,0,0});
        Image("skinExtra.index");yield return null;Image("skinExtra.nextIndex");
        DestroyImmediate(go);DestroyImmediate(mesh);DestroyImmediate(material);
    }

    void ColorArrayCases()
    {
        var mesh=Make();var color=new Color(2,.3f,.7f,1);mesh.colors=new[] {color,color,color};
        var material=new Material(Resources.Load<Shader>("PackedRead"));material.SetFloat("_UseUvs",0);
        var go=new GameObject("float color arrays");go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
        Value("colorArray.float",C(mesh.colors[0]));Value("colorArray.float.format",((int)mesh.GetVertexAttributeFormat(VertexAttribute.Color)).ToString());
        using(var b=mesh.GetVertexBuffer(0))Buffer("colorArray.float.buffer",b);
        Image("colorArray.float");mesh.SetVertices(Vertices);Image("colorArray.positions");
        mesh.colors32=new[] {new Color32(10,20,30,40),new Color32(10,20,30,40),new Color32(10,20,30,40)};
        Value("colorArray.byte",C(mesh.colors[0]));Value("colorArray.byte.format",((int)mesh.GetVertexAttributeFormat(VertexAttribute.Color)).ToString());
        using(var b=mesh.GetVertexBuffer(0))Buffer("colorArray.byte.buffer",b);
        Image("colorArray.byte");DestroyImmediate(go);DestroyImmediate(mesh);DestroyImmediate(material);
    }

    void QuadLayoutCases()
    {
        foreach(bool raw in new[] {false,true})
        foreach(bool wide in new[] {false,true})
        {
            var m=new Mesh();m.indexFormat=wide?IndexFormat.UInt32:IndexFormat.UInt16;
            m.vertices=new[] {new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)};
            m.subMeshCount=2;m.SetIndices(new[] {0,1,2,3},MeshTopology.Quads,0);m.SetIndices(new[] {0,2,3},MeshTopology.Triangles,1);
            if(raw)m.indexBufferTarget|=GraphicsBuffer.Target.Raw;
            ClearIndexPadding(m);
            string key="quadLayout.mixed."+raw+"."+wide;
            using(var b=m.GetIndexBuffer())Buffer(key,b);
            DestroyImmediate(m);
        }
        var mesh=new Mesh {vertices=new[] {new Vector3(-.5f,-.5f,0),new Vector3(.5f,-.5f,0),new Vector3(.5f,.5f,0),new Vector3(-.5f,.5f,0)}};
        mesh.SetIndices(new[] {0,1,2,3},MeshTopology.Quads,0);mesh.indexBufferTarget|=GraphicsBuffer.Target.Raw;
        var go=new GameObject("quad ranges");go.AddComponent<MeshFilter>().sharedMesh=mesh;
        var material=new Material(Resources.Load<Shader>("PackedRead"));material.SetFloat("_UseUvs",0);go.AddComponent<MeshRenderer>().sharedMaterial=material;
        using(var b=mesh.GetIndexBuffer())
        {
            var data=new ushort[b.count];b.GetData(data);
            for(int n=0;n<4;n++)data[n]=0;b.SetData(data);Image("quadLayout.logical");
            for(int n=4;n<data.Length;n++)data[n]=0;b.SetData(data);Image("quadLayout.physical");
            mesh.UploadMeshData(false);Image("quadLayout.upload");
            mesh.SetIndices(new[] {0,1,2,3},MeshTopology.Quads,0);Image("quadLayout.cpu");
        }
        DestroyImmediate(go);DestroyImmediate(mesh);DestroyImmediate(material);
    }

    IEnumerator SphereMoreCases()
    {
        var go=new GameObject("transformed gizmo sphere");var script=go.AddComponent<RenderApiSphereControl>();script.enabled=false;
        foreach(bool hdr in new[] {false,true})
        {
            camera.allowHDR=hdr;setGameGizmos?.Invoke(true);script.drew=false;
            yield return null;yield return new WaitForEndOfFrame();
            string key="sphereMore."+hdr;
            var image=ScreenCapture.CaptureScreenshotAsTexture();SaveImage(key,image);DestroyImmediate(image);Bool(key+".callback",script.drew);
        }
        camera.allowHDR=false;setGameGizmos?.Invoke(false);DestroyImmediate(go);
    }

    IEnumerator SkinSourceCases()
    {
        var go=new GameObject("skin source");var skin=go.AddComponent<SkinnedMeshRenderer>();
        var mesh=Make();mesh.triangles=new[] {0,2,1};
        mesh.boneWeights=new[] {new BoneWeight {weight0=1},new BoneWeight {weight0=1},new BoneWeight {weight0=1}};
        mesh.bindposes=new[] {Matrix4x4.identity};mesh.vertexBufferTarget|=GraphicsBuffer.Target.Raw;
        skin.sharedMesh=mesh;skin.bones=new[] {go.transform};skin.updateWhenOffscreen=true;skin.vertexBufferTarget|=GraphicsBuffer.Target.Raw;
        var material=new Material(Shader.Find("Unlit/Color"));material.color=Color.green;skin.sharedMaterial=material;
        yield return null;Image("skinSource.before");
        Try("skinSource.input.write",()=>
        {
            using(var b=mesh.GetVertexBuffer(0))
            {
                Buffer("skinSource.input",b,false);
                Value("skinSource.streams",mesh.vertexBufferCount.ToString());
                var layout=mesh.GetVertexAttributes();var entries=new string[layout.Length];
                for(int a=0;a<layout.Length;a++)entries[a]="["+(int)layout[a].attribute+","+(int)layout[a].format+","+layout[a].dimension+","+layout[a].stream+"]";
                Value("skinSource.layout","["+string.Join(",",entries)+"]");
                Bool("skinSource.weights.present",mesh.HasVertexAttribute(VertexAttribute.BlendWeight));
                Value("skinSource.weights.stream",mesh.GetVertexAttributeStream(VertexAttribute.BlendWeight).ToString());
                var compute=Resources.Load<ComputeShader>("MeshWrite");int kernel=compute.FindKernel("Skin");
                compute.SetBuffer(kernel,"RawVertices",b);compute.SetInt("Stride",b.stride);compute.SetInt("Count",3);compute.Dispatch(kernel,1,1,1);
            }
        });
        Image("skinSource.sameFrame");yield return null;Image("skinSource.nextFrame");
        Try("skinSource.output",()=>
        {
            using(var b=skin.GetVertexBuffer())
            {
                Buffer("skinSource.current",b,false);
                if(b==null)return;
                var values=new float[b.stride/4];b.GetData(values);Value("skinSource.position",V(new Vector3(values[0],values[1],values[2])));
            }
        });
        DestroyImmediate(go);DestroyImmediate(mesh);DestroyImmediate(material);
    }

    void HdrCases()
    {
        var mesh=new Mesh();
        mesh.SetVertexBufferParams(3,new VertexAttributeDescriptor(VertexAttribute.Position),new VertexAttributeDescriptor(VertexAttribute.Color,VertexAttributeFormat.Float32,4,1));
        mesh.SetVertexBufferData(Vertices,0,0,3,0);
        mesh.SetVertexBufferData(new[] {new Vector4(2,.3f,.7f,1),new Vector4(2,.3f,.7f,1),new Vector4(2,.3f,.7f,1)},0,0,3,1);
        mesh.SetIndices(new[] {0,1,2},MeshTopology.Triangles,0);mesh.vertexBufferTarget|=GraphicsBuffer.Target.Raw;
        var material=new Material(Resources.Load<Shader>("PackedRead"));material.SetFloat("_UseUvs",0);
        var go=new GameObject("HDR GPU mesh");go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterial=material;
        Image("hdr.mesh.before");
        Try("hdr.mesh.write",()=>
        {
            var compute=Resources.Load<ComputeShader>("MeshWrite");int kernel=compute.FindKernel("PackedHdr");
            using(var b=mesh.GetVertexBuffer(1))compute.SetBuffer(kernel,"PackedColors",b);
            compute.Dispatch(kernel,1,1,1);
        });
        Image("hdr.mesh.after");
        DestroyImmediate(go);DestroyImmediate(mesh);DestroyImmediate(material);
    }

    IEnumerator ExtendedCases()
    {
        var mesh = Make(); mesh.triangles = new[] { 0, 2, 1 };
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw | GraphicsBuffer.Target.CopyDestination;
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        var go = new GameObject("copy mesh"); go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var material = new Material(Shader.Find("Unlit/Color")); material.color = Color.green;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        var shifted = new Vector3[3];
        for (int n = 0; n < 3; n++) shifted[n] = Vertices[n] + Vector3.right * .5f;
        using (var buffer = mesh.GetVertexBuffer(0))
        using (var indices = mesh.GetIndexBuffer())
        using (var source = new GraphicsBuffer(GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource, 3, 12))
        {
            Try("extended.mesh.shortRead", () => buffer.GetData(new float[1]));
            Try("extended.mesh.setData", () => buffer.SetData(shifted)); Image("extended.mesh.setData");
            Value("extended.mesh.cpu", V(mesh.vertices[0]));
            source.SetData(Vertices);
            Try("extended.mesh.copy", () => Graphics.CopyBuffer(source, buffer)); Image("extended.mesh.copy");
            using (var cb = new CommandBuffer())
            {
                cb.SetBufferData(buffer, shifted);
                Try("extended.mesh.commandSet", () => Graphics.ExecuteCommandBuffer(cb));
            }
            Image("extended.mesh.commandSet");
            Try("extended.mesh.indexSet", () => indices.SetData(new ushort[] { 0, 1, 2, 0 }));
            Image("extended.mesh.indexSet");
            mesh.SetVertices(Vertices); Image("extended.mesh.vertexOnly");
            mesh.triangles = new[] { 0, 2, 1 }; Image("extended.mesh.indexOnly");
            // Same command stream: no intervening readback may force the dispatch to complete.
            var compute = Resources.Load<ComputeShader>("MeshWrite"); int kernel = compute.FindKernel("Skin");
            var rt = new RenderTexture(96,64,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear); rt.Create();
            using (var cb = new CommandBuffer())
            {
                cb.SetComputeBufferParam(compute,kernel,"RawVertices",buffer);
                cb.SetComputeIntParam(compute,"Stride",12); cb.SetComputeIntParam(compute,"Count",3);
                cb.DispatchCompute(compute,kernel,1,1,1);
                cb.SetRenderTarget(rt); cb.ClearRenderTarget(true,true,Color.black);
                cb.SetViewProjectionMatrices(camera.worldToCameraMatrix,camera.projectionMatrix);
                cb.DrawMesh(mesh,Matrix4x4.identity,material);
                Graphics.ExecuteCommandBuffer(cb);
            }
            var previous = RenderTexture.active; RenderTexture.active = rt;
            var image = new Texture2D(96,64,TextureFormat.RGBAFloat,false,true);
            image.ReadPixels(new Rect(0,0,96,64),0,0); image.Apply(); RenderTexture.active = previous;
            SaveImage("extended.mesh.dispatchDraw",image); DestroyImmediate(image); rt.Release(); DestroyImmediate(rt);
        }
        DestroyImmediate(go); DestroyImmediate(mesh); DestroyImmediate(material);
        var skinObject = new GameObject("offset skin"); var skin = skinObject.AddComponent<SkinnedMeshRenderer>();
        Try("extended.skin.empty",()=> { using(var b=skin.GetVertexBuffer()) Bool("extended.skin.empty.null",b==null); });
        mesh = Make(); mesh.normals = new[] { Vector3.forward,Vector3.forward,Vector3.forward };
        mesh.boneWeights = new[] { new BoneWeight { weight0=1 }, new BoneWeight { weight0=1 }, new BoneWeight { weight0=1 } };
        mesh.bindposes = new[] { Matrix4x4.identity };
        var bone = new GameObject("offset bone"); bone.transform.SetParent(skinObject.transform,false); bone.transform.localPosition = Vector3.right * .25f;
        skinObject.transform.position = Vector3.right * .7f;
        skin.sharedMesh=mesh; skin.bones=new[] {bone.transform}; skin.updateWhenOffscreen=true; skin.vertexBufferTarget|=GraphicsBuffer.Target.Raw;
        material = new Material(Shader.Find("Unlit/Color")); material.color=Color.green; skin.sharedMaterial=material;
        yield return null; Image("extended.skin.before");
        Try("extended.skin.offset",()=>
        {
            using(var b=skin.GetVertexBuffer())
            {
                Buffer("extended.skin.buffer",b,false);
                if(b!=null) { var values=new float[b.stride/4];b.GetData(values);Value("extended.skin.position",V(new Vector3(values[0],values[1],values[2]))); }
            }
        });
        DestroyImmediate(bone); DestroyImmediate(skinObject); DestroyImmediate(mesh); DestroyImmediate(material);
    }

    IEnumerator FollowupCases()
    {
        // Every new record and frame uses its own prefix; earlier controls remain unchanged.
        foreach (float spacing in new[] { 0f, -.25f, float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            string key = "followup.pixel." + F(spacing).Replace("\"", "");
            var sprite = new GameObject("nonfinite sprite").AddComponent<SpriteRenderer>();
            var texture = Texture2D.whiteTexture;
            sprite.sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f,.5f), texture.width);
            sprite.transform.position = new Vector3(.17f, -.19f, 0);
            PixelPerfectRendering.pixelSnapSpacing = spacing;
            Image(key);
            Value(key + ".position", V(sprite.transform.position));
            DestroyImmediate(sprite.gameObject);
        }
        PixelPerfectRendering.pixelSnapSpacing = 0;
        foreach (bool structured in new[] { false, true })
        foreach (bool unreadable in new[] { false, true })
        {
            string key = "followup.mesh." + structured + "." + unreadable;
            var mesh = Make();
            mesh.triangles = new[] { 0, 2, 1 };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.triangles = new[] { 0, 2, 1 };
            mesh.vertexBufferTarget |= structured ? GraphicsBuffer.Target.Structured : GraphicsBuffer.Target.Raw;
            mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
            mesh.UploadMeshData(unreadable);
            var go = new GameObject("GPU mesh");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Unlit/Color"));
            material.color = Color.green;
            renderer.sharedMaterial = material;
            Image(key + ".before");
            Try(key + ".write", () => WriteMesh(mesh, key, structured));
            Image(key + ".after");
            Bool(key + ".readable", mesh.isReadable);
            if (!unreadable)
            {
                Value(key + ".cpu", V(mesh.vertices[0]));
                mesh.UploadMeshData(false);
                Image(key + ".upload");
                mesh.SetVertices(Vertices);
                Image(key + ".setVertices");
                Try(key + ".command", () => WriteMesh(mesh, key + ".command", structured, true));
                Image(key + ".commandDraw");
                using (var old = mesh.GetVertexBuffer(0))
                {
                    mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
                    mesh.UploadMeshData(false);
                    Bool(key + ".invalidated", old.IsValid());
                    using (var current = mesh.GetVertexBuffer(0))
                        Bool(key + ".replaced", current.GetNativeBufferPtr() != old.GetNativeBufferPtr());
                    Image(key + ".layout");
                }
            }
            DestroyImmediate(go); DestroyImmediate(material); DestroyImmediate(mesh);
        }
        var skinObject = new GameObject("skin buffer");
        var skin = skinObject.AddComponent<SkinnedMeshRenderer>();
        var skinMesh = Make();
        skinMesh.triangles = new[] { 0, 2, 1 };
        skinMesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward };
        skinMesh.boneWeights = new[] { new BoneWeight { boneIndex0=0, weight0=1 }, new BoneWeight { boneIndex0=0, weight0=1 }, new BoneWeight { boneIndex0=0, weight0=1 } };
        skinMesh.bindposes = new[] { Matrix4x4.identity };
        skin.sharedMesh = skinMesh; skin.bones = new[] { skinObject.transform }; skin.rootBone = skinObject.transform;
        skin.updateWhenOffscreen = true;
        var skinMaterial = new Material(Shader.Find("Unlit/Color")); skinMaterial.color = Color.green;
        skin.sharedMaterial = skinMaterial;
        Value("followup.skin.defaultTarget", ((int)skin.vertexBufferTarget).ToString());
        skin.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        Value("followup.skin.target", ((int)skin.vertexBufferTarget).ToString());
        yield return null;
        Image("followup.skin.before");
        Try("followup.skin.buffer", () =>
        {
            using (var buffer = skin.GetVertexBuffer())
            {
                Buffer("followup.skin.current", buffer, false);
                if (buffer == null) return;
                var first = new float[buffer.stride / 4]; buffer.GetData(first);
                Value("followup.skin.first", V(new Vector3(first[0], first[1], first[2])));
                var compute = Resources.Load<ComputeShader>("MeshWrite");
                int kernel = compute.FindKernel("Skin");
                compute.SetBuffer(kernel, "RawVertices", buffer);
                compute.SetInt("Stride", buffer.stride); compute.SetInt("Count", skinMesh.vertexCount);
                compute.Dispatch(kernel, 1, 1, 1);
                buffer.GetData(first); Value("followup.skin.written", V(new Vector3(first[0], first[1], first[2])));
            }
        });
        Image("followup.skin.after");
        yield return null;
        Image("followup.skin.nextFrame");
        DestroyImmediate(skinObject); DestroyImmediate(skinMesh); DestroyImmediate(skinMaterial);
        var gizmoObject = new GameObject("selected gizmos");
        var gizmo = gizmoObject.AddComponent<RenderApiGizmoControls>();
        foreach (bool enabled in new[] { false, true })
        foreach (bool selected in new[] { false, true })
        {
            selectGizmoObject?.Invoke(selected ? gizmoObject : null);
            setGameGizmos?.Invoke(enabled); gizmo.drew = gizmo.drewSelected = false;
            yield return null; yield return new WaitForEndOfFrame();
            string key = "followup.gizmos." + enabled + "." + selected;
            var screen = ScreenCapture.CaptureScreenshotAsTexture(); SaveImage(key, screen); DestroyImmediate(screen);
            Bool(key + ".callback", gizmo.drew); Bool(key + ".selectedCallback", gizmo.drewSelected);
        }
        selectGizmoObject?.Invoke(null); setGameGizmos?.Invoke(false); DestroyImmediate(gizmoObject);
    }
}

public sealed partial class RenderApiProbe
{
    /// <summary>A skin buffer's first position and the third's position and normal (null, or
    /// its count, stride and target).</summary>
    void SkinRead(string key, GraphicsBuffer b, bool third = false)
    {
        Bool(key + ".null", b == null);
        if (b == null) return;
        Value(key + ".info", "[" + b.count + "," + b.stride + "," + (int)b.target + "]");
        var f = new float[b.count * b.stride / 4]; b.GetData(f);
        Value(key + ".v0", V(new Vector3(f[0], f[1], f[2])));
        int s = b.stride / 4;
        if (third) Value(key + ".v2", V(new Vector3(f[2 * s], f[2 * s + 1], f[2 * s + 2])));
    }

    /// <summary>GetPreviousVertexBuffer and GetVertexBuffer of a renderer whose bone moves each
    /// frame, with and without skinnedMotionVectors, under a camera drawing motion vectors or not;
    /// then a cloth's buffer against its particles.</summary>
    IEnumerator PreviousSkinCases()
    {
        foreach (bool motion in new[] { true, false })
        foreach (bool cameraMotion in new[] { false, true })
        {
            string p = "prevskin." + motion + "." + cameraMotion;
            camera.depthTextureMode = cameraMotion ? DepthTextureMode.Depth | DepthTextureMode.MotionVectors : DepthTextureMode.None;
            var go = new GameObject("previous skin");
            var bone = new GameObject("bone").transform; bone.SetParent(go.transform, false);
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = Make();
            mesh.boneWeights = new[] { new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 } };
            mesh.bindposes = new[] { Matrix4x4.identity };
            skin.sharedMesh = mesh; skin.bones = new[] { bone }; skin.rootBone = go.transform; skin.updateWhenOffscreen = true;
            skin.skinnedMotionVectors = motion;
            var material = new Material(Shader.Find("Unlit/Color"));
            skin.sharedMaterial = material;
            skin.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
            void Both(string k)
            {
                Try(k, () =>
                {
                    using (var b = skin.GetPreviousVertexBuffer()) SkinRead(k + ".previous", b);
                    using (var c = skin.GetVertexBuffer()) SkinRead(k + ".current", c);
                });
            }
            Both(p + ".created");
            for (int f = 1; f <= 4; f++)
            {
                bone.localPosition = new Vector3(0.1f * f, 0, 0);
                yield return null;
                Both(p + ".frame" + f);
            }
            yield return null;
            Both(p + ".still");
            skin.enabled = false; bone.localPosition = Vector3.right; yield return null;
            Both(p + ".disabled");
            DestroyImmediate(go); DestroyImmediate(mesh); DestroyImmediate(material);
        }
        camera.depthTextureMode = DepthTextureMode.None;
        var clothObject = new GameObject("cloth skin");
        var clothSkin = clothObject.AddComponent<SkinnedMeshRenderer>();
        var clothMesh = new Mesh { vertices = new[] { new Vector3(-.5f, 0, 0), new Vector3(.5f, 0, 0), new Vector3(-.5f, -1, 0), new Vector3(.5f, -1, 0) }, triangles = new[] { 0, 1, 2, 1, 3, 2 } };
        clothMesh.RecalculateNormals();
        var clothMaterial = new Material(Shader.Find("Unlit/Color"));
        clothSkin.sharedMesh = clothMesh; clothSkin.sharedMaterial = clothMaterial;
        clothSkin.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
        var cloth = clothObject.AddComponent<Cloth>();
        cloth.coefficients = new[] { new ClothSkinningCoefficient { maxDistance = 0 }, new ClothSkinningCoefficient { maxDistance = 0 },
            new ClothSkinningCoefficient { maxDistance = 10 }, new ClothSkinningCoefficient { maxDistance = 10 } };
        // The first two frames' buffers are not recorded: what they hold varies from run to run.
        yield return null;
        yield return null;
        for (int f = 2; f <= 4; f++)
        {
            yield return null;
            string k = "prevskin.cloth.frame" + f;
            Try(k, () =>
            {
                using (var c = clothSkin.GetVertexBuffer())
                using (var b = clothSkin.GetPreviousVertexBuffer())
                {
                    Bool(k + ".null", c == null);
                    if (c == null) return;
                    Value(k + ".info", "[" + c.count + "," + c.stride + "," + (int)c.target + "]");
                    var current = new float[c.count * c.stride / 4]; c.GetData(current);
                    int s = c.stride / 4;
                    bool particles = true;
                    for (int v = 0; v < c.count; v++)
                        particles &= cloth.vertices[v] == new Vector3(current[v * s], current[v * s + 1], current[v * s + 2])
                            && cloth.normals[v] == new Vector3(current[v * s + 3], current[v * s + 4], current[v * s + 5]);
                    Bool(k + ".particles", particles);
                    var before = new float[b.count * b.stride / 4]; b.GetData(before);
                    Bool(k + ".previousSame", System.Linq.Enumerable.SequenceEqual(before, current));
                }
            });
        }
        cloth.enabled = false; yield return null;
        Try("prevskin.cloth.disabled", () => { using (var c = clothSkin.GetVertexBuffer()) SkinRead("prevskin.cloth.disabled.current", c, true); });
        DestroyImmediate(clothObject); DestroyImmediate(clothMesh); DestroyImmediate(clothMaterial);
    }
}

public sealed partial class RenderApiProbe
{
    /// <summary>A skinned triangle rendered twice in one Update with its bone moved between the
    /// renders (after a frame at a first pose), with forceMatrixRecalculationPerRender off and
    /// on, and with its buffer asked for; then the next frame.</summary>
    IEnumerator ForcedSkinCases()
    {
        foreach (var (name, force, buffer) in new[] { ("plain", false, false), ("forced", true, false), ("buffer", false, true) })
        {
            string p = "prevskin.force." + name;
            var go = new GameObject("forced skin");
            var bone = new GameObject("bone").transform; bone.SetParent(go.transform, false);
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            var mesh = Make(); mesh.triangles = new[] { 0, 2, 1 };
            mesh.boneWeights = new[] { new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 }, new BoneWeight { weight0 = 1 } };
            mesh.bindposes = new[] { Matrix4x4.identity };
            skin.sharedMesh = mesh; skin.bones = new[] { bone }; skin.rootBone = go.transform; skin.updateWhenOffscreen = true;
            skin.forceMatrixRecalculationPerRender = force;
            var material = new Material(Shader.Find("Unlit/Color")); material.color = Color.green;
            skin.sharedMaterial = material;
            bone.localPosition = new Vector3(-1.5f, 0, 0);
            yield return null;
            if (buffer) using (skin.GetVertexBuffer()) { }
            yield return null;
            bone.localPosition = Vector3.zero;
            Image(p + ".first");
            bone.localPosition = new Vector3(1.5f, 0, 0);
            Image(p + ".second");
            yield return null;
            Image(p + ".next");
            DestroyImmediate(go); DestroyImmediate(mesh); DestroyImmediate(material);
        }
    }
}

public sealed class RenderApiGizmoControls : MonoBehaviour
{
    public bool drew, drewSelected;
    void OnDrawGizmos()
    {
        drew = true;
        Gizmos.matrix = Matrix4x4.Translate(new Vector3(-1.2f, 0, 0));
        Gizmos.color = Color.green; Gizmos.DrawWireCube(Vector3.zero, Vector3.one);
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = Color.blue; Gizmos.DrawSphere(new Vector3(1.2f, 0, 0), .4f);
        Gizmos.color = Color.yellow; Gizmos.DrawLine(new Vector3(-2,-1,0),new Vector3(2,-1,0));
    }
    void OnDrawGizmosSelected()
    {
        drewSelected = true;
        Gizmos.color = Color.magenta; Gizmos.DrawCube(new Vector3(0,1,0), new Vector3(.5f,.5f,.5f));
    }
}

public sealed class RenderApiSphereControl : MonoBehaviour
{
    public bool drew;
    void OnDrawGizmos()
    {
        drew=true;Gizmos.color=Color.cyan;
        Gizmos.matrix=Matrix4x4.TRS(new Vector3(-1.3f,-.2f,0),Quaternion.Euler(17,33,-15),new Vector3(1.2f,.8f,1.1f));
        Gizmos.DrawSphere(Vector3.zero,.55f);
        Gizmos.matrix=Matrix4x4.identity;
    }
}
