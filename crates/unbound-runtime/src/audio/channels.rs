//! Stack-backed frames for effects that share state across the active speaker layout.

/// Visit every active speaker once per frame; the unused slot of an odd layout stays untouched.
/// Extra buffers contain successive interleaved pairs, just like the front buffer.
pub(crate) fn process_frames(
    front: &mut [f32],
    extra: &mut [&mut [f32]],
    channels: usize,
    mut process: impl FnMut(&mut [f32], usize),
) {
    debug_assert!((2..=8).contains(&channels));
    for frame in 0..front.len() / 2 {
        let mut input = [0.0; 8];
        input[..2].copy_from_slice(&front[frame * 2..frame * 2 + 2]);
        for channel in 2..channels {
            input[channel] = extra[(channel - 2) / 2][frame * 2 + channel % 2];
        }
        process(&mut input[..channels], frame);
        front[frame * 2..frame * 2 + 2].copy_from_slice(&input[..2]);
        for channel in 2..channels {
            extra[(channel - 2) / 2][frame * 2 + channel % 2] = input[channel];
        }
    }
}
