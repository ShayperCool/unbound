// Presents the frame's bottom-up intermediate target on the window's (top-down) output texture,
// converting from the project's color space to what the output expects.

struct Params {
    // x: 1 when the intermediate holds gamma values (gamma projects),
    // y: 1 when the output is an sRGB format (the hardware encodes on write).
    mode: vec4<f32>,
}

@group(0) @binding(0) var source: texture_2d<f32>;
@group(0) @binding(1) var source_sampler: sampler;
@group(0) @binding(2) var<uniform> params: Params;

struct Out {
    @builtin(position) clip: vec4<f32>,
    @location(0) uv: vec2<f32>,
}

@vertex
fn vs_blit(@builtin(vertex_index) i: u32) -> Out {
    var o: Out;
    let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
    o.clip = vec4<f32>(uv * 2.0 - 1.0, 0.0, 1.0);
    // Output row 0 is the top; the source's top is its last row.
    o.uv = vec2<f32>(uv.x, uv.y);
    return o;
}

fn gamma_to_linear(c: vec3<f32>) -> vec3<f32> {
    let lo = c / 12.92;
    let hi = pow((c + 0.055) / 1.055, vec3<f32>(2.4));
    return select(hi, lo, c <= vec3<f32>(0.04045));
}

fn linear_to_gamma(c: vec3<f32>) -> vec3<f32> {
    let cc = max(c, vec3<f32>(0.0));
    let lo = cc * 12.92;
    let hi = 1.055 * pow(cc, vec3<f32>(1.0 / 2.4)) - 0.055;
    return select(hi, lo, cc <= vec3<f32>(0.0031308));
}

@fragment
fn fs_blit(in: Out) -> @location(0) vec4<f32> {
    let c = textureSample(source, source_sampler, in.uv);
    let gamma_source = params.mode.x > 0.5;
    let srgb_output = params.mode.y > 0.5;
    var rgb = clamp(c.rgb, vec3<f32>(0.0), vec3<f32>(1.0));
    if gamma_source && srgb_output {
        rgb = gamma_to_linear(rgb);
    } else if !gamma_source && !srgb_output {
        rgb = linear_to_gamma(rgb);
    }
    return vec4<f32>(rgb, 1.0);
}
