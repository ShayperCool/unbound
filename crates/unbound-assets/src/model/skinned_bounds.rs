//! The imported `SkinnedMeshRenderer.localBounds` (`m_AABB`) as the Unity 6000.0.71f1 importer
//! sets it (tests/projects/modelaxis, `Skinned` and `Rigged` sets). Unity documents that a
//! skinned renderer's default bounds include the model's imported animations
//! (Renderer.localBounds); the importer does this for Legacy rigs only:
//!
//! - Every rig: per bone, the box of the vertices it influences (with their blend shape frame
//!   positions) in its bind space, carried as a box into the root bone's space by the bone's
//!   import pose; the union over bones. The root bone's world-to-local matrix inverts a zero
//!   scale to zero, as Transform does.
//! - Legacy rigs add, for each clip, every sampled frame: from the clip's first key time, in
//!   steps of `1 / sampleRate` accumulated in single precision while the time is not past the
//!   last key (so a frame that rounding puts past the end is not sampled), the vertices skinned
//!   by the pose the clip's transform curves give (other nodes keep their import transform),
//!   in the root bone's space, for the meshes one of whose bones is animated by the clip or
//!   under a node it animates. Blend shapes are not applied to these vertices.
//!
//! Generic, Humanoid and None rigs keep the import-pose box whatever their clips do.
use super::{
    ImportedModel, Key, ModelClip, ModelMesh, ModelNode, ModelRenderer, RendererKind, curves,
    pivots,
};
use std::collections::HashMap;

/// Sets every skinned renderer's local bounds (`paths`: every node index per clip path).
pub(super) fn set_skinned_local_bounds(
    model: &mut ImportedModel,
    paths: &HashMap<String, Vec<u32>>,
) {
    let world = pivots::world_matrices(&model.nodes);
    let mut bounds: Vec<(usize, Box3, bool)> = Vec::new();
    for (index, node) in model.nodes.iter().enumerate() {
        if let Some(renderer) = &node.renderer
            && renderer.kind == RendererKind::SkinnedMesh
            && let Some(mesh) = model.meshes.get(renderer.mesh as usize)
            && let Some(root) = renderer.root_bone
        {
            let to_root = world_to_local(&model.nodes, root as usize);
            bounds.push((index, bone_boxes(renderer, mesh, &world, &to_root), false));
        }
    }
    if model.settings.animation_type == 1 {
        for clip in model.clips.iter().filter(|c| !c.preview) {
            sample_clip(model, clip, paths, &mut bounds);
        }
    }
    for (index, bounds, update_when_offscreen) in bounds {
        let Some(renderer) = model.nodes[index].renderer.as_mut() else {
            continue;
        };
        renderer.local_bounds = bounds.centre_extents();
        renderer.update_when_offscreen = update_when_offscreen;
    }
}

/// An axis-aligned box, empty until a point is added.
#[derive(Clone, Copy, Debug)]
struct Box3 {
    lo: [f64; 3],
    hi: [f64; 3],
}

impl Box3 {
    const EMPTY: Self = Self {
        lo: [f64::INFINITY; 3],
        hi: [f64::NEG_INFINITY; 3],
    };

    fn add(&mut self, p: ufbx::Vec3) {
        for (k, v) in [p.x, p.y, p.z].into_iter().enumerate() {
            self.lo[k] = self.lo[k].min(v);
            self.hi[k] = self.hi[k].max(v);
        }
    }

    fn is_empty(&self) -> bool {
        self.lo[0] > self.hi[0]
    }

    /// Adds this box, carried by `m`, as a box.
    fn carry_into(&self, m: &ufbx::Matrix, out: &mut Self) {
        let centre = ufbx::Vec3 {
            x: (self.lo[0] + self.hi[0]) * 0.5,
            y: (self.lo[1] + self.hi[1]) * 0.5,
            z: (self.lo[2] + self.hi[2]) * 0.5,
        };
        let e: [f64; 3] = std::array::from_fn(|k| (self.hi[k] - self.lo[k]) * 0.5);
        let c = ufbx::transform_position(m, centre);
        let rows = [
            [m.m00, m.m01, m.m02],
            [m.m10, m.m11, m.m12],
            [m.m20, m.m21, m.m22],
        ];
        for (i, (row, c)) in rows.iter().zip([c.x, c.y, c.z]).enumerate() {
            let extent = row[0].abs() * e[0] + row[1].abs() * e[1] + row[2].abs() * e[2];
            out.lo[i] = out.lo[i].min(c - extent);
            out.hi[i] = out.hi[i].max(c + extent);
        }
    }

    /// (centre, extents); None when empty or not finite.
    fn centre_extents(&self) -> Option<([f32; 3], [f32; 3])> {
        if self.is_empty() || self.lo.iter().chain(&self.hi).any(|v| !v.is_finite()) {
            return None;
        }
        Some((
            std::array::from_fn(|k| ((self.lo[k] + self.hi[k]) * 0.5) as f32),
            std::array::from_fn(|k| ((self.hi[k] - self.lo[k]) * 0.5) as f32),
        ))
    }
}

fn determinant(m: &ufbx::Matrix) -> f64 {
    m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) - m.m01 * (m.m10 * m.m22 - m.m12 * m.m20)
        + m.m02 * (m.m10 * m.m21 - m.m11 * m.m20)
}

/// The matrix has no inverse (a zero scale, or not finite).
fn is_singular(m: &ufbx::Matrix) -> bool {
    let det = determinant(m);
    !det.is_finite() || det.abs() < f64::MIN_POSITIVE
}

/// Unity's 1/x for a scale component: 0 for |x| at or below this.
const SCALE_EPSILON: f64 = 1e-5;

/// The world-to-local matrix of `index` as Transform composes it: each level's inverse
/// scale, rotation and translation, a near-zero scale inverted to zero.
fn world_to_local(nodes: &[ModelNode], index: usize) -> ufbx::Matrix {
    let mut out = identity();
    let mut at = Some(index);
    while let Some(i) = at {
        let node = &nodes[i];
        let inverse_scale = node.scale.map(|s| {
            let s = f64::from(s);
            if s.abs() > SCALE_EPSILON {
                1.0 / s
            } else {
                0.0
            }
        });
        let [x, y, z, w] = node.rotation.map(f64::from);
        let inverse_rotation = ufbx::transform_to_matrix(&ufbx::Transform {
            translation: pivots::ZERO,
            rotation: ufbx::Quat {
                x: -x,
                y: -y,
                z: -z,
                w,
            },
            scale: ufbx::Vec3 {
                x: 1.0,
                y: 1.0,
                z: 1.0,
            },
        });
        let [tx, ty, tz] = node.position.map(f64::from);
        let mut local_inverse = ufbx::Matrix {
            m03: -tx,
            m13: -ty,
            m23: -tz,
            ..identity()
        };
        local_inverse = ufbx::matrix_mul(&inverse_rotation, &local_inverse);
        let scale = ufbx::Matrix {
            m00: inverse_scale[0],
            m11: inverse_scale[1],
            m22: inverse_scale[2],
            ..Default::default()
        };
        local_inverse = ufbx::matrix_mul(&scale, &local_inverse);
        // World-to-local of i = local⁻¹(i) · world-to-local(parent).
        out = ufbx::matrix_mul(&out, &local_inverse);
        at = node.parent.map(|p| p as usize);
    }
    out
}

fn identity() -> ufbx::Matrix {
    ufbx::Matrix {
        m00: 1.0,
        m11: 1.0,
        m22: 1.0,
        ..Default::default()
    }
}

/// A mesh bind pose (column-major 4x4) as a matrix.
fn bind_matrix(b: &[f32; 16]) -> ufbx::Matrix {
    let b = b.map(f64::from);
    ufbx::Matrix {
        m00: b[0],
        m10: b[1],
        m20: b[2],
        m01: b[4],
        m11: b[5],
        m21: b[6],
        m02: b[8],
        m12: b[9],
        m22: b[10],
        m03: b[12],
        m13: b[13],
        m23: b[14],
    }
}

fn vec3(p: [f32; 3]) -> ufbx::Vec3 {
    ufbx::Vec3 {
        x: p[0].into(),
        y: p[1].into(),
        z: p[2].into(),
    }
}

/// The import-pose box: per bone, the box of the vertices it influences (with their blend
/// shape frame positions) in its bind space, carried into the root's space (`to_root`) by the
/// bone's world matrix in `world`.
fn bone_boxes(
    renderer: &ModelRenderer,
    mesh: &ModelMesh,
    world: &[ufbx::Matrix],
    to_root: &ufbx::Matrix,
) -> Box3 {
    let binds: Vec<ufbx::Matrix> = mesh.bind_poses.iter().map(bind_matrix).collect();
    let bones = renderer.bones.len().min(binds.len());
    let mut boxes = vec![Box3::EMPTY; bones];
    let frames: Vec<&super::ModelBlendFrame> =
        mesh.blend_shapes.iter().flat_map(|s| &s.frames).collect();
    for (v, weight) in mesh.bone_weights.iter().enumerate() {
        let Some(&p) = mesh.positions.get(v) else {
            break;
        };
        let shaped = std::iter::once(p).chain(frames.iter().filter_map(|f| {
            f.vertices
                .get(v)
                .map(|d| [p[0] + d[0], p[1] + d[1], p[2] + d[2]])
        }));
        for point in shaped {
            let point = vec3(point);
            for k in 0..4 {
                let bone = weight.bones[k] as usize;
                if weight.weights[k] > 0.0 && bone < bones {
                    boxes[bone].add(ufbx::transform_position(&binds[bone], point));
                }
            }
        }
    }
    let mut out = Box3::EMPTY;
    for (bone, b) in boxes.iter().enumerate() {
        if b.is_empty() {
            continue;
        }
        let Some(bone_world) = world.get(renderer.bones[bone] as usize) else {
            continue;
        };
        b.carry_into(&ufbx::matrix_mul(to_root, bone_world), &mut out);
    }
    out
}

/// One node's transform curves with their slopes.
struct NodeTrack {
    positions: Vec<Key<3>>,
    position_slopes: Vec<([f32; 3], [f32; 3])>,
    rotations: Vec<Key<4>>,
    rotation_slopes: Vec<([f32; 4], [f32; 4])>,
    scales: Vec<Key<3>>,
    scale_slopes: Vec<([f32; 3], [f32; 3])>,
}

impl NodeTrack {
    /// The node's local matrix at `time`: curves where it has them, else its import transform.
    fn matrix(&self, node: &ModelNode, time: f32) -> ufbx::Matrix {
        let position = if self.positions.is_empty() {
            node.position
        } else {
            curves::evaluate(&self.positions, &self.position_slopes, time)
        };
        let rotation = if self.rotations.is_empty() {
            node.rotation
        } else {
            let q = curves::evaluate(&self.rotations, &self.rotation_slopes, time);
            let length = q.iter().map(|c| c * c).sum::<f32>().sqrt();
            if length > 0.0 {
                q.map(|c| c / length)
            } else {
                node.rotation
            }
        };
        let scale = if self.scales.is_empty() {
            node.scale
        } else {
            curves::evaluate(&self.scales, &self.scale_slopes, time)
        };
        let [x, y, z, w] = rotation.map(f64::from);
        ufbx::transform_to_matrix(&ufbx::Transform {
            translation: vec3(position),
            rotation: ufbx::Quat { x, y, z, w },
            scale: vec3(scale),
        })
    }
}

/// Adds `clip`'s sampled frames to the bounds of the meshes it animates.
fn sample_clip(
    model: &ImportedModel,
    clip: &ModelClip,
    paths: &HashMap<String, Vec<u32>>,
    bounds: &mut [(usize, Box3, bool)],
) {
    let nodes = &model.nodes;
    let mut tracks: HashMap<usize, NodeTrack> = HashMap::new();
    let (mut start, mut end) = (f32::INFINITY, f32::NEG_INFINITY);
    for curve in &clip.curves {
        for (first, last) in [
            curve
                .positions
                .first()
                .zip(curve.positions.last())
                .map(|(a, b)| (a.time, b.time)),
            curve
                .rotations
                .first()
                .zip(curve.rotations.last())
                .map(|(a, b)| (a.time, b.time)),
            curve
                .scales
                .first()
                .zip(curve.scales.last())
                .map(|(a, b)| (a.time, b.time)),
        ]
        .into_iter()
        .flatten()
        {
            start = start.min(first);
            end = end.max(last);
        }
        // A path names the first node at it, as clips bind.
        let Some(&index) = paths.get(&curve.path).and_then(|i| i.first()) else {
            continue;
        };
        if curve.positions.is_empty() && curve.rotations.is_empty() && curve.scales.is_empty() {
            continue;
        }
        let (rotations, rotation_slopes) = curve.rotation_track(clip.smooth);
        tracks.insert(
            index as usize,
            NodeTrack {
                position_slopes: curve.positions_slopes(),
                positions: curve.positions.clone(),
                rotation_slopes,
                rotations,
                scale_slopes: curve.scales_slopes(clip.smooth),
                scales: curve.scales.clone(),
            },
        );
    }
    for float in &clip.floats {
        if let (Some(a), Some(b)) = (float.keys.first(), float.keys.last()) {
            start = start.min(a.time);
            end = end.max(b.time);
        }
    }
    if tracks.is_empty() || start > end || clip.sample_rate <= 0.0 {
        return;
    }
    // Animated: a node with curves or under one (nodes are listed parents first).
    let mut animated = vec![false; nodes.len()];
    for (i, node) in nodes.iter().enumerate() {
        animated[i] = tracks.contains_key(&i) || node.parent.is_some_and(|p| animated[p as usize]);
    }
    let meshes: Vec<usize> = (0..bounds.len())
        .filter(|&b| {
            let renderer = nodes[bounds[b].0].renderer.as_ref();
            renderer.is_some_and(|r| r.bones.iter().any(|&bone| animated[bone as usize]))
        })
        .collect();
    if meshes.is_empty() {
        return;
    }
    let step = 1.0f32 / clip.sample_rate;
    let mut world = vec![identity(); nodes.len()];
    let mut skin = Vec::new();
    let mut time = start;
    loop {
        for (i, node) in nodes.iter().enumerate() {
            let local = match tracks.get(&i) {
                Some(track) => track.matrix(node, time),
                None => pivots::matrix(node),
            };
            world[i] = match node.parent {
                Some(p) => ufbx::matrix_mul(&world[p as usize], &local),
                None => local,
            };
        }
        for &b in &meshes {
            let (index, ref mut bounds, ref mut update_when_offscreen) = bounds[b];
            let Some(renderer) = nodes[index].renderer.as_ref() else {
                continue;
            };
            let Some(mesh) = model.meshes.get(renderer.mesh as usize) else {
                continue;
            };
            let Some(root) = renderer.root_bone.map(|r| &world[r as usize]) else {
                continue;
            };
            // A pose the root bone's matrix cannot invert switches the renderer to updating
            // its bounds every frame instead.
            if is_singular(root) {
                *update_when_offscreen = true;
                continue;
            }
            let to_root = ufbx::matrix_invert(root);
            skinned_vertices_into(renderer, mesh, &world, &to_root, &mut skin, bounds);
        }
        let next = time + step;
        // Past the last key; or a time so large the step no longer advances it.
        if !(next <= end) || next <= time {
            break;
        }
        time = next;
    }
}

/// Adds the mesh's vertices skinned by the bones' `world` matrices, in the root's space (a
/// vertex without weights at the root's origin). `skin`: scratch for the skinning matrices.
fn skinned_vertices_into(
    renderer: &ModelRenderer,
    mesh: &ModelMesh,
    world: &[ufbx::Matrix],
    to_root: &ufbx::Matrix,
    skin: &mut Vec<ufbx::Matrix>,
    out: &mut Box3,
) {
    skin.clear();
    skin.extend(
        renderer
            .bones
            .iter()
            .zip(&mesh.bind_poses)
            .map(|(&bone, bind)| {
                ufbx::matrix_mul(
                    &ufbx::matrix_mul(to_root, &world[bone as usize]),
                    &bind_matrix(bind),
                )
            }),
    );
    for (p, weight) in mesh.positions.iter().zip(&mesh.bone_weights) {
        let p = vec3(*p);
        let mut skinned = pivots::ZERO;
        for k in 0..4 {
            let Some(m) = skin.get(weight.bones[k] as usize) else {
                continue;
            };
            if weight.weights[k] == 0.0 {
                continue;
            }
            let w = f64::from(weight.weights[k]);
            let q = ufbx::transform_position(m, p);
            skinned.x += q.x * w;
            skinned.y += q.y * w;
            skinned.z += q.z * w;
        }
        out.add(skinned);
    }
}

#[cfg(test)]
mod tests {
    use super::super::*;
    use super::*;

    fn node_at(parent: Option<u32>, position: [f32; 3], rotation: [f32; 4]) -> ModelNode {
        ModelNode {
            name: String::new(),
            parent,
            children: Vec::new(),
            position,
            rotation,
            scale: [1.0; 3],
            game_object: FileId {
                id: 0,
                source: FileIdSource::Derived,
            },
            transform: FileId {
                id: 0,
                source: FileIdSource::Derived,
            },
            renderer: None,
        }
    }

    fn skinned_column() -> ModelMesh {
        let identity = [
            1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0,
        ];
        let weight = |bone: u16| BoneWeight {
            bones: [bone, 0, 0, 0],
            weights: [1.0, 0.0, 0.0, 0.0],
        };
        ModelMesh {
            name: String::new(),
            file_id: FileId {
                id: 0,
                source: FileIdSource::Derived,
            },
            // Bone 0 holds the first two vertices, bone 1 the third; bone 2 none.
            positions: vec![[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [2.0, 1.0, 0.0]],
            normals: Vec::new(),
            tangents: Vec::new(),
            uvs: Vec::new(),
            colors: Vec::new(),
            submeshes: Vec::new(),
            bone_weights: vec![weight(0), weight(0), weight(1)],
            bind_poses: vec![identity; 3],
            bounds_min: [0.0; 3],
            bounds_max: [0.0; 3],
            blend_shapes: vec![ModelBlendShape {
                name: String::new(),
                frames: vec![ModelBlendFrame {
                    weight: 100.0,
                    vertices: vec![[0.0, 0.0, 3.0], [0.0; 3], [0.0; 3]],
                    normals: Vec::new(),
                    tangents: Vec::new(),
                }],
            }],
        }
    }

    fn skinned_renderer(bones: Vec<u32>, root_bone: Option<u32>) -> ModelRenderer {
        ModelRenderer {
            kind: RendererKind::SkinnedMesh,
            mesh: 0,
            materials: Vec::new(),
            renderer: FileId {
                id: 0,
                source: FileIdSource::Derived,
            },
            filter: None,
            bones,
            root_bone,
            blend_shape_weights: Vec::new(),
            local_bounds: None,
            update_when_offscreen: false,
        }
    }

    fn assert_box(bounds: Option<([f32; 3], [f32; 3])>, lo: [f32; 3], hi: [f32; 3]) {
        let (centre, extents) = bounds.unwrap();
        for k in 0..3 {
            assert!(
                (centre[k] - (lo[k] + hi[k]) / 2.0).abs() < 1e-5,
                "{centre:?} {extents:?}"
            );
            assert!(
                (extents[k] - (hi[k] - lo[k]) / 2.0).abs() < 1e-5,
                "{centre:?} {extents:?}"
            );
        }
    }

    /// Root bone at (1,0,0); a child bone turned 90 degrees about Z at (0,1,0) in it.
    fn rig() -> Vec<ModelNode> {
        let half = std::f32::consts::FRAC_1_SQRT_2;
        vec![
            node_at(None, [0.0; 3], [0.0, 0.0, 0.0, 1.0]),
            node_at(Some(0), [1.0, 0.0, 0.0], [0.0, 0.0, 0.0, 1.0]),
            node_at(Some(1), [0.0, 1.0, 0.0], [0.0, 0.0, half, half]),
        ]
    }

    fn model(nodes: Vec<ModelNode>, animation_type: i32, clips: Vec<ModelClip>) -> ImportedModel {
        let mut nodes = nodes;
        nodes[0].renderer = Some(skinned_renderer(vec![1, 2, 0], Some(1)));
        let meta = unbound_yaml::parse_value(&format!(
            "ModelImporter:\n  animationType: {animation_type}\n"
        ))
        .unwrap();
        ImportedModel {
            name: String::new(),
            nodes,
            file_order: Vec::new(),
            meshes: vec![skinned_column()],
            materials: Vec::new(),
            clips,
            animator: None,
            avatar: None,
            human_description: None,
            lod_group: None,
            scale: 1.0,
            settings: ModelSettings::read(&meta),
            warnings: Vec::new(),
        }
    }

    fn paths() -> HashMap<String, Vec<u32>> {
        [
            ("".to_string(), vec![0]),
            ("Root".to_string(), vec![1]),
            ("Root/Arm".to_string(), vec![2]),
        ]
        .into()
    }

    /// A clip moving the arm bone from y=1 to y=3 over one second at `rate` frames per second.
    fn lift(rate: f32) -> ModelClip {
        let key = |time, y| Key {
            time,
            value: [0.0, y, 0.0],
        };
        ModelClip {
            name: "Lift".into(),
            file_id: FileId {
                id: 0,
                source: FileIdSource::Derived,
            },
            legacy: true,
            sample_rate: rate,
            take_start: 0.0,
            length: 1.0,
            wrap_mode: 0,
            loop_time: false,
            loop_blend: false,
            cycle_offset: 0.0,
            curves: vec![NodeCurves::new(
                "Root/Arm".into(),
                vec![key(0.0, 1.0), key(1.0, 3.0)],
                Vec::new(),
                Vec::new(),
            )],
            floats: Vec::new(),
            events: Vec::new(),
            parameters: Vec::new(),
            mask: ClipMask::default(),
            additive_reference: AdditiveReference::default(),
            settings: ClipSettings::default(),
            preview: false,
            smooth: false,
            human_key_reduction: None,
            human_transforms: None,
            human_parts_off: 0,
        }
    }

    #[test]
    fn import_pose_bounds_are_per_bone_boxes_in_the_root_bones_space() {
        let mut model = model(rig(), 2, vec![lift(30.0)]);
        set_skinned_local_bounds(&mut model, &paths());
        // Bone 0 (the root): x 0..1, z 0..3 with the shape. Bone 1: the point (2,1) turned to
        // (-1,2), plus (0,1): at (-1,3). Generic rigs ignore what the clip does.
        let renderer = model.nodes[0].renderer.as_ref().unwrap();
        assert_box(renderer.local_bounds, [-1.0, 0.0, 0.0], [1.0, 3.0, 3.0]);
        assert!(!renderer.update_when_offscreen);
    }

    #[test]
    fn legacy_bounds_add_the_clips_sampled_frames() {
        // The arm's vertex rises from y=3 to y=5; at 30 frames per second the single-precision
        // step sum passes 1 s before the last frame, so the highest sample is frame 29.
        let mut model = model(rig(), 1, vec![lift(30.0)]);
        set_skinned_local_bounds(&mut model, &paths());
        let (centre, extents) = model.nodes[0]
            .renderer
            .as_ref()
            .unwrap()
            .local_bounds
            .unwrap();
        let high = centre[1] + extents[1];
        // Frame 29 (t = 0.96667): the vertex at 2 + 1 + 2·t.
        assert!((high - (3.0 + 2.0 * 29.0 / 30.0)).abs() < 1e-4, "{high}");
        // 60 frames per second reaches the end (frame 60 sums to just under 1 s).
        let mut model = self::model(rig(), 1, vec![lift(60.0)]);
        set_skinned_local_bounds(&mut model, &paths());
        let (centre, extents) = model.nodes[0]
            .renderer
            .as_ref()
            .unwrap()
            .local_bounds
            .unwrap();
        assert!((centre[1] + extents[1] - 5.0).abs() < 1e-4);
        // Blend shapes stay out of the sampled vertices: z keeps the import-pose 0..3.
        assert!((centre[2] - 1.5).abs() < 1e-5 && (extents[2] - 1.5).abs() < 1e-5);
    }

    #[test]
    fn a_zero_scale_root_bone_collapses_onto_its_origin() {
        // Transform inverts a zero scale to zero: every point lands on the root's origin axis.
        let mut nodes = rig();
        nodes[1].scale = [0.0, 1.0, 1.0];
        let mut model = model(nodes, 2, Vec::new());
        set_skinned_local_bounds(&mut model, &paths());
        let renderer = model.nodes[0].renderer.as_ref().unwrap();
        let (centre, extents) = renderer.local_bounds.unwrap();
        assert_eq!((centre[0], extents[0]), (0.0, 0.0));
        assert!(extents[1] > 0.0);
    }

    #[test]
    fn a_singular_sampled_root_switches_to_offscreen_updates() {
        let mut clip = lift(30.0);
        clip.curves[0].path = "Root".into();
        clip.curves[0].scales = vec![Key {
            time: 0.0,
            value: [0.0; 3],
        }];
        let mut model = model(rig(), 1, vec![clip]);
        set_skinned_local_bounds(&mut model, &paths());
        assert!(
            model.nodes[0]
                .renderer
                .as_ref()
                .unwrap()
                .update_when_offscreen
        );
    }
}
