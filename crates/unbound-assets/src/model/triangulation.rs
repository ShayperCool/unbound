//! Polygon triangle order measured in the Unity 6000.0.71f1 Editor,
//! tests/projects/lightmapuvs. GLU sweeps the mirrored, float32 control points in file face order.
use anyhow::{Result, bail, ensure};

unsafe extern "C" {
    fn unbound_model_tess(
        positions: *const f32,
        count: u32,
        indices: *mut u32,
        capacity: usize,
    ) -> i32;
}

pub fn polygon(points: &[[f32; 3]]) -> Result<Vec<u32>> {
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
    // adapter owns its callback state and refuses intersections requiring new vertex attributes.
    let count = unsafe {
        unbound_model_tess(
            points.as_ptr().cast(),
            points.len() as u32,
            indices.as_mut_ptr(),
            indices.len(),
        )
    };
    if count == -2 {
        bail!("model polygon tessellator allocation failed");
    }
    if count < 0 {
        bail!(
            "model polygon tessellation failed: intersecting or invalid polygons are not supported ({count})"
        );
    }
    ensure!(
        count > 0 && count as usize <= indices.len() && count % 3 == 0,
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
    Ok(indices)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn refuses_intersections_and_non_finite_coordinates() {
        assert!(
            polygon(&[
                [0.0, 0.0, 0.0],
                [1.0, 1.0, 0.0],
                [0.0, 1.0, 0.0],
                [1.0, 0.0, 0.0]
            ])
            .is_err()
        );
        assert!(polygon(&[[0.0, 0.0, 0.0], [1.0, 0.0, 0.0], [0.0, f32::NAN, 0.0]]).is_err());
    }
}
