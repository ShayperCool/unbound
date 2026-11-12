//! Unbound's own precompute of realtime GI, for scenes without the Editor's precomputed data:
//! the clusters of a system's surfaces ([`clusters`]), traced through [`geometry`].
//! See docs/REALTIME_GI.md.
pub mod clusters;
pub mod geometry;
mod math;

#[cfg(test)]
mod tests;
