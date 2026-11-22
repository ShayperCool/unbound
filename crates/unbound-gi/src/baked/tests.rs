use super::atlas::*;

fn instance(rect: [f32; 2], morton: u32) -> Instance {
    Instance {
        probe_lit: false,
        scene: [1, 2, 3, 4],
        parameters: Parameters::DEFAULT,
        rect,
        uv_bounds: [0.0, 0.0, 1.0, 1.0],
        morton,
    }
}

fn settings(max_size: i32, padding: i32) -> Settings {
    Settings {
        max_size,
        padding,
        resolution: 10.0,
        fixed_size: false,
    }
}

#[test]
fn rectangles_fill_the_tree_and_a_new_atlas_opens_when_full() {
    let instances: Vec<Instance> = (0..5).map(|k| instance([30.0, 30.0], k)).collect();
    let layout = layout(&settings(64, 2), &instances).unwrap();
    // Four fill the first atlas (split at 30 + 2 padding); the fifth opens a second, which
    // halves to 32 (the rectangle fits it once its padding is dropped at the sides) and no
    // further (16 is too small).
    assert_eq!(layout.lightmaps, vec![[64, 64], [32, 32]]);
    let offsets: Vec<(u32, [f32; 2])> = layout
        .placements
        .iter()
        .map(|p| (p.lightmap, [p.scale_offset[2], p.scale_offset[3]]))
        .collect();
    let s = 1.0 / 64.0;
    assert_eq!(
        offsets,
        vec![
            (0, [0.0, 0.0]),
            (0, [32.0 * s, 0.0]),
            (0, [0.0, 32.0 * s]),
            (0, [32.0 * s, 32.0 * s]),
            (1, [0.0, 0.0]),
        ]
    );
    assert_eq!(layout.placements[0].scale_offset[0], 30.0 / 64.0);
    assert_eq!(layout.placements[4].scale_offset[0], 30.0 / 32.0);
}

#[test]
fn padding_is_left_out_at_the_atlas_sides() {
    let layout = layout(&settings(64, 2), &[instance([63.0, 63.0], 0)]).unwrap();
    assert_eq!(layout.lightmaps, vec![[64, 64]]);
    let fixed = Settings {
        fixed_size: true,
        ..settings(64, 2)
    };
    let two = super::atlas::layout(
        &fixed,
        &[instance([10.0, 10.0], 0), instance([10.0, 10.0], 1)],
    )
    .unwrap();
    // A fixed size keeps the atlas whole.
    assert_eq!(two.lightmaps, vec![[64, 64]]);
}

#[test]
fn a_rectangle_bigger_than_an_atlas_is_scaled_down_to_it() {
    let mut big = instance([100.0, 50.0], 0);
    big.uv_bounds = [0.0, 0.0, 1.0, 0.5];
    let layout = layout(&settings(64, 0), &[big]).unwrap();
    // Scaled by 64/100 (64 × 32 up to rounding), and the atlas does not halve (64 wide).
    assert_eq!(layout.lightmaps, vec![[64, 64]]);
    let k = 100.0f32 * (64.0f32 / 100.0) / 1.0;
    assert_eq!(
        layout.placements[0].scale_offset,
        [k / 64.0, k / 64.0, 0.0, 0.0]
    );
}

#[test]
fn renderers_go_in_morton_order_then_larger_first() {
    // Given in another order: the first by Morton code, then of equal codes the larger.
    let instances = vec![
        instance([10.0, 10.0], 5),
        instance([20.0, 20.0], 5),
        instance([30.0, 30.0], 1),
    ];
    let layout = layout(&settings(64, 0), &instances).unwrap();
    let at = |i: usize| {
        [
            layout.placements[i].scale_offset[2],
            layout.placements[i].scale_offset[3],
        ]
    };
    // 30 first at the origin, 20 to its right, then 10 in the leftover above the 20.
    assert_eq!(at(2), [0.0, 0.0]);
    assert_eq!(at(1), [30.0 / 64.0, 0.0]);
    assert_eq!(at(0), [30.0 / 64.0, 20.0 / 64.0]);
}

#[test]
fn lightmap_parameters_and_scenes_keep_atlases_apart() {
    let mut tagged = instance([10.0, 10.0], 0);
    tagged.parameters.baked_lightmap_tag = 3;
    let mut other_scene = instance([10.0, 10.0], 0);
    other_scene.scene = [0, 0, 0, 1];
    let layout = layout(
        &settings(64, 0),
        &[instance([10.0, 10.0], 0), tagged, other_scene],
    )
    .unwrap();
    assert_eq!(layout.lightmaps.len(), 3);
    // The other scene's GUID is lower: its atlas comes first.
    assert_eq!(layout.placements[2].lightmap, 0);
    let mut limited = instance([10.0, 10.0], 0);
    limited.parameters.max_lightmap_count = 1;
    assert!(super::atlas::layout(&settings(64, 0), &[limited]).is_err());
}

#[test]
fn a_rectangle_comes_from_area_uv_bounds_and_texels_per_unit() {
    // A unit cube (area 6) with UVs over the unit square at 10 texels per unit: sqrt(6) × 10.
    assert_eq!(
        rectangle(1.0, 10.0, None, 6.0, [0.0, 0.0, 1.0, 1.0], 1024),
        [24.0, 24.0]
    );
    // Shaped as its UV bounds; halved per LOD level; at least 1; at most the max size.
    assert_eq!(
        rectangle(1.0, 10.0, None, 6.0, [0.0, 0.0, 1.0, 0.5], 1024),
        [34.0, 17.0]
    );
    assert_eq!(
        rectangle(1.0, 10.0, Some(2), 6.0, [0.0, 0.0, 1.0, 1.0], 1024),
        [6.0, 6.0]
    );
    assert_eq!(
        rectangle(0.0, 10.0, None, 6.0, [0.0, 0.0, 1.0, 1.0], 1024),
        [1.0, 1.0]
    );
    assert_eq!(
        rectangle(100.0, 10.0, None, 6.0, [0.0, 0.0, 1.0, 1.0], 512),
        [512.0, 512.0]
    );
    // No UV area: a texel.
    assert_eq!(rectangle(1.0, 10.0, None, 6.0, [0.0; 4], 1024), [1.0, 1.0]);
}

#[test]
fn surface_area_and_uv_bounds() {
    let quad = [
        [0.0, 0.0, 0.0],
        [1.0, 0.0, 0.0],
        [1.0, 1.0, 0.0],
        [0.0, 1.0, 0.0],
    ];
    let triangles = [[0, 1, 2], [0, 2, 3], [0, 2, 9]];
    let mut scale2 = [0f32; 16];
    for (k, v) in [(0, 2.0), (5, 3.0), (10, 1.0), (15, 1.0)] {
        scale2[k] = v;
    }
    // The out-of-range triangle is left out.
    assert_eq!(surface_area(&quad, &triangles, &scale2), 6.0);
    assert_eq!(
        uv_bounds(&[[0.25, 0.5], [0.75, 0.0], [0.5, 1.0]]),
        [0.25, 0.0, 0.5, 1.0]
    );
    assert_eq!(uv_bounds(&[]), [0.0; 4]);
}

#[test]
fn morton_codes_interleave_tenths_of_a_unit() {
    let base = morton([0.0, 0.0, 0.0]);
    // 65536 units in tenths is 655360 = 0x0a0000: its low 10 bits are 0.
    assert_eq!(base, 0);
    assert_eq!(morton([0.1, 0.0, 0.0]), 1);
    assert_eq!(morton([0.0, 0.1, 0.0]), 2);
    assert_eq!(morton([0.0, 0.0, 0.1]), 4);
    assert_eq!(morton([0.2, 0.0, 0.0]), 8);
    // Below -65536 a coordinate's magnitude counts (all three).
    assert_eq!(
        morton([-70000.0, 0.0, 0.0]),
        morton([70000.0 - 65536.0, -65536.0, -65536.0])
    );
}

#[test]
fn only_a_leaf_across_the_whole_atlas_scales_a_rectangle_down() {
    // The first splits the atlas into a 64 × 10 strip (then 40 + 24) and the rest above. The
    // second, wider than the 24 left of the strip, which does not reach the atlas's left side,
    // goes above whole (its scale from its height: its UV bounds are square).
    let layout = layout(
        &Settings {
            fixed_size: true,
            ..settings(64, 0)
        },
        &[instance([40.0, 10.0], 0), instance([30.0, 5.0], 1)],
    )
    .unwrap();
    assert_eq!(
        layout.placements[1].scale_offset,
        [5.0 / 64.0, 5.0 / 64.0, 0.0, 10.0 / 64.0]
    );
}

#[test]
fn lightmap_parameter_groups_go_in_the_order_of_their_hashes() {
    // tests/projects/bakedatlas Tags: the Editor's lightmaps of each group, in order.
    let d = Parameters::DEFAULT;
    let groups = [
        Parameters {
            supersampling: 4,
            ..d
        },
        Parameters {
            baked_lightmap_tag: 1,
            ..d
        },
        d,
        Parameters {
            back_face_tolerance: 0.5,
            ..d
        },
        Parameters { pushoff: 0.01, ..d },
        Parameters {
            baked_lightmap_tag: 2,
            ..d
        },
    ];
    let hashes: Vec<(u64, u64)> = groups.iter().map(Parameters::hash).collect();
    assert!(hashes.windows(2).all(|w| w[0] < w[1]), "{hashes:x?}");
}

#[test]
fn anti_aliasing_samples_give_one_two_or_four_samples_a_side() {
    // tests/projects/bakedbuffers Samples (1, 3, 4, 5, 8, 9, 16, 256), bakedatlas Tags (2).
    let side = Parameters::supersampling_of;
    assert_eq!([side(1), side(2), side(3)], [1, 2, 2]);
    assert_eq!(
        [side(4), side(5), side(8), side(9), side(16), side(256)],
        [4; 6]
    );
    // Not measured: below one sample.
    assert_eq!([side(0), side(-3)], [1, 1]);
}

#[test]
fn renderers_are_numbered_by_the_hashes_of_their_file_ids() {
    // tests/projects/bakedbuffers Shapes: the first renderers of the Editor's bake, in its
    // order, by their file IDs in the scene.
    let in_order = [879057353i64, 244122856, 1502029005, 1818302457, 633045107];
    let keys: Vec<(u64, u64)> = in_order.iter().map(|&id| object_key(id, 0)).collect();
    assert!(keys.windows(2).all(|w| w[0] < w[1]), "{keys:x?}");
    // The prefab instance's file ID is part of it.
    assert_ne!(object_key(879057353, 0), object_key(879057353, 1));
}

#[test]
fn a_renderer_whose_lightmap_uvs_span_no_area_fails_the_layout() {
    let mut bare = instance([1.0, 1.0], 0);
    bare.uv_bounds = [0.0; 4];
    assert!(layout(&settings(64, 2), &[bare]).is_err());
}

#[test]
fn many_rectangles_land_inside_their_lightmaps_without_overlapping() {
    // Sizes from a fixed sequence, Morton codes in another order: every rectangle with its
    // padding within its lightmap, none overlapping another of the same lightmap.
    let instances: Vec<Instance> = (0..1500u32)
        .map(|k| {
            instance(
                [1.0 + (k * 7 % 23) as f32, 1.0 + (k * 11 % 17) as f32],
                k.wrapping_mul(2654435761) % 4096,
            )
        })
        .collect();
    let s = settings(256, 2);
    let layout = layout(&s, &instances).unwrap();
    let mut boxes: Vec<(u32, [f32; 4])> = Vec::new();
    for (i, p) in layout.placements.iter().enumerate() {
        let [w, h] = layout.lightmaps[p.lightmap as usize];
        let side = w.max(h) as f32;
        // Square UV bounds: the scale is the rectangle's height over the side.
        let x = p.scale_offset[2] * side;
        let y = p.scale_offset[3] * side;
        let [rw, rh] = instances[i].rect;
        assert!(
            x >= 0.0 && y >= 0.0 && x + rw <= w as f32 && y + rh <= h as f32,
            "{i}: {x},{y} in {w}x{h}"
        );
        boxes.push((p.lightmap, [x, y, x + rw, y + rh]));
    }
    for (a, (la, ba)) in boxes.iter().enumerate() {
        for (lb, bb) in &boxes[a + 1..] {
            let apart =
                la != lb || ba[2] <= bb[0] || bb[2] <= ba[0] || ba[3] <= bb[1] || bb[3] <= ba[1];
            assert!(apart, "{ba:?} and {bb:?} overlap in lightmap {la}");
        }
    }
}

#[test]
fn probe_lit_renderers_get_atlases_of_their_own_that_are_no_lightmaps() {
    // tests/projects/bakedatlas Spread `Probes`: after the others, a lightmap-sized scale of its
    // own reduced atlas, index 0xFFFE, and no lightmap added.
    let mut probe_lit = instance([30.0, 30.0], 0);
    probe_lit.probe_lit = true;
    let lit = instance([10.0, 10.0], 5);
    let layout = layout(&settings(1024, 2), &[probe_lit, lit]).unwrap();
    assert_eq!(layout.lightmaps, vec![[16, 16]]);
    assert_eq!(layout.placements[1].lightmap, 0);
    assert_eq!(layout.placements[0].lightmap, NO_LIGHTMAP);
    assert_eq!(
        layout.placements[0].scale_offset,
        [30.0 / 32.0, 30.0 / 32.0, 0.0, 0.0]
    );
}

mod texels {
    use crate::baked::gbuffer::{self, GBuffer, Instance, Mesh, MeshError};
    use crate::baked::raster::{self, Texel};
    use std::collections::BTreeMap;

    /// The texels a triangle gets in a lightmap, with the point each is sampled at (the
    /// corners' UVs by their weights, in texels).
    fn drawn(
        uv: [[f32; 2]; 3],
        width: usize,
        height: usize,
        coverage: &mut [f32],
    ) -> BTreeMap<(usize, usize), [f32; 2]> {
        let mut out = BTreeMap::new();
        raster::triangle(uv, width, height, coverage, |t: Texel| {
            let point = |k: usize| {
                (0..3)
                    .map(|c| uv[t.corners[c]][k] * t.weights[c])
                    .sum::<f32>()
                    * [width, height][k] as f32
            };
            out.insert((t.x, t.y), [point(0), point(1)]);
        });
        out
    }

    fn near(a: [f32; 2], b: [f32; 2]) -> bool {
        (a[0] - b[0]).abs() < 1e-5 && (a[1] - b[1]).abs() < 1e-5
    }

    #[test]
    fn a_triangle_gets_the_texels_it_holds_and_those_it_only_touches() {
        // The corner of a 4-texel lightmap below the diagonal x + y = 3.6.
        let mut coverage = vec![0f32; 16];
        let texels = drawn([[0.0, 0.0], [0.9, 0.0], [0.0, 0.9]], 4, 4, &mut coverage);
        // Every texel the diagonal reaches: those whose centres are below it, sampled there
        // and covered whole, and the four it cuts a corner of (0.6 texels along each side:
        // 0.18 of the texel), sampled at the middle of that corner.
        assert_eq!(texels.len(), 10);
        for (&(x, y), &point) in &texels {
            let whole = x + y < 3;
            assert!(x + y <= 3, "({x}, {y})");
            let expected = if whole {
                [x as f32 + 0.5, y as f32 + 0.5]
            } else {
                [x as f32 + 0.2, y as f32 + 0.2]
            };
            assert!(near(point, expected), "({x}, {y}) sampled at {point:?}");
            let covered = coverage[4 * y + x];
            assert!(
                if whole {
                    covered == 1.0
                } else {
                    (covered - 0.18).abs() < 1e-5
                },
                "({x}, {y}) covered {covered}"
            );
        }
        // Beyond the diagonal nothing is covered.
        assert_eq!(coverage[4 * 3 + 3], 0.0);
        assert_eq!(coverage[4 * 2 + 2], 0.0);
    }

    #[test]
    fn both_windings_get_the_same_texels_at_the_same_points() {
        let (a, b, c) = ([0.1, 0.2], [0.9, 0.3], [0.4, 0.95]);
        let one = drawn([a, b, c], 8, 4, &mut vec![0f32; 32]);
        let other = drawn([b, a, c], 8, 4, &mut vec![0f32; 32]);
        assert!(one.len() > 10);
        assert_eq!(
            one.keys().collect::<Vec<_>>(),
            other.keys().collect::<Vec<_>>()
        );
        for (texel, point) in &one {
            assert!(
                near(*point, other[texel]),
                "{texel:?}: {point:?} and {:?}",
                other[texel]
            );
        }
    }

    #[test]
    fn a_texel_keeps_the_triangle_that_covers_most_of_it_and_the_later_of_equals() {
        let whole = [[0.0, 0.0], [1.0, 0.0], [0.0, 1.0]];
        // A sliver along the bottom of the first row of texels: a tenth of a texel high.
        let sliver = [[0.0, 0.0], [1.0, 0.0], [0.0, 0.025]];
        let draw = |order: &[(usize, [[f32; 2]; 3])]| {
            let mut coverage = vec![0f32; 16];
            let mut owner = vec![usize::MAX; 16];
            for &(id, uv) in order {
                raster::triangle(uv, 4, 4, &mut coverage, |t: Texel| {
                    owner[4 * t.y + t.x] = id
                });
            }
            owner
        };
        // The sliver holds no texel's centre: the triangle that does keeps the texel, drawn
        // before or after.
        assert_eq!(draw(&[(1, whole), (2, sliver)])[..3], [1, 1, 1]);
        assert_eq!(draw(&[(2, sliver), (1, whole)])[..3], [1, 1, 1]);
        // Alone, the sliver gets the texels it touches.
        assert_eq!(draw(&[(2, sliver)])[..4], [2, 2, 2, 2]);
        assert_eq!(draw(&[(2, sliver)])[4], usize::MAX);
        // The same triangle twice: the later one stays.
        assert_eq!(draw(&[(1, whole), (2, whole)])[0], 2);
    }

    #[test]
    fn small_and_flat_triangles() {
        // Within one texel of a 4 × 4 lightmap, far from its centre: that texel alone, sampled
        // at the triangle's middle.
        let small = [[0.52, 0.27], [0.56, 0.27], [0.52, 0.31]];
        let mut coverage = vec![0f32; 16];
        let texels = drawn(small, 4, 4, &mut coverage);
        assert_eq!(texels.keys().copied().collect::<Vec<_>>(), [(2, 1)]);
        assert!(near(
            texels[&(2, 1)],
            [4.0 * (0.52 + 0.04 / 3.0), 4.0 * (0.27 + 0.04 / 3.0)]
        ));
        assert!((coverage[4 + 2] - 0.0128).abs() < 1e-6, "{}", coverage[6]);
        // Without area in the lightmap: nothing.
        for flat in [[[0.3, 0.3]; 3], [[0.1, 0.1], [0.5, 0.5], [0.9, 0.9]]] {
            let mut coverage = vec![0f32; 16];
            assert!(drawn(flat, 4, 4, &mut coverage).is_empty());
            assert!(coverage.iter().all(|&c| c == 0.0));
        }
        // Not numbers: nothing, and no panic.
        assert!(
            drawn(
                [[f32::NAN, 0.0], [1.0, 0.0], [0.0, 1.0]],
                4,
                4,
                &mut vec![0f32; 16]
            )
            .is_empty()
        );
    }

    #[test]
    fn a_triangle_beyond_the_lightmap_gets_only_texels_within_it() {
        let texels = drawn(
            [[-1.0, -1.0], [3.0, -1.0], [-1.0, 3.0]],
            16,
            16,
            &mut vec![0f32; 256],
        );
        assert_eq!(texels.len(), 256);
    }

    fn quad(uv2: [[f32; 2]; 4]) -> Mesh {
        Mesh::new(
            vec![
                [0.0, 0.0, 0.0],
                [1.0, 0.0, 0.0],
                [1.0, 1.0, 0.0],
                [0.0, 1.0, 0.0],
            ],
            &[[0.0, 0.0, -2.0]; 4],
            &[[0.0, 0.0]; 4],
            &uv2,
            vec![vec![[0, 2, 1], [0, 3, 2]]],
        )
        .unwrap()
    }

    const UNIT: [[f32; 2]; 4] = [[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]];

    #[test]
    fn a_mesh_for_the_bake_has_unit_normals_and_lightmap_uvs_over_their_bounds() {
        let mesh = quad([[0.25, 0.5], [0.75, 0.5], [0.75, 0.6], [0.25, 0.6]]);
        assert_eq!(mesh.normals, [[0.0, 0.0, -1.0]; 4]);
        assert_eq!(mesh.uv_scale_offset[..2], [2.0, 1.0 / (0.6f32 - 0.5)]);
        assert_eq!(mesh.uvs[0], [0.0, 0.0]);
        assert!((mesh.uvs[2][0] - 1.0).abs() < 1e-6 && (mesh.uvs[2][1] - 1.0).abs() < 1e-6);
        // Without lightmap UVs its UVs are taken.
        let positions = vec![[0.0; 3], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]];
        let triangles = vec![vec![[0u32, 1, 2]]];
        let uv = [[0.0, 0.0], [0.5, 0.0], [0.0, 0.5]];
        let from_uv = Mesh::new(
            positions.clone(),
            &[[0.0, 0.0, 1.0]; 3],
            &uv,
            &[],
            triangles.clone(),
        )
        .unwrap();
        assert_eq!(from_uv.uvs, [[0.0, 0.0], [1.0, 0.0], [0.0, 1.0]]);
        // A normal without length points up; the others keep their direction.
        assert_eq!(gbuffer::unit_normal([0.0, 0.000_004, 0.0]), [0.0, 1.0, 0.0]);
        assert_eq!(gbuffer::unit_normal([0.0; 3]), [0.0, 1.0, 0.0]);
        assert_eq!(gbuffer::unit_normal([3.0, 0.0, 4.0]), [0.6, 0.0, 0.8]);
        // UVs on one line: no scale along the side without extent.
        assert_eq!(
            gbuffer::uv_scale_offset([0.5, 0.25, 0.0, 2.0]),
            [0.0, 0.5, -0.0, -0.125]
        );
        // What cannot be baked.
        let new = |n: &[[f32; 3]], uv: &[[f32; 2]], t: Vec<Vec<[u32; 3]>>| {
            Mesh::new(positions.clone(), n, uv, &[], t).err()
        };
        assert_eq!(new(&[], &uv, triangles.clone()), Some(MeshError::NoNormals));
        assert_eq!(
            new(&[[0.0, 0.0, 1.0]; 3], &[], triangles.clone()),
            Some(MeshError::NoUvs)
        );
        assert_eq!(
            new(
                &[[0.0, 0.0, 1.0]; 3],
                &[[0.0, 0.0], [f32::INFINITY, 0.0], [0.0, 1.0]],
                triangles.clone()
            ),
            Some(MeshError::UvNotFinite)
        );
        // A triangle naming a vertex the mesh does not have is left out.
        let kept = Mesh::new(
            positions.clone(),
            &[[0.0, 0.0, 1.0]; 3],
            &uv,
            &[],
            vec![vec![[0, 1, 3], [0, 1, 2]]],
        )
        .unwrap();
        assert_eq!(kept.submeshes, vec![vec![[0, 1, 2]]]);
        assert_eq!(
            Mesh::new(vec![], &[], &[], &[], vec![]).err(),
            Some(MeshError::NoPositions)
        );
    }

    /// A mesh in the xy plane whose UVs follow its positions.
    fn strip(
        positions: Vec<[f32; 3]>,
        normals: Vec<[f32; 3]>,
        submeshes: Vec<Vec<[u32; 3]>>,
    ) -> Mesh {
        let uv: Vec<[f32; 2]> = positions.iter().map(|p| [p[0] / 4.0, p[1]]).collect();
        Mesh::new(positions, &normals, &uv, &[], submeshes).unwrap()
    }

    #[test]
    fn charts_are_numbered_as_their_triangles_open_them() {
        let up = [0.0, 0.0, 1.0];
        let p = |x: f32, y: f32| [x, y, 0.0];
        // Three triangles in a row given first, third, second, then one apart: the second
        // joins the charts of the other two, whose later number is not given again.
        let positions = vec![
            p(0., 0.),
            p(1., 0.),
            p(0., 1.),
            p(1., 1.),
            p(2., 0.),
            p(2., 1.),
            p(3., 0.),
            p(4., 0.),
            p(3., 1.),
        ];
        let joining = strip(
            positions.clone(),
            vec![up; 9],
            vec![vec![[0, 2, 1], [3, 5, 4], [1, 2, 3], [6, 8, 7]]],
        );
        assert_eq!(joining.charts(0), (vec![vec![0, 0, 0, 2]], 3));
        assert_eq!(joining.charts(5), (vec![vec![5, 5, 5, 7]], 3));
        // Each submesh numbers from the mesh's first again.
        let apart = strip(
            positions.clone(),
            vec![up; 9],
            vec![vec![[0, 2, 1]], vec![[3, 5, 4], [6, 8, 7]]],
        );
        assert_eq!(apart.charts(3), (vec![vec![3], vec![3, 4]], 3));
        // Vertices of the same position, normal and lightmap UV are one: two triangles with
        // their own vertices along a shared edge are one chart, and two when the normals differ.
        let six = vec![
            p(0., 0.),
            p(1., 1.),
            p(1., 0.),
            p(0., 0.),
            p(0., 1.),
            p(1., 1.),
        ];
        let same = strip(six.clone(), vec![up; 6], vec![vec![[0, 1, 2], [3, 4, 5]]]);
        assert_eq!(same.charts(0), (vec![vec![0, 0]], 1));
        let tilted = [0.6, 0.0, 0.8];
        let split = strip(
            six,
            vec![up, up, up, tilted, tilted, tilted],
            vec![vec![[0, 1, 2], [3, 4, 5]]],
        );
        assert_eq!(split.charts(0), (vec![vec![0, 1]], 2));
        // No triangles: no charts.
        assert_eq!(
            strip(positions, vec![up; 9], vec![vec![]]).charts(4),
            (vec![vec![]], 0)
        );
    }

    fn whole(mesh: &Mesh, index: i32) -> Instance<'_> {
        Instance {
            mesh,
            scale_offset: [1.0, 1.0, 0.0, 0.0],
            index,
        }
    }

    #[test]
    fn a_lightmaps_texels_take_their_samples_from_its_renderers() {
        let mesh = quad(UNIT);
        let g: GBuffer = gbuffer::gbuffer(4, 2, 2, &[whole(&mesh, 0)]);
        assert_eq!((g.positions.len(), g.charts.len()), (8 * 4, 8));
        // The quad fills the lightmap: every texel has all four samples, of chart 0.
        assert!(g.occupancy.iter().all(|&n| n == 4));
        assert!(g.charts.iter().all(|&c| c == 0));
        // A sample is the point of the quad under its centre, with the quad's normal and the
        // numbers of its renderer and triangle (the second triangle holds the upper left).
        let sample = 8 * 3 + 1;
        let [x, y, z, renderer] = g.positions[sample];
        assert!(
            (x - 1.5 / 8.0).abs() < 1e-6
                && (y - 3.5 / 4.0).abs() < 1e-6
                && z == 0.0
                && renderer == 0.0
        );
        assert_eq!(g.normals[sample], [0.0, 0.0, -1.0, 1.0]);
        assert_eq!(g.plane_normals[sample][..3], [0.0, 0.0, -1.0]);
        assert_eq!(g.normals[7][3], 0.0);
    }

    #[test]
    fn samples_without_a_surface_borrow_from_their_texels_others() {
        // A triangle over the lower left of the first texel of a 2 × 2 lightmap with four
        // samples a texel: three of the texel's samples are on it, the fourth takes the first's.
        let mesh = Mesh {
            positions: vec![[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, 1.0, 0.0]],
            normals: vec![[0.0, 0.0, 1.0]; 3],
            uvs: vec![[0.0, 0.0], [0.5, 0.0], [0.0, 0.5]],
            uv_scale_offset: [1.0, 1.0, 0.0, 0.0],
            submeshes: vec![vec![[0, 1, 2]]],
        };
        let g = gbuffer::gbuffer(2, 2, 2, &[whole(&mesh, 3)]);
        assert_eq!(g.occupancy, [4, 0, 0, 0]);
        assert_eq!(g.charts, [0, -1, -1, -1]);
        assert_eq!(g.positions[4 + 1], g.positions[0]);
        assert_ne!(g.positions[1], g.positions[0]);
        assert_eq!(g.positions[0][3], 3.0);
        assert_eq!(g.positions[2][3], gbuffer::EMPTY);
        assert_eq!(g.normals[4 * 3 + 3], [0.0, 0.0, 0.0, gbuffer::EMPTY]);
    }

    #[test]
    fn a_later_renderer_takes_the_texels_it_covers_as_much() {
        let mesh = quad(UNIT);
        let g = gbuffer::gbuffer(2, 2, 1, &[whole(&mesh, 0), whole(&mesh, 1)]);
        assert!(g.positions.iter().all(|p| p[3] == 1.0));
        // Its charts are numbered after the first one's.
        assert_eq!(g.charts, [1; 4]);
        // One lightmap side of one sample per texel and of four give the same texels.
        let coarse = gbuffer::gbuffer(4, 4, 1, &[whole(&mesh, 0)]);
        let fine = gbuffer::gbuffer(4, 4, 4, &[whole(&mesh, 0)]);
        assert_eq!(coarse.charts, fine.charts);
        assert_eq!(fine.occupancy, [16; 16]);
        assert_eq!(coarse.occupancy, [1; 16]);
    }
}
