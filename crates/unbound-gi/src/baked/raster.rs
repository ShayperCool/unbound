//! Which texels of a lightmap a triangle of lightmap UVs covers, and where in each it is
//! sampled, as the Unity 6000.0.71f1 Editor's Progressive CPU lightmapper rasterizes its charts
//! (tests/projects/bakedbuffers). The rasterization is conservative: a texel the triangle only
//! touches is covered too, sampled at the centre of what the triangle covers of it. Each texel
//! keeps the triangle that covers most of it; a triangle that holds the texel's centre covers
//! all of it, and of triangles covering it equally the last one drawn stays. All arithmetic is
//! in `f32` in the Editor's order, so that the same triangles give the same texels.

/// A texel of a triangle: its column and row, and the weights of the triangle's corners at the
/// point it is sampled at, in the order of [`Texel::corners`].
#[derive(Clone, Copy, Debug)]
pub struct Texel {
    pub x: usize,
    pub y: usize,
    /// The triangle's corners in the order the weights are in: the first two are swapped when
    /// the triangle winds anticlockwise in the lightmap.
    pub corners: [usize; 3],
    pub weights: [f32; 3],
}

/// Half a texel's diagonal: a texel whose centre is this far inside every edge is inside the
/// triangle, and one whose centre is this far outside an edge is not touched.
const TEXEL_RADIUS: f32 = 0.707_106_77;
/// The same for a block of 8 × 8 texels (four diagonals).
const BLOCK_RADIUS: f32 = 5.656_854_2;
const BLOCK: f32 = 8.0;

/// The triangle's set-up: its corners in texels (wound clockwise), the gradients of the weights
/// of its second and third corners, and what tells whether a point is inside it.
struct Triangle {
    a: [f32; 2],
    b: [f32; 2],
    c: [f32; 2],
    swapped: bool,
    /// Gradients (per texel along x, along y) of the second corner's weight, in the form used
    /// for each winding, and of the third corner's weight.
    second_dx: f32,
    second_dy: f32,
    third_dx: f32,
    third_dy: f32,
    e1: [f32; 2],
    e2: [f32; 2],
    e1e1: f32,
    e2e2: f32,
    e1e2: f32,
    inverse: f32,
}

impl Triangle {
    fn corners(&self) -> [usize; 3] {
        if self.swapped { [1, 0, 2] } else { [0, 1, 2] }
    }

    /// The weights at a point given relative to the first corner, when they are all within the
    /// triangle (the second and third between 0 and 1).
    fn weights(&self, second: f32, third: f32) -> Option<[f32; 3]> {
        (third <= 1.0 && 0.0 <= third && 0.0 <= second && second <= 1.0)
            .then(|| [(1.0 - second) - third, second, third])
    }

    fn weights_at(&self, dx: f32, dy: f32) -> Option<[f32; 3]> {
        let third = dy * self.third_dy + self.third_dx * dx;
        let second = dy * self.second_dy + dx * self.second_dx;
        self.weights(second, third)
    }

    /// Whether the point (relative to the first corner; `y1`, `y2` are its y times the edges'
    /// y) is inside the triangle.
    fn holds(&self, dx: f32, y1: f32, y2: f32) -> bool {
        let d1 = dx * self.e1[0] + y1;
        let d2 = dx * self.e2[0] + y2;
        let u = (self.e2e2 * d1 - d2 * self.e1e2) * self.inverse;
        let v = (self.e1e1 * d2 - d1 * self.e1e2) * self.inverse;
        0.0 <= u && 0.0 <= v && v + u < 1.0
    }
}

/// A triangle cut by the four sides of a texel has at most seven corners.
#[derive(Clone, Copy)]
struct Polygon {
    points: [[f32; 2]; 8],
    count: usize,
}

impl Polygon {
    fn push(&mut self, p: [f32; 2]) {
        // Never more than seven for a triangle; one more would be dropped.
        if self.count < self.points.len() {
            self.points[self.count] = p;
            self.count += 1;
        }
    }

    fn edges(&self) -> impl Iterator<Item = ([f32; 2], [f32; 2])> + '_ {
        (0..self.count).map(|i| (self.points[i], self.points[(i + 1) % self.count]))
    }

    /// The part on the kept side of an axis-aligned line (Sutherland–Hodgman).
    fn clipped(&self, axis: usize, limit: f32, keep_above: bool) -> Polygon {
        let mut out = Polygon {
            points: [[0.0; 2]; 8],
            count: 0,
        };
        let other = 1 - axis;
        let inside = |d: f32| if keep_above { d <= 0.0 } else { 0.0 <= d };
        for (p, q) in self.edges() {
            let (dp, dq) = (limit - p[axis], limit - q[axis]);
            if inside(dp) {
                out.push(p);
            }
            if inside(dp) != inside(dq) {
                let mut cut = [0f32; 2];
                cut[axis] = limit;
                cut[other] = dp * ((q[other] - p[other]) / (q[axis] - p[axis])) + p[other];
                out.push(cut);
            }
        }
        out
    }
}

/// The area of the triangle within the texel centred at `centre`, and that part's centre
/// relative to the texel's (negated: the triangle winds clockwise).
fn within_texel(t: &Triangle, centre: [f32; 2]) -> (f32, [f32; 2]) {
    let relative = |p: [f32; 2]| [p[0] - centre[0], p[1] - centre[1]];
    let mut polygon = Polygon {
        points: [[0.0; 2]; 8],
        count: 0,
    };
    for corner in [t.a, t.b, t.c] {
        polygon.push(relative(corner));
    }
    for (axis, limit, keep_above) in [
        (0, -0.5, true),
        (1, -0.5, true),
        (0, 0.5, false),
        (1, 0.5, false),
    ] {
        polygon = polygon.clipped(axis, limit, keep_above);
    }
    let (mut doubled, mut sx, mut sy) = (0f32, 0f32, 0f32);
    for (p, q) in polygon.edges() {
        let cross = q[1] * p[0] - q[0] * p[1];
        doubled += cross;
        sx += (p[0] + q[0]) * cross;
        sy += (q[1] + p[1]) * cross;
    }
    let area = doubled.abs() * 0.5;
    if area == 0.0 {
        return (0.0, [0.0, 0.0]);
    }
    let scale = area * 6.0;
    (area, [sx / scale, sy / scale])
}

struct Target<'a, F: FnMut(Texel)> {
    width: usize,
    height: usize,
    /// How much of each texel the triangle kept for it covers (1 for a texel whose centre it
    /// holds).
    coverage: &'a mut [f32],
    shade: F,
}

impl<F: FnMut(Texel)> Target<'_, F> {
    /// Gives the texel to the triangle when it covers at least what the texel's triangle does.
    /// A texel outside the lightmap is left alone.
    fn cover(
        &mut self,
        t: &Triangle,
        x: f32,
        y: f32,
        covered: f32,
        weights: impl FnOnce() -> Option<[f32; 3]>,
    ) {
        let (column, row) = (x as i32, y as i32);
        if column < 0 || row < 0 || column as usize >= self.width || row as usize >= self.height {
            return;
        }
        let (column, row) = (column as usize, row as usize);
        let kept = &mut self.coverage[self.width * row + column];
        if *kept <= covered {
            *kept = covered;
            if let Some(weights) = weights() {
                (self.shade)(Texel {
                    x: column,
                    y: row,
                    corners: t.corners(),
                    weights,
                });
            }
        }
    }

    /// A texel the triangle touches without being known to hold all of it: sampled at its centre
    /// when the triangle holds that, else at the centre of the part the triangle covers.
    /// Whether the texel counts as reached.
    fn touch(&mut self, t: &Triangle, x: f32, y: f32, holds_centre: bool) -> bool {
        let (area, centre) = within_texel(t, [x, y]);
        let (px, py, covered) = if holds_centre {
            (x, y, 1.0)
        } else {
            let (px, py) = (x - centre[0], y - centre[1]);
            if px < 0.0
                || py < 0.0
                || self.width as f32 <= px
                || self.height as f32 <= py
                || area <= 0.0
            {
                return false;
            }
            (px, py, area)
        };
        self.cover(t, px, py, covered, || {
            t.weights_at(px - t.a[0], py - t.a[1])
        });
        true
    }
}

/// Rasterizes the triangle of lightmap UVs `uv` (0..1 across the lightmap) into a lightmap of
/// `width` × `height` texels: calls `shade` for each texel the triangle gets, with the weights
/// of its corners there. `coverage` (one value per texel, zero before the first triangle) keeps
/// how much of each texel its triangle covers, across the triangles of a lightmap. A triangle
/// without area in the lightmap gets nothing.
pub fn triangle(
    uv: [[f32; 2]; 3],
    width: usize,
    height: usize,
    coverage: &mut [f32],
    shade: impl FnMut(Texel),
) {
    assert_eq!(coverage.len(), width * height);
    let (w, h) = (width as f32, height as f32);
    let mut a = [uv[0][0] * w, uv[0][1] * h];
    let mut b = [uv[1][0] * w, uv[1][1] * h];
    let c = [uv[2][0] * w, uv[2][1] * h];
    let mut e1 = [b[0] - a[0], b[1] - a[1]];
    let mut e2 = [c[0] - a[0], c[1] - a[1]];
    // Anticlockwise: its first two corners change places, so that it is rasterized clockwise.
    let swapped = e1[1] * e2[0] - e2[1] * e1[0] < 0.0;
    if swapped {
        std::mem::swap(&mut a, &mut b);
        e1 = [b[0] - a[0], b[1] - a[1]];
        e2 = [c[0] - a[0], c[1] - a[1]];
    }
    let inverse = 1.0 / (e1[0] * e2[1] - e1[1] * e2[0]);
    if inverse.abs() == f32::INFINITY {
        return;
    }
    // The gradients of the corners' weights. The second corner's is taken as the weight of the
    // corner the first one was before a swap, in the Editor's form for each case.
    let (first, second) = if swapped { (0f32, 1f32) } else { (1f32, 0f32) };
    let (e1y, e2y, e1x, e2x) = (
        e1[1] * inverse,
        e2[1] * inverse,
        e1[0] * inverse,
        inverse * e2[0],
    );
    let (second_dx, second_dy) = if swapped {
        (
            (second - first) * e2y - (0.0 - first) * e1y,
            (0.0 - first) * e1x - (second - first) * e2x,
        )
    } else {
        (
            (first - second) * e2y - (0.0 - second) * e1y,
            (0.0 - second) * e1x - (first - second) * e2x,
        )
    };
    let e1e2 = e2[1] * e1[1] + e2[0] * e1[0];
    let (e1e1, e2e2) = (e1[1] * e1[1] + e1[0] * e1[0], e2[1] * e2[1] + e2[0] * e2[0]);
    let t = Triangle {
        a,
        b,
        c,
        swapped,
        second_dx,
        second_dy,
        third_dx: e2y * 0.0 - e1y,
        third_dy: e1x - e2x * 0.0,
        e1,
        e2,
        e1e1,
        e2e2,
        e1e2,
        inverse: 1.0 / (e2e2 * e1e1 - e1e2 * e1e2),
    };
    let mut target = Target {
        width,
        height,
        coverage,
        shade,
    };

    // The texels the triangle may touch, from the block of 8 its first one is in.
    let least = |p: f32, q: f32, r: f32| {
        let m = if p <= q { p } else { q };
        let m = if m <= r { m } else { r };
        (if 0.0 <= m { m } else { 0.0 }).floor()
    };
    let most = |p: f32, q: f32, r: f32, limit: f32| {
        let m = if q <= p { p } else { q };
        let m = if r <= m { m } else { r };
        (if m <= limit { m } else { limit }).ceil()
    };
    let start_x = ((least(a[0], b[0], c[0]) as i32 as u32) & !7) as i32 as f32 + 0.5;
    let start_y = ((least(a[1], b[1], c[1]) as i32 as u32) & !7) as i32 as f32 + 0.5;
    let end_x = most(a[0], b[0], c[0], w + -1.0) + 0.5;
    let end_y = most(a[1], b[1], c[1], h + -1.0) + 0.5;

    // Each edge's distance to a point, positive inside: x × `nx` + `k` + y × `ny`.
    struct Edge {
        nx: f32,
        ny: f32,
        k: f32,
        /// What the distance loses from a texel to the next along x (−`nx`).
        step: f32,
    }
    impl Edge {
        fn at(&self, x: f32, y: f32) -> f32 {
            (x * self.nx + self.k) + y * self.ny
        }
    }
    let edge = |from: [f32; 2], to: [f32; 2], through: [f32; 2]| {
        let d = [from[0] - to[0], from[1] - to[1]];
        let r = 1.0 / (d[0] * d[0] + d[1] * d[1]).sqrt();
        let (x, y) = (d[0] * r, d[1] * r);
        Edge {
            nx: -y,
            ny: x,
            k: -(through[1] * x) - through[0] * -y,
            step: y,
        }
    };
    let bc = edge(b, c, b);
    let ab = edge(a, b, a);
    let ca = {
        let d = [c[0] - a[0], c[1] - a[1]];
        let r = 1.0 / (d[0] * d[0] + d[1] * d[1]).sqrt();
        let (x, y) = (d[0] * r, d[1] * r);
        Edge {
            nx: -y,
            ny: x,
            k: c[0] * y - c[1] * x,
            step: y,
        }
    };

    let mut reached = 0u32;
    let mut by = start_y;
    while by <= end_y {
        let mut bx = start_x;
        while bx <= end_x {
            let (cx, cy) = (bx + 3.5, by + 3.5);
            let centre = [ca.at(cx, cy), bc.at(cx, cy), ab.at(cx, cy)];
            if centre.iter().all(|&d| -BLOCK_RADIUS < d) {
                if centre.iter().all(|&d| BLOCK_RADIUS <= d) {
                    // A block inside the triangle: every texel of it, the weights stepped along
                    // each row and from row to row.
                    let (dx, dy) = (bx - t.a[0], by - t.a[1]);
                    let mut row = [
                        dx * t.second_dx + dy * t.second_dy,
                        dx * t.third_dx + dy * t.third_dy,
                    ];
                    let mut y = by;
                    while y < by + BLOCK {
                        let [mut second, mut third] = row;
                        let mut x = bx;
                        while x < bx + BLOCK {
                            target.cover(&t, x, y, 1.0, || t.weights(second, third));
                            reached += 1;
                            second += t.second_dx;
                            third += t.third_dx;
                            x += 1.0;
                        }
                        row = [row[0] + t.second_dy, row[1] + t.third_dy];
                        y += 1.0;
                    }
                } else {
                    // A block the triangle's edges cross: each texel by its own distances.
                    let mut row = [ca.at(bx, by), bc.at(bx, by), ab.at(bx, by)];
                    let mut y = by;
                    while y < by + BLOCK {
                        let dy = y - t.a[1];
                        let (y1, y2) = (dy * t.e1[1], dy * t.e2[1]);
                        let mut d = row;
                        let mut x = bx;
                        while x < bx + BLOCK {
                            let dx = x - t.a[0];
                            if d.iter().all(|&d| TEXEL_RADIUS <= d) {
                                target.cover(&t, x, y, 1.0, || {
                                    t.weights(
                                        dx * t.second_dx + t.second_dy * dy,
                                        dx * t.third_dx + t.third_dy * dy,
                                    )
                                });
                                reached += 1;
                            } else if d.iter().all(|&d| -TEXEL_RADIUS <= d) {
                                reached += target.touch(&t, x, y, t.holds(dx, y1, y2)) as u32;
                            }
                            d = [d[0] - ca.step, d[1] - bc.step, d[2] - ab.step];
                            x += 1.0;
                        }
                        row = [row[0] + ca.ny, row[1] + bc.ny, row[2] + ab.ny];
                        y += 1.0;
                    }
                }
            }
            bx += BLOCK;
        }
        by += BLOCK;
    }
    if reached > 0 {
        return;
    }
    // A triangle too small or thin for its edges' distances to reach a texel: every texel of
    // its bounds by what it covers of it.
    let mut y = start_y;
    while y <= end_y {
        let dy = y - t.a[1];
        let mut x = start_x;
        while x <= end_x {
            let holds = t.holds(x - t.a[0], dy * t.e1[1], dy * t.e2[1]);
            reached += target.touch(&t, x, y, holds) as u32;
            x += 1.0;
        }
        y += 1.0;
    }
    if reached > 0 {
        return;
    }
    // One that covers nothing measurable of any texel: the texel half a texel beyond its
    // centre, kept by its doubled area.
    let doubled = (e1[0] * e2[1] - e1[1] * e2[0]).abs();
    if doubled < (1.0 / w) * h && doubled != 0.0 {
        let (px, py) = (
            (t.c[0] + t.b[0] + t.a[0]) / 3.0 + 0.5,
            (t.c[1] + t.b[1] + t.a[1]) / 3.0 + 0.5,
        );
        if 0.0 <= px && 0.0 <= py && px < w && py < h {
            target.cover(&t, px, py, doubled, || {
                let (dx, dy) = (px - t.a[0], py - t.a[1]);
                t.weights(
                    dx * t.second_dx + dy * t.second_dy,
                    t.third_dx * dx + t.third_dy * dy,
                )
            });
        }
    }
}
