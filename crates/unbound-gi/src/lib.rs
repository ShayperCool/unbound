//! Precomputed realtime global illumination: reading the data Unity precomputes into a scene's
//! LightingData asset ([`data`]) and updating its realtime lightmaps at runtime ([`solver`]).
//! See docs/REALTIME_GI.md.
pub mod data;
pub mod solver;

#[cfg(test)]
mod tests;
