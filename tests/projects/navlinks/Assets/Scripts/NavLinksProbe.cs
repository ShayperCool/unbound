using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Off-mesh links, run identically in the Unity Editor (the reference) and in Unbound when
/// <c>NAVLINKS_OUT</c> names the JSON-lines file to write. Every lane of <see cref="Lanes"/> has
/// agents crossing a link (or waiting at it, moved over it by a script, warped, stopped,
/// redirected...) at a fixed 0.02 s step; each agent's state is recorded every frame in LateUpdate
/// (after the AI update): position, rotation, next position, velocities, steering target,
/// remaining distance, path flags and corners, the current and next link data and the link's
/// occupied/activated flags. Scripted actions happen in Update, some a number of frames after
/// the frame the lane's first agent was first seen on its link (<c>onlink+k</c>).
/// </summary>
public class NavLinksProbe : MonoBehaviour
{
    const int Frames = 400;

    sealed class Lane
    {
        public string Name;
        public Lanes.Kind Kind;
        public Vector3 O;
        public readonly List<(string Name, NavMeshAgent Agent)> Agents = new List<(string, NavMeshAgent)>();
        public NavMeshLink Link;
#pragma warning disable CS0618
        public OffMeshLink Legacy;
#pragma warning restore CS0618
        /// <summary>The first frame (LateUpdate) the lane's first agent was on its link.</summary>
        public int OnLink = -1;
        public Action<int> Act;
    }

    readonly List<Lane> m_Lanes = new List<Lane>();
    readonly StringBuilder m_Out = new StringBuilder();
    int m_Frame;
    bool m_Done;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NAVLINKS_OUT"))) return;
        new GameObject("NavLinksProbe").AddComponent<NavLinksProbe>();
    }

    void Awake() => Time.captureDeltaTime = 0.02f;

    // ------------------------------------------------------------------ setup

    NavMeshLink AddLink(Lane lane, Vector3 start, Vector3 end, bool bidirectional = true, float width = 0f, float cost = -1f, int area = 0)
    {
        var go = new GameObject("Link " + lane.Name);
        go.SetActive(false);
        go.transform.position = lane.O;
        var link = go.AddComponent<NavMeshLink>();
        link.startPoint = start;
        link.endPoint = end;
        link.bidirectional = bidirectional;
        link.width = width;
        link.costModifier = cost;
        link.area = area;
        go.SetActive(true);
        return link;
    }

    NavMeshAgent Agent(Lane lane, string name, Vector3 at, Vector3? to, Action<NavMeshAgent> configure = null)
    {
        var go = new GameObject(lane.Name + "." + name);
        go.transform.position = lane.O + at;
        var agent = go.AddComponent<NavMeshAgent>();
        configure?.Invoke(agent);
        if (to.HasValue) agent.SetDestination(lane.O + to.Value);
        lane.Agents.Add((name, agent));
        return agent;
    }

    void Start()
    {
        // Every lane's path is found in the first frame (the per-frame A* budget is not what this
        // probe measures).
        NavMesh.pathfindingIterationsPerFrame = 100000;
        for (int i = 0; i < Lanes.All.Length; i++)
        {
            var lane = new Lane { Name = Lanes.All[i].Name, Kind = Lanes.All[i].Kind, O = Lanes.Origin(i) };
            m_Lanes.Add(lane);
            try { Setup(lane); }
            catch (Exception e)
            {
                Debug.LogException(e);
                Line(lane.Name, "-", 0, "\"error\":" + Text(e.GetType().Name + ": " + e.Message));
            }
        }
        foreach (var lane in m_Lanes)
            foreach (var (name, agent) in lane.Agents)
                Line(lane.Name, name, 0, "\"onNavMesh\":" + B(agent.isOnNavMesh) + ",\"p\":" + V(agent.transform.position)
                    + ",\"n\":" + V(agent.nextPosition) + ",\"pp\":" + B(agent.pathPending) + ",\"hp\":" + B(agent.hasPath)
                    + ",\"auto\":" + B(agent.autoTraverseOffMeshLink) + Links(lane, agent));
    }

    void Setup(Lane lane)
    {
        float hb = Lanes.HeightB(lane.Kind);
        var a = new Vector3(-8f, 0f, 0f);
        var b = new Vector3(8f, hb, 0f);
        var ls = new Vector3(-2f, 0f, 0f);
        var le = new Vector3(2f, hb, 0f);
        NavMeshAgent agent;
        switch (lane.Name)
        {
            case "serialized":
                lane.Link = GameObject.Find("Level/Lane serialized/Link").GetComponent<NavMeshLink>();
                Agent(lane, "a", a, b);
                break;
            case "legacy":
#pragma warning disable CS0618
                lane.Legacy = GameObject.Find("Level/Lane legacy").GetComponent<OffMeshLink>();
#pragma warning restore CS0618
                Agent(lane, "a", a, b);
                break;
            case "auto":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", a, b);
                break;
            case "oneway":
                lane.Link = AddLink(lane, ls, le, bidirectional: false);
                Agent(lane, "a", a, b);
                break;
            case "fast":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", a, b, g => { g.speed = 8f; g.acceleration = 30f; g.angularSpeed = 360f; });
                break;
            case "slow":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", a, b, g => g.speed = 1.5f);
                break;
            case "baseoffset":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", a + Vector3.up * 0.5f, b, g => g.baseOffset = 0.5f);
                break;
            case "norotation":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", a, b, g => g.updateRotation = false);
                break;
            case "noposition":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", a, b, g => g.updatePosition = false);
                break;
            case "drop":
                lane.Link = AddLink(lane, ls, le, bidirectional: false);
                Agent(lane, "a", a, b);
                break;
            case "climb":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", b, a);
                break;
            case "reverse":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", b, a);
                break;
            case "reverseoneway":
                lane.Link = AddLink(lane, ls, le, bidirectional: false);
                Agent(lane, "a", b, a);
                break;
            case "startonlink":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", ls, b);
                break;
            case "endatlink":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", a, le);
                break;
            case "shortafter":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", a, new Vector3(3f, 0f, 0f));
                break;
            case "nobraking":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", a, new Vector3(3f, 0f, 0f), g => g.autoBraking = false);
                break;
            case "snap":
                lane.Link = AddLink(lane, new Vector3(-1.2f, 0f, 0f), new Vector3(1.2f, 0f, 0f));
                Agent(lane, "a", a, b);
                break;
            case "wide":
                lane.Link = AddLink(lane, ls, le, width: 4f);
                Agent(lane, "a", new Vector3(-8f, 0f, 1.2f), new Vector3(8f, 0f, 1.2f));
                break;
            case "two":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", a, b);
                Agent(lane, "b", new Vector3(-10f, 0f, 0.8f), new Vector3(8f, 0f, 1.5f));
                break;
            case "costlow":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", new Vector3(-3f, 0f, 0f), new Vector3(3f, 0f, 0f));
                break;
            case "costhigh":
                lane.Link = AddLink(lane, ls, le, cost: 3f);
                Agent(lane, "a", new Vector3(-3f, 0f, 0f), new Vector3(3f, 0f, 0f));
                break;
            case "areajump":
                lane.Link = AddLink(lane, ls, le, area: 2);
                Agent(lane, "a", new Vector3(-3f, 0f, 0f), new Vector3(3f, 0f, 0f));
                break;
            case "areamask":
                lane.Link = AddLink(lane, ls, le, area: 2);
                Agent(lane, "a", new Vector3(-3f, 0f, 0f), new Vector3(3f, 0f, 0f), g => g.areaMask = ~(1 << 2));
                break;
            case "inactive":
                lane.Link = AddLink(lane, ls, le);
                lane.Link.activated = false;
                agent = Agent(lane, "a", a, b);
                lane.Act = f =>
                {
                    if (f == 100) lane.Link.activated = true;
                    if (f == 200) agent.SetDestination(lane.O + b);
                };
                break;
            case "manualwait":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b, g => g.autoTraverseOffMeshLink = false);
                lane.Act = f => { if (lane.OnLink >= 0 && f == lane.OnLink + 60) agent.CompleteOffMeshLink(); };
                break;
            case "manualmove":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b, g => g.autoTraverseOffMeshLink = false);
                StartCoroutine(Traverse(agent, false));
                break;
            case "manualcurve":
                lane.Link = AddLink(lane, ls, le, bidirectional: false);
                agent = Agent(lane, "a", a, b, g => g.autoTraverseOffMeshLink = false);
                StartCoroutine(Traverse(agent, true));
                break;
            case "manualtoggle":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b, g => g.autoTraverseOffMeshLink = false);
                lane.Act = f => { if (lane.OnLink >= 0 && f == lane.OnLink + 30) agent.autoTraverseOffMeshLink = true; };
                break;
            case "warpwait":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b, g => g.autoTraverseOffMeshLink = false);
                lane.Act = f => { if (lane.OnLink >= 0 && f == lane.OnLink + 10) Log(lane, "warp", agent.Warp(lane.O + new Vector3(6f, 0f, 0f))); };
                break;
            case "warpauto":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b);
                lane.Act = f => { if (lane.OnLink >= 0 && f == lane.OnLink + 3) Log(lane, "warp", agent.Warp(lane.O + new Vector3(6f, 0f, 0f))); };
                break;
            case "stopmid":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b);
                lane.Act = f =>
                {
                    if (lane.OnLink < 0) return;
                    if (f == lane.OnLink + 3) agent.isStopped = true;
                    if (f == lane.OnLink + 40) agent.isStopped = false;
                };
                break;
            case "resetmid":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b);
                lane.Act = f => { if (lane.OnLink >= 0 && f == lane.OnLink + 3) agent.ResetPath(); };
                break;
            case "redirectmid":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b);
                lane.Act = f => { if (lane.OnLink >= 0 && f == lane.OnLink + 3) Log(lane, "set", agent.SetDestination(lane.O + a)); };
                break;
            case "speedmid":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b);
                lane.Act = f => { if (lane.OnLink >= 0 && f == lane.OnLink + 3) agent.speed = 1f; };
                break;
            case "completeauto":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b);
                lane.Act = f => { if (lane.OnLink >= 0 && f == lane.OnLink + 3) agent.CompleteOffMeshLink(); };
                break;
            case "deactivatemid":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b);
                lane.Act = f => { if (lane.OnLink >= 0 && f == lane.OnLink + 3) lane.Link.activated = false; };
                break;
            case "autooffmid":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b);
                lane.Act = f =>
                {
                    if (lane.OnLink < 0) return;
                    if (f == lane.OnLink + 3) agent.autoTraverseOffMeshLink = false;
                    if (f == lane.OnLink + 30) agent.CompleteOffMeshLink();
                };
                break;
            case "inactive130":
                lane.Link = AddLink(lane, ls, le);
                lane.Link.activated = false;
                Agent(lane, "a", a, b);
                lane.Act = f => { if (f == 130) lane.Link.activated = true; };
                break;
            case "radius1":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", a, b, g => g.radius = 1f);
                break;
            case "snapnear":
                lane.Link = AddLink(lane, new Vector3(-1.05f, 0f, 0f), new Vector3(1.05f, 0f, 0f));
                Agent(lane, "a", a, b);
                break;
            case "snapfar":
                lane.Link = AddLink(lane, new Vector3(-0.85f, 0f, 0f), new Vector3(0.85f, 0f, 0f));
                Agent(lane, "a", a, b);
                break;
            case "high":
                lane.Link = AddLink(lane, new Vector3(-2f, 0.6f, 0f), new Vector3(2f, 0.6f, 0f));
                Agent(lane, "a", a, b);
                break;
            case "diagonal":
                lane.Link = AddLink(lane, new Vector3(-2f, 0f, -1.5f), new Vector3(2f, 0f, 1.5f));
                Agent(lane, "a", new Vector3(-8f, 0f, -1.5f), new Vector3(8f, 0f, 1.5f));
                break;
            case "turn":
                lane.Link = AddLink(lane, ls, le);
                Agent(lane, "a", new Vector3(-8f, 0f, 2f), new Vector3(8f, 0f, -2f));
                break;
            case "query":
                lane.Link = AddLink(lane, ls, le);
                foreach (var (from, to) in new[] { (a, b), (b, a), (new Vector3(-3f, 0f, 1f), new Vector3(3f, 0f, -1f)) })
                {
                    var path = new NavMeshPath();
                    bool found = NavMesh.CalculatePath(lane.O + from, lane.O + to, NavMesh.AllAreas, path);
                    var c = new StringBuilder();
                    var corners = path.corners;
                    for (int k = 0; k < corners.Length; k++) c.Append(k > 0 ? "," : "").Append(V(corners[k]));
                    Line(lane.Name, "-", 0, "\"path\":" + B(found) + ",\"ps\":" + Text(path.status.ToString()) + ",\"c\":[" + c + "]");
                }
                break;
            case "disableagent":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b);
                lane.Act = f =>
                {
                    if (lane.OnLink < 0) return;
                    if (f == lane.OnLink + 3) agent.enabled = false;
                    if (f == lane.OnLink + 20) agent.enabled = true;
                };
                break;
            case "deactivatewait":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b, g => g.autoTraverseOffMeshLink = false);
                lane.Act = f =>
                {
                    if (lane.OnLink < 0) return;
                    if (f == lane.OnLink + 10) lane.Link.activated = false;
                    if (f == lane.OnLink + 30) agent.CompleteOffMeshLink();
                };
                break;
            case "offedge":
                lane.Link = AddLink(lane, ls, le);
                // Beyond platform A's side (its NavMesh edge is near z = 2.5).
                Agent(lane, "a", a, new Vector3(-6f, 0f, 6f));
                agent = Agent(lane, "b", new Vector3(-8f, 0f, -1.5f), new Vector3(-3f, 0f, -1.5f));
                // Walking: 2.5 m beyond the platform's end, behind it.
                lane.Act = f => { if (f == 20) Log(lane, "set", agent.SetDestination(lane.O + new Vector3(-13.5f, 0f, -1.5f))); };
                break;
            case "crosstile":
                // From A over the gap to B, its end past the NavMesh tile edge at x = 42.67.
                lane.Link = AddLink(lane, ls, new Vector3(3f, 0f, 0f));
                Agent(lane, "a", a, b);
                agent = Agent(lane, "b", b, a);
                break;
            case "island":
                // No link: B is another island.
                Agent(lane, "a", a, b);
                Agent(lane, "b", new Vector3(-8f, 0f, 2f), new Vector3(0.3f, 0f, 2f));
                Agent(lane, "c", new Vector3(-8f, 0f, -2f), new Vector3(8f, 4f, -2f));
                break;
            case "targets":
                // No link. Paths to targets off the NavMesh, by NavMesh.CalculatePath now and by
                // SetDestination over the next frames, each bisected for where it stops being found.
                agent = Agent(lane, "a", a, null);
                Targets(lane, agent);
                break;
            case "pendingold":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", new Vector3(-8f, 0f, 1.5f), b);
                // Behind the walking agent (a search).
                lane.Act = f => { if (f == 25) Log(lane, "set", agent.SetDestination(lane.O + new Vector3(-9f, 0f, 2f))); };
                break;
            case "aheadoff":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", new Vector3(-8f, 0f, -1.5f), new Vector3(8f, 0f, -1.5f));
                // Beyond B's side, ahead of the walking agent.
                lane.Act = f => { if (f == 25) Log(lane, "set", agent.SetDestination(lane.O + new Vector3(8f, 0f, 5f))); };
                break;
            case "incall":
                // Destinations whose path needs no search: on a standing agent's polygon, and on the
                // rest of a walking agent's path (no link: A and B are separate).
                agent = Agent(lane, "a", a, null);
                var walker = Agent(lane, "b", new Vector3(2.5f, 0f, -1.5f), new Vector3(10f, 0f, -1.5f));
                lane.Act = f =>
                {
                    if (f == 10) Log(lane, "set", agent.SetDestination(lane.O + new Vector3(-6f, 0f, 1f)));
                    if (f == 30) Log(lane, "set", walker.SetDestination(lane.O + new Vector3(9f, 0f, 1f)));
                };
                break;
            case "widetype":
                WideType(lane);
                break;
            case "turning":
                // Turning controls: slow with a low angularSpeed on A, fast with a high one on B.
                Agent(lane, "a", new Vector3(-10f, 0f, 2f), new Vector3(-3f, 0f, -2f), g => { g.speed = 1.5f; g.angularSpeed = 40f; });
                Agent(lane, "b", new Vector3(2.5f, 0f, -2f), new Vector3(10f, 0f, 2f), g => { g.speed = 6f; g.acceleration = 20f; g.angularSpeed = 400f; });
                break;
            case "staleowner":
                lane.Link = AddLink(lane, ls, le);
                agent = Agent(lane, "a", a, b);
                // On B only, clear of the link.
                var own = Agent(lane, "b", new Vector3(3f, 0f, 1.5f), new Vector3(10f, 0f, 1.5f));
                lane.Act = f =>
                {
                    if (f == 20) agent.areaMask = ~(1 << 3);
                    if (f == 20) own.SetAreaCost(3, 7f);
                    if (f == 50) Log(lane, "set", agent.SetDestination(lane.O + b));
                };
                break;
            case "low":
                // Link ends 0.3 m below the NavMesh.
                lane.Link = AddLink(lane, new Vector3(-2f, -0.3f, 0f), new Vector3(2f, -0.3f, 0f));
                Agent(lane, "a", a, b);
                break;
            case "lowmanual":
                lane.Link = AddLink(lane, new Vector3(-2f, -0.3f, 0f), new Vector3(2f, -0.3f, 0f));
                agent = Agent(lane, "a", a, b, g => g.autoTraverseOffMeshLink = false);
                lane.Act = f => { if (lane.OnLink >= 0 && f == lane.OnLink + 10) agent.CompleteOffMeshLink(); };
                break;
            default:
                throw new InvalidOperationException("no setup for lane " + lane.Name);
        }
    }

    /// <summary>A script moving the agent over a link itself (as Unity's link mover samples do):
    /// straight at the agent's speed, or along a 1 m high parabola in 0.5 s; then completes it.</summary>
    IEnumerator Traverse(NavMeshAgent agent, bool curve)
    {
        while (true)
        {
            if (agent.isOnOffMeshLink)
            {
                var data = agent.currentOffMeshLinkData;
                var start = agent.transform.position;
                var end = data.endPos + Vector3.up * agent.baseOffset;
                if (curve)
                {
                    float t = 0f;
                    while (t < 1f)
                    {
                        float y = 1f * 4f * (t - t * t);
                        agent.transform.position = Vector3.Lerp(start, end, t) + y * Vector3.up;
                        t += Time.deltaTime / 0.5f;
                        yield return null;
                    }
                }
                else
                {
                    while (agent.transform.position != end)
                    {
                        agent.transform.position = Vector3.MoveTowards(agent.transform.position, end, agent.speed * Time.deltaTime);
                        yield return null;
                    }
                }
                agent.CompleteOffMeshLink();
            }
            yield return null;
        }
    }

    // ------------------------------------------------------------------ targets off the NavMesh

    /// <summary>Platform A's targets (lane "targets"): beyond its west end, beyond its side, beyond
    /// its corner, above and below it, and over the gap towards B; the distance swept is how far
    /// beyond the platform's surface (the gap: how far from A's edge).</summary>
    static readonly (string Name, Func<float, Vector3> At, float Max)[] Sweeps =
    {
        ("west", d => new Vector3(-11f - d, 0f, 0f), 16f),
        // Up to 24 m: the radius 1 agent's search reaches past 16 m (rows are 40 m apart).
        ("side", d => new Vector3(-6f, 0f, 3f + d), 24f),
        ("corner", d => new Vector3(-11f - d, 0f, 3f + d), 16f),
        ("above", d => new Vector3(-6f, d, 0f), 16f),
        ("below", d => new Vector3(-6f, -d, 0f), 16f),
        ("gap", d => new Vector3(-1f + d, 0f, 0f), 2f),
    };

    const int BisectSteps = 17;
    /// <summary>The frame of the first SetDestination step.</summary>
    const int SweepStart = 2;

    /// <summary>Whether a path is found (the gap: a complete one).</summary>
    static bool Found(string sweep, bool found, NavMeshPathStatus status) =>
        found && (sweep != "gap" || status == NavMeshPathStatus.PathComplete);

    void Targets(Lane lane, NavMeshAgent agent)
    {
        var from = lane.O + new Vector3(-8f, 0f, 0f);
        var path = new NavMeshPath();
        // NavMesh.CalculatePath (and the agent's, for two of them) at fixed distances, then bisected.
        foreach (var (name, at, max) in Sweeps)
        {
            foreach (var d in new[] { 0.25f, 1f, 2f, 3f, 6f })
            {
                if (d > max) continue;
                bool found = NavMesh.CalculatePath(from, lane.O + at(d), NavMesh.AllAreas, path);
                Line(lane.Name, "-", 0, "\"sweep\":" + Text(name) + ",\"d\":" + N(d) + ",\"path\":" + B(found) + PathText(path));
            }
            float edge = Bisect(name, max, d => (NavMesh.CalculatePath(from, lane.O + at(d), NavMesh.AllAreas, path), path.status));
            Line(lane.Name, "-", 0, "\"who\":\"a\",\"sweep\":" + Text(name) + ",\"query\":\"NavMesh\",\"edge\":" + N(edge));
            if (name == "west" || name == "above")
            {
                edge = Bisect(name, max, d => (agent.CalculatePath(lane.O + at(d), path), path.status));
                Line(lane.Name, "-", 0, "\"who\":\"a\",\"sweep\":" + Text(name) + ",\"query\":\"agent\",\"edge\":" + N(edge));
            }
        }
        // Agents of other sizes on the same platform (their own radius and height), and one of the
        // second agent type on its own NavMesh (lane "widetype"): their own CalculatePath, then
        // SetDestination.
        var wide = Agent(lane, "b", new Vector3(-9f, 0f, -1.6f), null, g => g.radius = 1f);
        var tall = Agent(lane, "c", new Vector3(-5f, 0f, -1.6f), null, g => g.height = 4f);
        var small = Agent(lane, "d", new Vector3(-3f, 0f, 1.5f), null, g => g.radius = 0.25f);
        var middle = Agent(lane, "e", new Vector3(-3f, 0f, -1.5f), null, g => g.radius = 0.75f);
        foreach (var (who, other) in new[] { ("b", wide), ("c", tall), ("d", small), ("e", middle) })
        {
            foreach (int k in SizeSweeps)
            {
                var (name, at, max) = Sweeps[k];
                float edge = Bisect(name, max, d => (other.CalculatePath(lane.O + at(d), path), path.status));
                Line(lane.Name, "-", 0, "\"who\":" + Text(who) + ",\"sweep\":" + Text(name) + ",\"query\":\"agent\",\"edge\":" + N(edge));
            }
        }
        m_Runs.Add((lane, "a", agent, lane.O, AllSweeps));
        m_Runs.Add((lane, "b", wide, lane.O, SizeSweeps));
        m_Runs.Add((lane, "c", tall, lane.O, SizeSweeps));
        m_Runs.Add((lane, "d", small, lane.O, SizeSweeps));
        m_Runs.Add((lane, "e", middle, lane.O, SizeSweeps));
        lane.Act = f => StepRuns(f);
    }

    static readonly int[] AllSweeps = { 0, 1, 2, 3, 4, 5 };
    /// <summary>Beyond the side, above.</summary>
    static readonly int[] SizeSweeps = { 1, 3 };

    /// <summary>The largest distance (of <paramref name="max"/>) at which the query finds a path.</summary>
    static float Bisect(string sweep, float max, Func<float, (bool Found, NavMeshPathStatus Status)> query)
    {
        float lo = 0f, hi = max;
        for (int i = 0; i < BisectSteps; i++)
        {
            float mid = 0.5f * (lo + hi);
            var (found, status) = query(mid);
            if (Found(sweep, found, status)) lo = mid; else hi = mid;
        }
        return lo;
    }

    /// <summary>The SetDestination bisections, run one after the other (agent, its platform's origin, sweeps).</summary>
    readonly List<(Lane Lane, string Who, NavMeshAgent Agent, Vector3 O, int[] Sweeps)> m_Runs = new List<(Lane, string, NavMeshAgent, Vector3, int[])>();
    int m_Run, m_RunSweep, m_RunStep;
    float m_Low, m_High, m_Tried;
    Vector3 m_RunStart;

    /// <summary>One SetDestination bisection step per frame: set in Update, read in the next Update.</summary>
    void StepRuns(int f)
    {
        if (f < SweepStart || m_Run >= m_Runs.Count) return;
        var run = m_Runs[m_Run];
        if (f == SweepStart)
        {
            m_RunStart = run.Agent.transform.position;
            m_High = Sweeps[run.Sweeps[0]].Max;
        }
        else
        {
            var (name, _, _) = Sweeps[run.Sweeps[m_RunSweep]];
            var agent = run.Agent;
            bool found = !agent.pathPending && agent.hasPath;
            Line(run.Lane.Name, "-", f, "\"who\":" + Text(run.Who) + ",\"sweep\":" + Text(name) + ",\"d\":" + N(m_Tried) + ",\"pp\":" + B(agent.pathPending)
                + ",\"hp\":" + B(agent.hasPath) + ",\"ps\":" + Text(agent.pathStatus.ToString()) + ",\"pe\":" + V(agent.pathEndPosition)
                + ",\"dst\":" + V(agent.destination));
            if (Found(name, found, agent.pathStatus)) m_Low = m_Tried; else m_High = m_Tried;
            if (++m_RunStep == BisectSteps)
            {
                Line(run.Lane.Name, "-", f, "\"who\":" + Text(run.Who) + ",\"sweep\":" + Text(name) + ",\"query\":\"SetDestination\",\"edge\":" + N(m_Low));
                m_RunStep = 0;
                m_Low = 0f;
                if (++m_RunSweep >= run.Sweeps.Length)
                {
                    agent.ResetPath();
                    agent.Warp(m_RunStart);
                    m_RunSweep = 0;
                    if (++m_Run >= m_Runs.Count) return;
                    run = m_Runs[m_Run];
                    m_RunStart = run.Agent.transform.position;
                }
                m_High = Sweeps[run.Sweeps[m_RunSweep]].Max;
            }
        }
        var (sweep, at, _) = Sweeps[run.Sweeps[m_RunSweep]];
        run.Agent.ResetPath();
        run.Agent.Warp(m_RunStart);
        m_Tried = 0.5f * (m_Low + m_High);
        Line(run.Lane.Name, "-", f, "\"who\":" + Text(run.Who) + ",\"sweep\":" + Text(sweep) + ",\"set\":" + B(run.Agent.SetDestination(run.O + at(m_Tried))));
    }

    /// <summary>Platform A of the lane on a NavMesh of the second agent type, built now, with an
    /// agent of that type; its sweeps run after the targets lane's.</summary>
    void WideType(Lane lane)
    {
        int type = -1;
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            if (NavMesh.GetSettingsNameFromID(NavMesh.GetSettingsByIndex(i).agentTypeID) == "Wide") type = NavMesh.GetSettingsByIndex(i).agentTypeID;
        if (type == -1) throw new InvalidOperationException("no agent type Wide");
        var sources = new List<NavMeshBuildSource>
        {
            new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(10f, 1f, 6f),
                transform = Matrix4x4.Translate(lane.O + new Vector3(-6f, -0.5f, 0f)),
                area = 0,
            },
        };
        var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByID(type), sources,
            new Bounds(lane.O + new Vector3(-6f, 0f, 0f), new Vector3(14f, 4f, 10f)), Vector3.zero, Quaternion.identity);
        NavMesh.AddNavMeshData(data);
        // Off the line of the sweeps' targets (x = -6).
        var agent = Agent(lane, "a", new Vector3(-8f, 0f, -1f), null, g => g.agentTypeID = type);
        var filter = new NavMeshQueryFilter { agentTypeID = type, areaMask = NavMesh.AllAreas };
        var path = new NavMeshPath();
        var from = lane.O + new Vector3(-6f, 0f, 0f);
        Line(lane.Name, "-", 0, "\"type\":" + Text(NavMesh.GetSettingsByID(type).agentRadius + "/" + NavMesh.GetSettingsByID(type).agentHeight)
            + ",\"path\":" + B(NavMesh.CalculatePath(from, lane.O + new Vector3(-6f, 0f, 1f), filter, path)) + PathText(path));
        foreach (int k in SizeSweeps)
        {
            var (name, at, max) = Sweeps[k];
            float edge = Bisect(name, max, d => (NavMesh.CalculatePath(from, lane.O + at(d), filter, path), path.status));
            Line(lane.Name, "-", 0, "\"who\":\"a\",\"sweep\":" + Text(name) + ",\"query\":\"NavMesh\",\"edge\":" + N(edge));
        }
        m_Runs.Add((lane, "a", agent, lane.O, SizeSweeps));
    }

    static string PathText(NavMeshPath path)
    {
        var c = new StringBuilder();
        var corners = path.corners;
        for (int k = 0; k < corners.Length; k++) c.Append(k > 0 ? "," : "").Append(V(corners[k]));
        return ",\"ps\":" + Text(path.status.ToString()) + ",\"c\":[" + c + "]";
    }

    // ------------------------------------------------------------------ frames

    void Update()
    {
        if (m_Done) return;
        foreach (var lane in m_Lanes)
        {
            try { lane.Act?.Invoke(m_Frame + 1); }
            catch (Exception e)
            {
                Debug.LogException(e);
                Line(lane.Name, "-", m_Frame + 1, "\"error\":" + Text(e.GetType().Name + ": " + e.Message));
            }
        }
    }

    void LateUpdate()
    {
        if (m_Done) return;
        m_Frame++;
        try
        {
            foreach (var lane in m_Lanes)
            {
                foreach (var (name, agent) in lane.Agents)
                {
                    if (lane.OnLink < 0 && name == lane.Agents[0].Name && agent.isOnOffMeshLink) lane.OnLink = m_Frame;
                    Line(lane.Name, name, m_Frame, State(lane, agent));
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Line("-", "-", m_Frame, "\"error\":" + Text(e.GetType().Name + ": " + e.Message));
            Finish(1);
            return;
        }
        if (m_Frame >= Frames)
        {
            m_Done = true;
            StartCoroutine(RecordWaitingRebuildControls());
        }
    }

    IEnumerator RecordWaitingRebuildControls()
    {
        // Run after the original lanes so their reference records stay unchanged.
        foreach (var lane in m_Lanes)
            foreach (var (_, agent) in lane.Agents)
                if (agent != null) DestroyImmediate(agent.gameObject);
        var crossing = m_Lanes.Find(lane => lane.Name == "crosstile");
        var other = m_Lanes.Find(lane => lane.Name == "auto");
        foreach (string kind in new[] { "unrelated", "disabled", "alreadywait.unrelated", "alreadywait.disabled" })
        {
            var lane = new Lane { Name = "waitrebuild." + kind, O = crossing.O, Link = crossing.Link };
            crossing.Link.activated = true;
            var first = Agent(lane, "crossing", new Vector3(-2f, 0f, 0f), new Vector3(8f, 0f, 0f), a => a.speed = 0.5f);
            var waiting = Agent(lane, "waiting", new Vector3(-3.2f, 0f, -0.3f), new Vector3(8f, 0f, 0f));
            bool afterWaiting = kind.StartsWith("alreadywait.");
            for (int frame = 1; frame <= (afterWaiting ? 40 : 25); frame++)
            {
                yield return null;
                if (frame == (afterWaiting ? 20 : 8))
                {
                    Line(lane.Name, "-", frame, "\"waitingBeforeChange\":" + B(first.isOnOffMeshLink && !waiting.isOnOffMeshLink && waiting.desiredVelocity.sqrMagnitude == 0f));
                    (kind.EndsWith("unrelated") ? other.Link : crossing.Link).activated = false;
                }
                foreach (var (name, agent) in lane.Agents)
                    Line(lane.Name, name, frame, State(lane, agent));
            }
            foreach (var (_, agent) in lane.Agents) DestroyImmediate(agent.gameObject);
            crossing.Link.activated = true;
            other.Link.activated = true;
        }
        yield return RecordRotationControls();
        yield return RecordPathEndControls();
        yield return RecordStoppingControls();
        yield return RecordUpAxisEdges();
        yield return RecordRotationWrites();
        yield return RecordActivationControls();
        yield return RecordPendingActivationControls();
        Finish(0);
    }

    IEnumerator RecordRotationControls()
    {
        var source = m_Lanes.Find(lane => lane.Name == "auto");
        foreach (string kind in new[] { "tiny.positive", "tiny.negative", "wrap", "tilted", "tilted.align", "tilted.disabledRotation", "negated", "slow.below", "slow.above", "limit", "disabled" })
        {
            var lane = new Lane { Name = "rotation." + kind, O = source.O };
            var start = new Vector3(-8f, 0f, -1f);
            var destination = kind == "wrap" ? new Vector3(-8f, 0f, 1.5f) : new Vector3(-3f, 0f, -1f);
            var agent = Agent(lane, "a", start, destination, a =>
            {
                a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                a.updateUpAxis = kind == "tilted.align" || kind == "tilted.disabledRotation";
                a.transform.rotation = kind == "tiny.positive" ? Quaternion.Euler(0f, 89.9f, 0f)
                    : kind == "tiny.negative" ? Quaternion.Euler(0f, 90.1f, 0f)
                    : kind == "wrap" ? Quaternion.Euler(0f, 359.9f, 0f)
                    : kind.StartsWith("tilted") ? Quaternion.Euler(25f, 40f, 15f)
                    : kind == "negated" ? new Quaternion(0f, -0.34202015f, 0f, -0.9396926f)
                    : Quaternion.identity;
                if (kind.StartsWith("slow.")) { a.speed = kind.EndsWith("below") ? 0.02f : 0.05f; a.acceleration = 100f; }
                if (kind == "limit") a.angularSpeed = 5f;
                if (kind == "disabled" || kind == "tilted.disabledRotation") a.updateRotation = false;
            });
            for (int frame = 1; frame <= 80; frame++)
            {
                yield return null;
                Line(lane.Name, "a", frame, State(lane, agent) + ",\"yaw\":" + N(Mathf.DeltaAngle(0f, agent.transform.eulerAngles.y))
                    + ",\"up\":" + V(agent.transform.up));
            }
            DestroyImmediate(agent.gameObject);
        }
        var settings = NavMesh.GetSettingsByID(0);
        var floor = new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
            size = new Vector3(12f, 1f, 8f), transform = Matrix4x4.Translate(new Vector3(0f, -0.5f, 0f)) };
        var data = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource> { floor },
            new Bounds(Vector3.zero, new Vector3(16f, 6f, 12f)), Vector3.zero, Quaternion.identity);
        var origin = new Vector3(0f, 0f, -300f);
        var surfaceRotation = Quaternion.Euler(25f, 0f, 15f);
        var instance = NavMesh.AddNavMeshData(data, origin, surfaceRotation);
        foreach (bool align in new[] { false, true })
        {
            var lane = new Lane { Name = "rotation.surface." + (align ? "align" : "keep"), O = origin };
            var go = new GameObject(lane.Name);
            go.transform.position = origin;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.updateRotation = false;
            agent.updateUpAxis = align;
            agent.transform.rotation = Quaternion.identity;
            for (int frame = 1; frame <= 8; frame++)
            {
                yield return null;
                Line(lane.Name, "a", frame, "\"on\":" + B(agent.isOnNavMesh) + ",\"up\":" + V(agent.transform.up));
            }
            DestroyImmediate(go);
        }
        NavMesh.RemoveNavMeshData(instance);
        DestroyImmediate(data);

        var sources = new List<NavMeshBuildSource>();
        foreach (float x in new[] { -4f, 4f })
            sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(4f, 1f, 6f), transform = Matrix4x4.Translate(new Vector3(x, -0.5f, 0f)) });
        data = NavMeshBuilder.BuildNavMeshData(settings, sources,
            new Bounds(Vector3.zero, new Vector3(16f, 6f, 10f)), Vector3.zero, Quaternion.identity);
        origin = new Vector3(0f, 0f, -500f);
        instance = NavMesh.AddNavMeshData(data, origin, Quaternion.identity);
        var connection = NavMesh.AddLink(new NavMeshLinkData { startPosition = new Vector3(-2.5f, 0f, 0f),
            endPosition = new Vector3(2.5f, 0f, 0f), bidirectional = true, costModifier = -1f },
            origin, Quaternion.Euler(30f, 0f, 0f));
        var linkLane = new Lane { Name = "rotation.link.align", O = origin };
        var linkAgent = Agent(linkLane, "a", new Vector3(-4f, 0f, 0f), new Vector3(4f, 0f, 0f), a =>
        {
            a.updateRotation = false;
            a.updateUpAxis = true;
            a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        });
        for (int frame = 1; frame <= 180; frame++)
        {
            yield return null;
            Line(linkLane.Name, "a", frame, "\"on\":" + B(linkAgent.isOnNavMesh) + ",\"link\":"
                + B(linkAgent.isOnOffMeshLink) + ",\"up\":" + V(linkAgent.transform.up));
        }
        DestroyImmediate(linkAgent.gameObject);
        NavMesh.RemoveLink(connection);
        NavMesh.RemoveNavMeshData(instance);
        DestroyImmediate(data);
    }

    IEnumerator RecordPathEndControls()
    {
        var source = m_Lanes.Find(lane => lane.Name == "auto");
        var lane = new Lane { Name = "endpoint.stopping", O = source.O };
        var agent = Agent(lane, "a", new Vector3(-8f, 0f, -1f), new Vector3(-3f, 0f, -1f), a =>
        {
            a.stoppingDistance = 0.6f;
            a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        });
        for (int frame = 1; frame <= 100; frame++)
        {
            if (frame == 81) agent.ResetPath();
            yield return null;
            Line(lane.Name, "a", frame, State(lane, agent));
        }
        DestroyImmediate(agent.gameObject);
    }

    IEnumerator RecordStoppingControls()
    {
        var source = m_Lanes.Find(lane => lane.Name == "auto");
        foreach (bool brake in new[] { true, false })
        {
            var lane = new Lane { Name = brake ? "stopping.large.resume" : "stopping.noBrake", O = source.O };
            var agent = Agent(lane, "a", new Vector3(-8f, 0f, -1f), new Vector3(-3f, 0f, -1f), a =>
            {
                a.stoppingDistance = brake ? 1.5f : 0.6f;
                a.autoBraking = brake;
                a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            for (int frame = 1; frame <= 130; frame++)
            {
                if (frame == 101)
                {
                    if (brake) agent.stoppingDistance = 0f;
                    else agent.ResetPath();
                }
                yield return null;
                Line(lane.Name, "a", frame, State(lane, agent) + ",\"stoppingDistance\":" + N(agent.stoppingDistance));
            }
            DestroyImmediate(agent.gameObject);
        }
    }

    IEnumerator RecordUpAxisEdges()
    {
        var source = m_Lanes.Find(lane => lane.Name == "auto");
        foreach (bool inverted in new[] { false, true })
        {
            var lane = new Lane { Name = inverted ? "rotation.up.inverted" : "rotation.up.vertical", O = source.O };
            var agent = Agent(lane, "a", new Vector3(-8f, 0f, -1f), null, a =>
            {
                a.updateRotation = false;
                a.updateUpAxis = true;
                a.transform.rotation = inverted ? new Quaternion(1f, 0f, 0f, 0f) : Quaternion.Euler(90f, 40f, 0f);
            });
            for (int frame = 1; frame <= 4; frame++)
            {
                yield return null;
                Line(lane.Name, "a", frame, "\"on\":" + B(agent.isOnNavMesh) + ",\"q\":" + Q(agent.transform.rotation)
                    + ",\"up\":" + V(agent.transform.up));
            }
            DestroyImmediate(agent.gameObject);
        }
        var parent = new GameObject("rotation parent");
        parent.transform.rotation = Quaternion.Euler(0f, 20f, 0f);
        var parentLane = new Lane { Name = "rotation.up.inverted.parent", O = source.O };
        var child = Agent(parentLane, "a", new Vector3(-8f, 0f, -1f), null, a =>
        {
            a.updateRotation = false;
            a.updateUpAxis = true;
            a.transform.SetParent(parent.transform, true);
            a.transform.localRotation = new Quaternion(1f, 0f, 0f, 0f);
        });
        for (int frame = 1; frame <= 4; frame++)
        {
            yield return null;
            Line(parentLane.Name, "a", frame, "\"on\":" + B(child.isOnNavMesh) + ",\"q\":" + Q(child.transform.rotation)
                + ",\"up\":" + V(child.transform.up));
        }
        DestroyImmediate(parent);
    }

    IEnumerator RecordRotationWrites()
    {
        var source = m_Lanes.Find(lane => lane.Name == "auto");
        foreach (bool tilted in new[] { false, true })
        {
            var lane = new Lane { Name = tilted ? "rotation.write.tilted" : "rotation.write.unchanged", O = source.O };
            var agent = Agent(lane, "a", new Vector3(-8f, 0f, -1f), null, a =>
            {
                a.updatePosition = false;
                a.updateRotation = false;
                a.updateUpAxis = true;
                a.transform.rotation = tilted ? Quaternion.Euler(25f, 40f, 15f) : Quaternion.identity;
            });
            for (int frame = 1; frame <= (tilted ? 1 : 4); frame++)
            {
                agent.transform.hasChanged = false;
                yield return null;
                Line(lane.Name, "a", frame, "\"changed\":" + B(agent.transform.hasChanged) + ",\"up\":" + V(agent.transform.up));
            }
            DestroyImmediate(agent.gameObject);
        }
    }

    IEnumerator RecordActivationControls()
    {
        var source = m_Lanes.Find(lane => lane.Name == "crosstile");
        foreach (string kind in new[] { "far", "near", "toggle" })
        foreach (bool repath in new[] { true, false })
        {
            var lane = new Lane { Name = "activation." + kind + (repath ? ".repath" : ".noRepath"), O = source.O, Link = source.Link };
            source.Link.activated = true;
            var agent = Agent(lane, "a", new Vector3(kind == "near" ? -3.2f : -8f, 0f, -0.3f), new Vector3(8f, 0f, 0f), a =>
            {
                a.speed = 0f;
                a.autoRepath = repath;
                a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            yield return null;
            Line(lane.Name, "a", 1, State(lane, agent));
            source.Link.activated = false;
            if (kind == "toggle") source.Link.activated = true;
            var query = new NavMeshPath();
            NavMesh.CalculatePath(lane.O + new Vector3(-8f, 0f, 0f), lane.O + new Vector3(8f, 0f, 0f), NavMesh.AllAreas, query);
            Line(lane.Name, "a", 2, State(lane, agent) + ",\"queryStatus\":" + Text(query.status.ToString()));
            yield return null;
            Line(lane.Name, "a", 3, State(lane, agent));
            source.Link.activated = true;
            Line(lane.Name, "a", 4, State(lane, agent));
            yield return null;
            Line(lane.Name, "a", 5, State(lane, agent));
            DestroyImmediate(agent.gameObject);
        }
    }

    IEnumerator RecordPendingActivationControls()
    {
        var source = m_Lanes.Find(lane => lane.Name == "crosstile");
        int budget = NavMesh.pathfindingIterationsPerFrame;
        NavMesh.pathfindingIterationsPerFrame = 1;
        foreach (int steps in new[] { 1, 2 })
        {
            var lane = new Lane { Name = "activation.pending." + steps, O = source.O, Link = source.Link };
            source.Link.activated = true;
            var agent = Agent(lane, "a", new Vector3(-8f, 0f, 0f), new Vector3(8f, 0f, 0f), a =>
            {
                a.speed = 0f;
                a.autoRepath = false;
                a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            for (int i = 0; i < steps; i++) yield return null;
            Line(lane.Name, "-", 1, "\"pendingBeforeChange\":" + B(agent.pathPending));
            source.Link.activated = false;
            yield return null;
            for (int i = 0; i < 30 && agent.pathPending; i++) yield return null;
            var query = new NavMeshPath();
            NavMesh.CalculatePath(lane.O + new Vector3(-8f, 0f, 0f), lane.O + new Vector3(8f, 0f, 0f), NavMesh.AllAreas, query);
            Line(lane.Name, "a", 2, State(lane, agent) + ",\"queryStatus\":" + Text(query.status.ToString()));
            yield return null;
            Line(lane.Name, "a", 3, State(lane, agent));
            DestroyImmediate(agent.gameObject);
        }
        source.Link.activated = true;
        NavMesh.pathfindingIterationsPerFrame = budget;
    }

    string State(Lane lane, NavMeshAgent agent)
    {
        var s = new StringBuilder();
        s.Append("\"dt\":").Append(N(Time.deltaTime));
        s.Append(",\"p\":").Append(V(agent.transform.position));
        var q = agent.transform.rotation;
        s.Append(",\"q\":").Append(Q(q));
        s.Append(",\"n\":").Append(V(agent.nextPosition));
        s.Append(",\"v\":").Append(V(agent.velocity));
        s.Append(",\"dv\":").Append(V(agent.desiredVelocity));
        s.Append(",\"st\":").Append(V(agent.steeringTarget));
        s.Append(",\"rd\":").Append(N(agent.remainingDistance));
        s.Append(",\"hp\":").Append(B(agent.hasPath));
        s.Append(",\"pp\":").Append(B(agent.pathPending));
        s.Append(",\"on\":").Append(B(agent.isOnNavMesh));
        s.Append(",\"ps\":").Append(Text(agent.pathStatus.ToString()));
        s.Append(",\"ol\":").Append(B(agent.isOnOffMeshLink));
        s.Append(",\"stop\":").Append(B(agent.isStopped));
        s.Append(",\"auto\":").Append(B(agent.autoTraverseOffMeshLink));
        s.Append(",\"dst\":").Append(V(agent.destination));
        s.Append(",\"pe\":").Append(V(agent.pathEndPosition));
        s.Append(",\"stale\":").Append(B(agent.isPathStale));
        var owner = agent.navMeshOwner;
        s.Append(",\"own\":").Append(Text(owner == null ? "null" : owner.name + ":" + owner.GetType().Name));
        s.Append(",\"c\":[");
        var corners = agent.path.corners;
        for (int i = 0; i < corners.Length; i++) s.Append(i > 0 ? "," : "").Append(V(corners[i]));
        s.Append(']');
        s.Append(Links(lane, agent));
        return s.ToString();
    }

    string Links(Lane lane, NavMeshAgent agent)
    {
        var s = new StringBuilder();
        s.Append(",\"cur\":").Append(Data(agent.currentOffMeshLinkData));
        s.Append(",\"nxt\":").Append(Data(agent.nextOffMeshLinkData));
        if (lane.Link != null) s.Append(",\"lk\":[").Append(B(lane.Link.occupied)).Append(',').Append(B(lane.Link.activated)).Append(']');
#pragma warning disable CS0618
        if (lane.Legacy != null) s.Append(",\"lk\":[").Append(B(lane.Legacy.occupied)).Append(',').Append(B(lane.Legacy.activated)).Append(']');
#pragma warning restore CS0618
        return s.ToString();
    }

    static string Data(OffMeshLinkData d)
    {
        var owner = d.owner;
        return "{\"valid\":" + B(d.valid) + ",\"act\":" + B(d.activated) + ",\"type\":" + Text(d.linkType.ToString())
            + ",\"s\":" + V(d.startPos) + ",\"e\":" + V(d.endPos)
            + ",\"owner\":" + Text(owner == null ? "null" : owner.name + ":" + owner.GetType().Name) + "}";
    }

    void Log(Lane lane, string what, bool result) =>
        Line(lane.Name, "-", m_Frame + 1, "\"" + what + "\":" + B(result));

    void Line(string lane, string agent, int frame, string body) =>
        m_Out.Append("{\"lane\":").Append(Text(lane)).Append(",\"agent\":").Append(Text(agent)).Append(",\"f\":").Append(frame)
            .Append(',').Append(body).Append("}\n");

    void Finish(int code)
    {
        m_Done = true;
        var path = Environment.GetEnvironmentVariable("NAVLINKS_OUT");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, m_Out.ToString());
        Debug.Log("[navlinks] written " + path);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    // ------------------------------------------------------------------ formatting

    static string N(float v) =>
        float.IsNaN(v) || float.IsInfinity(v) ? "\"" + v.ToString(CultureInfo.InvariantCulture) + "\"" : v.ToString("R", CultureInfo.InvariantCulture);

    static string V(Vector3 v) => "[" + N(v.x) + "," + N(v.y) + "," + N(v.z) + "]";

    static string Q(Quaternion q) => "[" + N(q.x) + "," + N(q.y) + "," + N(q.z) + "," + N(q.w) + "]";

    static string B(bool b) => b ? "true" : "false";

    static string Text(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s ?? "null")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
