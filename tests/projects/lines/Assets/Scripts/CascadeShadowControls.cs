using UnityEngine;
using UnityEngine.Rendering;

public partial class LineProbe
{
    void CascadeShadowControls()
    {
        var camera=NewCamera("CascadeShadowControls",true,new Vector3(0,0,-6),Vector3.zero);
        camera.enabled=false;camera.cullingMask=1<<ImageLayer;camera.farClipPlane=30;
        var light=new GameObject("CascadeShadowLight").AddComponent<Light>();
        light.type=LightType.Directional;light.shadows=LightShadows.Hard;
        light.shadowBias=light.shadowNormalBias=0;light.shadowCustomResolution=128;
        light.cullingMask=camera.cullingMask;light.transform.rotation=Quaternion.Euler(15,35,0);
        RenderSettings.sun=light;QualitySettings.shadowDistance=20;
        var material=new Material(Resources.Load<Shader>("LocalShadowPosition"));
        var read=new Material(Resources.Load<Shader>("LineShadowRead"));
        var target=new RenderTexture(128,128,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();
        var command=new CommandBuffer();
        command.SetGlobalTexture("_ProbeShadowMap",new RenderTargetIdentifier(BuiltinRenderTextureType.CurrentActive),RenderTextureSubElement.Depth);
        command.Blit(Texture2D.blackTexture,target,read,0);light.AddCommandBuffer(LightEvent.AfterShadowMap,command);
        foreach(string kind in new[]{"line","trail","mesh"})
        {
            bool trail=kind=="trail";
            Mesh mesh=null;
            var go=new GameObject("CascadeShadowStrip"){layer=ImageLayer};
            var points=new[]{new Vector3(-0.6f,0,-2),new Vector3(0.05f,0.25f,0.2f),new Vector3(0.6f,-0.1f,3)};
            LineRenderer line=null;TrailRenderer tr=null;Renderer renderer;
            if(kind=="mesh")
            {
                mesh=new Mesh();mesh.vertices=new[]{new Vector3(-0.6f,-0.15f,0),new Vector3(0.6f,-0.15f,0),new Vector3(0.6f,0.15f,0),new Vector3(-0.6f,0.15f,0)};
                mesh.triangles=new[]{0,1,2,0,2,3};mesh.RecalculateBounds();
                go.AddComponent<MeshFilter>().sharedMesh=mesh;renderer=go.AddComponent<MeshRenderer>();
            }
            else if(trail)
            {
                go.transform.position=points[points.Length-1];tr=go.AddComponent<TrailRenderer>();tr.time=100;
                tr.Clear();tr.emitting=true;tr.AddPositions(points);tr.emitting=false;tr.widthMultiplier=0.35f;
                tr.widthCurve=AnimationCurve.Linear(0,0.5f,1,1.5f);tr.numCapVertices=tr.numCornerVertices=2;renderer=tr;
            }
            else
            {
                line=go.AddComponent<LineRenderer>();line.positionCount=points.Length;line.SetPositions(points);line.widthMultiplier=0.35f;
                line.widthCurve=AnimationCurve.Linear(0,0.5f,1,1.5f);line.numCapVertices=line.numCornerVertices=2;renderer=line;
            }
            renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.TwoSided;
            foreach(string control in new[]{"two25","two60","fourDefault","fourWide","fourPerspective","fourTransformZ","twoPerspective25","twoPerspective60","fourPerspectiveWide"})
            {
                QualitySettings.shadowCascades=control.StartsWith("two")?2:4;
                QualitySettings.shadowCascade2Split=control.EndsWith("60")?0.6f:0.25f;
                QualitySettings.shadowCascade4Split=control.EndsWith("Wide")?new Vector3(0.2f,0.5f,0.8f):new Vector3(0.067f,0.2f,0.467f);
                camera.orthographic=!control.Contains("Perspective");
                var alignment=control=="fourTransformZ"?LineAlignment.TransformZ:LineAlignment.View;
                if(trail){tr.shadowBias=0.5f;tr.alignment=alignment;}else if(line!=null){line.shadowBias=0.5f;line.alignment=alignment;}
                string key="shadowCascade."+kind+"."+control;
                if(mesh!=null)MeshValue(key+".bake",mesh);else Bake(key+".bake",renderer,camera,false);Image(key+".normal",camera);SaveShadowTarget(key+".map",target);
            }
            DestroyImmediate(go);if(mesh!=null)DestroyImmediate(mesh);
        }
        light.RemoveAllCommandBuffers();command.Release();target.Release();DestroyImmediate(target);
        DestroyImmediate(material);DestroyImmediate(read);DestroyImmediate(camera.gameObject);DestroyImmediate(light.gameObject);RenderSettings.sun=null;
    }
}
