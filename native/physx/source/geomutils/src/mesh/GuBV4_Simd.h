// Unbound addition to the vendored PhysX 4.1.2 tree (see native/physx/UNBOUND_PATCHES.md).
//
// The BV4 midphase is written with SSE2 intrinsics and upstream compiles it on Intel CPUs only.
// GU_BV4_SIMD enables it wherever those intrinsics are available: on Intel natively and on arm64
// NEON through the definitions below (some use AArch64-only instructions). Each definition gives the same bits as the SSE2
// instruction of the same name, so arm64 runs the same BV4 traversal as Intel. The vector math
// layer BV4 also calls (Ps::aos) has its own NEON implementation and may round differently.

#ifndef GU_BV4_SIMD_H
#define GU_BV4_SIMD_H

#include "foundation/PxPreprocessor.h"

#if (PX_INTEL_FAMILY || (PX_A64 && PX_NEON)) && !defined(PX_SIMD_DISABLED)
	#define GU_BV4_SIMD 1
#else
	#define GU_BV4_SIMD 0
#endif

#if PX_A64 && PX_NEON && GU_BV4_SIMD
#include <arm_neon.h>

typedef float32x4_t	__m128;
typedef int32x4_t	__m128i;

#define _MM_SHUFFLE(z, y, x, w)	(((z) << 6) | ((y) << 4) | ((x) << 2) | (w))

// Lane i of the result is lane (imm >> 2i) & 3 of a; imm is a constant.
#define _mm_shuffle_epi32(a, imm)	\
	__builtin_shufflevector((a), (a), (imm) & 3, ((imm) >> 2) & 3, ((imm) >> 4) & 3, ((imm) >> 6) & 3)

static PX_FORCE_INLINE __m128i	_mm_castps_si128(__m128 a)		{ return vreinterpretq_s32_f32(a);	}
static PX_FORCE_INLINE __m128	_mm_castsi128_ps(__m128i a)		{ return vreinterpretq_f32_s32(a);	}

static PX_FORCE_INLINE __m128 _mm_and_ps(__m128 a, __m128 b)
{
	return vreinterpretq_f32_u32(vandq_u32(vreinterpretq_u32_f32(a), vreinterpretq_u32_f32(b)));
}

static PX_FORCE_INLINE __m128 _mm_or_ps(__m128 a, __m128 b)
{
	return vreinterpretq_f32_u32(vorrq_u32(vreinterpretq_u32_f32(a), vreinterpretq_u32_f32(b)));
}

// All ones where a > b (false for NaN), as SSE's ordered comparison.
static PX_FORCE_INLINE __m128 _mm_cmpgt_ps(__m128 a, __m128 b)	{ return vreinterpretq_f32_u32(vcgtq_f32(a, b));	}

// Bit i is the sign bit of lane i.
static PX_FORCE_INLINE int _mm_movemask_ps(__m128 a)
{
	const int32_t shifts[4] = { 0, 1, 2, 3 };
	const uint32x4_t signs = vshrq_n_u32(vreinterpretq_u32_f32(a), 31);
	return int(vaddvq_u32(vshlq_u32(signs, vld1q_s32(shifts))));
}

static PX_FORCE_INLINE __m128	_mm_load1_ps(const float* p)			{ return vld1q_dup_f32(p);	}
static PX_FORCE_INLINE void		_mm_store_ps(float* p, __m128 a)		{ vst1q_f32(p, a);			}
static PX_FORCE_INLINE __m128i	_mm_load_si128(const __m128i* p)		{ return vld1q_s32(reinterpret_cast<const int32_t*>(p));	}

// Arguments from the highest lane to the lowest, as in SSE.
static PX_FORCE_INLINE __m128 _mm_set_ps(float e3, float e2, float e1, float e0)
{
	const float lanes[4] = { e0, e1, e2, e3 };
	return vld1q_f32(lanes);
}

static PX_FORCE_INLINE __m128i _mm_set_epi32(int e3, int e2, int e1, int e0)
{
	const int32_t lanes[4] = { e0, e1, e2, e3 };
	return vld1q_s32(lanes);
}

static PX_FORCE_INLINE __m128i	_mm_set1_epi32(int a)						{ return vdupq_n_s32(a);	}
static PX_FORCE_INLINE __m128i	_mm_and_si128(__m128i a, __m128i b)			{ return vandq_s32(a, b);	}
// Round to nearest even, as SSE in its default rounding mode.
static PX_FORCE_INLINE __m128	_mm_cvtepi32_ps(__m128i a)					{ return vcvtq_f32_s32(a);	}

// Shifts by a count from 0 to 31; a negative NEON count shifts right, arithmetically for signed lanes.
static PX_FORCE_INLINE __m128i _mm_srai_epi32(__m128i a, int count)	{ return vshlq_s32(a, vdupq_n_s32(-count));	}
static PX_FORCE_INLINE __m128i _mm_slli_epi32(__m128i a, int count)	{ return vshlq_s32(a, vdupq_n_s32(count));	}

#endif	// PX_A64 && PX_NEON && GU_BV4_SIMD

#endif	// GU_BV4_SIMD_H
