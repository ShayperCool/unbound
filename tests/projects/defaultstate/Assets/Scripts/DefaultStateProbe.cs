using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using UnityEditor;

public sealed class DefaultStateProbe : MonoBehaviour
{
    [Serializable] public sealed class Candidate { public string type, module, category, status; }
    [Serializable] public sealed class Manifest { public Candidate[] types; }
    [Serializable] public sealed class Record { public string key, status, value; }
    sealed class Identity : IEqualityComparer<object>
    { public new bool Equals(object a, object b) { return ReferenceEquals(a,b); } public int GetHashCode(object value) { return RuntimeHelpers.GetHashCode(value); } }
    static StreamWriter output;
    static string progress;
    static Candidate[] candidates;
    static int index;
    static readonly Dictionary<Type,MemberInfo[]> members = new Dictionary<Type,MemberInfo[]>();
    static readonly Identity identity = new Identity();

    [RuntimeInitializeOnLoadMethod]
    static void Install()
    {
        var path = Environment.GetEnvironmentVariable("DEFAULT_STATE_OUT");
        if (string.IsNullOrEmpty(path)) return;
        output = new StreamWriter(path);
        progress = Environment.GetEnvironmentVariable("DEFAULT_STATE_PROGRESS");
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(Environment.GetEnvironmentVariable("DEFAULT_STATE_MANIFEST")));
        candidates = manifest.types;
        new GameObject("DefaultStateRecorder").AddComponent<DefaultStateProbe>();
    }
    void Update()
    {
        for (int step=0;step<4 && index<candidates.Length;step++,index++) Capture(candidates[index]);
        if (index<candidates.Length) return;
        Capture(new Candidate { type=typeof(FreshStateControl).FullName, module=typeof(FreshStateControl).Assembly.GetName().Name,
            category="class", status="candidate" });
        RemovedGetterControls();
        CoreControls();
        Physics2DControls();
        Physics2DPayloadControls();
        PhysicsControls();
        PhysicsPayloadControls();
        Emit("complete","value","1"); output.Dispose(); output=null;
        EditorApplication.Exit(0);
    }
    static void Emit(string key,string status,string value)
    {
        output.WriteLine(JsonUtility.ToJson(new Record { key=key,status=status,value=value }));
        output.Flush();
    }
    static void CoreControls()
    {
        var owner = new GameObject("CoreFactoryControl");
        try
        {
            owner.SetActive(false);
            Emit("CoreControl/factory/derived", "value", (owner.AddComponent<FactoryControlBehaviour>() != null).ToString());
            Emit("CoreControl/factory/meshRenderer", "value", (owner.AddComponent<MeshRenderer>() != null).ToString());
            foreach (var type in new[] { typeof(Component), typeof(Behaviour), typeof(MonoBehaviour), typeof(Renderer) })
                Emit("CoreControl/factory/" + type.Name, "value", (owner.AddComponent(type) == null).ToString());
            Emit("CoreControl/factory/componentCount", "value", owner.GetComponents<Component>().Length.ToString());
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
        CoreValue("localizationFalse", new JetBrains.Annotations.LocalizationRequiredAttribute(false).Required);
        CoreValue("disposeFalse", new JetBrains.Annotations.MustDisposeResourceAttribute(false).Value);
        var means = new JetBrains.Annotations.MeansImplicitUseAttribute((JetBrains.Annotations.ImplicitUseKindFlags)2);
        CoreValue("meansExplicitKind", new[] { (int)means.UseKindFlags, (int)means.TargetFlags });
        var used = new JetBrains.Annotations.UsedImplicitlyAttribute((JetBrains.Annotations.ImplicitUseTargetFlags)2);
        CoreValue("usedExplicitTarget", new[] { (int)used.UseKindFlags, (int)used.TargetFlags });
        var both = new JetBrains.Annotations.UsedImplicitlyAttribute((JetBrains.Annotations.ImplicitUseKindFlags)0,
            (JetBrains.Annotations.ImplicitUseTargetFlags)0);
        CoreValue("usedExplicitZero", new[] { (int)both.UseKindFlags, (int)both.TargetFlags });
        var category = new UnityEngine.Categorization.CategoryInfoAttribute { Order = -27 };
        var element = new UnityEngine.Categorization.ElementInfoAttribute { Order = 14 };
        CoreValue("categoryWrite", category.Order); CoreValue("elementWrite", element.Order);
        var space = new SpaceAttribute(23f);
        CoreValue("spaceExplicit", new object[] { space.height, space.applyToCollection });
        foreach (var type in new[] { typeof(UnityException), typeof(MissingReferenceException),
            typeof(MissingComponentException), typeof(UnassignedReferenceException) })
        {
            var one = (Exception)Activator.CreateInstance(type, new object[] { "changed" });
            var two = (Exception)Activator.CreateInstance(type, new object[] { "changed", new InvalidOperationException("inner") });
            CoreValue(type.Name + "/message", new object[] { one.HResult, one.Message });
            CoreValue(type.Name + "/inner", new object[] { two.HResult, two.Message, two.InnerException.Message });
        }
        foreach (bool populated in new[] { false, true })
        {
            var requiredOwner = new GameObject("RequiredRendererControl");
            var oldHandler = Debug.unityLogger.logHandler;
            var requiredHandler = new FactoryControlLogHandler(oldHandler);
            try
            {
                requiredOwner.SetActive(false);
                if (populated) requiredOwner.AddComponent<MeshRenderer>();
                Debug.unityLogger.logHandler = requiredHandler;
                var script = requiredOwner.AddComponent<RendererRequiredControlBehaviour>();
                CoreValue("requiredRenderer/" + populated, new object[] { script == null,
                    requiredOwner.GetComponents<Component>().Select(c => c.GetType().Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(),
                    requiredHandler.errors });
            }
            finally { Debug.unityLogger.logHandler = oldHandler; UnityEngine.Object.DestroyImmediate(requiredOwner); }
        }
        var selfOwner = new GameObject("SelfRequiredControl");
        var previousHandler = Debug.unityLogger.logHandler;
        var handler = new FactoryControlLogHandler(previousHandler);
        try
        {
            Debug.unityLogger.logHandler = handler;
            selfOwner.AddComponent<SelfRequiredControlBehaviour>();
            CoreValue("selfRequired", new[] { selfOwner.GetComponents<Component>().Length, handler.errors });
        }
        finally { Debug.unityLogger.logHandler = previousHandler; UnityEngine.Object.DestroyImmediate(selfOwner); }
        var cameraOwner = new GameObject("CoreCameraControl");
        var target = new RenderTexture(128, 32, 0);
        try
        {
            var camera = cameraOwner.AddComponent<Camera>();
            camera.targetTexture = target;
            CoreValue("cameraTarget", new object[] { camera.pixelWidth, camera.pixelHeight, camera.aspect });
            camera.targetTexture = null;
            CoreValue("cameraRestored", new object[] { camera.pixelWidth, camera.pixelHeight, camera.aspect });
            foreach (var pair in new object[][] {
                new object[] { "far", "farClipPlane", 73f, 115f }, new object[] { "near", "nearClipPlane", 2f, 3f },
                new object[] { "fov", "fieldOfView", 43f, 47f }, new object[] { "hdr", "allowHDR", false, true } })
            {
                var alias = typeof(Camera).GetProperty((string)pair[0]);
                var modern = typeof(Camera).GetProperty((string)pair[1]);
                alias.SetValue(camera, pair[2], null);
                var first = modern.GetValue(camera, null);
                modern.SetValue(camera, pair[3], null);
                CoreValue("alias/" + pair[0], new object[] { first, alias.GetValue(camera, null) });
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(cameraOwner); UnityEngine.Object.DestroyImmediate(target); }
        CoreArithmeticAndUvControls();
    }
    static void CoreArithmeticAndUvControls()
    {
        var rates = new[] { default(RefreshRate), new RefreshRate { numerator = 1, denominator = 0 },
            new RefreshRate { numerator = 60000, denominator = 1001 }, new RefreshRate { numerator = 5, denominator = 2 },
            new RefreshRate { numerator = 3, denominator = 2 }, new RefreshRate { numerator = uint.MaxValue, denominator = 1 },
            new RefreshRate { numerator = int.MaxValue, denominator = 1 } };
        for (int i = 0; i < rates.Length; i++)
        {
            var resolution = new Resolution { refreshRateRatio = rates[i] };
            CoreValue("rate/" + i, new object[] { rates[i].value, resolution.refreshRate, rates[i].ToString() });
        }
        var a = new RefreshRate { numerator = uint.MaxValue, denominator = uint.MaxValue - 1 };
        var b = new RefreshRate { numerator = uint.MaxValue - 1, denominator = uint.MaxValue - 2 };
        CoreValue("rateComparison", new object[] { rates[0].Equals(rates[1]), rates[0].CompareTo(rates[2]),
            rates[2].CompareTo(rates[0]), rates[0].CompareTo(rates[1]), a.Equals(b), a.CompareTo(b),
            new RefreshRate { numerator = 2, denominator = 1 }.Equals(new RefreshRate { numerator = 4, denominator = 2 }) });
        var negative = new Resolution { refreshRate = -1 };
        CoreValue("rateNegativeSetter", new object[] { negative.refreshRateRatio.numerator, negative.refreshRate });
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.one } };
        try
        {
            mesh.uv2 = new[] { new Vector2(4, 5), new Vector2(6, 7) };
            var legacy = typeof(Mesh).GetProperty("uv1");
            legacy.SetValue(mesh, new[] { Vector2.zero, Vector2.one }, null);
            CoreValue("uv1AfterWrite", new object[] { legacy.GetValue(mesh, null), mesh.uv2 });
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }
    static void CoreValue(string key, object value)
    { Emit("CoreControl/" + key, "value", Format(value, value.GetType(), new HashSet<object>(identity), 0)); }
    static Type ProbeType(string name)
    { var entry = candidates.First(c => c.type == "UnityEngine." + name); return Type.GetType(entry.type + ", " + entry.module, true); }
    static void Physics2DControls()
    {
        var owner = new GameObject("Physics2DFactoryControl");
        try
        {
            owner.SetActive(false);
            owner.AddComponent(ProbeType("Rigidbody2D"));
            foreach (var name in new[] { "Collider2D", "Joint2D", "AnchoredJoint2D", "Effector2D", "PhysicsUpdateBehaviour2D" })
                CoreValue("Physics2D/base/" + name, owner.AddComponent(ProbeType(name)) == null);
            foreach (var name in new[] { "BoxCollider2D", "DistanceJoint2D", "PointEffector2D" })
                CoreValue("Physics2D/concrete/" + name, owner.AddComponent(ProbeType(name)) != null);
            CoreValue("Physics2D/count", owner.GetComponents<Component>().Length);
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
        foreach (bool populated in new[] { false, true })
        {
            var requiredOwner = new GameObject("RequiredCollider2DControl");
            var previous = Debug.unityLogger.logHandler;
            var handler = new FactoryControlLogHandler(previous);
            try
            {
                requiredOwner.SetActive(false);
                if (populated) requiredOwner.AddComponent(ProbeType("BoxCollider2D"));
                Debug.unityLogger.logHandler = handler;
                var component = requiredOwner.AddComponent<Collider2DRequiredControlBehaviour>();
                CoreValue("Physics2D/required/" + populated, new object[] { component == null,
                    requiredOwner.GetComponents<Component>().Select(c => c.GetType().Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(), handler.errors });
            }
            finally { Debug.unityLogger.logHandler = previous; UnityEngine.Object.DestroyImmediate(requiredOwner); }
        }
    }
    static void Physics2DPayloadControls()
    {
        var invalid = new Collision2D();
        string firstError = "returned";
        try { var ignored = invalid.contacts; } catch (Exception error) { firstError = error.GetType().FullName; }
        CoreValue("Physics2D/collision/default", new object[] { invalid.enabled, invalid.contactCount, firstError });
        CoreValue("Physics2D/collision/retry", new object[] { invalid.contacts.Length,
            invalid.GetContacts(new ContactPoint2D[0]), ReferenceEquals(invalid.contacts, invalid.contacts) });
        var compositeOwner = new GameObject("CompositeCapabilityControl");
        try
        {
            compositeOwner.SetActive(false);
            compositeOwner.AddComponent<Rigidbody2D>(); compositeOwner.AddComponent<CompositeCollider2D>();
            var edge = compositeOwner.AddComponent<EdgeCollider2D>();
            edge.points = new[] { new Vector2(-1,-1), new Vector2(-1,1), new Vector2(1,1), new Vector2(1,-1), new Vector2(-1,-1) };
            edge.compositeOperation = Collider2D.CompositeOperation.Merge;
            var box = compositeOwner.AddComponent<BoxCollider2D>();
            box.compositeOperation = Collider2D.CompositeOperation.Merge;
            CoreValue("Physics2D/compositeCapability", new object[] { edge.compositeCapable, edge.compositeOperation,
                box.compositeCapable, box.compositeOperation });
        }
        finally { UnityEngine.Object.DestroyImmediate(compositeOwner); }
        var previousMode = Physics2D.simulationMode;
        var first = new GameObject("CollisionControlFirst"); var second = new GameObject("CollisionControlSecond");
        try
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            var body = first.AddComponent<Rigidbody2D>(); body.gravityScale = 0;
            first.AddComponent<BoxCollider2D>();
            var observer = first.AddComponent<CollisionContactControl>();
            second.transform.position = new Vector3(0.5f, 0, 0); second.AddComponent<BoxCollider2D>();
            Physics2D.SyncTransforms(); Physics2D.Simulate(0.02f);
            CoreValue("Physics2D/collision/enter", observer.enter ?? new object[] { "missing" });
            second.transform.position = new Vector3(5,0,0);
            Physics2D.SyncTransforms(); Physics2D.Simulate(0.02f);
            CoreValue("Physics2D/collision/exit", observer.exit ?? new object[] { "missing" });
            CoreValue("Physics2D/sceneContext", new object[] { default(PhysicsScene2D).IsValid(), default(PhysicsScene2D).subStepCount,
                Physics2D.defaultPhysicsScene.IsValid(), Physics2D.defaultPhysicsScene.subStepCount,
                default(PhysicsScene2D).GetHashCode(), Physics2D.defaultPhysicsScene.GetHashCode() });
        }
        finally
        { UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); Physics2D.simulationMode = previousMode; }
    }
    static void PhysicsControls()
    {
        var owner = new GameObject("PhysicsFactoryControl");
        try
        {
            owner.SetActive(false); var body = owner.AddComponent<Rigidbody>();
            foreach (var name in new[] { "Collider", "Joint" })
                CoreValue("Physics/base/" + name, owner.AddComponent(ProbeType(name)) == null);
            CoreValue("Physics/concrete", new[] { owner.AddComponent<BoxCollider>() != null, owner.AddComponent<FixedJoint>() != null });
            var iterations = typeof(Rigidbody).GetProperty("solverIterationCount");
            var velocity = typeof(Rigidbody).GetProperty("solverVelocityIterationCount");
            body.solverIterations = 13; body.solverVelocityIterations = 7;
            CoreValue("Physics/solver/readAliases", new[] { (int)iterations.GetValue(body, null), (int)velocity.GetValue(body, null) });
            iterations.SetValue(body, 9, null); velocity.SetValue(body, 3, null);
            CoreValue("Physics/solver/writeAliases", new[] { body.solverIterations, body.solverVelocityIterations });
            var collider = owner.AddComponent<MeshCollider>();
            var smooth = typeof(MeshCollider).GetProperty("smoothSphereCollisions");
            smooth.SetValue(collider, false, null);
            CoreValue("Physics/smoothFalse", smooth.GetValue(collider, null));
            smooth.SetValue(collider, true, null);
            CoreValue("Physics/smoothTrue", smooth.GetValue(collider, null));
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
        foreach (bool populated in new[] { false, true })
        {
            var requiredOwner = new GameObject("RequiredColliderControl");
            var previous = Debug.unityLogger.logHandler; var handler = new FactoryControlLogHandler(previous);
            try
            {
                requiredOwner.SetActive(false);
                if (populated) requiredOwner.AddComponent<BoxCollider>();
                Debug.unityLogger.logHandler = handler;
                var component = requiredOwner.AddComponent<ColliderRequiredControlBehaviour>();
                CoreValue("Physics/required/" + populated, new object[] { component == null,
                    requiredOwner.GetComponents<Component>().Select(c => c.GetType().Name).OrderBy(n => n, StringComparer.Ordinal).ToArray(), handler.errors });
            }
            finally { Debug.unityLogger.logHandler = previous; UnityEngine.Object.DestroyImmediate(requiredOwner); }
        }
        object command = default(RaycastCommand); var hits = typeof(RaycastCommand).GetProperty("maxHits");
        hits.SetValue(command, 27, null);
        CoreValue("Physics/maxHitsWritten", hits.GetValue(command, null));
        command = new RaycastCommand(Vector3.one, Vector3.up, new QueryParameters(), 41);
        hits.SetValue(command, -1, null);
        CoreValue("Physics/maxHitsConstructed", new object[] { hits.GetValue(command, null), ((RaycastCommand)command).distance });
        foreach (var name in new string[] { "", "changed" })
        {
            var material = new PhysicsMaterial(name);
            try { CoreValue("Physics/material/name/" + (name ?? "null"), material.name); }
            finally { UnityEngine.Object.DestroyImmediate(material); }
        }
        var defaultMaterial = new PhysicsMaterial();
        try { CoreValue("Physics/material/defaultName", defaultMaterial.name); }
        finally { UnityEngine.Object.DestroyImmediate(defaultMaterial); }
        var firstOwner = new GameObject("FirstMaterialControl"); var secondOwner = new GameObject("SecondMaterialControl");
        var source = new PhysicsMaterial("custom");
        try
        {
            firstOwner.SetActive(false); secondOwner.SetActive(false);
            var first = firstOwner.AddComponent<BoxCollider>(); var second = secondOwner.AddComponent<SphereCollider>();
            var initial = first.material;
            CoreValue("Physics/material/fresh", new object[] { initial.name, ReferenceEquals(initial, first.material), ReferenceEquals(initial, first.sharedMaterial) });
            first.sharedMaterial = source; second.sharedMaterial = source;
            source.dynamicFriction = 0.2f; source.staticFriction = 0.3f; source.bounciness = 0.4f;
            source.frictionCombine = PhysicsMaterialCombine.Maximum; source.bounceCombine = PhysicsMaterialCombine.Minimum;
            var own = first.material;
            CoreValue("Physics/material/copied", new object[] { own.name, ReferenceEquals(own, source), ReferenceEquals(own, first.material),
                own.dynamicFriction, own.staticFriction, own.bounciness, own.frictionCombine, own.bounceCombine });
            own.dynamicFriction = 0.8f;
            CoreValue("Physics/material/isolation", new[] { source.dynamicFriction, second.sharedMaterial.dynamicFriction, first.material.dynamicFriction });
            second.sharedMaterial = own; var transferred = second.material;
            CoreValue("Physics/material/transferred", new object[] { transferred.name, ReferenceEquals(own, transferred),
                ReferenceEquals(transferred, second.material), transferred.dynamicFriction });
            transferred.dynamicFriction = 0.9f;
            CoreValue("Physics/material/transferIsolation", new[] { first.material.dynamicFriction, second.material.dynamicFriction });
            UnityEngine.Object.DestroyImmediate(initial); UnityEngine.Object.DestroyImmediate(own); UnityEngine.Object.DestroyImmediate(transferred);
        }
        finally { UnityEngine.Object.DestroyImmediate(firstOwner); UnityEngine.Object.DestroyImmediate(secondOwner); UnityEngine.Object.DestroyImmediate(source); }
    }
    static void PhysicsPayloadControls()
    {
        var errors = new List<string>();
        foreach (var pair in new[] { new object[] { new Collision(), "gameObject" }, new object[] { new Collision(), "transform" },
            new object[] { new ControllerColliderHit(), "gameObject" }, new object[] { new ControllerColliderHit(), "transform" },
            new object[] { new ControllerColliderHit(), "rigidbody" } })
        {
            try { pair[0].GetType().GetProperty((string)pair[1]).GetValue(pair[0], null); errors.Add("returned"); }
            catch (Exception error) { errors.Add(Error(error)); }
        }
        CoreValue("Physics/payload/defaultErrors", errors.ToArray());
        var first = new GameObject("CollisionControl3D"); var second = new GameObject("CollisionTarget3D");
        var previousMode = Physics.simulationMode;
        try
        {
            Physics.simulationMode = SimulationMode.Script;
            first.AddComponent<Rigidbody>().useGravity = false; first.AddComponent<BoxCollider>();
            var observer = first.AddComponent<PhysicsPayloadControl>();
            second.transform.position = new Vector3(.5f,0,0); second.AddComponent<BoxCollider>();
            Physics.SyncTransforms(); Physics.Simulate(.02f);
            CoreValue("Physics/payload/collision", observer.collision ?? new object[] { "missing" });
        }
        finally { UnityEngine.Object.DestroyImmediate(first); UnityEngine.Object.DestroyImmediate(second); Physics.simulationMode = previousMode; }
        var mover = new GameObject("ControllerPayloadControl"); var target = new GameObject("ControllerPayloadTarget");
        try
        {
            mover.transform.position = new Vector3(100,0,0); target.transform.position = new Vector3(102,0,0);
            target.AddComponent<BoxCollider>();
            var observer = mover.AddComponent<PhysicsPayloadControl>(); var controller = mover.AddComponent<CharacterController>();
            Physics.SyncTransforms(); controller.Move(new Vector3(3,0,0));
            CoreValue("Physics/payload/controller", observer.controller ?? new object[] { "missing" });
        }
        finally { UnityEngine.Object.DestroyImmediate(mover); UnityEngine.Object.DestroyImmediate(target); }
        var jointOwner = new GameObject("DrivePrecisionControl");
        try
        {
            jointOwner.SetActive(false); var joint = jointOwner.AddComponent<ConfigurableJoint>();
            var drive = joint.xDrive; float value = drive.maximumForce;
            CoreValue("Physics/drive/precision", new object[] { BitConverter.ToInt32(BitConverter.GetBytes(value),0),
                BitConverter.ToInt32(BitConverter.GetBytes(float.MaxValue),0), value == float.MaxValue });
            drive.maximumForce = float.MaxValue; joint.xDrive = drive;
            CoreValue("Physics/drive/maximumWrite", BitConverter.ToInt32(BitConverter.GetBytes(joint.xDrive.maximumForce),0));
            drive.maximumForce = 27.5f; joint.xDrive = drive;
            CoreValue("Physics/drive/finiteWrite", joint.xDrive.maximumForce);
            foreach (var name in new[] { "xDrive", "yDrive", "zDrive", "angularXDrive", "angularYZDrive", "slerpDrive" })
            {
                var property = typeof(ConfigurableJoint).GetProperty(name); var values = new List<object>();
                foreach (var input in new[] { -3f, 27.5f, float.MaxValue, float.PositiveInfinity, float.NaN })
                {
                    var setting = new JointDrive { positionSpring = input, positionDamper = input, maximumForce = input, useAcceleration = true };
                    property.SetValue(joint, setting, null); var result = (JointDrive)property.GetValue(joint, null);
                    values.Add(new object[] { result.positionSpring, result.positionDamper, result.maximumForce, result.useAcceleration });
                }
                CoreValue("Physics/drive/range/" + name, values.ToArray());
            }

        }
        finally { UnityEngine.Object.DestroyImmediate(jointOwner); }
    }
    static void RemovedGetterControls()
    {
        foreach (var populated in new[] { false, true })
        {
            var owner = new GameObject("RemovedGetterControl");
            try
            {
                if (populated) owner.AddComponent<Rigidbody>();
                var prefix = "RemovedGetterControl/" + (populated ? "populated" : "empty");
                Emit(prefix + "/GetComponent", "value", (owner.GetComponent<Rigidbody>() != null).ToString());
                foreach (var target in new object[] { owner, owner.transform })
                {
                    var type = target is GameObject ? typeof(GameObject) : typeof(Component);
                    foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                        .OrderBy(p => p.Name, StringComparer.Ordinal))
                    {
                        var obsolete = (ObsoleteAttribute)Attribute.GetCustomAttribute(property, typeof(ObsoleteAttribute));
                        if (obsolete == null || property.PropertyType != typeof(Component)) continue;
                        var key = prefix + "/" + type.Name + "/" + property.Name;
                        try { property.GetValue(target, null); Emit(key, "value", "returned"); }
                        catch (Exception error)
                        {
                            while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                            Emit(key, "getter-threw", error.GetType().FullName + ":" + error.Message);
                        }
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
    }
    static void Progress(string key)
    { if (!string.IsNullOrEmpty(progress)) File.WriteAllText(progress,key); }
    static string Id(Type type)
    {
        if(type.IsArray)return Id(type.GetElementType())+"["+new string(',',type.GetArrayRank()-1)+"]";
        if(type.IsGenericType)return type.GetGenericTypeDefinition().FullName+"<"+string.Join(",",type.GetGenericArguments().Select(Id))+">";
        return type.FullName??type.Name;
    }
    static string Error(Exception error)
    {
        while(error is TargetInvocationException && error.InnerException!=null)error=error.InnerException;
        return error.GetType().FullName;
    }
    static MemberInfo[] Members(Type type)
    {
        MemberInfo[] result;
        if(members.TryGetValue(type,out result))return result;
        result=type.GetFields(BindingFlags.Instance|BindingFlags.Public).Cast<MemberInfo>()
            .Concat(type.GetProperties(BindingFlags.Instance|BindingFlags.Public))
            .OrderBy(m=>Id(m.DeclaringType)+"/"+m.MemberType+"/"+m.Name,StringComparer.Ordinal).ToArray();
        members[type]=result;return result;
    }
    static object Read(MemberInfo member,object target)
    { return member is FieldInfo?((FieldInfo)member).GetValue(target):((PropertyInfo)member).GetValue(target,null); }
    static Type MemberType(MemberInfo member)
    { return member is FieldInfo?((FieldInfo)member).FieldType:((PropertyInfo)member).PropertyType; }
    static void Capture(Candidate candidate)
    {
        var factory=candidate.type+"/@factory";
        if(candidate.status!="candidate") { Emit(factory,"excluded",candidate.status);return; }
        Progress(factory);
        object value=null;GameObject owner=null;
        try
        {
            var type=Type.GetType(candidate.type+", "+candidate.module,false);
            if(type==null) { Emit(factory,"missing-type",candidate.module);return; }
            value=Create(type,candidate.category,out owner);
            if(ReferenceEquals(value,null)) { Emit(factory,"factory-null","");return; }
            Emit(factory,"value",candidate.category);
            foreach(var member in Members(type))
            {
                var key=candidate.type+"/"+member.MemberType+"/"+Id(member.DeclaringType)+"/"+member.Name;
                if(member is PropertyInfo indexed && indexed.GetIndexParameters().Length>0)
                    key+="("+string.Join(",",indexed.GetIndexParameters().Select(parameter=>Id(parameter.ParameterType)))+")";
                var property=member as PropertyInfo;
                if(property!=null && (property.GetGetMethod()==null || property.GetIndexParameters().Length!=0))
                { Emit(key,"not-readable",property.GetGetMethod()==null?"no-public-getter":"indexer-requires-arguments");continue; }
                if (type.FullName == "UnityEngine.ModifiableContactPair" && property != null
                    && (property.Name == "colliderInstanceID" || property.Name == "otherColliderInstanceID" || property.Name == "massProperties"))
                { Emit(key,"requires-context","a live contact-modification callback payload");continue; }
                if (type.FullName == "UnityEngine.Rendering.LocalKeyword" && property != null
                    && (property.Name == "isDynamic" || property.Name == "isOverridable" || property.Name == "type"))
                { Emit(key,"requires-context","a keyword initialized from a shader keyword space");continue; }
                if (type.FullName == "UnityEngine.Rendering.LocalKeywordSpace" && property != null)
                { Emit(key,"requires-context","a keyword space obtained from a shader or compute shader");continue; }
                if (property != null && IsIntegratedDescriptor(type))
                { Emit(key,"requires-context","an integrated subsystem descriptor returned by SubsystemManager");continue; }
                Progress(key);
                // Each readable member observes a new instance, so an allocating or stateful
                // getter cannot change the next member's creation-time observation.
                try
                {
                    if (ReferenceEquals(value,null)) value=Create(type,candidate.category,out owner);
                    if (ReferenceEquals(value,null)) { Emit(key,"factory-null","");continue; }
                    object answer;
                    try { answer=Read(member,value); }
                    catch(Exception error) { Emit(key,"getter-threw",Error(error));continue; }
                    try { Emit(key,"value",Format(answer,MemberType(member),new HashSet<object>(identity),0)); }
                    catch(Exception error) { Emit(key,"format-threw",Error(error)); }
                }
                catch(Exception error) { Emit(key,"factory-threw",Error(error)); }
                finally { Release(value,owner);value=null;owner=null; }

            }
        }
        catch(Exception error) { Emit(factory,"factory-threw",Error(error)); }
        finally { Release(value,owner); }
    }
    static object Create(Type type,string category,out GameObject owner)
    {
        owner=null;
        if(category=="component")
        {
            owner=new GameObject("default-"+type.FullName);
            return owner.AddComponent(type);
        }
        return category=="scriptable"?ScriptableObject.CreateInstance(type):Activator.CreateInstance(type);
    }
    static void Release(object value,GameObject owner)
    {
        if(owner!=null)UnityEngine.Object.DestroyImmediate(owner);
        else if(value is UnityEngine.Object)UnityEngine.Object.DestroyImmediate((UnityEngine.Object)value);
        else if(value is IDisposable)try { ((IDisposable)value).Dispose(); }catch(Exception){}
    }
    static bool IsIntegratedDescriptor(Type type)
    {
        for(var current=type;current!=null;current=current.BaseType)
            if(current.FullName=="UnityEngine.IntegratedSubsystemDescriptor")return true;
        return false;
    }
    static string MemberId(MemberInfo member)
    {
        var prefix=member.MemberType+":"+(member.DeclaringType==null?"":Id(member.DeclaringType)+".")+member.Name;
        var method=member as MethodBase;
        if(method!=null)
        {
            prefix+=(method.IsGenericMethod?"<"+string.Join(",",method.GetGenericArguments().Select(Id))+">":"")
                +"("+string.Join(",",method.GetParameters().Select(parameter=>Id(parameter.ParameterType)))+")";
            if(method is MethodInfo)prefix+=":"+Id(((MethodInfo)method).ReturnType);
        }
        return prefix;
    }
    static string Format(object value,Type declared,HashSet<object> seen,int depth)
    {
        if(ReferenceEquals(value,null))return "null";
        if(value is string)return "string:"+(string)value;
        if(value is bool)return (bool)value?"true":"false";
        if(value is float)return ((float)value).ToString("G9",CultureInfo.InvariantCulture);
        if(value is double)return ((double)value).ToString("G17",CultureInfo.InvariantCulture);
        var type=value.GetType();
        if(type.IsEnum)return "enum:"+((Enum)value).ToString("D");
        if(value is Type)return "type:"+Id((Type)value);
        if(value is MemberInfo)return MemberId((MemberInfo)value);
        if(value is Assembly)return "assembly:"+((Assembly)value).GetName().Name;
        if(value is Module)return "module:"+((Module)value).Name;
        if(value is DateTime)return "date:"+((DateTime)value).Ticks+":"+((DateTime)value).Kind;
        if(value is TimeSpan)return "duration:"+((TimeSpan)value).Ticks;
        if(value is Guid)return "guid:"+((Guid)value).ToString("D");
        if(value is IntPtr)return (IntPtr)value==IntPtr.Zero?"null-pointer":"non-null-pointer";
        if(value is UIntPtr)return (UIntPtr)value==UIntPtr.Zero?"null-pointer":"non-null-pointer";
        if(type.IsPrimitive || value is decimal)return Convert.ToString(value,CultureInfo.InvariantCulture);
        if(value is UnityEngine.Object)
        {
            var obj=(UnityEngine.Object)value;
            string name;
            try { name=obj.name; }catch(Exception e) { name="<"+Error(e)+">"; }
            return "object:"+Id(type)+":"+name;
        }
        if(depth>=5)return "<depth-limit:"+Id(declared)+">";
        if(!type.IsValueType && !seen.Add(value))return "<cycle:"+Id(declared)+">";
        try
        {
            if(value is Delegate)
            {
                return "delegate:["+string.Join(";",((Delegate)value).GetInvocationList().Select(callback=>
                    MemberId(callback.Method)+";target="+Format(callback.Target,callback.Target==null?typeof(object):callback.Target.GetType(),seen,depth+1)))+"]";
            }
            if(value is IEnumerable)
            {
                var text=new StringBuilder("[");bool first=true;
                foreach(var element in (IEnumerable)value)
                { if(!first)text.Append(',');first=false;text.Append(Format(element,element==null?typeof(object):element.GetType(),seen,depth+1)); }
                return text.Append(']').ToString();
            }
            var contract=declared.IsInterface?declared:type;
            var fields=contract.GetFields(BindingFlags.Public|BindingFlags.Instance);
            var allFields=contract.GetFields(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance);
            var selected=type.IsValueType&&fields.Length>0&&fields.Length==allFields.Length?fields.Cast<MemberInfo>().ToArray():Members(contract);
            var result=new StringBuilder(Id(declared)+"{");bool initial=true;
            foreach(var member in selected)
            {
                var property=member as PropertyInfo;
                if(property!=null&&(property.GetGetMethod()==null||property.GetIndexParameters().Length!=0))continue;
                if(!initial)result.Append(';');initial=false;result.Append(member.Name).Append('=');
                try { result.Append(Format(Read(member,value),MemberType(member),seen,depth+1)); }
                catch(Exception e) { result.Append('<').Append(Error(e)).Append('>'); }
            }
            return result.Append('}').ToString();
        }
        finally { if(!type.IsValueType)seen.Remove(value); }
    }
}

public sealed class FactoryControlLogHandler : ILogHandler
{
    readonly ILogHandler previous;
    public int errors;
    public FactoryControlLogHandler(ILogHandler previous) { this.previous = previous; }
    public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args)
    { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++; previous.LogFormat(type, context, format, args); }
    public void LogException(Exception error, UnityEngine.Object context)
    { errors++; previous.LogException(error, context); }
}

public sealed class FreshStateControl
{
    int state;
    public int field;
    public int AChanges => ++state;
    public int ZReads => state;
    public NestedStateDerived ZNested => new NestedStateDerived();
}

public class NestedStateBase
{
    int answer=7;
    public int Answer => answer;
}
public sealed class NestedStateDerived : NestedStateBase
{
    public int field=2;
}
