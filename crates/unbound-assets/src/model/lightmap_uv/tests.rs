//! The unwrap against Unity 6000.0.71f1's UV2 for meshes of tests/projects/lightmapuvs (the
//! Default variant: hard angle 88, distortions 8% and 15%, margin calculated at resolution 40):
//! the unwrap input as the importer builds it, and the Editor's UV2 at each triangle corner,
//! taken from the probe's reference by the corners' positions and UVs.

use super::*;

// The probe's Cube: six quads, a chart each.
const CUBE_POSITIONS: [[f32; 3]; 8] = [
    [
        0.009999999776482582,
        -0.009999999776482582,
        -0.009999999776482582,
    ],
    [
        0.009999999776482582,
        -0.009999999776482582,
        0.009999999776482582,
    ],
    [
        0.009999999776482582,
        0.009999999776482582,
        -0.009999999776482582,
    ],
    [
        0.009999999776482582,
        0.009999999776482582,
        0.009999999776482582,
    ],
    [
        -0.009999999776482582,
        -0.009999999776482582,
        -0.009999999776482582,
    ],
    [
        -0.009999999776482582,
        -0.009999999776482582,
        0.009999999776482582,
    ],
    [
        -0.009999999776482582,
        0.009999999776482582,
        -0.009999999776482582,
    ],
    [
        -0.009999999776482582,
        0.009999999776482582,
        0.009999999776482582,
    ],
];
const CUBE_TRIANGLES: [[u32; 3]; 12] = [
    [0, 2, 3],
    [0, 3, 1],
    [2, 6, 7],
    [2, 7, 3],
    [6, 4, 5],
    [6, 5, 7],
    [4, 0, 1],
    [4, 1, 5],
    [2, 0, 4],
    [2, 4, 6],
    [7, 5, 1],
    [7, 1, 3],
];
const CUBE_UVS: [[[f32; 2]; 3]; 12] = [
    [[0.375, 0.0], [0.375, 0.25], [0.625, 0.25]],
    [[0.375, 0.0], [0.625, 0.25], [0.625, 0.0]],
    [[0.375, 0.25], [0.375, 0.5], [0.625, 0.5]],
    [[0.375, 0.25], [0.625, 0.5], [0.625, 0.25]],
    [[0.375, 0.5], [0.375, 0.75], [0.625, 0.75]],
    [[0.375, 0.5], [0.625, 0.75], [0.625, 0.5]],
    [[0.375, 0.75], [0.375, 1.0], [0.625, 1.0]],
    [[0.375, 0.75], [0.625, 1.0], [0.625, 0.75]],
    [[0.125, 0.5], [0.125, 0.75], [0.375, 0.75]],
    [[0.125, 0.5], [0.375, 0.75], [0.375, 0.5]],
    [[0.625, 0.5], [0.625, 0.75], [0.875, 0.75]],
    [[0.625, 0.5], [0.875, 0.75], [0.875, 0.5]],
];
const CUBE_EXPECTED: [[[f32; 2]; 3]; 12] = [
    [
        [0.29187414050102234, 0.19136640429496765],
        [0.2918742299079895, 0.2918739914894104],
        [0.19136647880077362, 0.2918740510940552],
    ],
    [
        [0.29187414050102234, 0.19136640429496765],
        [0.19136647880077362, 0.2918740510940552],
        [0.19136640429496765, 0.19136649370193481],
    ],
    [
        [0.7751147747039795, 0.2918739914894104],
        [0.7751147747039795, 0.19136640429496765],
        [0.8756225109100342, 0.19136640429496765],
    ],
    [
        [0.7751147747039795, 0.2918739914894104],
        [0.8756225109100342, 0.19136640429496765],
        [0.8756225109100342, 0.2918739914894104],
    ],
    [
        [0.48324063420295715, 0.2918739914894104],
        [0.48324063420295715, 0.19136640429496765],
        [0.5837483406066895, 0.19136640429496765],
    ],
    [
        [0.48324063420295715, 0.2918739914894104],
        [0.5837483406066895, 0.19136640429496765],
        [0.5837483406066895, 0.2918739914894104],
    ],
    [
        [0.29187414050102234, 0.4832404553890228],
        [0.29187414050102234, 0.5837480425834656],
        [0.19136640429496765, 0.5837480425834656],
    ],
    [
        [0.29187414050102234, 0.4832404553890228],
        [0.19136640429496765, 0.5837480425834656],
        [0.19136640429496765, 0.4832404553890228],
    ],
    [
        [0.2918739914894104, 0.8756221532821655],
        [0.19136640429496765, 0.8756221532821655],
        [0.19136640429496765, 0.7751144170761108],
    ],
    [
        [0.2918739914894104, 0.8756221532821655],
        [0.19136640429496765, 0.7751144170761108],
        [0.2918739914894104, 0.7751144170761108],
    ],
    [
        [0.48324054479599, 0.4832404553890228],
        [0.5837481021881104, 0.4832404553890228],
        [0.5837481021881104, 0.5837482213973999],
    ],
    [
        [0.48324054479599, 0.4832404553890228],
        [0.5837481021881104, 0.5837482213973999],
        [0.48324054479599, 0.5837482213973999],
    ],
];

// The probe's TwoUV plane: a 3×3 grid of quads, one chart with interior vertices.
const GRID_POSITIONS: [[f32; 3]; 16] = [
    [0.009999999776482582, -0.009999999776482582, 0.0],
    [-0.009999999776482582, -0.009999999776482582, 0.0],
    [0.009999999776482582, 0.009999999776482582, 0.0],
    [-0.009999999776482582, 0.009999999776482582, 0.0],
    [0.009999999776482582, 0.003333332948386669, 0.0],
    [0.009999999776482582, -0.003333332948386669, 0.0],
    [0.003333332948386669, -0.009999999776482582, 0.0],
    [-0.003333332948386669, -0.009999999776482582, 0.0],
    [-0.009999999776482582, -0.003333332948386669, 0.0],
    [-0.009999999776482582, 0.003333332948386669, 0.0],
    [-0.003333332948386669, 0.009999999776482582, 0.0],
    [0.003333332948386669, 0.009999999776482582, 0.0],
    [0.003333332948386669, -0.003333332948386669, 0.0],
    [0.003333332948386669, 0.003333332948386669, 0.0],
    [-0.003333332948386669, -0.003333332948386669, 0.0],
    [-0.003333332948386669, 0.003333332948386669, 0.0],
];
const GRID_TRIANGLES: [[u32; 3]; 18] = [
    [15, 10, 3],
    [15, 3, 9],
    [4, 2, 11],
    [4, 11, 13],
    [13, 11, 10],
    [13, 10, 15],
    [0, 5, 12],
    [0, 12, 6],
    [5, 4, 13],
    [5, 13, 12],
    [6, 12, 14],
    [6, 14, 7],
    [12, 13, 15],
    [12, 15, 14],
    [7, 14, 8],
    [7, 8, 1],
    [14, 15, 9],
    [14, 9, 8],
];
const GRID_UVS: [[[f32; 2]; 3]; 18] = [
    [
        [0.6666666269302368, 0.6666666865348816],
        [0.6666666269302368, 1.0],
        [1.0, 1.0],
    ],
    [
        [0.6666666269302368, 0.6666666865348816],
        [1.0, 1.0],
        [1.0, 0.6666666865348816],
    ],
    [
        [0.0, 0.6666666269302368],
        [0.0, 1.0],
        [0.3333333134651184, 1.0],
    ],
    [
        [0.0, 0.6666666269302368],
        [0.3333333134651184, 1.0],
        [0.3333333134651184, 0.6666666865348816],
    ],
    [
        [0.3333333134651184, 0.6666666865348816],
        [0.3333333134651184, 1.0],
        [0.6666666269302368, 1.0],
    ],
    [
        [0.3333333134651184, 0.6666666865348816],
        [0.6666666269302368, 1.0],
        [0.6666666269302368, 0.6666666865348816],
    ],
    [
        [0.0, 0.0],
        [0.0, 0.3333333134651184],
        [0.3333333134651184, 0.3333333432674408],
    ],
    [
        [0.0, 0.0],
        [0.3333333134651184, 0.3333333432674408],
        [0.3333333432674408, 0.0],
    ],
    [
        [0.0, 0.3333333134651184],
        [0.0, 0.6666666269302368],
        [0.3333333134651184, 0.6666666865348816],
    ],
    [
        [0.0, 0.3333333134651184],
        [0.3333333134651184, 0.6666666865348816],
        [0.3333333134651184, 0.3333333432674408],
    ],
    [
        [0.3333333432674408, 0.0],
        [0.3333333134651184, 0.3333333432674408],
        [0.6666666269302368, 0.3333333432674408],
    ],
    [
        [0.3333333432674408, 0.0],
        [0.6666666269302368, 0.3333333432674408],
        [0.6666666865348816, 0.0],
    ],
    [
        [0.3333333134651184, 0.3333333432674408],
        [0.3333333134651184, 0.6666666865348816],
        [0.6666666269302368, 0.6666666865348816],
    ],
    [
        [0.3333333134651184, 0.3333333432674408],
        [0.6666666269302368, 0.6666666865348816],
        [0.6666666269302368, 0.3333333432674408],
    ],
    [
        [0.6666666865348816, 0.0],
        [0.6666666269302368, 0.3333333432674408],
        [1.0, 0.3333333432674408],
    ],
    [
        [0.6666666865348816, 0.0],
        [1.0, 0.3333333432674408],
        [1.0, 0.0],
    ],
    [
        [0.6666666269302368, 0.3333333432674408],
        [0.6666666269302368, 0.6666666865348816],
        [1.0, 0.6666666865348816],
    ],
    [
        [0.6666666269302368, 0.3333333432674408],
        [1.0, 0.6666666865348816],
        [1.0, 0.3333333432674408],
    ],
];
const GRID_EXPECTED: [[[f32; 2]; 3]; 18] = [
    [
        [0.507965624332428, 0.5079656839370728],
        [0.4687499701976776, 0.5079657435417175],
        [0.4687499403953552, 0.46875],
    ],
    [
        [0.507965624332428, 0.5079656839370728],
        [0.4687499403953552, 0.46875],
        [0.507965624332428, 0.4687499701976776],
    ],
    [
        [0.5079656839370728, 0.586397111415863],
        [0.46875, 0.586397111415863],
        [0.4687499701976776, 0.5471813678741455],
    ],
    [
        [0.5079656839370728, 0.586397111415863],
        [0.4687499701976776, 0.5471813678741455],
        [0.507965624332428, 0.5471813678741455],
    ],
    [
        [0.507965624332428, 0.5471813678741455],
        [0.4687499701976776, 0.5471813678741455],
        [0.4687499701976776, 0.5079657435417175],
    ],
    [
        [0.507965624332428, 0.5471813678741455],
        [0.4687499701976776, 0.5079657435417175],
        [0.507965624332428, 0.5079656839370728],
    ],
    [
        [0.5863969922065735, 0.5863970518112183],
        [0.5471813082695007, 0.586397111415863],
        [0.5471813082695007, 0.5471813678741455],
    ],
    [
        [0.5863969922065735, 0.5863970518112183],
        [0.5471813082695007, 0.5471813678741455],
        [0.5863969326019287, 0.5471813678741455],
    ],
    [
        [0.5471813082695007, 0.586397111415863],
        [0.5079656839370728, 0.586397111415863],
        [0.507965624332428, 0.5471813678741455],
    ],
    [
        [0.5471813082695007, 0.586397111415863],
        [0.507965624332428, 0.5471813678741455],
        [0.5471813082695007, 0.5471813678741455],
    ],
    [
        [0.5863969326019287, 0.5471813678741455],
        [0.5471813082695007, 0.5471813678741455],
        [0.547181248664856, 0.5079656839370728],
    ],
    [
        [0.5863969326019287, 0.5471813678741455],
        [0.547181248664856, 0.5079656839370728],
        [0.5863969326019287, 0.5079656839370728],
    ],
    [
        [0.5471813082695007, 0.5471813678741455],
        [0.507965624332428, 0.5471813678741455],
        [0.507965624332428, 0.5079656839370728],
    ],
    [
        [0.5471813082695007, 0.5471813678741455],
        [0.507965624332428, 0.5079656839370728],
        [0.547181248664856, 0.5079656839370728],
    ],
    [
        [0.5863969326019287, 0.5079656839370728],
        [0.547181248664856, 0.5079656839370728],
        [0.547181248664856, 0.4687499701976776],
    ],
    [
        [0.5863969326019287, 0.5079656839370728],
        [0.547181248664856, 0.4687499701976776],
        [0.5863969326019287, 0.4687499403953552],
    ],
    [
        [0.547181248664856, 0.5079656839370728],
        [0.507965624332428, 0.5079656839370728],
        [0.507965624332428, 0.4687499701976776],
    ],
    [
        [0.547181248664856, 0.5079656839370728],
        [0.507965624332428, 0.4687499701976776],
        [0.547181248664856, 0.4687499701976776],
    ],
];

fn default_settings() -> SecondaryUvSettings {
    SecondaryUvSettings {
        hard_angle: 88.0,
        angle_distortion: 8.0,
        area_distortion: 15.000001,
        margin_method: 1,
        pack_margin: 4.0,
        min_lightmap_resolution: 40.0,
        min_object_scale: 1.0,
    }
}

fn assert_unwraps(
    positions: &[[f32; 3]],
    triangles: &[[u32; 3]],
    uvs: &[[[f32; 2]; 3]],
    expected: &[[[f32; 2]; 3]],
) {
    let polygons: Vec<u32> = (0..triangles.len() as u32).collect();
    let input = UnwrapInput {
        positions,
        triangles,
        polygons: &polygons,
        uvs: Some(uvs),
    };
    let out = generate(&input, &default_settings()).unwrap();
    for (t, (got, want)) in out.iter().zip(expected).enumerate() {
        for k in 0..3 {
            assert_eq!(
                got[k].map(f32::to_bits),
                want[k].map(f32::to_bits),
                "triangle {t} corner {k}: {:?} != {:?}",
                got[k],
                want[k]
            );
        }
    }
}

#[test]
fn unwraps_the_cube_like_unity() {
    assert_unwraps(&CUBE_POSITIONS, &CUBE_TRIANGLES, &CUBE_UVS, &CUBE_EXPECTED);
}

#[test]
fn unwraps_a_grid_with_interior_vertices_like_unity() {
    assert_unwraps(&GRID_POSITIONS, &GRID_TRIANGLES, &GRID_UVS, &GRID_EXPECTED);
}

fn unwrap_default(
    positions: &[[f32; 3]],
    triangles: &[[u32; 3]],
) -> Result<Vec<[[f32; 2]; 3]>, Failure> {
    let polygons: Vec<u32> = (0..triangles.len() as u32).collect();
    let input = UnwrapInput {
        positions,
        triangles,
        polygons: &polygons,
        uvs: None,
    };
    generate(&input, &default_settings())
}

#[test]
fn a_degenerate_triangle_takes_the_uv2_of_a_neighbour_along_a_shared_edge() {
    // Triangles 0, 2 and 3 have their three corners on one line; triangle 3 shares no edge with
    // a kept triangle. The third corner takes the UV2 of the shared edge's nearer end: vertex 2
    // lies nearer vertex 1, vertex 4 nearer vertex 0.
    let positions = [
        [0.0, 0.0, 0.0],
        [1.0, 0.0, 0.0],
        [3.0, 0.0, 0.0],
        [0.0, 1.0, 0.0],
        [0.2, 0.0, 0.0],
        [5.0, 5.0, 0.0],
        [6.0, 5.0, 0.0],
        [7.0, 5.0, 0.0],
    ];
    let triangles = [[0, 1, 2], [0, 1, 3], [0, 1, 4], [5, 6, 7]];
    let out = unwrap_default(&positions, &triangles).unwrap();
    let kept = out[1];
    assert_eq!(out[0], [kept[0], kept[1], kept[1]]);
    assert_eq!(out[2], [kept[0], kept[1], kept[0]]);
    assert_eq!(out[3], [[0.0; 2]; 3]);
    assert_ne!(kept[0], kept[1]);
}

#[test]
fn a_mesh_of_degenerate_triangles_only_has_nothing_left_to_unwrap() {
    let positions = [[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [2.0, 0.0, 0.0]];
    assert_eq!(
        unwrap_default(&positions, &[[0, 1, 2], [0, 0, 1]]),
        Err(Failure::NothingLeft)
    );
}

#[test]
fn the_cleanup_moves_removed_triangles_after_the_kept_ones() {
    // Walking from the last triangle, a removed one is replaced by the last kept one.
    let positions = [
        [0.0, 0.0, 0.0],
        [1.0, 0.0, 0.0],
        [0.0, 1.0, 0.0],
        [1.0, 1.0, 0.0],
        [2.0, 0.0, 0.0],
    ];
    let triangles = [[0, 1, 2], [0, 1, 4], [1, 3, 2], [2, 2, 3]];
    let g = cleanup::prepare(&positions, &triangles).unwrap();
    assert_eq!(g.sources, [0, 2]);
    assert_eq!(g.removed, [1, 3]);
    assert_eq!(g.triangles, [[0, 1, 2], [1, 3, 2]]);
    // Vertex 4 is no longer used and is dropped.
    assert_eq!(g.positions.len(), 4);
}

#[test]
fn a_position_that_is_not_a_number_keeps_the_mesh_instead_of_emptying_it() {
    let mut positions = vec![
        [0.0, 0.0, 0.0],
        [1.0, 0.0, 0.0],
        [0.0, 1.0, 0.0],
        [1.0, 1.0, 0.0],
    ];
    let triangles = [[0, 1, 2], [1, 3, 2]];
    assert!(unwrap_default(&positions, &triangles).is_ok());
    for bad in [f32::NAN, f32::INFINITY] {
        positions[3][2] = bad;
        assert!(matches!(
            unwrap_default(&positions, &triangles),
            Err(Failure::Unsupported(_))
        ));
    }
    // No triangles at all: nothing to unwrap and nothing to report.
    assert_eq!(unwrap_default(&positions, &[]), Ok(Vec::new()));
}

#[test]
fn a_triangle_with_coincident_corners_is_removed_and_the_rest_unwraps() {
    // Vertices 3 and 4 are distinct but lie at one position: triangle 1 has no area.
    let positions = [
        [0.0, 0.0, 0.0],
        [1.0, 0.0, 0.0],
        [0.0, 1.0, 0.0],
        [1.0, 1.0, 0.0],
        [1.0, 1.0, 0.0],
    ];
    let triangles = [[0, 1, 2], [1, 3, 4], [1, 3, 2]];
    let g = cleanup::prepare(&positions, &triangles).unwrap();
    assert_eq!(g.sources, [0, 2]);
    assert_eq!(g.removed, [1]);
    assert_eq!(g.positions.len(), 4);
    let out = unwrap_default(&positions, &triangles).unwrap();
    // It shares the edge (1, 3) with triangle 2; its third corner lies on vertex 3.
    assert_eq!(out[1], [out[2][0], out[2][1], out[2][1]]);
    assert_ne!(out[2][0], out[2][1]);
}

#[test]
fn a_large_mesh_full_of_degenerate_triangles_is_cleaned_and_filled_from_its_neighbours() {
    // A grid of 200×200 quads, and on every horizontal edge a triangle with a repeated corner.
    const N: u32 = 200;
    let mut positions = Vec::new();
    for y in 0..=N {
        for x in 0..=N {
            positions.push([x as f32, y as f32, ((x * 7 + y * 3) % 5) as f32 * 0.1]);
        }
    }
    let at = |x: u32, y: u32| y * (N + 1) + x;
    let mut triangles = Vec::new();
    for y in 0..=N {
        for x in 0..N {
            if y < N {
                triangles.push([at(x, y), at(x + 1, y), at(x + 1, y + 1)]);
                triangles.push([at(x, y), at(x + 1, y + 1), at(x, y + 1)]);
            }
            triangles.push([at(x, y), at(x + 1, y), at(x, y)]);
        }
    }
    let kept = (2 * N * N) as usize;
    let g = cleanup::prepare(&positions, &triangles).unwrap();
    assert_eq!(g.triangles.len(), kept);
    assert_eq!(g.removed.len(), triangles.len() - kept);
    assert_eq!(g.positions.len(), positions.len());
    assert!(manifold(0, &g.triangles), "edges only");
    let mut all: Vec<u32> = g.sources.iter().chain(&g.removed).copied().collect();
    all.sort_unstable();
    assert!(all.iter().copied().eq(0..triangles.len() as u32));

    // Every removed triangle takes the corners of the first kept triangle (in the cleanup's
    // order) that has its edge; a sample is checked against a plain search.
    let polygons: Vec<u32> = (0..triangles.len() as u32).collect();
    let input = UnwrapInput {
        positions: &positions,
        triangles: &triangles,
        polygons: &polygons,
        uvs: None,
    };
    let out: Vec<[[f32; 2]; 3]> = (0..triangles.len())
        .map(|t| [0, 1, 2].map(|k| [t as f32, k as f32]))
        .collect();
    let edges = KeptEdges::new(&input, &g.sources);
    for &removed in g.removed.iter().step_by(97) {
        let [a, b, _] = triangles[removed as usize];
        let neighbour = *g
            .sources
            .iter()
            .find(|&&s| triangles[s as usize].contains(&a) && triangles[s as usize].contains(&b))
            .unwrap();
        let n = triangles[neighbour as usize];
        let corner = |v: u32| n.iter().position(|&c| c == v).unwrap() as f32;
        let uv = removed_triangle_uvs(&input, &g.sources, &edges, removed, &out);
        // The third corner repeats the first, so it lies on the edge's first end.
        assert_eq!(
            uv,
            [a, b, a].map(|v| [neighbour as f32, corner(v)]),
            "triangle {removed}"
        );
    }
}

#[test]
fn two_closed_solids_sharing_an_edge_are_split_apart_and_unwrap() {
    // Two tetrahedra with the edge (0, 1) in common: four triangles along it.
    let positions = [
        [0.0, 0.0, 0.0],
        [1.0, 0.0, 0.0],
        [0.5, 1.0, 0.3],
        [0.5, 0.4, 1.0],
        [0.5, -1.0, -0.3],
        [0.5, -0.4, -1.0],
    ];
    let solid = |c: u32, d: u32| [[0, 1, c], [0, d, 1], [0, c, d], [1, d, c]];
    let triangles: Vec<[u32; 3]> = solid(2, 3).into_iter().chain(solid(4, 5)).collect();
    assert!(!manifold(6, &triangles));
    let g = cleanup::prepare(&positions, &triangles).unwrap();
    assert!(g.removed.is_empty());
    assert_eq!(g.sources, [0, 1, 2, 3, 4, 5, 6, 7]);
    // Each end of the shared edge gets one copy: the solids no longer touch.
    assert_eq!(g.positions.len(), 8);
    assert!(manifold(g.positions.len(), &g.triangles));
    let shared = |t: &[u32; 3], u: &[u32; 3]| t.iter().any(|v| u.contains(v));
    for t in &g.triangles[..4] {
        assert!(g.triangles[4..].iter().all(|u| !shared(t, u)));
    }
    let out = unwrap_default(&positions, &triangles).unwrap();
    for (i, uv) in out.iter().enumerate() {
        assert!(
            uv.iter()
                .flatten()
                .all(|c| c.is_finite() && (0.0..=1.0).contains(c)),
            "triangle {i}: {uv:?}"
        );
        let area = (uv[1][0] - uv[0][0]) * (uv[2][1] - uv[0][1])
            - (uv[2][0] - uv[0][0]) * (uv[1][1] - uv[0][1]);
        assert!(area.abs() > 1e-6, "triangle {i} has no UV2 area: {uv:?}");
    }
}

/// Whether every edge has at most two triangles running opposite ways, and the triangles
/// around every vertex form one fan.
fn manifold(vertex_count: usize, triangles: &[[u32; 3]]) -> bool {
    let mut directed = std::collections::HashSet::new();
    let mut undirected: std::collections::HashMap<(u32, u32), u32> = Default::default();
    for t in triangles {
        for k in 0..3 {
            let (a, b) = (t[k], t[(k + 1) % 3]);
            if !directed.insert((a, b)) {
                return false;
            }
            *undirected.entry((a.min(b), a.max(b))).or_default() += 1;
        }
    }
    if undirected.values().any(|&n| n > 2) {
        return false;
    }
    (0..vertex_count as u32).all(|v| {
        let tris: Vec<usize> = (0..triangles.len())
            .filter(|&i| triangles[i].contains(&v))
            .collect();
        let mut reached = vec![*tris.first().unwrap_or(&0)];
        let mut i = 0;
        while i < reached.len() && !tris.is_empty() {
            let t = triangles[reached[i]];
            for &u in &tris {
                let shared = t
                    .iter()
                    .filter(|&&c| c != v && triangles[u].contains(&c))
                    .count();
                if shared > 0 && !reached.contains(&u) {
                    reached.push(u);
                }
            }
            i += 1;
        }
        tris.is_empty() || reached.len() == tris.len()
    })
}

#[test]
fn the_cleanup_splits_non_manifold_edges_and_vertices() {
    // A fin (an edge of three triangles) and a bow tie (two fans at one vertex).
    let fin_positions = [
        [0.0, 0.0, 0.0],
        [1.0, 0.0, 0.0],
        [0.5, 1.0, 0.0],
        [0.5, -1.0, 0.3],
        [0.5, -0.8, -0.6],
    ];
    let fin = [[0, 1, 2], [1, 0, 3], [0, 1, 4]];
    let g = cleanup::prepare(&fin_positions, &fin).unwrap();
    assert!(!manifold(5, &fin));
    assert!(manifold(g.positions.len(), &g.triangles));
    // Both ends of the shared edge get a copy for each of their two extra fans.
    assert_eq!(g.positions.len(), 5 + 4);
    let bow_positions = [
        [0.0, 0.0, 0.0],
        [-1.0, -1.0, 0.0],
        [-1.0, 1.0, 0.0],
        [1.0, -1.0, 0.0],
        [1.0, 1.0, 0.0],
    ];
    let bow = [[0, 1, 2], [0, 4, 3]];
    let g = cleanup::prepare(&bow_positions, &bow).unwrap();
    assert!(!manifold(5, &bow));
    assert!(manifold(g.positions.len(), &g.triangles));
    // The apex keeps the fan of the last triangle; the first triangle gets the copy.
    assert_eq!(g.triangles, [[5, 1, 2], [0, 3, 4]]);
    assert_eq!(g.positions[5], g.positions[0]);
    // Both unwrap.
    assert!(unwrap_default(&fin_positions, &fin).is_ok());
    assert!(unwrap_default(&bow_positions, &bow).is_ok());
}

#[test]
fn introsort_orders_like_a_stable_sort_on_distinct_keys() {
    let mut keys: Vec<(u32, u32)> = (0..200u32)
        .map(|i| (i.wrapping_mul(2654435761) % 997, i))
        .collect();
    let mut expected = keys.clone();
    expected.sort();
    crate::sort::introsort(&mut keys, |a, b| a < b);
    assert_eq!(keys, expected);
}

#[test]
fn trigonometry_matches_the_reference_points() {
    assert_eq!(math::cos(0.0), 1.0);
    assert_eq!(math::sin(0.0), 0.0);
    assert_eq!(math::acos(1.0), 0.0);
    assert_eq!(math::acos(-1.0), std::f64::consts::PI);
    assert!(math::acos(f64::NAN) == std::f64::consts::PI);
    // The cosine polynomial is accurate to about 1e-6.
    for i in 0..64 {
        let x = i as f64 * 0.37 - 11.0;
        assert!(
            (math::cos(x) - x.cos()).abs() < 2e-6 && (math::sin(x) - x.sin()).abs() < 2e-6,
            "{x}"
        );
    }
    assert!((math::ln(2.5) - 2.5f64.ln()).abs() < 1e-12);
    assert!((math::exp(-1.3) - (-1.3f64).exp()).abs() < 1e-12);
}

#[test]
fn simplifies_a_large_part_as_before() {
    // A flat 10×10 grid of quads without UVs, simplified to a tenth of its 200 triangles: a
    // regression check of the collapses Unbound chooses (their effect on UV2 is measured in
    // tests/projects/lightmapuvs).
    let mut positions = Vec::new();
    for j in 0..11 {
        for i in 0..11 {
            positions.push([(i as f64 / 10.0) as f32, (j as f64 / 10.0) as f32, 0.0]);
        }
    }
    let mut triangles = Vec::new();
    for j in 0..10u32 {
        for i in 0..10u32 {
            let (a, c) = (j * 11 + i, j * 11 + i + 11);
            triangles.push([a, a + 1, c + 1]);
            triangles.push([a, c + 1, c]);
        }
    }
    let uvs = vec![[[0.0f32; 2]; 3]; triangles.len()];
    let mut simplified = simplify::Simplification::new(&positions, &triangles, &uvs).unwrap();
    assert_eq!(simplified.applied(), 118);
    let coarse = simplified.with_facets(20);
    assert_eq!(
        coarse,
        [
            173, 175, 177, 178, 179, 181, 183, 185, 187, 189, 190, 191, 192, 193, 194, 195, 196,
            197, 198, 199
        ]
    );
    let mut labels = vec![-1; triangles.len()];
    for &f in &coarse {
        labels[f as usize] = (f % 3) as i32;
    }
    simplified.refine(&mut labels, usize::MAX);
    assert_eq!(simplified.current().len(), 200);
    assert!(labels.iter().all(|&l| (0..3).contains(&l)));
}
