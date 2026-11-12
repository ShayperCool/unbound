//! Precomputed realtime global illumination: reading the data Unity precomputes into a scene's
//! LightingData asset ([`data`]) and updating its realtime lightmaps ([`solver`], [`directionality`]) and light probes
//! ([`probes`]) at runtime.
//! See docs/REALTIME_GI.md.
pub mod auto_charts;
pub mod data;
pub mod directionality;
mod eigen;
pub mod environment;
pub mod instance;
pub mod probes;
pub mod realtime_uv;
pub mod solver;
pub mod visibility;

#[cfg(test)]
mod tests;
