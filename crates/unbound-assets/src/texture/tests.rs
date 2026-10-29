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
    assert_eq!(
        dilated.mips[0].data,
        [0, 255, 0, 255, 0, 255, 0, 0, 0, 255, 0, 0]
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

/// The baked reflection of tests/projects/baked (a 768x128 EXR, glossy convolution) imports as a
/// 128 cubemap of floats, every mip finite.
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
        (128, 6, TextureFormat::Rgba32F)
    );
    assert_eq!(tex.mips.len(), 6 * 8);
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
    };
    let rgba32 = unbound_yaml::parse_value(&rgba32).unwrap();
    let texture = importer
        .import(Path::new("Mask.png"), &png, &rgba32)
        .unwrap();
    assert!(texture.bc7.is_none());
}
