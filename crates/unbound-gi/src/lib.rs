//! Precomputed realtime global illumination: reading the data Unity precomputes into a scene's
//! LightingData asset ([`data`]) and updating its realtime lightmaps ([`solver`]) and light probes
//! ([`probes`]) at runtime.
//! See docs/REALTIME_GI.md.
pub mod data;
pub mod probes;
pub mod realtime_uv;
pub mod solver;
pub mod visibility;

#[cfg(test)]
mod tests;
