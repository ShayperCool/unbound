//! Which lights a point of the scene takes at a bounce of the bake's paths: the scene is cut
//! into regions, each listing the point and spot lights that may reach it; a path's surface
//! takes every directional light and the lights of its region, and where its region has more
//! lights than the bounce may test, it picks that many of them, half by their power and half
//! evenly (matches the Unity 6000.0.71f1 Editor's Progressive CPU lightmapper,
//! tests/projects/bakedbuffers `Lamps`).

use super::light::{Kind, Light};

/// The regions' size along x and z, and along y.
const REGION_ACROSS: f32 = 25.0;
const REGION_UP: f32 = 55.0;
/// At most this many regions along an axis.
const MOST_REGIONS: u32 = 20;
/// How far the scene's box reaches beyond its renderers' bounds.
const SCENE_MARGIN: f32 = 0.5;
/// The most lights a bounce tests at its first surface; one fewer at each further one, and
/// at least one.
const MOST_RAYS: u32 = 4;

/// The luminance of a linear colour, summed as the lights' order sums it.
fn order_luminance(c: [f32; 3]) -> f32 {
    c[2] * 0.0722 + c[1] * 0.7152 + c[0] * 0.2126
}

/// The luminance of a linear colour, summed as the lights' power sums it.
fn power_luminance(c: [f32; 3]) -> f32 {
    c[2] * 0.0722 + (c[1] * 0.7152 + c[0] * 0.2126)
}

/// A box: its least and greatest corners.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Bounds {
    pub min: [f32; 3],
    pub max: [f32; 3],
}

impl Bounds {
    /// The box around points; `None` without any.
    pub fn around(points: impl IntoIterator<Item = [f32; 3]>) -> Option<Bounds> {
        let mut bounds: Option<Bounds> = None;
        for p in points {
            let b = bounds.get_or_insert(Bounds { min: p, max: p });
            for k in 0..3 {
                b.min[k] = b.min[k].min(p[k]);
                b.max[k] = b.max[k].max(p[k]);
            }
        }
        bounds
    }
}

/// The lights of one region, as indices into the bake's lights, in the grid's order, and the
/// running sum of their powers, of 0 to 1.
#[derive(Clone, Debug, Default)]
pub struct Region {
    pub lights: Vec<usize>,
    cumulative: Vec<f32>,
    /// One over the sum of the region's powers (one where it is 0).
    sum_reciprocal: f32,
}

/// The bake's light grid.
#[derive(Clone, Debug)]
pub struct Grid {
    min: [f32; 3],
    cell: [f32; 3],
    dims: [u32; 3],
    regions: Vec<Region>,
    /// The directional lights, in the grid's order.
    pub directional: Vec<usize>,
    /// Each light's power (by the bake's light index).
    power: Vec<f32>,
}

/// A light the grid picked for a surface and how much of its light the surface takes.
#[derive(Clone, Copy, Debug, PartialEq)]
pub struct Pick {
    pub light: usize,
    pub weight: f32,
}

/// The order of the grid's lights: kind (spot, directional, point), the spot's cone, the
/// indirect colour's luminance, the colour's, the range; lights alike in all of these keep the
/// bake's order (the Editor tells them apart by an identity of its own, not measured).
fn order(lights: &[Light]) -> Vec<usize> {
    let kind = |l: &Light| match l.kind {
        Kind::Spot => 0,
        Kind::Directional => 1,
        Kind::Point => 2,
    };
    let cone = |l: &Light| match l.kind {
        Kind::Spot => (l.cone_angle * 0.5).cos(),
        _ => 0.0,
    };
    let mut indices: Vec<usize> = (0..lights.len()).collect();
    indices.sort_by(|&a, &b| {
        let (a, b) = (&lights[a], &lights[b]);
        kind(a)
            .cmp(&kind(b))
            .then(cone(a).total_cmp(&cone(b)))
            .then(order_luminance(a.indirect_color).total_cmp(&order_luminance(b.indirect_color)))
            .then(order_luminance(a.color).total_cmp(&order_luminance(b.color)))
            .then(a.range.total_cmp(&b.range))
    });
    indices
}

/// A light's power for picking it: its colour's luminance by how much of the sphere it lights
/// (a point light all of it, a spot light by its cone).
fn power(light: &Light, half_cell_diagonal: f32) -> f32 {
    let luminance = power_luminance(light.color);
    match light.kind {
        Kind::Spot => luminance * ((light.cone_angle * 0.5).cos().acos() * 4.0),
        Kind::Point => luminance * (4.0 * std::f32::consts::PI),
        Kind::Directional => {
            luminance * (std::f32::consts::PI * half_cell_diagonal * half_cell_diagonal)
        }
    }
}

fn dot(a: [f32; 3], b: [f32; 3]) -> f32 {
    a[0] * b[0] + a[1] * b[1] + a[2] * b[2]
}

/// `v` within `low..=high` (not a number stays one).
fn clamp(v: f32, low: f32, high: f32) -> f32 {
    if v < low {
        low
    } else if high < v {
        high
    } else {
        v
    }
}

/// Whether a light may reach a box of the scene: a directional light reaches everything, a
/// point light the boxes its range's sphere meets, a spot light those its range's sphere meets
/// that reach in front of it (its cone is not tested: the built-in pipeline's spot lights,
/// whose cone's falloff a table gives, are listed so; matches the Unity 6000.0.71f1 Editor,
/// tests/projects/bakedbuffers `Spread`).
fn reaches(light: &Light, b: &Bounds) -> bool {
    let p = light.position;
    if light.kind == Kind::Directional || (0..3).all(|k| b.min[k] <= p[k] && p[k] <= b.max[k]) {
        return true;
    }
    if light.kind == Kind::Spot {
        // How far the box reaches along the spot's axis beyond the light's own plane.
        let d = light.forward;
        let half: [f32; 3] = std::array::from_fn(|k| (b.max[k] - b.min[k]) * 0.5);
        let centre: [f32; 3] = std::array::from_fn(|k| half[k] + b.min[k]);
        let behind = (p[2] * d[2] + p[1] * d[1] + d[0] * p[0])
            - (half[2] * d[2].abs() + half[1] * d[1].abs() + half[0] * d[0].abs())
            - (d[2] * centre[2] + d[1] * centre[1] + d[0] * centre[0]);
        if !(behind <= 0.0) {
            return false;
        }
    }
    let nearest: [f32; 3] = std::array::from_fn(|k| clamp(p[k], b.min[k], b.max[k]));
    let to: [f32; 3] = std::array::from_fn(|k| nearest[k] - p[k]);
    dot(to, to) <= light.range * light.range
}

impl Grid {
    /// The grid of a bake's lights over the box around its renderers.
    pub fn new(lights: &[Light], scene: Bounds) -> Grid {
        let scene = Bounds {
            min: scene.min.map(|v| v - SCENE_MARGIN),
            max: scene.max.map(|v| v + SCENE_MARGIN),
        };
        // The box the lights' ranges reach, from the scene's origin on, within the scene's.
        let mut reach = Bounds {
            min: [0.0; 3],
            max: [0.0; 3],
        };
        for light in lights.iter().filter(|l| l.kind != Kind::Directional) {
            for k in 0..3 {
                reach.min[k] = reach.min[k].min(light.position[k] - light.range);
                reach.max[k] = reach.max[k].max(light.position[k] + light.range);
            }
        }
        let overlaps = (0..3).all(|k| {
            (scene.min[k] - reach.min[k]).abs()
                <= (reach.max[k] - reach.min[k]) + (scene.max[k] - scene.min[k])
        });
        let bounds = if overlaps {
            Bounds {
                min: std::array::from_fn(|k| scene.min[k].max(reach.min[k])),
                max: std::array::from_fn(|k| scene.max[k].min(reach.max[k])),
            }
        } else {
            Bounds {
                min: [0.0; 3],
                max: [0.0; 3],
            }
        };
        let size = [REGION_ACROSS, REGION_UP, REGION_ACROSS];
        let dims: [u32; 3] = std::array::from_fn(|k| {
            (((bounds.max[k] - bounds.min[k]) / size[k]).ceil() as u32).clamp(1, MOST_REGIONS)
        });
        let cell: [f32; 3] =
            std::array::from_fn(|k| (bounds.max[k] - bounds.min[k]) / dims[k] as f32);
        let half_diagonal = dot(cell, cell).sqrt() * 0.5;
        let power: Vec<f32> = lights.iter().map(|l| power(l, half_diagonal)).collect();
        let ordered = order(lights);
        let directional = ordered
            .iter()
            .copied()
            .filter(|&i| lights[i].kind == Kind::Directional)
            .collect();
        let mut regions = Vec::with_capacity((dims[0] * dims[1] * dims[2]) as usize);
        for z in 0..dims[2] {
            for y in 0..dims[1] {
                for x in 0..dims[0] {
                    let at = [x, y, z];
                    let min: [f32; 3] =
                        std::array::from_fn(|k| cell[k] * at[k] as f32 + bounds.min[k]);
                    let region = Bounds {
                        min,
                        max: std::array::from_fn(|k| cell[k] + min[k]),
                    };
                    let members: Vec<usize> = ordered
                        .iter()
                        .copied()
                        .filter(|&i| {
                            lights[i].kind != Kind::Directional && reaches(&lights[i], &region)
                        })
                        .collect();
                    regions.push(Region::new(members, &power));
                }
            }
        }
        Grid {
            min: bounds.min,
            cell,
            dims,
            regions,
            directional,
            power,
        }
    }

    /// The region a point is in, if it is in the grid.
    pub fn region(&self, point: [f32; 3]) -> Option<&Region> {
        let mut index = 0usize;
        let mut stride = 1usize;
        for k in 0..3 {
            let at = ((point[k] - self.min[k]) / self.cell[k]) as i64;
            if at < 0 || at >= self.dims[k] as i64 {
                return None;
            }
            index += at as usize * stride;
            stride *= self.dims[k] as usize;
        }
        self.regions.get(index)
    }

    /// The point and spot lights a surface at a bounce (1 at the first surface a path meets)
    /// takes, each with its weight, to `take`: all its region's lights, or where they are more
    /// than the bounce tests, that many picked with the number `pick` of 0..1.
    pub fn lights_at(&self, region: &Region, bounce: u32, pick: f32, mut take: impl FnMut(Pick)) {
        let count = region.lights.len();
        let rays = MOST_RAYS.saturating_sub(bounce).saturating_add(1).max(1) as usize;
        if count <= rays {
            for &light in &region.lights {
                take(Pick { light, weight: 1.0 });
            }
            return;
        }
        let each = 1.0 / rays as f32;
        let mut number = pick;
        for _ in 0..rays {
            take(region.pick(&self.power, &mut number, each));
        }
    }
}

impl Region {
    fn new(lights: Vec<usize>, power: &[f32]) -> Region {
        let mut cumulative = Vec::with_capacity(lights.len() + 1);
        cumulative.push(0f32);
        for (k, &light) in lights.iter().enumerate() {
            cumulative.push(cumulative[k] + power[light]);
        }
        let total = cumulative[lights.len()];
        let total = if total != 0.0 { total } else { 1.0 };
        for value in cumulative.iter_mut() {
            *value /= total;
        }
        Region {
            lights,
            cumulative,
            sum_reciprocal: 1.0 / total,
        }
    }

    /// Picks one of the region's lights with `number` of 0..1, half of its range by the
    /// lights' power and half evenly, and leaves in `number` what of it is left to pick the
    /// next light with. The weight is `weight` over how likely both ways were to pick it.
    fn pick(&self, power: &[f32], number: &mut f32, weight: f32) -> Pick {
        let count = self.lights.len();
        let light = if *number >= 0.5 {
            *number = (*number - 0.5) * 2.0;
            // The first running sum not below the number.
            let mut left = 0usize;
            let mut span = count + 1;
            while span > 0 {
                let step = span / 2;
                let at = left + step;
                if self.cumulative[at] < *number {
                    left = at + 1;
                    span -= step + 1;
                } else {
                    span = step;
                }
            }
            let segment = left.clamp(1, count);
            let (low, high) = (self.cumulative[segment - 1], self.cumulative[segment]);
            *number = (*number - low) / (high - low);
            self.lights[segment - 1]
        } else {
            *number *= 2.0;
            let k = ((*number * count as f32) as usize).min(count - 1);
            self.lights[k]
        };
        let evenly = 1.0 / count as f32;
        let by_power = power[light] * self.sum_reciprocal;
        Pick {
            light,
            weight: weight * (2.0 / (evenly + by_power)),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::baked::light::Mode;

    fn point(position: [f32; 3], range: f32, color: [f32; 3]) -> Light {
        Light {
            kind: Kind::Point,
            mode: Mode::Baked,
            color,
            indirect_color: color,
            position,
            forward: [0.0, 0.0, 1.0],
            range,
            cone_angle: 0.0,
            casts_shadows: true,
        }
    }

    fn spot(position: [f32; 3], forward: [f32; 3], degrees: f32, range: f32) -> Light {
        Light {
            kind: Kind::Spot,
            forward,
            cone_angle: degrees.to_radians(),
            ..point(position, range, [1.0; 3])
        }
    }

    fn cube(min: [f32; 3], max: [f32; 3]) -> Bounds {
        Bounds { min, max }
    }

    #[test]
    fn regions_are_25_m_across_and_55_m_up_within_the_lights_reach_and_the_scene() {
        // Lights reaching from x -50 to 50, the scene from -40 to 40 (and 0.5 more): four
        // regions of 20.25 m along x; the reach starts at the origin, so y runs from 0 (not
        // the lights' 5) to their 15.
        let lights = [
            point([-45.0, 10.0, 0.0], 5.0, [1.0; 3]),
            point([45.0, 10.0, 0.0], 5.0, [1.0; 3]),
        ];
        let grid = Grid::new(&lights, cube([-40.0, 0.0, -1.0], [40.0, 100.0, 1.0]));
        assert_eq!(grid.dims, [4, 1, 1]);
        assert_eq!(grid.min, [-40.5, 0.0, -1.5]);
        assert!((grid.cell[0] - 81.0 / 4.0).abs() < 1e-5);
        assert!((grid.cell[1] - 15.0).abs() < 1e-5);
        // Each light is in the regions its sphere meets only.
        assert_eq!(grid.region([-39.0, 10.0, 0.0]).unwrap().lights, vec![0]);
        assert_eq!(grid.region([39.0, 10.0, 0.0]).unwrap().lights, vec![1]);
        assert!(grid.region([0.0, 10.0, 0.0]).unwrap().lights.is_empty());
        // Outside the grid: no region.
        assert!(grid.region([0.0, 20.0, 0.0]).is_none());
        // At most 20 regions along an axis, and at least one.
        let far = [point([0.0, 0.0, 0.0], 1000.0, [1.0; 3])];
        let grid = Grid::new(&far, cube([-900.0, 0.0, 0.0], [900.0, 0.0, 0.0]));
        assert_eq!(grid.dims, [20, 1, 1]);
    }

    #[test]
    fn a_spot_light_reaches_the_boxes_in_front_of_it_within_its_range() {
        let down = spot([0.0, 5.0, 0.0], [0.0, -1.0, 0.0], 40.0, 20.0);
        // Below it, and around its axis' foot.
        assert!(reaches(&down, &cube([-1.0, 0.0, -1.0], [1.0, 1.0, 1.0])));
        assert!(reaches(&down, &cube([1.0, -1.0, -1.0], [2.0, 0.0, 1.0])));
        // Beside the cone, in front of the light and within its range: the cone itself is not
        // tested.
        assert!(reaches(&down, &cube([10.0, 3.0, -1.0], [11.0, 4.0, 1.0])));
        // Above it, behind its plane, though within its range.
        assert!(!reaches(&down, &cube([-1.0, 6.0, -1.0], [1.0, 7.0, 1.0])));
        // A box reaching across its plane.
        assert!(reaches(&down, &cube([3.0, 4.0, -1.0], [4.0, 9.0, 1.0])));
        // Beyond the range.
        assert!(!reaches(
            &down,
            &cube([-1.0, -30.0, -1.0], [1.0, -29.0, 1.0])
        ));
        // A box holding the light.
        assert!(reaches(&down, &cube([-1.0, 4.0, -1.0], [1.0, 6.0, 1.0])));
        // A point light: its sphere, in every direction.
        let bulb = point([0.0, 0.0, 0.0], 2.0, [1.0; 3]);
        assert!(reaches(&bulb, &cube([1.0, 1.0, -1.0], [3.0, 3.0, 1.0])));
        assert!(reaches(&bulb, &cube([-1.0, 1.0, -1.0], [1.0, 3.0, 1.0])));
        assert!(!reaches(&bulb, &cube([1.5, 1.5, 1.5], [3.0, 3.0, 3.0])));
    }

    #[test]
    fn lights_are_ordered_spots_first_then_by_their_colours() {
        let lights = [
            point([0.0; 3], 5.0, [1.0, 1.0, 1.0]),
            point([1.0; 3], 5.0, [0.1, 0.1, 0.1]),
            spot([2.0; 3], [0.0, -1.0, 0.0], 30.0, 5.0),
            spot([3.0; 3], [0.0, -1.0, 0.0], 60.0, 5.0),
        ];
        // The wider cone's smaller cosine first, the dimmer point first.
        assert_eq!(order(&lights), vec![3, 2, 1, 0]);
    }

    #[test]
    fn a_bounce_takes_all_its_regions_lights_or_picks_as_many_as_it_tests() {
        let lights: Vec<Light> = (0..6)
            .map(|k| point([k as f32 * 0.1, 0.0, 0.0], 50.0, [1.0 + k as f32; 3]))
            .collect();
        let grid = Grid::new(&lights, cube([-1.0; 3], [1.0; 3]));
        let region = grid.region([0.0; 3]).unwrap();
        assert_eq!(region.lights.len(), 6);
        let take = |bounce: u32, pick: f32| {
            let mut picks = Vec::new();
            grid.lights_at(region, bounce, pick, |p| picks.push(p));
            picks
        };
        // Four rays at the first surface, three at the second, ..., one from the fourth on.
        assert_eq!(take(1, 0.3).len(), 4);
        assert_eq!(take(2, 0.3).len(), 3);
        assert_eq!(take(4, 0.3).len(), 1);
        assert_eq!(take(9, 0.3).len(), 1);
        // A light picked evenly (a number under a half) or by power, weighed by both chances.
        let total: f32 = (0..6).map(|k| 4.0 * PI * (1.0 + k as f32)).sum();
        let first = take(4, 0.1)[0];
        assert_eq!(first.light, 1);
        let chance = |light: usize| 1.0 / 6.0 + 4.0 * PI * (1.0 + light as f32) / total;
        assert!((first.weight - 2.0 / chance(1)).abs() < 1e-4, "{first:?}");
        let by_power = take(4, 0.99)[0];
        assert_eq!(by_power.light, 5);
        // Fewer lights than rays: every one, weighing 1.
        let few = Grid::new(&lights[..3], cube([-1.0; 3], [1.0; 3]));
        let mut picks = Vec::new();
        few.lights_at(few.region([0.0; 3]).unwrap(), 1, 0.7, |p| picks.push(p));
        assert_eq!(
            picks,
            (0..3)
                .map(|light| Pick { light, weight: 1.0 })
                .collect::<Vec<_>>()
        );
    }

    use std::f32::consts::PI;
}
