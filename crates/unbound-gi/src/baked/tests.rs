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

mod lighting {
    use crate::baked::direct::{self, MixedMode, Receiver};
    use crate::baked::gbuffer::{self, Instance, Mesh};
    use crate::baked::light::{FAR, Kind, Light, Mode, Tables};
    use crate::baked::trace::{Ray, Scene};

    const IDENTITY: [f32; 16] = [
        1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
    ];

    fn ray(origin: [f32; 3], direction: [f32; 3]) -> Ray {
        Ray { origin, direction }
    }

    /// A unit square in the plane z = `z`, as two triangles sharing a diagonal.
    fn square(z: f32) -> [[[f32; 3]; 3]; 2] {
        let (a, b, c, d) = ([0.0, 0.0, z], [1.0, 0.0, z], [1.0, 1.0, z], [0.0, 1.0, z]);
        [[a, b, c], [a, c, d]]
    }

    #[test]
    fn a_ray_is_stopped_by_what_lies_before_its_limit() {
        let scene = Scene::new(square(1.0));
        let up = [0.0, 0.0, 1.0];
        assert!(scene.occluded(&ray([0.25, 0.5, 0.0], up), 10.0));
        // Either side of a triangle stops it.
        assert!(scene.occluded(&ray([0.25, 0.5, 2.0], [0.0, 0.0, -1.0]), 10.0));
        // Not what lies at or beyond the limit, behind the origin, or beside the ray.
        assert!(!scene.occluded(&ray([0.25, 0.5, 0.0], up), 1.0));
        assert!(!scene.occluded(&ray([0.25, 0.5, 0.0], up), 0.5));
        assert!(!scene.occluded(&ray([0.25, 0.5, 1.5], up), 10.0));
        assert!(!scene.occluded(&ray([1.5, 0.5, 0.0], up), 10.0));
        // A ray that starts on a triangle is not stopped by it.
        assert!(!scene.occluded(&ray([0.25, 0.5, 1.0], up), 10.0));
        // Along the plane: not stopped.
        assert!(!scene.occluded(&ray([-1.0, 0.5, 1.0], [1.0, 0.0, 0.0]), 10.0));
        // The limit is in lengths of the direction.
        assert!(scene.occluded(&ray([0.25, 0.5, 0.0], [0.0, 0.0, 0.5]), 3.0));
        assert!(!scene.occluded(&ray([0.25, 0.5, 0.0], [0.0, 0.0, 0.5]), 1.5));
    }

    #[test]
    fn a_ray_through_a_shared_edge_is_stopped() {
        // Rays through the squares' diagonals, from points and in directions whose products
        // do not come out exact in 32 bits.
        let scene = Scene::new(square(1.0).into_iter().chain(square(3.7)));
        for k in 0..200 {
            let t = 0.003 + k as f32 * 0.004_97;
            let direction = [0.1 + 0.001 * k as f32, -0.07, 0.93];
            let through = [t, t, 1.0];
            let origin: [f32; 3] = std::array::from_fn(|c| through[c] - direction[c] * 0.731);
            assert!(scene.occluded(&ray(origin, direction), 100.0), "{k}");
        }
    }

    #[test]
    fn a_ray_through_the_edges_and_corners_of_a_flat_grid_is_stopped() {
        // A floor of 8 × 8 squares in one plane (their boxes have no thickness, and its
        // triangles are in many leaves): rays through the squares' corners and edges, from
        // below at several slants.
        let floor = (0..64).flat_map(|k| {
            let (x, y) = ((k % 8) as f32 * 0.37, (k / 8) as f32 * 0.37);
            let corner = |dx: f32, dy: f32| [x + dx * 0.37, y + dy * 0.37, 1.3];
            [
                [corner(0.0, 0.0), corner(1.0, 0.0), corner(1.0, 1.0)],
                [corner(0.0, 0.0), corner(1.0, 1.0), corner(0.0, 1.0)],
            ]
        });
        let scene = Scene::new(floor);
        assert_eq!(scene.triangle_count(), 128);
        for k in 0..49 {
            let corner = [(1 + k % 7) as f32 * 0.37, (1 + k / 7) as f32 * 0.37, 1.3];
            for (slant, along) in [
                ([0.0, 0.0, 1.0], 0.0),
                ([0.3, -0.2, 1.0], 0.0),
                ([-0.11, 0.07, 0.4], 0.5),
                ([0.9, 0.9, 0.2], 0.25),
            ] {
                // Through the corner, and through a point along the edge from it.
                let through = [corner[0] + along * 0.37, corner[1], corner[2]];
                let origin: [f32; 3] = std::array::from_fn(|c| through[c] - slant[c] * 1.7);
                assert!(
                    scene.occluded(&ray(origin, slant), 100.0),
                    "{k} {slant:?} {along}"
                );
            }
        }
        // Past the floor's side: not stopped.
        assert!(!scene.occluded(&ray([3.5, 1.0, 0.0], [0.0, 0.0, 1.0]), 100.0));
    }

    #[test]
    fn many_triangles_stop_the_rays_a_search_of_all_of_them_finds() {
        // A fixed scatter of small triangles and of rays.
        let mut state = 12345u32;
        let mut next = move || {
            state = state.wrapping_mul(1664525).wrapping_add(1013904223);
            (state >> 8) as f32 / 16777216.0
        };
        let triangles: Vec<[[f32; 3]; 3]> = (0..500)
            .map(|_| {
                let p = [next() * 10.0, next() * 10.0, next() * 10.0];
                std::array::from_fn(|_| std::array::from_fn(|c| p[c] + next() * 0.8 - 0.4))
            })
            .collect();
        let scene = Scene::new(triangles.iter().copied());
        assert_eq!(scene.triangle_count(), 500);
        let mut stopped = 0;
        for _ in 0..400 {
            let r = ray(
                [next() * 10.0, next() * 10.0, next() * 10.0],
                [next() - 0.5, next() - 0.5, next() - 0.5],
            );
            let alone = triangles
                .iter()
                .any(|t| Scene::new([*t]).occluded(&r, 30.0));
            assert_eq!(scene.occluded(&r, 30.0), alone);
            stopped += alone as u32;
        }
        assert!(stopped > 20 && stopped < 380, "{stopped}");
        // No triangles, and triangles that are not numbers: nothing stops a ray.
        assert!(!Scene::new([]).occluded(&ray([0.0; 3], [0.0, 0.0, 1.0]), FAR));
        let broken = Scene::new([[[f32::NAN, 0.0, 1.0], [1.0, 0.0, 1.0], [0.0, 1.0, 1.0]]]);
        assert_eq!(broken.triangle_count(), 0);
    }

    fn light(kind: Kind) -> Light {
        Light {
            kind,
            mode: Mode::Baked,
            color: [2.0, 1.0, 0.5],
            indirect_color: [2.0, 1.0, 0.5],
            position: [0.0, 0.0, 4.0],
            forward: [0.0, 0.0, -1.0],
            right: [-1.0, 0.0, 0.0],
            range: 10.0,
            cone_angle: 60f32.to_radians(),
            casts_shadows: true,
        }
    }

    fn near(a: f32, b: f32) -> bool {
        (a - b).abs() <= 1e-4 * b.abs().max(1.0)
    }

    #[test]
    fn a_directional_light_gives_its_colour_by_the_surfaces_facing() {
        let tables = Tables::new();
        let sun = light(Kind::Directional);
        let straight = sun
            .arriving(&tables, sun.color, [5.0, 5.0, 0.0], [0.0, 0.0, 1.0])
            .unwrap();
        assert_eq!(
            (straight.color, straight.direction, straight.distance),
            ([2.0, 1.0, 0.5], [0.0, 0.0, 1.0], FAR)
        );
        let tilted = sun
            .arriving(&tables, sun.color, [0.0; 3], [0.0, 0.6, 0.8])
            .unwrap();
        assert!(near(tilted.color[0], 1.6) && near(tilted.color[2], 0.4));
        // Facing away, edge on, or with a normal that is not a number: nothing.
        assert!(
            sun.arriving(&tables, sun.color, [0.0; 3], [0.0, 0.0, -1.0])
                .is_none()
        );
        assert!(
            sun.arriving(&tables, sun.color, [0.0; 3], [1.0, 0.0, 0.0])
                .is_none()
        );
        assert!(
            sun.arriving(&tables, sun.color, [0.0; 3], [f32::NAN, 0.0, 0.0])
                .is_none()
        );
        // A surface's normal without length (normals that cancel out) takes nothing; a point
        // that faces every way (a light probe's) takes all of it, from point lights too.
        assert!(
            sun.arriving(&tables, sun.color, [0.0; 3], [0.0; 3])
                .is_none()
        );
        let around = sun.arriving_around(&tables, sun.color, [0.0; 3]).unwrap();
        assert_eq!(
            (around.color, around.direction),
            ([2.0, 1.0, 0.5], [0.0, 0.0, 1.0])
        );
        let bulb = light(Kind::Point);
        let beside = [bulb.position[0] + 1.0, bulb.position[1], bulb.position[2]];
        assert!(bulb.arriving(&tables, [1.0; 3], beside, [0.0; 3]).is_none());
        let lit = bulb.arriving_around(&tables, [1.0; 3], beside).unwrap();
        assert_eq!(lit.direction, [-1.0, 0.0, 0.0]);
        assert!(lit.color[0] > 0.0);
    }

    #[test]
    fn a_point_light_fades_with_distance_to_nothing_at_its_range() {
        let tables = Tables::new();
        let bulb = light(Kind::Point);
        let up = [0.0, 0.0, 1.0];
        let at = |distance: f32| {
            bulb.arriving(&tables, [1.0; 3], [0.0, 0.0, 4.0 - distance], up)
                .map(|a| a.color[0])
        };
        // The built-in pipeline's falloff: 1 / (1 + 25 d²) of the distance over the range,
        // faded to nothing between 0.8 and 1. It is read from a table of 1024 samples laid
        // out over 1023 steps, which puts it a little beyond the distance asked for.
        let about = |a: f32, b: f32, within: f32| (a - b).abs() <= within * b;
        assert!(about(at(1.0).unwrap(), 1.0 / 1.25, 3e-3));
        assert!(about(at(5.0).unwrap(), 1.0 / 7.25, 3e-3));
        // (Steeper where it fades.)
        assert!(about(
            at(9.0).unwrap(),
            1.0 / 21.25 * (1.0 - (0.81 - 0.64) / 0.36),
            2e-2
        ));
        assert!(at(1.0).unwrap() < 1.0 / 1.25 && at(5.0).unwrap() < 1.0 / 7.25);
        assert!(at(9.999).unwrap() < 1e-4);
        assert!(at(10.0).is_none() && at(12.0).is_none());
        // At the light: its whole colour, whatever the surface faces.
        let here = bulb
            .arriving(&tables, bulb.color, bulb.position, [0.0, 0.0, -1.0])
            .unwrap();
        assert_eq!((here.color, here.distance), (bulb.color, 0.0));
        // Its direction and distance are to the light; below the surface it gives nothing.
        let beside = bulb
            .arriving(&tables, [1.0; 3], [3.0, 0.0, 0.0], up)
            .unwrap();
        assert!(
            near(beside.distance, 5.0)
                && near(beside.direction[0], -0.6)
                && near(beside.direction[2], 0.8)
        );
        assert!(
            bulb.arriving(&tables, [1.0; 3], [0.0, 0.0, 5.0], up)
                .is_none()
        );
    }

    #[test]
    fn a_spot_light_ends_at_its_cone() {
        let tables = Tables::new();
        let spot = light(Kind::Spot);
        let up = [0.0, 0.0, 1.0];
        // Lit from 4 above: at `x` from the axis the direction is atan(x / 4) off it.
        let at = |x: f32| {
            spot.arriving(&tables, [1.0; 3], [x, 0.0, 0.0], up)
                .map(|a| a.color[0])
        };
        let point = |x: f32| {
            light(Kind::Point)
                .arriving(&tables, [1.0; 3], [x, 0.0, 0.0], up)
                .unwrap()
                .color[0]
        };
        // On the axis and well inside the cone: the point light's value.
        assert!(near(at(0.0).unwrap(), point(0.0)));
        assert!(near(at(0.5).unwrap(), point(0.5)));
        // Towards the edge (30 degrees: 2.309 from the axis) it fades; beyond it, nothing.
        let (inner, outer) = (
            at(2.0).unwrap() / point(2.0),
            at(2.28).unwrap() / point(2.28),
        );
        assert!(
            inner < 0.9 && outer < inner && outer > 0.0 && outer < 0.1,
            "{inner} {outer}"
        );
        assert!(at(2.32).is_none() && at(8.0).is_none());
        // Behind the spot: nothing.
        assert!(
            spot.arriving(&tables, [1.0; 3], [0.0, 0.0, 5.0], [0.0, 0.0, -1.0])
                .is_none()
        );
    }

    #[test]
    fn mixed_lights_bake_their_direct_light_only_in_subtractive_mode() {
        let (mut baked, mut mixed) = (light(Kind::Point), light(Kind::Point));
        baked.mode = Mode::Baked;
        mixed.mode = Mode::Mixed;
        for mode in [
            MixedMode::IndirectOnly,
            MixedMode::Subtractive,
            MixedMode::Shadowmask,
        ] {
            assert!(direct::bakes_direct(&baked, mode));
            assert_eq!(
                direct::bakes_direct(&mixed, mode),
                mode == MixedMode::Subtractive
            );
        }
    }

    /// A unit quad in the plane z = 0 facing +z, filling a lightmap.
    fn floor() -> Mesh {
        Mesh::new(
            vec![
                [0.0, 0.0, 0.0],
                [1.0, 0.0, 0.0],
                [1.0, 1.0, 0.0],
                [0.0, 1.0, 0.0],
            ],
            &[[0.0, 0.0, 1.0]; 4],
            &[[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]],
            &[],
            vec![vec![[0, 1, 2], [0, 2, 3]]],
        )
        .unwrap()
    }

    #[test]
    fn a_shadowmask_channel_keeps_what_of_a_texel_its_light_reaches_and_sees() {
        let mesh = floor();
        let instances = [Instance {
            mesh: &mesh,
            scale_offset: [1.0, 1.0, 0.0, 0.0],
            index: 0,
        }];
        let g = gbuffer::gbuffer(4, 4, 2, &instances);
        let mut sun = light(Kind::Directional);
        sun.forward = [0.0, -0.6, -0.8];
        sun.mode = Mode::Mixed;
        // A roof over a quarter of the texels' width: x < 0.375.
        let roof = Scene::new([
            [[0.0, -2.0, 1.0], [0.375, -2.0, 1.0], [0.375, 4.0, 1.0]],
            [[0.0, -2.0, 1.0], [0.375, 4.0, 1.0], [0.0, 4.0, 1.0]],
        ]);
        let receivers = [Receiver {
            local_to_world: IDENTITY,
            receives_shadows: true,
            viewer: crate::baked::trace::Viewer::Lod0,
        }];
        let masks = |mixed: MixedMode, channel: Option<u8>| {
            direct::direct(&g, &receivers, &[sun], &[channel], mixed, &roof, 0.0001).shadowmask
        };
        let mask = masks(MixedMode::Shadowmask, Some(2));
        for (texel, value) in mask.iter().enumerate() {
            // Columns: under the roof, half under it, out of it.
            let want = match texel % 4 {
                0 => 0.0,
                1 => 0.5,
                _ => 1.0,
            };
            assert_eq!(*value, [0.0, 0.0, want, 0.0], "{texel}");
        }
        // No shadowmask outside the Shadowmask Lighting Mode, nor for a light without a
        // channel; its direct light is not baked either way.
        assert!(
            masks(MixedMode::IndirectOnly, Some(2))
                .iter()
                .all(|v| *v == [0.0; 4])
        );
        assert!(
            masks(MixedMode::Shadowmask, None)
                .iter()
                .all(|v| *v == [0.0; 4])
        );
        // A spot light whose cone reaches half of a texel's samples, seen by all: the whole
        // channel, as the part of the reached samples that see it.
        let mut spot = light(Kind::Spot);
        spot.mode = Mode::Mixed;
        spot.position = [0.5, 0.5, 4.0];
        spot.cone_angle = 4f32.to_radians();
        spot.range = 10.0;
        let lit = direct::direct(
            &g,
            &receivers,
            &[spot],
            &[Some(0)],
            MixedMode::Shadowmask,
            &Scene::new([]),
            0.0001,
        )
        .shadowmask;
        assert!(lit.iter().all(|v| v[0] == 0.0 || v[0] == 1.0), "{lit:?}");
        assert!(lit.iter().any(|v| v[0] == 1.0));
    }

    #[test]
    fn texels_take_the_mean_light_of_their_samples_unless_shadowed() {
        let mesh = floor();
        let instances = [Instance {
            mesh: &mesh,
            scale_offset: [1.0, 1.0, 0.0, 0.0],
            index: 0,
        }];
        let g = gbuffer::gbuffer(4, 4, 2, &instances);
        let mut sun = light(Kind::Directional);
        sun.forward = [0.0, -0.6, -0.8];
        // A roof over the half x < 0.5, high enough that the slanted light still meets it.
        let roof = Scene::new([
            [[0.0, -2.0, 1.0], [0.5, -2.0, 1.0], [0.5, 4.0, 1.0]],
            [[0.0, -2.0, 1.0], [0.5, 4.0, 1.0], [0.0, 4.0, 1.0]],
        ]);
        let lit = |receives: bool, casts: bool, mode: Mode, mixed: MixedMode, scene: &Scene| {
            let mut sun = sun;
            sun.casts_shadows = casts;
            sun.mode = mode;
            let receivers = [Receiver {
                local_to_world: IDENTITY,
                receives_shadows: receives,
                viewer: crate::baked::trace::Viewer::Lod0,
            }];
            direct::direct(&g, &receivers, &[sun], &[], mixed, scene, 0.0001).irradiance
        };
        let open = lit(
            true,
            true,
            Mode::Baked,
            MixedMode::IndirectOnly,
            &Scene::new([]),
        );
        assert!(
            open.iter()
                .all(|t| near(t[0], 1.6) && near(t[1], 0.8) && near(t[2], 0.4) && t[3] == 1.0)
        );
        let roofed = lit(true, true, Mode::Baked, MixedMode::IndirectOnly, &roof);
        for (texel, value) in roofed.iter().enumerate() {
            let shadowed = texel % 4 < 2;
            assert!(
                if shadowed {
                    value[0] == 0.0
                } else {
                    near(value[0], 1.6)
                },
                "{texel}: {value:?}"
            );
            assert_eq!(value[3], 1.0);
        }
        // A renderer that receives no shadows and a light that casts none are lit under the roof.
        assert_eq!(
            lit(false, true, Mode::Baked, MixedMode::IndirectOnly, &roof),
            open
        );
        assert_eq!(
            lit(true, false, Mode::Baked, MixedMode::IndirectOnly, &roof),
            open
        );
        // A mixed light's direct light is baked in Subtractive mode alone.
        assert!(
            lit(true, true, Mode::Mixed, MixedMode::IndirectOnly, &roof)
                .iter()
                .all(|t| t[..3] == [0.0; 3] && t[3] == 1.0)
        );
        assert_eq!(
            lit(true, true, Mode::Mixed, MixedMode::Subtractive, &roof),
            roofed
        );
        // The edge of the roof's shadow within a texel: the mean of its samples.
        let edge = Scene::new([
            [[0.0, -2.0, 1.0], [0.375, -2.0, 1.0], [0.375, 4.0, 1.0]],
            [[0.0, -2.0, 1.0], [0.375, 4.0, 1.0], [0.0, 4.0, 1.0]],
        ]);
        let half = lit(true, true, Mode::Baked, MixedMode::IndirectOnly, &edge);
        assert!(
            near(half[1][0], 0.8) && half[0][0] == 0.0 && near(half[2][0], 1.6),
            "{:?}",
            &half[..4]
        );
        // A sample's rays start pushed off its triangle: what lies nearer than that does not
        // shadow it.
        let film = Scene::new(square(0.00005));
        let receivers = [Receiver {
            local_to_world: IDENTITY,
            receives_shadows: true,
            viewer: crate::baked::trace::Viewer::Lod0,
        }];
        let above = direct::direct(
            &g,
            &receivers,
            &[sun],
            &[],
            MixedMode::IndirectOnly,
            &film,
            0.0001,
        )
        .irradiance;
        let below = direct::direct(
            &g,
            &receivers,
            &[sun],
            &[],
            MixedMode::IndirectOnly,
            &film,
            0.0,
        )
        .irradiance;
        assert!(above.iter().all(|t| near(t[0], 1.6)) && below.iter().all(|t| t[0] == 0.0));
        // An unused texel has nothing, not even its fourth value.
        let small = [Instance {
            mesh: &mesh,
            scale_offset: [0.5, 0.5, 0.0, 0.0],
            index: 0,
        }];
        let partial = gbuffer::gbuffer(4, 4, 2, &small);
        let receivers = [Receiver {
            local_to_world: IDENTITY,
            receives_shadows: true,
            viewer: crate::baked::trace::Viewer::Lod0,
        }];
        let values = direct::direct(
            &partial,
            &receivers,
            &[sun],
            &[],
            MixedMode::IndirectOnly,
            &Scene::new([]),
            0.0001,
        )
        .irradiance;
        assert_eq!(values[3], [0.0; 4]);
        assert!(near(values[0][0], 1.6));
    }

    #[test]
    fn a_renderers_matrix_places_its_samples_and_turns_their_normals() {
        // The floor turned to face +x (its normal +z goes to +x) and moved: lit by a light
        // along -x, and not by one along -z.
        let mesh = floor();
        let instances = [Instance {
            mesh: &mesh,
            scale_offset: [1.0, 1.0, 0.0, 0.0],
            index: 0,
        }];
        let g = gbuffer::gbuffer(2, 2, 1, &instances);
        let turned = [
            0.0, 0.0, -1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 5.0, 0.0, 0.0, 1.0,
        ];
        let receivers = [Receiver {
            local_to_world: turned,
            receives_shadows: true,
            viewer: crate::baked::trace::Viewer::Lod0,
        }];
        let mut along_x = light(Kind::Directional);
        along_x.forward = [-1.0, 0.0, 0.0];
        let lit = direct::direct(
            &g,
            &receivers,
            &[along_x, light(Kind::Directional)],
            &[],
            MixedMode::IndirectOnly,
            &Scene::new([]),
            0.0001,
        )
        .irradiance;
        assert!(lit.iter().all(|t| near(t[0], 2.0)));
        // A wall just in front of where the matrix puts it shadows it.
        let wall = Scene::new([[[5.5, -9.0, -9.0], [5.5, 9.0, -9.0], [5.5, 0.0, 9.0]]]);
        let shadowed = direct::direct(
            &g,
            &receivers,
            &[along_x],
            &[],
            MixedMode::IndirectOnly,
            &wall,
            0.0001,
        )
        .irradiance;
        assert!(shadowed.iter().all(|t| t[0] == 0.0 && t[3] == 1.0));
        // Mirrored, its normal turns with it: the matrix's rows for normals carry the sign.
        let mirrored = [
            -1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
        ];
        let rows = direct::normal_matrix(&mirrored);
        assert_eq!(
            direct::world_normal(&rows, [1.0, 0.0, 0.0]),
            [-1.0, 0.0, 0.0]
        );
        assert_eq!(
            direct::world_normal(&rows, [0.0, 0.0, 1.0]),
            [0.0, 0.0, 1.0]
        );
    }
}

mod bounced {
    use crate::baked::direct::Receiver;
    use crate::baked::gbuffer::{self, Instance, Mesh};
    use crate::baked::grid::{Bounds, Grid};
    use crate::baked::indirect::{self, Material, Settings, Surface, Texture, World};
    use crate::baked::light::{Kind, Light, Mode};
    use crate::baked::sampling::{self, BLUE_NOISE_BYTES, BlueNoiseSize, Samples, Twister};
    use crate::baked::sky::{Importance, Sky};
    use crate::baked::trace::{Ray, Scene};

    const IDENTITY: [f32; 16] = [
        1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
    ];

    #[test]
    fn a_texel_takes_the_nearest_power_of_two_of_the_samples_asked_for() {
        let counts = |asked: &[u32], supersampling| -> Vec<u32> {
            asked
                .iter()
                .map(|&n| sampling::sample_count(n, supersampling))
                .collect()
        };
        // Equally near two powers: the upper one.
        assert_eq!(
            counts(&[0, 1, 2, 3, 5, 6, 10, 24, 64, 100, 512], 1),
            [1, 1, 2, 4, 4, 8, 8, 32, 64, 128, 512]
        );
        // Never fewer than the samples a texel is rasterized with.
        assert_eq!(counts(&[1, 3, 10, 64], 2), [4, 4, 8, 64]);
        assert_eq!(counts(&[1, 10, 64], 4), [16, 16, 64]);
        let distributions: Vec<usize> = [1, 2, 4, 64, 255, 256, 512]
            .map(sampling::distribution)
            .to_vec();
        assert_eq!(distributions, [0, 1, 2, 6, 7, 8, 8]);
    }

    #[test]
    fn the_rotations_generator_is_the_mersenne_twister() {
        // The generator's known first and 10000th numbers for its usual seed.
        let mut twister = Twister::new(5489);
        assert_eq!(twister.draw(), 3_499_211_612);
        let mut last = 0;
        for _ in 1..10_000 {
            last = twister.draw();
        }
        assert_eq!(last, 4_123_659_995);
    }

    #[test]
    fn the_sobol_sequence_halves_its_first_dimension_and_rotations_repeat() {
        assert_eq!(Samples::new(&[0; 10]).err(), Some(BlueNoiseSize(10)));
        let samples = Samples::new(&vec![0; BLUE_NOISE_BYTES]).unwrap();
        let first: Vec<f32> = (0..8).map(|i| samples.sobol(i, 0)).collect();
        assert_eq!(first, [0.0, 0.5, 0.25, 0.75, 0.125, 0.625, 0.375, 0.875]);
        // Every dimension stays in 0..1 and differs from the first.
        for dimension in [1, 2, 3, 14, 1023] {
            let values: Vec<f32> = (1..64).map(|i| samples.sobol(i, dimension)).collect();
            assert!(values.iter().all(|v| (0.0..1.0).contains(v)), "{dimension}");
            assert_ne!(
                values,
                (1..64).map(|i| samples.sobol(i, 0)).collect::<Vec<_>>()
            );
        }
        // A texel has twelve rotations, read in threes from any dimension on; texels repeat
        // every 512 along a wide lightmap, and by its width along a narrow one.
        let rotations = samples.rotation(3, 5, 1024, 0);
        assert!(rotations.iter().all(|r| (0.0..=1.0).contains(r)));
        assert_eq!(samples.rotation(3, 5, 1024, 12), rotations);
        assert_eq!(samples.rotation(3 + 512, 5 + 512, 1024, 0), rotations);
        assert_ne!(samples.rotation(4, 5, 1024, 0), rotations);
        assert_eq!(samples.rotation(3, 5, 1024, 11)[1], rotations[0]);
        assert_eq!(
            samples.rotation(0, 1, 64, 0),
            samples.rotation(64, 0, 1024, 0)
        );
    }

    #[test]
    fn the_blue_noise_sequence_is_ranked_and_scrambled_per_texel_and_sample_count() {
        let mut data = vec![0u8; BLUE_NOISE_BYTES];
        // The sequence: sample 0 has 10 in dimension 1, sample 3 has 20.
        data[1] = 10;
        data[1 + 3 * 256] = 20;
        let tiles = 256 * 256;
        let tile_values = 8 * 128 * 128;
        // For 4 samples (the third distribution), texel (2, 1): dimension 1 is ranked by 3
        // and scrambled by 5. The ranking tiles come first, then the scrambling ones.
        let place = 2 * tile_values + 1 + (2 + 128) * 8;
        data[tiles + place] = 3;
        data[tiles + 9 * tile_values + place] = 5;
        let samples = Samples::new(&data).unwrap();
        assert_eq!(samples.blue(0, 1, 2, 1, 2), (0.5 + (20 ^ 5) as f32) / 256.0);
        // Another texel, and another sample count, read sample 0 as it is.
        assert_eq!(samples.blue(0, 1, 3, 1, 2), 10.5 / 256.0);
        assert_eq!(samples.blue(0, 1, 2, 1, 3), 10.5 / 256.0);
        // The tile repeats every 128 texels, the tiles' dimensions every 8, samples every 256.
        assert_eq!(
            samples.blue(256, 1, 2 + 128, 1 + 128, 2),
            samples.blue(0, 1, 2, 1, 2)
        );
        assert_eq!(samples.blue(0, 9, 2, 1, 2), (0.5 + 5.0) / 256.0);
    }

    #[test]
    fn directions_follow_the_cosine_around_a_normal() {
        // The square's centre is the axis; its corners lie on the horizon.
        assert_eq!(sampling::cosine_direction([0.5, 0.5]), [0.0, 0.0, 1.0]);
        let corner = sampling::cosine_direction([1.0, 1.0]);
        assert!(
            corner[2].abs() < 1e-3
                && (corner[0] * corner[0] + corner[1] * corner[1] - 1.0).abs() < 1e-6
        );
        for normal in [
            [0.0, 0.0, 1.0],
            [0.0, 0.0, -1.0],
            [0.6, 0.0, 0.8],
            [0.0, -1.0, 0.0],
        ] {
            let (t, b) = sampling::basis(normal);
            let dot = |a: [f32; 3], b: [f32; 3]| a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
            assert!(
                dot(t, normal).abs() < 1e-6
                    && dot(b, normal).abs() < 1e-6
                    && dot(t, b).abs() < 1e-6
            );
            assert!((dot(t, t) - 1.0).abs() < 1e-6 && (dot(b, b) - 1.0).abs() < 1e-6);
            assert_eq!(sampling::around(normal, [0.5, 0.5]), normal);
        }
    }

    #[test]
    fn a_ray_finds_the_nearest_triangle_its_side_and_its_number() {
        // Two triangles over the origin, the nearer one given last; one that is not a number
        // between them keeps its number.
        let far = [[-1.0, -1.0, 4.0], [3.0, -1.0, 4.0], [-1.0, 3.0, 4.0]];
        let broken = [[f32::NAN, 0.0, 1.0], [1.0, 0.0, 1.0], [0.0, 1.0, 1.0]];
        let near = [[-1.0, -1.0, 2.0], [3.0, -1.0, 2.0], [-1.0, 3.0, 2.0]];
        let scene = Scene::new([far, broken, near]);
        let up = Ray {
            origin: [0.0; 3],
            direction: [0.0, 0.0, 1.0],
        };
        let hit = scene.closest(&up, f32::INFINITY).unwrap();
        assert_eq!((hit.triangle, hit.distance), (2, 2.0));
        // The corners' cross product points up: a ray going up meets the back.
        assert!(!hit.front);
        assert_eq!(hit.weights, [0.25, 0.25]);
        let down = Ray {
            origin: [0.0, 0.0, 5.0],
            direction: [0.0, 0.0, -1.0],
        };
        let hit = scene.closest(&down, f32::INFINITY).unwrap();
        assert_eq!((hit.triangle, hit.distance, hit.front), (0, 1.0, true));
        // Nothing before the limit, nothing beside the triangles.
        assert_eq!(scene.closest(&up, 2.0), None);
        assert_eq!(
            scene.closest(
                &Ray {
                    origin: [5.0, 5.0, 0.0],
                    direction: [0.0, 0.0, 1.0]
                },
                f32::INFINITY
            ),
            None
        );
        assert_eq!(Scene::new([]).closest(&up, f32::INFINITY), None);
    }

    /// A unit quad in the plane z = 0 facing +z, filling a lightmap.
    fn floor() -> Mesh {
        Mesh::new(
            vec![
                [0.0, 0.0, 0.0],
                [1.0, 0.0, 0.0],
                [1.0, 1.0, 0.0],
                [0.0, 1.0, 0.0],
            ],
            &[[0.0, 0.0, 1.0]; 4],
            &[[0.0, 0.0], [1.0, 0.0], [1.0, 1.0], [0.0, 1.0]],
            &[],
            vec![vec![[0, 1, 2], [0, 2, 3]]],
        )
        .unwrap()
    }

    /// A wide ceiling at z = 2 whose front faces down (its corners' cross product points down).
    fn ceiling(material: u32) -> Surface {
        Surface {
            material,
            mirrored: false,
            double_sided: false,
            corners: [
                [-500.0, -500.0, 2.0],
                [-500.0, 1000.0, 2.0],
                [1000.0, -500.0, 2.0],
            ],
            normals: [[0.0, 0.0, -1.0]; 3],
            uvs: [[0.0, 0.0], [0.0, 1.0], [1.0, 0.0]],
        }
    }

    fn sun_from_below() -> Light {
        Light {
            kind: Kind::Directional,
            mode: Mode::Baked,
            color: [9.0, 9.0, 9.0],
            indirect_color: [2.0, 1.0, 0.5],
            position: [0.0; 3],
            forward: [0.0, 0.0, 1.0],
            right: [1.0, 0.0, 0.0],
            range: 0.0,
            cone_angle: 1.0,
            casts_shadows: true,
        }
    }

    struct Bake {
        surfaces: Vec<Surface>,
        materials: Vec<Material>,
        lights: Vec<Light>,
        casters: Vec<[[f32; 3]; 3]>,
        sky: Option<Sky>,
        settings: Settings,
    }

    impl Bake {
        fn new(surface: Surface, albedo: [f32; 3], emissive: [f32; 3]) -> Bake {
            Bake {
                surfaces: vec![surface],
                materials: vec![Material {
                    albedo: Texture::uniform([albedo[0], albedo[1], albedo[2], 1.0]),
                    emissive: Texture::uniform([emissive[0], emissive[1], emissive[2], 1.0]),
                }],
                lights: Vec::new(),
                casters: Vec::new(),
                sky: None,
                settings: Settings {
                    samples: 16,
                    max_bounces: 1,
                    min_bounces: 1,
                    pushoff: 0.0001,
                    environment_samples: 0,
                },
            }
        }

        /// The floor's one texel: its light and its validity.
        fn run(&self) -> ([f32; 4], f32) {
            let (light, validity, _) = self.run_under_the_sky();
            (light, validity)
        }

        /// The floor's one texel: its indirect light, its validity and the sky's light on it.
        fn run_under_the_sky(&self) -> ([f32; 4], f32, [f32; 4]) {
            let lit = self.bake();
            (lit.irradiance[0], lit.validity[0], lit.environment[0])
        }

        fn bake(&self) -> indirect::Indirect {
            let mesh = floor();
            let instances = [Instance {
                mesh: &mesh,
                scale_offset: [1.0, 1.0, 0.0, 0.0],
                index: 0,
            }];
            let g = gbuffer::gbuffer(1, 1, 2, &instances);
            let receivers = [Receiver {
                local_to_world: IDENTITY,
                receives_shadows: true,
                viewer: crate::baked::trace::Viewer::Lod0,
            }];
            let scene = Scene::new(self.surfaces.iter().map(|s| s.corners));
            let casters = Scene::new(self.casters.iter().copied());
            let around =
                Bounds::around(self.surfaces.iter().flat_map(|s| s.corners)).unwrap_or(Bounds {
                    min: [0.0; 3],
                    max: [0.0; 3],
                });
            let grid = Grid::new(&self.lights, around);
            let world = World {
                scene: &scene,
                casters: &casters,
                surfaces: &self.surfaces,
                materials: &self.materials,
                lights: &self.lights,
                grid: &grid,
                sky: self.sky.as_ref(),
            };
            let samples = Samples::new(&vec![0; BLUE_NOISE_BYTES]).unwrap();
            indirect::indirect(&g, &receivers, &world, &samples, self.settings)
        }
    }

    #[test]
    fn a_texel_keeps_the_direction_its_light_came_from_and_how_much_it_faces_it() {
        // An even sky over the floor (facing +z). The test's sampling numbers are all alike,
        // so every sample looks the same way: the mean direction is that one, of unit length,
        // above the floor, and the floor faces it by its z.
        let mut open = Bake::new(ceiling(0), [1.0; 3], [0.0; 3]);
        open.surfaces.clear();
        open.sky = Some(even_sky([1.0, 2.0, 3.0], false));
        open.settings.environment_samples = 64;
        let toward = open.bake().directionality[0];
        let mean = [
            toward[0] * 2.0 - 1.0,
            toward[1] * 2.0 - 1.0,
            toward[2] * 2.0 - 1.0,
        ];
        let length = (mean[0] * mean[0] + mean[1] * mean[1] + mean[2] * mean[2]).sqrt();
        assert!((length - 1.0).abs() < 1e-5 && mean[2] > 0.0, "{toward:?}");
        assert!((toward[3] - toward[2]).abs() < 1e-6, "{toward:?}");
        // No light along any path or from the sky: the middle values.
        let dark = Bake::new(ceiling(0), [1.0; 3], [0.0; 3]);
        assert_eq!(dark.bake().directionality[0], [0.5; 4]);
    }

    /// A sky of one colour, with or without directions picked by its brightness.
    fn even_sky(color: [f32; 3], picked: bool) -> Sky {
        let face = || Texture::uniform([color[0], color[1], color[2], 1.0]);
        let faces = [face(), face(), face(), face(), face(), face()];
        let importance = picked.then(|| Importance::of(&faces, 64));
        Sky { faces, importance }
    }

    #[test]
    fn a_texel_under_an_even_sky_takes_its_colour_unless_something_is_in_the_way() {
        for picked in [false, true] {
            let mut open = Bake::new(ceiling(0), [1.0; 3], [0.0; 3]);
            open.surfaces.clear();
            open.sky = Some(even_sky([1.0, 2.0, 3.0], picked));
            open.settings.environment_samples = 64;
            let (light, validity, sky) = open.run_under_the_sky();
            assert_eq!((light, validity), ([0.0, 0.0, 0.0, 1.0], 0.0));
            // Looking around by the cosine an even sky gives its colour whatever the
            // direction. With picked directions a sample's light depends on its direction (the
            // mean over many samples is the colour): here the colour's hue and its order.
            let tolerance = if picked { 1.0 } else { 1e-5 };
            assert!(
                (0..3).all(|k| (sky[k] - (k + 1) as f32).abs() <= tolerance * (k + 1) as f32),
                "{picked} {sky:?}"
            );
            assert!(
                sky[0] > 0.0
                    && (sky[1] - 2.0 * sky[0]).abs() < 1e-4
                    && (sky[2] - 3.0 * sky[0]).abs() < 1e-4
            );
            // No ray is sent without a bounce, to the sky either.
            open.settings.max_bounces = 0;
            assert_eq!(open.run_under_the_sky().2, [0.0, 0.0, 0.0, 1.0]);
            // Whatever is in the way stops the sky's light, also what casts no shadow (the
            // ceiling is not among the casters).
            let mut covered = Bake::new(ceiling(0), [1.0; 3], [0.0; 3]);
            covered.sky = Some(even_sky([1.0, 2.0, 3.0], picked));
            covered.settings.environment_samples = 64;
            assert_eq!(covered.run_under_the_sky().2, [0.0, 0.0, 0.0, 1.0]);
        }
    }

    #[test]
    fn a_surface_sends_back_the_skys_light_while_the_path_may_go_on() {
        // The ceiling's front faces down, where nothing is between it and the sky.
        let mut bake = Bake::new(ceiling(0), [0.5, 0.25, 1.0], [0.0; 3]);
        bake.sky = Some(even_sky([2.0, 2.0, 2.0], false));
        bake.settings.max_bounces = 2;
        let (light, validity) = bake.run();
        assert!(
            near(light, [1.0, 0.5, 2.0]) && validity == 0.0,
            "{light:?} {validity}"
        );
        // At the path's last surface the sky is not asked.
        bake.settings.max_bounces = 1;
        assert_eq!(bake.run(), ([0.0, 0.0, 0.0, 1.0], 0.0));
    }

    fn near(a: [f32; 4], b: [f32; 3]) -> bool {
        (0..3).all(|k| (a[k] - b[k]).abs() <= 1e-5 * b[k].abs().max(1.0)) && a[3] == 1.0
    }

    #[test]
    fn a_texel_takes_what_the_surfaces_its_rays_meet_emit() {
        // Every ray of the floor meets the glowing ceiling's front: the texel has its emission.
        let glow = Bake::new(ceiling(0), [0.0; 3], [3.0, 2.0, 1.0]);
        let (light, validity) = glow.run();
        assert!(near(light, [3.0, 2.0, 1.0]), "{light:?}");
        assert_eq!(validity, 0.0);
        // Nothing above: nothing comes back.
        let mut empty = Bake::new(ceiling(0), [0.0; 3], [3.0, 2.0, 1.0]);
        empty.surfaces[0].corners = [[-1.0, -1.0, -5.0], [-1.0, 1.0, -5.0], [1.0, -1.0, -5.0]];
        assert_eq!(empty.run(), ([0.0, 0.0, 0.0, 1.0], 0.0));
    }

    #[test]
    fn a_surface_met_from_behind_gives_nothing_and_counts_against_the_texel() {
        let mut turned = Bake::new(ceiling(0), [1.0; 3], [3.0, 2.0, 1.0]);
        turned.surfaces[0].corners.swap(1, 2);
        assert_eq!(turned.run(), ([0.0, 0.0, 0.0, 1.0], 1.0));
        // A mirrored renderer's front is the other side.
        turned.surfaces[0].mirrored = true;
        let (light, validity) = turned.run();
        assert!(
            near(light, [3.0, 2.0, 1.0]) && validity == 0.0,
            "{light:?} {validity}"
        );
        // A double sided material's back is a front.
        turned.surfaces[0].mirrored = false;
        turned.surfaces[0].double_sided = true;
        let (light, validity) = turned.run();
        assert!(
            near(light, [3.0, 2.0, 1.0]) && validity == 0.0,
            "{light:?} {validity}"
        );
    }

    #[test]
    fn a_surface_sends_back_the_lights_indirect_colour_by_its_albedo_unless_shadowed() {
        let mut lit = Bake::new(ceiling(0), [0.5, 0.25, 1.0], [0.0; 3]);
        lit.lights.push(sun_from_below());
        // The sun shines straight up at the ceiling: its indirect colour times the albedo.
        let (light, _) = lit.run();
        assert!(near(light, [1.0, 0.25, 0.5]), "{light:?}");
        // Corner normals at 60 degrees to the light halve it (they are not brought back to
        // unit length after mixing, which here they keep).
        lit.surfaces[0].normals = [[0.0, 0.866_025_4, -0.5]; 3];
        let (light, _) = lit.run();
        assert!(near(light, [0.5, 0.125, 0.25]), "{light:?}");
        lit.surfaces[0].normals = [[0.0, 0.0, -0.5]; 3];
        let (light, _) = lit.run();
        assert!(near(light, [0.5, 0.125, 0.25]), "{light:?}");
        // A shadow caster between the sun and the ceiling: nothing; a light that casts no
        // shadow passes it.
        lit.surfaces[0].normals = [[0.0, 0.0, -1.0]; 3];
        lit.casters = vec![[
            [-900.0, -900.0, 1.0],
            [-900.0, 2000.0, 1.0],
            [2000.0, -900.0, 1.0],
        ]];
        assert_eq!(lit.run().0, [0.0, 0.0, 0.0, 1.0]);
        lit.lights[0].casts_shadows = false;
        assert!(near(lit.run().0, [1.0, 0.25, 0.5]));
        // Without a bounce nothing is sent out, and nothing counts against the texel.
        lit.settings.max_bounces = 0;
        lit.surfaces[0].corners.swap(1, 2);
        assert_eq!(lit.run(), ([0.0, 0.0, 0.0, 1.0], 0.0));
    }

    #[test]
    fn a_path_goes_on_to_further_surfaces_until_max_bounces_or_chance_ends_it() {
        // A dark ceiling over a glowing floor-level sheet: a path's second surface is the
        // sheet below (a second surface, met from above).
        let sheet = Surface {
            material: 1,
            mirrored: false,
            double_sided: false,
            corners: [
                [-5000.0, -5000.0, -1.0],
                [10000.0, -5000.0, -1.0],
                [-5000.0, 10000.0, -1.0],
            ],
            normals: [[0.0, 0.0, 1.0]; 3],
            uvs: [[0.0; 2]; 3],
        };
        let mut two = Bake::new(ceiling(0), [0.5, 0.5, 0.5], [0.0; 3]);
        two.surfaces.push(sheet);
        two.materials.push(Material {
            albedo: Texture::uniform([0.0, 0.0, 0.0, 1.0]),
            emissive: Texture::uniform([4.0, 2.0, 1.0, 1.0]),
        });
        // One bounce: the ceiling alone, which gives nothing.
        assert_eq!(two.run().0, [0.0, 0.0, 0.0, 1.0]);
        // Two bounces, chance not asked before the third: the sheet's glow by the ceiling's albedo.
        two.settings.max_bounces = 2;
        two.settings.min_bounces = 2;
        let (light, _) = two.run();
        assert!(near(light, [2.0, 1.0, 0.5]), "{light:?}");
        // Chance from the first surface on: the test's sequence numbers are all 0.5 / 256,
        // below the ceiling's albedo, so every path goes on, weighted by 1 / 0.5.
        two.settings.min_bounces = 1;
        let (light, _) = two.run();
        assert!(near(light, [4.0, 2.0, 1.0]), "{light:?}");
        // An albedo below the number ends every path at the ceiling.
        two.materials[0].albedo = Texture::uniform([0.001, 0.001, 0.001, 1.0]);
        assert_eq!(two.run().0, [0.0, 0.0, 0.0, 1.0]);
    }

    #[test]
    fn a_material_image_is_read_between_its_texels_centres() {
        let (red, green, blue) = (
            [1.0, 0.0, 0.0, 1.0],
            [0.0, 1.0, 0.0, 1.0],
            [0.0, 0.0, 1.0, 1.0],
        );
        let image = Texture {
            width: 2,
            height: 2,
            texels: vec![red, green, blue, blue],
        };
        // At a texel's centre: the texel. Rows run from v = 0.
        assert_eq!(image.at([0.25, 0.25]), red);
        assert_eq!(image.at([0.75, 0.25]), green);
        assert_eq!(image.at([0.25, 0.75]), blue);
        // Between two centres: their mix; between the four: all of them.
        assert_eq!(image.at([0.5, 0.25]), [0.5, 0.5, 0.0, 1.0]);
        assert_eq!(image.at([0.375, 0.25]), [0.75, 0.25, 0.0, 1.0]);
        assert_eq!(image.at([0.5, 0.5]), [0.25, 0.25, 0.5, 1.0]);
        // Beyond the outermost centres, and outside the image: the edge's texels.
        assert_eq!(image.at([0.0, 0.0]), red);
        assert_eq!(image.at([1.0, 0.1]), green);
        assert_eq!(image.at([-3.0, 0.25]), red);
        assert_eq!(image.at([7.0, 0.25]), green);
        assert_eq!(image.at([0.25, 9.0]), blue);
        assert_eq!(Texture::uniform(green).at([0.3, 0.9]), green);
        assert_eq!(
            Texture {
                width: 0,
                height: 0,
                texels: Vec::new()
            }
            .at([0.5, 0.5]),
            [0.0; 4]
        );
    }

    #[test]
    fn a_material_is_read_at_the_lightmap_coordinates_of_the_point() {
        // The ceiling's emission image has two columns; the floor's rays (all one direction
        // with the test's sequence) meet it where its first coordinate is small.
        let mut glow = Bake::new(ceiling(0), [0.0; 3], [0.0; 3]);
        glow.materials[0].emissive = Texture {
            width: 2,
            height: 1,
            texels: vec![[1.0, 0.0, 0.0, 1.0], [0.0, 1.0, 0.0, 1.0]],
        };
        let (left, _) = glow.run();
        assert!(left[0] > 0.8 && left[1] < 0.2 && left[2] == 0.0, "{left:?}");
        // The same corners with the coordinate's ends swapped: the other column.
        glow.surfaces[0].uvs = [[1.0, 0.0], [1.0, 1.0], [0.0, 0.0]];
        let (right, _) = glow.run();
        assert!(
            right[1] > 0.8 && right[0] < 0.2 && right[2] == 0.0,
            "{right:?}"
        );
        assert!((left[0] - right[1]).abs() < 1e-5 && (left[1] - right[0]).abs() < 1e-5);
    }

    #[test]
    fn lod_levels_filter_occlusion_and_bounces_per_group() {
        use crate::baked::trace::{Lod, Viewer};

        let tri = |z: f32| [[-1.0, -1.0, z], [3.0, -1.0, z], [-1.0, 3.0, z]];
        let up = Ray {
            origin: [0.0; 3],
            direction: [0.0, 0.0, 1.0],
        };
        let a_lod0 = Lod {
            group: 0,
            mask: 0b01,
        };
        let a_lod1 = Lod {
            group: 0,
            mask: 0b10,
        };
        let a_shared = Lod {
            group: 0,
            mask: 0b11,
        };
        let a_lod2 = Lod {
            group: 0,
            mask: 0b100,
        };
        let b_lod0 = Lod {
            group: 1,
            mask: 0b01,
        };
        let b_lod1 = Lod {
            group: 1,
            mask: 0b10,
        };
        let non_lod = Lod::NONE;

        let viewer_a1 = Viewer::from_lod(a_lod1);
        let viewer_a2 = Viewer::from_lod(a_lod2);

        // LOD 0 / non-LOD rays see only non-LOD and LOD 0 (and shared LOD 0+1), never lower LODs.
        let lower_only = Scene::with_lods([(tri(1.0), a_lod1), (tri(2.0), b_lod1)]);
        assert!(!lower_only.occluded_for(&up, 10.0, Viewer::Lod0));
        assert_eq!(lower_only.closest_for(&up, 10.0, Viewer::Lod0), None);
        // Same-group LOD 1 sees its own LOD 1 within limit, not past limit, and never group B's LOD 1.
        assert!(lower_only.occluded_for(&up, 10.0, viewer_a1));
        assert!(!lower_only.occluded_for(&up, 0.5, viewer_a1));
        assert!(!lower_only.occluded_for(&up, 10.0, viewer_a2));
        assert!(!lower_only.occluded_for(&up, 1.5, Viewer::from_lod(b_lod1)));
        assert!(lower_only.occluded_for(&up, 3.0, Viewer::from_lod(b_lod1)));

        // Lower LOD skips its own group's LOD 0-only, sees other groups' LOD 0 and non-LOD,
        // and switches to Viewer::Lod0 once a bounce hits a non-LOD or other-group LOD 0 surface.
        let own_lod0_then_other =
            Scene::with_lods([(tri(1.0), a_lod0), (tri(2.0), b_lod0), (tri(3.0), non_lod)]);
        let (hit, next) = own_lod0_then_other
            .closest_for(&up, 10.0, viewer_a1)
            .unwrap();
        assert_eq!((hit.triangle, hit.distance, next), (1, 2.0, Viewer::Lod0));
        // Own-group LOD 0 alone does not occlude A's LOD 1.
        let own_lod0_only = Scene::with_lods([(tri(1.0), a_lod0)]);
        assert!(!own_lod0_only.occluded_for(&up, 10.0, viewer_a1));
        assert!(own_lod0_only.occluded_for(&up, 10.0, Viewer::Lod0));

        // Same-group LOD 1 hit takes precedence over a closer non-LOD triangle (measured in
        // bakedbuffers/Lod) and keeps viewer_a1 on the bounce.
        let self_behind_wall = Scene::with_lods([(tri(1.0), non_lod), (tri(2.0), a_lod1)]);
        let (hit, next) = self_behind_wall.closest_for(&up, 10.0, viewer_a1).unwrap();
        assert_eq!((hit.triangle, hit.distance, next), (1, 2.0, viewer_a1));
        // When limit excludes the LOD 1 triangle, Step A misses and Step B hits the closer non-LOD
        // triangle, switching to Lod0.
        let (hit, next) = self_behind_wall.closest_for(&up, 1.5, viewer_a1).unwrap();
        assert_eq!((hit.triangle, hit.distance, next), (0, 1.0, Viewer::Lod0));

        // Shared LOD 0+1 renderer of group A is seen by both Lod0 and A's LOD 1 (not A's LOD 2),
        // and hitting it transitions A's LOD 1 to LodShared (skips Step A on next bounce, still
        // sees A's LOD 1 for shadow/environment occlusion).
        let with_shared = Scene::with_lods([(tri(1.0), a_shared), (tri(2.0), a_lod1)]);
        assert!(with_shared.occluded_for(&up, 10.0, Viewer::Lod0));
        assert!(!with_shared.occluded_for(&up, 10.0, viewer_a2));
        let (hit, next) = with_shared.closest_for(&up, 1.5, viewer_a1).unwrap();
        assert_eq!(
            (hit.triangle, next),
            (
                0,
                Viewer::LodShared {
                    group: 0,
                    level_bit: 0b10
                }
            )
        );
        // From LodShared, occlusion still sees A's LOD 1 at z = 2, while closest skips Step A.
        let past_shared = Ray {
            origin: [0.0, 0.0, 1.5],
            direction: [0.0, 0.0, 1.0],
        };
        assert!(with_shared.occluded_for(&past_shared, 10.0, next));
        assert_eq!(with_shared.closest_for(&past_shared, 10.0, next), None);

        // Non-LOD triangles remain visible even if a direct caller passes group == u32::MAX.
        let max_group_viewer = Viewer::from_lod(Lod {
            group: u32::MAX,
            mask: 0b10,
        });
        let non_lod_only = Scene::with_lods([(tri(1.0), non_lod)]);
        assert!(non_lod_only.occluded_for(&up, 10.0, max_group_viewer));
        assert_eq!(
            non_lod_only
                .closest_for(&up, 10.0, max_group_viewer)
                .map(|(h, v)| (h.triangle, v)),
            Some((0, Viewer::Lod0))
        );

        // When a lower-LOD bounce misses its own group and level, closest_from traces the base
        // pass from base_origin and returns base_origin as the hit's origin.
        let base_only = Scene::with_lods([(tri(2.0), non_lod)]);
        let bounced = Ray {
            origin: [0.0, 0.0, 5.0],
            direction: [0.0, 0.0, 1.0],
        };
        let (hit, next, used_origin) = base_only
            .closest_from(&bounced, [0.0, 0.0, 0.0], 10.0, viewer_a1)
            .unwrap();
        assert_eq!(
            (hit.triangle, hit.distance, next, used_origin),
            (0, 2.0, Viewer::Lod0, [0.0; 3])
        );
        // When it hits its own group and level, ray.origin is kept.
        let own_only = Scene::with_lods([(tri(6.0), a_lod1)]);
        let (hit, next, used_origin) = own_only
            .closest_from(&bounced, [0.0, 0.0, 0.0], 10.0, viewer_a1)
            .unwrap();
        assert_eq!(
            (hit.triangle, hit.distance, next, used_origin),
            (0, 1.0, viewer_a1, [0.0, 0.0, 5.0])
        );
    }

    #[test]
    fn lower_lod_direct_shadow_and_sky_non_caster_rules() {
        use crate::baked::trace::{Lod, Viewer};

        let mesh = floor();
        let instances = [Instance {
            mesh: &mesh,
            scale_offset: [1.0, 1.0, 0.0, 0.0],
            index: 0,
        }];
        let g = gbuffer::gbuffer(1, 1, 2, &instances);
        let a_lod0 = Lod {
            group: 0,
            mask: 0b01,
        };
        let a_lod1 = Lod {
            group: 0,
            mask: 0b10,
        };
        let viewer_a1 = Viewer::from_lod(a_lod1);

        // Direct shadow: a ceiling belonging to A's LOD 0 shadows a Lod0 receiver, not A's LOD 1;
        // a ceiling belonging to A's LOD 1 shadows A's LOD 1, not a Lod0 receiver.
        let roof = ceiling(0).corners;
        let lod0_roof = Scene::with_lods([(roof, a_lod0)]);
        let lod1_roof = Scene::with_lods([(roof, a_lod1)]);
        let mut sun = sun_from_below();
        sun.forward = [0.0, 0.0, -1.0];
        let direct_r = |viewer: Viewer, casters: &Scene| {
            let receivers = [Receiver {
                local_to_world: IDENTITY,
                receives_shadows: true,
                viewer,
            }];
            crate::baked::direct::direct(
                &g,
                &receivers,
                &[sun],
                &[],
                crate::baked::direct::MixedMode::IndirectOnly,
                casters,
                0.0001,
            )
            .irradiance[0][0]
        };
        assert_eq!(direct_r(Viewer::Lod0, &lod0_roof), 0.0);
        assert!(direct_r(viewer_a1, &lod0_roof) > 8.0);
        assert!(direct_r(Viewer::Lod0, &lod1_roof) > 8.0);
        assert_eq!(direct_r(viewer_a1, &lod1_roof), 0.0);

        // Sky occlusion: a non-shadow-casting non-LOD roof blocks sky rays for Lod0 receivers,
        // but lower-LOD receivers test shadow casters only and still see the sky through it.
        let mut bake = Bake::new(ceiling(0), [0.0; 3], [0.0; 3]);
        bake.sky = Some(even_sky([1.0, 1.0, 1.0], false));
        bake.settings.environment_samples = 16;
        let scene = Scene::with_lods([(bake.surfaces[0].corners, Lod::NONE)]);
        let casters = Scene::with_lods([]);
        let around = Bounds::around(bake.surfaces[0].corners).unwrap();
        let grid = Grid::new(&bake.lights, around);
        let world = World {
            scene: &scene,
            casters: &casters,
            surfaces: &bake.surfaces,
            materials: &bake.materials,
            lights: &bake.lights,
            grid: &grid,
            sky: bake.sky.as_ref(),
        };
        let samples = Samples::new(&vec![0; BLUE_NOISE_BYTES]).unwrap();
        let sky_of = |viewer: Viewer| {
            let receivers = [Receiver {
                local_to_world: IDENTITY,
                receives_shadows: true,
                viewer,
            }];
            indirect::indirect(&g, &receivers, &world, &samples, bake.settings).environment[0][0]
        };
        assert_eq!(sky_of(Viewer::Lod0), 0.0);
        assert!((sky_of(viewer_a1) - 1.0).abs() < 1e-5);
    }

    #[test]
    fn inclusive_probe_rays_hit_coplanar_triangles_at_t_zero() {
        use crate::baked::light::Kind;
        use crate::baked::probes;
        use crate::baked::trace::{Lod, Viewer};

        let tri = [[-5.0, 0.0, -5.0], [-5.0, 0.0, 5.0], [5.0, 0.0, 5.0]];
        let scene = Scene::with_lods([(tri, Lod::NONE)]);
        let ray_up = Ray {
            origin: [-1.0, 0.0, 1.0],
            direction: [0.0, 1.0, 0.0],
        };
        let ray_down = Ray {
            origin: [-1.0, 0.0, 1.0],
            direction: [0.0, -1.0, 0.0],
        };
        // Exclusive tracing (used by lightmap texels and depth > 0 bounces) misses t == 0.
        assert!(!scene.occluded(&ray_up, f32::INFINITY));
        assert!(
            scene
                .closest_from(&ray_up, ray_up.origin, f32::INFINITY, Viewer::Lod0)
                .is_none()
        );
        // Inclusive tracing (used by probe origin rays) hits at t == 0 with front/back orientation.
        assert!(scene.occluded_inclusive(&ray_up, f32::INFINITY));
        let (hit_up, _, _) = scene
            .closest_from_inclusive(&ray_up, ray_up.origin, f32::INFINITY, Viewer::Lod0)
            .expect("upward coplanar ray hits at t == 0");
        assert_eq!(hit_up.distance, 0.0);
        assert!(!hit_up.front, "upward ray exits +Y plane from behind");
        let (hit_down, _, _) = scene
            .closest_from_inclusive(&ray_down, ray_down.origin, f32::INFINITY, Viewer::Lod0)
            .expect("downward coplanar ray hits at t == 0");
        assert_eq!(hit_down.distance, 0.0);
        assert!(hit_down.front, "downward ray enters +Y plane from front");

        // Probe sitting on a single-sided floor at y == 0: sun shadow ray hits at t == 0 and
        // half the sphere's validity rays exit through the floor's backface (validity == 0.5).
        let floor_tris = [
            [[-5.0, 0.0, -5.0], [-5.0, 0.0, 5.0], [5.0, 0.0, 5.0]],
            [[-5.0, 0.0, -5.0], [5.0, 0.0, 5.0], [5.0, 0.0, -5.0]],
        ];
        let floor_scene = Scene::new(floor_tris);
        let sun = Light {
            kind: Kind::Directional,
            mode: Mode::Baked,
            color: [2.0, 2.0, 2.0],
            indirect_color: [2.0, 2.0, 2.0],
            position: [0.0; 3],
            forward: [0.0, -1.0, 0.0],
            right: [1.0, 0.0, 0.0],
            range: 0.0,
            cone_angle: 0.0,
            casts_shadows: true,
        };
        let grid = Grid::new(
            &[sun],
            Bounds {
                min: [-5.0, -1.0, -5.0],
                max: [5.0, 1.0, 5.0],
            },
        );
        let coplanar_direct = probes::direct(&[[0.0, 0.0, 0.0]], &[sun], &grid, &floor_scene);
        assert_eq!(
            coplanar_direct[0],
            probes::Coefficients::default(),
            "probe coplanar with floor is shadowed at t == 0"
        );
        let surfaces: Vec<Surface> = floor_tris
            .iter()
            .map(|&corners| Surface {
                material: 0,
                mirrored: false,
                double_sided: false,
                corners,
                normals: [[0.0, 1.0, 0.0]; 3],
                uvs: [[0.0, 0.0]; 3],
            })
            .collect();
        let materials = [Material {
            albedo: Texture::uniform([0.5, 0.5, 0.5, 1.0]),
            emissive: Texture::uniform([0.0, 0.0, 0.0, 1.0]),
        }];
        let world = World {
            scene: &floor_scene,
            casters: &floor_scene,
            surfaces: &surfaces,
            materials: &materials,
            lights: &[sun],
            grid: &grid,
            sky: None,
        };
        let samples = Samples::new(&vec![0; BLUE_NOISE_BYTES]).unwrap();
        let validity = indirect::probe_validity(&[[0.0, 0.0, 0.0]], &world, &samples, 256);
        assert!(
            (validity[0] - 0.5).abs() < 0.05,
            "coplanar probe validity should be ~0.5, got {}",
            validity[0]
        );
    }
}
