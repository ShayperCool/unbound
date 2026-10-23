//! Sprites defined by a texture's `TextureImporter` settings.
//!
//! A texture of type Sprite creates Sprite sub-assets next to its Texture2D (fileID 2800000):
//!
//! * **Single** / **Polygon** mode: one sprite covering the texture, fileID 21300000, named after the
//!   texture; pivot, border and outline from the importer's top-level fields and `spriteSheet`.
//! * **Multiple** mode: one sprite per `spriteSheet.sprites` entry. Its fileID is the entry's
//!   `internalID` (2019.3+), else the ID `spriteSheet.nameFileIdTable` gives its name (2022+), else
//!   the ID of its name in `internalIDToNameTable`/`fileIDToRecycleName` (older metas:
//!   21300000, 21300002, ...).
//!
//! Rects are in pixels of the *source* image with Unity's bottom-left origin, as authored.
//! [`SpriteDef::texture_rect`] is the same rect in the imported (possibly downscaled) texture.
//!
//! The sprite mesh (FullRect quad or Tight outline) is built by the runtime; the custom outlines
//! authored in the Sprite Editor are passed through. TODO: generate Tight meshes from alpha when no
//! outline is authored (Unity traces the alpha contour and simplifies it per
//! `spriteTessellationDetail`); a FullRect quad is used until then.
use crate::meta::{
    self, FileIdAllocator, FileIdSource, FileIdTable, boolean, class, float, int, polygons,
};
use serde::{Deserialize, Serialize};
use unbound_yaml::Value;

/// The fileID of the sprite of a Single or Polygon mode texture.
pub const SINGLE_SPRITE_FILE_ID: i64 = 21_300_000;

/// `spriteMode`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum SpriteMode {
    /// No sprites: `spriteMode: 0`, or a texture type other than Sprite.
    None,
    Single,
    Multiple,
    Polygon,
}

/// `spriteMeshType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
pub enum SpriteMeshType {
    FullRect,
    Tight,
}

/// A rect in pixels, origin bottom-left.
#[derive(Debug, Clone, Copy, PartialEq, Default, Serialize, Deserialize)]
pub struct Rect {
    pub x: f32,
    pub y: f32,
    pub width: f32,
    pub height: f32,
}

/// The texture-wide sprite settings.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SpriteSettings {
    pub mode: SpriteMode,
    /// `spritePixelsToUnits`.
    pub pixels_per_unit: f32,
    /// `alignment` of a single sprite (see [`alignment_pivot`]).
    pub alignment: i32,
    /// `spritePivot`, used when `alignment` is 9 (custom).
    pub pivot: [f32; 2],
    /// `spriteBorder`: left, bottom, right, top in pixels.
    pub border: [f32; 4],
    pub mesh_type: SpriteMeshType,
    pub extrude: u32,
    /// `spriteGenerateFallbackPhysicsShape`.
    pub generate_physics_shape: bool,
}

impl SpriteSettings {
    /// Reads the settings of a `TextureImporter` section; `is_sprite` is whether the texture type
    /// is Sprite (other types keep a `spriteMode` that Unity ignores).
    pub fn read(imp: &Value, is_sprite: bool) -> Self {
        let mode = match (is_sprite, int(imp, "spriteMode").unwrap_or(1)) {
            (false, _) | (true, 0) => SpriteMode::None,
            (true, 2) => SpriteMode::Multiple,
            (true, 3) => SpriteMode::Polygon,
            (true, _) => SpriteMode::Single,
        };
        Self {
            mode,
            pixels_per_unit: float(imp, "spritePixelsToUnits")
                .filter(|&p| p > 0.0)
                .unwrap_or(100.0),
            alignment: int(imp, "alignment").unwrap_or(0) as i32,
            pivot: meta::vec2(imp.get("spritePivot")).unwrap_or([0.5, 0.5]),
            border: meta::vec4(imp.get("spriteBorder")).unwrap_or_default(),
            mesh_type: match int(imp, "spriteMeshType").unwrap_or(1) {
                0 => SpriteMeshType::FullRect,
                _ => SpriteMeshType::Tight,
            },
            extrude: int(imp, "spriteExtrude").unwrap_or(1).clamp(0, 32) as u32,
            generate_physics_shape: boolean(imp, "spriteGenerateFallbackPhysicsShape")
                .unwrap_or(true),
        }
    }
}

/// A Sprite sub-asset.
#[derive(Debug, Clone, PartialEq, Serialize, Deserialize)]
pub struct SpriteDef {
    pub file_id: i64,
    pub file_id_source: FileIdSource,
    pub name: String,
    /// In source-image pixels, origin bottom-left (as in the `.meta`).
    pub rect: Rect,
    /// `rect` in the imported texture's pixels (differs when the max-size clamp downscaled it).
    pub texture_rect: Rect,
    /// `alignment`: 0 center, 1 top-left, 2 top, 3 top-right, 4 left, 5 right, 6 bottom-left,
    /// 7 bottom, 8 bottom-right, 9 custom.
    pub alignment: i32,
    /// Normalized within `rect`, (0, 0) bottom-left.
    pub pivot: [f32; 2],
    /// Left, bottom, right, top, in source pixels.
    pub border: [f32; 4],
    /// Authored pixels per unit (source pixels).
    pub pixels_per_unit: f32,
    /// `Sprite.pixelsPerUnit` as Unity reports it: scaled with the texture so the sprite keeps its
    /// size in world units.
    pub texture_pixels_per_unit: f32,
    pub mesh_type: SpriteMeshType,
    pub extrude: u32,
    pub generate_physics_shape: bool,
    /// Custom outline polygons (Tight mesh), in pixels relative to the rect centre.
    pub outline: Vec<Vec<[f32; 2]>>,
    /// Custom physics shape polygons, in pixels relative to the rect centre.
    pub physics_shape: Vec<Vec<[f32; 2]>>,
    /// `spriteID` (a GUID string), when the meta has one.
    pub sprite_id: String,
}

/// The pivot an `alignment` value stands for; `custom` for 9 (and unknown values).
pub fn alignment_pivot(alignment: i32, custom: [f32; 2]) -> [f32; 2] {
    match alignment {
        0 => [0.5, 0.5],
        1 => [0.0, 1.0],
        2 => [0.5, 1.0],
        3 => [1.0, 1.0],
        4 => [0.0, 0.5],
        5 => [1.0, 0.5],
        6 => [0.0, 0.0],
        7 => [0.5, 0.0],
        8 => [1.0, 0.0],
        _ => custom,
    }
}

fn rect(v: Option<&Value>) -> Option<Rect> {
    let v = v?;
    Some(Rect {
        x: float(v, "x")?,
        y: float(v, "y")?,
        width: float(v, "width")?,
        height: float(v, "height")?,
    })
}

/// The sprites of a `TextureImporter` section. `original` is the source image size, `imported`
/// the size of the imported texture.
pub fn read_sprites(
    imp: &Value,
    settings: &SpriteSettings,
    texture_name: &str,
    original: (u32, u32),
    imported: (u32, u32),
) -> Vec<SpriteDef> {
    let scale = [
        imported.0 as f32 / original.0.max(1) as f32,
        imported.1 as f32 / original.1.max(1) as f32,
    ];
    let sheet = imp.get("spriteSheet");
    let sheet_field = |key: &str| sheet.and_then(|s| s.get(key));
    let make = |file_id,
                file_id_source,
                name: &str,
                rect: Rect,
                alignment,
                pivot,
                border,
                entry: Option<&Value>| {
        let field = |key: &str| entry.or(sheet).and_then(|e| e.get(key));
        SpriteDef {
            file_id,
            file_id_source,
            name: name.to_string(),
            rect,
            texture_rect: Rect {
                x: rect.x * scale[0],
                y: rect.y * scale[1],
                width: rect.width * scale[0],
                height: rect.height * scale[1],
            },
            alignment,
            pivot: alignment_pivot(alignment, pivot),
            border,
            pixels_per_unit: settings.pixels_per_unit,
            texture_pixels_per_unit: settings.pixels_per_unit * scale[0],
            mesh_type: settings.mesh_type,
            extrude: settings.extrude,
            generate_physics_shape: settings.generate_physics_shape,
            outline: polygons(field("outline")),
            physics_shape: polygons(field("physicsShape")),
            sprite_id: field("spriteID")
                .and_then(Value::as_str)
                .unwrap_or_default()
                .to_string(),
        }
    };

    match settings.mode {
        SpriteMode::None => Vec::new(),
        SpriteMode::Single | SpriteMode::Polygon => {
            let full = Rect {
                x: 0.0,
                y: 0.0,
                width: original.0 as f32,
                height: original.1 as f32,
            };
            vec![make(
                SINGLE_SPRITE_FILE_ID,
                FileIdSource::Fixed,
                texture_name,
                full,
                settings.alignment,
                settings.pivot,
                settings.border,
                None,
            )]
        }
        SpriteMode::Multiple => {
            let table = FileIdTable::read(imp);
            let name_table = sheet_field("nameFileIdTable").and_then(Value::as_map);
            let entries = sheet_field("sprites")
                .and_then(Value::as_seq)
                .unwrap_or_default();
            let mut ids = FileIdAllocator::new(&table);
            // IDs carried by the entries themselves are taken first, so a derived ID never
            // collides with one of them.
            let own: Vec<Option<i64>> = entries
                .iter()
                .map(|e| {
                    let name = e.get("name").and_then(Value::as_str).unwrap_or("");
                    int(e, "internalID")
                        .filter(|&id| id != 0)
                        .or_else(|| name_table.and_then(|t| t.get(name)).and_then(Value::as_i64))
                })
                .collect();
            own.iter().flatten().for_each(|&id| ids.reserve(id));
            let mut out = Vec::with_capacity(entries.len());
            for (entry, own) in entries.iter().zip(own) {
                let name = entry.get("name").and_then(Value::as_str).unwrap_or("");
                let (file_id, source) = match own {
                    Some(id) => (id, FileIdSource::Meta),
                    None => ids.id(class::SPRITE, name, None),
                };
                let Some(rect) = rect(entry.get("rect")) else {
                    continue;
                };
                out.push(make(
                    file_id,
                    source,
                    name,
                    rect,
                    int(entry, "alignment").unwrap_or(0) as i32,
                    meta::vec2(entry.get("pivot")).unwrap_or([0.5, 0.5]),
                    meta::vec4(entry.get("border")).unwrap_or_default(),
                    Some(entry),
                ));
            }
            out
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::texture::TextureSettings;

    fn sprites_of(meta: &str, original: (u32, u32), imported: (u32, u32)) -> Vec<SpriteDef> {
        let meta = unbound_yaml::parse_value(meta).unwrap();
        let settings = TextureSettings::read(&meta, "Standalone");
        read_sprites(
            meta::importer(&meta, "TextureImporter"),
            &settings.sprite,
            "Atlas",
            original,
            imported,
        )
    }

    const MULTIPLE_6000: &str = r#"fileFormatVersion: 2
guid: aa68192ef33b323e400536404a0298f3
TextureImporter:
  internalIDToNameTable:
  - first:
      213: -2413806693520163455
    second: Circle
  serializedVersion: 13
  spriteMode: 2
  spriteMeshType: 1
  alignment: 0
  spritePivot: {x: 0.5, y: 0.5}
  spritePixelsToUnits: 256
  spriteBorder: {x: 0, y: 0, z: 0, w: 0}
  textureType: 8
  spriteSheet:
    serializedVersion: 2
    sprites:
    - serializedVersion: 2
      name: Circle
      rect:
        serializedVersion: 2
        x: 0
        y: 0
        width: 256
        height: 256
      alignment: 7
      pivot: {x: 0.5, y: 0.5}
      border: {x: 1, y: 2, z: 3, w: 4}
      outline:
      - - {x: 33, y: 128}
        - {x: -33, y: 128}
        - {x: -95, y: 95}
      physicsShape: []
      spriteID: 18d3544e99f608ed0800000000000000
      internalID: -2413806693520163455
    - serializedVersion: 2
      name: Square
      rect:
        serializedVersion: 2
        x: 256
        y: 0
        width: 128
        height: 64
      alignment: 9
      pivot: {x: 0.25, y: 0.75}
      border: {x: 0, y: 0, z: 0, w: 0}
      spriteID: 28d3544e99f608ed0800000000000000
      internalID: 0
    nameFileIdTable:
      Circle: -2413806693520163455
      Square: 5555
"#;

    #[test]
    fn multiple_mode_uses_internal_ids() {
        let sprites = sprites_of(MULTIPLE_6000, (512, 256), (512, 256));
        assert_eq!(sprites.len(), 2);
        let circle = &sprites[0];
        assert_eq!(circle.file_id, -2413806693520163455);
        assert_eq!(circle.name, "Circle");
        assert_eq!(circle.rect.width, 256.0);
        assert_eq!(circle.pivot, [0.5, 0.0]); // alignment 7: bottom
        assert_eq!(circle.border, [1.0, 2.0, 3.0, 4.0]);
        assert_eq!(circle.pixels_per_unit, 256.0);
        assert_eq!(circle.mesh_type, SpriteMeshType::Tight);
        assert_eq!(
            circle.outline,
            vec![vec![[33.0, 128.0], [-33.0, 128.0], [-95.0, 95.0]]]
        );
        let square = &sprites[1];
        assert_eq!(square.file_id, 5555); // internalID 0: nameFileIdTable
        assert_eq!(square.pivot, [0.25, 0.75]);
        assert_eq!(square.rect.x, 256.0);
    }

    #[test]
    fn multiple_mode_reads_legacy_recycle_names() {
        let meta = r#"fileFormatVersion: 2
TextureImporter:
  fileIDToRecycleName:
    21300000: EmojiOne_0
    21300002: EmojiOne_1
  serializedVersion: 5
  spriteMode: 2
  spritePixelsToUnits: 100
  textureType: 8
  spriteSheet:
    serializedVersion: 2
    sprites:
    - serializedVersion: 2
      name: EmojiOne_1
      rect:
        serializedVersion: 2
        x: 0
        y: 0
        width: 128
        height: 128
      alignment: 0
      pivot: {x: 0.5, y: 0.5}
      border: {x: 0, y: 0, z: 0, w: 0}
    - serializedVersion: 2
      name: EmojiOne_0
      rect:
        serializedVersion: 2
        x: 128
        y: 0
        width: 128
        height: 128
      alignment: 0
      pivot: {x: 0.5, y: 0.5}
      border: {x: 0, y: 0, z: 0, w: 0}
    - serializedVersion: 2
      name: EmojiOne_New
      rect:
        serializedVersion: 2
        x: 256
        y: 0
        width: 128
        height: 128
      alignment: 0
      pivot: {x: 0.5, y: 0.5}
      border: {x: 0, y: 0, z: 0, w: 0}
"#;
        let sprites = sprites_of(meta, (512, 512), (256, 256));
        let ids: Vec<_> = sprites
            .iter()
            .map(|s| (s.name.as_str(), s.file_id))
            .collect();
        assert_eq!(
            ids,
            [
                ("EmojiOne_1", 21300002),
                ("EmojiOne_0", 21300000),
                ("EmojiOne_New", 21300004),
            ]
        );
        assert_eq!(sprites[2].file_id_source, FileIdSource::Derived);
        // Downscaled to half: texture rects and pixels per unit follow.
        assert_eq!(sprites[1].texture_rect.x, 64.0);
        assert_eq!(sprites[1].texture_rect.width, 64.0);
        assert_eq!(sprites[1].texture_pixels_per_unit, 50.0);
    }

    #[test]
    fn single_mode_covers_the_texture() {
        let meta = "TextureImporter:\n  spriteMode: 1\n  alignment: 9\n  spritePivot:\n    x: 0.1\n    y: 0.2\n  spriteBorder:\n    x: 5\n    y: 6\n    z: 7\n    w: 8\n  spritePixelsToUnits: 32\n  spriteMeshType: 0\n  textureType: 8\n";
        let sprites = sprites_of(meta, (300, 200), (300, 200));
        assert_eq!(sprites.len(), 1);
        let s = &sprites[0];
        assert_eq!(s.file_id, SINGLE_SPRITE_FILE_ID);
        assert_eq!(s.name, "Atlas");
        assert_eq!(
            s.rect,
            Rect {
                x: 0.0,
                y: 0.0,
                width: 300.0,
                height: 200.0
            }
        );
        assert_eq!(s.pivot, [0.1, 0.2]);
        assert_eq!(s.border, [5.0, 6.0, 7.0, 8.0]);
        assert_eq!(s.pixels_per_unit, 32.0);
        assert_eq!(s.mesh_type, SpriteMeshType::FullRect);
    }

    #[test]
    fn non_sprite_types_have_no_sprites() {
        let meta = "TextureImporter:\n  spriteMode: 1\n  textureType: 0\n";
        assert!(sprites_of(meta, (64, 64), (64, 64)).is_empty());
    }
}
