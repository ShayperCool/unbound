using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using PL = UnityEngine.PlayerLoop;

namespace Unbound.Runtime
{
    /// <summary>
    /// The cloths: one NvCloth solver (native/unbound_cloth.cpp) stepped after the frame's scripts
    /// (PostLateUpdate.PhysicsSkinnedClothBeginUpdate) by the fixed time step, once per physics step
    /// the frame ran (measured: a cloth's step is 0.02 s at 60 frames per second, some frames none).
    /// </summary>
    internal static class ClothWorld
    {
        static readonly List<Cloth> s_Cloths = new List<Cloth>();
        static bool s_Registered;
        static int s_Steps;

        static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            PlayerLoopDriver.Register(typeof(PL.FixedUpdate.PhysicsFixedUpdate), () => s_Steps++);
            PlayerLoopDriver.Register(typeof(PL.PostLateUpdate.PhysicsSkinnedClothBeginUpdate), Step);
        }

        internal static void Add(Cloth cloth)
        {
            EnsureRegistered();
            if (s_Cloths.Contains(cloth)) return;
            s_Cloths.Add(cloth);
            // Created at its first step, from the pose then (a scale set after adding counts).
            cloth.Release();
            cloth.m_SetupPending = true;
        }

        internal static void Remove(Cloth cloth)
        {
            s_Cloths.Remove(cloth);
            cloth.Release();
        }

        internal static void Reset()
        {
            foreach (var cloth in s_Cloths) cloth.Release();
            s_Cloths.Clear();
            s_Steps = 0;
        }

        static void Step()
        {
            int steps = s_Steps;
            s_Steps = 0;
            if (steps == 0 || s_Cloths.Count == 0) return;
            float dt = Time.fixedDeltaTime;
            ClothNative.uac_inter_collision(Physics.interCollisionDistance, Physics.interCollisionStiffness, Physics.interCollisionSettingsToggle ? 1 : 0);
            var cloths = s_Cloths.ToArray();
            for (int k = 0; k < steps; k++)
            {
                bool any = false;
                foreach (var cloth in cloths)
                {
                    if (cloth.m_SetupPending && cloth.ShouldSimulate) cloth.Setup();
                    bool simulate = cloth.m_Native != 0 && cloth.ShouldSimulate;
                    ClothNative.uac_cloth_set_simulated(cloth.m_Native, simulate ? 1 : 0);
                    if (!simulate) continue;
                    cloth.BeforeStep();
                    any = true;
                }
                if (!any) return;
                ClothNative.uac_simulate(dt);
                foreach (var cloth in cloths)
                {
                    if (cloth.m_Native == 0 || !cloth.ShouldSimulate) continue;
                    cloth.Fade(dt);
                    cloth.AfterStep();
                    if (cloth.m_FadedOut)
                    {
                        cloth.m_FadedOut = false;
                        cloth.enabled = false;
                    }
                }
            }
        }
    }

    /// <summary>The flat C API of native/unbound_cloth.cpp (in the unbound_physx library).</summary>
    internal static unsafe class ClothNative
    {
        const string Library = "unbound_physx";

        [StructLayout(LayoutKind.Sequential)]
        internal struct Params
        {
            internal Vector3 Gravity;
            internal Vector3 Damping;
            internal Vector3 LinearDrag;
            internal Vector3 AngularDrag;
            internal Vector3 LinearInertia;
            internal Vector3 AngularInertia;
            internal Vector3 CentrifugalInertia;
            internal float SolverFrequency;
            internal float StiffnessFrequency;
            internal float StretchStiffness;
            internal float ShearStiffness;
            internal float BendStiffness;
            internal float TetherStiffness;
            internal float TetherScale;
            internal float Friction;
            internal float CollisionMassScale;
            internal int ContinuousCollision;
            internal float SelfCollisionDistance;
            internal float SelfCollisionStiffness;
            internal float SleepThreshold;
            internal float MotionConstraintStiffness;
        }

        [DllImport(Library)] internal static extern ulong uac_cloth_create(float* particles, int count, uint* triangles, int triangleCount, Vector3 gravity, int geodesicTethers);
        [DllImport(Library)] internal static extern void uac_cloth_destroy(ulong cloth);
        [DllImport(Library)] internal static extern void uac_cloth_configure(ulong cloth, in Params p);
        [DllImport(Library)] internal static extern void uac_cloth_set_frame(ulong cloth, Vector3 position, Quaternion rotation);
        [DllImport(Library)] internal static extern void uac_cloth_teleport(ulong cloth, Vector3 position, Quaternion rotation);
        [DllImport(Library)] internal static extern void uac_cloth_clear_inertia(ulong cloth);
        [DllImport(Library)] internal static extern void uac_cloth_motion_constraints(ulong cloth, float* values, int count);
        [DllImport(Library)] internal static extern void uac_cloth_separation_constraints(ulong cloth, float* values, int count);
        [DllImport(Library)] internal static extern void uac_cloth_collision(ulong cloth, float* spheres, int sphereCount, uint* capsules, int capsuleCount);
        [DllImport(Library)] internal static extern void uac_cloth_virtual_particles(ulong cloth, uint* indices, int count, float* weights, int weightCount);
        [DllImport(Library)] internal static extern void uac_cloth_self_collision_indices(ulong cloth, uint* indices, int count);
        [DllImport(Library)] internal static extern void uac_cloth_set_particles(ulong cloth, float* values, int count, int previousToo);
        [DllImport(Library)] internal static extern int uac_cloth_particles(ulong cloth, float* output, int capacity);
        [DllImport(Library)] internal static extern void uac_cloth_set_simulated(ulong cloth, int simulated);
        [DllImport(Library)] internal static extern void uac_simulate(float dt);
        [DllImport(Library)] internal static extern void uac_inter_collision(float distance, float stiffness, int iterations);
    }
}
