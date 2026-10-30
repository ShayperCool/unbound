/*
* Copyright (c) 2006-2011 Erin Catto http://www.box2d.org
*
* This software is provided 'as-is', without any express or implied
* warranty.  In no event will the authors be held liable for any damages
* arising from the use of this software.
* Permission is granted to anyone to use this software for any purpose,
* including commercial applications, and to alter it and redistribute it
* freely, subject to the following restrictions:
* 1. The origin of this software must not be misrepresented; you must not
* claim that you wrote the original software. If you use this software
* in a product, an acknowledgment in the product documentation would be
* appreciated but is not required.
* 2. Altered source versions must be plainly marked as such, and must not be
* misrepresented as being the original software.
* 3. This notice may not be removed or altered from any source distribution.
*/

// Altered C# adaptation of Box2D v2.3.1's coupled prismatic limit solver.
// https://github.com/erincatto/box2d/blob/v2.3.1/Box2D/Box2D/Dynamics/Joints/b2PrismaticJoint.cpp
// Unity's measured slider limits retain this solver and its positional slop.
using System;
using System.Numerics;
using Unbound.Box2D.Common;

namespace Unbound.Box2D.Dynamics.Joints
{
    internal partial class PrismaticJoint
    {
        readonly bool _unityLimits;
        int _unityLimitState;
        float _unityLimitImpulse;
        Matrix3x3 _unityK;

        void InitUnityLimits(float mA, float mB, float iA, float iB)
        {
            float k13 = iA * _s1 * _a1 + iB * _s2 * _a2;
            float k23 = iA * _a1 + iB * _a2;
            float k33 = mA + mB + iA * _a1 * _a1 + iB * _a2 * _a2;
            _unityK.Ex.Set(_k.Ex.X, _k.Ex.Y, k13);
            _unityK.Ey.Set(_k.Ey.X, _k.Ey.Y, k23);
            _unityK.Ez.Set(k13, k23, k33);
            int state = !_enableLimit ? 0
                : Math.Abs(_upperTranslation - _lowerTranslation) < 2f * Settings.LinearSlop ? 3
                : _translation <= _lowerTranslation ? 1 : _translation >= _upperTranslation ? 2 : 0;
            if (state == 0 || (state != 3 && state != _unityLimitState)) _unityLimitImpulse = 0f;
            _unityLimitState = state;
        }

        void SolveUnityVelocityConstraints(in SolverData data)
        {
            var vA = data.Velocities[_indexA].V; var wA = data.Velocities[_indexA].W;
            var vB = data.Velocities[_indexB].V; var wB = data.Velocities[_indexB].W;
            float mA = _invMassA, mB = _invMassB, iA = _invIA, iB = _invIB;
            if (_enableMotor && _unityLimitState != 3)
            {
                float cdot = Vector2.Dot(_axis, vB - vA) + _a2 * wB - _a1 * wA;
                float impulse = _axialMass * (_motorSpeed - cdot);
                float previous = _motorImpulse, limit = data.Step.Dt * _maxMotorForce;
                _motorImpulse = MathUtils.Clamp(_motorImpulse + impulse, -limit, limit);
                impulse = _motorImpulse - previous;
                var p = impulse * _axis;
                vA -= mA * p; wA -= iA * impulse * _a1;
                vB += mB * p; wB += iB * impulse * _a2;
            }
            var cdot1 = new Vector2(Vector2.Dot(_perp, vB - vA) + _s2 * wB - _s1 * wA, wB - wA);
            Vector3 df;
            if (_enableLimit && _unityLimitState != 0)
            {
                float cdot2 = Vector2.Dot(_axis, vB - vA) + _a2 * wB - _a1 * wA;
                var previous = new Vector3(_impulse, _unityLimitImpulse);
                var next = previous + _unityK.Solve33(-new Vector3(cdot1, cdot2));
                if (_unityLimitState == 1) next.Z = Math.Max(next.Z, 0f);
                else if (_unityLimitState == 2) next.Z = Math.Min(next.Z, 0f);
                var b = -cdot1 - (next.Z - previous.Z) * new Vector2(_unityK.Ez.X, _unityK.Ez.Y);
                _impulse = _unityK.Solve22(b) + new Vector2(previous.X, previous.Y);
                _unityLimitImpulse = next.Z;
                df = new Vector3(_impulse, _unityLimitImpulse) - previous;
            }
            else
            {
                var xy = _k.Solve(-cdot1); _impulse += xy;
                df = new Vector3(xy, 0f);
            }
            var P = df.X * _perp + df.Z * _axis;
            float la = df.X * _s1 + df.Y + df.Z * _a1;
            float lb = df.X * _s2 + df.Y + df.Z * _a2;
            vA -= mA * P; wA -= iA * la;
            vB += mB * P; wB += iB * lb;
            data.Velocities[_indexA].V = vA; data.Velocities[_indexA].W = wA;
            data.Velocities[_indexB].V = vB; data.Velocities[_indexB].W = wB;
        }
    }
}
