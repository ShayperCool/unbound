//! Polygon triangle order measured in the Unity 6000.0.71f1 Editor,
//! tests/projects/lightmapuvs. GLU sweeps the mirrored, float32 control points in file face order.
//!
//! Corners at one position (bridged holes, CAD seams) are merged by the sweep and keep the first
//! corner; zero-area polygons give no triangles. A polygon whose edges cross is discarded whole,
//! as the importer does ("self-intersecting and has been discarded").
use anyhow::{Result, bail, ensure};

unsafe extern "C" {
    fn unbound_model_tess(
        positions: *const f32,
        count: u32,
        indices: *mut u32,
        capacity: usize,
    ) -> i32;
}

/// A polygon's triangles as corner indices, or `None` when its edges cross.
pub fn polygon(points: &[[f32; 3]]) -> Result<Option<Vec<u32>>> {
    ensure!(
        points.len() >= 3 && points.len() <= u32::MAX as usize,
        "invalid model polygon size"
    );
    ensure!(
        points.iter().flatten().all(|v| v.is_finite()),
        "non-finite model polygon coordinates are not supported"
    );
    let capacity = (points.len() - 2)
        .checked_mul(3)
        .ok_or_else(|| anyhow::anyhow!("model polygon index count overflows"))?;
    ensure!(
        capacity <= i32::MAX as usize,
        "model polygon has too many triangle indices"
    );
    let mut indices = vec![0; capacity];
    // SAFETY: contiguous triples and bounded output live through the synchronous call. The
    // adapter owns its callback state; it merges coincident corners and refuses crossings.
    let count = unsafe {
        unbound_model_tess(
            points.as_ptr().cast(),
            points.len() as u32,
            indices.as_mut_ptr(),
            indices.len(),
        )
    };
    match count {
        -3 => return Ok(None),
        -2 => bail!("model polygon tessellator allocation failed"),
        ..0 => bail!("model polygon tessellation failed ({count})"),
        _ => {}
    }
    ensure!(
        count as usize <= indices.len() && count % 3 == 0,
        "model polygon has no valid tessellation"
    );
    indices.truncate(count as usize);
    ensure!(
        indices.iter().all(|&v| (v as usize) < points.len()),
        "invalid tessellated model vertex"
    );
    for triangle in indices.chunks_mut(3) {
        triangle.swap(1, 2);
    }
    Ok(Some(indices))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn crossing_polygons_are_discarded_and_non_finite_coordinates_refused() {
        let bowtie = [
            [0.0, 0.0, 0.0],
            [1.0, 1.0, 0.0],
            [0.0, 1.0, 0.0],
            [1.0, 0.0, 0.0],
        ];
        assert_eq!(polygon(&bowtie).unwrap(), None);
        assert!(polygon(&[[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, f32::NAN, 0.0]]).is_err());
    }

    #[test]
    fn coincident_corners_merge_into_the_first() {
        // A square with a square hole, bridged: corners 4 and 9 repeat corners 0 and 5.
        let o = [[0.0, 0.0], [2.0, 0.0], [2.0, 2.0], [0.0, 2.0]];
        let i = [[0.5, 0.5], [0.5, 1.5], [1.5, 1.5], [1.5, 0.5]];
        let ring: Vec<[f32; 3]> = [&o[..], &o[..1], &i[..], &i[..1]]
            .concat()
            .into_iter()
            .map(|[x, y]| [x, y, 0.0])
            .collect();
        let indices = polygon(&ring).unwrap().unwrap();
        assert_eq!(indices.len(), 3 * 8);
        // Each position keeps one corner; no triangle repeats a position.
        for t in indices.chunks(3) {
            let p: Vec<_> = t.iter().map(|&c| ring[c as usize]).collect();
            assert!(p[0] != p[1] && p[1] != p[2] && p[0] != p[2], "{t:?}");
        }
        let used = |a: u32, b: u32| indices.contains(&a) as u8 + indices.contains(&b) as u8;
        assert_eq!((used(0, 4), used(5, 9)), (1, 1));
    }

    #[test]
    fn zero_area_polygons_have_no_triangles() {
        // Collinear corners overlap their own edges; either way nothing is drawn.
        let line: Vec<[f32; 3]> = (0..5).map(|k| [k as f32 * 0.25, 1.0, 0.0]).collect();
        let result = polygon(&line).unwrap();
        assert!(
            result.as_deref().is_none_or(<[u32]>::is_empty),
            "{result:?}"
        );
    }
}
