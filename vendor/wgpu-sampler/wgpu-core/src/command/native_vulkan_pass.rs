//! Prepared Vulkan depth/stencil passes recorded in the ordinary command stream.
use crate::{
    command::{encoder::EncodingState, ArcCommand, CommandEncoderError, EncoderStateError},
    device::DeviceError,
    global::Global,
    id::{CommandEncoderId, DeviceId, TextureViewId},
    resource::{ParentDevice, RawResourceAccess, TextureView},
};
use alloc::sync::Arc;

/// A reusable native pass whose attachments stay owned by Core.
#[derive(Debug)]
pub struct NativeVulkanDepthPass {
    views: [Arc<TextureView>; 2],
    pass: u64,
    framebuffer: u64,
    width: u32,
    height: u32,
}

impl Global {
    /// Prepares an externally owned Vulkan pass. See the frontend's safety contract.
    pub unsafe fn device_prepare_native_vulkan_depth_pass(
        &self,
        device_id: DeviceId,
        source: TextureViewId,
        destination: TextureViewId,
        pass: u64,
        framebuffer: u64,
        width: u32,
        height: u32,
    ) -> Option<Arc<NativeVulkanDepthPass>> {
        let device = self.hub.devices.get(device_id);
        #[cfg(vulkan)]
        if device.raw().as_any().is::<hal::vulkan::Device>()
            && pass != 0
            && framebuffer != 0
            && width > 0
            && height > 0
        {
            #[cfg(feature = "trace")]
            if device.trace.lock().is_some() {
                return None;
            }
            let source = self.hub.texture_views.get(source).get().ok()?;
            let destination = self.hub.texture_views.get(destination).get().ok()?;
            source.same_device(&device).ok()?;
            destination.same_device(&device).ok()?;
            return Some(Arc::new(NativeVulkanDepthPass {
                views: [source, destination],
                pass,
                framebuffer,
                width,
                height,
            }));
        }
        let _ = (
            device,
            source,
            destination,
            pass,
            framebuffer,
            width,
            height,
        );
        None
    }

    /// Adds a prepared pass without allocating another encoder or command payload.
    pub fn command_encoder_run_native_vulkan_depth_pass(
        &self,
        id: CommandEncoderId,
        pass: &Arc<NativeVulkanDepthPass>,
    ) -> Result<(), EncoderStateError> {
        let encoder = self.hub.command_encoders.get(id);
        if !Arc::ptr_eq(&encoder.device, &pass.views[0].device) {
            return Err(EncoderStateError::Invalid);
        }
        #[cfg(feature = "trace")]
        if encoder.device.trace.lock().is_some() {
            return Err(EncoderStateError::Invalid);
        }
        let result = encoder
            .data
            .lock()
            .push_with(|| Ok::<_, DeviceError>(ArcCommand::NativeVulkanDepthPass(pass.clone())));
        result
    }
}

pub(crate) fn encode(
    state: &mut EncodingState,
    pass: &NativeVulkanDepthPass,
) -> Result<(), CommandEncoderError> {
    #[cfg(vulkan)]
    {
        for view in &pass.views {
            view.same_device(state.device)?;
            let raw = view.parent.try_raw(state.snatch_guard)?;
            state.tracker.views.insert_single(view.clone());
            for pending in state.tracker.textures.set_single(
                &view.parent,
                view.selector.clone(),
                wgt::TextureUses::DEPTH_STENCIL_WRITE,
            ) {
                let barrier = pending.into_hal(raw);
                // SAFETY: Core owns the live textures; the native pass uses their full attachment views.
                unsafe {
                    state
                        .raw_encoder
                        .transition_textures(core::slice::from_ref(&barrier));
                }
            }
        }
        let encoder = state
            .raw_encoder
            .as_any_mut()
            .downcast_mut::<hal::vulkan::CommandEncoder>()
            .ok_or(DeviceError::Lost)?;
        // SAFETY: the frontend contract retains the compatible pass/framebuffer through GPU completion.
        unsafe {
            encoder.run_native_depth_pass(pass.pass, pass.framebuffer, pass.width, pass.height);
        }
        return Ok(());
    }
    #[cfg(not(vulkan))]
    {
        let _ = (state, pass);
        Err(DeviceError::Lost.into())
    }
}
