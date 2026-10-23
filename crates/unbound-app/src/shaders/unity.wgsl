// Unbound's ports of Unity's built-in (and common package) shaders. One module, one entry point pair
// per shader family; render states come from the pipeline, keyword variants from `mat.flags`.
//
// Conventions: matrices are Unity's (column-major, OpenGL clip space). Every render target is stored
// bottom-up (row 0 = Unity's bottom row, matching Unity's texture rows), so clip y is negated and
// z remapped to wgpu's [0, w] in `to_target`.

struct Draw {
    model: mat4x4<f32>,
    view: mat4x4<f32>,
    // Camera.projectionMatrix as Unity gives it (OpenGL convention).
    proj: mat4x4<f32>,
    // The per-draw color (CanvasRenderer color and alpha).
    color: vec4<f32>,
    // _ScreenParams: target width, height, 1 + 1/width, 1 + 1/height.
    screen: vec4<f32>,
    // x: time (_Time.y), y: color space (0 gamma, 1 linear), z: 1 when the main texture is alpha-only.
    misc: vec4<f32>,
    pad: vec4<f32>,
}

struct Material {
    color: vec4<f32>,
    main_st: vec4<f32>,
    tint: vec4<f32>,
    sample_add: vec4<f32>,
    emission: vec4<f32>,
    color2: vec4<f32>,
    color3: vec4<f32>,
    color4: vec4<f32>,
    st_a: vec4<f32>,
    st_b: vec4<f32>,
    p0: vec4<f32>,
    p1: vec4<f32>,
    p2: vec4<f32>,
    p3: vec4<f32>,
    p4: vec4<f32>,
    p5: vec4<f32>,
    p6: vec4<f32>,
    p7: vec4<f32>,
    // x: keyword bits (see `keywords` in families.rs), y: family variant.
    flags: vec4<u32>,
    pad: vec4<f32>,
}

const K_ALPHACLIP: u32 = 1u;
const K_OUTLINE_ON: u32 = 2u;
const K_UNDERLAY_ON: u32 = 4u;
const K_UNDERLAY_INNER: u32 = 8u;
const K_GLOW_ON: u32 = 16u;
const K_STRAIGHT_ALPHA: u32 = 32u;
const K_ALPHATEST_ON: u32 = 64u;
const K_ALPHAPREMULTIPLY_ON: u32 = 128u;
const K_ALPHABLEND_ON: u32 = 256u;
const K_EMISSION: u32 = 512u;
const K_ALPHAMODULATE_ON: u32 = 1024u;
const K_CANVAS_GROUP_COMPATIBLE: u32 = 2048u;
const K_DARK_COLOR_ALPHA_ADDITIVE: u32 = 4096u;
const K_ENABLE_FILL: u32 = 8192u;
const K_PREMULTIPLY_OUTPUT: u32 = 16384u;
const K_VERTEX_COLOR_GAMMA: u32 = 32768u;

@group(0) @binding(0) var<uniform> draw: Draw;
@group(1) @binding(0) var<uniform> mat: Material;
@group(1) @binding(1) var tex_a: texture_2d<f32>;
@group(1) @binding(2) var samp_a: sampler;
@group(1) @binding(3) var tex_b: texture_2d<f32>;
@group(1) @binding(4) var samp_b: sampler;
@group(2) @binding(0) var main_tex: texture_2d<f32>;
@group(2) @binding(1) var main_samp: sampler;

struct VertexIn {
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) tangent: vec4<f32>,
    @location(3) color: vec4<f32>,
    @location(4) uv0: vec4<f32>,
    @location(5) uv1: vec4<f32>,
    @location(6) uv2: vec4<f32>,
    @location(7) uv3: vec4<f32>,
}

struct VertexOut {
    @builtin(position) clip: vec4<f32>,
    @location(0) color: vec4<f32>,
    @location(1) uv0: vec4<f32>,
    @location(2) uv1: vec4<f32>,
    @location(3) uv2: vec4<f32>,
    @location(4) world_normal: vec3<f32>,
    @location(5) world_pos: vec3<f32>,
    @location(6) p0: vec4<f32>,
    @location(7) p1: vec4<f32>,
    @location(8) p2: vec4<f32>,
    @location(9) c2: vec4<f32>,
}

fn has(k: u32) -> bool {
    return (mat.flags.x & k) != 0u;
}

fn is_linear() -> bool {
    return draw.misc.y > 0.5;
}

// OpenGL clip space → wgpu's, for a bottom-up target.
fn to_target(c: vec4<f32>) -> vec4<f32> {
    return vec4<f32>(c.x, -c.y, 0.5 * c.z + 0.5 * c.w, c.w);
}

fn object_to_clip(p: vec4<f32>) -> vec4<f32> {
    return draw.proj * (draw.view * (draw.model * p));
}

fn transform_tex(uv: vec2<f32>, st: vec4<f32>) -> vec2<f32> {
    return uv * st.xy + st.zw;
}

fn safe_normalize(v: vec3<f32>) -> vec3<f32> {
    let l = length(v);
    if l > 1e-8 {
        return v / l;
    }
    return vec3<f32>(0.0, 0.0, -1.0);
}

fn sample_main(uv: vec2<f32>) -> vec4<f32> {
    return textureSample(main_tex, main_samp, uv);
}

fn gamma_to_linear(c: vec3<f32>) -> vec3<f32> {
    // Unity's GammaToLinearSpace (exact sRGB curve).
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

fn gamma_to_target(c: vec3<f32>) -> vec3<f32> {
    if is_linear() {
        return gamma_to_linear(c);
    }
    return c;
}

fn target_to_gamma(c: vec3<f32>) -> vec3<f32> {
    if is_linear() {
        return linear_to_gamma(c);
    }
    return c;
}

fn camera_position() -> vec3<f32> {
    let r = mat3x3<f32>(draw.view[0].xyz, draw.view[1].xyz, draw.view[2].xyz);
    return -(transpose(r) * draw.view[3].xyz);
}

// --- The common vertex stage -------------------------------------------------------------------

@vertex
fn vs_main(v: VertexIn) -> VertexOut {
    var o: VertexOut;
    let world = draw.model * vec4<f32>(v.position, 1.0);
    o.clip = to_target(draw.proj * (draw.view * world));
    o.color = v.color * draw.color;
    o.uv0 = v.uv0;
    o.uv1 = v.uv1;
    o.uv2 = v.uv2;
    o.world_normal = safe_normalize((draw.model * vec4<f32>(v.normal, 0.0)).xyz);
    o.world_pos = world.xyz;
    o.p0 = vec4<f32>(0.0);
    o.p1 = vec4<f32>(0.0);
    o.p2 = vec4<f32>(0.0);
    o.c2 = vec4<f32>(0.0);
    if has(K_VERTEX_COLOR_GAMMA) {
        // _UIVertexColorAlwaysGammaSpace: the canvas keeps vertex colors in gamma space.
        o.color = vec4<f32>(gamma_to_target(o.color.rgb), o.color.a);
    }
    return o;
}

// --- UI/Default and friends --------------------------------------------------------------------
// fs_ui: UI/Default as Unity 6 ships it (premultiplied output, Blend One OneMinusSrcAlpha);
// fs_tmp_sprite / fs_tmp_bitmap: TextMeshPro/Sprite and TextMeshPro/Bitmap (straight alpha).

fn main_sample_add() -> vec4<f32> {
    // The UI system adds (1,1,1,0) when it draws an alpha-only texture (fonts).
    if draw.misc.z > 0.5 {
        return vec4<f32>(1.0, 1.0, 1.0, 0.0) + mat.sample_add;
    }
    return mat.sample_add;
}

@fragment
fn fs_ui(in: VertexOut) -> @location(0) vec4<f32> {
    var color = in.color * mat.color;
    // Round the interpolated alpha to 1/255 steps like UI/Default does.
    color.a = round(color.a * 255.0) / 255.0;
    color = color * (sample_main(transform_tex(in.uv0.xy, mat.main_st)) + main_sample_add());
    if has(K_ALPHACLIP) && color.a < 0.001 {
        discard;
    }
    if has(K_PREMULTIPLY_OUTPUT) {
        color = vec4<f32>(color.rgb * color.a, color.a);
    }
    return color;
}

@fragment
fn fs_tmp_sprite(in: VertexOut) -> @location(0) vec4<f32> {
    let color = (sample_main(transform_tex(in.uv0.xy, mat.main_st)) + main_sample_add()) * in.color * mat.color;
    if has(K_ALPHACLIP) && color.a < 0.001 {
        discard;
    }
    return color;
}

@fragment
fn fs_tmp_bitmap(in: VertexOut) -> @location(0) vec4<f32> {
    let vc = in.color * mat.color;
    let t = sample_main(in.uv0.xy);
    let face = textureSample(tex_a, samp_a, transform_tex(in.uv1.xy, mat.st_a));
    let color = vec4<f32>(face.rgb * vc.rgb, vc.a * t.a);
    if has(K_ALPHACLIP) && color.a < 0.001 {
        discard;
    }
    return color;
}

// --- Sprites/Default ----------------------------------------------------------------------------
// variant 0: Sprites/Default, 1: sprite mask (alpha cutoff, writes stencil only).

@fragment
fn fs_sprite(in: VertexOut) -> @location(0) vec4<f32> {
    var t = sample_main(in.uv0.xy);
    if mat.p0.w > 0.5 {
        // _EnableExternalAlpha with _AlphaTex.
        let alpha = textureSample(tex_a, samp_a, in.uv0.xy);
        t.a = mix(t.a, alpha.r, mat.p0.w);
    }
    if mat.flags.y == 1u {
        if t.a * in.color.a < mat.p0.x {
            discard;
        }
        return vec4<f32>(1.0, 1.0, 1.0, 1.0);
    }
    var c = t * in.color * mat.color * mat.tint;
    c = vec4<f32>(c.rgb * c.a, c.a);
    return c;
}

// --- Unlit/* -----------------------------------------------------------------------------------
// variant 0: Unlit/Texture (and Mobile/Unlit), 1: Unlit/Color, 2: Unlit/Transparent,
// 3: Unlit/Transparent Cutout.

@fragment
fn fs_unlit(in: VertexOut) -> @location(0) vec4<f32> {
    let variant = mat.flags.y;
    var c: vec4<f32>;
    if variant == 1u {
        c = mat.color;
    } else {
        c = sample_main(transform_tex(in.uv0.xy, mat.main_st));
    }
    c = c * draw.color;
    if variant == 3u && c.a - mat.p0.x < 0.0 {
        discard;
    }
    return c;
}

// --- Lit: Legacy Shaders/Diffuse, Mobile/Diffuse, Standard (approximation) ---------------------
// variant 0: Legacy Diffuse (_Color), 1: Mobile/Diffuse, 2: Standard.
// One default directional light (Unity's default scene light: rotation (50, -30, 0)) and a
// hemispherical ambient.

const LIGHT_DIR: vec3<f32> = vec3<f32>(0.3213938, 0.7660444, -0.5566704);
const LIGHT_COLOR: vec3<f32> = vec3<f32>(1.0, 0.9568627, 0.8392157);
const AMBIENT_SKY: vec3<f32> = vec3<f32>(0.45, 0.5, 0.6);
const AMBIENT_GROUND: vec3<f32> = vec3<f32>(0.2, 0.2, 0.2);

@fragment
fn fs_lit(in: VertexOut, @builtin(front_facing) front: bool) -> @location(0) vec4<f32> {
    let variant = mat.flags.y;
    var albedo = sample_main(transform_tex(in.uv0.xy, mat.main_st));
    if variant != 1u {
        albedo = albedo * mat.color;
    }
    albedo = albedo * draw.color;
    if variant == 2u && has(K_ALPHATEST_ON) && albedo.a < mat.p0.x {
        discard;
    }
    var n = safe_normalize(in.world_normal);
    if !front {
        n = -n;
    }
    var light = LIGHT_COLOR;
    var sky = AMBIENT_SKY;
    var ground = AMBIENT_GROUND;
    if is_linear() {
        light = gamma_to_linear(light);
        sky = gamma_to_linear(sky);
        ground = gamma_to_linear(ground);
    }
    let ambient = mix(ground, sky, n.y * 0.5 + 0.5);
    var rgb = albedo.rgb * (ambient + light * max(dot(n, LIGHT_DIR), 0.0));
    var alpha = albedo.a;
    if variant == 2u {
        // Metallic surfaces reflect less diffuse light.
        rgb = rgb * (1.0 - 0.96 * mat.p0.y);
        if has(K_ALPHAPREMULTIPLY_ON) {
            rgb = rgb * alpha;
        }
        if has(K_EMISSION) {
            rgb = rgb + mat.emission.rgb * textureSample(tex_a, samp_a, transform_tex(in.uv0.xy, mat.main_st)).rgb;
        }
        if !(has(K_ALPHABLEND_ON) || has(K_ALPHAPREMULTIPLY_ON)) {
            alpha = 1.0;
        }
    }
    return vec4<f32>(rgb, alpha);
}

// --- Particles ---------------------------------------------------------------------------------
// variant 0: legacy Additive / Alpha Blended (2 * color * _TintColor * tex), 1: legacy Alpha
// Blended Premultiply, 2: mobile particles (tex * color), 3: Particles/Standard Unlit,
// 4: legacy Multiply.

@fragment
fn fs_particle(in: VertexOut) -> @location(0) vec4<f32> {
    let variant = mat.flags.y;
    let t = sample_main(transform_tex(in.uv0.xy, mat.main_st));
    if variant == 0u {
        var c = 2.0 * in.color * mat.tint * t;
        c.a = clamp(c.a, 0.0, 1.0);
        return c;
    }
    if variant == 1u {
        return in.color * t * in.color.a;
    }
    if variant == 2u {
        return t * in.color;
    }
    if variant == 4u {
        let prev = in.color * t;
        return mix(vec4<f32>(1.0), prev, prev.a);
    }
    var c = t * mat.color * in.color;
    if has(K_ALPHATEST_ON) && c.a < mat.p0.x {
        discard;
    }
    if has(K_ALPHAPREMULTIPLY_ON) {
        c = vec4<f32>(c.rgb * c.a, c.a);
    }
    if has(K_ALPHAMODULATE_ON) {
        c = vec4<f32>(mix(vec3<f32>(1.0), c.rgb, c.a), c.a);
    }
    if !(has(K_ALPHATEST_ON) || has(K_ALPHABLEND_ON) || has(K_ALPHAPREMULTIPLY_ON) || has(K_ALPHAMODULATE_ON)) {
        c.a = 1.0;
    }
    return c;
}

// --- TextMeshPro SDF ---------------------------------------------------------------------------
// Ports of TMP_SDF.shader (variant 0) and TMP_SDF-Mobile.shader (variant 1), with
// face/outline/underlay/glow, softness and dilate. UV0.w carries the SDF scale (negative = bold),
// UV1 the face/outline texture coordinates.
//
// Material slots: color = _FaceColor, color2 = _OutlineColor, color3 = _UnderlayColor,
// color4 = _GlowColor, st_a = _FaceTex_ST, st_b = _OutlineTex_ST,
// p0 = (_FaceDilate, _OutlineWidth, _OutlineSoftness, _WeightNormal),
// p1 = (_WeightBold, _ScaleRatioA, _ScaleRatioB, _ScaleRatioC),
// p2 = (_GradientScale, _ScaleX, _ScaleY, _PerspectiveFilter),
// p3 = (_Sharpness, _TextureWidth, _TextureHeight, _VertexOffsetX),
// p4 = (_VertexOffsetY, _UnderlayOffsetX, _UnderlayOffsetY, _UnderlayDilate),
// p5 = (_UnderlaySoftness, _GlowOffset, _GlowInner, _GlowOuter),
// p6 = (_GlowPower, _FaceUVSpeedX, _FaceUVSpeedY, _OutlineUVSpeedX), p7 = (_OutlineUVSpeedY, ...).

@vertex
fn vs_tmp(v: VertexIn) -> VertexOut {
    var o: VertexOut;
    let mobile = mat.flags.y == 1u;
    let face_dilate = mat.p0.x;
    let outline_width = mat.p0.y;
    let outline_softness = mat.p0.z;
    let ratio_a = mat.p1.y;
    let ratio_b = mat.p1.z;
    let ratio_c = mat.p1.w;
    let gradient_scale = mat.p2.x;
    let underlay_dilate = mat.p4.w;
    let underlay_softness = mat.p5.x;

    let bold = select(0.0, 1.0, v.uv0.w <= 0.0);
    var vert = vec4<f32>(v.position, 1.0);
    vert.x = vert.x + mat.p3.w;
    vert.y = vert.y + mat.p4.x;
    let world = draw.model * vert;
    let vpos = draw.proj * (draw.view * world);
    o.clip = to_target(vpos);

    let p2x2 = mat2x2<f32>(draw.proj[0].xy, draw.proj[1].xy);
    var pixel_size = vec2<f32>(vpos.w) / (vec2<f32>(mat.p2.y, mat.p2.z) * abs(p2x2 * draw.screen.xy));
    var scale = inverseSqrt(dot(pixel_size, pixel_size));
    scale = scale * abs(v.uv0.w) * gradient_scale * (mat.p3.x + 1.0);
    if draw.proj[3][3] == 0.0 {
        let n = safe_normalize((draw.model * vec4<f32>(v.normal, 0.0)).xyz);
        let view_dir = safe_normalize(camera_position() - world.xyz);
        scale = mix(abs(scale) * (1.0 - mat.p2.w), scale, abs(dot(n, view_dir)));
    }
    var weight = mix(mat.p0.w, mat.p1.x, bold) / 4.0;
    weight = (weight + face_dilate) * ratio_a * 0.5;

    let color = v.color * draw.color;
    let underlay = has(K_UNDERLAY_ON) || has(K_UNDERLAY_INNER);
    let offset = vec2<f32>(
        -(mat.p4.y * ratio_c) * gradient_scale / mat.p3.y,
        -(mat.p4.z * ratio_c) * gradient_scale / mat.p3.z,
    );
    o.world_normal = vec3<f32>(0.0);
    o.world_pos = world.xyz;
    o.uv2 = vec4<f32>(0.0);

    if mobile {
        var layer_scale = scale;
        scale = scale / (1.0 + outline_softness * ratio_a * scale);
        let bias = (0.5 - weight) * scale - 0.5;
        let outline = outline_width * ratio_a * 0.5 * scale;
        var opacity = color.a;
        if underlay {
            opacity = 1.0;
        }
        var face = vec4<f32>(color.rgb, opacity) * mat.color;
        face = vec4<f32>(face.rgb * face.a, face.a);
        var outline_color = mat.color2;
        outline_color.a = outline_color.a * opacity;
        outline_color = vec4<f32>(outline_color.rgb * outline_color.a, outline_color.a);
        outline_color = mix(face, outline_color, sqrt(min(1.0, outline * 2.0)));
        layer_scale = layer_scale / (1.0 + underlay_softness * ratio_c * layer_scale);
        let layer_bias = (0.5 - weight) * layer_scale - 0.5 - underlay_dilate * ratio_c * 0.5 * layer_scale;
        o.color = face;
        o.c2 = outline_color;
        o.uv0 = vec4<f32>(v.uv0.xy, 0.0, 0.0);
        o.p0 = vec4<f32>(scale, bias - outline, bias + outline, bias);
        o.p1 = vec4<f32>(v.uv0.xy + offset, color.a, 0.0);
        o.p2 = vec4<f32>(layer_scale, layer_bias, 0.0, 0.0);
        o.uv1 = vec4<f32>(0.0);
    } else {
        let bias = (0.5 - weight) + (0.5 / scale);
        var alpha_clip = 1.0 - outline_width * ratio_a - outline_softness * ratio_a;
        if has(K_GLOW_ON) {
            alpha_clip = min(alpha_clip, 1.0 - mat.p5.y * ratio_b - mat.p5.w * ratio_b);
        }
        alpha_clip = alpha_clip / 2.0 - (0.5 / scale) - weight;
        var b_scale = scale;
        b_scale = b_scale / (1.0 + underlay_softness * ratio_c * b_scale);
        let b_bias = (0.5 - weight) * b_scale - 0.5 - underlay_dilate * ratio_c * 0.5 * b_scale;
        let underlay_color = vec4<f32>(mat.color3.rgb * mat.color3.a, mat.color3.a);
        o.color = color;
        o.uv0 = vec4<f32>(v.uv0.xy, 0.0, 0.0);
        o.p0 = vec4<f32>(alpha_clip, scale, bias, weight);
        o.p1 = vec4<f32>(v.uv0.xy + offset, b_scale, b_bias);
        o.c2 = underlay_color;
        o.p2 = vec4<f32>(transform_tex(v.uv1.xy, mat.st_a), transform_tex(v.uv1.xy, mat.st_b));
        o.uv1 = vec4<f32>(0.0);
    }
    return o;
}

fn tmp_get_color(d: f32, face_in: vec4<f32>, outline_in: vec4<f32>, outline: f32, softness: f32) -> vec4<f32> {
    let face_alpha = 1.0 - clamp((d - outline * 0.5 + softness * 0.5) / (1.0 + softness), 0.0, 1.0);
    let outline_alpha = clamp(d + outline * 0.5, 0.0, 1.0) * sqrt(min(1.0, outline));
    let face = vec4<f32>(face_in.rgb * face_in.a, face_in.a);
    let outline_color = vec4<f32>(outline_in.rgb * outline_in.a, outline_in.a);
    return mix(face, outline_color, outline_alpha) * face_alpha;
}

fn tmp_glow_color(d: f32, scale: f32) -> vec4<f32> {
    let ratio_b = mat.p1.z;
    var glow = d - (mat.p5.y * ratio_b) * 0.5 * scale;
    let t = mix(mat.p5.z, mat.p5.w * ratio_b, step(0.0, glow)) * 0.5 * scale;
    glow = clamp(abs(glow / (1.0 + t)), 0.0, 1.0);
    glow = 1.0 - pow(glow, mat.p6.x);
    glow = glow * sqrt(min(1.0, t));
    return vec4<f32>(mat.color4.rgb, clamp(mat.color4.a * glow * 2.0, 0.0, 1.0));
}

@fragment
fn fs_tmp(in: VertexOut) -> @location(0) vec4<f32> {
    let underlay_on = has(K_UNDERLAY_ON);
    let underlay_inner = has(K_UNDERLAY_INNER);
    if mat.flags.y == 1u {
        // TMP_SDF-Mobile.shader
        var d = sample_main(in.uv0.xy).a * in.p0.x;
        var c = in.color * clamp(d - in.p0.w, 0.0, 1.0);
        if has(K_OUTLINE_ON) {
            c = mix(in.c2, in.color, clamp(d - in.p0.z, 0.0, 1.0));
            c = c * clamp(d - in.p0.y, 0.0, 1.0);
        }
        let underlay_color = vec4<f32>(mat.color3.rgb * mat.color3.a, mat.color3.a);
        if underlay_on {
            d = sample_main(in.p1.xy).a * in.p2.x;
            c = c + underlay_color * clamp(d - in.p2.y, 0.0, 1.0) * (1.0 - c.a);
        }
        if underlay_inner {
            let sd = clamp(d - in.p0.z, 0.0, 1.0);
            d = sample_main(in.p1.xy).a * in.p2.x;
            c = c + underlay_color * (1.0 - clamp(d - in.p2.y, 0.0, 1.0)) * sd * (1.0 - c.a);
        }
        if underlay_on || underlay_inner {
            c = c * in.p1.z;
        }
        if has(K_ALPHACLIP) && c.a < 0.001 {
            discard;
        }
        return c;
    }

    // TMP_SDF.shader
    let ratio_a = mat.p1.y;
    let c = sample_main(in.uv0.xy).a;
    if !underlay_on && c < in.p0.x {
        discard;
    }
    let scale = in.p0.y;
    let bias = in.p0.z;
    let sd = (bias - c) * scale;
    let outline = (mat.p0.y * ratio_a) * scale;
    let softness = (mat.p0.z * ratio_a) * scale;
    let time = draw.misc.x;
    var face_color = mat.color;
    var outline_color = mat.color2;
    face_color = vec4<f32>(face_color.rgb * in.color.rgb, face_color.a);
    face_color = face_color * textureSample(tex_a, samp_a, in.p2.xy + vec2<f32>(mat.p6.y, mat.p6.z) * time);
    outline_color = outline_color * textureSample(tex_b, samp_b, in.p2.zw + vec2<f32>(mat.p6.w, mat.p7.x) * time);
    face_color = tmp_get_color(sd, face_color, outline_color, outline, softness);
    if underlay_on {
        let d = sample_main(in.p1.xy).a * in.p1.z;
        face_color = face_color + in.c2 * clamp(d - in.p1.w, 0.0, 1.0) * (1.0 - face_color.a);
    }
    if underlay_inner {
        let d = sample_main(in.p1.xy).a * in.p1.z;
        face_color = face_color + in.c2 * (1.0 - clamp(d - in.p1.w, 0.0, 1.0)) * clamp(1.0 - sd, 0.0, 1.0) * (1.0 - face_color.a);
    }
    if has(K_GLOW_ON) {
        let glow = tmp_glow_color(sd, scale);
        face_color = vec4<f32>(face_color.rgb + glow.rgb * glow.a, face_color.a);
    }
    if has(K_ALPHACLIP) && face_color.a < 0.001 {
        discard;
    }
    return face_color * in.color.a;
}

// --- Spine -------------------------------------------------------------------------------------
// variant 0: Spine/Skeleton, 1: Spine blend modes (PMA _Color), 2: Skeleton Tint, 3: Skeleton
// Tint Black, 4: Skeleton Fill, 5: Special/Skeleton Grayscale, 6: SkeletonGraphic,
// 7: SkeletonGraphic Tint Black. color = _Color, color2 = _Black or _FillColor,
// p0.z = _FillPhase / _GrayPhase.

fn pma_gamma_to_target(c: vec4<f32>, saturated: bool) -> vec4<f32> {
    if !is_linear() {
        return c;
    }
    if c.a == 0.0 {
        return vec4<f32>(gamma_to_linear(c.rgb), c.a);
    }
    var rgb = gamma_to_linear(c.rgb / c.a);
    if saturated {
        rgb = clamp(rgb, vec3<f32>(0.0), vec3<f32>(1.0));
    }
    return vec4<f32>(rgb * c.a, c.a);
}

fn spine_tinted(tex: vec4<f32>, dark_tint: vec3<f32>, light_pma: vec4<f32>, light_alpha: f32, dark_alpha: f32) -> vec4<f32> {
    let a = tex.a * light_pma.a;
    var tex_dark: vec3<f32>;
    if has(K_STRAIGHT_ALPHA) {
        tex_dark = 1.0 - tex.rgb;
    } else {
        tex_dark = tex.a - tex.rgb;
    }
    let dark = tex_dark * dark_tint * light_alpha;
    let light = tex.rgb * light_pma.rgb;
    var frag = vec4<f32>(dark + light, a);
    if has(K_STRAIGHT_ALPHA) {
        frag = vec4<f32>(frag.rgb * tex.a, frag.a);
    }
    if has(K_DARK_COLOR_ALPHA_ADDITIVE) {
        frag.a = a * (1.0 - dark_alpha);
    }
    return frag;
}

@fragment
fn fs_spine(in: VertexOut) -> @location(0) vec4<f32> {
    let variant = mat.flags.y;
    var tex = sample_main(in.uv0.xy);
    let pma_color = vec4<f32>(mat.color.rgb * mat.color.a, mat.color.a);
    if variant >= 6u {
        // SkeletonGraphic: CanvasRenderer already converted vertex colors as straight alpha.
        var vc: vec4<f32>;
        var canvas_alpha = 1.0;
        if variant == 7u {
            let original_alpha = select(in.color.a, in.uv2.y, has(K_CANVAS_GROUP_COMPATIBLE));
            if has(K_CANVAS_GROUP_COMPATIBLE) {
                canvas_alpha = select(in.color.a / original_alpha, in.color.a, original_alpha == 0.0);
            }
            vc = pma_gamma_to_target(vec4<f32>(target_to_gamma(in.color.rgb), original_alpha), false) * pma_color;
            tex = tex + mat.sample_add;
            if has(K_ALPHACLIP) && tex.a < 0.001 {
                discard;
            }
            let dark = gamma_to_target(vec3<f32>(in.uv1.x, in.uv1.y, in.uv2.x)) + mat.color2.rgb;
            return spine_tinted(tex, dark, vc, mat.color.a, mat.color2.a) * canvas_alpha;
        }
        if has(K_CANVAS_GROUP_COMPATIBLE) {
            vc = vec4<f32>(in.color.rgb * in.color.a, in.color.a);
        } else {
            vc = pma_gamma_to_target(vec4<f32>(target_to_gamma(in.color.rgb), in.color.a), true);
        }
        vc = vc * pma_color;
        if has(K_STRAIGHT_ALPHA) {
            tex = vec4<f32>(tex.rgb * tex.a, tex.a);
        }
        var c = (tex + mat.sample_add) * vc;
        if has(K_ALPHACLIP) && c.a < 0.001 {
            discard;
        }
        if has(K_ENABLE_FILL) {
            c = vec4<f32>(mix(c.rgb, mat.color2.rgb * c.a, mat.p0.z), c.a);
        }
        return c;
    }
    let vc = pma_gamma_to_target(in.color, false);
    if variant == 2u {
        return spine_tinted(tex, mat.color2.rgb, vc * pma_color, mat.color.a, mat.color2.a);
    }
    if variant == 3u {
        let dark = gamma_to_target(vec3<f32>(in.uv1.x, in.uv1.y, in.uv2.x)) + mat.color2.rgb;
        return spine_tinted(tex, dark, vc * pma_color, mat.color.a, mat.color2.a);
    }
    if variant == 4u {
        let final_alpha = tex.a * vc.a;
        if has(K_STRAIGHT_ALPHA) {
            tex = vec4<f32>(tex.rgb * tex.a, tex.a);
        }
        let rgb = mix(tex.rgb * vc.rgb, mat.color2.rgb * final_alpha, mat.p0.z);
        return vec4<f32>(rgb, final_alpha);
    }
    if variant == 5u {
        if has(K_STRAIGHT_ALPHA) {
            tex = vec4<f32>(tex.rgb * tex.a, tex.a);
        }
        let final_alpha = tex.a * vc.a;
        let rgb = tex.rgb * vc.rgb;
        let gray = dot(rgb, vec3<f32>(0.3, 0.59, 0.11));
        return vec4<f32>(mix(rgb, vec3<f32>(gray), mat.p0.z), final_alpha);
    }
    if has(K_STRAIGHT_ALPHA) {
        tex = vec4<f32>(tex.rgb * tex.a, tex.a);
    }
    if variant == 1u {
        return tex * vc * pma_color;
    }
    return tex * vc;
}

// --- Generic: any other shader -----------------------------------------------------------------
// tex(_MainTex) * _Color * _TintColor * vertex color; premultiplied when the shader blends with
// One OneMinusSrcAlpha; alpha test with _ALPHATEST_ON.

@fragment
fn fs_generic(in: VertexOut) -> @location(0) vec4<f32> {
    var c = sample_main(transform_tex(in.uv0.xy, mat.main_st)) * mat.color * mat.tint * in.color;
    if has(K_ALPHATEST_ON) && c.a < mat.p0.x {
        discard;
    }
    if has(K_PREMULTIPLY_OUTPUT) {
        c = vec4<f32>(c.rgb * c.a, c.a);
    }
    return c;
}

// --- Camera clears inside a viewport -----------------------------------------------------------

struct ClearOut {
    @builtin(position) clip: vec4<f32>,
}

@vertex
fn vs_clear(@builtin(vertex_index) i: u32) -> ClearOut {
    var o: ClearOut;
    let uv = vec2<f32>(f32((i << 1u) & 2u), f32(i & 2u));
    // Depth 1 (far): clears the depth buffer as well.
    o.clip = vec4<f32>(uv * 2.0 - 1.0, 1.0, 1.0);
    return o;
}

@fragment
fn fs_clear() -> @location(0) vec4<f32> {
    return draw.color;
}
