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
                // Then the schedule of lane "inactive" with autoRepath off: a partial path while the
                // link is off, the link switched on midway, a new destination later.
                lane.Link.activated = false;
                agent = Agent(lane, "a", a, b, g => g.autoRepath = false);
                lane.Act = f =>
                {
                    if (f == 100) lane.Link.activated = true;
                    if (f == 200) agent.SetDestination(lane.O + b);
                };
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
        if (m_Frame >= Frames) Finish(0);
    }

    string State(Lane lane, NavMeshAgent agent)
    {
        var s = new StringBuilder();
        s.Append("\"dt\":").Append(N(Time.deltaTime));
        s.Append(",\"p\":").Append(V(agent.transform.position));
        var q = agent.transform.rotation;
        s.Append(",\"q\":[").Append(N(q.x)).Append(',').Append(N(q.y)).Append(',').Append(N(q.z)).Append(',').Append(N(q.w)).Append(']');
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
