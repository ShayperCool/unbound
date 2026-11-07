//! Static FBX coordinate conversion measured by `tests/projects/modelaxis` on 6000.0.71f1.
use super::{ModelMesh, ModelNode};

/// A signed axis permutation, applied after the importer's existing X reflection.
pub(super) struct StaticAxes {
    source: [usize; 3],
    sign: [f32; 3],
    determinant: f32,
}

impl StaticAxes {
    pub fn new(axes: ufbx::CoordinateAxes, baked: bool) -> anyhow::Result<Self> {
        anyhow::ensure!(
            ufbx::coordinate_axes_valid(axes),
            "static FBX axis conversion requires valid coordinate-axis metadata"
        );
        // ufbx's `front` is the direction towards the viewer. Unity's Z-forward,
        // left-handed target therefore has front = -Z.
        let components = [axes.right as usize, axes.up as usize, axes.front as usize];
        let source = components.map(|v| v / 2);
        let sign = std::array::from_fn(|i| {
            let source_sign = if components[i] % 2 == 0 { 1.0 } else { -1.0 };
            let target_sign = if baked && i == 2 { -1.0 } else { 1.0 };
            let reflected = if source[i] == 0 { -1.0 } else { 1.0 };
            let output_reflection = if !baked && i == 0 { -1.0 } else { 1.0 };
            source_sign * target_sign * reflected * output_reflection
        });
        let inversions = (source[0] > source[1]) as usize
            + (source[0] > source[2]) as usize
            + (source[1] > source[2]) as usize;
        let determinant =
            sign.iter().product::<f32>() * if inversions % 2 == 0 { 1.0 } else { -1.0 };
        Ok(Self {
            source,
            sign,
            determinant,
        })
    }

    fn vector(&self, v: [f32; 3]) -> [f32; 3] {
        std::array::from_fn(|i| v[self.source[i]] * self.sign[i])
    }

    pub fn baked_node(&self, node: &mut ModelNode) {
        node.position = self.vector(node.position);
        node.scale = std::array::from_fn(|i| node.scale[self.source[i]]);
        // C R C^-1: the quaternion's vector part is an axial vector, so reflections
        // contribute the determinant in addition to the signed permutation.
        let q = self.vector([node.rotation[0], node.rotation[1], node.rotation[2]]);
        for (i, value) in q.into_iter().enumerate() {
            node.rotation[i] = value * self.determinant;
        }
    }

    pub fn mesh(&self, mesh: &mut ModelMesh) {
        for p in &mut mesh.positions {
            *p = self.vector(*p);
        }
        for n in &mut mesh.normals {
            *n = self.vector(*n);
        }
        for t in &mut mesh.tangents {
            let v = self.vector([t[0], t[1], t[2]]);
            *t = [v[0], v[1], v[2], t[3] * self.determinant];
        }
        let a = self.vector(mesh.bounds_min);
        let b = self.vector(mesh.bounds_max);
        mesh.bounds_min = std::array::from_fn(|i| a[i].min(b[i]));
        mesh.bounds_max = std::array::from_fn(|i| a[i].max(b[i]));
        // Unity retains the importer's triangle index order in both measured bake modes.
        // Vertex order, material slots and UV channels are also unchanged.
    }
}

impl StaticAxes {
    pub fn reverses_handedness(&self) -> bool {
        self.determinant < 0.0
    }

    /// Unity's measured unbaked improper-axis conversion uses the negated axis
    /// matrix as the top-level rotation, without introducing negative scales.
    /// Translation uses the axis matrix itself; descendants stay mesh-local.
    pub fn unbaked_node(&self, node: &mut ModelNode) {
        node.position = self.vector(node.position);
        let mut matrix = ufbx::Matrix::default();
        // -C is a proper rotation when C reverses handedness.
        let columns = [
            self.vector([1.0, 0.0, 0.0]),
            self.vector([0.0, 1.0, 0.0]),
            self.vector([0.0, 0.0, 1.0]),
        ];
        matrix.m00 = -columns[0][0] as f64;
        matrix.m10 = -columns[0][1] as f64;
        matrix.m20 = -columns[0][2] as f64;
        matrix.m01 = -columns[1][0] as f64;
        matrix.m11 = -columns[1][1] as f64;
        matrix.m21 = -columns[1][2] as f64;
        matrix.m02 = -columns[2][0] as f64;
        matrix.m12 = -columns[2][1] as f64;
        matrix.m22 = -columns[2][2] as f64;
        let [x, y, z, w] = node.rotation.map(f64::from);
        let q = ufbx::quat_mul(
            ufbx::matrix_to_transform(&matrix).rotation,
            ufbx::Quat { x, y, z, w },
        );
        node.rotation = [q.x as f32, q.y as f32, q.z as f32, q.w as f32];
    }
}
