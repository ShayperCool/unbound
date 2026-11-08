using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.LowLevel;
using PL = UnityEngine.PlayerLoop;

/// <summary>
/// Where in the player loop walking agents change (<c>loop.*</c>): agents in lanes of the strip and
/// obstacle rigs (4 m apart, clear of the strips and obstacles) and one over a NavMesh link in the
/// rays rig, recorded after <c>PreUpdate.AIUpdate</c> (<c>a</c>), in Update (<c>u</c>), after
/// <c>PreLateUpdate.AIUpdatePostScript</c> (<c>s</c>) and in LateUpdate (<c>l</c>) of every frame.
/// Each lane is one mechanism: a destination set in Update or LateUpdate, updatePosition or
/// updateRotation off, a script setting transform.position, nextPosition, Move, Warp, velocity,
/// isStopped or a new destination (on and off the NavMesh) in Update, or the transform or
/// nextPosition in LateUpdate (<c>c</c>, <c>d</c>: right after those changes), a short path's
/// arrival, an agent crossing a link, and one made and given a destination in Update.
/// </summary>
public class LoopTiming : MonoBehaviour
{
    /// <summary>Frames recorded (counted from the frame after the lanes are made).</summary>
    public const int Frames = 20;
    /// <summary>The frame whose Update (or LateUpdate) changes the lanes' agents.</summary>
    const int ChangeFrame = 6;
    /// <summary>The frame whose Update (or LateUpdate) gives the walkU (walkL) lane its destination.</summary>
    const int DestinationFrame = 2;
    const int Mud = 4;

    public static bool Done { get; private set; }

    readonly List<(string Name, string Rig, NavMeshAgent Agent)> m_Lanes = new List<(string, string, NavMeshAgent)>();
    int m_Frame;
    static LoopTiming s_Instance;

    static Vector3 P(float x, float z) => new Vector3(x, 0f, z);

    void Awake()
    {
        s_Instance = this;
        InstallMarkers();
        // Lanes: z = -5, -3, 3, 5 of each rig, walking from x = -5 towards x = 5.
        var lanes = new (string Name, string Rig, float Z, Action<NavMeshAgent> Setup)[]
        {
            ("walkU", "strip1", -5f, null),
            ("walkL", "strip1", -3f, null),
            ("noPosition", "strip1", 3f, a => { a.updatePosition = false; Go(a); }),
            ("noRotation", "strip1", 5f, a => { a.updateRotation = false; Go(a); }),
            ("position", "strip2", -5f, Go),
            ("next", "strip2", -3f, Go),
            ("move", "strip2", 3f, Go),
            ("warp", "strip2", 5f, Go),
            ("velocity", "box", -5f, Go),
            ("stop", "box", -3f, Go),
            ("redirect", "box", 3f, Go),
            ("positionLate", "box", 5f, Go),
            ("short", "bar", -5f, a => a.SetDestination(Origin(a) + P(-4.4f, -5f))),
            ("nextLate", "bar", -3f, Go),
            ("redirectOff", "bar", 3f, Go),
            ("standBack", "turned", -5f, null),
            ("redirectAhead", "turned", -3f, Go),
            ("redirectMid", "turned", 3f, Go),
            ("redirectSame", "turned", 5f, Go),
        };
        foreach (var (name, rig, z, setup) in lanes)
        {
            var go = new GameObject("loop." + name);
            go.transform.position = Rigs.Get(rig).Origin + P(-5f, z);
            var agent = go.AddComponent<NavMeshAgent>();
            m_Lanes.Add((name, rig, agent));
            setup?.Invoke(agent);
        }
        // A link over the rays rig's Mud strip, which the link lane's agent avoids by its area cost
        // (clear of the probe's NavMeshAgent.Raycast agent at x = -3, z = -3, and of the Water).
        var rays = Rigs.Get("rays").Origin;
        NavMesh.AddLink(new NavMeshLinkData
        {
            startPosition = rays + P(2.5f, -0.6f),
            endPosition = rays + P(2.5f, 0.6f),
            width = 0f,
            // Cheap, so that the way over it beats the way round the strip's open end.
            costModifier = 0.1f,
            bidirectional = true,
            area = 0,
            agentTypeID = 0,
        });
        var link = new GameObject("loop.link");
        link.transform.position = rays + P(2.5f, -3.5f);
        var linked = link.AddComponent<NavMeshAgent>();
        // Fast, so that it reaches, crosses and leaves the 1.2 m link within the recorded frames.
        linked.speed = 10f;
        linked.acceleration = 100f;
        linked.SetAreaCost(Mud, 100f);
        linked.SetDestination(rays + P(2.5f, 3.5f));
        m_Lanes.Add(("link", "rays", linked));
        // Right after the lanes are made, their destinations set (loop.c0).
        Log("c");
    }

    Vector3 Origin(NavMeshAgent agent) => Rigs.Get(m_Lanes.Find(e => e.Agent == agent).Rig ?? "strip1").Origin;

    /// <summary>Along the lane to x = 5.</summary>
    static void Go(NavMeshAgent agent)
    {
        var p = agent.transform.position;
        agent.SetDestination(new Vector3(p.x + 10f, p.y, p.z));
    }

    NavMeshAgent Lane(string name) => m_Lanes.Find(e => e.Name == name).Agent;

    void Log(string point)
    {
        if (m_Frame < (point == "c" ? 0 : 1) || m_Frame > Frames) return;
        ProbeLog.Begin("loop." + point + m_Frame);
        foreach (var (name, rig, agent) in m_Lanes)
        {
            var o = Rigs.Get(rig).Origin;
            ProbeLog.Value(name + ".position", agent.transform.position - o);
            ProbeLog.Value(name + ".rotation", agent.transform.rotation.eulerAngles.y);
            ProbeLog.Value(name + ".next", agent.nextPosition - o);
            ProbeLog.Value(name + ".velocity", agent.velocity);
            ProbeLog.Value(name + ".desired", agent.desiredVelocity);
            ProbeLog.Value(name + ".pending", agent.pathPending);
            ProbeLog.Value(name + ".remaining", agent.pathPending ? -1f : agent.remainingDistance);
            ProbeLog.Value(name + ".link", agent.isOnOffMeshLink);
        }
    }

    void Update()
    {
        m_Frame++;
        Log("u");
        if (m_Frame == DestinationFrame)
        {
            Go(Lane("walkU"));
            Log("c");
        }
        if (m_Frame != ChangeFrame) return;
        var side = new Vector3(0f, 0f, 0.4f);
        var position = Lane("position");
        position.transform.position += side;
        var next = Lane("next");
        next.nextPosition = next.nextPosition + side;
        Lane("move").Move(side);
        var warp = Lane("warp");
        warp.Warp(warp.nextPosition + side);
        Lane("velocity").velocity = new Vector3(0f, 0f, 2f);
        Lane("stop").isStopped = true;
        // Back along the lane, on the NavMesh; and 3 m back, beyond the NavMesh's edge.
        Lane("redirect").SetDestination(Rigs.Get("box").Origin + P(-5.3f, 3f));
        // An agent made now and given a destination at once (logged from loop.c6 on).
        var fresh = new GameObject("loop.fresh");
        fresh.transform.position = Rigs.Get("bar").Origin + P(-5f, 5f);
        var freshAgent = fresh.AddComponent<NavMeshAgent>();
        m_Lanes.Add(("fresh", "bar", freshAgent));
        Go(freshAgent);
        // A standing agent given a destination as near; a walking one given one far ahead.
        Lane("standBack").SetDestination(Rigs.Get("turned").Origin + P(-5.3f, -5f));
        Lane("redirectAhead").SetDestination(Rigs.Get("turned").Origin + P(5f, -2f));
        // A walking one given a destination half way along its path; and its own destination again.
        Lane("redirectMid").SetDestination(Rigs.Get("turned").Origin + P(0f, 3f));
        var same = Lane("redirectSame");
        same.SetDestination(same.destination);
        var off = Lane("redirectOff");
        off.SetDestination(off.nextPosition - new Vector3(3f, 0f, 0f));
        // Right after the changes (loop.c*).
        Log("c");
    }

    void LateUpdate()
    {
        Log("l");
        if (m_Frame == DestinationFrame) Go(Lane("walkL"));
        if (m_Frame == ChangeFrame)
        {
            Lane("positionLate").transform.position += new Vector3(0f, 0f, 0.4f);
            var next = Lane("nextLate");
            next.nextPosition = next.nextPosition + new Vector3(0f, 0f, 0.4f);
            // Right after the changes in LateUpdate (loop.d*).
            Log("d");
        }
        if (m_Frame >= Frames) Done = true;
    }

    // ------------------------------------------------------------------ player-loop markers

    sealed class AfterAI { }
    sealed class AfterPostScript { }

    static void InstallMarkers()
    {
        var loop = PlayerLoop.GetCurrentPlayerLoop();
        for (int i = 0; i < loop.subSystemList.Length; i++)
        {
            var phase = loop.subSystemList[i];
            if (phase.subSystemList == null) continue;
            var list = new List<PlayerLoopSystem>();
            foreach (var s in phase.subSystemList)
            {
                list.Add(s);
                if (s.type == typeof(PL.PreUpdate.AIUpdate))
                    list.Add(new PlayerLoopSystem { type = typeof(AfterAI), updateDelegate = () => Marker("a") });
                else if (s.type == typeof(PL.PreLateUpdate.AIUpdatePostScript))
                    list.Add(new PlayerLoopSystem { type = typeof(AfterPostScript), updateDelegate = () => Marker("s") });
            }
            phase.subSystemList = list.ToArray();
            loop.subSystemList[i] = phase;
        }
        PlayerLoop.SetPlayerLoop(loop);
    }

    /// <summary>The marker after AIUpdate runs before this frame's Update counts the frame.</summary>
    static void Marker(string point)
    {
        if (s_Instance == null) return;
        if (point == "a")
        {
            s_Instance.m_Frame++;
            s_Instance.Log("a");
            s_Instance.m_Frame--;
        }
        else s_Instance.Log("s");
    }
}
