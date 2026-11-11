using System;
using System.IO;
using UnityEngine;
public static class ProbeEntry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string output=Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if(string.IsNullOrEmpty(output)) return;
        var factoryAngles = new[] { Vector3.zero,new Vector3(0,0,11),new Vector3(0,0,12),new Vector3(0,0,23),
            new Vector3(0,0,90),new Vector3(0,0,13.37f),new Vector3(0,0,-47.125f),new Vector3(0,0,137.6f),
            new Vector3(23,35,7),new Vector3(23,35,19),new Vector3(17,-29,13),new Vector3(1.3f,2.7f,9.1f),
            new Vector3(37.2f,-68.3f,119.7f),new Vector3(-180,90,360),new Vector3(181,-179,540),new Vector3(.0001f,-.001f,.01f),new Vector3(0,0,360),new Vector3(0,0,-360),new Vector3(360,0,0),new Vector3(-360,0,0),
            new Vector3(0,360,0),new Vector3(0,-360,0),new Vector3(.000001f,0,0),new Vector3(-.000001f,0,0),
            new Vector3(359.99997f,0,0),new Vector3(360.00003f,0,0),new Vector3(0,359.99997f,-360.00003f),
            new Vector3(89.99999f,0,0),new Vector3(90.00001f,0,0),new Vector3(0,89.99999f,0),new Vector3(0,90.00001f,0),
            new Vector3(0,0,89.99999f),new Vector3(0,0,90.00001f),new Vector3(-90,0,0),
            new Vector3(179.99998f,0,0),new Vector3(180.00002f,0,0),new Vector3(-180.00002f,0,0),
            new Vector3(0,0,179.99998f),new Vector3(0,0,180.00002f),new Vector3(720,-1080,1440),new Vector3(-720,1080,-1440),
            new Vector3(10000,-20000,30000),new Vector3(1000000,-1000000,500000),new Vector3(12.34567f,-234.567f,45.6789f),
            new Vector3(-.03f,179.9f,-91.1f),new Vector3(.1f,.2f,.3f) };
        for(int i=0;i<factoryAngles.Length;i++)
        {
            var angle=factoryAngles[i];ProbeLog.Begin("factory/"+i);ProbeLog.Value("degrees",angle);
            Q("vector",Quaternion.Euler(angle));Q("components",Quaternion.Euler(angle.x,angle.y,angle.z));
            Q("normalized",Quaternion.Normalize(Quaternion.Euler(angle)));
            var q=Quaternion.Euler(angle);ProbeLog.Value("normSquared",Quaternion.Dot(q,q));
            ProbeLog.Value("right",q*Vector3.right);ProbeLog.Value("up",q*Vector3.up);ProbeLog.Value("forward",q*Vector3.forward);
            ProbeLog.Value("mixed",q*new Vector3(.3f,-.7f,1.1f));

        }
        var quaternions = new[] { Quaternion.identity,new Quaternion(0,0,0,0),new Quaternion(.23f,-.17f,.31f,.9f),
            new Quaternion(.3f,-.7f,1.1f,-.9f),new Quaternion(1,2,3,4),new Quaternion(-1.3f,.4f,.27f,-2.1f),
            new Quaternion(0,0,.7071068f,.7071068f),new Quaternion(-.38839376f,-.795187056f,-.08882126f,-.4570982f),
            new Quaternion(1e-6f,-2e-6f,3e-6f,-4e-6f),new Quaternion(17,-29,13,-41) };
        var vectors = new[] { Vector3.right,Vector3.up,Vector3.forward,new Vector3(.3f,-.7f,1.1f),
            new Vector3(-2.3f,4.7f,.011f),new Vector3(1000.125f,-.0003125f,17.25f),new Vector3(1e-6f,-2e-7f,3e-8f),Vector3.zero };
        for(int i=0;i<quaternions.Length;i++)
        for(int j=0;j<vectors.Length;j++)
        {
            ProbeLog.Begin("raw/"+i+"/"+j);Q("quaternion",quaternions[i]);
            ProbeLog.Value("input",vectors[j]);ProbeLog.Value("result",quaternions[i]*vectors[j]);
        }
        DotNormalizeControls();
        File.WriteAllText(output,ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void DotNormalizeControls()
    {
        // Fixed component inputs isolate Dot/Normalize from factory rounding.
        var factoryInputs=new[] { QB(0,0,0,1065353216),
            QB(0,0,1036274375,1065275977),
            QB(0,0,1037439749,1065261309),
            QB(0,0,1045178139,1065016408),
            QB(0,0,1060439283,1060439283),
            QB(0,0,1039034558,1065239150),
            QB(0,0,-1093882899,1063954408),
            QB(0,0,1064217798,1052321497),
            QB(1045742330,1049666909,-1154054672,1064287653),
            QB(1047647704,1048886231,1036176936,1064206469),
            QB(1038730989,-1098497024,1041540590,1064466857),
            QB(1012390212,1018757821,1034018140,1065294979),
            QB(-1096307722,-1090670818,1061463677,1047845958),
            QB(1060439283,-1291534237,-1087044365,868688020),
            QB(-1139869613,1007614069,949992068,1065351938),
            QB(896142521,-1223530243,951517908,1065353216),
            QB(0,0,-1279541970,-1082130432),
            QB(0,0,867941678,-1082130432),
            QB(-1279541970,0,0,-1082130432),
            QB(867941678,0,0,-1082130432),
            QB(0,-1279541970,0,-1082130432),
            QB(0,867941678,0,-1082130432),
            QB(840297522,0,0,1065353216),
            QB(-1307186126,0,0,1065353216),
            QB(886116532,0,0,-1082130432),
            QB(-1263603892,0,0,-1082130432),
            QB(705617095,-1261367116,-1263603892,1065353216),
            QB(1060439282,0,0,1060439284),
            QB(1060439284,0,0,1060439282),
            QB(0,1060439282,0,1060439284),
            QB(0,1060439284,0,1060439282),
            QB(0,0,1060439282,1060439284),
            QB(0,0,1060439284,1060439282),
            QB(-1087044365,0,0,1060439283),
            QB(1065353216,0,0,877727924),
            QB(1065353216,0,0,-1271992500),
            QB(-1082130432,0,0,-1271992500),
            QB(0,0,1065353216,877727924),
            QB(0,0,1065353216,-1271992500),
            QB(-1271153363,852287056,-1262764754,-1082130432),
            QB(876330287,-1295196660,884718894,-1082130432),
            QB(-1088882918,-1091395612,-1092810197,1056350395),
            QB(1059163049,-1097419720,-1103112207,-1087283389),
            QB(-1094263924,-1085566623,-1112152084,-1091958586),
            QB(-1086931036,1060324851,-1176076296,978399794),
            QB(979760607,988051103,992704758,1065353127) };
        for (int i=0;i<factoryInputs.Length;i++)
        {
            var q=factoryInputs[i];
            ProbeLog.Begin("dotnorm/factory/"+i);
            F("dot",Quaternion.Dot(q,q));
            N(q);
        }
        var inputs=new[] { Quaternion.identity,new Quaternion(0,0,0,0),
            new Quaternion(float.Epsilon,-float.Epsilon,float.Epsilon,-float.Epsilon),
            new Quaternion(1e-20f,-2e-20f,3e-20f,-4e-20f),
            new Quaternion(1e-23f,-2e-23f,3e-23f,-4e-23f),
            new Quaternion(1e-18f,-2e-18f,3e-18f,-4e-18f),
            new Quaternion(.3f,-.7f,1.1f,-.9f),new Quaternion(1,2,3,4),
            new Quaternion(float.MaxValue,1,-2,3),new Quaternion(1e19f,-2e19f,3e19f,-4e19f),
            new Quaternion(float.NaN,1,2,3),new Quaternion(1,2,3,float.NaN),
            new Quaternion(float.PositiveInfinity,1,2,3),new Quaternion(1,float.NegativeInfinity,2,3),
            new Quaternion(-0f,0f,-0f,0f),new Quaternion(1e-6f,0,0,0),
            new Quaternion(9.999999e-6f,0,0,0),new Quaternion(1e-5f,0,0,0),
            new Quaternion(1.0000001e-5f,0,0,0),new Quaternion(1e-4f,0,0,0) };
        for(int i=0;i<inputs.Length;i++)
        {
            var q=inputs[i]; ProbeLog.Begin("dotnorm/raw/"+i); B("input",q);
            F("dot",Quaternion.Dot(q,q)); N(q);
            Pair(q,Quaternion.identity);
            q.ToAngleAxis(out float rawAngle,out Vector3 rawAxis);
            F("axisAngle",rawAngle); F("axis.x",rawAxis.x); F("axis.y",rawAxis.y); F("axis.z",rawAxis.z);
        }
        var boundaries=new[] { new Quaternion(0.526056409f,0.540396392f,0.262062252f,-0.60212934f), new Quaternion(0.526056409f,0.540396392f,0.262062252f,-0.60212779f),
            new Quaternion(0.526056409f,0.540396392f,0.262062252f,-0.60212934f), new Quaternion(0.526056409f,0.540396392f,0.262062252f,-0.602127731f),
            new Quaternion(0.526056409f,0.540396392f,0.262062252f,-0.60212934f), new Quaternion(0.526056409f,0.540396392f,0.262062252f,-0.602127671f),
            new Quaternion(0.256155431f,-0.674340487f,0.331052005f,-0.608320475f), new Quaternion(0.256155431f,-0.674340487f,0.331052005f,-0.608318985f),
            new Quaternion(0.256155431f,-0.674340487f,0.331052005f,-0.608320475f), new Quaternion(0.256155431f,-0.674340487f,0.331052005f,-0.608318925f),
            new Quaternion(0.256155431f,-0.674340487f,0.331052005f,-0.608320475f), new Quaternion(0.256155431f,-0.674340487f,0.331052005f,-0.608318865f),
            new Quaternion(0.757056952f,-0.0118467491f,0.647465885f,-0.0866750479f), new Quaternion(0.757056952f,-0.0118467491f,0.647465885f,-0.0866609514f),
            new Quaternion(0.757056952f,-0.0118467491f,0.647465885f,-0.0866750479f), new Quaternion(0.757056952f,-0.0118467491f,0.647465885f,-0.0866609439f),
            new Quaternion(0.757056952f,-0.0118467491f,0.647465885f,-0.0866750479f), new Quaternion(0.757056952f,-0.0118467491f,0.647465885f,-0.0866609365f),
            new Quaternion(-0.807339013f,0.371707171f,-0.25203824f,-0.382771701f), new Quaternion(-0.807339013f,0.371707171f,-0.25203824f,-0.382769197f),
            new Quaternion(-0.807339013f,0.371707171f,-0.25203824f,-0.382771701f), new Quaternion(-0.807339013f,0.371707171f,-0.25203824f,-0.382769167f),
            new Quaternion(-0.807339013f,0.371707171f,-0.25203824f,-0.382771701f), new Quaternion(-0.807339013f,0.371707171f,-0.25203824f,-0.382769138f),
            new Quaternion(-0.00279926276f,-0.0981051326f,0.927556217f,0.360564858f), new Quaternion(-0.00279926276f,-0.0981051326f,0.927556217f,0.360561997f),
            new Quaternion(-0.00279926276f,-0.0981051326f,0.927556217f,0.360564858f), new Quaternion(-0.00279926276f,-0.0981051326f,0.927556217f,0.360562027f),
            new Quaternion(-0.00279926276f,-0.0981051326f,0.927556217f,0.360564858f), new Quaternion(-0.00279926276f,-0.0981051326f,0.927556217f,0.360562056f),
            new Quaternion(0.235984474f,-0.722281694f,0.349591464f,0.548093379f), new Quaternion(0.235984474f,-0.722281694f,0.349591464f,0.548091352f),
            new Quaternion(0.235984474f,-0.722281694f,0.349591464f,0.548093379f), new Quaternion(0.235984474f,-0.722281694f,0.349591464f,0.548091412f),
            new Quaternion(0.235984474f,-0.722281694f,0.349591464f,0.548093379f), new Quaternion(0.235984474f,-0.722281694f,0.349591464f,0.548091471f),
            new Quaternion(-0.741827071f,0.519306958f,-0.424213767f,-0.00746250153f), new Quaternion(-0.741827071f,0.519306958f,-0.424213767f,-0.00730953133f),
            new Quaternion(-0.741827071f,0.519306958f,-0.424213767f,-0.00746250153f), new Quaternion(-0.741827071f,0.519306958f,-0.424213767f,-0.00730953086f),
            new Quaternion(-0.741827071f,0.519306958f,-0.424213767f,-0.00746250153f), new Quaternion(-0.741827071f,0.519306958f,-0.424213767f,-0.0073095304f),
            new Quaternion(0.343016922f,-0.667060137f,-0.619486511f,0.231530756f), new Quaternion(0.343016922f,-0.667060137f,-0.619486511f,0.231526956f),
            new Quaternion(0.343016922f,-0.667060137f,-0.619486511f,0.231530756f), new Quaternion(0.343016922f,-0.667060137f,-0.619486511f,0.231526971f),
            new Quaternion(0.343016922f,-0.667060137f,-0.619486511f,0.231530756f), new Quaternion(0.343016922f,-0.667060137f,-0.619486511f,0.231526986f),
            new Quaternion(-0.0533711165f,-0.414791703f,0.802083611f,0.42633456f), new Quaternion(-0.0533711165f,-0.414791703f,0.802083611f,0.426332355f),
            new Quaternion(-0.0533711165f,-0.414791703f,0.802083611f,0.42633456f), new Quaternion(-0.0533711165f,-0.414791703f,0.802083611f,0.426332384f),
            new Quaternion(-0.0533711165f,-0.414791703f,0.802083611f,0.42633456f), new Quaternion(-0.0533711165f,-0.414791703f,0.802083611f,0.426332414f),
            new Quaternion(-0.677244365f,0.393708199f,-0.210212022f,-0.584931433f), new Quaternion(-0.677244365f,0.393708199f,-0.210212022f,-0.584929824f),
            new Quaternion(-0.677244365f,0.393708199f,-0.210212022f,-0.584931433f), new Quaternion(-0.677244365f,0.393708199f,-0.210212022f,-0.584929764f),
            new Quaternion(-0.677244365f,0.393708199f,-0.210212022f,-0.584931433f), new Quaternion(-0.677244365f,0.393708199f,-0.210212022f,-0.584929705f),
            new Quaternion(0.678167999f,-0.325757742f,-0.576167762f,-0.319375366f), new Quaternion(0.678167999f,-0.325757742f,-0.576167762f,-0.319372684f),
            new Quaternion(0.678167999f,-0.325757742f,-0.576167762f,-0.319375366f), new Quaternion(0.678167999f,-0.325757742f,-0.576167762f,-0.319372654f),
            new Quaternion(0.678167999f,-0.325757742f,-0.576167762f,-0.319375366f), new Quaternion(0.678167999f,-0.325757742f,-0.576167762f,-0.319372624f),
            new Quaternion(0.889918506f,0.259695888f,-0.315039754f,0.203354299f), new Quaternion(0.889918506f,0.259695888f,-0.315039754f,0.203349695f),
            new Quaternion(0.889918506f,0.259695888f,-0.315039754f,0.203354299f), new Quaternion(0.889918506f,0.259695888f,-0.315039754f,0.20334971f),
            new Quaternion(0.889918506f,0.259695888f,-0.315039754f,0.203354299f), new Quaternion(0.889918506f,0.259695888f,-0.315039754f,0.203349724f),
            new Quaternion(-0.226783812f,-0.715634763f,0.644993961f,0.142894223f), new Quaternion(-0.226783812f,-0.715634763f,0.644993961f,0.14288725f),
            new Quaternion(-0.226783812f,-0.715634763f,0.644993961f,0.142894223f), new Quaternion(-0.226783812f,-0.715634763f,0.644993961f,0.142887264f),
            new Quaternion(-0.226783812f,-0.715634763f,0.644993961f,0.142894223f), new Quaternion(-0.226783812f,-0.715634763f,0.644993961f,0.142887279f),
            new Quaternion(-0.59802258f,0.436924428f,0.531697392f,0.410808802f), new Quaternion(-0.59802258f,0.436924428f,0.531697392f,0.410806417f),
            new Quaternion(-0.59802258f,0.436924428f,0.531697392f,0.410808802f), new Quaternion(-0.59802258f,0.436924428f,0.531697392f,0.410806447f),
            new Quaternion(-0.59802258f,0.436924428f,0.531697392f,0.410808802f), new Quaternion(-0.59802258f,0.436924428f,0.531697392f,0.410806477f),
            new Quaternion(0.399112046f,-0.672246218f,-0.238839239f,-0.575977743f), new Quaternion(0.399112046f,-0.672246218f,-0.238839239f,-0.575976133f),
            new Quaternion(0.399112046f,-0.672246218f,-0.238839239f,-0.575977743f), new Quaternion(0.399112046f,-0.672246218f,-0.238839239f,-0.575976074f),
            new Quaternion(0.399112046f,-0.672246218f,-0.238839239f,-0.575977743f), new Quaternion(0.399112046f,-0.672246218f,-0.238839239f,-0.575976014f),
            new Quaternion(-0.34011066f,-0.735516012f,-0.581893682f,0.0688526779f), new Quaternion(-0.34011066f,-0.735516012f,-0.581893682f,0.0688377619f),
            new Quaternion(-0.34011066f,-0.735516012f,-0.581893682f,0.0688526779f), new Quaternion(-0.34011066f,-0.735516012f,-0.581893682f,0.0688377693f),
            new Quaternion(-0.34011066f,-0.735516012f,-0.581893682f,0.0688526779f), new Quaternion(-0.34011066f,-0.735516012f,-0.581893682f,0.0688377768f),
            new Quaternion(-0.239993438f,0.0665200353f,0.57891345f,-0.776426017f), new Quaternion(-0.239993438f,0.0665200353f,0.57891345f,-0.776424885f),
            new Quaternion(-0.239993438f,0.0665200353f,0.57891345f,-0.776426017f), new Quaternion(-0.239993438f,0.0665200353f,0.57891345f,-0.776424825f),
            new Quaternion(-0.239993438f,0.0665200353f,0.57891345f,-0.776426017f), new Quaternion(-0.239993438f,0.0665200353f,0.57891345f,-0.776424766f),
            new Quaternion(0.654105186f,-0.0239082631f,0.640179515f,-0.402175277f), new Quaternion(0.654105186f,-0.0239082631f,0.640179515f,-0.402172863f),
            new Quaternion(0.654105186f,-0.0239082631f,0.640179515f,-0.402175277f), new Quaternion(0.654105186f,-0.0239082631f,0.640179515f,-0.402172834f),
            new Quaternion(0.654105186f,-0.0239082631f,0.640179515f,-0.402175277f), new Quaternion(0.654105186f,-0.0239082631f,0.640179515f,-0.402172804f),
            new Quaternion(0.211929902f,-0.677191556f,0.672163308f,-0.211408913f), new Quaternion(0.211929902f,-0.677191556f,0.672163308f,-0.211404473f),
            new Quaternion(0.211929902f,-0.677191556f,0.672163308f,-0.211408913f), new Quaternion(0.211929902f,-0.677191556f,0.672163308f,-0.211404458f),
            new Quaternion(0.211929902f,-0.677191556f,0.672163308f,-0.211408913f), new Quaternion(0.211929902f,-0.677191556f,0.672163308f,-0.211404443f),
            new Quaternion(-0.605450094f,0.0366740488f,-0.691339195f,-0.392600656f), new Quaternion(-0.605450094f,0.0366740488f,-0.691339195f,-0.392598212f),
            new Quaternion(-0.605450094f,0.0366740488f,-0.691339195f,-0.392600656f), new Quaternion(-0.605450094f,0.0366740488f,-0.691339195f,-0.392598182f),
            new Quaternion(-0.605450094f,0.0366740488f,-0.691339195f,-0.392600656f), new Quaternion(-0.605450094f,0.0366740488f,-0.691339195f,-0.392598152f),
            new Quaternion(0.568821192f,0.559816539f,0.0646404177f,0.599057078f), new Quaternion(0.568821192f,0.559816539f,0.0646404177f,0.599055529f),
            new Quaternion(0.568821192f,0.559816539f,0.0646404177f,0.599057078f), new Quaternion(0.568821192f,0.559816539f,0.0646404177f,0.599055588f),
            new Quaternion(0.568821192f,0.559816539f,0.0646404177f,0.599057078f), new Quaternion(0.568821192f,0.559816539f,0.0646404177f,0.599055648f),
            new Quaternion(-0.397925317f,0.741845906f,-0.364528745f,0.398043871f), new Quaternion(-0.397925317f,0.741845906f,-0.364528745f,0.398041219f),
            new Quaternion(-0.397925317f,0.741845906f,-0.364528745f,0.398043871f), new Quaternion(-0.397925317f,0.741845906f,-0.364528745f,0.398041248f),
            new Quaternion(-0.397925317f,0.741845906f,-0.364528745f,0.398043871f), new Quaternion(-0.397925317f,0.741845906f,-0.364528745f,0.398041278f),
            new Quaternion(0.465795219f,-0.470857829f,0.479910672f,0.575337648f), new Quaternion(0.465795219f,-0.470857829f,0.479910672f,0.57533592f),
            new Quaternion(0.465795219f,-0.470857829f,0.479910672f,0.575337648f), new Quaternion(0.465795219f,-0.470857829f,0.479910672f,0.575335979f),
            new Quaternion(0.465795219f,-0.470857829f,0.479910672f,0.575337648f), new Quaternion(0.465795219f,-0.470857829f,0.479910672f,0.575336039f),
            new Quaternion(-0.568197608f,0.479182065f,0.232027069f,-0.627454698f), new Quaternion(-0.568197608f,0.479182065f,0.232027069f,-0.627453268f),
            new Quaternion(-0.568197608f,0.479182065f,0.232027069f,-0.627454698f), new Quaternion(-0.568197608f,0.479182065f,0.232027069f,-0.627453208f),
            new Quaternion(-0.568197608f,0.479182065f,0.232027069f,-0.627454698f), new Quaternion(-0.568197608f,0.479182065f,0.232027069f,-0.627453148f) };
        for(int i=0;i<boundaries.Length;i+=2)
        {
            ProbeLog.Begin("dotnorm/equality/"+(i/2));
            Pair(boundaries[i],boundaries[i+1]);
        }
        var exactThresholds=new[] { BitConverter.ToSingle(BitConverter.GetBytes(1065353198),0),
            BitConverter.ToSingle(BitConverter.GetBytes(1065353199),0),
            BitConverter.ToSingle(BitConverter.GetBytes(1065353200),0),-1f };
        for(int i=0;i<exactThresholds.Length;i++)
        {
            ProbeLog.Begin("dotnorm/threshold/"+i);
            var target=new Quaternion(0,0,0,exactThresholds[i]);
            Pair(Quaternion.identity,target);
            B("towardsZero",Quaternion.RotateTowards(Quaternion.identity,target,0f));
        }
        var nativeControls=new[] { Quaternion.identity,new Quaternion(1,2,3,4),
            new Quaternion(.3f,-.7f,1.1f,-.9f),new Quaternion(.114148952f,-.262237549f,.145164222f,.9471689f) };
        for(int i=0;i<nativeControls.Length;i++)
        {
            ProbeLog.Begin("dotnorm/result-normalization/"+i);
            var q=nativeControls[i];
            B("lerp",Quaternion.Lerp(q,q,0f));
            B("unclamped",Quaternion.LerpUnclamped(q,q,0f));
            q.ToAngleAxis(out float angle,out Vector3 axis);
            F("axisAngle",angle); F("axis.x",axis.x); F("axis.y",axis.y); F("axis.z",axis.z);
        }
        var nearAngles=new[] { QB(0,0,0,1065353216),
            QB(951517907,0,0,1065353216),
            QB(979682183,0,0,1065353210),
            QB(984322724,0,0,1065353202),
            QB(985072335,0,0,1065353200),
            QB(985821948,0,0,1065353198),
            QB(986571561,0,0,1065353195),
            QB(988070785,0,0,1065353190),
            QB(1007614399,0,0,1065352577),
            QB(1060439283,0,0,1060439283),
            QB(1065353210,0,0,979682575),
            QB(1065353216,0,0,924009539),
            QB(1065353216,0,0,-1287930578),
            QB(1065353216,0,0,-1223377987),
            QB(1065353210,0,0,-1167799571),
            QB(979683872,0,0,-1082130438),
            QB(-1279541970,-2147483648,-2147483648,-1082130432) };
        for(int i=0;i<nearAngles.Length;i++)
        {
            ProbeLog.Begin("dotnorm/angle/"+i);
            var q=nearAngles[i];
            Pair(Quaternion.identity,q);
            F("oppositeAngle",Quaternion.Angle(q,new Quaternion(-q.x,-q.y,-q.z,-q.w)));
        }
    }
    static Quaternion QB(int x,int y,int z,int w) => new Quaternion(
        BitConverter.ToSingle(BitConverter.GetBytes(x),0),BitConverter.ToSingle(BitConverter.GetBytes(y),0),
        BitConverter.ToSingle(BitConverter.GetBytes(z),0),BitConverter.ToSingle(BitConverter.GetBytes(w),0));
    static void N(Quaternion q)
    {
        B("static",Quaternion.Normalize(q));
        B("property",q.normalized);
        B("original",q);
        q.Normalize(); B("instance",q);
    }
    static void Pair(Quaternion a,Quaternion b)
    {
        B("a",a); B("b",b);
        F("dotPair",Quaternion.Dot(a,b));
        ProbeLog.Value("equal",a==b); ProbeLog.Value("unequal",a!=b);
        F("angle",Quaternion.Angle(a,b));
    }
    static void F(string key,float value)
    {
        ProbeLog.Value(key,value.ToString("G9",System.Globalization.CultureInfo.InvariantCulture));
        ProbeLog.Value(key+".bits",BitConverter.ToInt32(BitConverter.GetBytes(value),0));
    }
    static void B(string key,Quaternion q)
    {
        F(key+".x",q.x); F(key+".y",q.y); F(key+".z",q.z); F(key+".w",q.w);
    }
    static void Q(string key,Quaternion q)
    {
        ProbeLog.Value(key+".x",q.x);ProbeLog.Value(key+".y",q.y);ProbeLog.Value(key+".z",q.z);ProbeLog.Value(key+".w",q.w);
    }
}
