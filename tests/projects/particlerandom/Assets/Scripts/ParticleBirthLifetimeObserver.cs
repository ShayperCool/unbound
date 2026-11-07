using UnityEngine;

public class ParticleBirthLifetimeObserver : MonoBehaviour
{
    public int calls;
    void OnParticleSystemStopped() { calls++; }
}
