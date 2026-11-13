using UnityEngine.UIElements;
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
    static bool core2Waiting;
    void Update()
    {
        for (int step=0;step<4 && index<candidates.Length;step++,index++) Capture(candidates[index]);
        if (index<candidates.Length) return;
        if (!core2Waiting) { Core2Begin(); Core2BeginMore(); core2Waiting = true; return; }
        if (!core2Resource.isDone || !core2Bundle.isDone || !core2Instantiation.isDone || !core2MissingResource.isDone) return;
        Capture(new Candidate { type=typeof(FreshStateControl).FullName, module=typeof(FreshStateControl).Assembly.GetName().Name,
            category="class", status="candidate" });
        try
        {
            RemovedGetterControls();
            CoreControls();
            Physics2DControls();
            Physics2DPayloadControls();
            PhysicsControls();
            AudioControls();
            ParticleModuleControls();
            PhysicsPayloadControls();
            EditorImporterControls();
            UiControls();
            Core2Controls(); Core2MoreControls(); Core2TimeConstructorControl(); Core2MeshControls(); Core2LineCurveControls(); Core2RenderSettingControls(); Core2BindingControls(); Core2LayoutMutationControls();
            Emit("complete","value","1"); output.Dispose(); output=null;
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Debug.LogException(error); output?.Dispose(); output=null;
            EditorApplication.Exit(1);
        }
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
    { Emit("CoreControl/" + key, "value", Format(value, value == null ? typeof(object) : value.GetType(), new HashSet<object>(identity), 0)); }
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
    static void AudioControls()
    {
        foreach (var provider in new[] { "empty", "AudioSource", "AudioListener" })
        {
            foreach (var filter in new[] { "AudioLowPassFilter", "AudioHighPassFilter", "AudioEchoFilter", "AudioDistortionFilter", "AudioChorusFilter", "AudioReverbFilter" })
            {
                var owner = new GameObject("AudioRequirementControl");
                try
                {
                    owner.SetActive(false);
                    if (provider != "empty") owner.AddComponent(ProbeType(provider));
                    var value = owner.AddComponent(ProbeType(filter));
                    CoreValue("Audio/filter/" + provider + "/" + filter, new object[] { value != null,
                        owner.GetComponents<Component>().Select(c => c.GetType().Name).OrderBy(n => n, StringComparer.Ordinal).ToArray() });
                }
                finally { UnityEngine.Object.DestroyImmediate(owner); }
            }
            var requiredOwner = new GameObject("RequiredAudioControl");
            try
            {
                requiredOwner.SetActive(false);
                if (provider != "empty") requiredOwner.AddComponent(ProbeType(provider));
                CoreValue("Audio/base/" + provider, requiredOwner.AddComponent(ProbeType("AudioBehaviour")) == null);
                CoreValue("Audio/required/" + provider, requiredOwner.AddComponent<AudioRequiredControlBehaviour>() != null);
            }
            finally { UnityEngine.Object.DestroyImmediate(requiredOwner); }
        }
        var zoneOwner = new GameObject("ReverbRemovedSettingControl");
        var previous = Debug.unityLogger.logHandler; var handler = new RemovedAudioSettingLogHandler(previous);
        try
        {
            zoneOwner.SetActive(false); var zone = zoneOwner.AddComponent<AudioReverbZone>();
            var removed = typeof(AudioReverbZone).GetProperty("roomRolloffFactor");
            Debug.unityLogger.logHandler = handler;
            removed.SetValue(zone, 2.5f, null); var first = removed.GetValue(zone, null);
            zone.reverbPreset = AudioReverbPreset.Cave;
            removed.SetValue(zone, -4f, null); var second = removed.GetValue(zone, null);
            CoreValue("Audio/removedReverbSetting", new object[] { first, second, handler.warnings.ToArray() });
        }
        finally { Debug.unityLogger.logHandler = previous; UnityEngine.Object.DestroyImmediate(zoneOwner); }
    }
    static void ParticleModuleControls()
    {
        var owner = new GameObject("ParticleModuleControl"); owner.SetActive(false);
        var system = owner.AddComponent<ParticleSystem>();
        CoreValue("Particles/defaultInitialization", new object[] { system.main.emitterVelocityMode,
            system.collision.enableInteriorCollisions, system.textureSheetAnimation.rowMode,
            system.textureSheetAnimation.spriteCount, system.textureSheetAnimation.GetSprite(0) == null });
        CoreValue("Particles/defaultLinearCurves", new object[] { system.sizeOverLifetime.x, system.sizeBySpeed.x,
            system.lifetimeByEmitterSpeed.curve, system.noise.remap, system.textureSheetAnimation.frameOverTime });
        var noise = system.noise; var initialQuality = noise.quality; noise.quality = ParticleSystemNoiseQuality.High;
        CoreValue("Particles/noiseQuality", new object[] { initialQuality, noise.quality });
        var field = owner.AddComponent<ParticleSystemForceField>();
        var initialLength = field.length; field.length = 3.5f;
        CoreValue("Particles/forceFieldLength", new object[] { initialLength, field.length });
        var subEmitters = system.subEmitters;
        foreach (var property in typeof(ParticleSystem.SubEmittersModule).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(ParticleSystem)).OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            try { property.GetValue(subEmitters, null); Emit("CoreControl/Particles/removed/" + property.Name + "/get", "value", "returned"); }
            catch (Exception error) { Emit("CoreControl/Particles/removed/" + property.Name + "/get", "getter-threw", Error(error)); }
            try { property.SetValue(subEmitters, system, null); Emit("CoreControl/Particles/removed/" + property.Name + "/set", "value", "returned"); }
            catch (Exception error) { Emit("CoreControl/Particles/removed/" + property.Name + "/set", "setter-threw", Error(error)); }
        }
        CoreValue("Particles/removedPreservesCount", system.subEmitters.subEmittersCount);
        var sheet = system.textureSheetAnimation;
        try { sheet.AddSprite(null); Emit("CoreControl/Particles/addNullSprite", "value", "returned"); }
        catch (Exception error) { Emit("CoreControl/Particles/addNullSprite", "setter-threw", Error(error) + ":" + error.Message); }
        var emptyCount = sheet.spriteCount;
        var texture = new Texture2D(1, 1); var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
        try
        {
            sheet.AddSprite(sprite); var firstCount = sheet.spriteCount; var first = sheet.GetSprite(0) == sprite;
            sheet.AddSprite(sprite); var secondCount = sheet.spriteCount;
            sheet.RemoveSprite(1);
            try { sheet.SetSprite(0, null); Emit("CoreControl/Particles/setNullSprite", "value", "returned"); }
            catch (Exception error) { Emit("CoreControl/Particles/setNullSprite", "setter-threw", Error(error) + ":" + error.Message); }
            var replaced = sheet.spriteCount == 1 && sheet.GetSprite(0) == sprite;
            sheet.RemoveSprite(0); var removed = sheet.spriteCount; sheet.AddSprite(sprite);
            CoreValue("Particles/spriteSlots", new object[] { emptyCount, firstCount, first, secondCount, replaced, removed, sheet.spriteCount });
        }
        finally { UnityEngine.Object.DestroyImmediate(sprite); UnityEngine.Object.DestroyImmediate(texture); }
        var collider = owner.AddComponent<BoxCollider>();
        foreach (var populated in new[] { false, true })
        {
            object collisionEvent = default(ParticleCollisionEvent);
            if (populated) typeof(ParticleCollisionEvent).GetField("m_ColliderInstanceID", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(collisionEvent, collider.GetInstanceID());
            try { typeof(ParticleCollisionEvent).GetProperty("collider").GetValue(collisionEvent, null); }
            catch (Exception error)
            {
                while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                Emit("CoreControl/Particles/removedCollider/" + populated, "getter-threw", error.GetType().FullName + ":" + error.Message);
            }
            CoreValue("Particles/colliderComponent/" + populated,
                ((ParticleCollisionEvent)collisionEvent).colliderComponent == (populated ? collider : null));
        }
        var main = system.main; main.startSpeed = 13f; main.loop = false;
        var shape = system.shape; shape.radius = 2.75f;
        var copied = main; copied.startSpeedMultiplier = 17f;
        CoreValue("Particles/attached", new object[] { main.startSpeed, system.main.startSpeedMultiplier, main.loop, shape.radius });
        var handles = new object[] { system.main, system.emission, system.shape, system.velocityOverLifetime,
            system.limitVelocityOverLifetime, system.inheritVelocity, system.lifetimeByEmitterSpeed, system.forceOverLifetime,
            system.colorOverLifetime, system.colorBySpeed, system.sizeOverLifetime, system.sizeBySpeed,
            system.rotationOverLifetime, system.rotationBySpeed, system.externalForces, system.noise,
            system.collision, system.trigger, system.subEmitters, system.textureSheetAnimation, system.lights,
            system.trails, system.customData };
        UnityEngine.Object.DestroyImmediate(owner);
        foreach (var handle in handles)
        {
            var type = handle.GetType();
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var key = "CoreControl/Particles/destroyed/" + type.Name + "/" + property.Name;
                try { Emit(key, "value", Format(property.GetValue(handle, null), property.PropertyType, new HashSet<object>(identity), 0)); }
                catch (Exception error) { Emit(key, "getter-threw", Error(error)); }
            }
        }
        CoreValue("Particles/defaultIsolated", default(ParticleSystem.MainModule).startSpeed);
    }
    static ResourceRequest core2Resource;
    static AssetBundleCreateRequest core2Bundle;
    static AsyncInstantiateOperation<GameObject> core2Instantiation;
    static GameObject core2Original;
    static void Core2Begin()
    {
        core2Resource = Resources.LoadAsync<TextAsset>("CoreDefaultStateControl");
        var old = Debug.unityLogger.logHandler;
        try
        {
            Debug.unityLogger.logHandler = new FactoryControlLogHandler(old);
            core2Bundle = AssetBundle.LoadFromMemoryAsync(new byte[] { 0, 1, 2, 3 });
        }
        finally { Debug.unityLogger.logHandler = old; }
        core2Original = new GameObject("CoreAsyncDefaultControl"); core2Original.SetActive(false);
        core2Instantiation = UnityEngine.Object.InstantiateAsync(core2Original);
    }
    sealed class Core2ManagedRequest : ResourceRequest
    {
        protected override UnityEngine.Object GetResult() => null;
    }
    static object Core2Read(Func<object> read)
    {
        try { return read(); } catch (Exception error) { return Error(error); }
    }
    static object Core2Write(Action write)
    {
        try { write(); return "ok"; } catch (Exception error) { return Error(error); }
    }
    static void Core2Controls()
    {
        foreach (var op in new AsyncOperation[] { new AsyncOperation(), new ResourceRequest(),
            new AssetBundleCreateRequest(), new AssetBundleRequest(), new AsyncInstantiateOperation(), new Core2ManagedRequest(), new AssetBundleRecompressOperation(), new AssetBundleUnloadOperation() })
        {
            string key = "Core2/async/bare/" + op.GetType().Name;
            CoreValue(key, new object[] { Core2Read(() => op.isDone), Core2Read(() => op.progress),
                Core2Read(() => op.priority), Core2Read(() => op.allowSceneActivation),
                Core2Write(() => op.priority = 7), Core2Write(() => op.allowSceneActivation = false) });
            Action<AsyncOperation> handler = _ => {};
            CoreValue(key + "/callbacks", new object[] { Core2Write(() => op.completed += handler), Core2Write(() => op.completed -= handler) });
            if (op is AssetBundleRecompressOperation)
                foreach (var property in op.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    CoreValue(key + "/" + property.Name, Core2Read(() => property.GetValue(op, null)));
            if (op is AsyncInstantiateOperation instantiate)
                CoreValue(key + "/methods", new[] { Core2Read(() => instantiate.IsWaitingForSceneActivation()),
                    Core2Write(() => instantiate.WaitForCompletion()), Core2Write(() => instantiate.Cancel()) });
            if (op is ResourceRequest request) CoreValue(key + "/asset", Core2Read(() => request.asset));
            if (op is AssetBundleCreateRequest bundle) CoreValue(key + "/bundle", Core2Read(() => bundle.assetBundle));
            if (op is AssetBundleRequest assets) CoreValue(key + "/all", Core2Read(() => assets.allAssets));
        }
        foreach (var op in new AsyncOperation[] { core2Resource, core2Bundle, core2Instantiation })
        {
            if (!op.isDone) throw new InvalidOperationException("Core default operation did not complete.");
            int callbacks = 0; bool same = false;
            op.completed += value => { callbacks++; same = ReferenceEquals(value, op); };
            op.priority = 9; op.allowSceneActivation = false;
            CoreValue("Core2/async/factory/" + op.GetType().Name, new object[] { op.isDone, op.progress,
                op.priority, op.allowSceneActivation, callbacks, same });
            op.allowSceneActivation = true;
        }
        CoreValue("Core2/async/resourceAsset", new object[] { core2Resource.asset != null, ((TextAsset)core2Resource.asset).text });
        CoreValue("Core2/async/failedBundle", core2Bundle.assetBundle == null);
        CoreValue("Core2/async/instantiated", new object[] { core2Instantiation.Result.Length, core2Instantiation.Result[0].activeSelf });
        foreach (var item in core2Instantiation.Result) UnityEngine.Object.DestroyImmediate(item);
        UnityEngine.Object.DestroyImmediate(core2Original);
        Core2Renderers();
        Core2CameraAndLight();
        Core2EditModeControls();
    }
    static void Core2Renderers()
    {
        var root = new GameObject("CoreRendererState"); root.SetActive(false);
        try
        {
            foreach (var type in new[] { typeof(MeshRenderer), typeof(SkinnedMeshRenderer), typeof(SpriteRenderer),
                typeof(LineRenderer), typeof(TrailRenderer), typeof(BillboardRenderer), typeof(ParticleSystemRenderer),
                Type.GetType("UnityEngine.Tilemaps.TilemapRenderer, UnityEngine.TilemapModule", true) })
            {
                var go = new GameObject(type.Name); go.transform.SetParent(root.transform);
                var renderer = type == typeof(ParticleSystemRenderer) ? go.AddComponent<ParticleSystem>().GetComponent<ParticleSystemRenderer>() : (Renderer)go.AddComponent(type);
                CoreValue("Core2/renderer/" + type.Name + "/initial", new object[] { renderer.isLOD0,
                    renderer.shadowCastingMode, renderer.receiveShadows, renderer.lightProbeUsage,
                    renderer.reflectionProbeUsage, renderer.motionVectorGenerationMode,
                    renderer.rayTracingAccelerationStructureBuildFlags, renderer.rayTracingMode });
                foreach (var alias in new[] { "castShadows", "motionVectors", "useLightProbes" })
                {
                    var property = typeof(Renderer).GetProperty(alias);
                    var initial = property.GetValue(renderer, null);
                    property.SetValue(renderer, false, null); var disabled = property.GetValue(renderer, null);
                    property.SetValue(renderer, true, null);
                    CoreValue("Core2/renderer/" + type.Name + "/" + alias, new[] { initial, disabled, property.GetValue(renderer, null) });
                }
            }
            var group = root.AddComponent<LODGroup>();
            CoreValue("Core2/lod/initial", group.GetLODs());
            var a = root.GetComponentInChildren<MeshRenderer>(true);
            var b = root.GetComponentInChildren<SkinnedMeshRenderer>(true);
            group.SetLODs(new[] { new LOD(.8f, new Renderer[] { a }), new LOD(.2f, new Renderer[] { b }) });
            CoreValue("Core2/lod/distinct", new[] { a.isLOD0, b.isLOD0 });
            group.SetLODs(new[] { new LOD(.8f, new Renderer[] { a }), new LOD(.2f, new Renderer[] { a, b }) });
            CoreValue("Core2/lod/repeated", new[] { a.isLOD0, b.isLOD0 });
            group.enabled = false; CoreValue("Core2/lod/disabled", new[] { a.isLOD0, b.isLOD0 });
            group.SetLODs(Array.Empty<LOD>()); CoreValue("Core2/lod/removed", new object[] { a.isLOD0, b.isLOD0, group.lodCount });
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    static void Core2CameraAndLight()
    {
        var go = new GameObject("CoreOpticalState"); go.SetActive(false);
        try
        {
            var camera = go.AddComponent<Camera>();
            foreach (var name in new[] { "aperture", "barrelClipping", "bladeCount", "curvature", "focusDistance", "iso",
                "renderCloudsInSceneView", "shutterSpeed", "stereoActiveEye", "stereoTargetEye" })
            {
                var p = typeof(Camera).GetProperty(name);
                CoreValue("Core2/camera/" + name + "/initial", p.GetValue(camera, null));
                if (!p.CanWrite) continue;
                var values = p.PropertyType == typeof(bool) ? new object[] { false, true }
                    : p.PropertyType == typeof(Vector2) ? new object[] { new Vector2(-3, 100), new Vector2(4, 7), new Vector2(7, 4) }
                    : p.PropertyType == typeof(int) ? new object[] { -3, 0, 4, 1000 }
                    : p.PropertyType.IsEnum ? new object[] { Enum.ToObject(p.PropertyType, 0), Enum.ToObject(p.PropertyType, 1) }
                    : new object[] { -3f, 0f, 4f, 1000f, float.NaN, float.PositiveInfinity };
                for (int i = 0; i < values.Length; i++) CoreValue("Core2/camera/" + name + "/write/" + i,
                    new[] { Core2Write(() => p.SetValue(camera, values[i], null)), Core2Read(() => p.GetValue(camera, null)) });
            }
            var light = go.AddComponent<Light>();
            foreach (var name in new[] { "attenuate", "enableSpotReflector", "lightUnit", "lightmappingMode", "luxAtDistance", "shadowSoftness", "shadowSoftnessFade" })
            {
                var p = typeof(Light).GetProperty(name); CoreValue("Core2/light/" + name + "/initial", p.GetValue(light, null));
                var values = p.PropertyType == typeof(bool) ? new object[] { false, true }
                    : p.PropertyType.IsEnum ? new object[] { Enum.ToObject(p.PropertyType, 0), Enum.ToObject(p.PropertyType, 7) }
                    : new object[] { -3f, 0f, 4f, float.NaN };
                for (int i = 0; i < values.Length; i++) CoreValue("Core2/light/" + name + "/write/" + i,
                    new[] { Core2Write(() => p.SetValue(light, values[i], null)), Core2Read(() => p.GetValue(light, null)) });
            }

        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    static void Core2EditModeControls()
    {
        foreach (var name in new[] { "DefaultStatePlainEditControl", "DefaultStateAlwaysEditControl",
            "DefaultStateInheritedEditControl", "DefaultStateInEditControl" })
        {
            var type = typeof(DefaultStateProbe).Assembly.GetType(name);
            var owner = new GameObject("edit-mode-control"); owner.SetActive(false);
            try
            {
                var attached = (MonoBehaviour)owner.AddComponent(type);
                Core2EditModeState("Core2/editMode/attached/" + name, attached);
                CoreValue("Core2/editMode/attachedCtor/" + name, type.GetField("constructorRunMode").GetValue(attached));
                var manual = (MonoBehaviour)Activator.CreateInstance(type);
                try { Core2EditModeState("Core2/editMode/manual/" + name, manual);
                    CoreValue("Core2/editMode/manualCtor/" + name, type.GetField("constructorRunMode").GetValue(manual)); }
                finally { if (manual != null) UnityEngine.Object.DestroyImmediate(manual); }
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }
    }
    static void Core2EditModeState(string key, MonoBehaviour target)
    {
        try
        {
            var initial = target.runInEditMode; target.runInEditMode = true;
            var enabled = target.runInEditMode; target.runInEditMode = false;
            CoreValue(key, new object[] { initial, enabled, target.runInEditMode });
        }
        catch (Exception error) { CoreValue(key, Error(error)); }
    }
    static GameObject core2LodRoot;
    static Renderer core2LodFirst, core2LodSecond;
    static LODGroup core2LodGroup;
    static ResourceRequest core2MissingResource;
    static void Core2BeginMore()
    {
        core2MissingResource = Resources.LoadAsync<TextAsset>("NoSuchCoreDefaultStateControl");
        core2LodRoot = new GameObject("ActiveCoreLodControl"); core2LodRoot.SetActive(false);
        var a = new GameObject("first"); a.transform.SetParent(core2LodRoot.transform);
        var b = new GameObject("second"); b.transform.SetParent(core2LodRoot.transform);
        core2LodFirst = a.AddComponent<MeshRenderer>(); core2LodSecond = b.AddComponent<MeshRenderer>();
        core2LodGroup = core2LodRoot.AddComponent<LODGroup>();
        core2LodGroup.SetLODs(new[] { new LOD(.8f, new[] { core2LodFirst }), new LOD(.2f, new[] { core2LodSecond }) });
        core2LodRoot.SetActive(true);
    }
    sealed class Core2ExceptionLogHandler : ILogHandler
    {
        readonly ILogHandler before;
        internal int errors;
        internal string lastException;
        internal Core2ExceptionLogHandler(ILogHandler before) { this.before = before; }
        public void LogException(Exception error, UnityEngine.Object context)
        { errors++; lastException = error.GetType().FullName; before.LogException(error,context); }
        public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args)
        { before.LogFormat(type,context,format,args); }
    }
    static void Core2TimeConstructorControl()
    {
        var owner = new GameObject("TimeConstructorControl"); owner.SetActive(false);
        var before = Debug.unityLogger.logHandler; var handler = new Core2ExceptionLogHandler(before);
        try
        {
            Debug.unityLogger.logHandler = handler;
            DefaultStateTimeFailingEditControl result = null;
            var error = Core2Write(() => result = owner.AddComponent<DefaultStateTimeFailingEditControl>());
            CoreValue("Core2/editMode/timeFailingCtor", new object[] { error, result != null,
                handler.errors, handler.lastException, Time.time >= 0 });
            owner.SetActive(true);
            CoreValue("Core2/editMode/timeFailingCtorCallbacks", new object[] { result != null && result.awakened,
                handler.errors, handler.lastException });
        }
        finally { Debug.unityLogger.logHandler = before; UnityEngine.Object.DestroyImmediate(owner); }
    }
    static object[] Core2MeshState(Mesh mesh) => new object[] { mesh.vertexCount, mesh.vertexAttributeCount,
        mesh.vertexBufferCount, mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Position) };
    static void Core2MeshControls()
    {
        var mesh = new Mesh();
        try
        {
            CoreValue("Core2/mesh/new", Core2MeshState(mesh));
            mesh.vertices = Array.Empty<Vector3>(); CoreValue("Core2/mesh/emptyVertices", Core2MeshState(mesh));
            mesh.vertices = new[] { Vector3.zero, Vector3.one }; CoreValue("Core2/mesh/populated", Core2MeshState(mesh));
            var read = Mesh.AcquireReadOnlyMeshData(mesh); var data = read[0];
            CoreValue("Core2/mesh/dataRead", new object[] { data.vertexCount, data.vertexBufferCount, data.subMeshCount, data.indexFormat });
            CoreValue("Core2/mesh/readOnlyWriteRejected", Core2Write(() => data.SetVertexBufferParams(3,
                new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position))));
            read.Dispose();
            CoreValue("Core2/mesh/dataDisposed", Core2Read(() => data.vertexCount));
            mesh.Clear(); CoreValue("Core2/mesh/clearRetainingLayout", Core2MeshState(mesh));
            mesh.Clear(false); CoreValue("Core2/mesh/clearDroppingLayout", Core2MeshState(mesh));
            mesh.SetVertexBufferParams(0, new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position));
            CoreValue("Core2/mesh/explicitEmpty", Core2MeshState(mesh));
            CoreValue("Core2/mesh/streamGapRejected", Core2Write(() => mesh.SetVertexBufferParams(3,
                new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position,
                    UnityEngine.Rendering.VertexAttributeFormat.Float32, 3, 2))));
            CoreValue("Core2/mesh/afterInvalidStream", Core2MeshState(mesh));
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
        var empty = default(Mesh.MeshData);
        CoreValue("Core2/mesh/defaultData", new[] { Core2Read(() => empty.vertexCount), Core2Read(() => empty.vertexBufferCount),
            Core2Read(() => empty.subMeshCount), Core2Read(() => empty.indexFormat) });
        var writable = Mesh.AllocateWritableMeshData(1);
        try
        {
            var data = writable[0];
            CoreValue("Core2/mesh/writableEmpty", new object[] { data.vertexCount, data.vertexBufferCount, data.subMeshCount, data.indexFormat });
            data.SetVertexBufferParams(2, new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position));
            CoreValue("Core2/mesh/writablePopulated", new object[] { data.vertexCount, data.vertexBufferCount, data.subMeshCount, data.indexFormat });
        }
        finally { writable.Dispose(); }
    }
    static void Core2LineCurveControls()
    {
        var root = new GameObject("CoreLineCurveControl"); root.SetActive(false);
        try
        {
            foreach (var type in new[] { typeof(LineRenderer), typeof(TrailRenderer) })
            {
                var owner = new GameObject(type.Name); owner.transform.SetParent(root.transform);
                var renderer = owner.AddComponent(type); var property = type.GetProperty("widthCurve");
                var initial = (AnimationCurve)property.GetValue(renderer, null);
                CoreValue("Core2/line/" + type.Name + "/initial", initial);
                initial.MoveKey(0, new Keyframe(0, 7));
                CoreValue("Core2/line/" + type.Name + "/getterCopy", ((AnimationCurve)property.GetValue(renderer, null)).Evaluate(0));
                var assigned = new AnimationCurve(new Keyframe(0, 2), new Keyframe(1, 4));
                property.SetValue(renderer, assigned, null); assigned.MoveKey(0, new Keyframe(0, 9));
                CoreValue("Core2/line/" + type.Name + "/setterCopy", ((AnimationCurve)property.GetValue(renderer, null)).Evaluate(0));
                CoreValue("Core2/line/" + type.Name + "/null", Core2Write(() => property.SetValue(renderer, null, null)));
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        CoreValue("Core2/line/publicKeyframe", new Keyframe(0, 1));
    }
    static void Core2RenderSettingControls()
    {
        var go = new GameObject("CoreRenderSettingControl"); go.SetActive(false);
        try
        {
            var renderer = go.AddComponent<MeshRenderer>();
            foreach (int flag in new[] { 0, 6, 129, -1 })
            {
                renderer.rayTracingAccelerationStructureBuildFlags = (UnityEngine.Rendering.RayTracingAccelerationStructureBuildFlags)flag;
                CoreValue("Core2/renderer/flags/" + flag, new object[] { renderer.rayTracingAccelerationStructureBuildFlags,
                    renderer.rayTracingAccelerationStructureBuildFlagsOverride });
            }
            renderer.rayTracingAccelerationStructureBuildFlagsOverride = true;
            CoreValue("Core2/renderer/flagsOverride", new object[] { renderer.rayTracingAccelerationStructureBuildFlags,
                renderer.rayTracingAccelerationStructureBuildFlagsOverride });
            foreach (int mode in new[] { 0, 1, 3, 7, -3, -1, 4, 5, 255 })
            {
                renderer.rayTracingMode = (UnityEngine.Experimental.Rendering.RayTracingMode)mode;
                CoreValue("Core2/renderer/rayMode/" + mode, renderer.rayTracingMode);
            }
            renderer.stitchLightmapSeams = false;
            CoreValue("Core2/renderer/stitchOff", renderer.stitchLightmapSeams);
            renderer.stitchLightmapSeams = true;
            CoreValue("Core2/renderer/stitchOn", renderer.stitchLightmapSeams);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    static void Core2MoreControls()
    {
        CoreValue("Core2/lod/activeAfterUpdates", new[] { core2LodFirst.isLOD0, core2LodSecond.isLOD0 });
        core2LodGroup.ForceLOD(1);
        CoreValue("Core2/lod/forced", new[] { core2LodFirst.isLOD0, core2LodSecond.isLOD0 });
        core2LodGroup.SetLODs(new[] { new LOD(.8f, new[] { core2LodFirst, core2LodSecond }),
            new LOD(.2f, new[] { core2LodSecond }) });
        CoreValue("Core2/lod/activeRepeated", new[] { core2LodFirst.isLOD0, core2LodSecond.isLOD0 });
        core2LodGroup.enabled = false;
        CoreValue("Core2/lod/activeDisabled", new[] { core2LodFirst.isLOD0, core2LodSecond.isLOD0 });
        UnityEngine.Object.DestroyImmediate(core2LodGroup);
        CoreValue("Core2/lod/groupDestroyed", new[] { core2LodFirst.isLOD0, core2LodSecond.isLOD0 });
        UnityEngine.Object.DestroyImmediate(core2LodRoot);
        core2MissingResource.allowSceneActivation = false; core2MissingResource.priority = -2;
        CoreValue("Core2/async/missingResource", new object[] { core2MissingResource.isDone, core2MissingResource.progress,
            core2MissingResource.asset == null, core2MissingResource.priority, core2MissingResource.allowSceneActivation });
        var owner = new GameObject("NestedComponentConstructor"); owner.SetActive(false);
        try
        {
            var added = owner.AddComponent<DefaultStateNestedEditControl>();
            var nested = added.nested;
            CoreValue("Core2/editMode/nestedCtor", new object[] { added.constructorRunMode,
                nested.constructorRunMode, nested == null, nested.GetInstanceID() == 0,
                Core2Read(() => nested.name), Core2Read(() => nested.hideFlags) });
            var before = Debug.unityLogger.logHandler; var logs = new FactoryControlLogHandler(before);
            try
            {
                Debug.unityLogger.logHandler = logs;
                DefaultStateFailingEditControl failed = null;
                var result = Core2Write(() => failed = owner.AddComponent<DefaultStateFailingEditControl>());
                CoreValue("Core2/editMode/failingCtor", new object[] { result, failed != null,
                    failed == null ? false : failed.entered, logs.errors });
                owner.SetActive(true);
                CoreValue("Core2/editMode/failingCtorCallbacks", new object[] { failed != null && failed.awakened,
                    failed != null && failed.enabledCallback, logs.errors });
            }
            finally { Debug.unityLogger.logHandler = before; }
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); }
    }
    static void Core2BindingControls()
    {
        var go = new GameObject("CoreBindingControl"); go.SetActive(false);
        try
        {
            var attached = go.AddComponent<DefaultStatePlainEditControl>();
            CoreValue("Core2/bindings/constructor", attached.constructorBindings);
            CoreValue("Core2/bindings/attachedInactive", new[] { attached.useGUILayout, attached.didAwake, attached.didStart });
            attached.useGUILayout = false; CoreValue("Core2/bindings/layoutWrite", attached.useGUILayout);
            go.SetActive(true);
            CoreValue("Core2/bindings/attachedAwake", new[] { attached.didAwake, attached.didStart });
            var old = Debug.unityLogger.logHandler;
            try
            {
                Debug.unityLogger.logHandler = new FactoryControlLogHandler(old);
                var manual = new DefaultStatePlainEditControl();
                CoreValue("Core2/bindings/manual", new[] { Core2Read(() => manual.useGUILayout), Core2Read(() => manual.didAwake), Core2Read(() => manual.didStart), Core2Write(() => manual.useGUILayout = false) });
            }
            finally { Debug.unityLogger.logHandler = old; }
            var camera = go.AddComponent<Camera>(); camera.aperture = 3.2f; camera.focusDistance = 17; camera.curvature = new Vector2(7, 4); camera.iso = 731;
            var other = new GameObject("CoreCameraCopy");
            try
            {
                var copied = other.AddComponent<Camera>(); copied.CopyFrom(camera);
                CoreValue("Core2/camera/copied", new object[] { copied.aperture, copied.focusDistance, copied.curvature, copied.iso });
            }
            finally { UnityEngine.Object.DestroyImmediate(other); }
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    static void Core2LayoutMutationControls()
    {
        var mesh = new Mesh();
        try
        {
            mesh.SetVertexBufferParams(3, new UnityEngine.Rendering.VertexAttributeDescriptor(UnityEngine.Rendering.VertexAttribute.Position));
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
            CoreValue("Core2/meshLayout/addedNormals", new object[] { mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal), mesh.vertexAttributeCount, mesh.GetVertexBufferStride(0) });
            mesh.normals = null; mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }; mesh.triangles = new[] { 0, 1, 2 }; mesh.RecalculateNormals();
            CoreValue("Core2/meshLayout/recalculated", new object[] { mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal), mesh.normals });
            mesh.tangents = new Vector4[3]; mesh.colors32 = new Color32[3]; mesh.uv = new Vector2[3]; mesh.Clear(true);
            CoreValue("Core2/meshLayout/retained", new object[] { mesh.vertexAttributeCount, mesh.vertexBufferCount, mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal), mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Color), mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0) });
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            CoreValue("Core2/meshLayout/repopulated", new object[] { mesh.vertexAttributeCount, mesh.normals.Length, mesh.colors32.Length, mesh.uv.Length });
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up };
            mesh.colors32 = new[] { new Color32(1, 2, 3, 4), new Color32(5, 6, 7, 8), new Color32(9, 10, 11, 12) };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up };
            CoreValue("Core2/meshLayout/repopulatedValues", new object[] { mesh.normals, mesh.colors32, mesh.uv });
            mesh.uv2 = new Vector2[3]; CoreValue("Core2/meshLayout/addedUV", new object[] { mesh.vertexAttributeCount, mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord1) });
            mesh.normals = null; CoreValue("Core2/meshLayout/removedNormals", new object[] { mesh.vertexAttributeCount, mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Normal) });
            mesh.Clear(false); CoreValue("Core2/meshLayout/removedLayout", new object[] { mesh.vertexAttributeCount, mesh.vertexBufferCount });
        }
        finally { UnityEngine.Object.DestroyImmediate(mesh); }
    }
    static void UiControls()
    {
        foreach (var descriptor in new UnityEngine.UIElements.UxmlAttributeDescription[] {
            new UnityEngine.UIElements.UxmlBoolAttributeDescription(), new UnityEngine.UIElements.UxmlColorAttributeDescription(),
            new UnityEngine.UIElements.UxmlDoubleAttributeDescription(), new UnityEngine.UIElements.UxmlFloatAttributeDescription(),
            new UnityEngine.UIElements.UxmlHash128AttributeDescription(), new UnityEngine.UIElements.UxmlIntAttributeDescription(),
            new UnityEngine.UIElements.UxmlLongAttributeDescription(), new UnityEngine.UIElements.UxmlStringAttributeDescription(),
            new UnityEngine.UIElements.UxmlUnsignedIntAttributeDescription(), new UnityEngine.UIElements.UxmlUnsignedLongAttributeDescription() })
        {
            var initial = descriptor.use;
            descriptor.use = UnityEngine.UIElements.UxmlAttributeDescription.Use.Required;
            var changed = descriptor.use;
            descriptor.use = UnityEngine.UIElements.UxmlAttributeDescription.Use.None;
            CoreValue("UI/attribute/" + descriptor.GetType().Name + "/use", new object[] { initial, changed, descriptor.use });
        }
        var color = new UnityEngine.UIElements.UxmlColorAttributeDescription();
        CoreValue("UI/color/default", new object[] { color.defaultValue, color.defaultValueAsString });
        color.defaultValue = new Color(-0.25f, 0.5f, 1.5f, 0.125f);
        CoreValue("UI/color/written", new object[] { color.defaultValue, color.defaultValueAsString });
        var imgui = new UnityEngine.UIElements.IMGUIContainer();
        CoreValue("UI/imgui/default", new object[] { imgui.focusable, imgui.contextType });
        imgui.focusable = false; imgui.contextType = UnityEngine.UIElements.ContextType.Player;
        CoreValue("UI/imgui/written", new object[] { imgui.focusable, imgui.contextType });
        imgui.Dispose();
        var slider = new UnityEngine.UIElements.MinMaxSlider();
        CoreValue("UI/minmax/default", slider.pickingMode);
        slider.pickingMode = UnityEngine.UIElements.PickingMode.Position;
        CoreValue("UI/minmax/written", slider.pickingMode);
        var radio = new UnityEngine.UIElements.RadioButtonGroup();
        CoreValue("UI/radio/container", new object[] { ReferenceEquals(radio, radio.contentContainer), radio.contentContainer.focusable,
            radio.contentContainer.name, radio.contentContainer.childCount });
        foreach (var text in new UnityEngine.UIElements.TextElement[] { new UnityEngine.UIElements.TextElement(),
            new UnityEngine.UIElements.Label(), new UnityEngine.UIElements.Button(), new UnityEngine.UIElements.RepeatButton() })
        {
            text.text = "logical";
            var field = typeof(UnityEngine.UIElements.TextElement).GetField("m_RenderedText", BindingFlags.Instance | BindingFlags.NonPublic);
            CoreValue("UI/text/" + text.GetType().Name + "/identity", ReferenceEquals(text, text.experimental));
            text.experimental.SetRenderedText("painted");
            CoreValue("UI/text/" + text.GetType().Name + "/override", new object[] { text.text, field.GetValue(text) });
            text.text = "next";
            CoreValue("UI/text/" + text.GetType().Name + "/restored", new object[] { text.text, field.GetValue(text) });
        }
        UiAtlasControls();
        UiPointerControls();
        UiJsonControls();
        UiAdditionalControls();
    }
    static void UiAtlasControls()
    {
        var empty = new UnityEngine.UIElements.DynamicAtlasSettings();
        CoreValue("UI/atlas/new", AtlasValues(empty));
        var first = UnityEngine.UIElements.DynamicAtlasSettings.defaults;
        var second = UnityEngine.UIElements.DynamicAtlasSettings.defaults;
        CoreValue("UI/atlas/defaults", AtlasValues(first));
        CoreValue("UI/atlas/fresh", ReferenceEquals(first, second));
        first.minAtlasSize = -2; first.maxAtlasSize = 17; first.maxSubTextureSize = 3;
        first.activeFilters = (UnityEngine.UIElements.DynamicAtlasFilters)129;
        first.customFilter = UiAtlasFilter;
        CoreValue("UI/atlas/written", AtlasValues(first));
        CoreValue("UI/atlas/isolated", AtlasValues(second));
        var encoded = JsonUtility.ToJson(first);
        CoreValue("UI/atlas/serialized", encoded);
        CoreValue("UI/atlas/restored", AtlasValues(JsonUtility.FromJson<UnityEngine.UIElements.DynamicAtlasSettings>(encoded)));
        var a = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
        var b = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
        try
        {
            CoreValue("UI/atlas/panelDefault", AtlasValues(a.dynamicAtlasSettings));
            var saved = a.dynamicAtlasSettings;
            saved.minAtlasSize = 128;
            CoreValue("UI/atlas/panelOwnership", new object[] { ReferenceEquals(saved, a.dynamicAtlasSettings),
                ReferenceEquals(saved, b.dynamicAtlasSettings), a.dynamicAtlasSettings.minAtlasSize, b.dynamicAtlasSettings.minAtlasSize });
            a.dynamicAtlasSettings = first;
            CoreValue("UI/atlas/panelAssigned", new object[] { ReferenceEquals(first, a.dynamicAtlasSettings), a.dynamicAtlasSettings.maxAtlasSize });
            a.dynamicAtlasSettings = null;
            CoreValue("UI/atlas/panelNull", a.dynamicAtlasSettings == null);
        }
        finally { UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b); }
    }
    static object[] AtlasValues(UnityEngine.UIElements.DynamicAtlasSettings settings)
        => new object[] { settings.minAtlasSize, settings.maxAtlasSize, settings.maxSubTextureSize, settings.activeFilters, settings.customFilter != null };
    static bool UiAtlasFilter(Texture2D texture, ref UnityEngine.UIElements.DynamicAtlasFilters flags)
    { flags = UnityEngine.UIElements.DynamicAtlasFilters.Size; return true; }
    static void UiPointerControls()
    {
        foreach (var pointer in new[] { "unknown", "pen", "touch" })
        {
            var e = UnityEngine.UIElements.PointerMoveEvent.GetPooled();
            SetPointer(e, "pointerType", pointer == "unknown" ? UnityEngine.UIElements.PointerType.unknown : pointer);
            SetPointer(e, "tilt", new Vector2(0.2f, -0.3f));
            CoreValue("UI/pointer/" + pointer + "/first", PointerAngles(e));
            SetPointer(e, "tilt", new Vector2(-0.4f, 0.1f));
            CoreValue("UI/pointer/" + pointer + "/cached", PointerAngles(e));
            SetPointer(e, "altitudeAngle", 0.7f); SetPointer(e, "azimuthAngle", 1.2f);
            CoreValue("UI/pointer/" + pointer + "/anglesWritten", PointerAngles(e));
            e.Dispose();
            var reset = UnityEngine.UIElements.PointerMoveEvent.GetPooled();
            CoreValue("UI/pointer/" + pointer + "/reset", PointerAngles(reset));
            reset.Dispose();
        }
        var a = new UnityEngine.UIElements.PointerMoveEvent();
        var b = new UnityEngine.UIElements.PointerMoveEvent();
        var c = new UnityEngine.UIElements.PointerDownEvent();
        CoreValue("UI/eventIds/relations", new object[] { a.eventTypeId == b.eventTypeId,
            a.eventTypeId == UnityEngine.UIElements.PointerMoveEvent.TypeId(), a.eventTypeId != c.eventTypeId });
        a.Dispose(); b.Dispose(); c.Dispose();
    }
    static void SetPointer(UnityEngine.UIElements.PointerMoveEvent e, string name, object value)
        => e.GetType().GetProperty(name).SetValue(e, value, null);
    static object[] PointerAngles(UnityEngine.UIElements.PointerMoveEvent e)
        => new object[] { e.altitudeAngle, e.azimuthAngle, e.tilt };
    [Serializable] sealed class UiJsonText { public string text; public string[] items; }
    static void UiJsonControls()
    {
        var inputs = new[] { "ordinary", "\0", "before\0after", "trailing\0", "literal\\0", "é🤍\u0001" };
        for (var i = 0; i < inputs.Length; i++)
        {
            var data = new UiJsonText { text = inputs[i], items = new[] { inputs[i], "tail" } };
            CoreValue("UI/json/write/" + i, JsonUtility.ToJson(data));
            CoreValue("UI/json/editorWrite/" + i, UnityEditor.EditorJsonUtility.ToJson(data));
        }
        foreach (var input in new[] { "before\\u0000after", "\\u0000", "literal\\\\0" })
        {
            var json = "{\"text\":\"" + input + "\"}";
            var parsed = JsonUtility.FromJson<UiJsonText>(json);
            CoreValue("UI/json/read/" + input, parsed.text.Select(c => (int)c).ToArray());
            parsed.text = "previous"; JsonUtility.FromJsonOverwrite(json, parsed);
            CoreValue("UI/json/overwrite/" + input, parsed.text.Select(c => (int)c).ToArray());
        }
    }
    sealed class UiContentHost : VisualElement
    {
        readonly VisualElement container;
        public UiContentHost() { container = new VisualElement(); hierarchy.Add(container); }
        public override VisualElement contentContainer => container;
    }
    static void UiListHierarchyControls()
    {
        var list = new ListView(new List<string> { "one", "two" }, 20);
        list.showFoldoutHeader = true; list.showAddRemoveFooter = true;
        var foldout = list.Q<Foldout>(BaseListView.foldoutHeaderUssClassName);
        var scroll = list.Q<ScrollView>();
        var footer = list.Q(BaseListView.footerUssClassName);
        CoreValue("UI/hierarchy/listUnattached", new[] { ReferenceEquals(scroll.parent, foldout),
            ReferenceEquals(scroll.hierarchy.parent, foldout.contentContainer),
            ReferenceEquals(footer.parent, foldout.contentContainer), ReferenceEquals(footer.hierarchy.parent, foldout.contentContainer) });
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        var owner = new GameObject("UiHierarchyPanel"); owner.SetActive(false);
        try
        {
            var document = owner.AddComponent<UIDocument>(); document.panelSettings = settings;
            owner.SetActive(true); document.rootVisualElement.Add(list);
            CoreValue("UI/hierarchy/listAttached", new[] { ReferenceEquals(scroll.parent, foldout),
                ReferenceEquals(scroll.hierarchy.parent, foldout.contentContainer),
                ReferenceEquals(footer.parent, foldout.contentContainer), ReferenceEquals(footer.hierarchy.parent, foldout.contentContainer) });
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); UnityEngine.Object.DestroyImmediate(settings); }
    }
    static void UiHierarchyControls()
    {
        var host = new UiContentHost(); var leaf = new VisualElement(); host.Add(leaf);
        CoreValue("UI/hierarchy/add", new[] { ReferenceEquals(leaf.parent, host),
            ReferenceEquals(leaf.hierarchy.parent, host.contentContainer), host.childCount == 1 });
        var inserted = new VisualElement(); host.Insert(0, inserted);
        CoreValue("UI/hierarchy/insert", new[] { ReferenceEquals(inserted.parent, host),
            ReferenceEquals(inserted.hierarchy.parent, host.contentContainer), ReferenceEquals(host[0], inserted) });
        var direct = new VisualElement(); host.hierarchy.Add(direct);
        CoreValue("UI/hierarchy/direct", new[] { ReferenceEquals(direct.parent, host), ReferenceEquals(direct.hierarchy.parent, host) });
        var other = new UiContentHost(); other.Add(leaf);
        CoreValue("UI/hierarchy/reparent", new[] { ReferenceEquals(leaf.parent, other),
            ReferenceEquals(leaf.hierarchy.parent, other.contentContainer), host.childCount == 1 });
        leaf.RemoveFromHierarchy();
        CoreValue("UI/hierarchy/remove", new[] { leaf.parent == null, leaf.hierarchy.parent == null, other.childCount == 0 });
    }
    static void UiPointerMatrixControls()
    {
        int index = 0;
        foreach (var pair in new[] { new Vector2(.7f,1.2f), new Vector2(-.4f,.1f), new Vector2(1,0),
            new Vector2(0,1), new Vector2(Mathf.PI/2,Mathf.PI/2), new Vector2(.1f,-1.7f),
            new Vector2(2.3f,3.7f), new Vector2(-2.7f,-.9f), new Vector2(.001f,.001f) })
        {
            var e = new PointerMoveEvent();
            SetPointer(e,"pointerType",UnityEngine.UIElements.PointerType.touch);
            SetPointer(e,"altitudeAngle",pair.x); SetPointer(e,"azimuthAngle",pair.y);
            CoreValue("UI/pointer/matrix/" + index++, PointerAngles(e));
            e.Dispose();
        }
    }
    static void UiAdditionalControls()
    {
        UiHierarchyControls();
        UiListHierarchyControls();
        UiPointerMatrixControls();
        var touch = new Touch { fingerId = 0, phase = TouchPhase.Moved, altitudeAngle = 0.7f, azimuthAngle = 1.2f,
            position = new Vector2(3, 4), pressure = 1f, maximumPossiblePressure = 1f };
        var fromTouch = UnityEngine.UIElements.PointerMoveEvent.GetPooled(touch);
        try
        {
            CoreValue("UI/pointer/fromTouch", PointerAngles(fromTouch));
            var copy = UnityEngine.UIElements.PointerMoveEvent.GetPooled((UnityEngine.UIElements.IPointerEvent)fromTouch);
            try { CoreValue("UI/pointer/fromTouchCopy", PointerAngles(copy)); }
            finally { copy.Dispose(); }
        }
        finally { fromTouch.Dispose(); }
        var pen = new PenData { tilt = new Vector2(0.2f, -0.3f), pressure = 0.75f };
        var fromPen = UnityEngine.UIElements.PointerMoveEvent.GetPooled(pen);
        try { CoreValue("UI/pointer/fromPen", PointerAngles(fromPen)); }
        finally { fromPen.Dispose(); }
        var calls = 0; Action action = () => calls++;
        var container = new UnityEngine.UIElements.IMGUIContainer(action);
        CoreValue("UI/imgui/actionConstructor", new object[] { container.focusable, container.contextType,
            ReferenceEquals(action, container.onGUIHandler), calls });
        container.onGUIHandler = null;
        CoreValue("UI/imgui/actionCleared", container.onGUIHandler == null);
        container.Dispose();
        var radio = new UnityEngine.UIElements.RadioButtonGroup("group", new List<string> { "a", "b" });
        var manual = new UnityEngine.UIElements.RadioButton(); radio.Add(manual);
        CoreValue("UI/radio/choiceAndManual", new object[] { radio.contentContainer.childCount,
            radio.Query<UnityEngine.UIElements.RadioButton>().ToList().Count, ReferenceEquals(manual.parent, radio.contentContainer), radio.value });
        radio.choices = new[] { "c" };
        CoreValue("UI/radio/replacedChoices", new object[] { radio.contentContainer.childCount,
            radio.Query<UnityEngine.UIElements.RadioButton>().ToList().Count, ReferenceEquals(manual.parent, radio.contentContainer), radio.value });
        var data = new UiJsonText { text = "previous" };
        UnityEditor.EditorJsonUtility.FromJsonOverwrite("{\"text\":\"before\\u0000after\"}", data);
        CoreValue("UI/json/editorOverwrite", data.text.Select(c => (int)c).ToArray());
    }
    static void EditorImporterControls()
    {
        EditorSmallWrapperControls();
        var previousHandler = Debug.unityLogger.logHandler;
        var failureHandler = new FactoryControlLogHandler(previousHandler);
        try
        {
            Debug.unityLogger.logHandler = failureHandler;
            var failed = UnityEditor.AssetImporter.GetAtPath("Assets/DefaultStateControl.defaultstatefail");
            CoreValue("Editor/importer/failedConstructor", new object[] { failed != null,
                failed == null ? false : (bool)failed.GetType().GetField("entered").GetValue(failed),
                failed == null ? false : failed.GetInstanceID() != 0, failureHandler.errors });
        }
        finally { Debug.unityLogger.logHandler = previousHandler; }
        var scriptedType = UnityEditor.AssetImporter.GetAtPath("Assets/DefaultStateControl.defaultstate").GetType();
        var windowType = scriptedType.Assembly.GetType("DefaultStateWindow");
        var basicWindow = ScriptableObject.CreateInstance<UnityEditor.EditorWindow>();
        var customWindow = (UnityEditor.EditorWindow)ScriptableObject.CreateInstance(windowType);
        try
        {
            CoreValue("Editor/window/base", new object[] { basicWindow.minSize, basicWindow.position, basicWindow.depthBufferBits,
                typeof(UnityEditor.EditorWindow).GetProperty("antiAlias").GetValue(basicWindow, null), basicWindow.titleContent.text,
                basicWindow.saveChangesMessage, basicWindow.hideFlags });
            CoreValue("Editor/window/customConstructor", windowType.GetField("constructorValues").GetValue(customWindow));
            CoreValue("Editor/window/customAwake", windowType.GetField("awakeValues").GetValue(customWindow));
            windowType.GetMethod("ChangeDefaults").Invoke(customWindow, null);
            CoreValue("Editor/window/customWritten", new object[] { customWindow.minSize, customWindow.position, customWindow.depthBufferBits,
                typeof(UnityEditor.EditorWindow).GetProperty("antiAlias").GetValue(customWindow, null), customWindow.titleContent.text,
                customWindow.saveChangesMessage, customWindow.hideFlags });
        }
        finally { UnityEngine.Object.DestroyImmediate(basicWindow); UnityEngine.Object.DestroyImmediate(customWindow); }

        foreach (var type in new[] { typeof(UnityEditor.AssetImporter), typeof(UnityEditor.TextureImporter),
            typeof(UnityEditor.MonoImporter), typeof(UnityEditor.PluginImporter), scriptedType,
            typeof(UnityEditor.AudioImporter), typeof(UnityEditor.VideoClipImporter) })
        {
            var importer = (UnityEditor.AssetImporter)Activator.CreateInstance(type);
            CoreValue("Editor/importer/bare/" + type.Name + "/identity", new object[] { importer == null, importer.GetInstanceID() });
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                var key = "CoreControl/Editor/importer/bare/" + type.Name + "/" + property.Name;
                try { Emit(key, "value", Format(property.GetValue(importer, null), property.PropertyType, new HashSet<object>(identity), 0)); }
                catch (Exception error) { Emit(key, "getter-threw", Error(error)); }
            }
            if (type == scriptedType)
            {
                CoreValue("Editor/importer/manualConstructor", new object[] { type.GetField("constructorState").GetValue(importer),
                    type.GetField("nestedConstructorState").GetValue(importer) });
                CoreValue("Editor/importer/manualFieldInitializer", type.GetField("fieldInitializerState").GetValue(importer));
            }
            if (type == typeof(UnityEditor.TextureImporter)) RemovedTextureSettings(importer, "bare");
            if (type == typeof(UnityEditor.AudioImporter) || type == typeof(UnityEditor.VideoClipImporter)) RemovedMediaImporterSettings(importer);
            UnityEngine.Object.DestroyImmediate(importer);
        }
        foreach (var path in new[] { "Assets/Scripts/FactoryControlBehaviour.cs", "Assets/DefaultStateControl.png", "Assets/DefaultStateControl.defaultstate" })
        {
            var importer = UnityEditor.AssetImporter.GetAtPath(path);
            CoreValue("Editor/importer/bound/" + path, new object[] { importer.GetType().Name, importer.assetPath,
                importer.name, importer.hideFlags, importer.userData, importer.GetInstanceID() != 0, importer != null });
            importer.userData = "changed-control";
            CoreValue("Editor/importer/written/" + path, new object[] { importer.userData,
                ReferenceEquals(importer, UnityEditor.AssetImporter.GetAtPath(path)) });
            importer.userData = null; importer.assetBundleName = null; importer.assetBundleVariant = null;
            CoreValue("Editor/importer/nullStrings/" + path, new object[] { importer.userData, importer.assetBundleName, importer.assetBundleVariant });
            if (importer is UnityEditor.TextureImporter) RemovedTextureSettings(importer, "bound");
            if (importer.GetType() == scriptedType)
            {
                CoreValue("Editor/importer/boundConstructor", new object[] { scriptedType.GetField("constructorState").GetValue(importer),
                    scriptedType.GetField("nestedConstructorState").GetValue(importer) });
                CoreValue("Editor/importer/boundConstructorMessage", scriptedType.GetField("constructorMessage").GetValue(importer));
                CoreValue("Editor/importer/boundFieldInitializer", scriptedType.GetField("fieldInitializerState").GetValue(importer));
            }
        }
    }
    [AttributeUsage(AttributeTargets.All)]
    sealed class EmptyEditorControlAttribute : Attribute { }
    static void EditorSmallWrapperControls()
    {
        foreach (var item in new[] {
            new KeyValuePair<string, Func<int>>("types", () => default(UnityEditor.TypeCache.TypeCollection).Count),
            new KeyValuePair<string, Func<int>>("methods", () => default(UnityEditor.TypeCache.MethodCollection).Count),
            new KeyValuePair<string, Func<int>>("fields", () => default(UnityEditor.TypeCache.FieldInfoCollection).Count) })
        {
            var key = "CoreControl/Editor/collection/default/" + item.Key;
            try { Emit(key, "value", item.Value().ToString(CultureInfo.InvariantCulture)); }
            catch (Exception error) { Emit(key, "getter-threw", Error(error)); }
        }
        CoreValue("Editor/collection/empty/types", UnityEditor.TypeCache.GetTypesWithAttribute<EmptyEditorControlAttribute>().Count);
        CoreValue("Editor/collection/empty/methods", UnityEditor.TypeCache.GetMethodsWithAttribute<EmptyEditorControlAttribute>().Count);
        CoreValue("Editor/collection/empty/fields", UnityEditor.TypeCache.GetFieldsWithAttribute<EmptyEditorControlAttribute>().Count);
        CoreValue("Editor/collection/live/types", UnityEditor.TypeCache.GetTypesDerivedFrom<ScriptableObject>().Count > 0);
        CoreValue("Editor/collection/live/methods", UnityEditor.TypeCache.GetMethodsWithAttribute<ObsoleteAttribute>().Count > 0);
        CoreValue("Editor/collection/live/fields", UnityEditor.TypeCache.GetFieldsWithAttribute<ObsoleteAttribute>().Count > 0);
        var processor = new UnityEditor.AssetPostprocessor();
        CoreValue("Editor/postprocessor/default", processor.assetPath);
        processor.assetPath = "Assets/explicit.asset"; CoreValue("Editor/postprocessor/written", processor.assetPath);
        processor.assetPath = ""; CoreValue("Editor/postprocessor/empty", processor.assetPath);
        processor.assetPath = null; CoreValue("Editor/postprocessor/null", processor.assetPath);
        object identifier = default(UnityEditor.Build.Content.ObjectIdentifier);
        var path = identifier.GetType().GetProperty("filePath");
        CoreValue("Editor/identifier/default", path.GetValue(identifier, null));
        path.SetValue(identifier, "explicit-path", null); CoreValue("Editor/identifier/written", path.GetValue(identifier, null));
        path.SetValue(identifier, null, null); CoreValue("Editor/identifier/null", path.GetValue(identifier, null));
    }
    static void RemovedMediaImporterSettings(UnityEditor.AssetImporter importer)
    {
        var names = importer is UnityEditor.AudioImporter
            ? new[] { "hardware", "channels", "compressionBitrate", "loopable", "threeD" }
            : new[] { "linearColor", "useLegacyImporter" };
        foreach (var name in names)
        {
            var property = importer.GetType().GetProperty(name);
            foreach (var changed in new[] { false, true })
            {
                object value = property.PropertyType == typeof(bool) ? (object)changed
                    : property.PropertyType.IsEnum ? Enum.ToObject(property.PropertyType, changed ? 77 : -42) : (object)(changed ? 77 : -42);
                property.SetValue(importer, value, null);
                CoreValue("Editor/importer/removed/" + importer.GetType().Name + "/" + name + "/" + changed, property.GetValue(importer, null));
            }
        }
        if (importer is UnityEditor.AudioImporter)
            foreach (var name in new[] { "durationMS", "frequency", "origChannelCount", "origIsCompressible", "origIsMonoForcable", "defaultBitrate", "origType" })
            {
                var property = importer.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                CoreValue("Editor/importer/removed/internal/" + name, property.GetValue(importer, null));
            }
    }
    static void RemovedTextureSettings(UnityEditor.AssetImporter importer, string context)
    {
        foreach (var name in new[] { "correctGamma", "generateMipsInLinearSpace", "spritePackingTag" })
        {
            var property = importer.GetType().GetProperty(name);
            foreach (var changed in new[] { false, true })
            {
                property.SetValue(importer, property.PropertyType == typeof(bool) ? (object)changed : changed ? null : "changed-tag", null);
                CoreValue("Editor/importer/removed/" + context + "/" + name + "/" + changed, property.GetValue(importer, null));
            }
        }
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

public sealed class RemovedAudioSettingLogHandler : ILogHandler
{
    readonly ILogHandler previous;
    public readonly List<string> warnings = new List<string>();
    public RemovedAudioSettingLogHandler(ILogHandler previous) { this.previous = previous; }
    public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args)
    { if (type == LogType.Warning) warnings.Add(string.Format(format, args)); previous.LogFormat(type, context, format, args); }
    public void LogException(Exception error, UnityEngine.Object context) { previous.LogException(error, context); }
}
