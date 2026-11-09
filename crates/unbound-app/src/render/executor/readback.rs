//! Shared staging storage for synchronous texture and buffer downloads.
use super::*;

// Reuse staging storage across nearby reads, then release its high-water allocation after a
// short idle span so occasional screenshots do not retain peak download memory indefinitely.
const STAGING_IDLE_FRAMES: u64 = 8;

#[derive(Default)]
pub(super) struct Staging {
    buffer: Option<wgpu::Buffer>,
    last_used: u64,
}

impl Staging {
    pub(super) fn frame(&mut self, frame: u64) {
        if frame.saturating_sub(self.last_used) >= STAGING_IDLE_FRAMES {
            self.buffer = None;
        }
    }
}

impl UnityRenderer {
    pub(super) fn read_gpu_data(
        &mut self,
        size: u64,
        label: &'static str,
        encode: impl FnOnce(&mut wgpu::CommandEncoder, &wgpu::Buffer),
        decode: impl FnOnce(&[u8]) -> Option<Vec<u8>>,
    ) -> Option<Vec<u8>> {
        let limit = self.device.limits().max_buffer_size;
        if size > limit {
            tracing::warn!(
                "GPU readback needs {size} bytes, beyond the device's {limit}-byte staging limit"
            );
            return None;
        }
        if self
            .readback
            .buffer
            .as_ref()
            .is_none_or(|buffer| buffer.size() < size)
        {
            self.readback.buffer = Some(self.device.create_buffer(&wgpu::BufferDescriptor {
                label: Some("unbound readback staging"),
                size: size.checked_next_power_of_two().unwrap_or(limit).min(limit),
                usage: wgpu::BufferUsages::MAP_READ | wgpu::BufferUsages::COPY_DST,
                mapped_at_creation: false,
            }));
        }
        self.readback.last_used = self.frame_number;
        let buffer = self.readback.buffer.as_ref()?.clone();
        let mut encoder = self
            .device
            .create_command_encoder(&wgpu::CommandEncoderDescriptor { label: Some(label) });
        encode(&mut encoder, &buffer);
        let index = self.queue.submit([encoder.finish()]);
        let slice = buffer.slice(..size);
        slice.map_async(wgpu::MapMode::Read, |_| {});
        if let Err(error) = self.device.poll(wgpu::PollType::Wait {
            submission_index: Some(index),
            timeout: None,
        }) {
            buffer.unmap();
            tracing::warn!("GPU readback failed: {error}");
            return None;
        }
        let data = match slice.get_mapped_range() {
            Ok(data) => data,
            Err(error) => {
                buffer.unmap();
                tracing::warn!("GPU readback mapping failed: {error}");
                return None;
            }
        };
        let output = decode(&data);
        drop(data);
        buffer.unmap();
        output
    }
}
