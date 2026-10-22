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
fn settings_hash_tracks_settings() {
    let a = settings(META_6000, "Standalone");
    let b = settings(META_6000, "Android");
    assert_eq!(a.hash(), settings(META_6000, "Standalone").hash());
    assert_ne!(a.hash(), b.hash());
}
