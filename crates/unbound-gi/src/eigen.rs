//! Eigenvalues and eigenvectors of a symmetric 3x3 matrix: Householder tridiagonalisation and
//! the QL method with implicit shifts (the classic public-domain routines). The eigenvalues
//! come out ascending; column `k` of the result is the eigenvector of eigenvalue `k`.

const N: usize = 3;

fn hypot2(x: f64, y: f64) -> f64 {
    (x * x + y * y).sqrt()
}

fn tred2(v: &mut [[f64; N]; N], d: &mut [f64; N], e: &mut [f64; N]) {
    for j in 0..N {
        d[j] = v[N - 1][j];
    }
    for i in (1..N).rev() {
        let mut scale = 0.0;
        let mut h = 0.0;
        for k in 0..i {
            scale += d[k].abs();
        }
        if scale == 0.0 {
            e[i] = d[i - 1];
            for j in 0..i {
                d[j] = v[i - 1][j];
                v[i][j] = 0.0;
                v[j][i] = 0.0;
            }
        } else {
            for k in 0..i {
                d[k] /= scale;
                h += d[k] * d[k];
            }
            let mut f = d[i - 1];
            let mut g = h.sqrt();
            if f > 0.0 {
                g = -g;
            }
            e[i] = scale * g;
            h -= f * g;
            d[i - 1] = f - g;
            for j in 0..i {
                e[j] = 0.0;
            }
            for j in 0..i {
                f = d[j];
                v[j][i] = f;
                g = e[j] + v[j][j] * f;
                for k in j + 1..i {
                    g += v[k][j] * d[k];
                    e[k] += v[k][j] * f;
                }
                e[j] = g;
            }
            f = 0.0;
            for j in 0..i {
                e[j] /= h;
                f += e[j] * d[j];
            }
            let hh = f / (h + h);
            for j in 0..i {
                e[j] -= hh * d[j];
            }
            for j in 0..i {
                f = d[j];
                g = e[j];
                for k in j..i {
                    v[k][j] -= f * e[k] + g * d[k];
                }
                d[j] = v[i - 1][j];
                v[i][j] = 0.0;
            }
        }
        d[i] = h;
    }
    for i in 0..N - 1 {
        v[N - 1][i] = v[i][i];
        v[i][i] = 1.0;
        let h = d[i + 1];
        if h != 0.0 {
            for k in 0..=i {
                d[k] = v[k][i + 1] / h;
            }
            for j in 0..=i {
                let mut g = 0.0;
                for k in 0..=i {
                    g += v[k][i + 1] * v[k][j];
                }
                for k in 0..=i {
                    v[k][j] -= g * d[k];
                }
            }
        }
        for k in 0..=i {
            v[k][i + 1] = 0.0;
        }
    }
    for j in 0..N {
        d[j] = v[N - 1][j];
        v[N - 1][j] = 0.0;
    }
    v[N - 1][N - 1] = 1.0;
    e[0] = 0.0;
}

fn tql2(v: &mut [[f64; N]; N], d: &mut [f64; N], e: &mut [f64; N]) {
    for i in 1..N {
        e[i - 1] = e[i];
    }
    e[N - 1] = 0.0;
    let mut f = 0.0;
    let mut tst1 = 0.0f64;
    let eps = 2.0f64.powi(-52);
    for l in 0..N {
        tst1 = tst1.max(d[l].abs() + e[l].abs());
        let mut m = l;
        while m < N {
            if e[m].abs() <= eps * tst1 {
                break;
            }
            m += 1;
        }
        if m > l {
            loop {
                let mut g = d[l];
                let mut p = (d[l + 1] - g) / (2.0 * e[l]);
                let mut r = hypot2(p, 1.0);
                if p < 0.0 {
                    r = -r;
                }
                d[l] = e[l] / (p + r);
                d[l + 1] = e[l] * (p + r);
                let dl1 = d[l + 1];
                let mut h = g - d[l];
                for i in l + 2..N {
                    d[i] -= h;
                }
                f += h;
                p = d[m];
                let mut c = 1.0;
                let mut c2 = c;
                let mut c3 = c;
                let el1 = e[l + 1];
                let mut s = 0.0;
                let mut s2 = 0.0;
                for i in (l..m).rev() {
                    c3 = c2;
                    c2 = c;
                    s2 = s;
                    g = c * e[i];
                    h = c * p;
                    r = hypot2(p, e[i]);
                    e[i + 1] = s * r;
                    s = e[i] / r;
                    c = p / r;
                    p = c * d[i] - s * g;
                    d[i + 1] = h + s * (c * g + s * d[i]);
                    for k in 0..N {
                        h = v[k][i + 1];
                        v[k][i + 1] = s * v[k][i] + c * h;
                        v[k][i] = c * v[k][i] - s * h;
                    }
                }
                p = -s * s2 * c3 * el1 * e[l] / dl1;
                e[l] = s * p;
                d[l] = c * p;
                if e[l].abs() <= eps * tst1 {
                    break;
                }
            }
        }
        d[l] += f;
        e[l] = 0.0;
    }
    for i in 0..N - 1 {
        let mut k = i;
        let mut p = d[i];
        for j in i + 1..N {
            if d[j] < p {
                k = j;
                p = d[j];
            }
        }
        if k != i {
            d[k] = d[i];
            d[i] = p;
            for j in 0..N {
                let t = v[j][i];
                v[j][i] = v[j][k];
                v[j][k] = t;
            }
        }
    }
}

/// `(vectors, values)` of a symmetric matrix.
pub fn eigen_decomposition(a: &[[f64; N]; N]) -> ([[f64; N]; N], [f64; N]) {
    let mut v = *a;
    let mut d = [0.0; N];
    let mut e = [0.0; N];
    tred2(&mut v, &mut d, &mut e);
    tql2(&mut v, &mut d, &mut e);
    (v, d)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_plane_of_points_has_its_normal_first() {
        // Points in the plane z = 0, spread along x more than y.
        let a = [[10.0, 0.0, 0.0], [0.0, 4.0, 0.0], [0.0, 0.0, 0.0]];
        let (v, d) = eigen_decomposition(&a);
        assert_eq!(d, [0.0, 4.0, 10.0]);
        assert_eq!(
            [v[0][0].abs(), v[1][0].abs(), v[2][0].abs()],
            [0.0, 0.0, 1.0]
        );
    }

    #[test]
    fn a_tilted_matrix_decomposes() {
        let a = [[2.0, 1.0, 0.5], [1.0, 3.0, 0.25], [0.5, 0.25, 1.0]];
        let (v, d) = eigen_decomposition(&a);
        for k in 0..3 {
            for i in 0..3 {
                let av: f64 = (0..3).map(|j| a[i][j] * v[j][k]).sum();
                assert!((av - d[k] * v[i][k]).abs() < 1e-12);
            }
        }
        assert!(d[0] <= d[1] && d[1] <= d[2]);
    }
}
