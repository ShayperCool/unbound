// Regenerate the portable reciprocal seeds on the Linux x86 reference host:
// c++ -O2 tools/validation/particle_reciprocal_seeds.cpp -o <cache>/reciprocal-seeds
// <cache>/reciprocal-seeds | base64 -w 120
// The output is the base64 initializer in ParticleLifetimeReciprocal.cs.
// This records arithmetic estimates, independently of particle fixtures.
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <xmmintrin.h>

int main()
{
    for (uint32_t index = 0; index < 4096; ++index)
    {
        uint32_t bits = 0x3f800000u | (index << 11);
        float input;
        std::memcpy(&input, &bits, sizeof input);
        float seed = _mm_cvtss_f32(_mm_rcp_ss(_mm_set_ss(input)));
        std::memcpy(&bits, &seed, sizeof bits);
        uint32_t mantissa = (bits & 0x7fffffu) >> 11;
        std::putchar(mantissa & 255);
        std::putchar(mantissa >> 8);
    }
}
