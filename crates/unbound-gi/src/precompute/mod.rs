//! Unbound's own precompute of realtime GI, for scenes without the Editor's precomputed data:
//! the clusters of a system's surfaces ([`clusters`]) and the transport into its realtime
//! lightmap texels ([`transport`]) and the visibility of directional lights ([`visibility`]),
//! traced through [`geometry`].
//! See docs/REALTIME_GI.md.
pub mod clusters;
pub mod geometry;
mod math;
pub mod probes;
pub mod transport;
pub mod visibility;

#[cfg(test)]
mod tests;
