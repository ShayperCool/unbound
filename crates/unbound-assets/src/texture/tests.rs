use super::*;

fn settings(meta: &str, target: &str) -> TextureSettings {
    TextureSettings::read(&unbound_yaml::parse_value(meta).unwrap(), target)
}

/// A Unity 6000.0 meta (serializedVersion 13), trimmed.
const META_6000: &str = r#"fileFormatVersion: 2
guid: aa68192ef33b323e400536404a0298f3
TextureImporter:
  internalIDToNameTable: []
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: 0
    linearTexture: 0
  isReadable: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 0
    aniso: 4
    mipBias: 0.5
    wrapU: 1
    wrapV: 0
    wrapW: 2
  nPOTScale: 3
  alphaUsage: 2
  alphaIsTransparency: 1
  textureType: 0
  textureShape: 1
  platformSettings:
  - serializedVersion: 3
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 4096
    resizeAlgorithm: 1
    overridden: 0
  - serializedVersion: 3
    buildTarget: Standalone
    maxTextureSize: 512
    resizeAlgorithm: 0
    overridden: 0
  - serializedVersion: 3
    buildTarget: Android
    maxTextureSize: 256
    resizeAlgorithm: 0
    overridden: 1
"#;

#[test]
fn reads_6000_settings() {
    let s = settings(META_6000, "Standalone");
    assert_eq!(s.texture_type, TextureType::Default);
    assert_eq!(s.shape, TextureShape::Texture2D);
    assert!(!s.srgb);
    assert!(s.mipmaps);
    assert!(s.is_readable);
    assert_eq!(s.alpha_source, AlphaSource::FromGrayScale);
    assert!(s.alpha_is_transparency);
    assert_eq!(s.npot_scale, NpotScale::ToSmaller);
    assert_eq!(
        (s.wrap_u, s.wrap_v, s.wrap_w),
        (WrapMode::Clamp, WrapMode::Repeat, WrapMode::Mirror)
    );
    assert_eq!(s.filter, FilterMode::Point);
    assert_eq!(s.aniso, 4);
    assert_eq!(s.mip_bias, 0.5);
    // Standalone is not overridden: the default platform entry applies (not the legacy 2048).
    assert_eq!(s.max_size, 4096);
    assert_eq!(s.resize_algorithm, ResizeAlgorithm::Bilinear);
    // Android is overridden.
    let android = settings(META_6000, "Android");
    assert_eq!(android.max_size, 256);
    assert_eq!(android.resize_algorithm, ResizeAlgorithm::Mitchell);
}

/// A Unity 5.4 meta (serializedVersion 2): `linearTexture`, `wrapMode`, `-1` defaults,
/// `buildTargetSettings`.
const META_54: &str = r#"fileFormatVersion: 2
guid: b93330fc8ea08407dbc514b5101afa14
TextureImporter:
  fileIDToRecycleName: {}
  serializedVersion: 2
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    linearTexture: 1
    correctGamma: 0
  isReadable: 0
  grayScaleToAlpha: 1
  maxTextureSize: 1024
  textureSettings:
    filterMode: -1
    aniso: -1
    mipBias: -1
    wrapMode: -1
  nPOTScale: 1
  spriteMode: 0
  textureType: 2
  buildTargetSettings:
  - buildTarget: Standalone
    maxTextureSize: 128
    textureFormat: -1
"#;

#[test]
fn reads_legacy_settings() {
    let s = settings(META_54, "Android");
    assert_eq!(s.texture_type, TextureType::Gui);
    assert!(!s.srgb);
    assert!(!s.mipmaps);
    assert_eq!(s.alpha_source, AlphaSource::FromGrayScale);
    // GUI textures are never NPOT-scaled and clamp by default.
    assert_eq!(s.npot_scale, NpotScale::None);
    assert_eq!(s.wrap_u, WrapMode::Clamp);
    assert_eq!(s.filter, FilterMode::Bilinear);
    assert_eq!(s.aniso, 1);
    assert_eq!(s.mip_bias, 0.0);
    assert_eq!(s.max_size, 1024);
    // `buildTargetSettings` lists overridden targets only.
    assert_eq!(settings(META_54, "Standalone").max_size, 128);
}

#[test]
fn defaults_without_a_meta() {
    let s = TextureSettings::default();
    assert_eq!(s.texture_type, TextureType::Default);
    assert!(s.srgb && s.mipmaps && !s.is_readable);
    assert_eq!(s.npot_scale, NpotScale::ToNearest);
    assert_eq!(s.max_size, 2048);
    assert_eq!(s.wrap_u, WrapMode::Repeat);
    assert_eq!(s.sprite.mode, crate::SpriteMode::None);
}

#[test]
fn normal_maps_are_linear() {
    let s = settings(
        "TextureImporter:\n  mipmaps:\n    sRGBTexture: 1\n  textureType: 1\n",
        "Standalone",
    );
    assert_eq!(s.texture_type, TextureType::NormalMap);
    assert!(!s.srgb);
}

#[test]
fn npot_scaling() {
    use NpotScale::*;
    assert_eq!(npot_size(256, 128, ToNearest), (256, 128));
    assert_eq!(npot_size(300, 100, None), (300, 100));
    assert_eq!(npot_size(300, 100, ToNearest), (256, 128));
    assert_eq!(npot_size(384, 96, ToNearest), (512, 128)); // ties go up
    assert_eq!(npot_size(300, 100, ToLarger), (512, 128));
    assert_eq!(npot_size(300, 100, ToSmaller), (256, 64));
    assert_eq!(npot_size(1, 3, ToSmaller), (1, 2));
}

#[test]
fn max_size_fit() {
    assert_eq!(fit_max_size(1024, 512, 2048), (1024, 512));
    assert_eq!(fit_max_size(4096, 2048, 1024), (1024, 512));
    assert_eq!(fit_max_size(2688, 1242, 2048), (2048, 946));
    assert_eq!(fit_max_size(1242, 2688, 1024), (473, 1024));
    assert_eq!(fit_max_size(5000, 2, 32), (32, 1));
}

fn png(width: u32, height: u32, pixel: impl Fn(u32, u32) -> [u8; 4]) -> Vec<u8> {
    let image = image::RgbaImage::from_fn(width, height, |x, y| image::Rgba(pixel(x, y)));
    let mut out = std::io::Cursor::new(Vec::new());
    image.write_to(&mut out, image::ImageFormat::Png).unwrap();
    out.into_inner()
}

#[test]
fn imports_bottom_up_with_mips() {
    // Top row red, bottom row blue.
    let bytes = png(4, 2, |_, y| {
        if y == 0 {
            [255, 0, 0, 255]
        } else {
            [0, 0, 255, 255]
        }
    });
    let meta = unbound_yaml::parse_value("TextureImporter:\n  nPOTScale: 0\n").unwrap();
    let tex = TextureImporter::default()
        .import(Path::new("Assets/a.png"), &bytes, &meta)
        .unwrap();
    assert_eq!((tex.width, tex.height), (4, 2));
    assert_eq!(tex.name, "a");
    assert_eq!(tex.mips.len(), 3); // 4x2, 2x1, 1x1
    assert_eq!(
        tex.mips
            .iter()
            .map(|m| (m.width, m.height))
            .collect::<Vec<_>>(),
        [(4, 2), (2, 1), (1, 1)]
    );
    // Row 0 is the bottom row.
    assert_eq!(&tex.mips[0].data[..4], &[0, 0, 255, 255]);
    assert_eq!(&tex.mips[0].data[16..20], &[255, 0, 0, 255]);
    // sRGB-correct average of red and blue: linear 0.5 encodes to 188, not 128.
    assert_eq!(&tex.mips[1].data[..4], &[188, 0, 188, 255]);
    assert!(!tex.has_alpha);
    assert!(tex.sprites.is_empty());
}

#[test]
fn downscales_to_max_size_and_keeps_original_size() {
    let bytes = png(64, 32, |x, _| [x as u8 * 4, 0, 0, 255]);
    let meta = unbound_yaml::parse_value(
        "TextureImporter:\n  mipmaps:\n    enableMipMap: 0\n  maxTextureSize: 32\n  textureType: 8\n  spriteMode: 1\n",
    )
    .unwrap();
    let tex = TextureImporter::default()
        .import(Path::new("s.png"), &bytes, &meta)
        .unwrap();
    assert_eq!((tex.width, tex.height), (32, 16));
    assert_eq!((tex.original_width, tex.original_height), (64, 32));
    assert_eq!(tex.mips.len(), 1);
    let sprite = tex.sprite(crate::sprite::SINGLE_SPRITE_FILE_ID).unwrap();
    assert_eq!(sprite.rect.width, 64.0);
    assert_eq!(sprite.texture_rect.width, 32.0);
}

#[test]
fn alpha_source_none_is_opaque_and_dilation_fills_transparent_colour() {
    // A 3x1 image: opaque green, transparent black, transparent black.
    let bytes = png(3, 1, |x, _| {
        if x == 0 {
            [0, 255, 0, 255]
        } else {
            [0, 0, 0, 0]
        }
    });
    let dilated = TextureImporter::default()
        .import(
            Path::new("d.png"),
            &bytes,
            &unbound_yaml::parse_value(
                "TextureImporter:\n  mipmaps:\n    enableMipMap: 0\n  nPOTScale: 0\n  alphaIsTransparency: 1\n",
            )
            .unwrap(),
        )
        .unwrap();
    // The jump flood takes max(w, h) / 2 = 1 step here: the texel next to the visible
    // one takes its colour, the one two texels away is never reached and keeps its own.
    assert_eq!(
        dilated.mips[0].data,
        [0, 255, 0, 255, 0, 255, 0, 0, 0, 0, 0, 0]
    );
    assert!(dilated.has_alpha);

    let opaque = TextureImporter::default()
        .import(
            Path::new("d.png"),
            &bytes,
            &unbound_yaml::parse_value(
                "TextureImporter:\n  mipmaps:\n    enableMipMap: 0\n  nPOTScale: 0\n  alphaUsage: 0\n",
            )
            .unwrap(),
        )
        .unwrap();
    assert_eq!(
        opaque.mips[0].data,
        [0, 255, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255]
    );
    assert!(!opaque.has_alpha);
}

#[test]
fn decodes_color_mapped_tga() {
    // 2x2, RLE colour-mapped (type 9), 8-bit indices into a 24-bit BGR palette, bottom-up rows:
    // bottom row red, red; top row blue, green.
    let mut tga = vec![0, 1, 9, 0, 0, 3, 0, 24, 0, 0, 0, 0, 2, 0, 2, 0, 8, 0];
    tga.extend_from_slice(&[
        0, 0, 255, /* red */ 0, 255, 0, /* green */ 255, 0, 0, /* blue */
    ]);
    tga.extend_from_slice(&[0x81, 0]); // run of 2 × red
    tga.extend_from_slice(&[0x01, 2, 1]); // raw: blue, green
    let meta =
        unbound_yaml::parse_value("TextureImporter:\n  mipmaps:\n    enableMipMap: 0\n").unwrap();
    let tex = TextureImporter::default()
        .import(Path::new("c.tga"), &tga, &meta)
        .unwrap();
    // Unity's order is bottom-up too.
    assert_eq!(
        tex.mips[0].data,
        [
            255, 0, 0, 255, 255, 0, 0, 255, 0, 0, 255, 255, 0, 255, 0, 255
        ]
    );
}

#[test]
fn settings_hash_tracks_settings() {
    let a = settings(META_6000, "Standalone");
    let b = settings(META_6000, "Android");
    assert_eq!(a.hash(), settings(META_6000, "Standalone").hash());
    assert_ne!(a.hash(), b.hash());
}

/// A 6:1 strip imports as six faces in Unity's order (+X, -X, +Y, -Y, +Z, -Z), each with its mips.
#[test]
fn a_strip_imports_as_a_cubemap() {
    let colors: [[u8; 4]; 6] = [
        [255, 0, 0, 255],
        [0, 255, 0, 255],
        [0, 0, 255, 255],
        [255, 255, 0, 255],
        [0, 255, 255, 255],
        [255, 0, 255, 255],
    ];
    let (w, h) = (24u32, 4u32);
    let mut img = image::RgbaImage::new(w, h);
    for (x, _, p) in img.enumerate_pixels_mut() {
        *p = image::Rgba(colors[(x / 4) as usize]);
    }
    let mut png = Vec::new();
    img.write_to(&mut std::io::Cursor::new(&mut png), image::ImageFormat::Png)
        .unwrap();
    let meta = unbound_yaml::parse_value("TextureImporter:\n  textureShape: 2\n  generateCubemap: 6\n  mipmaps:\n    sRGBTexture: 0\n").unwrap();
    let tex = TextureImporter::default()
        .import(Path::new("Sky.png"), &png, &meta)
        .unwrap();
    assert_eq!((tex.width, tex.height, tex.layers), (4, 4, 6));
    assert_eq!(tex.mips.len(), 18); // 4x4, 2x2, 1x1 per face
    for (face, color) in colors.iter().enumerate() {
        assert_eq!(&tex.mips[face * 3].data[..4], color, "face {face}");
        assert_eq!(
            &tex.mips[face * 3 + 2].data[..4],
            color,
            "face {face}'s last mip"
        );
    }
}

/// A byte strip of one colour per face (faces 8 texels wide), imported as a cubemap.
fn byte_cube(convolution: i32, srgb: i32, mipmaps: i32) -> ImportedTexture {
    let colors = [
        [200u8, 30, 10, 255],
        [20, 180, 40, 255],
        [10, 20, 220, 255],
        [240, 240, 10, 255],
        [10, 230, 230, 255],
        [128, 64, 200, 255],
    ];
    let bytes = png(48, 8, |x, _| colors[(x / 8) as usize]);
    let meta = unbound_yaml::parse_value(&format!(
        "TextureImporter:\n  textureShape: 2\n  generateCubemap: 6\n  cubemapConvolution: {convolution}\n  mipmaps:\n    enableMipMap: {mipmaps}\n    sRGBTexture: {srgb}\n"
    ))
    .unwrap();
    TextureImporter::default()
        .import(Path::new("Cube.png"), &bytes, &meta)
        .unwrap()
}

/// Specular convolution of byte faces keeps bytes (and the sRGB setting), mip 0 unchanged, a
/// uniform face uniform deep inside; without mipmaps nothing is convolved.
#[test]
fn convolved_byte_cubes_stay_bytes() {
    for srgb in [0, 1] {
        let plain = byte_cube(0, srgb, 1);
        let tex = byte_cube(1, srgb, 1);
        assert_eq!(
            (tex.format, tex.srgb, tex.mips.len()),
            (TextureFormat::Rgba8, srgb == 1, 6 * 4)
        );
        for face in 0..6 {
            assert_eq!(
                tex.mips[face * 4].data,
                plain.mips[face * 4].data,
                "mip 0 of face {face}"
            );
        }
        // Convolved mips mix neighbouring faces: the corner texel of +Z's mip 1 differs from the
        // box-filtered one.
        let corner = &tex.mips[4 * 4 + 1].data;
        assert_ne!(&corner[..3], &plain.mips[4 * 4 + 1].data[..3]);
        assert!(
            tex.mips
                .iter()
                .all(|m| m.data.chunks(4).all(|p| p[3] == 255))
        );
    }
    let flat = byte_cube(1, 1, 0);
    assert_eq!(flat.mips.len(), 6);
    assert_eq!(flat.mips[0].data, byte_cube(0, 1, 0).mips[0].data);
}

/// An infinite texel spreads as infinity, never as NaN: reads of zero weight (a level or a
/// bilinear neighbour) leave it out.
#[test]
fn an_infinite_texel_never_turns_into_nan() {
    let size = 16u32;
    let mut data = Vec::new();
    for y in 0..size {
        for x in 0..size * 6 {
            let v = if x == 4 * size + 8 && y == 8 {
                f32::INFINITY
            } else {
                0.5
            };
            data.extend([v, v, v, 1.0].iter().flat_map(|c| c.to_le_bytes()));
        }
    }
    let image = Image {
        width: size * 6,
        height: size,
        format: TextureFormat::Rgba32F,
        data,
    };
    let chains = specular::convolve(&cubemap::faces(&image, cubemap::Layout::HorizontalStrip));
    let values = || {
        chains.iter().flatten().flat_map(|m| {
            m.data
                .chunks(4)
                .map(|c| f32::from_le_bytes(c.try_into().unwrap()))
        })
    };
    assert!(values().all(|v| !v.is_nan()));
    assert!(values().any(f32::is_infinite));
}

/// In a gamma project an HDR cube is gamma-encoded, also a convolved one too small to convolve
/// (one-texel faces).
#[test]
fn a_gamma_projects_hdr_cube_is_encoded_whatever_its_size() {
    for size in [1u32, 4] {
        let value = 0.25f32;
        let data: Vec<u8> = (0..size * size * 6)
            .flat_map(|_| [value, value, value, 1.0])
            .flat_map(f32::to_le_bytes)
            .collect();
        let mut exr = Vec::new();
        let img = image::Rgba32FImage::from_raw(
            size * 6,
            size,
            data.chunks(4)
                .map(|c| f32::from_le_bytes(c.try_into().unwrap()))
                .collect(),
        )
        .unwrap();
        image::DynamicImage::ImageRgba32F(img)
            .write_to(
                &mut std::io::Cursor::new(&mut exr),
                image::ImageFormat::OpenExr,
            )
            .unwrap();
        let meta = unbound_yaml::parse_value("TextureImporter:\n  textureShape: 2\n  generateCubemap: 6\n  cubemapConvolution: 1\n  mipmaps:\n    sRGBTexture: 1\n").unwrap();
        let importer = TextureImporter {
            gamma: true,
            ..TextureImporter::default()
        };
        let tex = importer.import(Path::new("Cube.exr"), &exr, &meta).unwrap();
        let first = f32::from_le_bytes(tex.mips[0].data[..4].try_into().unwrap());
        assert!((first - 0.537).abs() < 0.01, "{size}: {first}");
    }
}

/// The baked reflection of tests/projects/baked (a 384x64 EXR, glossy convolution) imports as a
/// 64 cubemap of floats, every mip finite. BakedSetup sets the probe resolution to 64.
#[test]
fn a_baked_reflection_imports_as_a_convolved_cubemap() {
    let file = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../tests/projects/baked/Assets/Scenes/Baked/ReflectionProbe-0.exr");
    let bytes = std::fs::read(&file).unwrap();
    let meta = unbound_yaml::parse_value(
        &std::fs::read_to_string(file.with_extension("exr.meta")).unwrap(),
    )
    .unwrap();
    let started = std::time::Instant::now();
    let tex = TextureImporter::default()
        .import(&file, &bytes, &meta)
        .unwrap();
    eprintln!("imported in {:?}", started.elapsed());
    assert_eq!(
        (tex.width, tex.layers, tex.format),
        (64, 6, TextureFormat::Rgba32F)
    );
    assert_eq!(tex.mips.len(), 6 * 7);
    for mip in &tex.mips {
        assert!(
            mip.data
                .as_chunks::<4>()
                .0
                .iter()
                .all(|c| f32::from_le_bytes(*c).is_finite())
        );
    }
}

#[test]
fn compressed_mips_decode_close_to_the_pixels() {
    // A gradient with alpha, 8x8: mips 8, 4, 2 (padded), 1 (padded).
    let bytes = png(8, 8, |x, y| {
        [(x * 8) as u8, (y * 8) as u8, 128, (255 - x * 4) as u8]
    });
    let meta = unbound_yaml::parse_value("TextureImporter:\n  nPOTScale: 0\n").unwrap();
    let importer = TextureImporter {
        compress: true,
        ..TextureImporter::default()
    };
    let tex = importer
        .import(Path::new("Assets/g.png"), &bytes, &meta)
        .unwrap();
    let bc7 = tex.bc7.as_ref().expect("an 8x8 RGBA8 texture compresses");
    assert_eq!(bc7.len(), tex.mips.len());
    assert_eq!(bc7[0].len(), 4 * 16);
    assert!(bc7[2..].iter().all(|b| b.len() == 16));
    let mut block = [0u32; 16];
    texture2ddecoder::decode_bc7_block(&bc7[0][..16], &mut block);
    for (i, texel) in block.iter().enumerate() {
        let (x, y) = (i % 4, i / 4);
        let p = &tex.mips[0].data[(y * 8 + x) * 4..][..4];
        // The decoder gives BGRA in a u32.
        let [b, g, r, a] = texel.to_le_bytes();
        for (got, want) in [r, g, b, a].iter().zip(p) {
            assert!(
                (*got as i32 - *want as i32).abs() <= 8,
                "texel {i}: {:?} vs {:?}",
                [r, g, b, a],
                p
            );
        }
    }
    // Not divisible by 4: left uncompressed.
    let odd = png(6, 6, |_, _| [1, 2, 3, 255]);
    assert!(
        importer
            .import(Path::new("Assets/o.png"), &odd, &meta)
            .unwrap()
            .bc7
            .is_none()
    );
}

#[test]
fn hdr_colour_is_gamma_encoded_in_a_gamma_project() {
    // Measured on the 6000.0 Editor (gamma, Android): an EXR's linear 0.18 imports as about 0.46,
    // 1.19 as 1.08; linear projects keep the values.
    let mut exr = Vec::new();
    let image = image::Rgba32FImage::from_fn(4, 4, |x, _| {
        image::Rgba(if x < 2 {
            [0.18, 0.18, 0.18, 1.0]
        } else {
            [1.19, 1.19, 1.19, 0.5]
        })
    });
    image::DynamicImage::ImageRgba32F(image)
        .write_to(
            &mut std::io::Cursor::new(&mut exr),
            image::ImageFormat::OpenExr,
        )
        .unwrap();
    let meta = unbound_yaml::parse_value(
        "TextureImporter:\n  mipmaps:\n    sRGBTexture: 1\n  nPOTScale: 0\n",
    )
    .unwrap();
    let texel = |gamma: bool, x: usize| {
        let importer = TextureImporter {
            gamma,
            ..TextureImporter::default()
        };
        let tex = importer
            .import(Path::new("Assets/sky.exr"), &exr, &meta)
            .unwrap();
        let d = &tex.mips[0].data[x * 16..x * 16 + 16];
        [0, 12].map(|at| f32::from_le_bytes(d[at..at + 4].try_into().unwrap()))
    };
    let [dark, _] = texel(true, 0);
    let [bright, alpha] = texel(true, 3);
    assert!((dark - 0.4613).abs() < 2e-3, "{dark}");
    assert!((bright - 1.0823).abs() < 2e-3, "{bright}");
    assert_eq!(alpha, 0.5);
    assert!((texel(false, 0)[0] - 0.18).abs() < 1e-6);
}

#[test]
fn a_target_that_keeps_the_pixels_is_not_block_compressed() {
    let meta = |android: &str| {
        format!(
            "TextureImporter:\n  platformSettings:\n  - buildTarget: DefaultTexturePlatform\n    textureFormat: -1\n    textureCompression: 1\n    overridden: 0\n  - buildTarget: Android\n{android}"
        )
    };
    // RGBA32 for Android (a UI mask whose transparent texels must stay at alpha 0).
    let rgba32 = meta("    textureFormat: 4\n    overridden: 1\n");
    assert!(settings(&rgba32, "Android").uncompressed);
    assert!(!settings(&rgba32, "Standalone").uncompressed);
    assert!(
        !settings(
            &meta("    textureFormat: 47\n    overridden: 1\n"),
            "Android"
        )
        .uncompressed
    );
    assert!(
        settings(
            &meta("    textureFormat: -1\n    textureCompression: 0\n    overridden: 1\n"),
            "Android"
        )
        .uncompressed
    );
    // Not overridden: the default platform's (compressed).
    assert!(
        !settings(
            &meta("    textureFormat: 4\n    overridden: 0\n"),
            "Android"
        )
        .uncompressed
    );

    let png = png(8, 8, |x, y| if x + y == 0 { [255; 4] } else { [0; 4] });
    let importer = TextureImporter {
        build_target: "Android".into(),
        compress: true,
        gamma: false,
        normal_maps_xyz: true,
    };
    let rgba32 = unbound_yaml::parse_value(&rgba32).unwrap();
    let texture = importer
        .import(Path::new("Mask.png"), &png, &rgba32)
        .unwrap();
    assert!(texture.bc7.is_none());
}

#[test]
fn single_cell_arrays_and_volumes_are_not_block_compressed() {
    // A 1x1 flipbook is one slice, which passed the single-layer check; the renderer binds
    // arrays and volumes through RGBA8 views, which a BC7 texture cannot provide.
    let png = png(8, 8, |x, y| [x as u8 * 30, y as u8 * 30, 7, 255]);
    let importer = TextureImporter {
        build_target: "Standalone".into(),
        compress: true,
        gamma: false,
        normal_maps_xyz: false,
    };
    for shape in [4, 8] {
        let meta = unbound_yaml::parse_value(&format!(
            "TextureImporter:\n  textureShape: {shape}\n  flipbookColumns: 1\n  flipbookRows: 1\n  sRGBTexture: 1\n  platformSettings:\n  - buildTarget: DefaultTexturePlatform\n    textureFormat: -1\n    textureCompression: 1\n"
        ))
        .unwrap();
        let texture = importer.import(Path::new("Cell.png"), &png, &meta).unwrap();
        assert_eq!(texture.layers, 1);
        assert!(texture.bc7.is_none(), "shape {shape}");
    }
    // The same image as a 2D texture is still compressed.
    let meta = unbound_yaml::parse_value(
        "TextureImporter:\n  textureShape: 1\n  sRGBTexture: 1\n  platformSettings:\n  - buildTarget: DefaultTexturePlatform\n    textureFormat: -1\n    textureCompression: 1\n",
    )
    .unwrap();
    let texture = importer.import(Path::new("Cell.png"), &png, &meta).unwrap();
    assert!(texture.bc7.is_some());
}

fn import_with(meta: &str, bytes: &[u8], xyz: bool) -> ImportedTexture {
    TextureImporter {
        normal_maps_xyz: xyz,
        ..TextureImporter::default()
    }
    .import(
        Path::new("t.png"),
        bytes,
        &unbound_yaml::parse_value(meta).unwrap(),
    )
    .unwrap()
}

fn texel(mip: &MipLevel, x: u32, y: u32) -> [u8; 4] {
    let at = ((y * mip.width + x) * 4) as usize;
    mip.data[at..at + 4].try_into().unwrap()
}

// The expected values below were recorded from the 6000.0 Editor (tests/projects/textureimport).

#[test]
fn kaiser_mips_keep_the_negative_lobes() {
    // Mid-gray with one white texel at (12, 12) from the top: row 51 bottom-up.
    let bytes = png(64, 64, |x, y| {
        if (x, y) == (12, 12) {
            [255; 4]
        } else {
            [128, 128, 128, 255]
        }
    });
    let tex = import_with(
        "TextureImporter:\n  mipmaps:\n    mipMapMode: 1\n    sRGBTexture: 0\n",
        &bytes,
        false,
    );
    let row: Vec<u8> = (4..8).map(|x| texel(&tex.mips[1], x, 25)[0]).collect();
    assert_eq!(row, [127, 135, 152, 126]);
}

#[test]
fn box_mips_of_odd_sizes_sample_bilinearly() {
    // 3x1 -> 1x1 samples the middle texel's centre: the middle texel alone.
    let bytes = png(3, 1, |x, _| [[0, 200, 40][x as usize], 0, 0, 255]);
    let tex = import_with(
        "TextureImporter:\n  mipmaps:\n    sRGBTexture: 0\n  nPOTScale: 0\n",
        &bytes,
        false,
    );
    assert_eq!(texel(&tex.mips[1], 0, 0), [200, 0, 0, 255]);
}

#[test]
fn fade_out_blends_encoded_colour_to_gray() {
    let bytes = png(4, 4, |_, _| [200, 100, 0, 200]);
    let meta = "TextureImporter:\n  mipmaps:\n    fadeOut: 1\n    mipMapFadeDistanceStart: 0\n    mipMapFadeDistanceEnd: 2\n";
    let tex = import_with(meta, &bytes, false);
    // Halfway at level 1, gray at level 2; alpha untouched.
    assert_eq!(texel(&tex.mips[0], 0, 0), [200, 100, 0, 200]);
    assert_eq!(texel(&tex.mips[1], 0, 0), [164, 114, 64, 200]);
    assert_eq!(texel(&tex.mips[2], 0, 0), [128, 128, 128, 200]);
}

#[test]
fn border_mips_filter_the_edge_only() {
    // A bright interior with a dark frame: the frame stays dark down the chain.
    let bytes = png(8, 8, |x, y| {
        if x == 0 || y == 0 || x == 7 || y == 7 {
            [0, 0, 0, 255]
        } else {
            [255; 4]
        }
    });
    let tex = import_with(
        "TextureImporter:\n  mipmaps:\n    borderMipMap: 1\n    sRGBTexture: 0\n",
        &bytes,
        false,
    );
    assert_eq!(texel(&tex.mips[1], 0, 1), [0, 0, 0, 255]);
    assert_eq!(texel(&tex.mips[1], 1, 1), [255; 4]);
    assert_eq!(texel(&tex.mips[3], 0, 0), [0, 0, 0, 255]);
}

#[test]
fn normal_maps_are_dxt5nm_or_xyz_by_target() {
    let bytes = png(2, 2, |_, _| [128, 234, 198, 255]);
    let meta = "TextureImporter:\n  textureType: 1\n  nPOTScale: 0\n";
    assert_eq!(
        texel(&import_with(meta, &bytes, false).mips[0], 0, 0),
        [255, 234, 234, 128]
    );
    assert_eq!(
        texel(&import_with(meta, &bytes, true).mips[0], 0, 0),
        [128, 234, 198, 255]
    );
}

#[test]
fn normal_maps_from_height() {
    // A white texel on black at (16, 16) from the top: row 15 bottom-up; heightScale 0.25.
    let bytes = png(32, 32, |x, y| {
        if (x, y) == (16, 16) {
            [255; 4]
        } else {
            [0, 0, 0, 255]
        }
    });
    let meta = "TextureImporter:\n  textureType: 1\n  bumpmap:\n    convertToNormalMap: 1\n    heightScale: 0.25\n";
    let tex = import_with(meta, &bytes, false);
    assert_eq!(texel(&tex.mips[0], 15, 15), [255, 128, 128, 14]);
    assert_eq!(texel(&tex.mips[0], 17, 15), [255, 128, 128, 241]);
    assert_eq!(texel(&tex.mips[0], 16, 16), [255, 241, 241, 128]);
    let sobel = import_with(&format!("{meta}    normalMapFilter: 1\n"), &bytes, false);
    assert_eq!(texel(&sobel.mips[0], 15, 15), [255, 128, 128, 38]);
    assert_eq!(texel(&sobel.mips[0], 15, 16), [255, 179, 179, 76]);
}

#[test]
fn alpha_source_none_keeps_alpha_in_an_explicit_alpha_format() {
    let bytes = png(1, 1, |_, _| [10, 20, 30, 40]);
    let meta = |format: i32| {
        format!(
            "TextureImporter:\n  alphaUsage: 0\n  platformSettings:\n  - buildTarget: Standalone\n    textureFormat: {format}\n    overridden: 1\n"
        )
    };
    assert_eq!(
        import_with(&meta(4), &bytes, false).mips[0].data,
        [10, 20, 30, 40]
    );
    assert_eq!(
        import_with(&meta(3), &bytes, false).mips[0].data,
        [10, 20, 30, 255]
    );
    assert_eq!(
        import_with(&meta(-1), &bytes, false).mips[0].data,
        [10, 20, 30, 255]
    );
}

#[test]
fn alpha_from_gray_is_the_mean_rounded_down() {
    let bytes = png(1, 1, |_, _| [113, 35, 178, 255]);
    let tex = import_with("TextureImporter:\n  alphaUsage: 2\n", &bytes, false);
    assert_eq!(tex.mips[0].data, [113, 35, 178, 108]);
}

#[test]
fn hdr_max_size_resize_matches_the_editor() {
    // tests/projects/textureimport's float case, whose probe value Unbound's runtime reports as
    // RGBAHalf: the imported floats must equal the Editor's (modified Mitchell, gamma after the
    // resize in a gamma project), bit for bit.
    let probe = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects/textureimport");
    let source = probe.join("Assets/Textures/MaxSizeSoftHdr.exr");
    let bytes = std::fs::read(&source).unwrap();
    let meta = unbound_yaml::parse_value(
        &std::fs::read_to_string(source.with_extension("exr.meta")).unwrap(),
    )
    .unwrap();
    fn base64(text: &str) -> Vec<u8> {
        let value = |c: u8| match c {
            b'A'..=b'Z' => c - b'A',
            b'a'..=b'z' => c - b'a' + 26,
            b'0'..=b'9' => c - b'0' + 52,
            b'+' => 62,
            _ => 63,
        };
        let digits: Vec<u8> = text.bytes().filter(|&c| c != b'=').map(value).collect();
        digits
            .chunks(4)
            .flat_map(|q| {
                let n = q
                    .iter()
                    .enumerate()
                    .fold(0u32, |n, (i, &d)| n | (d as u32) << (18 - 6 * i));
                n.to_be_bytes()[1..q.len()].to_vec()
            })
            .collect()
    }
    for (profile, gamma, target) in [
        ("Linear", false, "Standalone"),
        ("Gamma", true, "Standalone"),
        ("Android", true, "Android"),
    ] {
        let reference = std::fs::read_to_string(
            probe.join(format!("Reference/unity-6000.0.71f1/{profile}.jsonl")),
        )
        .unwrap();
        let level = reference
            .lines()
            .map(|l| serde_json::from_str::<serde_json::Value>(l).unwrap())
            .find(|v| v["k"] == "MaxSizeSoftHdr.mip0")
            .unwrap();
        let (size, data) = level["v"].as_str().unwrap().split_once(':').unwrap();
        assert_eq!(size, "128x128");
        let tex = TextureImporter {
            gamma,
            build_target: target.into(),
            ..TextureImporter::default()
        }
        .import(Path::new("MaxSizeSoftHdr.exr"), &bytes, &meta)
        .unwrap();
        assert_eq!(
            (tex.width, tex.height, tex.format),
            (128, 128, TextureFormat::Rgba32F)
        );
        let expected = base64(data);
        let differing = expected
            .chunks(4)
            .zip(tex.mips[0].data.chunks(4))
            .filter(|(a, b)| a != b)
            .count();
        assert_eq!(
            (tex.mips[0].data.len(), differing),
            (expected.len(), 0),
            "{profile}"
        );
    }
}

/// Standard base64 (with padding) to bytes.
fn decode_base64(text: &str) -> Vec<u8> {
    let value = |c: u8| match c {
        b'A'..=b'Z' => c - b'A',
        b'a'..=b'z' => c - b'a' + 26,
        b'0'..=b'9' => c - b'0' + 52,
        b'+' => 62,
        _ => 63,
    };
    let digits: Vec<u8> = text.bytes().filter(|&c| c != b'=').map(value).collect();
    digits
        .chunks(4)
        .flat_map(|q| {
            let n = q
                .iter()
                .enumerate()
                .fold(0u32, |n, (i, &d)| n | (d as u32) << (18 - 6 * i));
            n.to_be_bytes()[1..q.len()].to_vec()
        })
        .collect()
}

#[test]
fn flipbook_arrays_and_volumes_match_the_editor() {
    // tests/projects/texturearrays: every readable imported Texture2DArray and Texture3D case,
    // every slice and mip level byte for byte (GetPixels32 of the Editor's capture).
    let probe = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects/texturearrays");
    for (profile, target) in [("Gamma", "Standalone"), ("Android", "Android")] {
        let reference: std::collections::HashMap<String, String> = std::fs::read_to_string(
            probe.join(format!("Reference/unity-6000.0.71f1/{profile}.jsonl")),
        )
        .unwrap()
        .lines()
        .map(|l| serde_json::from_str::<serde_json::Value>(l).unwrap())
        .map(|v| {
            (
                v["k"].as_str().unwrap().into(),
                v["v"].as_str().unwrap().into(),
            )
        })
        .collect();
        let importer = TextureImporter {
            gamma: true,
            build_target: target.into(),
            normal_maps_xyz: target == "Android",
            ..TextureImporter::default()
        };
        let mut checked = 0;
        for entry in std::fs::read_dir(probe.join("Assets/Textures")).unwrap() {
            let path = entry.unwrap().path();
            if path.extension().is_none_or(|e| e != "png") {
                continue;
            }
            let name = path.file_stem().unwrap().to_str().unwrap().to_string();
            let info = &reference[&format!("{name}.info")];
            let volume = info.starts_with("Texture3D ");
            if !(volume || info.starts_with("Texture2DArray ")) || info.contains("readable False") {
                continue;
            }
            let meta = unbound_yaml::parse_value(
                &std::fs::read_to_string(path.with_extension("png.meta")).unwrap(),
            )
            .unwrap();
            let tex = importer
                .import(&path, &std::fs::read(&path).unwrap(), &meta)
                .unwrap();
            let size = format!(
                "{}x{}x{} mips {}",
                tex.width,
                tex.height,
                tex.layers,
                tex.mip_count()
            );
            assert!(info.contains(&size), "{name}: {size} vs {info}");
            for layer in 0..if volume { 1 } else { tex.layers as usize } {
                for mip in 0..tex.mip_count() {
                    let key = if volume {
                        format!("{name}.mip{mip}.px32")
                    } else {
                        format!("{name}.e{layer}.mip{mip}.px32")
                    };
                    let data = decode_base64(reference[&key].split_once(':').unwrap().1);
                    assert!(
                        tex.level(layer, mip).unwrap().data == data,
                        "{profile} {key}"
                    );
                    checked += 1;
                }
            }
        }
        assert!(checked > 200, "{checked} levels");
    }
}

#[test]
fn a_cookie_keeps_its_alpha_in_every_channel() {
    // An opaque image imported as a Cookie with the alpha taken from the input: white.
    let bytes = png(2, 1, |x, _| {
        if x == 0 {
            [200, 40, 10, 255]
        } else {
            [0, 0, 0, 255]
        }
    });
    let tex = import_with(
        "TextureImporter:\n  textureType: 4\n  alphaUsage: 1\n",
        &bytes,
        false,
    );
    assert_eq!(tex.mips[0].data, [255; 8]);
    assert!(!tex.srgb);
    // From the gray scale: the mean, in every channel.
    let tex = import_with(
        "TextureImporter:\n  textureType: 4\n  alphaUsage: 2\n",
        &bytes,
        false,
    );
    assert_eq!(tex.mips[0].data, [83, 83, 83, 83, 0, 0, 0, 0]);
    // Control: a Default texture keeps its colour.
    let tex = import_with(
        "TextureImporter:\n  textureType: 0\n  alphaUsage: 2\n",
        &bytes,
        false,
    );
    assert_eq!(tex.mips[0].data, [200, 40, 10, 83, 0, 0, 0, 0]);
}

/// The format the Editor reports for automatic uncompressed formats (tests/projects/urp
/// texture-formats.jsonl): Cookie and Single Channel textures keep their channel, others RGB24
/// or RGBA32 by their alpha; compression, explicit formats and normal maps keep Unbound's
/// RGBA32 answer.
#[test]
fn automatic_uncompressed_formats_are_reported() {
    let opaque = png(4, 4, |x, _| [x as u8 * 60, 10, 20, 255]);
    let clear = png(4, 4, |x, _| {
        [x as u8 * 60, 10, 20, if x == 0 { 0 } else { 255 }]
    });
    let report = |bytes: &[u8], meta: &str| {
        let meta = unbound_yaml::parse_value(&format!("TextureImporter:\n  nPOTScale: 0\n{meta}"))
            .unwrap();
        TextureImporter::default()
            .import(Path::new("Assets/a.png"), bytes, &meta)
            .unwrap()
            .reported_format()
    };
    let uncompressed = "  textureCompression: 0\n";
    assert_eq!(report(&opaque, uncompressed), Some(unity_format::RGB24));
    assert_eq!(report(&clear, uncompressed), Some(unity_format::RGBA32));
    assert_eq!(
        report(&clear, &format!("{uncompressed}  alphaUsage: 0\n")),
        Some(unity_format::RGB24)
    );
    assert_eq!(
        report(&opaque, &format!("{uncompressed}  textureType: 4\n")),
        Some(unity_format::ALPHA8)
    );
    assert_eq!(
        report(
            &opaque,
            &format!("{uncompressed}  textureType: 10\n  singleChannelComponent: 0\n")
        ),
        Some(unity_format::ALPHA8)
    );
    assert_eq!(
        report(
            &opaque,
            &format!("{uncompressed}  textureType: 10\n  singleChannelComponent: 1\n")
        ),
        Some(unity_format::R8)
    );
    assert_eq!(
        report(&opaque, &format!("{uncompressed}  textureType: 1\n")),
        None
    );
    // Compressed (the default) and explicit formats are not reported, but for a Single Channel
    // Alpha texture, which stays Alpha8.
    assert_eq!(report(&opaque, ""), None);
    assert_eq!(
        report(&opaque, "  textureType: 10\n  singleChannelComponent: 0\n"),
        Some(unity_format::ALPHA8)
    );
    assert_eq!(
        report(&opaque, "  textureType: 10\n  singleChannelComponent: 1\n"),
        None
    );
    assert_eq!(report(&opaque, "  textureType: 4\n"), None);
    assert_eq!(report(&opaque, "  textureFormat: 4\n"), None);
}

/// The channel the GPU samples of a one-channel import (tests/projects/urp
/// texture-samples.jsonl): a Single Channel texture keeps its channel compressed or not (Alpha8,
/// or R8 / BC4), stays linear when marked sRGB and is not BC7-compressed; an uncompressed Cookie
/// keeps its alpha. Controls: a compressed Cookie (unmeasured) and a Default texture keep their
/// RGBA and BC7.
#[test]
fn one_channel_imports_keep_their_channel_on_the_gpu() {
    let bytes = png(8, 8, |x, y| [x as u8 * 30, y as u8 * 30, 7, 200]);
    let import = |meta: &str| {
        let meta = unbound_yaml::parse_value(&format!(
            "TextureImporter:\n  nPOTScale: 0\n  sRGBTexture: 1\n{meta}"
        ))
        .unwrap();
        TextureImporter {
            compress: true,
            ..TextureImporter::default()
        }
        .import(Path::new("Assets/a.png"), &bytes, &meta)
        .unwrap()
    };
    let uncompressed = "  textureCompression: 0\n";
    for compression in ["", uncompressed] {
        let alpha = import(&format!(
            "{compression}  textureType: 10\n  singleChannelComponent: 0\n"
        ));
        assert_eq!(
            alpha.gpu_channel(),
            Some(GpuChannel::Alpha),
            "{compression:?}"
        );
        assert!(alpha.bc7.is_none());
        let red = import(&format!(
            "{compression}  textureType: 10\n  singleChannelComponent: 1\n"
        ));
        assert_eq!(red.gpu_channel(), Some(GpuChannel::Red), "{compression:?}");
        assert!(red.bc7.is_none());
        assert!(
            !red.srgb,
            "a Single Channel texture marked sRGB stays linear"
        );
    }
    let cookie = import(&format!("{uncompressed}  textureType: 4\n"));
    assert_eq!(cookie.gpu_channel(), Some(GpuChannel::Alpha));
    let compressed_cookie = import("  textureType: 4\n");
    assert_eq!(compressed_cookie.gpu_channel(), None);
    assert!(compressed_cookie.bc7.is_some());
    let default = import("  textureType: 0\n");
    assert_eq!(default.gpu_channel(), None);
    assert!(default.bc7.is_some());
    assert!(default.srgb);
    // An explicit format keeps the texture's RGBA and its sRGB setting (unmeasured).
    let explicit = import("  textureType: 10\n  singleChannelComponent: 1\n  textureFormat: 4\n");
    assert_eq!(explicit.gpu_channel(), None);
    assert!(explicit.srgb);
}

/// A Shadowmask or Lightmap keeps its input alpha (an unused channel or texel 0) under "Alpha
/// Source: None"; other textures are made opaque.
#[test]
fn baked_lightmaps_keep_their_alpha() {
    let mask = png(4, 4, |x, _| [255 * (x % 2) as u8, 0, 0, 0]);
    let alpha = |texture_type: i32| {
        let meta = unbound_yaml::parse_value(&format!(
            "TextureImporter:\n  nPOTScale: 0\n  alphaUsage: 0\n  textureType: {texture_type}\n"
        ))
        .unwrap();
        TextureImporter::default()
            .import(Path::new("Assets/a.png"), &mask, &meta)
            .unwrap()
            .mips[0]
            .data[3]
    };
    assert_eq!(alpha(11), 0);
    assert_eq!(alpha(6), 0);
    assert_eq!(alpha(0), 255);
}
