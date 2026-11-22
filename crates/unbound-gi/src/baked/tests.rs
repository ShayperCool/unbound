use super::atlas::*;

fn instance(rect: [f32; 2], morton: u32) -> Instance {
    Instance {
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
