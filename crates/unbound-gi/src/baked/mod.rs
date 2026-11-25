//! Unbound's own baker of baked global illumination (Q677): what the Unity 6000.0.71f1 Editor's
//! Progressive CPU lightmapper makes of a scene: the layout of its lightmaps ([`atlas`]), each
//! lightmap's texels ([`gbuffer`], rasterized by [`raster`]), the direct light of its lights
//! on them ([`direct`], [`light`], shadowed through [`trace`]) and the light that reaches them
//! from other surfaces ([`indirect`], with the rays of [`sampling`]), and the lightmap made of them
//! ([`composite`], its seams stitched by [`seams`]).
pub mod atlas;
pub mod composite;
pub mod dilate;
pub mod direct;
pub mod directional;
pub mod filter;
pub mod gbuffer;
pub mod grid;
pub mod images;
pub mod indirect;
pub mod levels;
pub mod light;
pub mod light_probes;
pub mod probes;
pub mod raster;
pub mod sampling;
pub mod seams;
pub mod shadowmask;
pub mod sky;
mod spooky;
pub mod trace;

#[cfg(test)]
mod tests;
