//! FBX node pivots and geometric transforms as the Unity 6000.0.71f1 importer bakes them
//! (tests/projects/modelaxis, `Pivots` and `Skinned` sets).
//!
//! A node keeps its rotation and scale. Its GameObject sits at `M·c`, where `M` is the FBX
//! node-to-parent matrix (with pivots, offsets and pre/post rotation) and `c` is the node's
//! rotation pivot minus its scaling offset; its children's positions move by `-c`. The node's
//! mesh takes the geometric transform `G` and the same shift: `v' = G·v - c`. Because
//! `M·v = M·c + L·(v - c)` for the linear part `L` of an affine `M`, this keeps every vertex and
//! child where the FBX places it, for any `c`. Normals take only the rotation of `G`, not its
//! scale; a negative determinant negates all three scale axes (see `rotation_of`). One geometry shared by several
//! nodes is baked once, with the offset of its first node in import order. Inherit types other
//! than RSrs are not reproduced: the Editor ignores them too.
//!
//! A node whose mesh is skinned by bones is the exception: it sits at its transform without
//! its pivots (measured with equal rotation and scaling pivots, `MeshPivot`), and its vertices
//! take only `G`; its bind poses keep the skinned vertices where the bones put them.
//!
//! The Editor bakes the mesh part only for a node that names its attribute
//! (`DefaultAttributeIndex`, which exporters always write); without it the vertices stay as
//! they are while the node still takes its pivots.
use super::{ModelMesh, ModelNode, ModelRenderer};

pub(super) const ZERO: ufbx::Vec3 = ufbx::Vec3 {
    x: 0.0,
    y: 0.0,
    z: 0.0,
};

fn vec3_prop(node: &ufbx::Node, name: &str) -> ufbx::Vec3 {
    ufbx::find_prop(&node.element.props, name).map_or(ZERO, |p| ufbx::Vec3 {
        x: p.value_vec4.x,
        y: p.value_vec4.y,
        z: p.value_vec4.z,
    })
}

fn sub(a: ufbx::Vec3, b: ufbx::Vec3) -> ufbx::Vec3 {
    ufbx::Vec3 {
        x: a.x - b.x,
        y: a.y - b.y,
        z: a.z - b.z,
    }
}

fn add(a: ufbx::Vec3, b: ufbx::Vec3) -> ufbx::Vec3 {
    ufbx::Vec3 {
        x: a.x + b.x,
        y: a.y + b.y,
        z: a.z + b.z,
    }
}

fn is_zero(v: ufbx::Vec3) -> bool {
    v.x == 0.0 && v.y == 0.0 && v.z == 0.0
}

/// `M·p` for the node transform `t` (`M·0` is its translation).
fn transformed(t: &ufbx::Transform, p: ufbx::Vec3) -> ufbx::Vec3 {
    let scaled = ufbx::Vec3 {
        x: p.x * t.scale.x,
        y: p.y * t.scale.y,
        z: p.z * t.scale.z,
    };
    add(t.translation, ufbx::quat_rotate_vec3(t.rotation, scaled))
}

/// Where a node's GameObject is placed relative to its FBX transform.
#[derive(Clone, Copy)]
pub(super) enum Placement {
    /// At `M·c`; children and the mesh move by `-c`.
    At(ufbx::Vec3),
    /// A bone-skinned mesh's node: at `M` without its rotation and scaling pivots.
    WithoutPivots {
        rotation_pivot: ufbx::Vec3,
        scaling_pivot: ufbx::Vec3,
    },
}

impl Placement {
    pub fn of(node: &ufbx::Node) -> Self {
        if node.is_root {
            return Self::At(ZERO);
        }
        let skinned = node
            .mesh
            .as_ref()
            .is_some_and(|m| m.skin_deformers.iter().any(|s| !s.clusters.is_empty()));
        if skinned {
            return Self::WithoutPivots {
                rotation_pivot: vec3_prop(node, "RotationPivot"),
                scaling_pivot: vec3_prop(node, "ScalingPivot"),
            };
        }
        Self::At(sub(
            vec3_prop(node, "RotationPivot"),
            vec3_prop(node, "ScalingOffset"),
        ))
    }

    /// `c`: how far its children and mesh move back.
    pub fn offset(&self) -> ufbx::Vec3 {
        match *self {
            Self::At(c) => c,
            Self::WithoutPivots { .. } => ZERO,
        }
    }

    pub fn shifts_children(&self) -> bool {
        !is_zero(self.offset())
    }

    /// What about a skinned mesh's pivoted node is unmeasured: its children's placement, and
    /// pivots that differ or offsets (measured: equal rotation and scaling pivots, no offsets).
    pub fn unmeasured(&self, node: &ufbx::Node) -> Option<&'static str> {
        let Self::WithoutPivots {
            rotation_pivot,
            scaling_pivot,
        } = *self
        else {
            return None;
        };
        let offsets = !is_zero(vec3_prop(node, "RotationOffset"))
            || !is_zero(vec3_prop(node, "ScalingOffset"));
        let differ = rotation_pivot.x != scaling_pivot.x
            || rotation_pivot.y != scaling_pivot.y
            || rotation_pivot.z != scaling_pivot.z;
        let children = self.moves() && !node.children.is_empty();
        match (offsets || differ, children) {
            (true, true) => Some(
                "its pivots differ or it has offsets, and its children keep their FBX translation",
            ),
            (true, false) => Some("its pivots differ or it has offsets"),
            (false, true) => Some("its children keep their FBX translation"),
            (false, false) => None,
        }
    }

    /// The position depends on the node's rotation and scale, not only its translation.
    pub fn moves(&self) -> bool {
        match *self {
            Self::At(c) => !is_zero(c),
            Self::WithoutPivots {
                rotation_pivot,
                scaling_pivot,
            } => !is_zero(rotation_pivot) || !is_zero(scaling_pivot),
        }
    }

    /// The GameObject's local position (FBX space) for the node transform `t`, its parent
    /// placed with offset `parent`.
    pub fn position(&self, t: &ufbx::Transform, parent: ufbx::Vec3) -> ufbx::Vec3 {
        let p = match *self {
            Self::At(c) => transformed(t, c),
            // M·0 = T + Roff + Rp + R·(Soff - Rp + Sp - S·Sp); drop the pivot terms.
            Self::WithoutPivots {
                rotation_pivot,
                scaling_pivot,
            } => {
                let rotated = |v| ufbx::quat_rotate_vec3(t.rotation, v);
                let p = sub(t.translation, rotation_pivot);
                let p = add(p, rotated(rotation_pivot));
                let p = sub(p, rotated(scaling_pivot));
                add(p, sub(transformed(t, scaling_pivot), t.translation))
            }
        };
        sub(p, parent)
    }
}

/// What the importer bakes into one geometry: `G` and the node's `-c` (FBX space).
#[derive(Clone)]
pub(super) struct GeometryOffset {
    /// `T(-c)·G`.
    matrix: ufbx::Matrix,
    /// The rotation normals and tangents take.
    rotation: ufbx::Quat,
    identity: bool,
}

impl GeometryOffset {
    pub fn identity() -> Self {
        Self {
            matrix: ufbx::Matrix {
                m00: 1.0,
                m11: 1.0,
                m22: 1.0,
                ..Default::default()
            },
            rotation: ufbx::Quat {
                x: 0.0,
                y: 0.0,
                z: 0.0,
                w: 1.0,
            },
            identity: true,
        }
    }

    /// The offset of `node`'s geometry, the node placed with offset `c`.
    pub fn of(node: &ufbx::Node, c: ufbx::Vec3) -> Self {
        let named = ufbx::find_prop(&node.element.props, "DefaultAttributeIndex")
            .is_some_and(|p| p.value_int >= 0);
        if !named || (!node.has_geometry_transform && is_zero(c)) {
            return Self::identity();
        }
        let mut matrix = node.geometry_to_node;
        matrix.m03 -= c.x;
        matrix.m13 -= c.y;
        matrix.m23 -= c.z;
        Self {
            rotation: rotation_of(&node.geometry_to_node),
            matrix,
            identity: false,
        }
    }

    pub fn is_identity(&self) -> bool {
        self.identity
    }

    pub fn point(&self, v: ufbx::Vec3) -> ufbx::Vec3 {
        if self.identity {
            v
        } else {
            ufbx::transform_position(&self.matrix, v)
        }
    }

    /// A blend shape's position delta.
    pub fn delta(&self, v: ufbx::Vec3) -> ufbx::Vec3 {
        if self.identity {
            v
        } else {
            ufbx::transform_direction(&self.matrix, v)
        }
    }

    /// A normal, tangent or normal delta.
    pub fn direction(&self, v: ufbx::Vec3) -> ufbx::Vec3 {
        if self.identity {
            v
        } else {
            ufbx::quat_rotate_vec3(self.rotation, v)
        }
    }

    /// A geometry-to-bone matrix for the baked vertices.
    pub fn bind(&self, geometry_to_bone: &ufbx::Matrix) -> ufbx::Matrix {
        if self.identity {
            *geometry_to_bone
        } else {
            ufbx::matrix_mul(geometry_to_bone, &ufbx::matrix_invert(&self.matrix))
        }
    }
}

/// The rotation of `m`'s linear part: its columns normalized, all three negated when the
/// determinant is negative (measured: GeometricScaling (-1, 1.5, 1) turns normals by 180 degrees
/// about X, tests/projects/modelaxis GeoNegS).
fn rotation_of(m: &ufbx::Matrix) -> ufbx::Quat {
    let columns = [
        [m.m00, m.m10, m.m20],
        [m.m01, m.m11, m.m21],
        [m.m02, m.m12, m.m22],
    ];
    let [a, b, c] = columns;
    let det = a[0] * (b[1] * c[2] - b[2] * c[1]) - b[0] * (a[1] * c[2] - a[2] * c[1])
        + c[0] * (a[1] * b[2] - a[2] * b[1]);
    let sign = if det < 0.0 { -1.0 } else { 1.0 };
    let unit = columns.map(|v| {
        let length = (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]).sqrt() * sign;
        if length == 0.0 {
            v
        } else {
            v.map(|x| x / length)
        }
    });
    let rotation = ufbx::Matrix {
        m00: unit[0][0],
        m10: unit[0][1],
        m20: unit[0][2],
        m01: unit[1][0],
        m11: unit[1][1],
        m21: unit[1][2],
        m02: unit[2][0],
        m12: unit[2][1],
        m22: unit[2][2],
        ..Default::default()
    };
    ufbx::matrix_to_transform(&rotation).rotation
}

fn matrix(node: &ModelNode) -> ufbx::Matrix {
    let [x, y, z, w] = node.rotation.map(f64::from);
    let [tx, ty, tz] = node.position.map(f64::from);
    let [sx, sy, sz] = node.scale.map(f64::from);
    ufbx::transform_to_matrix(&ufbx::Transform {
        translation: ufbx::Vec3 {
            x: tx,
            y: ty,
            z: tz,
        },
        rotation: ufbx::Quat { x, y, z, w },
        scale: ufbx::Vec3 {
            x: sx,
            y: sy,
            z: sz,
        },
    })
}

/// Each node's world matrix (Unity space), parents first in `nodes`.
pub(super) fn world_matrices(nodes: &[ModelNode]) -> Vec<ufbx::Matrix> {
    let mut world: Vec<ufbx::Matrix> = Vec::with_capacity(nodes.len());
    for node in nodes {
        let local = matrix(node);
        let matrix = match node.parent {
            Some(p) => ufbx::matrix_mul(&world[p as usize], &local),
            None => local,
        };
        world.push(matrix);
    }
    world
}

/// No clip moves a node away from its import transform: each transform curve of `curves` holds
/// the imported position, rotation and scale of a node at its path (`paths`: every node index
/// per path; names may repeat).
pub(super) fn poses_are_import_pose<'a>(
    nodes: &[ModelNode],
    paths: &std::collections::HashMap<String, Vec<u32>>,
    mut curves: impl Iterator<Item = &'a super::NodeCurves>,
) -> bool {
    // Keys and import transforms come from the same f32 conversion; this allows rounding only
    // (a 0.1 degree turn changes a quaternion component by ~9e-4).
    const TOLERANCE: f32 = 1e-5;
    let near = |a: &[f32], b: &[f32]| {
        a.iter()
            .zip(b)
            .all(|(x, y)| (x - y).abs() <= TOLERANCE * y.abs().max(1.0))
    };
    let holds = |curve: &super::NodeCurves, node: &ModelNode| {
        let q = node.rotation;
        let flipped = q.map(|v| -v);
        curve
            .positions
            .iter()
            .all(|k| near(&k.value, &node.position))
            && curve.scales.iter().all(|k| near(&k.value, &node.scale))
            && curve
                .rotations
                .iter()
                .all(|k| near(&k.value, &q) || near(&k.value, &flipped))
    };
    curves.all(|curve| {
        paths.get(&curve.path).is_some_and(|indices| {
            indices
                .iter()
                .filter_map(|&i| nodes.get(i as usize))
                .any(|node| holds(curve, node))
        })
    })
}

/// Every skinned renderer's local bounds in its final (axis-converted) hierarchy.
pub(super) fn set_skinned_local_bounds(model: &mut super::ImportedModel) {
    let world = world_matrices(&model.nodes);
    for node in &mut model.nodes {
        if let Some(renderer) = &mut node.renderer
            && renderer.kind == super::RendererKind::SkinnedMesh
            && let Some(mesh) = model.meshes.get(renderer.mesh as usize)
        {
            renderer.local_bounds = skinned_local_bounds(renderer, mesh, &world);
        }
    }
}

/// An axis-aligned box, empty until a point is added.
#[derive(Clone, Copy)]
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
}

fn determinant(m: &ufbx::Matrix) -> f64 {
    m.m00 * (m.m11 * m.m22 - m.m12 * m.m21) - m.m01 * (m.m10 * m.m22 - m.m12 * m.m20)
        + m.m02 * (m.m10 * m.m21 - m.m11 * m.m20)
}

/// The imported `SkinnedMeshRenderer.localBounds` (centre, extents) in the root bone's space:
/// per bone, the box of the vertices it influences (including their blend shape frame
/// positions) in its bind space, carried as a box into the root bone's space by the bone's
/// import pose; the union over bones. Bones without influenced vertices add nothing. None for
/// a degenerate (zero-scale) root bone.
pub(super) fn skinned_local_bounds(
    renderer: &ModelRenderer,
    mesh: &ModelMesh,
    world: &[ufbx::Matrix],
) -> Option<([f32; 3], [f32; 3])> {
    let root = world.get(renderer.root_bone? as usize)?;
    let det = determinant(root);
    if !det.is_finite() || det.abs() < f64::MIN_POSITIVE {
        return None;
    }
    let to_root = ufbx::matrix_invert(root);
    let binds: Vec<ufbx::Matrix> = mesh
        .bind_poses
        .iter()
        .map(|b| {
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
        })
        .collect();
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
            let point = ufbx::Vec3 {
                x: point[0].into(),
                y: point[1].into(),
                z: point[2].into(),
            };
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
        b.carry_into(&ufbx::matrix_mul(&to_root, bone_world), &mut out);
    }
    if out.is_empty() || out.lo.iter().chain(&out.hi).any(|v| !v.is_finite()) {
        return None;
    }
    Some((
        std::array::from_fn(|k| ((out.lo[k] + out.hi[k]) * 0.5) as f32),
        std::array::from_fn(|k| ((out.hi[k] - out.lo[k]) * 0.5) as f32),
    ))
}

#[cfg(test)]
mod tests {
    use super::super::*;
    use super::*;

    /// A tetrahedron geometry with flat per-corner normals (one face's normal is oblique).
    fn tetra(id: u32) -> String {
        format!(
            r#"	Geometry: {id}, "Geometry::Tetra{id}", "Mesh" {{
		Vertices: *12 {{
			a: 0,0,0,2,0,0,0,3,0,0,0,4
		}}
		PolygonVertexIndex: *12 {{
			a: 0,2,-2,0,1,-4,0,3,-3,1,2,-4
		}}
		LayerElementNormal: 0 {{
			Version: 102
			MappingInformationType: "ByPolygonVertex"
			ReferenceInformationType: "Direct"
			Normals: *36 {{
				a: 0,0,-1,0,0,-1,0,0,-1,0,-1,0,0,-1,0,0,-1,0,-1,0,0,-1,0,0,-1,0,0,0.76822128,0.51214752,0.38411064,0.76822128,0.51214752,0.38411064,0.76822128,0.51214752,0.38411064
			}}
		}}
		Layer: 0 {{
			Version: 100
			LayerElement: {{
				Type: "LayerElementNormal"
				TypedIndex: 0
			}}
		}}
	}}
"#
        )
    }

    fn node(id: u32, name: &str, props: &str) -> String {
        format!(
            r#"	Model: {id}, "Model::{name}", "Mesh" {{
		Version: 232
		Properties70:  {{
			P: "DefaultAttributeIndex", "int", "Integer", "",0
{props}		}}
	}}
"#
        )
    }

    /// Nodes `(id, name, properties, parent, geometry)` under one file.
    fn import(nodes: &[(u32, &str, &str, u32, u32)]) -> ImportedModel {
        let mut geometries: Vec<u32> = nodes.iter().map(|n| n.4).collect();
        geometries.sort();
        geometries.dedup();
        // Y-up, right-handed, centimetres.
        let mut text = String::from(
            "; FBX 7.4.0 project file\nGlobalSettings:  {\n\tVersion: 1000\n\tProperties70:  {\n\
             \t\tP: \"UpAxis\", \"int\", \"Integer\", \"\",1\n\t\tP: \"UpAxisSign\", \"int\", \"Integer\", \"\",1\n\
             \t\tP: \"FrontAxis\", \"int\", \"Integer\", \"\",2\n\t\tP: \"FrontAxisSign\", \"int\", \"Integer\", \"\",1\n\
             \t\tP: \"CoordAxis\", \"int\", \"Integer\", \"\",0\n\t\tP: \"CoordAxisSign\", \"int\", \"Integer\", \"\",1\n\
             \t\tP: \"UnitScaleFactor\", \"double\", \"Number\", \"\",1\n\t}\n}\nObjects:  {\n",
        );
        for &g in &geometries {
            text += &tetra(g);
        }
        let mut links = String::new();
        for &(id, name, props, parent, geometry) in nodes {
            text += &node(id, name, props);
            links += &format!("\tC: \"OO\",{id},{parent}\n\tC: \"OO\",{geometry},{id}\n");
        }
        text += &format!("}}\nConnections:  {{\n{links}}}\n");
        let meta = unbound_yaml::parse_value(
            "ModelImporter:\n  meshes:\n    meshOptimizationFlags: 0\n    preserveHierarchy: 1\n  tangentSpace:\n    normalImportMode: 0\n",
        )
        .unwrap();
        ModelImporter::default()
            .import(Path::new("Pivots.fbx"), text.as_bytes(), &meta)
            .unwrap()
    }

    fn mesh<'a>(model: &'a ImportedModel, node: &str) -> &'a ModelMesh {
        let n = model.nodes.iter().find(|n| n.name == node).unwrap();
        &model.meshes[n.renderer.as_ref().unwrap().mesh as usize]
    }

    fn near(a: [f32; 3], b: [f32; 3]) -> bool {
        a.iter().zip(&b).all(|(x, y)| (x - y).abs() < 1e-5)
    }

    #[test]
    fn geometric_transforms_move_vertices_and_rotate_normals_only() {
        // G = T(0.5,-1,2) R(0,0,90) S(2,0.5,1.5): vertex (2,0,0) -> (0.5,3,2) cm; the oblique
        // normal turns by 90 degrees about Z and is not rescaled (tests/projects/modelaxis GeoS).
        let model = import(&[
            (
                200,
                "Geo",
                "\t\t\tP: \"GeometricTranslation\", \"Vector3D\", \"Vector\", \"\",0.5,-1,2\n\
                 \t\t\tP: \"GeometricRotation\", \"Vector3D\", \"Vector\", \"\",0,0,90\n\
                 \t\t\tP: \"GeometricScaling\", \"Vector3D\", \"Vector\", \"\",2,0.5,1.5\n",
                0,
                100,
            ),
            (201, "Child", "", 200, 101),
        ]);
        let geo = mesh(&model, "Geo");
        assert!(
            near(geo.positions[1], [-0.005, 0.03, 0.02]),
            "{:?}",
            geo.positions[1]
        );
        let n = geo.normals[9];
        assert!(near(n, [0.51214752, 0.76822128, 0.38411064]), "{n:?}");
        // The child neither moves nor takes the geometric transform.
        let child = model.nodes.iter().find(|n| n.name == "Child").unwrap();
        assert!(near(child.position, [0.0, 0.0, 0.0]));
        assert!(near(mesh(&model, "Child").positions[1], [-0.02, 0.0, 0.0]));
    }

    #[test]
    fn shared_geometry_takes_its_first_nodes_offset() {
        let geometric = "\t\t\tP: \"GeometricTranslation\", \"Vector3D\", \"Vector\", \"\",0,0,1\n";
        let first = import(&[(200, "A", geometric, 0, 100), (201, "B", "", 0, 100)]);
        let second = import(&[(200, "A", "", 0, 100), (201, "B", geometric, 0, 100)]);
        assert_eq!(mesh(&first, "B").positions, mesh(&first, "A").positions);
        assert!(near(mesh(&first, "B").positions[0], [0.0, 0.0, 0.01]));
        // No helper node: the later instance's geometric transform is dropped.
        assert_eq!(second.nodes.len(), 3);
        assert!(near(mesh(&second, "B").positions[0], [0.0, 0.0, 0.0]));
    }

    #[test]
    fn negative_geometric_scale_keeps_the_winding() {
        let plain = import(&[(200, "A", "", 0, 100)]);
        let negative = import(&[(
            200,
            "A",
            "\t\t\tP: \"GeometricScaling\", \"Vector3D\", \"Vector\", \"\",-1,1.5,1\n",
            0,
            100,
        )]);
        assert_eq!(mesh(&negative, "A").submeshes, mesh(&plain, "A").submeshes);
        assert!(near(mesh(&negative, "A").positions[1], [0.02, 0.0, 0.0]));
        // Normals take the decomposed rotation (180 degrees about X), not the reflection.
        assert!(near(mesh(&negative, "A").normals[0], [0.0, 0.0, 1.0]));
    }

    #[test]
    fn the_scaling_offset_places_the_node_and_its_children() {
        // c = Rp - Soff = (2,-1,-0.25): node at M·c, children at -c, vertices at v - c.
        let model = import(&[
            (
                200,
                "A",
                "\t\t\tP: \"ScalingOffset\", \"Vector3D\", \"Vector\", \"\",-2,1,0.25\n\
                 \t\t\tP: \"Lcl Scaling\", \"Lcl Scaling\", \"\", \"A\",2,2,2\n",
                0,
                100,
            ),
            (201, "B", "", 200, 101),
        ]);
        let a = model.nodes.iter().find(|n| n.name == "A").unwrap();
        // M·c = Soff + S·c = (-2,1,0.25) + (4,-2,-0.5) = (2,-1,-0.25) cm.
        assert!(
            near(a.position, [-0.02, -0.01, -0.0025]),
            "{:?}",
            a.position
        );
        let b = model.nodes.iter().find(|n| n.name == "B").unwrap();
        assert!(near(b.position, [0.02, 0.01, 0.0025]), "{:?}", b.position);
        assert!(near(mesh(&model, "A").positions[0], [0.02, 0.01, 0.0025]));
    }

    #[test]
    fn a_skinned_mesh_node_drops_its_pivots() {
        // T (1.5, 0.5, -2), 20 degrees about Z, pivots (0.5, 1, 0): placed at T
        // (tests/projects/modelaxis MeshPivot), not at M·Rp.
        let (sin, cos) = 10f64.to_radians().sin_cos();
        let rotation = ufbx::Quat {
            x: 0.0,
            y: 0.0,
            z: sin,
            w: cos,
        };
        let pivot = ufbx::Vec3 {
            x: 0.5,
            y: 1.0,
            z: 0.0,
        };
        let t = ufbx::Vec3 {
            x: 1.5,
            y: 0.5,
            z: -2.0,
        };
        // ufbx's translation: M·0 = T + Rp - R·Rp.
        let translation = add(
            add(t, pivot),
            ufbx::quat_rotate_vec3(
                rotation,
                ufbx::Vec3 {
                    x: -0.5,
                    y: -1.0,
                    z: 0.0,
                },
            ),
        );
        let transform = ufbx::Transform {
            translation,
            rotation,
            scale: ufbx::Vec3 {
                x: 1.0,
                y: 1.0,
                z: 1.0,
            },
        };
        let placement = Placement::WithoutPivots {
            rotation_pivot: pivot,
            scaling_pivot: pivot,
        };
        let p = placement.position(&transform, ZERO);
        assert!(
            (p.x - t.x).abs() < 1e-12 && (p.y - t.y).abs() < 1e-12 && (p.z - t.z).abs() < 1e-12,
            "{p:?}"
        );
        assert!(!placement.shifts_children() && placement.moves());
    }

    /// The probe's `AnimPivot` (a legacy clip turning and moving pivoted nodes) with the given
    /// animation compression.
    fn anim_pivot(compression: u32) -> ImportedModel {
        let dir = Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/modelaxis/Assets/Resources/Pivots");
        let source = dir.join("AnimPivot.fbx");
        let meta = std::fs::read_to_string(dir.join("AnimPivot.fbx.meta"))
            .unwrap()
            .replace(
                "animationCompression: 0",
                &format!("animationCompression: {compression}"),
            );
        let meta = unbound_yaml::parse_value(&meta).unwrap();
        ModelImporter::default()
            .import(&source, &std::fs::read(&source).unwrap(), &meta)
            .unwrap()
    }

    #[test]
    fn reduced_clips_keep_pivoted_positions_on_every_frame() {
        // A pivoted node's position follows its rotation along an arc. The keyframe-reduced
        // import, interpolated as clips are, gives the uncompressed (measured) position at every
        // frame, not a chord between sparse keys.
        let curve = |m: &ImportedModel| {
            let clip = m.clips.iter().find(|c| !c.preview).unwrap();
            let body = clip.curves.iter().find(|c| c.path == "Body").unwrap();
            body.positions.clone()
        };
        let (exact, reduced) = (curve(&anim_pivot(0)), curve(&anim_pivot(1)));
        assert!(exact.len() > 20, "{exact:?}");
        for e in &exact {
            let after = reduced
                .iter()
                .position(|k| k.time >= e.time - 1e-6)
                .unwrap();
            let value = if after == 0 || (reduced[after].time - e.time).abs() < 1e-6 {
                reduced[after].value
            } else {
                let (a, b) = (&reduced[after - 1], &reduced[after]);
                let t = (e.time - a.time) / (b.time - a.time);
                std::array::from_fn(|i| a.value[i] + (b.value[i] - a.value[i]) * t)
            };
            assert!(near(value, e.value), "{e:?} {value:?}");
        }
    }

    fn curve(path: &str, position: [f32; 3], rotation: [f32; 4]) -> NodeCurves {
        let key = |value| Key { time: 0.0, value };
        NodeCurves {
            path: path.into(),
            positions: vec![
                key(position),
                Key {
                    time: 1.0,
                    value: position,
                },
            ],
            rotations: vec![Key {
                time: 0.0,
                value: rotation,
            }],
            scales: vec![key([1.0; 3])],
        }
    }

    #[test]
    fn only_clips_holding_the_import_pose_keep_it_as_the_bounds_pose() {
        let nodes = vec![
            node_at(None, [0.0; 3], [0.0, 0.0, 0.0, 1.0]),
            node_at(Some(0), [1.0, 2.0, 3.0], [0.0, 0.0, 0.0, 1.0]),
        ];
        // Two siblings named Bone, each curve holding one of them.
        let nodes = [
            nodes,
            vec![node_at(Some(0), [5.0, 0.0, 0.0], [0.0, 0.0, 0.0, 1.0])],
        ]
        .concat();
        let paths: std::collections::HashMap<String, Vec<u32>> =
            [("".to_string(), vec![0]), ("Bone".to_string(), vec![1, 2])].into();
        let twins = [
            curve("Bone", [1.0, 2.0, 3.0], [0.0, 0.0, 0.0, 1.0]),
            curve("Bone", [5.0, 0.0, 0.0], [0.0, 0.0, 0.0, 1.0]),
        ];
        assert!(poses_are_import_pose(&nodes, &paths, twins.iter()));
        // A 0.1 degree turn is a pose of its own.
        let (sin, cos) = 0.05f32.to_radians().sin_cos();
        let nudged = [curve("Bone", [1.0, 2.0, 3.0], [0.0, 0.0, sin, cos])];
        assert!(!poses_are_import_pose(&nodes, &paths, nudged.iter()));
        let same = [curve("Bone", [1.0, 2.0, 3.0], [0.0, 0.0, 0.0, -1.0])];
        assert!(poses_are_import_pose(&nodes, &paths, same.iter()));
        // A constant pose other than the import pose (a one-frame "crouch" take).
        let other = [curve("Bone", [1.0, 1.0, 3.0], [0.0, 0.0, 0.0, 1.0])];
        assert!(!poses_are_import_pose(&nodes, &paths, other.iter()));
        let turned = [curve("Bone", [1.0, 2.0, 3.0], [0.0, 0.0, 0.5, 0.866])];
        assert!(!poses_are_import_pose(&nodes, &paths, turned.iter()));
        let mut moving = curve("Bone", [1.0, 2.0, 3.0], [0.0, 0.0, 0.0, 1.0]);
        moving.positions[1].value = [1.0, 2.5, 3.0];
        assert!(!poses_are_import_pose(&nodes, &paths, [moving].iter()));
        assert!(!poses_are_import_pose(
            &nodes,
            &paths,
            [curve("Missing", [0.0; 3], [0.0, 0.0, 0.0, 1.0])].iter()
        ));
    }

    #[test]
    fn a_degenerate_root_bone_gives_no_bounds() {
        let mut root = node_at(None, [0.0; 3], [0.0, 0.0, 0.0, 1.0]);
        root.scale = [0.0, 1.0, 1.0];
        let mesh = skinned_column();
        let renderer = skinned_renderer(vec![0], Some(0));
        assert_eq!(
            skinned_local_bounds(&renderer, &mesh, &world_matrices(&[root])),
            None
        );
    }

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
        }
    }

    #[test]
    fn skinned_bounds_are_per_bone_boxes_in_the_root_bones_space() {
        let half = std::f32::consts::FRAC_1_SQRT_2;
        // Root bone at (1,0,0); a child bone turned 90 degrees about Z at (0,1,0) in it.
        let nodes = vec![
            node_at(None, [0.0; 3], [0.0, 0.0, 0.0, 1.0]),
            node_at(Some(0), [1.0, 0.0, 0.0], [0.0, 0.0, 0.0, 1.0]),
            node_at(Some(1), [0.0, 1.0, 0.0], [0.0, 0.0, half, half]),
        ];
        let mesh = skinned_column();
        let renderer = skinned_renderer(vec![1, 2, 0], Some(1));
        let (centre, extents) =
            skinned_local_bounds(&renderer, &mesh, &world_matrices(&nodes)).unwrap();
        // Bone 0 (the root): x 0..1, z 0..3 with the shape. Bone 1: the point (2,1) turned to
        // (-1,2), plus (0,1): at (-1,3).
        let lo = [-1.0, 0.0, 0.0];
        let hi = [1.0, 3.0, 3.0];
        for k in 0..3 {
            assert!(
                (centre[k] - (lo[k] + hi[k]) / 2.0).abs() < 1e-5,
                "{centre:?}"
            );
            assert!(
                (extents[k] - (hi[k] - lo[k]) / 2.0).abs() < 1e-5,
                "{extents:?}"
            );
        }
    }
}
