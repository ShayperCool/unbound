//! The lightmap a bake makes of its texel buffers (matches the Unity 6000.0.71f1 Editor's
//! Progressive CPU lightmapper without filtering, tests/projects/bakedbuffers `lightmap`): each
//! used texel's direct, indirect and sky's light summed; the texels too many of whose rays met a
//! back face (Lightmap Parameters' Backface Tolerance) given their neighbours' light instead;
//! and the texels around the charts given their neighbours' light, as many texels out as the
//! Lighting Settings' padding, so that filtering at a chart's edge reads the chart's light. The
//! seams are stitched afterwards ([`super::seams`]). Where the Lighting Settings filter (the Auto
//! filtering, or the Advanced one's Open Image Denoise denoiser and Gaussian or A-Trous filters),
//! the direct light and the indirect and sky's light are denoised ([`super::denoise`]) and
//! filtered ([`super::filter`]) each on its own before they are summed. Not made: the texels
//! further out.

use super::{denoise, dilate, filter};
use anyhow::Result;
use std::path::Path;

/// The buffers of one lightmap, `width` × `height` texels, rows from the first.
pub struct Buffers<'a> {
    pub width: usize,
    pub height: usize,
    /// Whether each texel is used (a chart covers it).
    pub used: &'a [bool],
    /// Per texel its direct light (red, green, blue, and a fourth number).
    pub direct: &'a [[f32; 4]],
    /// Per texel its indirect light, where it was baked.
    pub indirect: Option<&'a [[f32; 4]]>,
    /// Per texel the sky's light straight on it, where it was baked.
    pub environment: Option<&'a [[f32; 4]]>,
    /// Per texel the part of its rays that met a back face first, where it was counted.
    pub validity: Option<&'a [f32]>,
    /// Per texel its chart (-1: none); read where the light is filtered.
    pub charts: &'a [i32],
    /// Per sample its world normal (`supersampling` × `supersampling` a texel, rows of `width ×
    /// supersampling`), and that number; read where the A-Trous filter filters the light.
    pub normals: Option<(&'a [[f32; 4]], usize)>,
}

/// A filter of the Lighting Settings' Advanced filtering.
#[derive(Clone, Copy, Debug, PartialEq)]
pub enum PartFilter {
    /// The Gaussian filter of this radius, in texels ([`filter::gaussian`]).
    Gaussian(f32),
    /// The A-Trous filter of this position sigma, in texels ([`filter::atrous`]).
    ATrous(f32),
}

/// What becomes of one part of the light: denoised by Open Image Denoise or not, then filtered by
/// a filter or not.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct PartFiltering {
    pub denoise: bool,
    pub filter: Option<PartFilter>,
}

/// What becomes of the direct light and of the indirect and sky's light.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Filtering {
    pub direct: PartFiltering,
    pub indirect: PartFiltering,
}

impl Filtering {
    /// Whether a part is denoised (and so Open Image Denoise's library needed).
    pub fn denoises(&self) -> bool {
        self.direct.denoise || self.indirect.denoise
    }
}

/// What processing a part reads besides the part: its lightmap's size, charts and samples'
/// normals, and Open Image Denoise's library where a part is denoised.
#[derive(Clone, Copy)]
pub struct Post<'a> {
    pub width: usize,
    pub height: usize,
    pub charts: &'a [i32],
    pub normals: Option<(&'a [[f32; 4]], usize)>,
    pub denoiser: Option<&'a Path>,
}

/// One part of a lightmap's light (or of one of its images), its fourth value each texel's
/// weight, processed as `with` says: denoised (after one pass of the fill gave the texels
/// without weight their neighbours' light and weight), then filtered.
pub fn process(part: &mut [[f32; 4]], with: PartFiltering, post: &Post) -> Result<()> {
    let (width, height) = (post.width, post.height);
    if with.denoise {
        let Some(library) = post.denoiser else {
            anyhow::bail!("denoising needs Open Image Denoise's library");
        };
        let mut filled = vec![[0.0f32; 4]; part.len()];
        dilate::pass(part, &mut filled, None, width, height);
        denoise::denoise(library, &filled, part, width, height)?;
    }
    match with.filter {
        Some(PartFilter::Gaussian(radius)) => {
            filter::gaussian(part, post.charts, width, height, radius)
        }
        Some(PartFilter::ATrous(spread)) => {
            let (normals, supersampling) = post
                .normals
                .expect("the A-Trous filter reads the samples' normals");
            filter::atrous(
                part,
                post.charts,
                normals,
                width,
                height,
                supersampling,
                spread,
            )
        }
        None => {}
    }
    Ok(())
}

/// A lightmap made of its buffers, and where it is filtered its direct light and its indirect
/// and sky's light as processed (the directional image is made of them).
#[derive(Debug)]
pub struct Composed {
    pub lightmap: Vec<[f32; 4]>,
    pub parts: Option<[Vec<[f32; 4]>; 2]>,
}

/// The lightmap's texels: red, green, blue, and 1 for a texel that holds light (a used one whose
/// light stands or that its neighbours' reaches, or one within `padding` texels of those; where a
/// part is denoised, also one beside those), 0 for the others (which hold none).
/// `backface_tolerance` is the part of a texel's rays that must meet front faces for its own
/// light to stand. `denoiser` is the path of Open Image Denoise 1.4.2's library, needed where
/// `filtering` denoises (an error otherwise, as is the library's failure).
pub fn composite(
    buffers: &Buffers,
    backface_tolerance: f32,
    padding: u32,
    filtering: Option<Filtering>,
    denoiser: Option<&Path>,
) -> Result<Composed> {
    let (width, height) = (buffers.width, buffers.height);
    let texels = width * height;
    // The direct light, and the indirect and sky's light, of each used texel.
    let mut direct = vec![[0.0f32; 4]; texels];
    let mut indirect = vec![[0.0f32; 4]; texels];
    for k in 0..texels {
        if !buffers.used[k] {
            continue;
        }
        for c in 0..3 {
            direct[k][c] = buffers.direct[k][c];
            indirect[k][c] = buffers.indirect.map_or(0.0, |b| b[k][c])
                + buffers.environment.map_or(0.0, |b| b[k][c]);
        }
        direct[k][3] = 1.0;
        indirect[k][3] = 1.0;
    }
    // (Summed in the order of the measured unfiltered lightmaps.)
    let mut out = vec![[0.0f32; 4]; texels];
    for (k, texel) in out.iter_mut().enumerate() {
        if !buffers.used[k] {
            continue;
        }
        for c in 0..3 {
            texel[c] = buffers.direct[k][c]
                + buffers.indirect.map_or(0.0, |b| b[k][c])
                + buffers.environment.map_or(0.0, |b| b[k][c]);
        }
        texel[3] = 1.0;
    }
    // The texels whose light does not stand (more of their rays met back faces than the
    // tolerance allows, or no number).
    let marks = dilate::markers(buffers.used, buffers.validity, backface_tolerance, &out);
    // Filtered: each part on its own, weighed by those marks ([`process`]); then summed, each
    // texel's weight the product of its parts' weights.
    let post = Post {
        width,
        height,
        charts: buffers.charts,
        normals: buffers.normals,
        denoiser,
    };
    let mut parts = None;
    if let Some(filtering) = filtering {
        for (part, with) in [
            (&mut indirect, filtering.indirect),
            (&mut direct, filtering.direct),
        ] {
            for (texel, mark) in part.iter_mut().zip(&marks) {
                texel[3] = *mark;
            }
            process(part, with, &post)?;
        }
        for (k, texel) in out.iter_mut().enumerate() {
            for c in 0..3 {
                texel[c] = direct[k][c] + indirect[k][c];
            }
            texel[3] = direct[k][3] * indirect[k][3];
        }
        parts = Some([direct, indirect]);
    } else {
        for (texel, mark) in out.iter_mut().zip(marks) {
            texel[3] = mark;
        }
    }
    // The texels without light given their neighbours' light, then `padding` rings around the
    // charts ([`dilate`]; a group of them no texel holding its own light reaches holds none: not
    // measured). The weight of a texel that holds light is 1 (as in the Editor's lightmaps; a
    // filtered weight is 1 within the reciprocal's last bit).
    let mut lightmap = dilate::fill(out, buffers.used, width, height, padding);
    for texel in &mut lightmap {
        if texel[3] > 0.0 {
            texel[3] = 1.0;
        }
    }
    Ok(Composed { lightmap, parts })
}

#[cfg(test)]
mod tests {
    use super::*;

    fn lit(values: &[f32]) -> Vec<[f32; 4]> {
        values.iter().map(|&v| [v, v, v, 1.0]).collect()
    }

    #[test]
    fn a_used_texel_holds_its_direct_indirect_and_skys_light() {
        let used = [true, false];
        let direct = lit(&[1.0, 5.0]);
        let indirect = lit(&[0.25, 5.0]);
        let environment = lit(&[0.5, 5.0]);
        let out = composite(
            &Buffers {
                width: 2,
                height: 1,
                used: &used,
                direct: &direct,
                indirect: Some(&indirect),
                environment: Some(&environment),
                validity: Some(&[0.0, 0.0]),
                charts: &[],
                normals: None,
            },
            0.9,
            0,
            None,
            None,
        )
        .unwrap()
        .lightmap;
        assert_eq!(out, vec![[1.75, 1.75, 1.75, 1.0], [0.0; 4]]);
    }

    #[test]
    fn a_texel_whose_rays_met_back_faces_takes_its_neighbours_light_ring_by_ring() {
        // A row of five used texels: the last two met back faces (half of their rays), the
        // third a tenth of them (as the tolerance allows).
        let used = [true; 5];
        let direct = lit(&[2.0, 4.0, 6.0, 100.0, 200.0]);
        let validity = [0.0, 0.0, 0.1, 0.5, 0.5];
        let out = composite(
            &Buffers {
                width: 5,
                height: 1,
                used: &used,
                direct: &direct,
                indirect: None,
                environment: None,
                validity: Some(&validity),
                charts: &[],
                normals: None,
            },
            0.9,
            0,
            None,
            None,
        )
        .unwrap()
        .lightmap;
        // The fourth takes the third's light; the fifth, beside no texel whose light stands at
        // first, the fourth's as the first ring gave it (each a mean as the fill makes it,
        // within a bit).
        let red: Vec<f32> = out.iter().map(|t| t[0]).collect();
        assert_eq!(&red[..3], &[2.0, 4.0, 6.0]);
        assert!(red[3..].iter().all(|&r| (r - 6.0).abs() < 1e-5), "{red:?}");
    }

    #[test]
    fn back_face_texels_no_texel_reaches_hold_no_light_and_none_for_the_padding() {
        // A chart of two texels whose rays all met back faces, with an unused texel beside it.
        let used = [true, true, false];
        let direct = lit(&[1.0, 2.0, 0.0]);
        let out = composite(
            &Buffers {
                width: 3,
                height: 1,
                used: &used,
                direct: &direct,
                indirect: None,
                environment: None,
                validity: Some(&[1.0, 1.0, 0.0]),
                charts: &[],
                normals: None,
            },
            0.9,
            2,
            None,
            None,
        )
        .unwrap()
        .lightmap;
        assert_eq!(out, vec![[0.0; 4]; 3]);
    }

    #[test]
    fn the_texels_around_a_chart_take_its_light_as_far_as_the_padding() {
        // A 5 x 1 row: a used texel in the middle, padding 1.
        let used = [false, false, true, false, false];
        let direct = lit(&[0.0, 0.0, 3.0, 0.0, 0.0]);
        let buffers = Buffers {
            width: 5,
            height: 1,
            used: &used,
            direct: &direct,
            indirect: None,
            environment: None,
            validity: None,
            charts: &[],
            normals: None,
        };
        let out = composite(&buffers, 0.9, 1, None, None).unwrap().lightmap;
        let alpha: Vec<f32> = out.iter().map(|t| t[3]).collect();
        assert_eq!(alpha, vec![0.0, 1.0, 1.0, 1.0, 0.0]);
        let near = |a: f32, b: f32| (a - b).abs() < 1e-5;
        assert!(near(out[1][0], 3.0));
        // Two rings: the mean of the neighbours that hold light, ring by ring.
        let out = composite(&buffers, 0.9, 2, None, None).unwrap().lightmap;
        assert!(out.iter().all(|t| near(t[0], 3.0)), "{out:?}");
        // A texel between two charts takes the mean of both.
        let used = [true, false, true];
        let direct = lit(&[1.0, 0.0, 3.0]);
        let out = composite(
            &Buffers {
                width: 3,
                height: 1,
                used: &used,
                direct: &direct,
                indirect: None,
                environment: None,
                validity: None,
                charts: &[],
                normals: None,
            },
            0.9,
            1,
            None,
            None,
        )
        .unwrap()
        .lightmap;
        assert!(near(out[1][0], 2.0) && out[1][3] == 1.0, "{:?}", out[1]);
    }

    /// A part to be denoised needs Open Image Denoise's library: without it the lightmap is not
    /// made (said), rather than made undenoised.
    #[test]
    fn denoising_without_the_library_is_an_error() {
        let used = [true; 2];
        let direct = lit(&[1.0, 2.0]);
        let buffers = Buffers {
            width: 2,
            height: 1,
            used: &used,
            direct: &direct,
            indirect: None,
            environment: None,
            validity: None,
            charts: &[0, 0],
            normals: None,
        };
        let filtering = Filtering {
            direct: PartFiltering::default(),
            indirect: PartFiltering {
                denoise: true,
                filter: None,
            },
        };
        assert!(filtering.denoises());
        let error = composite(&buffers, 0.9, 0, Some(filtering), None).unwrap_err();
        assert!(
            format!("{error:#}").contains("Open Image Denoise"),
            "{error:#}"
        );
    }

    /// Filtered: the direct light and the indirect and sky's light each by its own radius, a
    /// texel whose light does not stand weighed 0 (and then filled by its neighbours), summed.
    #[test]
    fn filtering_blurs_direct_and_indirect_light_each_by_its_radius() {
        let used = [true; 4];
        let direct = lit(&[4.0, 0.0, 0.0, 9.0]);
        let indirect = lit(&[0.0, 2.0, 0.0, 0.0]);
        let environment = lit(&[0.0, 0.0, 1.0, 0.0]);
        let charts = [0, 0, 0, 0];
        let buffers = Buffers {
            width: 4,
            height: 1,
            used: &used,
            direct: &direct,
            indirect: Some(&indirect),
            environment: Some(&environment),
            validity: Some(&[0.0, 0.0, 0.0, 0.5]),
            charts: &charts,
            normals: None,
        };
        let filtering = Filtering {
            direct: PartFiltering {
                denoise: false,
                filter: Some(PartFilter::Gaussian(1.0)),
            },
            indirect: PartFiltering::default(),
        };
        let out = composite(&buffers, 0.9, 0, Some(filtering), None)
            .unwrap()
            .lightmap;
        let k = filter::kernel(1.0);
        // Direct: the first two mix (the fourth does not stand); indirect + sky unfiltered.
        let d0 = 4.0 * k[1] / (k[1] + k[2]);
        let d1 = 4.0 * k[0] / (k[0] + k[1] + k[2]);
        let near = |a: f32, b: f32| (a - b).abs() < 1e-5;
        assert!(near(out[0][0], d0) && near(out[1][0], d1 + 2.0), "{out:?}");
        assert!(near(out[2][0], 1.0), "{out:?}");
        // The fourth's light does not stand: its neighbour's, as before filtering.
        assert!(near(out[3][0], out[2][0]) && out[3][3] == 1.0, "{out:?}");
        // Without filtering the sum stays as it was.
        let plain = composite(&buffers, 0.9, 0, None, None).unwrap().lightmap;
        assert!(
            near(plain[0][0], 4.0) && near(plain[1][0], 2.0),
            "{plain:?}"
        );
    }
}
