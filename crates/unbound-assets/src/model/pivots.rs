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
//! A node whose mesh is skinned by bones is the exception: its rotation and scaling pivots are
//! dropped from its translation and its rotation offset is ignored, so it sits at `T'` (see
//! [`Placement::position`]) plus `R·S·c` with `c = -ScalingOffset`; its vertices take `G` and
//! the shift `-c`, its children move by `-c` like any node's, and its bind poses keep the
//! skinned vertices where the bones put them (measured: tests/projects/modelaxis `Skinned`
//! MeshPivot and the `Rigged` Mesh* controls, with unequal pivots, each offset and children).
//!
//! The Editor bakes the mesh part only for a node that names its attribute
//! (`DefaultAttributeIndex`, which exporters always write); without it the vertices stay as
//! they are while the node still takes its pivots.
use super::ModelNode;

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
    /// A bone-skinned mesh's node: without its rotation and scaling pivots and rotation offset,
    /// shifted by `c = -scaling_offset`.
    WithoutPivots {
        rotation_pivot: ufbx::Vec3,
        scaling_pivot: ufbx::Vec3,
        scaling_offset: ufbx::Vec3,
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
                scaling_offset: vec3_prop(node, "ScalingOffset"),
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
            Self::WithoutPivots { scaling_offset, .. } => sub(ZERO, scaling_offset),
        }
    }

    pub fn shifts_children(&self) -> bool {
        !is_zero(self.offset())
    }

    /// The position depends on the node's rotation and scale, not only its translation.
    pub fn moves(&self) -> bool {
        match *self {
            Self::At(c) => !is_zero(c),
            Self::WithoutPivots {
                rotation_pivot,
                scaling_pivot,
                scaling_offset,
            } => !is_zero(rotation_pivot) || !is_zero(scaling_pivot) || !is_zero(scaling_offset),
        }
    }

    /// The GameObject's local position (FBX space) for the node transform `t`, its parent
    /// placed with offset `parent`.
    pub fn position(&self, t: &ufbx::Transform, parent: ufbx::Vec3) -> ufbx::Vec3 {
        let p = match *self {
            Self::At(c) => transformed(t, c),
            // M·0 = T + Roff + Rp + R·(Soff - Rp + Sp - S·Sp); drop the pivot terms.
            // T' = T - Rp + R·(Rp - Sp + S·Sp), then T' + R·S·c.
            Self::WithoutPivots {
                rotation_pivot,
                scaling_pivot,
                ..
            } => {
                let rotated = |v| ufbx::quat_rotate_vec3(t.rotation, v);
                let p = sub(t.translation, rotation_pivot);
                let p = add(p, rotated(rotation_pivot));
                let p = sub(p, rotated(scaling_pivot));
                let p = add(p, sub(transformed(t, scaling_pivot), t.translation));
                add(p, sub(transformed(t, self.offset()), t.translation))
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

pub(super) fn matrix(node: &ModelNode) -> ufbx::Matrix {
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
            scaling_offset: ZERO,
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
}
