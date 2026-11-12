//! Stack-backed frames for effects that share state across the active speaker layout.

/// Visit every active speaker once per frame. Mono mirrors its first output into storage;
/// the unused final slot of a wider odd layout stays untouched.
/// Extra buffers contain successive interleaved pairs, just like the front buffer.
pub(crate) fn process_frames(
    front: &mut [f32],
    extra: &mut [&mut [f32]],
    channels: usize,
    mut process: impl FnMut(&mut [f32], usize),
) {
    debug_assert!((1..=8).contains(&channels));
    for frame in 0..front.len() / 2 {
        let mut input = [0.0; 8];
        input[..2].copy_from_slice(&front[frame * 2..frame * 2 + 2]);
        for channel in 2..channels {
            input[channel] = extra[(channel - 2) / 2][frame * 2 + channel % 2];
        }
        process(&mut input[..channels], frame);
        front[frame * 2] = input[0];
        front[frame * 2 + 1] = input[usize::from(channels > 1)];
        for channel in 2..channels {
            extra[(channel - 2) / 2][frame * 2 + channel % 2] = input[channel];
        }
    }
}

/// Store a single-channel effect's first output in both internal graph lanes.
pub(crate) fn retain_mono(front: &mut [f32]) {
    for frame in front.chunks_exact_mut(2) {
        frame[1] = frame[0];
    }
}
