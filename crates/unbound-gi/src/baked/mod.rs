//! Unbound's own baker of baked global illumination (Q677): what the Unity 6000.0.71f1 Editor's
//! Progressive CPU lightmapper makes of a scene: the layout of its lightmaps ([`atlas`]), each
//! lightmap's texels ([`gbuffer`], rasterized by [`raster`]) and the direct light of its lights
//! on them ([`direct`], [`light`], shadowed through [`trace`]).
pub mod atlas;
pub mod direct;
pub mod gbuffer;
pub mod light;
pub mod raster;
mod spooky;
pub mod trace;

#[cfg(test)]
mod tests;
