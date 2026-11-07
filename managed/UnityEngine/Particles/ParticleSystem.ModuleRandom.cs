namespace UnityEngine
{
    public sealed partial class ParticleSystem
    {
        // Measured in Unity 6000.0.71f1 by tests/projects/particlerandom.
        // Modules restart a generator from the particle seed plus their own salt.
        // Vector components consume successive draws; size and rotation share one.
        static Random.State ModuleRandom(uint seed, uint salt) =>
            Random.State.FromSeed(unchecked((int)(seed + salt)));

        static float ModuleValue(uint seed, uint salt)
        {
            var random = ModuleRandom(seed, salt);
            return random.NextValue();
        }

        static Vector3 ModuleVector(uint seed, uint salt)
        {
            var random = ModuleRandom(seed, salt);
            return ModuleVector(ref random);
        }

        static Vector3 ModuleVector(ref Random.State random) =>
            new Vector3(random.NextValue(), random.NextValue(), random.NextValue());

        static uint ModuleWord(uint seed, uint salt)
        {
            var random = ModuleRandom(seed, salt);
            random.NextValue();
            return random.S3;
        }

        public partial struct PlaybackState
        {
            internal Random.State m_EmissionRandom;
            internal Random.State m_ForceRandom0, m_ForceRandom1, m_ForceRandom2, m_ForceRandom3;
        }

        Random.State m_EmissionRandom;
        void EnsureModuleRandom()
        {
            if (!m_InitialRandomReady) BeginPlaybackRandom();
        }

        float NextEmissionValue()
        {
            EnsureModuleRandom();
            return m_EmissionRandom.NextValue();
        }

        readonly Random.State[] m_ForceRandom = new Random.State[4];
        readonly Vector3[] m_ForceSamples = new Vector3[4];
        uint m_ModuleGroupSeed;
        int m_ModuleLane;

        // Speed modification shares the first particle's seed in each group of four.
        // Randomized force has persistent lanes, advancing three draws per group.
        void BeginModuleSample(int index, uint seed)
        {
            m_ModuleLane = index & 3;
            if (m_ModuleLane != 0) return;
            m_ModuleGroupSeed = seed;
            if (m_Force.enabled && m_Force.randomized && m_Force.x.IsRandom)
            {
                EnsureModuleRandom();
                for (int lane = 0; lane < 4; lane++)
                    m_ForceSamples[lane] = ModuleVector(ref m_ForceRandom[lane]);
            }
        }
    }
}
