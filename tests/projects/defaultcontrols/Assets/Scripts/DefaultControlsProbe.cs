using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Experimental.Rendering;
using UnityEngine.LowLevelPhysics;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

/// <summary>
/// Writes values.jsonl in $DEFAULTCONTROLS_OUT (tests/value_probe.py rows): controls for creation
/// defaults recorded by tests/projects/defaultstate, each set apart from the fresh value it
/// explains (a value set after creation, another context, an argument that takes another path).
/// Floats are written with "R"; an exception is written as "throws" with its type and message;
/// log lines a call writes are rows of their own (".log").
/// </summary>
public class DefaultControlsProbe : MonoBehaviour
{
    const string Variable = "DEFAULTCONTROLS_OUT";
    static StreamWriter output;
    static readonly List<string> s_Logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    /// <summary>Records the action's answer (or what it throws) and, as key.log, the log lines it
    /// wrote (only when it wrote any).</summary>
    static void Try(string key, Func<string> action)
    {
        s_Logs.Clear();
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
        if (s_Logs.Count > 0) Row(key + ".log", string.Join(" | ", s_Logs));
        s_Logs.Clear();
    }

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);
    static string Q(Quaternion q) => F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w);

    static string Positions(Vector3[] points)
    {
        if (points == null) return "null";
        var parts = new string[points.Length];
        for (int i = 0; i < points.Length; i++) parts[i] = "(" + V(points[i]) + ")";
        return points.Length + ":" + string.Join(";", parts);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("DefaultControlsProbe").AddComponent<DefaultControlsProbe>();
    }

    IEnumerator Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        Application.logMessageReceived += OnLog;
        Try("section.immediate", () =>
        {
            CommandBuffers();
            Formats();
            Descriptors();
            CullingMasks();
            Lights();
            Readbacks();
            Animators();
            return "done";
        });
        // Objects whose values may change once the engine has stepped: read new, after the next
        // physics step and after two more frames.
        Try("section.staged", () => { Stage(); return "made"; });
        Read("new");
        yield return new WaitForFixedUpdate();
        Read("fixed");
        yield return null;
        yield return null;
        Read("frame");
        Try("section.destinations", () => { Destinations(); return "set"; });
        Read("destination");
        yield return null;
        yield return null;
        Read("destinationFrame");
        Application.logMessageReceived -= OnLog;
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }

    static readonly List<KeyValuePair<string, Func<string>>> s_Staged = new List<KeyValuePair<string, Func<string>>>();

    static void Staged(string key, Func<string> read) => s_Staged.Add(new KeyValuePair<string, Func<string>>(key, read));

    static void Read(string stage)
    {
        foreach (var entry in s_Staged)
        {
            // Once moving, the agent's position follows the frame times (two Editor captures differ).
            if (stage == "destinationFrame" && entry.Key == "agent.destination") continue;
            Try(entry.Key + "." + stage, entry.Value);
        }
    }

    static GameObject Make(string name, Vector3 position, Vector3 scale, bool active = true)
    {
        var g = new GameObject(name);
        g.transform.position = position;
        g.transform.localScale = scale;
        if (!active) g.SetActive(false);
        return g;
    }

    /// <summary>Makes the staged objects: colliders (their geometry holders), NavMesh agents on
    /// and off a NavMesh built at run time, renderers whose bounds follow their geometry, a
    /// canvas, a tilemap and the 2D physics scene.</summary>
    static void Stage()
    {
        // Colliders, away from each other.
        var box = Make("GeomBox", new Vector3(-20, 30, 0), new Vector3(2, 3, 4)).AddComponent<BoxCollider>();
        box.size = new Vector3(1, 2, 3);
        box.center = new Vector3(1, 0, 0);
        Staged("geom.box", () => Geometry(box));
        var boxDefault = Make("GeomBoxDefault", new Vector3(-24, 30, 0), Vector3.one).AddComponent<BoxCollider>();
        Staged("geom.boxDefault", () => Geometry(boxDefault));
        var boxDisabled = Make("GeomBoxDisabled", new Vector3(-28, 30, 0), Vector3.one).AddComponent<BoxCollider>();
        boxDisabled.enabled = false;
        Staged("geom.boxDisabled", () => Geometry(boxDisabled));
        var boxInactive = Make("GeomBoxInactive", new Vector3(-32, 30, 0), Vector3.one, active: false).AddComponent<BoxCollider>();
        Staged("geom.boxInactive", () => Geometry(boxInactive));
        for (int direction = 0; direction < 3; direction++)
        {
            var capsule = Make("GeomCapsule" + direction, new Vector3(-20, 40 + direction * 6, 0), new Vector3(1, 2, 1)).AddComponent<CapsuleCollider>();
            capsule.radius = 0.5f;
            capsule.height = 3f;
            capsule.direction = direction;
            Staged("geom.capsule" + direction, () => Geometry(capsule));
        }
        var capsuleDefault = Make("GeomCapsuleDefault", new Vector3(-26, 40, 0), Vector3.one).AddComponent<CapsuleCollider>();
        Staged("geom.capsuleDefault", () => Geometry(capsuleDefault));
        var sphere = Make("GeomSphere", new Vector3(-20, 60, 0), new Vector3(2, 3, 4)).AddComponent<SphereCollider>();
        sphere.radius = 0.7f;
        Staged("geom.sphere", () => Geometry(sphere));
        var controller = Make("GeomController", new Vector3(-20, 70, 0), Vector3.one).AddComponent<CharacterController>();
        Staged("geom.controller", () => Geometry(controller));
        var meshless = Make("GeomMeshless", new Vector3(-20, 80, 0), Vector3.one).AddComponent<MeshCollider>();
        Staged("geom.meshless", () => Geometry(meshless));
        var primitive = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var cube = primitive.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(primitive);
        foreach (var convex in new[] { false, true })
        {
            var mesh = Make("GeomMesh" + convex, new Vector3(-20, 90 + (convex ? 6 : 0), 0), new Vector3(2, 3, 4)).AddComponent<MeshCollider>();
            mesh.convex = convex;
            mesh.sharedMesh = cube;
            Staged("geom.mesh." + (convex ? "convex" : "triangles"), () => Geometry(mesh));
        }
        var terrainData = new TerrainData();
        terrainData.heightmapResolution = 33;
        terrainData.size = new Vector3(10, 5, 10);
        var terrain = Make("GeomTerrain", new Vector3(-60, 30, 0), Vector3.one).AddComponent<TerrainCollider>();
        Staged("geom.terrainEmpty", () => Geometry(terrain));
        var terrainSet = Make("GeomTerrainSet", new Vector3(-80, 30, 0), Vector3.one).AddComponent<TerrainCollider>();
        terrainSet.terrainData = terrainData;
        Staged("geom.terrain", () => Geometry(terrainSet));
        var body = Make("GeomWheelBody", new Vector3(-20, 110, 0), Vector3.one).AddComponent<Rigidbody>();
        body.isKinematic = true;
        var wheelObject = new GameObject("GeomWheel");
        wheelObject.transform.SetParent(body.transform, false);
        var wheel = wheelObject.AddComponent<WheelCollider>();
        Staged("geom.wheel", () => Geometry(wheel));

        // Capsules on non-uniformly scaled objects, controllers and boxes and spheres on mirrored ones.
        foreach (var (name, direction, scale) in new[]
        {
            ("y312", 1, new Vector3(3, 2, 1)), ("y123", 1, new Vector3(1, 2, 3)), ("x123", 0, new Vector3(1, 2, 3)),
            ("z312", 2, new Vector3(3, 2, 1)), ("x321", 0, new Vector3(3, 2, 1)), ("z111", 2, Vector3.one),
        })
        {
            var capsule = Make("GeomCapsule." + name, new Vector3(-40, 40 + direction * 6, scale.x * 10 + scale.z), scale).AddComponent<CapsuleCollider>();
            capsule.radius = 0.5f;
            capsule.height = 3f;
            capsule.direction = direction;
            Staged("geom.capsule." + name, () => Geometry(capsule));
        }
        var capsuleShort = Make("GeomCapsuleShort", new Vector3(-46, 40, 0), new Vector3(1, 2, 1)).AddComponent<CapsuleCollider>();
        capsuleShort.radius = 1f;
        capsuleShort.height = 1f;
        Staged("geom.capsuleShort", () => Geometry(capsuleShort));
        var controllerBig = Make("GeomControllerBig", new Vector3(-40, 70, 0), Vector3.one).AddComponent<CharacterController>();
        controllerBig.radius = 1f;
        controllerBig.height = 4f;
        Staged("geom.controllerBig", () => Geometry(controllerBig));
        var controllerSkin = Make("GeomControllerSkin", new Vector3(-44, 70, 0), Vector3.one).AddComponent<CharacterController>();
        controllerSkin.skinWidth = 0.2f;
        Staged("geom.controllerSkin", () => Geometry(controllerSkin));
        var controllerScaled = Make("GeomControllerScaled", new Vector3(-48, 70, 0), new Vector3(2, 2, 2)).AddComponent<CharacterController>();
        Staged("geom.controllerScaled", () => Geometry(controllerScaled));
        var boxMirrored = Make("GeomBoxMirrored", new Vector3(-40, 80, 0), new Vector3(-2, 3, 4)).AddComponent<BoxCollider>();
        Staged("geom.boxMirrored", () => Geometry(boxMirrored));
        var sphereMirrored = Make("GeomSphereMirrored", new Vector3(-44, 80, 0), new Vector3(2, -3, 4)).AddComponent<SphereCollider>();
        sphereMirrored.radius = 0.7f;
        Staged("geom.sphereMirrored", () => Geometry(sphereMirrored));

        // A NavMesh of a 20 x 20 box, and agents on it, off it and disabled.
        var sources = new List<NavMeshBuildSource>
        {
            new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = new Vector3(20, 1, 20), transform = Matrix4x4.TRS(new Vector3(0, -0.5f, 0), Quaternion.identity, Vector3.one), area = 0 },
        };
        var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(0), sources, new Bounds(Vector3.zero, new Vector3(40, 10, 40)), Vector3.zero, Quaternion.identity);
        NavMesh.AddNavMeshData(data);
        var on = Make("AgentOn", new Vector3(0, 0, 0), Vector3.one).AddComponent<NavMeshAgent>();
        Staged("agent.on", () => Agent(on));
        var off = Make("AgentOff", new Vector3(100, 0, 100), Vector3.one).AddComponent<NavMeshAgent>();
        Staged("agent.off", () => Agent(off));
        var disabled = Make("AgentDisabled", new Vector3(3, 0, 3), Vector3.one).AddComponent<NavMeshAgent>();
        disabled.enabled = false;
        Staged("agent.disabled", () => Agent(disabled));
        s_DestinationAgent = Make("AgentDestination", new Vector3(-3, 0, -3), Vector3.one).AddComponent<NavMeshAgent>();
        Staged("agent.destination", () => Agent(s_DestinationAgent));
        s_OffDestinationAgent = Make("AgentOffDestination", new Vector3(100, 0, -100), Vector3.one).AddComponent<NavMeshAgent>();
        Staged("agent.offDestination", () => Agent(s_OffDestinationAgent));
        var obstacleCapsule = Make("ObstacleCapsule", new Vector3(-8, 0, 8), Vector3.one).AddComponent<NavMeshObstacle>();
        obstacleCapsule.shape = NavMeshObstacleShape.Capsule;
        Staged("obstacle.new", () => Obstacle(Make("ObstacleNew", new Vector3(8, 0, 8), Vector3.one).AddComponent<NavMeshObstacle>(), destroy: true));
        Staged("obstacle.capsule", () => Obstacle(obstacleCapsule, destroy: false));
        Staged("obstacle.setHeight", () =>
        {
            var o = Make("ObstacleHeight", new Vector3(8, 0, -8), Vector3.one).AddComponent<NavMeshObstacle>();
            o.height = 3f;
            var asBox = Obstacle(o, destroy: false);
            o.shape = NavMeshObstacleShape.Capsule;
            var asCapsule = Obstacle(o, destroy: false);
            o.shape = NavMeshObstacleShape.Box;
            var back = Obstacle(o, destroy: true);
            return asBox + " -> " + asCapsule + " -> " + back;
        });

        foreach (var shape in new[] { NavMeshObstacleShape.Box, NavMeshObstacleShape.Capsule })
        {
            Staged("obstacle." + shape + ".height3", () => { var o = NewObstacle(shape); o.height = 3f; return Obstacle(o, destroy: true); });
            Staged("obstacle." + shape + ".radius2", () => { var o = NewObstacle(shape); o.radius = 2f; return Obstacle(o, destroy: true); });
            Staged("obstacle." + shape + ".size246", () => { var o = NewObstacle(shape); o.size = new Vector3(2, 4, 6); return Obstacle(o, destroy: true); });
            Staged("obstacle." + shape + ".center", () => { var o = NewObstacle(shape); o.center = new Vector3(1, 2, 3); return Obstacle(o, destroy: true); });
            Staged("obstacle." + shape + ".size246.switch", () =>
            {
                var o = NewObstacle(shape);
                o.size = new Vector3(2, 4, 6);
                o.center = new Vector3(1, 2, 3);
                o.shape = shape == NavMeshObstacleShape.Box ? NavMeshObstacleShape.Capsule : NavMeshObstacleShape.Box;
                return Obstacle(o, destroy: true);
            });
            Staged("obstacle." + shape + ".same", () =>
            {
                var o = NewObstacle(shape);
                o.size = new Vector3(2, 4, 6);
                o.shape = shape;
                return Obstacle(o, destroy: true);
            });
        }

        // Renderers whose bounds follow their geometry.
        var trail = Make("Trail", new Vector3(30, 0, 0), Vector3.one).AddComponent<TrailRenderer>();
        Staged("trail.still", () => RendererBounds(trail));
        var trailOff = Make("TrailOff", new Vector3(40, 0, 0), Vector3.one).AddComponent<TrailRenderer>();
        trailOff.emitting = false;
        Staged("trail.notEmitting", () => RendererBounds(trailOff));
        var trailAway = Make("TrailAway", new Vector3(-10, 5, 2), Vector3.one).AddComponent<TrailRenderer>();
        Staged("trail.away", () => RendererBounds(trailAway));
        var trailWide = Make("TrailWide", new Vector3(10, 0, 0), Vector3.one).AddComponent<TrailRenderer>();
        trailWide.widthMultiplier = 3f;
        Staged("trail.wide", () => RendererBounds(trailWide));
        var trailScaled = Make("TrailScaled", new Vector3(0, 0, 10), new Vector3(2, 2, 2)).AddComponent<TrailRenderer>();
        Staged("trail.scaled", () => RendererBounds(trailScaled));
        var line = Make("Line", new Vector3(50, 0, 0), Vector3.one).AddComponent<LineRenderer>();
        Staged("line.new", () => RendererBounds(line));

        var grid = new GameObject("Grid").AddComponent<Grid>();
        var tilemap = new GameObject("Tilemap").AddComponent<Tilemap>();
        tilemap.transform.SetParent(grid.transform, false);
        var tilemapRenderer = tilemap.gameObject.AddComponent<TilemapRenderer>();
        Staged("tilemap.empty", () => TilemapBounds(tilemap, tilemapRenderer));
        var spriteless = new GameObject("TilemapSpriteless").AddComponent<Tilemap>();
        spriteless.transform.SetParent(grid.transform, false);
        var spritelessRenderer = spriteless.gameObject.AddComponent<TilemapRenderer>();
        spriteless.SetTile(new Vector3Int(2, 1, 0), ScriptableObject.CreateInstance<Tile>());
        Staged("tilemap.spriteless", () => TilemapBounds(spriteless, spritelessRenderer));
        var withSprite = new GameObject("TilemapSprite").AddComponent<Tilemap>();
        withSprite.transform.SetParent(grid.transform, false);
        var withSpriteRenderer = withSprite.gameObject.AddComponent<TilemapRenderer>();
        var texture = new Texture2D(16, 16);
        var tile = ScriptableObject.CreateInstance<Tile>();
        tile.sprite = Sprite.Create(texture, new Rect(0, 0, 16, 16), new Vector2(0.5f, 0.5f), 16);
        withSprite.SetTile(new Vector3Int(2, 1, 0), tile);
        Staged("tilemap.sprite", () => TilemapBounds(withSprite, withSpriteRenderer));

        var canvas = new GameObject("Canvas").AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Staged("canvas.overlay", () => V2(canvas.renderingDisplaySize));
        var worldCanvas = new GameObject("WorldCanvas").AddComponent<Canvas>();
        worldCanvas.renderMode = RenderMode.WorldSpace;
        Staged("canvas.world", () => V2(worldCanvas.renderingDisplaySize));

        var cameraCanvas = new GameObject("CameraCanvas").AddComponent<Canvas>();
        cameraCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        Staged("canvas.cameraWithout", () => V2(cameraCanvas.renderingDisplaySize));
        var sizedCanvas = new GameObject("SizedCanvas").AddComponent<Canvas>();
        sizedCanvas.renderMode = RenderMode.WorldSpace;
        ((RectTransform)sizedCanvas.transform).sizeDelta = new Vector2(200, 50);
        Staged("canvas.worldSized", () => V2(sizedCanvas.renderingDisplaySize));
        Staged("canvas.new", () => { var c = new GameObject("NewCanvas").AddComponent<Canvas>(); var r = c.renderMode + ":" + V2(c.renderingDisplaySize); DestroyImmediate(c.gameObject); return r; });

        Staged("physics2d.default.subStepCount", () => Physics2D.defaultPhysicsScene.subStepCount.ToString(CultureInfo.InvariantCulture));
        var local = SceneManager.CreateScene("Physics2DLocal", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        Staged("physics2d.local.subStepCount", () => local.GetPhysicsScene2D().subStepCount.ToString(CultureInfo.InvariantCulture));
        Staged("physics2d.local.simulated", () =>
        {
            var scene = local.GetPhysicsScene2D();
            scene.Simulate(0.02f);
            return scene.subStepCount.ToString(CultureInfo.InvariantCulture);
        });

        Staged("physics2d.script", () =>
        {
            var mode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;
            var scene = local.GetPhysicsScene2D();
            scene.Simulate(0.02f);
            var r = "local=" + scene.subStepCount;
            Physics2D.Simulate(0.02f);
            r += ";default=" + Physics2D.defaultPhysicsScene.subStepCount;
            var stepping = Physics2D.useSubStepping;
            Physics2D.useSubStepping = true;
            Physics2D.Simulate(0.1f);
            r += ";subStepping=" + Physics2D.defaultPhysicsScene.subStepCount;
            Physics2D.useSubStepping = stepping;
            Physics2D.simulationMode = mode;
            return r;
        });
        var bodyScene = SceneManager.CreateScene("Physics2DBody", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var body2d = new GameObject("Body2D");
        SceneManager.MoveGameObjectToScene(body2d, bodyScene);
        body2d.AddComponent<Rigidbody2D>();
        body2d.AddComponent<BoxCollider2D>();
        Staged("physics2d.body.subStepCount", () => bodyScene.GetPhysicsScene2D().subStepCount.ToString(CultureInfo.InvariantCulture));
        Staged("physics2d.body.simulated", () =>
        {
            var scene = bodyScene.GetPhysicsScene2D();
            scene.Simulate(0.02f);
            var r = "fixed=" + scene.subStepCount;
            var mode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.Script;
            scene.Simulate(0.02f);
            r += ";script=" + scene.subStepCount;
            var stepping = Physics2D.useSubStepping;
            Physics2D.useSubStepping = true;
            scene.Simulate(0.1f);
            r += ";subStepping=" + scene.subStepCount;
            Physics2D.useSubStepping = stepping;
            Physics2D.simulationMode = mode;
            return r;
        });
        var staticScene = SceneManager.CreateScene("Physics2DStatic", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var static2d = new GameObject("Static2D");
        SceneManager.MoveGameObjectToScene(static2d, staticScene);
        static2d.AddComponent<BoxCollider2D>();
        Staged("physics2d.static.simulated", () =>
        {
            var scene = staticScene.GetPhysicsScene2D();
            scene.Simulate(0.02f);
            return scene.subStepCount.ToString(CultureInfo.InvariantCulture);
        });
        var sleepingScene = SceneManager.CreateScene("Physics2DSleeping", new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        var sleeping2d = new GameObject("Sleeping2D");
        SceneManager.MoveGameObjectToScene(sleeping2d, sleepingScene);
        var sleepingBody = sleeping2d.AddComponent<Rigidbody2D>();
        sleepingBody.bodyType = RigidbodyType2D.Kinematic;
        Staged("physics2d.kinematic.simulated", () =>
        {
            var scene = sleepingScene.GetPhysicsScene2D();
            scene.Simulate(0.02f);
            return scene.subStepCount.ToString(CultureInfo.InvariantCulture);
        });
        Staged("terrainData.heightmapTexture", () => TextureName(terrainData.heightmapTexture));
        Staged("terrainData.holesTexture", () => TextureName(terrainData.holesTexture));
        Staged("terrainData.new.heightmapTexture", () => TextureName(new TerrainData().heightmapTexture));
        Staged("terrainData.new.holesTexture", () => TextureName(new TerrainData().holesTexture));
    }

    static NavMeshAgent s_DestinationAgent, s_OffDestinationAgent;

    static void Destinations()
    {
        Try("agent.destination.set", () => s_DestinationAgent.SetDestination(new Vector3(5, 0, 5)).ToString());
        Try("agent.offDestination.set", () => s_OffDestinationAgent.SetDestination(new Vector3(5, 0, 5)).ToString());
    }

    static string V2(Vector2 v) => F(v.x) + "," + F(v.y);

    static string TextureName(Texture texture) => texture == null ? "null" : texture.GetType().Name + ":" + texture.name + ":" + texture.width + "x" + texture.height;

    static string Geometry(Collider collider)
    {
        var holder = collider.GeometryHolder;
        switch (holder.Type)
        {
            case GeometryType.Box: return "Box:" + V(holder.As<BoxGeometry>().HalfExtents);
            case GeometryType.Sphere: return "Sphere:" + F(holder.As<SphereGeometry>().Radius);
            case GeometryType.Capsule: { var c = holder.As<CapsuleGeometry>(); return "Capsule:" + F(c.Radius) + "," + F(c.HalfLength); }
            case GeometryType.ConvexMesh: { var c = holder.As<ConvexMeshGeometry>(); return "ConvexMesh:" + V(c.Scale) + ";" + Q(c.ScaleAxisRotation); }
            case GeometryType.TriangleMesh: { var c = holder.As<TriangleMeshGeometry>(); return "TriangleMesh:" + V(c.Scale) + ";" + Q(c.ScaleAxisRotation); }
            default: return holder.Type.ToString();
        }
    }

    static string Agent(NavMeshAgent agent) =>
        "on=" + agent.isOnNavMesh + ";dest=" + V(agent.destination) + ";status=" + agent.pathStatus + ";remaining=" + F(agent.remainingDistance)
        + ";hasPath=" + agent.hasPath + ";pending=" + agent.pathPending + ";next=" + V(agent.nextPosition) + ";end=" + V(agent.pathEndPosition);

    static NavMeshObstacle NewObstacle(NavMeshObstacleShape shape)
    {
        var o = Make("ObstacleControl", new Vector3(-8, 0, -8), Vector3.one).AddComponent<NavMeshObstacle>();
        if (shape != NavMeshObstacleShape.Box) o.shape = shape;
        return o;
    }

    static string Obstacle(NavMeshObstacle o, bool destroy)
    {
        var text = o.shape + ":height=" + F(o.height) + ";radius=" + F(o.radius) + ";size=" + V(o.size) + ";center=" + V(o.center);
        if (destroy) DestroyImmediate(o.gameObject);
        return text;
    }

    static string RendererBounds(Renderer renderer) => "world=" + V(renderer.bounds.center) + "/" + V(renderer.bounds.extents) + ";local=" + V(renderer.localBounds.center) + "/" + V(renderer.localBounds.extents);

    static string TilemapBounds(Tilemap map, TilemapRenderer renderer) =>
        "cells=" + map.cellBounds + ";local=" + V(map.localBounds.center) + "/" + V(map.localBounds.extents) + ";" + RendererBounds(renderer);

    static void OnLog(string condition, string stackTrace, LogType type) => s_Logs.Add(type + ": " + condition);

    /// <summary>CommandBuffer.name: new, then set to a name, to empty and to null.</summary>
    static void CommandBuffers()
    {
        Try("cmd.new.name", () => new CommandBuffer().name);
        Try("cmd.set.name", () => { var c = new CommandBuffer(); c.name = "probe"; return c.name; });
        Try("cmd.setEmpty.name", () => { var c = new CommandBuffer(); c.name = "probe"; c.name = ""; return c.name; });
        Try("cmd.setNull.name", () => { var c = new CommandBuffer(); c.name = "probe"; c.name = null; return c.name == null ? "null" : "string:" + c.name; });
        Try("cmd.cleared.name", () => { var c = new CommandBuffer(); c.Clear(); return c.name; });
    }

    /// <summary>GraphicsFormatUtility.GetRenderTextureFormat of every graphics format.</summary>
    static void Formats()
    {
        // By name: obsolete aliases share their value with another name.
        foreach (var name in Enum.GetNames(typeof(GraphicsFormat)))
        {
            var format = (GraphicsFormat)Enum.Parse(typeof(GraphicsFormat), name);
            Try("rtformat." + (int)format + "." + name, () => ((int)GraphicsFormatUtility.GetRenderTextureFormat(format)).ToString(CultureInfo.InvariantCulture));
        }
    }

    static string Attachment(AttachmentDescriptor a) => (int)a.format + "/" + (GraphicsFormatUtility.IsDepthStencilFormat(a.graphicsFormat) ? "depth" : a.graphicsFormat.ToString());

    /// <summary>The descriptor's color format, graphics format, sampling mode and the kind of its
    /// depth format (none, depth only, depth and stencil: the format itself is the device's).</summary>
    static string Descriptor(RenderTextureDescriptor d) =>
        (int)d.colorFormat + "/" + d.graphicsFormat + "/" + d.shadowSamplingMode + "/"
        + (d.depthStencilFormat == GraphicsFormat.None ? "nodepth" : GraphicsFormatUtility.IsStencilFormat(d.depthStencilFormat) ? "depthstencil" : "depth");

    /// <summary>AttachmentDescriptor.format and RenderTextureDescriptor.colorFormat: default, made
    /// from a format, and with the fields their getters read set apart.</summary>
    static void Descriptors()
    {
        Try("attach.default", () => Attachment(default));
        Try("attach.none", () => Attachment(new AttachmentDescriptor(GraphicsFormat.None)));
        Try("attach.argb32", () => Attachment(new AttachmentDescriptor(RenderTextureFormat.ARGB32)));
        Try("attach.depth", () => Attachment(new AttachmentDescriptor(RenderTextureFormat.Depth)));
        Try("attach.shadowmap", () => Attachment(new AttachmentDescriptor(RenderTextureFormat.Shadowmap)));
        Try("attach.d32", () => Attachment(new AttachmentDescriptor(GraphicsFormat.D32_SFloat)));
        Try("attach.r8g8b8a8", () => Attachment(new AttachmentDescriptor(GraphicsFormat.R8G8B8A8_UNorm)));
        Try("attach.setNone", () => { var a = new AttachmentDescriptor(RenderTextureFormat.ARGB32); a.graphicsFormat = GraphicsFormat.None; return Attachment(a); });

        Try("rtd.default", () => Descriptor(default));
        Try("rtd.default.samplingNone", () => { RenderTextureDescriptor d = default; d.shadowSamplingMode = ShadowSamplingMode.None; return Descriptor(d); });
        Try("rtd.default.samplingRaw", () => { RenderTextureDescriptor d = default; d.shadowSamplingMode = ShadowSamplingMode.RawDepth; return Descriptor(d); });
        Try("rtd.default.samplingCompare", () => { RenderTextureDescriptor d = default; d.shadowSamplingMode = ShadowSamplingMode.CompareDepths; return Descriptor(d); });
        Try("rtd.argb32", () => Descriptor(new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0)));
        Try("rtd.argb32.depth", () => Descriptor(new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 24)));
        Try("rtd.depth", () => Descriptor(new RenderTextureDescriptor(4, 4, RenderTextureFormat.Depth, 24)));
        Try("rtd.shadowmap", () => Descriptor(new RenderTextureDescriptor(4, 4, RenderTextureFormat.Shadowmap, 24)));
        Try("rtd.depth.samplingRaw", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.Depth, 24); d.shadowSamplingMode = ShadowSamplingMode.RawDepth; return Descriptor(d); });
        Try("rtd.depth.samplingCompare", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.Depth, 24); d.shadowSamplingMode = ShadowSamplingMode.CompareDepths; return Descriptor(d); });
        Try("rtd.argb32.noColor", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0); d.graphicsFormat = GraphicsFormat.None; return Descriptor(d); });
        Try("rtd.argb32.noColor.samplingCompare", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0); d.graphicsFormat = GraphicsFormat.None; d.shadowSamplingMode = ShadowSamplingMode.CompareDepths; return Descriptor(d); });
        Try("rtd.depth.setArgb32", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.Depth, 24); d.colorFormat = RenderTextureFormat.ARGB32; return Descriptor(d); });
        Try("rtd.argb32.setShadowmap", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 24); d.colorFormat = RenderTextureFormat.Shadowmap; return Descriptor(d); });
        Try("rtd.argb32.setDepth", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 24); d.colorFormat = RenderTextureFormat.Depth; return Descriptor(d); });
        Try("rtd.default.depth24", () => { RenderTextureDescriptor d = default; d.depthBufferBits = 24; return Descriptor(d); });
        Try("rtd.default.samplingNone.depth24", () => { RenderTextureDescriptor d = default; d.shadowSamplingMode = ShadowSamplingMode.None; d.depthBufferBits = 24; return Descriptor(d); });
        Try("rtd.argb32.samplingCompare.depth24", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0); d.shadowSamplingMode = ShadowSamplingMode.CompareDepths; d.depthBufferBits = 24; return Descriptor(d); });
        Try("rtd.argb32.samplingRaw.depth24", () => { var d = new RenderTextureDescriptor(4, 4, RenderTextureFormat.ARGB32, 0); d.shadowSamplingMode = ShadowSamplingMode.RawDepth; d.depthBufferBits = 24; return Descriptor(d); });
        Try("rtd.texture.depth", () => { var t = new RenderTexture(4, 4, 24, RenderTextureFormat.Depth); var r = Descriptor(t.descriptor); DestroyImmediate(t); return r; });
        Try("rtd.texture.shadowmap", () => { var t = new RenderTexture(4, 4, 24, RenderTextureFormat.Shadowmap); var r = Descriptor(t.descriptor); DestroyImmediate(t); return r; });

        Try("srf.new.terrainDetailUnsupported", () => new SupportedRenderingFeatures().terrainDetailUnsupported.ToString());
        Try("srf.setFalse.terrainDetailUnsupported", () => { var s = new SupportedRenderingFeatures(); s.terrainDetailUnsupported = false; return s.terrainDetailUnsupported.ToString(); });
    }

    /// <summary>Scene culling masks: batch filter settings default and set; objects in the probe
    /// scene, new, inactive, in DontDestroyOnLoad, in a created scene and in a preview scene.</summary>
    static void CullingMasks()
    {
        Try("bfs.default", () => default(BatchFilterSettings).sceneCullingMask.ToString(CultureInfo.InvariantCulture));
        Try("bfs.set5", () => { BatchFilterSettings s = default; s.sceneCullingMask = 5; return s.sceneCullingMask.ToString(CultureInfo.InvariantCulture); });
        Try("bfs.set0", () => { BatchFilterSettings s = default; s.sceneCullingMask = 0; return s.sceneCullingMask.ToString(CultureInfo.InvariantCulture); });
        Try("bfs.drawRange", () => default(BatchDrawRange).filterSettings.sceneCullingMask.ToString(CultureInfo.InvariantCulture));

        Try("go.authored", () => GameObject.Find("AuthoredEmpty").sceneCullingMask.ToString(CultureInfo.InvariantCulture));
        Try("go.new", () => { var g = new GameObject("MaskNew"); var r = g.sceneCullingMask; DestroyImmediate(g); return r.ToString(CultureInfo.InvariantCulture); });
        Try("go.inactive", () => { var g = new GameObject("MaskInactive"); g.SetActive(false); var r = g.sceneCullingMask; DestroyImmediate(g); return r.ToString(CultureInfo.InvariantCulture); });
        Try("go.ddol", () => { var g = new GameObject("MaskDdol"); DontDestroyOnLoad(g); var r = g.sceneCullingMask; DestroyImmediate(g); return r.ToString(CultureInfo.InvariantCulture); });
        Try("go.created", () =>
        {
            var scene = SceneManager.CreateScene("MaskScene");
            var g = new GameObject("MaskCreated");
            SceneManager.MoveGameObjectToScene(g, scene);
            var r = g.sceneCullingMask;
            DestroyImmediate(g);
            return r.ToString(CultureInfo.InvariantCulture);
        });
#if UNITY_EDITOR
        Try("editor.activeScene", () => UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(SceneManager.GetActiveScene()).ToString(CultureInfo.InvariantCulture));
        Try("editor.invalidScene", () => UnityEditor.SceneManagement.EditorSceneManager.GetSceneCullingMask(default).ToString(CultureInfo.InvariantCulture));
        Try("editor.defaultConstant", () => UnityEditor.SceneManagement.SceneCullingMasks.DefaultSceneCullingMask.ToString(CultureInfo.InvariantCulture));
#endif
    }

    /// <summary>Light.layerShadowCullDistances of a new light; LightProbeGroup positions added by
    /// AddComponent (active and inactive object), loaded empty and with two points, instantiated.</summary>
    static void Lights()
    {
        Try("light.layerShadowCullDistances", () =>
        {
            var g = new GameObject("CullLight");
            var light = g.AddComponent<Light>();
            var a = light.layerShadowCullDistances;
            var text = a == null ? "null" : a.Length + ":" + string.Join(",", Array.ConvertAll(a, F));
            DestroyImmediate(g);
            return text;
        });
        Try("light.layerShadowCullDistances.copy", () =>
        {
            var g = new GameObject("CullLightCopy");
            var light = g.AddComponent<Light>();
            var a = light.layerShadowCullDistances;
            a[0] = 5f;
            var b = light.layerShadowCullDistances;
            var text = (ReferenceEquals(a, b) ? "same" : "copy") + ":" + F(b[0]);
            DestroyImmediate(g);
            return text;
        });

        Try("lpg.added", () => { var g = new GameObject("ProbesAdded"); var r = Positions(g.AddComponent<LightProbeGroup>().probePositions); DestroyImmediate(g); return r; });
        Try("lpg.addedInactive", () => { var g = new GameObject("ProbesInactive"); g.SetActive(false); var r = Positions(g.AddComponent<LightProbeGroup>().probePositions); DestroyImmediate(g); return r; });
        Try("lpg.authoredEmpty", () => Positions(GameObject.Find("AuthoredEmpty").GetComponent<LightProbeGroup>().probePositions));
        Try("lpg.authoredTwo", () => Positions(GameObject.Find("AuthoredTwo").GetComponent<LightProbeGroup>().probePositions));
        Try("lpg.instantiatedEmpty", () => { var g = Instantiate(GameObject.Find("AuthoredEmpty")); var r = Positions(g.GetComponent<LightProbeGroup>().probePositions); DestroyImmediate(g); return r; });
        Try("lpg.instantiatedTwo", () => { var g = Instantiate(GameObject.Find("AuthoredTwo")); var r = Positions(g.GetComponent<LightProbeGroup>().probePositions); DestroyImmediate(g); return r; });
        Try("lpg.instantiatedAdded", () =>
        {
            var g = new GameObject("ProbesToCopy");
            var p = g.AddComponent<LightProbeGroup>();
            p.probePositions = new[] { new Vector3(7, 8, 9) };
            var copy = Instantiate(g);
            var r = Positions(copy.GetComponent<LightProbeGroup>().probePositions);
            DestroyImmediate(copy);
            DestroyImmediate(g);
            return r;
        });
        Try("lpg.setEmpty", () => { var g = new GameObject("ProbesSet"); var p = g.AddComponent<LightProbeGroup>(); p.probePositions = new Vector3[0]; var r = Positions(p.probePositions); DestroyImmediate(g); return r; });
    }

    /// <summary>A default AsyncGPUReadbackRequest and a default CullingResults.</summary>
    static void Readbacks()
    {
        Try("readback.default.done", () => default(AsyncGPUReadbackRequest).done.ToString());
        Try("readback.default.hasError", () => default(AsyncGPUReadbackRequest).hasError.ToString());
        Try("readback.default.width", () => default(AsyncGPUReadbackRequest).width.ToString(CultureInfo.InvariantCulture));
        Try("readback.default.layerCount", () => default(AsyncGPUReadbackRequest).layerCount.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.lightIndexCount", () => default(CullingResults).lightIndexCount.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.reflectionProbeIndexCount", () => default(CullingResults).reflectionProbeIndexCount.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.lightAndReflectionProbeIndexCount", () => default(CullingResults).lightAndReflectionProbeIndexCount.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.visibleLights", () => default(CullingResults).visibleLights.Length.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.visibleOffscreenVertexLights", () => default(CullingResults).visibleOffscreenVertexLights.Length.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.visibleReflectionProbes", () => default(CullingResults).visibleReflectionProbes.Length.ToString(CultureInfo.InvariantCulture));
        Try("culling.default.GetLightIndexMap", () => { var m = default(CullingResults).GetLightIndexMap(Unity.Collections.Allocator.Temp); var n = m.Length; m.Dispose(); return n.ToString(CultureInfo.InvariantCulture); });
        Try("culling.default.equals", () => default(CullingResults).Equals(default(CullingResults)).ToString());
    }

    static string AnimatorState(Animator a) =>
        "pivot=" + F(a.feetPivotActive) + ";target=" + Q(a.targetRotation) + "@" + V(a.targetPosition)
        + ";playback=" + F(a.playbackTime) + ";start=" + F(a.recorderStartTime) + ";stop=" + F(a.recorderStopTime)
        + ";mode=" + a.recorderMode;

    /// <summary>A new Animator's pivot, target and recorder values, with and without a controller,
    /// on an inactive object, and after each of them is set.</summary>
    static void Animators()
    {
        Try("animator.new", () => { var g = new GameObject("AnimNew"); var r = AnimatorState(g.AddComponent<Animator>()); DestroyImmediate(g); return r; });
        Try("animator.inactive", () => { var g = new GameObject("AnimInactive"); g.SetActive(false); var r = AnimatorState(g.AddComponent<Animator>()); DestroyImmediate(g); return r; });
#if UNITY_EDITOR
        Try("animator.controller", () =>
        {
            var g = new GameObject("AnimController");
            var a = g.AddComponent<Animator>();
            var controller = new UnityEditor.Animations.AnimatorController();
            controller.AddLayer("Base");
            controller.layers[0].stateMachine.AddState("Idle");
            a.runtimeAnimatorController = controller;
            var r = AnimatorState(a);
            DestroyImmediate(g);
            return r;
        });
#endif
        foreach (var value in new[] { 0.25f, 0f, 1f, 3f, -1f })
        {
            Try("animator.feetPivotActive." + F(value), () =>
            {
                var g = new GameObject("AnimPivot");
                var a = g.AddComponent<Animator>();
                a.feetPivotActive = value;
                var r = F(a.feetPivotActive);
                DestroyImmediate(g);
                return r;
            });
        }
        Try("animator.feetPivotActive.inactive", () =>
        {
            var g = new GameObject("AnimPivotInactive");
            g.SetActive(false);
            var a = g.AddComponent<Animator>();
            a.feetPivotActive = 0.5f;
            var r = F(a.feetPivotActive);
            DestroyImmediate(g);
            return r;
        });
        Try("animator.inactiveActivated", () =>
        {
            var g = new GameObject("AnimActivated");
            g.SetActive(false);
            var a = g.AddComponent<Animator>();
            g.SetActive(true);
            var r = AnimatorState(a);
            DestroyImmediate(g);
            return r;
        });
        Try("animator.inactiveSetActivated", () =>
        {
            var g = new GameObject("AnimSetActivated");
            g.SetActive(false);
            var a = g.AddComponent<Animator>();
            a.feetPivotActive = 0.5f;
            g.SetActive(true);
            var before = F(a.feetPivotActive);
            a.feetPivotActive = 0.25f;
            var r = before + "," + F(a.feetPivotActive);
            DestroyImmediate(g);
            return r;
        });
        Try("animator.disabled", () =>
        {
            var g = new GameObject("AnimDisabled");
            var a = g.AddComponent<Animator>();
            a.enabled = false;
            var r = F(a.feetPivotActive);
            a.feetPivotActive = 0.5f;
            r += "," + F(a.feetPivotActive);
            DestroyImmediate(g);
            return r;
        });
        Try("animator.deactivated", () =>
        {
            var g = new GameObject("AnimDeactivated");
            var a = g.AddComponent<Animator>();
            a.feetPivotActive = 0.5f;
            g.SetActive(false);
            var r = F(a.feetPivotActive);
            a.feetPivotActive = 0.25f;
            g.SetActive(true);
            r += "," + F(a.feetPivotActive);
            DestroyImmediate(g);
            return r;
        });
        Try("animator.recorderStartTime.set", () => { var g = new GameObject("AnimStart"); var a = g.AddComponent<Animator>(); a.recorderStartTime = 2f; var r = AnimatorState(a); DestroyImmediate(g); return r; });
        Try("animator.recorderStopTime.set", () => { var g = new GameObject("AnimStop"); var a = g.AddComponent<Animator>(); a.recorderStopTime = 2f; var r = AnimatorState(a); DestroyImmediate(g); return r; });
        Try("animator.playbackTime.set", () => { var g = new GameObject("AnimPlayback"); var a = g.AddComponent<Animator>(); a.playbackTime = 1f; var r = AnimatorState(a); DestroyImmediate(g); return r; });
    }
}
