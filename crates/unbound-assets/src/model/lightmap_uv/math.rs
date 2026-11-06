//! Elementary functions as polynomial approximations: sine and cosine by octant after reduction
//! to [-π, π], arc cosine through arc sine, the logarithm by a series on the mantissa and the
//! exponential by a rational form. With these, the unwrap's UV2 matches the Unity 6000.0.71f1
//! Editor's bit for bit (tests/projects/lightmapuvs).

use std::f64::consts::{FRAC_PI_2, FRAC_PI_4, PI, TAU};

const THREE_FRAC_PI_4: f64 = 2.356194490192345;
const FRAC_1_SQRT_2: f64 = 0.7071067811865476;

/// cos(x) ≈ cos_poly(x²) for |x| ≤ π/4.
fn cos_poly(z: f64) -> f64 {
    ((((((-1.1470745597729725e-11 * z + 2.08767569878681e-5) * z + -2.755731922398589e-7) * z
        + 2.48015873015873e-5)
        * z
        + -0.001388888888888889)
        * z
        + 0.041666666666666664)
        * z
        + -0.5)
        * z
        + 1.0
}

/// sin(x) ≈ x + sin_tail(x²)·x·x² for |x| ≤ π/4.
fn sin_tail(z: f64) -> f64 {
    ((((1.6059043836821613e-10 * z + -2.505210838544172e-8) * z + 2.7557319223985893e-6) * z
        + -0.0001984126984126984)
        * z
        + 0.008333333333333333)
        * z
        + -0.16666666666666666
}

/// `x` reduced to [-π, π].
fn reduce(x: f64) -> f64 {
    let (shift, back) = if x > 0.0 { (PI, -PI) } else { (-PI, PI) };
    (shift + x) % TAU + back
}

pub fn cos(x: f64) -> f64 {
    let y = reduce(x);
    let a = y.abs();
    if a <= FRAC_PI_4 {
        return cos_poly(y * y);
    }
    if a <= FRAC_PI_2 {
        let t = a + -FRAC_PI_4;
        let z = t * t;
        return (cos_poly(z) - (sin_tail(z) * (t * z) + t)) * FRAC_1_SQRT_2;
    }
    if THREE_FRAC_PI_4 < a {
        let t = a + -THREE_FRAC_PI_4;
        let z = t * t;
        return (cos_poly(z) + (sin_tail(z) * (t * z) + t)) * -FRAC_1_SQRT_2;
    }
    let t = a + -FRAC_PI_2;
    let z = t * t;
    -(sin_tail(z) * (t * z) + t)
}

pub fn sin(x: f64) -> f64 {
    let y = reduce(x);
    let a = y.abs();
    let v = if a <= FRAC_PI_4 {
        let z = y * y;
        sin_tail(z) * (z * a) + a
    } else if a <= FRAC_PI_2 {
        let t = -FRAC_PI_4 + a;
        let z = t * t;
        (cos_poly(z) + (sin_tail(z) * (t * z) + t)) * FRAC_1_SQRT_2
    } else if a <= THREE_FRAC_PI_4 {
        let t = -FRAC_PI_2 + a;
        cos_poly(t * t)
    } else {
        let t = -THREE_FRAC_PI_4 + a;
        let z = t * t;
        (cos_poly(z) - (sin_tail(z) * (t * z) + t)) * FRAC_1_SQRT_2
    };
    if y < 0.0 { -v } else { v }
}

/// asin(a) ≈ a + asin_tail(a²)·a²·a for a ≤ 1/2.
fn asin_tail(z: f64) -> f64 {
    ((((0.017352764423076924 * z + 0.022372159090909092) * z + 0.030381944444444444) * z
        + 0.044642857142857144)
        * z
        + 0.075)
        * z
        + 0.16666666666666666
}

/// acos(c), clamped to [0, π] (π when `c` is not a number).
pub fn acos(c: f64) -> f64 {
    if !(-1.0 < c) {
        return PI;
    }
    if !(c < 1.0) {
        return 0.0;
    }
    let sign = if c < 0.0 { -1.0 } else { 1.0 };
    let a = c.abs();
    let asin = if a <= 0.5 {
        let z = c * c;
        asin_tail(z) * (z * a) + a
    } else {
        let h = (1.0 - a) * 0.5;
        let s = if h > 0.0 { h.sqrt() } else { 0.0 };
        let z = s * s;
        let t = asin_tail(z) * (s * z) + s;
        FRAC_PI_2 - (t + t)
    };
    FRAC_PI_2 - sign * asin
}

/// The mantissa in [0.5, 1) and exponent of a positive normal `x`.
fn frexp(x: f64) -> (f64, i32) {
    let bits = x.to_bits();
    let exponent = ((bits >> 52) & 0x7ff) as i32;
    (
        f64::from_bits((bits & !(0x7ff << 52)) | (1022 << 52)),
        exponent - 1022,
    )
}

/// ln(max(x, 1e-10)).
pub fn ln(x: f64) -> f64 {
    let x = if 1e-10 <= x { x } else { 1e-10 };
    let (m, e) = frexp(x);
    let m = m + m;
    let s = if m > 0.0 { m.sqrt() } else { 0.0 };
    let t = (s + -1.0) / (s + 1.0);
    let z = t * t;
    (e - 1) as f64 / 1.4426950408889634
        + (((((((z * 0.0666666666666667 + 0.0769230769230769) * z + 0.0909090909090909) * z
            + 0.111111111111111)
            * z
            + 0.142857142857143)
            * z
            + 0.2)
            * z
            + 0.333333333333333)
            * z
            + 1.0)
            * t
            * 4.0
}

/// e^x.
pub fn exp(x: f64) -> f64 {
    const LN_2: f64 = 0.6931471805599453;
    let mut r = x % LN_2;
    let mut k = (x / LN_2) as i32;
    if r <= 0.34657359027997264 {
        if r < -0.34657359027997264 {
            k -= 1;
            r += LN_2;
        }
    } else {
        k += 1;
        r += -LN_2;
    }
    let z = r * r;
    let e = (r + r)
        / (((((4.1381367970572385e-8 * z + -1.6533902205465252e-6) * z + 6.613756321437934e-5)
            * z
            + -0.0027777777777015593)
            * z
            + 0.16666666666666602)
            * z
            + 2.0
            - r)
        + 1.0;
    if k == 0 {
        return e;
    }
    // 2^k, added to the exponent field of the high word.
    let bits = e.to_bits();
    let high = (bits >> 32) as u32 as i32;
    let low = bits & u64::from(u32::MAX);
    let shifted = k.wrapping_mul(1 << 20).wrapping_add(high);
    if k < -1021 {
        let high = shifted.wrapping_add(1000 << 20) as u32 as u64;
        f64::from_bits(low | (high << 32)) * 9.332636185032189e-302
    } else {
        f64::from_bits(low | ((shifted as u32 as u64) << 32))
    }
}
