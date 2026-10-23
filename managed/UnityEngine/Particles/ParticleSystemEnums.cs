using System;

// Particle system enums that Enums.Generated.cs does not list (the rest of the module's enums are there).

namespace UnityEngine
{
    public enum ParticleSystemGravitySource
    {
        Physics3D = 0,
        Physics2D = 1,
    }

    [Flags]
    public enum ParticleSystemBakeMeshOptions
    {
        Default = 0,
        BakeRotationAndScale = 1,
        BakePosition = 2,
    }

    [Flags]
    public enum ParticleSystemBakeTextureOptions
    {
        Default = 4,
        BakeRotationAndScale = 1,
        BakePosition = 2,
        PerVertex = 4,
        PerParticle = 8,
        IncludeParticleIndices = 16,
    }
}

namespace UnityEngine.Rendering
{
    [Flags]
    public enum UVChannelFlags
    {
        UV0 = 1,
        UV1 = 2,
        UV2 = 4,
        UV3 = 8,
    }
}
