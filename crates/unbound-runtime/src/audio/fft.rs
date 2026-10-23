//! `GetSpectrumData`: the magnitude spectrum of the latest output.

/// `FFTWindow`.
pub fn window(kind: i32, i: usize, n: usize) -> f32 {
    use std::f32::consts::PI;
    if n < 2 {
        return 1.0;
    }
    let x = i as f32 / (n - 1) as f32;
    match kind {
        // Triangle
        1 => 1.0 - (2.0 * x - 1.0).abs(),
        // Hamming
        2 => 0.54 - 0.46 * (2.0 * PI * x).cos(),
        // Hanning
        3 => 0.5 - 0.5 * (2.0 * PI * x).cos(),
        // Blackman
        4 => 0.42 - 0.5 * (2.0 * PI * x).cos() + 0.08 * (4.0 * PI * x).cos(),
        // BlackmanHarris
        5 => {
            0.35875 - 0.48829 * (2.0 * PI * x).cos() + 0.14128 * (4.0 * PI * x).cos()
                - 0.01168 * (6.0 * PI * x).cos()
        }
        // Rectangular
        _ => 1.0,
    }
}

/// In-place iterative radix-2 FFT; `re.len()` must be a power of two.
pub fn fft(re: &mut [f32], im: &mut [f32]) {
    let n = re.len();
    debug_assert!(n.is_power_of_two() && im.len() == n);
    let mut j = 0;
    for i in 1..n {
        let mut bit = n >> 1;
        while j & bit != 0 {
            j ^= bit;
            bit >>= 1;
        }
        j |= bit;
        if i < j {
            re.swap(i, j);
            im.swap(i, j);
        }
    }
    let mut len = 2;
    while len <= n {
        let angle = -2.0 * std::f64::consts::PI / len as f64;
        let (w_im, w_re) = angle.sin_cos();
        for start in (0..n).step_by(len) {
            let (mut c_re, mut c_im) = (1.0f64, 0.0f64);
            for k in 0..len / 2 {
                let a = start + k;
                let b = a + len / 2;
                let t_re = re[b] as f64 * c_re - im[b] as f64 * c_im;
                let t_im = re[b] as f64 * c_im + im[b] as f64 * c_re;
                re[b] = (re[a] as f64 - t_re) as f32;
                im[b] = (im[a] as f64 - t_im) as f32;
                re[a] = (re[a] as f64 + t_re) as f32;
                im[a] = (im[a] as f64 + t_im) as f32;
                let next = c_re * w_re - c_im * w_im;
                c_im = c_re * w_im + c_im * w_re;
                c_re = next;
            }
        }
        len <<= 1;
    }
}

/// The spectrum of `signal` (its latest `2 * out.len()` samples) in `out.len()` bins from 0 Hz to
/// half the sample rate: magnitudes normalized so that a full-scale sine on a bin with the
/// rectangular window reads 1.
pub fn spectrum(signal: &[f32], window_kind: i32, out: &mut [f32]) {
    let bins = out.len();
    let n = bins * 2;
    let mut re = vec![0.0f32; n];
    let mut im = vec![0.0f32; n];
    let start = signal.len().saturating_sub(n);
    let taken = &signal[start..];
    let offset = n - taken.len();
    let mut window_sum = 0.0f32;
    for i in 0..n {
        let w = window(window_kind, i, n);
        window_sum += w;
        if i >= offset {
            re[i] = taken[i - offset] * w;
        }
    }
    fft(&mut re, &mut im);
    let scale = if window_sum > 0.0 {
        2.0 / window_sum
    } else {
        0.0
    };
    for (k, value) in out.iter_mut().enumerate() {
        *value = (re[k] * re[k] + im[k] * im[k]).sqrt() * scale;
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_sine_peaks_on_its_bin() {
        let n = 1024;
        let signal: Vec<f32> = (0..n)
            .map(|i| (2.0 * std::f32::consts::PI * 64.0 * i as f32 / n as f32).sin())
            .collect();
        let mut out = vec![0.0; n / 2];
        spectrum(&signal, 0, &mut out);
        let peak = out
            .iter()
            .enumerate()
            .max_by(|a, b| a.1.partial_cmp(b.1).unwrap())
            .unwrap();
        assert_eq!(peak.0, 64);
        assert!((peak.1 - 1.0).abs() < 1e-3, "{}", peak.1);
        assert!(out[10] < 1e-3);
    }
}
