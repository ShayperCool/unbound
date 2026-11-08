using UnityEngine;

/// <summary>The probe's level, shared by <c>ProbeSetup</c> (which builds and bakes it) and
/// <see cref="CostProbe"/> (which measures it): nine 12 m square rigs, each on its own ground.</summary>
public static class Rigs
{
    public const float Size = 12f;
    public const int Walkable = 0, NotWalkable = 1, Water = 3, Mud = 4;

    public struct Patch
    {
        public string Name;
        public Vector3 Center, Size;
        public float Height, Yaw;
        public int Area;
    }

    public struct Rig
    {
        public string Name;
        public Vector3 Origin;
        public Patch[] Patches;
    }

    static Patch Area(string name, Vector3 center, Vector3 size, int area) =>
        new Patch { Name = name, Center = center, Size = size, Height = 0.04f, Area = area };

    static Patch Obstacle(Vector3 center, Vector3 size, float yaw = 0f) =>
        new Patch { Name = "Obstacle", Center = center, Size = size, Height = 2f, Yaw = yaw, Area = NotWalkable };

    // Rig origins: three by three, 14 m apart (2 m gaps between the grounds).
    static Vector3 At(int i, int j) => new Vector3(7f + 14f * i, 0f, 7f + 14f * j);

    public static readonly Rig[] All =
    {
        // A 1 m Mud strip across the way from z = -4 to z = 4, open at its +x end (x 4..6).
        new Rig { Name = "strip1", Origin = At(0, 0), Patches = new[] { Area("Mud", new Vector3(-1f, 0f, 0f), new Vector3(10f, 0f, 1f), Mud) } },
        // A 2 m strip, off the rig's middle.
        new Rig { Name = "strip2", Origin = At(1, 0), Patches = new[] { Area("Mud", new Vector3(-1f, 0f, 0.3f), new Vector3(10f, 0f, 2f), Mud) } },
        // A 0.6 m Mud strip with a Water patch touching its north side (raycasts and two areas).
        new Rig { Name = "rays", Origin = At(2, 0), Patches = new[]
        {
            Area("Mud", new Vector3(-1f, 0f, 0f), new Vector3(10f, 0f, 0.6f), Mud),
            Area("Water", new Vector3(0f, 0f, 0.8f), new Vector3(2f, 0f, 1f), Water),
        } },
        // Baked obstacles: a 2 m box, a 1 x 3 bar off the grid, a turned 3 x 1 bar.
        new Rig { Name = "box", Origin = At(0, 1), Patches = new[] { Obstacle(Vector3.zero, new Vector3(2f, 0f, 2f)) } },
        new Rig { Name = "bar", Origin = At(1, 1), Patches = new[] { Obstacle(new Vector3(0.37f, 0f, 0.21f), new Vector3(1f, 0f, 3f)) } },
        new Rig { Name = "turned", Origin = At(2, 1), Patches = new[] { Obstacle(Vector3.zero, new Vector3(3f, 0f, 1f), 30f) } },
        // Carved at runtime (CostProbe.Carvers): the 2 m box; the 1 x 3 bar beside a Mud patch.
        new Rig { Name = "carvebox", Origin = At(0, 2), Patches = new Patch[0] },
        new Rig { Name = "carvebar", Origin = At(1, 2), Patches = new[] { Area("Mud", new Vector3(3f, 0f, 0f), new Vector3(2f, 0f, 4f), Mud) } },
        // Agents placed off the NavMesh's height (scene-placed and added by the probe).
        new Rig { Name = "agents", Origin = At(2, 2), Patches = new Patch[0] },
    };

    public static Rig Get(string name)
    {
        foreach (var rig in All)
            if (rig.Name == name) return rig;
        throw new System.ArgumentException(name);
    }

    public struct SceneAgent
    {
        public string Name;
        public Vector3 Position;
    }

    /// <summary>Agents saved in the scene (enabled when it loads): at the ground's height (below the
    /// NavMesh), above it, and beside the NavMesh's edge.</summary>
    public static readonly SceneAgent[] SceneAgents =
    {
        new SceneAgent { Name = "sceneGround", Position = At(2, 2) + new Vector3(-4f, 0f, -4f) },
        new SceneAgent { Name = "sceneHigh", Position = At(2, 2) + new Vector3(-4f, 0.3f, -2.5f) },
        new SceneAgent { Name = "sceneEdge", Position = At(2, 2) + new Vector3(-5.8f, 0f, -1f) },
    };
}
