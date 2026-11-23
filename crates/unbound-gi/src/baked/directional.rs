//! The direction light comes to a texel from, for directional lightmaps (the Lighting
//! Settings' Directional Mode): the light's directions weighed by their luminance, and how
//! much of it the texel's surface faces (matches the Unity 6000.0.71f1 Editor's Progressive
//! CPU lightmapper, tests/projects/bakedbuffers `Directional*`).

/// The luminance a direction is weighed by.
fn luminance(c: [f32; 3]) -> f32 {
    c[0] * 0.22 + c[1] * 0.707 + c[2] * 0.071
}

/// What a texel takes from where: the directions light arrives from, each times the light's
/// luminance, summed, and the luminance summed.
#[derive(Clone, Copy, Debug, Default, PartialEq)]
pub struct Toward {
    sum: [f32; 3],
    weight: f32,
}

/// What a texel without light, or a bake without directional lightmaps, holds.
pub const NONE: [f32; 4] = [0.5; 4];

impl Toward {
    /// Light of `color` that arrives from `direction` (towards where it comes from, of unit
    /// length).
    pub fn add(&mut self, direction: [f32; 3], color: [f32; 3]) {
        let weight = luminance(color);
        for (k, d) in direction.iter().enumerate() {
            self.sum[k] += d * weight;
        }
        self.weight += weight;
    }

    /// The texel's value: the mean direction (of length up to 1, shorter where light comes
    /// from several sides) mapped from -1..1 to 0..1, and how much the texel's samples face it,
    /// the mean over its samples of their normals' cosine with it, mapped the same way.
    /// `normals` is the sum of the texel's samples' normals, `samples` their number.
    pub fn encode(&self, normals: [f32; 3], samples: u32) -> [f32; 4] {
        if !(self.weight > 0.0) || samples == 0 {
            return NONE;
        }
        let mean = self.sum.map(|v| v / self.weight);
        let facing =
            (normals[0] * mean[0] + normals[1] * mean[1] + normals[2] * mean[2]) / samples as f32;
        [
            mean[0] * 0.5 + 0.5,
            mean[1] * 0.5 + 0.5,
            mean[2] * 0.5 + 0.5,
            facing * 0.5 + 0.5,
        ]
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn near(a: [f32; 4], b: [f32; 4]) -> bool {
        a.iter().zip(&b).all(|(x, y)| (x - y).abs() < 1e-6)
    }

    #[test]
    fn a_texel_without_light_holds_the_middle_values() {
        assert_eq!(Toward::default().encode([0.0, 2.0, 0.0], 2), NONE);
        let mut black = Toward::default();
        black.add([0.0, 1.0, 0.0], [0.0; 3]);
        assert_eq!(black.encode([0.0, 2.0, 0.0], 2), NONE);
        // No sample with a surface.
        let mut lit = Toward::default();
        lit.add([0.0, 1.0, 0.0], [1.0; 3]);
        assert_eq!(lit.encode([0.0; 3], 0), NONE);
    }

    #[test]
    fn light_from_one_side_gives_its_direction_and_how_much_the_samples_face_it() {
        let mut toward = Toward::default();
        let slanted = [0.6, 0.8, 0.0];
        toward.add(slanted, [1.0, 0.5, 0.25]);
        // Two samples facing up and one facing the light: their mean cosine with it.
        let normals = [0.6 + 0.0 + 0.0, 0.8 + 1.0 + 1.0, 0.0];
        let facing = (1.0 + 0.8 + 0.8) / 3.0;
        assert!(near(
            toward.encode(normals, 3),
            [0.8, 0.9, 0.5, facing * 0.5 + 0.5]
        ));
    }

    #[test]
    fn directions_are_weighed_by_the_lights_luminance() {
        let mut toward = Toward::default();
        // Green weighs most: its light pulls the mean towards +x.
        toward.add([1.0, 0.0, 0.0], [0.0, 1.0, 0.0]);
        toward.add([-1.0, 0.0, 0.0], [1.0, 0.0, 0.0]);
        let mean = (0.707 - 0.22) / (0.707 + 0.22);
        let value = toward.encode([0.0, 1.0, 0.0], 1);
        assert!(near(value, [mean * 0.5 + 0.5, 0.5, 0.5, 0.5]), "{value:?}");
        // Equal light from opposite sides cancels: the mean direction has no length.
        let mut even = Toward::default();
        even.add([0.0, 0.0, 1.0], [1.0; 3]);
        even.add([0.0, 0.0, -1.0], [1.0; 3]);
        assert!(near(even.encode([0.0, 0.0, 1.0], 1), [0.5; 4]));
    }
}
