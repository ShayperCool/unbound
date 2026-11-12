//! The precompute's stages on small hand-made scenes (the Unity 6000.0.71f1 Editor's precompute
//! of tests/projects/realtimegi is compared in `crate::tests`).
use super::clusters::{GROUP, Instance, SAMPLES_PER_CLUSTER, cluster};
use super::geometry::Bvh;
use super::math::V3;

/// Lightmap Parameters' default Modelling Tolerance.
const TOLERANCE: f32 = 0.01;

/// A mesh: positions, normals, texels and triangles.
#[derive(Default)]
struct Mesh {
    positions: Vec<V3>,
    normals: Vec<V3>,
    texels: Vec<[f32; 2]>,
    triangles: Vec<[u32; 3]>,
}

impl Mesh {
    /// A quad from `corner` along `u` and `v` (its normal u x v), its texels `texel` + 8 per unit.
    fn quad(&mut self, corner: V3, u: V3, v: V3, texel: [f32; 2]) -> &mut Self {
        let n = super::math::normalized(super::math::cross(u, v));
        let base = self.positions.len() as u32;
        for (a, b) in [(0.0, 0.0), (1.0, 0.0), (1.0, 1.0), (0.0, 1.0)] {
            self.positions
                .push(std::array::from_fn(|k| corner[k] + u[k] * a + v[k] * b));
            self.normals.push(n);
            self.texels.push([texel[0] + 8.0 * a, texel[1] + 8.0 * b]);
        }
        self.triangles.push([base, base + 1, base + 2]);
        self.triangles.push([base, base + 2, base + 3]);
        self
    }
    /// The five faces of a box from `lo` to `hi` but its bottom, facing out (u x v).
    fn open_box(&mut self, lo: V3, hi: V3) -> &mut Self {
        let d: V3 = std::array::from_fn(|k| hi[k] - lo[k]);
        let (x, y, z) = ([d[0], 0.0, 0.0], [0.0, d[1], 0.0], [0.0, 0.0, d[2]]);
        self.quad([lo[0], hi[1], lo[2]], z, x, [10.0, 0.0])
            .quad(lo, z, y, [20.0, 0.0])
            .quad([hi[0], lo[1], lo[2]], y, z, [30.0, 0.0])
            .quad(lo, y, x, [40.0, 0.0])
            .quad([lo[0], lo[1], hi[2]], x, y, [50.0, 0.0])
    }
    fn instance(&self, cluster_size: f32) -> Instance<'_> {
        Instance {
            positions: &self.positions,
            normals: &self.normals,
            texels: &self.texels,
            triangles: &self.triangles,
            cluster_size,
            modelling_tolerance: TOLERANCE,
        }
    }
}

/// A floor from (0, 0, 0) to (x, 0, z), facing up.
fn floor(x: f32, z: f32) -> Mesh {
    let mut m = Mesh::default();
    m.quad([0.0; 3], [0.0, 0.0, z], [x, 0.0, 0.0], [0.0, 0.0]);
    m
}

#[test]
fn the_hierarchy_finds_the_nearest_front_and_back_faces() {
    // Two quads facing +y at heights 0 and 1, and many small triangles beside them.
    let mut tris = Vec::new();
    let mut facing = Vec::new();
    for y in [0.0, 1.0] {
        tris.push([[0.0, y, 0.0], [0.0, y, 1.0], [1.0, y, 1.0]]);
        tris.push([[0.0, y, 0.0], [1.0, y, 1.0], [1.0, y, 0.0]]);
        facing.extend([[0.0, 1.0, 0.0]; 2]);
    }
    for i in 0..100 {
        let x = 2.0 + i as f32 * 0.1;
        tris.push([[x, 0.0, 0.0], [x + 0.05, 0.0, 0.0], [x, 0.05, 0.0]]);
        facing.push([0.0, 0.0, 1.0]);
    }
    let bvh = Bvh::new(tris, facing);
    let down = bvh
        .intersect([0.5, 2.0, 0.5], [0.0, -1.0, 0.0], f32::INFINITY)
        .unwrap();
    assert!(down.front && (down.distance - 1.0).abs() < 1e-6 && down.triangle < 4);
    let up = bvh
        .intersect([0.5, 0.5, 0.5], [0.0, 1.0, 0.0], f32::INFINITY)
        .unwrap();
    assert!(!up.front && (up.distance - 0.5).abs() < 1e-6);
    // The limit and a miss.
    assert!(
        bvh.intersect([0.5, 0.5, 0.5], [0.0, 1.0, 0.0], 0.4)
            .is_none()
    );
    assert!(
        bvh.intersect([0.5, 2.0, 0.5], [0.0, 1.0, 0.0], f32::INFINITY)
            .is_none()
    );
    // One of the small triangles, from behind.
    let side = bvh
        .intersect([2.21, 0.01, -1.0], [0.0, 0.0, 1.0], f32::INFINITY)
        .unwrap();
    assert!(!side.front && side.triangle == 4 + 2);
}

#[test]
fn a_full_voxel_face_gets_the_precomputes_density_of_points() {
    let m = floor(3.0, 3.0);
    let c = cluster(&[m.instance(1.0)], 32, 32).unwrap();
    assert_eq!(c.input.clusters(), 9);
    for k in 0..9 {
        let points = &c.input.positions
            [c.input.cluster_points[k] as usize..c.input.cluster_points[k + 1] as usize];
        assert_eq!(points.len(), SAMPLES_PER_CLUSTER as usize);
        // All in one voxel of a cell.
        let cell = |p: &V3| (p[0].floor() as i32, p[2].floor() as i32);
        assert!(
            points
                .iter()
                .all(|p| cell(p) == cell(&points[0]) && p[1] == 0.0)
        );
        assert!((c.areas[k] - 1.0).abs() < 1e-5);
    }
    // Normals face up; the points come in groups of four neighbours.
    assert!(c.input.normals.iter().all(|n| *n == [0.0, 1.0, 0.0]));
    assert_eq!(c.input.positions.len() % GROUP, 0);
    // At half the cluster size, four times the clusters of the same number of points each.
    let c = cluster(&[m.instance(0.5)], 32, 32).unwrap();
    assert_eq!(c.input.clusters(), 36);
    assert_eq!(c.input.positions.len(), 36 * SAMPLES_PER_CLUSTER as usize);
}

#[test]
fn small_parts_join_a_neighbour() {
    // 1 x 1.3: a full voxel and a strip of 0.3 of a face, which joins it.
    let m = floor(1.0, 1.3);
    let c = cluster(&[m.instance(1.0)], 16, 16).unwrap();
    assert_eq!(c.input.clusters(), 1);
    assert!((c.areas[0] - 1.3).abs() < 1e-5);
    assert_eq!(
        c.input.positions.len(),
        4 * (1.3f32 * SAMPLES_PER_CLUSTER / 4.0).round() as usize
    );
    // Two strips of 0.3 along the far edge of two voxels join each other: 2 x 1.3 has three.
    let m = floor(2.0, 1.3);
    let c = cluster(&[m.instance(1.0)], 16, 16).unwrap();
    assert_eq!(c.input.clusters(), 3);
    // A strip of 0.5 of a face is a cluster of its own.
    let m = floor(1.0, 1.5);
    assert_eq!(
        cluster(&[m.instance(1.0)], 16, 16)
            .unwrap()
            .input
            .clusters(),
        2
    );
}

#[test]
fn points_inside_other_geometry_are_left_out() {
    // A box standing on a floor: the floor under it and the box's sides are one plane apart.
    let ground = floor(4.0, 4.0);
    let mut block = Mesh::default();
    block.open_box([1.0, 0.0, 1.0], [2.0, 1.0, 2.0]);
    let c = cluster(&[ground.instance(1.0), block.instance(1.0)], 64, 64).unwrap();
    for p in &c.input.positions {
        let under = p[1] == 0.0 && p[0] > 1.01 && p[0] < 1.99 && p[2] > 1.01 && p[2] < 1.99;
        assert!(!under, "{p:?}");
    }
    // The floor's clusters cover what the box leaves of it, the box its five faces.
    let total: f32 = c.areas.iter().sum();
    assert!((total - (16.0 - 1.0 + 5.0)).abs() < 0.2, "{total}");
    // Without the box the floor's points cover all of it.
    let alone = cluster(&[ground.instance(1.0)], 64, 64).unwrap();
    assert!((alone.areas.iter().sum::<f32>() - 16.0).abs() < 1e-4);
}

#[test]
fn the_tree_and_the_samples_cover_every_cluster() {
    let ground = floor(5.0, 3.0);
    let mut block = Mesh::default();
    block.open_box([1.0, 0.0, 1.0], [2.5, 1.2, 2.0]);
    let c = cluster(&[ground.instance(1.0), block.instance(0.5)], 64, 64).unwrap();
    let input = &c.input;
    // Each parent's children are the next values in order, before the parent, each counted
    // once; every parent's weights add up to 255; the last parent is the only root.
    let mut child = 0usize;
    for (p, &count) in input.tree_children.iter().enumerate() {
        assert!(count >= 1);
        assert!(child + count as usize <= input.clusters() + p);
        let w: u32 = input.tree_weights[child..child + count as usize]
            .iter()
            .map(|&w| w as u32)
            .sum();
        assert_eq!(w, 255);
        child += count as usize;
    }
    assert_eq!(child, input.clusters() + input.tree_children.len() - 1);
    // Material samples: per cluster, groups whose sample weights add up to their points, as
    // many points as the cluster has; the bounce samples' shares add up to one.
    let m = &c.materials;
    for k in 0..input.clusters() {
        let points = input.cluster_points[k + 1] - input.cluster_points[k];
        let groups = m.cluster_groups[k] as usize..m.cluster_groups[k + 1] as usize;
        let mut sum = 0;
        for g in groups {
            let samples = m.group_samples[g] as usize..m.group_samples[g + 1] as usize;
            let w: u32 = m.samples[samples].iter().map(|s| s.1 as u32).sum();
            assert_eq!(w, m.groups[g].1 as u32);
            sum += w;
        }
        assert_eq!(sum, points);
        let b = &c.bounce[c.bounce_samples[k] as usize..c.bounce_samples[k + 1] as usize];
        assert!((b.iter().map(|s| s.1).sum::<f32>() - 1.0).abs() < 1e-5);
        assert!(b.windows(2).all(|w| w[0].1 >= w[1].1));
    }
    // The block's renderer has clusters of half the size.
    let block_points = m
        .groups
        .iter()
        .filter(|g| g.0 == 1)
        .map(|g| g.1 as usize)
        .sum::<usize>();
    assert!(block_points > 0);
    // The floor under the block is no cluster; every other triangle is one's.
    assert!(
        c.triangle_cluster
            .iter()
            .all(|&k| (k as usize) < input.clusters() || k == super::clusters::NO_CLUSTER)
    );
    assert!(c.triangle_cluster.contains(&super::clusters::NO_CLUSTER));
}

#[test]
fn degenerate_and_not_finite_triangles_add_nothing() {
    let mut m = floor(2.0, 2.0);
    let base = m.positions.len() as u32;
    // A triangle of no area and one with a NaN corner.
    m.positions.extend([
        [0.0, 1.0, 0.0],
        [1.0, 1.0, 0.0],
        [2.0, 1.0, 0.0],
        [f32::NAN, 1.0, 0.0],
    ]);
    m.normals.extend([[0.0, 1.0, 0.0]; 4]);
    m.texels.extend([[0.0, 0.0]; 4]);
    m.triangles.push([base, base + 1, base + 2]);
    m.triangles.push([base, base + 3, base + 1]);
    let c = cluster(&[m.instance(1.0)], 32, 32).unwrap();
    assert_eq!(c.input.clusters(), 4);
    assert!((c.areas.iter().sum::<f32>() - 4.0).abs() < 1e-4);
    assert!(c.input.positions.iter().flatten().all(|v| v.is_finite()));
}

#[test]
fn inputs_that_cannot_be_clustered_fail() {
    let m = floor(1.0, 1.0);
    assert!(cluster(&[], 8, 8).is_err());
    assert!(cluster(&[m.instance(0.0)], 8, 8).is_err());
    assert!(cluster(&[m.instance(f32::NAN)], 8, 8).is_err());
    assert!(cluster(&[m.instance(1.0)], 0, 8).is_err());
    let mut bad = floor(1.0, 1.0);
    bad.triangles.push([0, 1, 9]);
    assert!(cluster(&[bad.instance(1.0)], 8, 8).is_err());
    bad.triangles.pop();
    bad.normals.pop();
    assert!(cluster(&[bad.instance(1.0)], 8, 8).is_err());
    // A surface cut into too many parts fails rather than exhausting memory.
    let huge = floor(4000.0, 4000.0);
    let error = cluster(&[huge.instance(1.0)], 8, 8)
        .err()
        .unwrap()
        .to_string();
    assert!(error.contains("more than"), "{error}");
}

#[test]
fn a_voxels_part_faces_one_half_space() {
    // A curved strip within one voxel (normals 60 degrees apart) is one cluster.
    let mut arc = Mesh::default();
    let (r, h, steps) = (0.9f32, 0.9f32, 6);
    for i in 0..=steps {
        let a = (15.0 + 60.0 * i as f32 / steps as f32).to_radians();
        for y in [0.0, h] {
            arc.positions.push([r * a.cos(), y, r * a.sin()]);
            arc.normals.push([a.cos(), 0.0, a.sin()]);
            arc.texels.push([i as f32, y]);
        }
    }
    for i in 0..steps as u32 {
        let (a, b) = (2 * i, 2 * i + 2);
        arc.triangles.push([a, a + 1, b + 1]);
        arc.triangles.push([a, b + 1, b]);
    }
    let c = cluster(&[arc.instance(1.0)], 16, 16).unwrap();
    assert_eq!(c.input.clusters(), 1);
    // Two faces of a corner at a right angle within one voxel are two.
    let mut corner = Mesh::default();
    corner
        .quad([0.0; 3], [0.0, 0.0, 0.9], [0.9, 0.0, 0.0], [0.0, 0.0])
        .quad([0.0; 3], [0.0, 0.9, 0.0], [0.0, 0.0, 0.9], [10.0, 0.0]);
    let c = cluster(&[corner.instance(1.0)], 32, 32).unwrap();
    assert_eq!(c.input.clusters(), 2);
}

#[test]
fn a_face_on_a_voxel_plane_keeps_its_area() {
    // At a cluster size of 0.2 a shelf at 1.8 lies on a plane of the grid from 0, which the
    // division by the size and the planes' positions round to opposite sides of.
    let mut m = floor(1.0, 1.0);
    m.quad(
        [0.0, 1.8, 0.0],
        [0.0, 0.0, 0.6],
        [0.6, 0.0, 0.0],
        [10.0, 0.0],
    );
    let c = cluster(&[m.instance(0.2)], 32, 32).unwrap();
    assert!(
        (c.areas.iter().sum::<f32>() - 1.36).abs() < 1e-3,
        "{:?}",
        c.areas.iter().sum::<f32>()
    );
    for size in [0.1, 0.3, 0.4, 0.45] {
        let c = cluster(&[m.instance(size)], 32, 32).unwrap();
        assert!((c.areas.iter().sum::<f32>() - 1.36).abs() < 1e-3, "{size}");
    }
    // A face on a plane between two slabs is in one of them, not in both.
    let c = cluster(&[floor(1.0, 1.0).instance(0.25)], 32, 32).unwrap();
    assert_eq!(c.input.clusters(), 16);
}

#[test]
fn renderers_of_other_cluster_sizes_keep_their_own_density() {
    let ground = floor(4.0, 4.0);
    let mut block = Mesh::default();
    block.open_box([1.0, 0.0, 1.0], [2.0, 1.0, 2.0]);
    let c = cluster(&[ground.instance(1.0), block.instance(0.5)], 64, 64).unwrap();
    // The box's faces in clusters of half a unit, each of the precompute's points.
    let m = &c.materials;
    let box_clusters: Vec<usize> = (0..c.input.clusters())
        .filter(|&k| m.groups[m.cluster_groups[k] as usize].0 == 1)
        .collect();
    assert_eq!(box_clusters.len(), 5 * 4);
    for k in box_clusters {
        let n = c.input.cluster_points[k + 1] - c.input.cluster_points[k];
        assert_eq!(n, SAMPLES_PER_CLUSTER as u32);
    }
    // The floor's points: its area less the box's footprint at one cluster's density per unit.
    let floor_points: usize = m
        .groups
        .iter()
        .filter(|g| g.0 == 0)
        .map(|g| g.1 as usize)
        .sum();
    assert!(
        (floor_points as f32 - 15.0 * SAMPLES_PER_CLUSTER).abs() <= 24.0,
        "{floor_points}"
    );
}

#[test]
fn far_and_tilted_surfaces_keep_their_points() {
    // A slope far from the origin: its points' rays must not meet its own triangles.
    let mut m = Mesh::default();
    m.quad(
        [300.0, 200.0, 300.0],
        [0.0, 0.6, 2.0],
        [2.0, 0.3, 0.0],
        [0.0, 0.0],
    );
    let area = {
        let n = super::math::cross([0.0, 0.6, 2.0], [2.0, 0.3, 0.0]);
        super::math::length(n)
    };
    let c = cluster(&[m.instance(0.2)], 32, 32).unwrap();
    assert!(
        (c.areas.iter().sum::<f32>() - area).abs() < 1e-3 * area,
        "{}",
        c.areas.iter().sum::<f32>()
    );
}

#[test]
fn vertices_no_triangle_uses_change_nothing() {
    let m = floor(2.0, 2.0);
    let a = cluster(&[m.instance(1.0)], 32, 32).unwrap();
    let mut n = floor(2.0, 2.0);
    for p in [[-1e9, 5.0, 3.0], [f32::NEG_INFINITY, 0.0, 0.0]] {
        n.positions.push(p);
        n.normals.push([0.0, 1.0, 0.0]);
        n.texels.push([0.0, 0.0]);
    }
    let b = cluster(&[n.instance(1.0)], 32, 32).unwrap();
    assert_eq!(a.input.positions, b.input.positions);
}

#[test]
fn surfaces_facing_geometry_within_the_modelling_tolerance_are_left_out() {
    // A slab 0.5 cm and one 2 cm above a floor: the floor under the first and the first's
    // bottom face each other within the tolerance of 1 cm and have no points; under the
    // second they keep them.
    let ground = floor(4.0, 2.0);
    let slab = |y: f32, x: f32| {
        let mut m = Mesh::default();
        // Its bottom facing down and its top facing up.
        m.quad([x, y, 0.5], [1.0, 0.0, 0.0], [0.0, 0.0, 1.0], [0.0, 20.0])
            .quad(
                [x, y + 0.1, 0.5],
                [0.0, 0.0, 1.0],
                [1.0, 0.0, 0.0],
                [10.0, 20.0],
            );
        m
    };
    let (near, far) = (slab(0.005, 0.5), slab(0.02, 2.5));
    let c = cluster(
        &[ground.instance(1.0), near.instance(1.0), far.instance(1.0)],
        64,
        64,
    )
    .unwrap();
    let inside = |p: &V3, x: f32| p[0] > x + 0.05 && p[0] < x + 0.95 && p[2] > 0.55 && p[2] < 1.45;
    let below = |p: &V3, y: f32| p[1] <= y + 1e-6;
    let near_count = c
        .input
        .positions
        .iter()
        .filter(|p| inside(p, 0.5) && below(p, 0.005))
        .count();
    let far_count = c
        .input
        .positions
        .iter()
        .filter(|p| inside(p, 2.5) && below(p, 0.02))
        .count();
    assert_eq!(near_count, 0);
    assert!(
        far_count as f32 >= 1.5 * SAMPLES_PER_CLUSTER * 0.8,
        "{far_count}"
    );
    // At a tolerance of zero both gaps keep their points.
    let open = [&ground, &near, &far].map(|m| Instance {
        modelling_tolerance: 0.0,
        ..m.instance(1.0)
    });
    let c = cluster(&open, 64, 64).unwrap();
    let near_count = c
        .input
        .positions
        .iter()
        .filter(|p| inside(p, 0.5) && below(p, 0.005))
        .count();
    assert!(
        near_count as f32 >= 1.5 * SAMPLES_PER_CLUSTER * 0.8,
        "{near_count}"
    );
}

#[test]
fn a_dense_mesh_is_not_refused() {
    // 640 000 triangles within four voxels: a part each, far under the limit.
    let mut m = Mesh::default();
    let n = 400u32;
    for z in 0..=n {
        for x in 0..=n {
            m.positions
                .push([2.0 * x as f32 / n as f32, 0.0, 2.0 * z as f32 / n as f32]);
            m.normals.push([0.0, 1.0, 0.0]);
            m.texels.push([0.0, 0.0]);
        }
    }
    for z in 0..n {
        for x in 0..n {
            let a = z * (n + 1) + x;
            m.triangles.push([a, a + n + 1, a + n + 2]);
            m.triangles.push([a, a + n + 2, a + 1]);
        }
    }
    let c = cluster(&[m.instance(1.0)], 32, 32).unwrap();
    assert_eq!(c.input.clusters(), 4);
    assert!((c.areas.iter().sum::<f32>() - 4.0).abs() < 1e-2);
}

#[test]
fn a_mesh_without_normals_faces_its_winding() {
    // A box whose normals are zero stands on a floor: rays meeting it from outside meet its
    // front, so the floor round it keeps its points and the floor under it does not.
    let ground = floor(4.0, 4.0);
    let mut block = Mesh::default();
    block.open_box([1.0, 0.0, 1.0], [2.0, 1.0, 2.0]);
    block.normals.iter_mut().for_each(|n| *n = [0.0; 3]);
    let c = cluster(&[ground.instance(1.0), block.instance(1.0)], 64, 64).unwrap();
    let floor_points = c.input.positions.iter().filter(|p| p[1] == 0.0).count() as f32;
    assert!(
        (floor_points - 15.0 * SAMPLES_PER_CLUSTER).abs() <= 24.0,
        "{floor_points}"
    );
}

#[test]
fn a_dense_tilted_mesh_gathers_into_parts() {
    // A grid of 160 000 triangles turned 30 degrees, of slightly different normals, in a few
    // voxels: each voxel's piece of it is one part, its clusters cover all of it.
    let mut m = Mesh::default();
    let n = 200u32;
    let (c, s) = (30f32.to_radians().cos(), 30f32.to_radians().sin());
    for z in 0..=n {
        for x in 0..=n {
            let (u, v) = (1.5 * x as f32 / n as f32, 1.5 * z as f32 / n as f32);
            // A gentle bump: no two normals alike.
            let h = 0.01 * (u * 7.0).sin() * (v * 5.0).cos();
            m.positions.push([u * c, u * s + h, v]);
            m.normals.push(super::math::normalized([-s, c, 0.0]));
            m.texels.push([0.0, 0.0]);
        }
    }
    for z in 0..n {
        for x in 0..n {
            let a = z * (n + 1) + x;
            m.triangles.push([a, a + n + 1, a + n + 2]);
            m.triangles.push([a, a + n + 2, a + 1]);
        }
    }
    let start = std::time::Instant::now();
    let c = cluster(&[m.instance(1.0)], 32, 32).unwrap();
    assert!(
        start.elapsed().as_secs_f32() < 10.0,
        "{:?}",
        start.elapsed()
    );
    assert!(c.input.clusters() >= 2);
}

/// The transport of `meshes` (cluster size 1) into an output of `size` x `size` texels whose
/// every texel is its own chart.
fn transport_of(meshes: &[&Mesh], size: u32, budget: usize) -> crate::data::Transport {
    use super::transport::{DEFAULT_SETTINGS, Settings, transport};
    let instances: Vec<Instance> = meshes.iter().map(|m| m.instance(1.0)).collect();
    let c = cluster(&instances, size, size).unwrap();
    let mask: Vec<u32> = (0..size * size).collect();
    let settings = Settings {
        budget,
        ..DEFAULT_SETTINGS
    };
    transport(&instances, &c, size, size, &mask, [7; 16], settings).unwrap()
}

/// Per texel: its weights by reference (dependency, index), decoded.
fn texel_weights(
    t: &crate::data::Transport,
) -> std::collections::HashMap<(u16, u16), Vec<((u16, u32), f32)>> {
    let mut out: std::collections::HashMap<(u16, u16), Vec<((u16, u32), f32)>> = Default::default();
    for s in &t.segments {
        let n = s.references.len();
        for (k, &texel) in s.texels.iter().enumerate() {
            let list = out.entry(texel).or_default();
            for (r, &reference) in s.references.iter().enumerate() {
                let w =
                    s.reference_weights[r] as f32 * s.weights[k * n + r] as f32 / (65535.0 * 255.0);
                if w > 0.0 {
                    list.push((reference, w));
                }
            }
        }
    }
    out
}

#[test]
fn a_texel_under_the_open_sky_sees_the_environment_by_its_cosine() {
    // A floor alone: every texel takes all of its light from the environment, 0.554 through
    // the cube face it faces and 0.111 through each of the four beside it (as the Unity
    // 6000.0.71f1 Editor's transport of an outward face of tests/projects/realtimegi `Room`).
    let m = floor(1.0, 1.0);
    let t = transport_of(&[&m], 8, 1000);
    assert_eq!(t.dependencies[0], crate::data::ENVIRONMENT_ID);
    let weights = texel_weights(&t);
    assert!(!weights.is_empty());
    for list in weights.values() {
        let mut faces = [0f32; 6];
        for &((dep, index), w) in list {
            assert_eq!(dep, 0, "a cluster reference under the open sky");
            assert!(index < 384, "{index}");
            faces[index as usize / 64] += w;
        }
        assert!((faces.iter().sum::<f32>() - 1.0).abs() < 0.01, "{faces:?}");
        assert!(
            (faces[2] - 0.554).abs() < 0.006 && faces[3] == 0.0,
            "{faces:?}"
        );
        for f in [0, 1, 4, 5] {
            assert!((faces[f] - 0.111).abs() < 0.004, "{faces:?}");
        }
    }
}

#[test]
fn a_texel_facing_a_near_surface_takes_its_light_from_its_clusters() {
    // Two quads 5 cm apart facing each other: most of a texel's light is the other's, but at
    // the edges, where rays leave between them.
    let mut m = floor(1.0, 1.0);
    m.quad(
        [0.0, 0.05, 0.0],
        [1.0, 0.0, 0.0],
        [0.0, 0.0, 1.0],
        [0.0, 10.0],
    );
    let t = transport_of(&[&m], 32, 1000);
    let weights = texel_weights(&t);
    assert!(weights.len() >= 2 * 64);
    for (texel, list) in &weights {
        let clusters: f32 = list.iter().filter(|r| r.0.0 == 1).map(|r| r.1).sum();
        let all: f32 = list.iter().map(|r| r.1).sum();
        assert!((all - 1.0).abs() < 0.01, "{texel:?}: {all}");
        // The floor's texels run 8 a unit from (0, 0), the quad's from (0, 10): those more
        // than a quarter from the edges.
        let inner = |v: u16| (2..6).contains(&v);
        if inner(texel.0) && (inner(texel.1) || inner(texel.1.wrapping_sub(10))) {
            assert!(clusters > 0.9, "{texel:?}: {clusters} of {all}");
        }
    }
}

#[test]
fn the_budget_bounds_a_texels_references() {
    // A floor under a lid of many small clusters: the references of each texel fit the budget
    // and still add up to all of its light.
    let ground = floor(8.0, 8.0);
    let mut lid = Mesh::default();
    lid.quad(
        [0.0, 2.0, 0.0],
        [8.0, 0.0, 0.0],
        [0.0, 0.0, 8.0],
        [0.0, 40.0],
    );
    let unlimited = texel_weights(&transport_of(&[&ground, &lid], 64, 100_000));
    let limited = texel_weights(&transport_of(&[&ground, &lid], 64, 16));
    assert!(unlimited.values().any(|l| l.len() > 32));
    for (texel, list) in &limited {
        assert!(list.len() <= 16, "{texel:?}: {}", list.len());
        let (a, b): (f32, f32) = (
            list.iter().map(|r| r.1).sum(),
            unlimited[texel].iter().map(|r| r.1).sum(),
        );
        assert!((a - b).abs() < 0.01, "{texel:?}: {a} {b}");
    }
}

#[test]
fn a_texel_inside_other_geometry_takes_a_neighbours_light() {
    // The floor under a box: the texels whose footprint the box covers take the light of the
    // nearest texel of their chart that is not covered, rather than none.
    let ground = floor(4.0, 4.0);
    let mut block = Mesh::default();
    block.open_box([1.0, 0.0, 1.0], [3.0, 1.0, 3.0]);
    let instances = [ground.instance(1.0), block.instance(1.0)];
    let c = cluster(&instances, 64, 64).unwrap();
    // One chart for the floor's texels (0..8 x 0..8 at 2 texels a unit), one for the rest.
    let mask: Vec<u32> = (0..64 * 64)
        .map(|i| (i % 64 >= 8 || i / 64 >= 8) as u32)
        .collect();
    let mut floor_texels = Mesh::default();
    floor_texels.quad([0.0; 3], [0.0, 0.0, 4.0], [4.0, 0.0, 0.0], [0.0, 0.0]);
    let t = super::transport::transport(
        &instances,
        &c,
        64,
        64,
        &mask,
        [7; 16],
        super::transport::DEFAULT_SETTINGS,
    )
    .unwrap();
    let w = texel_weights(&t);
    // The floor's texels: its quad's texels run 8 per unit edge here; those under the box
    // (texels 3..4 on either axis at 2 per unit) still receive.
    for (texel, list) in &w {
        if texel.0 < 8 && texel.1 < 8 {
            assert!(list.iter().map(|r| r.1).sum::<f32>() > 0.5, "{texel:?}");
        }
    }
}

#[test]
fn transports_that_cannot_be_made_fail() {
    use super::transport::{DEFAULT_SETTINGS, Settings, transport};
    let m = floor(1.0, 1.0);
    let instances = [m.instance(1.0)];
    let c = cluster(&instances, 8, 8).unwrap();
    let mask: Vec<u32> = (0..64).collect();
    assert!(transport(&instances, &c, 8, 8, &mask[..63], [0; 16], DEFAULT_SETTINGS).is_err());
    for settings in [
        Settings {
            rays: 0,
            ..DEFAULT_SETTINGS
        },
        Settings {
            budget: 0,
            ..DEFAULT_SETTINGS
        },
    ] {
        assert!(transport(&instances, &c, 8, 8, &mask, [0; 16], settings).is_err());
    }
}
