//! The precompute's stages on small hand-made scenes (the Unity 6000.0.71f1 Editor's precompute
//! of tests/projects/realtimegi is compared in `crate::tests`).
use super::clusters::{GROUP, Instance, SAMPLES_PER_CLUSTER, cluster};
use super::geometry::Bvh;
use super::math::V3;

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
