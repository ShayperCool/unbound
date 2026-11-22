//! Unbound's own baker of baked global illumination (Q677): what the Unity 6000.0.71f1 Editor's
//! Progressive lightmappers make of a scene, starting with the layout of its lightmaps
//! ([`atlas`]).
pub mod atlas;
pub mod direct;
pub mod gbuffer;
pub mod light;
pub mod raster;
pub mod trace;
mod spooky;

#[cfg(test)]
mod tests;
