// Clears a camera's partial viewport (color, and depth with stencil) by drawing over it.

struct Draw {
    model: mat4x4<f32>,
    view: mat4x4<f32>,
    proj: mat4x4<f32>,
    // The clear color.
    color: vec4<f32>,
    screen: vec4<f32>,
    // x: the requested device depth.
    misc: vec4<f32>,
}

@group(0) @binding(0) var<uniform> draw: Draw;

struct ClearOut {
    @builtin(position) clip: vec4<f32>,
}

@vertex
fn vs_clear(@builtin(vertex_index) i: u32) -> ClearOut {
    var o: ClearOut;
    let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
    o.clip = vec4<f32>(uv * 2.0 - 1.0, draw.misc.x, 1.0);
    return o;
}

@fragment
fn fs_clear() -> @location(0) vec4<f32> {
    return draw.color;
}

@fragment
fn fs_clear_signed() -> @location(0) vec4<i32> {
    return bitcast<vec4<i32>>(draw.color);
}

@fragment
fn fs_clear_unsigned() -> @location(0) vec4<u32> {
    return bitcast<vec4<u32>>(draw.color);
}
