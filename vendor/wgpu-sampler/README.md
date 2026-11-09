# wgpu with native sampler LOD bias and the OpenGL device

Sources are the unmodified crates.io packages wgpu, wgpu-core and wgpu-hal 30.0.1,
with their original licenses. Local changes add an explicit native Vulkan sampler-bias
creation method. The ordinary WebGPU descriptor and its zero-bias path stay compatible.

The extension uses the existing Core resource registration, device ownership, Vulkan
sampler cache, and destruction path. It does not import a foreign sampler or cast private
handles. Nonzero bias is rejected outside Vulkan and desktop OpenGL (`gl-device.patch`) and on Vulkan
portability-subset devices.
Vulkan device limits bound the supported bias. See the sampler probe for Native controls.

`gl-device.patch` makes wgpu's OpenGL backend draw Unity's OpenGL Core device: a 4.5 core
context, texture views of another format, dimension or range, sampled multisampled textures and
the sampler LOD bias on desktop OpenGL. PATCHES.md lists each change and why.
