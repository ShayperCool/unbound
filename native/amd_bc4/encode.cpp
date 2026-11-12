//===============================================================================
// Copyright (c) 2007-2024  Advanced Micro Devices, Inc. All rights reserved.
// Copyright (c) 2004-2006 ATI Technologies Inc.
//===============================================================================
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//
//  File Name:   ATIXCodec.cpp
//  Description: Performs the DXT-style block compression
//
//
//////////////////////////////////////////////////////////////////////////////

#include <algorithm>
#include <cmath>
#include <cstring>
#include <cstdlib>
#include <cstdint>
using CODECFLOAT=float;
using CMP_BYTE=uint8_t;
using BYTE=uint8_t;
using DWORD=uint32_t;
using CMP_WORD=uint16_t;
using std::min;
using std::max;
#define MAX_BLOCK 64
#define MAX_POINTS 16
#define NUM_ENDPOINTS 2
#define MAX_ERROR 128000.f
#define GBL_SCH_STEP 0.018f
#define GBL_SCH_EXT 0.1f
#define LCL_SCH_STEP 0.6f
#define SCH_STPS 3
#define ALIGN_16
static const float sMvF[3]={0.f,-1.f,1.f};
static CODECFLOAT Clstr1(CMP_BYTE*  pcIndices,
                         CODECFLOAT _blockIn[MAX_BLOCK],
                         CODECFLOAT _ramp[NUM_ENDPOINTS],
                         int        _NmbrClrs,
                         int        nNumPoints,
                         bool       bFixedRampPoints,
                         int        _intPrec    = 8,
                         int        _fracPrec   = 0,
                         bool       _bFixedRamp = true);

static void BldRmp1(CODECFLOAT _Rmp[MAX_POINTS], CODECFLOAT _InpRmp[NUM_ENDPOINTS], int nNumPoints)
{
    // for 3 point ramp; not to select the 4th point in min
    for (int e = nNumPoints; e < MAX_POINTS; e++)
        _Rmp[e] = 100000.f;

    _Rmp[0] = _InpRmp[0];
    _Rmp[1] = _InpRmp[1];
    for (int e = 1; e < nNumPoints - 1; e++)
        _Rmp[e + 1] = (_Rmp[0] * (nNumPoints - 1 - e) + _Rmp[1] * e) / (CODECFLOAT)(nNumPoints - 1);
}

static void
GetRmp1(CODECFLOAT _rampDat[MAX_POINTS], CODECFLOAT _ramp[NUM_ENDPOINTS], int nNumPoints, bool bFixedRampPoints, int _intPrec, int _fracPrec, bool _bFixedRamp)
{
    if (_ramp[0] == _ramp[1])
        return;

    if (!bFixedRampPoints && _ramp[0] <= _ramp[1] || bFixedRampPoints && _ramp[0] > _ramp[1])
    {
        CODECFLOAT t = _ramp[0];
        _ramp[0]     = _ramp[1];
        _ramp[1]     = t;
    }

    _rampDat[0] = _ramp[0];
    _rampDat[1] = _ramp[1];

    CODECFLOAT IntFctr  = (CODECFLOAT)(1 << _intPrec);
    CODECFLOAT FracFctr = (CODECFLOAT)(1 << _fracPrec);

    CODECFLOAT ramp[NUM_ENDPOINTS];
    ramp[0] = _ramp[0] * FracFctr;
    ramp[1] = _ramp[1] * FracFctr;

    BldRmp1(_rampDat, ramp, nNumPoints);
    if (bFixedRampPoints)
    {
        _rampDat[nNumPoints]     = 0.;
        _rampDat[nNumPoints + 1] = FracFctr * IntFctr - 1.f;
    }

    if (_bFixedRamp)
    {
        for (int i = 0; i < nNumPoints; i++)
        {
            _rampDat[i] = floor(_rampDat[i] + 0.5f);
            _rampDat[i] /= FracFctr;
        }
    }
}

static CODECFLOAT Clstr1(CMP_BYTE*  pcIndices,
                         CODECFLOAT _blockIn[MAX_BLOCK],
                         CODECFLOAT _ramp[NUM_ENDPOINTS],
                         int        _NmbrClrs,
                         int        nNumPoints,
                         bool       bFixedRampPoints,
                         int        _intPrec,
                         int        _fracPrec,
                         bool       _bFixedRamp)
{
    CODECFLOAT Err = 0.f;
    CODECFLOAT alpha[MAX_POINTS];

    for (int i = 0; i < _NmbrClrs; i++)
        pcIndices[i] = 0;

    if (_ramp[0] == _ramp[1])
        return Err;

    if (!_bFixedRamp)
    {
        _intPrec  = 8;
        _fracPrec = 0;
    }

    GetRmp1(alpha, _ramp, nNumPoints, bFixedRampPoints, _intPrec, _fracPrec, _bFixedRamp);

    if (bFixedRampPoints)
        nNumPoints += 2;

    const CODECFLOAT OverIntFctr = 1.f / ((CODECFLOAT)(1 << _intPrec) - 1.f);
    for (int i = 0; i < nNumPoints; i++)
        alpha[i] *= OverIntFctr;

    // For each colour in the original block, calculate its weighted
    // distance from each point in the original and assign it
    // to the closest cluster
    for (int i = 0; i < _NmbrClrs; i++)
    {
        CODECFLOAT shortest = 10000000.f;

        // Get the original alpha
        CODECFLOAT acur = _blockIn[i];

        for (BYTE j = 0; j < nNumPoints; j++)
        {
            CODECFLOAT adist = (acur - alpha[j]);
            adist *= adist;

            if (adist < shortest)
            {
                shortest     = adist;
                pcIndices[i] = j;
            }
        }

        Err += shortest;
    }

    return Err;
}

static CODECFLOAT RmpSrch1(CODECFLOAT _Blk[MAX_BLOCK],
                           CODECFLOAT _Rpt[MAX_BLOCK],
                           CODECFLOAT _maxerror,
                           CODECFLOAT _min_ex,
                           CODECFLOAT _max_ex,
                           int        _NmbrClrs,
                           CMP_BYTE   nNumPoints)
{
    CODECFLOAT       error  = 0;
    const CODECFLOAT step   = (_max_ex - _min_ex) / (CODECFLOAT)(nNumPoints - 1);
    const CODECFLOAT step_h = step * 0.5f;
    const CODECFLOAT rstep  = 1.0f / step;

    for (int i = 0; i < _NmbrClrs; i++)
    {
        CODECFLOAT v;
        // Work out which value in the block this select
        CODECFLOAT del;

        if ((del = _Blk[i] - _min_ex) <= 0)
            v = _min_ex;
        else if (_Blk[i] - _max_ex >= 0)
            v = _max_ex;
        else
            v = (floor((del + step_h) * rstep) * step) + _min_ex;

        // And accumulate the error
        CODECFLOAT del2 = (_Blk[i] - v);
        error += del2 * del2 * _Rpt[i];

        // if we've already lost to the previous step bail out
        if (_maxerror < error)
        {
            error = _maxerror;
            break;
        }
    }
    return error;
}

static CODECFLOAT Refine1(CODECFLOAT  _Blk[MAX_BLOCK],
                          CODECFLOAT  _Rpt[MAX_BLOCK],
                          CODECFLOAT  _MaxError,
                          CODECFLOAT& _min_ex,
                          CODECFLOAT& _max_ex,
                          CODECFLOAT  _m_step,
                          CODECFLOAT  _min_bnd,
                          CODECFLOAT  _max_bnd,
                          int         _NmbrClrs,
                          CMP_BYTE    dwNumPoints,
                          bool        _bUseSSE2)
{
#ifdef USE_CMP_CORE_API
    CMP_UNUSED(_bUseSSE2);
#endif
    // Start out assuming our endpoints are the min and max values we've determined
    // Attempt a (simple) progressive refinement step to reduce noise in the
    // output image by trying to find a better overall match for the endpoints.

    CODECFLOAT maxerror = _MaxError;
    CODECFLOAT min_ex   = _min_ex;
    CODECFLOAT max_ex   = _max_ex;

    int mode, bestmode;
    do
    {
        CODECFLOAT cr_min0 = min_ex;
        CODECFLOAT cr_max0 = max_ex;
        for (bestmode = -1, mode = 0; mode < SCH_STPS * SCH_STPS; mode++)
        {
            // check each move (see sStep for direction)
            CODECFLOAT cr_min = min_ex + _m_step * sMvF[mode / SCH_STPS];
            CODECFLOAT cr_max = max_ex + _m_step * sMvF[mode % SCH_STPS];

            cr_min = max(cr_min, _min_bnd);
            cr_max = min(cr_max, _max_bnd);

            CODECFLOAT error;
            error = RmpSrch1(_Blk, _Rpt, maxerror, cr_min, cr_max, _NmbrClrs, dwNumPoints);

            if (error < maxerror)
            {
                maxerror = error;
                bestmode = mode;
                cr_min0  = cr_min;
                cr_max0  = cr_max;
            }
        }

        if (bestmode != -1)
        {
            // make move (see sStep for direction)
            min_ex = cr_min0;
            max_ex = cr_max0;
        }
    } while (bestmode != -1);

    _min_ex = min_ex;
    _max_ex = max_ex;

    return maxerror;
}

#ifndef USE_CMP_CORE_API
static int QSortFCmp(const void* Elem1, const void* Elem2)
{
    int ret = 0;

    if (*(CODECFLOAT*)Elem1 - *(CODECFLOAT*)Elem2 < 0.)
        ret = -1;
    else if (*(CODECFLOAT*)Elem1 - *(CODECFLOAT*)Elem2 > 0.)
        ret = 1;
    return ret;
}
#endif

/*--------------------------------------------------------------------------------------------
// input [0,1]
   this is the case when the input data and possible ramp values are all on integer grid.
*/
#define _INT_GRID (_bFixedRamp && _FracPrc == 0)

static CODECFLOAT CompBlock1(CODECFLOAT _RmpPnts[NUM_ENDPOINTS],
                             CODECFLOAT _Blk[MAX_BLOCK],
                             int        _Nmbr,
                             CMP_BYTE   dwNumPoints,
                             bool       bFixedRampPoints,
                             int        _IntPrc,
                             int        _FracPrc,
                             bool       _bFixedRamp,
                             bool       _bUseSSE2,
                             bool       isSigned)
{
#ifdef USE_CMP_CORE_API
    CMP_UNUSED(isSigned);
#endif

    CODECFLOAT fMaxError = 0.f;
    CODECFLOAT Ramp[NUM_ENDPOINTS];
    CODECFLOAT IntFctr = (CODECFLOAT)(1 << _IntPrc);

    ALIGN_16 CODECFLOAT afUniqueValues[MAX_BLOCK];
    ALIGN_16 CODECFLOAT afValueRepeats[MAX_BLOCK];
    for (int i = 0; i < MAX_BLOCK; i++)
        afUniqueValues[i] = afValueRepeats[i] = 0.f;

    // For each unique value we compute the number of it appearances.
    CODECFLOAT fBlk[MAX_BLOCK];
    std::memcpy(fBlk, _Blk, _Nmbr * sizeof(CODECFLOAT));

    // sort the input
    qsort((void*)fBlk, (size_t)_Nmbr, sizeof(CODECFLOAT), QSortFCmp);

    CODECFLOAT new_p = -2.;

    int   N0s = 0, N1s = 0;
    DWORD dwUniqueValues = 0;
    afUniqueValues[0]    = 0.f;

    bool requiresCalculation = true;

    if (bFixedRampPoints)
    {
        for (int i = 0; i < _Nmbr; i++)
        {
            if (new_p != fBlk[i])
            {
                new_p = fBlk[i];
                if (new_p <= 1.5 / 255.)
                    N0s++;
                else if (new_p >= 253.5 / 255.)
                    N1s++;
                else
                {
                    afUniqueValues[dwUniqueValues] = fBlk[i];
                    afValueRepeats[dwUniqueValues] = 1.f;
                    dwUniqueValues++;
                }
            }
            else if (dwUniqueValues > 0 && afUniqueValues[dwUniqueValues - 1] == new_p)
                afValueRepeats[dwUniqueValues - 1] += 1.f;
        }

        // if number of unique colors is less or eq 2 we've done either, but we know that we may have 0s and/or 1s as well.
        // To avoid for the ramp to be considered flat we invented couple entries on the way.
        if (dwUniqueValues <= 2)
        {
            if (dwUniqueValues == 2)
            {  // if 2, take them
                Ramp[0] = floor(afUniqueValues[0] * (IntFctr - 1) + 0.5f);
                Ramp[1] = floor(afUniqueValues[1] * (IntFctr - 1) + 0.5f);
            }
            else if (dwUniqueValues == 1)
            {  // if 1, add another one
                Ramp[0] = floor(afUniqueValues[0] * (IntFctr - 1) + 0.5f);
                Ramp[1] = Ramp[0] + 1.f;
            }
            else
            {  // if 0, invent them
                Ramp[0] = 128.f;
                Ramp[1] = Ramp[0] + 1.f;
            }

            fMaxError           = 0.f;
            requiresCalculation = false;
        }
    }
    else
    {
        for (int i = 0; i < _Nmbr; i++)
        {
            if (new_p != fBlk[i])
            {
                afUniqueValues[dwUniqueValues] = new_p = fBlk[i];
                afValueRepeats[dwUniqueValues]         = 1.f;
                dwUniqueValues++;
            }
            else
                afValueRepeats[dwUniqueValues - 1] += 1.f;
        }

        // if number of unique colors is less or eq 2, we've done
        if (dwUniqueValues <= 2)
        {
            Ramp[0] = floor(afUniqueValues[0] * (IntFctr - 1) + 0.5f);
            if (dwUniqueValues == 1)
                Ramp[1] = Ramp[0] + 1.f;
            else
                Ramp[1] = floor(afUniqueValues[1] * (IntFctr - 1) + 0.5f);
            fMaxError           = 0.f;
            requiresCalculation = false;
        }
    }

    if (requiresCalculation)
    {
        CODECFLOAT min_ex  = afUniqueValues[0];
        CODECFLOAT max_ex  = afUniqueValues[dwUniqueValues - 1];
        CODECFLOAT min_bnd = 0, max_bnd = 1.;
        CODECFLOAT min_r = min_ex, max_r = max_ex;
        CODECFLOAT gbl_l = 0, gbl_r = 0;
        CODECFLOAT cntr = (min_r + max_r) / 2;

        CODECFLOAT gbl_err = MAX_ERROR;
        // Trying to avoid unnecessary calculations. Heuristics: after some analisis it appears
        // that in integer case, if the input interval not more then 48 we won't get much better

        bool wantsSearch = !(_INT_GRID && max_ex - min_ex <= 48.f / IntFctr);

        if (wantsSearch)
        {
            // Search.
            // 1. take the vicinities of both low and high bound of the input interval.
            // 2. setup some search step
            // 3. find the new low and high bound which provides an (sub) optimal (infinite precision) clusterization.
            CODECFLOAT gbl_llb = (min_bnd > min_r - GBL_SCH_EXT) ? min_bnd : min_r - GBL_SCH_EXT;
            CODECFLOAT gbl_rrb = (max_bnd < max_r + GBL_SCH_EXT) ? max_bnd : max_r + GBL_SCH_EXT;
            CODECFLOAT gbl_lrb = (cntr < min_r + GBL_SCH_EXT) ? cntr : min_r + GBL_SCH_EXT;
            CODECFLOAT gbl_rlb = (cntr > max_r - GBL_SCH_EXT) ? cntr : max_r - GBL_SCH_EXT;
            for (CODECFLOAT step_l = gbl_llb; step_l < gbl_lrb; step_l += GBL_SCH_STEP)
            {
                for (CODECFLOAT step_r = gbl_rrb; gbl_rlb <= step_r; step_r -= GBL_SCH_STEP)
                {
                    CODECFLOAT sch_err;
                    sch_err = RmpSrch1(afUniqueValues, afValueRepeats, gbl_err, step_l, step_r, dwUniqueValues, dwNumPoints);
                    if (sch_err < gbl_err)
                    {
                        gbl_err = sch_err;
                        gbl_l   = step_l;
                        gbl_r   = step_r;
                    }
                }
            }

            min_r = gbl_l;
            max_r = gbl_r;
        }

        // This is a refinement call. The function tries to make several small stretches or squashes to
        // minimize quantization error.
        CODECFLOAT m_step = LCL_SCH_STEP / IntFctr;
        fMaxError         = Refine1(afUniqueValues, afValueRepeats, gbl_err, min_r, max_r, m_step, min_bnd, max_bnd, dwUniqueValues, dwNumPoints, _bUseSSE2);

        min_ex = min_r;
        max_ex = max_r;

        max_ex *= (IntFctr - 1);
        min_ex *= (IntFctr - 1);
        /*
        this one is tricky. for the float or high fractional precision ramp it tries to avoid
        for the ramp to be collapsed into one integer number after rounding.
        Notice the condition. There is a difference between max_ex and min_ex but after rounding
        they may collapse into the same integer.

        So we try to run the same refinement procedure but with starting position on the integer grid
        and step equal 1.
        */
        if (!_INT_GRID && max_ex - min_ex > 0. && floor(min_ex + 0.5f) == floor(max_ex + 0.5f))
        {
            m_step  = 1.;
            gbl_err = MAX_ERROR;
            for (DWORD i = 0; i < dwUniqueValues; i++)
                afUniqueValues[i] *= (IntFctr - 1);

            max_ex = min_ex = floor(min_ex + 0.5f);

            gbl_err = Refine1(afUniqueValues, afValueRepeats, gbl_err, min_ex, max_ex, m_step, 0.f, 255.f, dwUniqueValues, dwNumPoints, _bUseSSE2);

            fMaxError = gbl_err;
        }
        Ramp[1] = floor(max_ex + 0.5f);
        Ramp[0] = floor(min_ex + 0.5f);
    }

    // Ensure that the two endpoints are not the same
    // This is legal but serves no need & can break some optimizations in the compressor
    if (Ramp[0] == Ramp[1])
    {
        if (Ramp[1] < 255.f)
            Ramp[1]++;
        else
            Ramp[1]--;
    }
    _RmpPnts[0] = Ramp[0];
    _RmpPnts[1] = Ramp[1];

    return fMaxError;
}

/*--------------------------------------------------------------------------------------------
// input [0,1]
void CompBlock1X(CODECFLOAT* _Blk, [IN] scalar data block (alphas or normals) in float format
                 CMP_DWORD blockCompressed[NUM_ENDPOINTS],  [OUT] compressed data in DXT5 alpha foramt
                 int _NbrClrs,              [IN] actual number of elements in the block
                 int _intPrec,              [IN} integer precision; it applies both to the input data and
                                                 to the ramp points
                 int _fracPrec,             [IN] fractional precision of the ramp points
                 bool _bFixedRamp,          [IN] non-fixed ramp means we have input and generate
                                                 output as float. fixed ramp means that they are fractional numbers.
               )
---------------------------------------------------------------------------------------------*/

CODECFLOAT CompBlock1X(CODECFLOAT* _Blk,
                       CMP_WORD    dwBlockSize,
                       CMP_BYTE    nEndpoints[2],
                       CMP_BYTE*   pcIndices,
                       CMP_BYTE    dwNumPoints,
                       bool        bFixedRampPoints,
                       bool        _bUseSSE2,
                       int         _intPrec,
                       int         _fracPrec,
                       bool        _bFixedRamp)
{
    // just to make them initialized
    if (!_bFixedRamp)
    {
        _intPrec  = 8;
        _fracPrec = 0;
    }

    // this one makes the bulk of the work
    CODECFLOAT Ramp[NUM_ENDPOINTS];
    CompBlock1(Ramp, _Blk, dwBlockSize, dwNumPoints, bFixedRampPoints, _intPrec, _fracPrec, _bFixedRamp, _bUseSSE2, false);

    // final clusterization applied
    CODECFLOAT fError = Clstr1(pcIndices, _Blk, Ramp, dwBlockSize, dwNumPoints, bFixedRampPoints, _intPrec, _fracPrec, _bFixedRamp);
    nEndpoints[0]     = (BYTE)Ramp[0];
    nEndpoints[1]     = (BYTE)Ramp[1];

    return fError;
}


extern "C" void unbound_bc4_block(const uint8_t *src, uint8_t *dst) {
 bool same=true;for(int i=1;i<16;++i)same&=src[i]==src[0];if(same){dst[0]=dst[1]=src[0];for(int i=2;i<8;++i)dst[i]=0;return;}
 float f[16]; for(int i=0;i<16;++i) f[i]=float(src[i])/255.f;
 uint8_t ends[2][2], idx[2][16];
 float e=CompBlock1X(f,16,ends[0],idx[0],8,false,false,8,0,true);
 int best=0;
 if(e!=0.f) { float e2=CompBlock1X(f,16,ends[1],idx[1],6,true,false,8,0,true); if(e2<e) best=1; }
 uint64_t bits=uint64_t(ends[best][0])|(uint64_t(ends[best][1])<<8);
 for(int i=0;i<16;++i) bits|=uint64_t(idx[best][i])<<(16+3*i);
 for(int i=0;i<8;++i)dst[i]=uint8_t(bits>>(8*i));
}
