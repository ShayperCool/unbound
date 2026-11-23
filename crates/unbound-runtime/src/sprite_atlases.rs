//! Sprite atlases packed for Play mode, as the Unity 6000.0.71f1 Editor packs them before it enters
//! Play mode when the project's Sprite Packer mode says so (tests/projects/spriteatlas): each
//! atlas of the mode's kind becomes page textures, and its sprites draw and report from them.
//! A sprite larger than its atlas's page cancels the packing of every atlas, as in the Editor.
use anyhow::{Context, Result};
use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use unbound_assets::{
    AssetCache, AssetKind, Importer, SpriteDef,
    sprite_atlas::{self, PackSettings, PackSprite, definition::AtlasDefinition},
    texture::{ImportedTexture, TextureImporter},
};
use unbound_project::AssetDb;
use unbound_yaml::{Guid, Value};

/// The file id of a texture's Texture2D, which stands for all its sprites as a packable.
const TEXTURE_FILE_ID: i64 = 2800000;

/// Extensions of files whose sprites scripted importers make (PSD Importer, Vector Graphics,
/// Aseprite Importer): such packables are not packed.
const SCRIPTED_SPRITE_EXTENSIONS: &[&str] = &["psb", "svg", "aseprite", "ase"];

/// How the managed side names an atlas page to import: `atlas:<atlas guid>:<page>`.
pub const PAGE_KEY_PREFIX: &str = "atlas:";
/// Between a page and the shader property of one of its secondary pages, in their keys and in
/// the names the Editor gives secondary pages (`<page name>?_NormalMap`).
const SECONDARY_SEPARATOR: char = '?';

/// The atlases packed for Play mode, by asset path.
#[derive(Debug, Default)]
pub struct AtlasSet {
    pub atlases: Vec<(Guid, PackedAtlas)>,
    /// The Editor's error when a sprite larger than its atlas's page cancelled all packing.
    pub cancelled: Option<String>,
}

impl AtlasSet {
    /// Merges newly packed atlases into this set, replacing existing entries with the same GUID
    /// and preserving all unrelated atlases.
    pub fn merge(&mut self, other: &AtlasSet) {
        if other.cancelled.is_some() {
            return;
        }
        self.cancelled = None;
        for (guid, atlas) in &other.atlases {
            if let Some(existing) = self.atlases.iter_mut().find(|(g, _)| g == guid) {
                existing.1 = atlas.clone();
            } else {
                self.atlases.push((*guid, atlas.clone()));
            }
        }
    }
}

/// Packs the atlases at `paths` (asset paths) in parallel, through the import cache. Atlases that
/// fail to read or import are reported and left out; a sprite larger than its atlas's page
/// cancels the packing of every atlas, with the Editor's error.
pub fn pack_all(
    paths: &[String],
    db: &AssetDb,
    textures: &TextureImporter,
    cache: &AssetCache,
) -> AtlasSet {
    use rayon::prelude::*;
    let importer = AtlasImporter {
        db,
        textures,
        cache,
    };
    let packed: Vec<_> = paths
        .par_iter()
        .filter_map(|path| {
            let asset = db.by_path(path)?;
            match cache.get_or_import(&importer, &asset.file, &meta_path(&asset.file)) {
                Ok(atlas) => Some((asset.guid, (*atlas).clone())),
                Err(e) => {
                    tracing::error!("Unbound: sprite atlas {path} is not packed: {e:#}");
                    None
                }
            }
        })
        .collect();
    let mut set = AtlasSet::default();
    for (_, atlas) in &packed {
        for warning in &atlas.warnings {
            tracing::warn!("Unbound: {warning}");
        }
        if let (Some(needed), None) = (atlas.oversize, &set.cancelled) {
            set.cancelled = Some(format!(
                "Packing Error: Sprite Atlas ({}) contain sprite(s) larger than its max texture \
                 size. It needs at least {needed}.",
                atlas.name
            ));
        }
    }
    if let Some(error) = &set.cancelled {
        tracing::error!("{error}");
        return set;
    }
    set.atlases = packed;
    set
}

/// An atlas page by its key (`<atlas guid>:<page>`).
pub fn page(set: &AtlasSet, key: &str) -> Result<ImportedTexture> {
    let (guid, page) = key.split_once(':').context("bad atlas page key")?;
    let guid = Guid::parse(guid).context("bad atlas guid")?;
    let (page, secondary) = match page.split_once(SECONDARY_SEPARATOR) {
        Some((page, name)) => (page, Some(name)),
        None => (page, None),
    };
    let page: usize = page.parse().context("bad atlas page")?;
    let (_, atlas) = set
        .atlases
        .iter()
        .find(|(g, _)| *g == guid)
        .context("no such packed atlas")?;
    match secondary {
        None => atlas.pages.get(page).cloned().context("no such atlas page"),
        Some(name) => atlas
            .secondary_pages
            .get(page)
            .and_then(|pages| pages.iter().find(|(n, _)| n == name))
            .map(|(_, texture)| texture.clone())
            .context("no such secondary atlas page"),
    }
}

fn guid_text(guid: u128) -> String {
    format!("{guid:032x}")
}

/// What the managed side binds sprites with.
pub fn to_json(set: &AtlasSet) -> serde_json::Value {
    use serde_json::json;
    let atlases: Vec<_> = set
        .atlases
        .iter()
        .map(|(guid, atlas)| {
            json!({
                "guid": guid_text(guid.0),
                "name": atlas.name,
                "tag": atlas.tag,
                "includeInBuild": atlas.include_in_build,
                "complete": atlas.complete,
                "tight": atlas.tight,
                "master": atlas.master.map(guid_text),
                "scale": atlas.scale,
                "pages": atlas.pages.iter().zip(&atlas.page_names).zip(&atlas.uv_sizes).enumerate().map(|(i, ((page, name), uv))| json!({
                    "key": format!("{PAGE_KEY_PREFIX}{}:{i}", guid_text(guid.0)),
                    "name": name,
                    "width": page.width,
                    "height": page.height,
                    "uvWidth": uv[0],
                    "uvHeight": uv[1],
                    "secondary": atlas.secondary_pages.get(i).into_iter().flatten().map(|(property, texture)| json!({
                        "property": property,
                        "key": format!("{PAGE_KEY_PREFIX}{}:{i}{SECONDARY_SEPARATOR}{property}", guid_text(guid.0)),
                        "name": texture.name,
                    })).collect::<Vec<_>>(),
                })).collect::<Vec<_>>(),
                "sprites": atlas.sprites.iter().map(|s| json!({
                    "texture": guid_text(s.texture),
                    "fileId": s.file_id,
                    "name": s.name,
                    "page": s.page,
                    "rect": s.texture_rect,
                    "uvTransform": s.uv_transform,
                    "rotation": s.rotation,
                    "secondary": s.secondary,
                })).collect::<Vec<_>>(),
            })
        })
        .collect();
    json!({ "cancelled": set.cancelled, "atlases": atlases })
}

/// A packed atlas (or why it was not packed).
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PackedAtlas {
    pub name: String,
    pub tag: String,
    pub include_in_build: bool,
    pub tight: bool,
    /// A variant's master atlas: its sprites are the master's, its pages the master's scaled.
    pub master: Option<u128>,
    /// The variant's scale (1 for other atlases): `Sprite.spriteAtlasTextureScale` of its clones.
    pub scale: f32,
    /// The page sizes the sprites' rects and UVs refer to (a variant's are its master's).
    pub uv_sizes: Vec<[u32; 2]>,
    pub pages: Vec<ImportedTexture>,
    /// Page texture names, as the Editor names them (with Unbound's own hash).
    pub page_names: Vec<String>,
    /// Each page's secondary pages: the sprites' secondary textures (`_NormalMap`, `_MaskTex`,
    /// ...) laid out as the page lays out the sprites, by the shader property they bind.
    #[serde(default)]
    pub secondary_pages: Vec<Vec<(String, ImportedTexture)>>,
    /// In `SpriteAtlas.GetSprites` order.
    pub sprites: Vec<PackedSprite>,
    /// A sprite is larger than the page: the size the atlas would need.
    pub oversize: Option<u32>,
    /// What could not be packed as the Editor packs it.
    pub warnings: Vec<String>,
    /// Every sprite of the atlas's packables was packed: no packable, texture or master was left
    /// out (an incomplete pack is not stored into the atlas's file).
    pub complete: bool,
}

/// A sprite's place in its atlas.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PackedSprite {
    pub texture: u128,
    pub file_id: i64,
    /// The sprite's name, as the atlas lists it (`m_PackedSpriteNamesToIndex`).
    pub name: String,
    pub page: u32,
    pub texture_rect: [f32; 4],
    pub uv_transform: [f32; 4],
    pub rotation: u8,
    /// The shader properties of the sprite's secondary textures its page's secondary pages hold.
    #[serde(default)]
    pub secondary: Vec<String>,
}

/// Packs one atlas asset (`.spriteatlas` or `.spriteatlasv2`) through the import cache.
pub struct AtlasImporter<'a> {
    pub db: &'a AssetDb,
    pub textures: &'a TextureImporter,
    pub cache: &'a AssetCache,
}

fn meta_path(file: &Path) -> PathBuf {
    PathBuf::from(format!("{}.meta", file.display()))
}

fn definition(source: &Path, bytes: &[u8], meta: &Value) -> Result<AtlasDefinition> {
    let text = std::str::from_utf8(bytes).context("the sprite atlas is not UTF-8")?;
    let v2 = source
        .extension()
        .is_some_and(|e| e.eq_ignore_ascii_case("spriteatlasv2"));
    if v2 {
        let stem = source
            .file_stem()
            .and_then(|s| s.to_str())
            .unwrap_or_default();
        sprite_atlas::definition::read_v2(text, meta, stem)
    } else {
        sprite_atlas::definition::read_v1(text)
    }
}

impl AtlasImporter<'_> {
    /// Packables Unbound does not pack: neither textures nor folders (scripted importers'
    /// sprites, `.psb`, `.svg`, ...), and such files inside folder packables.
    fn skipped(&self, packables: &[(Guid, i64)]) -> Vec<(String, PathBuf)> {
        let mut out = Vec::new();
        for &(guid, _) in packables {
            let Some(asset) = self.db.by_guid(guid) else {
                continue;
            };
            if asset.is_folder {
                let prefix = format!("{}/", asset.path);
                out.extend(
                    self.db
                        .iter()
                        .filter(|a| !a.is_folder && a.path.starts_with(&prefix))
                        .filter(|a| {
                            a.file
                                .extension()
                                .and_then(|e| e.to_str())
                                .is_some_and(|e| {
                                    SCRIPTED_SPRITE_EXTENSIONS
                                        .contains(&e.to_ascii_lowercase().as_str())
                                })
                        })
                        .map(|a| (a.path.clone(), a.file.clone())),
                );
            } else if AssetKind::from_path(&asset.file) != Some(AssetKind::Texture) {
                out.push((asset.path.clone(), asset.file.clone()));
            }
        }
        out
    }

    /// The textures a packable stands for, with the sprite it names (`None`: all of them).
    fn textures(&self, packables: &[(Guid, i64)]) -> Vec<(Guid, PathBuf, Option<i64>)> {
        let mut out = Vec::new();
        for &(guid, file_id) in packables {
            let Some(asset) = self.db.by_guid(guid) else {
                continue;
            };
            if asset.is_folder {
                let prefix = format!("{}/", asset.path);
                let mut inside: Vec<_> = self
                    .db
                    .iter()
                    .filter(|a| !a.is_folder && a.path.starts_with(&prefix))
                    .filter(|a| AssetKind::from_path(&a.file) == Some(AssetKind::Texture))
                    .collect();
                inside.sort_by(|a, b| a.path.cmp(&b.path));
                out.extend(inside.into_iter().map(|a| (a.guid, a.file.clone(), None)));
            } else if AssetKind::from_path(&asset.file) == Some(AssetKind::Texture) {
                let sprite = (file_id != TEXTURE_FILE_ID).then_some(file_id);
                out.push((asset.guid, asset.file.clone(), sprite));
            }
        }
        out
    }
}

/// A sprite's render mesh in units from its pivot: the importer's Tight mesh, an authored one,
/// else its rect's quad.
fn render_mesh(sprite: &SpriteDef) -> (Vec<[f32; 2]>, Vec<u16>) {
    let ppu = sprite.texture_pixels_per_unit;
    let r = sprite.texture_rect;
    let pivot = [sprite.pivot[0] * r.width, sprite.pivot[1] * r.height];
    if let Some(mesh) = &sprite.mesh
        && !mesh.triangles.is_empty()
    {
        return (mesh.vertices.clone(), mesh.triangles.clone());
    }
    if !sprite.vertices.is_empty() && !sprite.indices.is_empty() {
        let vertices = sprite
            .vertices
            .iter()
            .map(|v| [(v[0] - pivot[0]) / ppu, (v[1] - pivot[1]) / ppu])
            .collect();
        return (vertices, sprite.indices.iter().map(|&i| i as u16).collect());
    }
    let corner = |x: f32, y: f32| [(x - pivot[0]) / ppu, (y - pivot[1]) / ppu];
    (
        vec![
            corner(0.0, r.height),
            corner(r.width, r.height),
            corner(0.0, 0.0),
            corner(r.width, 0.0),
        ],
        vec![0, 1, 2, 2, 1, 3],
    )
}

/// The secondary textures a texture's `.meta` names (its sprite sheet's `secondaryTextures`).
fn secondary_guids(meta: &Path) -> Vec<Guid> {
    let Some(value) = std::fs::read_to_string(meta)
        .ok()
        .and_then(|t| unbound_yaml::parse_value(&t).ok())
    else {
        return Vec::new();
    };
    value
        .get("TextureImporter")
        .and_then(|i| i.get("spriteSheet"))
        .and_then(|s| s.get("secondaryTextures"))
        .and_then(Value::as_seq)
        .into_iter()
        .flatten()
        .filter_map(|t| Guid::parse(t.get("texture")?.get("guid")?.as_str()?))
        .collect()
}

/// The packer's key for a sprite's set of secondary textures (0 without any): sprites of one set
/// pack together. The Editor packs a sprite without secondary textures before those with them
/// (tests/projects/spriteatlassecondary); its order of two different sets is not a function of
/// the sets (swapping sets between sprites reorders them), so this orders them by an FNV-1a
/// hash of their property names and texture GUIDs.
fn secondary_hash(textures: &[unbound_assets::sprite::SecondaryTextureDef]) -> u64 {
    if textures.is_empty() {
        return 0;
    }
    let mut parts: Vec<(&str, &str)> = textures
        .iter()
        .map(|t| (t.name.as_str(), t.guid.as_str()))
        .collect();
    parts.sort_unstable();
    let mut hash: u64 = 0xcbf2_9ce4_8422_2325;
    for (name, guid) in parts {
        for byte in name.bytes().chain([0]).chain(guid.bytes()).chain([0]) {
            hash = (hash ^ u64::from(byte)).wrapping_mul(0x0000_0100_0000_01b3);
        }
    }
    hash.max(1)
}

/// The format part of a page's name: the Editor's for the measured formats, else the Unity
/// TextureFormat number.
fn compression_name(texture: &ImportedTexture) -> String {
    match texture.reported_format() {
        None | Some(4) => "Uncompressed".into(),
        Some(12) => "DXT5|BC3".into(),
        Some(format) => format!("Format{format}"),
    }
}

impl AtlasImporter<'_> {
    /// A variant: the master's sprites on the master's pages scaled by `scale` (the importer's
    /// Mitchell filter (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlas); the size rounds down, unmeasured for scales
    /// that do not divide it), stored with the variant's own texture settings.
    fn variant(
        &self,
        source: &Path,
        mut packed: PackedAtlas,
        def: &AtlasDefinition,
        master: Guid,
        scale: f32,
    ) -> Result<PackedAtlas> {
        let Some(asset) = self.db.by_guid(master).filter(|a| a.file != source) else {
            packed.warnings.push(format!(
                "variant atlas {}: its master atlas is missing (or itself); it is not packed",
                def.name
            ));
            packed.complete = false;
            return Ok(packed);
        };
        let master = self
            .cache
            .get_or_import(self, &asset.file, &meta_path(&asset.file))
            .with_context(|| format!("packing the master of variant atlas {}", def.name))?;
        if master.master.is_some() {
            packed.warnings.push(format!(
                "variant atlas {}: its master is a variant; it is not packed",
                def.name
            ));
            packed.complete = false;
            return Ok(packed);
        }
        packed.oversize = master.oversize;
        packed.complete = master.complete;
        packed.scale = scale;
        packed.sprites = master.sprites.clone();
        packed.uv_sizes = master.uv_sizes.clone();
        let mut settings = self.textures.settings(&def.texture_meta);
        settings.format_alpha = Some(true);
        if master
            .pages
            .iter()
            .any(|p| p.blocks.is_some() || p.crunched.is_some())
        {
            packed.warnings.push(format!(
                "variant atlas {}: its master's pages are compressed; it scales their decoded \
                 texels (unmeasured)",
                def.name
            ));
        }
        for (i, page) in master.pages.iter().enumerate() {
            let (w, h) = (page.width, page.height);
            let size = |v: u32| ((v as f32 * scale) as u32).max(1);
            let (sw, sh) = (size(w), size(h));
            let texels = unbound_assets::texture::resize_pixels(w, h, &page.mips[0].data, sw, sh);
            let mut page_settings = settings.clone();
            page_settings.max_size = page_settings.max_size.max(sw.max(sh));
            let mut texture =
                unbound_assets::texture::import_pixels(&def.name, sw, sh, texels, page_settings);
            self.textures.store(&mut texture)?;
            if i == 0 && master.secondary_pages.iter().any(|p| !p.is_empty()) {
                packed.warnings.push(format!(
                    "variant atlas {}: its master's secondary pages are not scaled (its sprites keep \
                     their own secondary textures)",
                    def.name
                ));
            }
            let name = format!("{}-var-{scale:.2}-#{}", master.page_names[i], i + 1);
            texture.name = name.clone();
            packed.page_names.push(name);
            packed.pages.push(texture);
        }
        Ok(packed)
    }
}

impl Importer for AtlasImporter<'_> {
    type Output = PackedAtlas;
    const KIND: &'static str = "sprite-atlases";
    // 2: warnings for skipped packables and compressed variant masters.
    // 3: compressed pages keep their block format (the size fallback resolves per page).
    // 4: each packed sprite keeps its name.
    // 5: an atlas tells whether its pack is complete.
    // 6: a variant without a master is not packed (its pack is incomplete).
    // 7: secondary textures are packed into secondary pages.
    const VERSION: u32 = 7;

    fn dependencies(&self, source: &Path) -> Result<Vec<PathBuf>> {
        let bytes = std::fs::read(source)?;
        let meta = std::fs::read_to_string(meta_path(source))
            .ok()
            .and_then(|t| unbound_yaml::parse_value(&t).ok())
            .unwrap_or_default();
        let def = definition(source, &bytes, &meta)?;
        let mut files = Vec::new();
        // A variant packs its master's sprites (one level: a variant of a variant is not packed).
        let mut packables = def.packables.clone();
        if let Some(master) = def.variant.and_then(|(m, _)| self.db.by_guid(m)) {
            files.push(meta_path(&master.file));
            files.push(master.file.clone());
            let master_meta = std::fs::read_to_string(meta_path(&master.file))
                .ok()
                .and_then(|t| unbound_yaml::parse_value(&t).ok())
                .unwrap_or_default();
            if let Ok(master_def) =
                definition(&master.file, &std::fs::read(&master.file)?, &master_meta)
            {
                packables.extend(master_def.packables);
            }
        }
        for (_, file, _) in self.textures(&packables) {
            // The secondary textures the texture's sprites name are packed too.
            for guid in secondary_guids(&meta_path(&file)) {
                if let Some(asset) = self.db.by_guid(guid) {
                    files.push(meta_path(&asset.file));
                    files.push(asset.file.clone());
                }
            }
            files.push(meta_path(&file));
            files.push(file);
        }
        // The skipped packables are reported: adding or removing one changes the result.
        files.extend(
            self.skipped(&def.packables)
                .into_iter()
                .map(|(_, file)| file),
        );
        Ok(files)
    }

    fn options_key(&self) -> String {
        format!(
            "textures{}:{}",
            TextureImporter::VERSION,
            self.textures.options_key()
        )
    }

    fn import(&self, source: &Path, bytes: &[u8], meta: &Value) -> Result<PackedAtlas> {
        let def = definition(source, bytes, meta)?;
        let mut packed = PackedAtlas {
            name: def.name.clone(),
            tag: def.tag.clone(),
            include_in_build: def.include_in_build,
            tight: def.tight,
            master: def.variant.map(|(m, _)| m.0),
            scale: 1.0,
            uv_sizes: Vec::new(),
            pages: Vec::new(),
            page_names: Vec::new(),
            secondary_pages: Vec::new(),
            sprites: Vec::new(),
            oversize: None,
            warnings: Vec::new(),
            complete: true,
        };
        if let Some((master, scale)) = def.variant {
            return self.variant(source, packed, &def, master, scale);
        }
        // A variant without a master has nothing to pack, and its pack is not stored into its
        // file (matches the Unity 6000.0.71f1 Editor, tests/projects/spriteatlaseditor
        // v1wholewritecrlf: such a file is not written at the Play-mode entry).
        if def.variant_without_master {
            packed.warnings.push(format!(
                "variant atlas {}: it has no master atlas; it is not packed",
                def.name
            ));
            packed.complete = false;
            return Ok(packed);
        }
        let skipped = self.skipped(&def.packables);
        if !skipped.is_empty() {
            packed.complete = false;
            packed.warnings.push(format!(
                "atlas {}: packables that are not textures are not packed (their sprites keep their \
                 own textures, and the other sprites are laid out without them): {}",
                def.name,
                skipped
                    .iter()
                    .map(|(path, _)| path.as_str())
                    .collect::<Vec<_>>()
                    .join(", ")
            ));
        }
        if def.alpha_splitting {
            packed.warnings.push(format!(
                "atlas {}: alpha splitting is not made (Android ETC1 builds only)",
                def.name
            ));
        }
        // The member textures, imported as the build target imports them.
        let mut textures: Vec<(Guid, std::sync::Arc<ImportedTexture>, Option<i64>)> = Vec::new();
        for (guid, file, sprite) in self.textures(&def.packables) {
            let texture = self
                .cache
                .get_or_import(self.textures, &file, &meta_path(&file))
                .with_context(|| format!("importing {}", file.display()))?;
            textures.push((guid, texture, sprite));
        }
        // The sprites, each once.
        let mut chosen: Vec<(usize, usize)> = Vec::new();
        for (t, (guid, texture, only)) in textures.iter().enumerate() {
            if texture.format != unbound_assets::texture::TextureFormat::Rgba8 {
                if !texture.sprites.is_empty() {
                    packed.complete = false;
                    packed.warnings.push(format!(
                        "atlas {}: the sprites of floating-point texture {} are not packed",
                        def.name, texture.name
                    ));
                }
                continue;
            }
            for (s, sprite) in texture.sprites.iter().enumerate() {
                if only.is_some_and(|id| id != sprite.file_id) {
                    continue;
                }
                let duplicate = chosen.iter().any(|&(ot, os)| {
                    textures[ot].0 == *guid && textures[ot].1.sprites[os].file_id == sprite.file_id
                });
                if !duplicate {
                    chosen.push((t, s));
                }
            }
        }
        // The sprites' secondary textures, imported as the build target imports them (each texture
        // once): packed into secondary pages laid out as the main pages (matches the Unity
        // 6000.0.71f1 Editor, tests/projects/spriteatlassecondary). A texture that is missing, does
        // not import, or differs in size or kind from its sprite's texture is not packed: the
        // sprite keeps its own for that property.
        if def.secondary_settings {
            packed.warnings.push(format!(
                "atlas {}: its per-property secondary texture settings are not applied (the \
                 secondary pages take the atlas's settings and their textures' colour space)",
                def.name
            ));
        }
        let mut imported: std::collections::HashMap<Guid, Option<std::sync::Arc<ImportedTexture>>> =
            std::collections::HashMap::new();
        let mut secondary: Vec<Vec<(String, std::sync::Arc<ImportedTexture>)>> = Vec::new();
        for &(t, s) in &chosen {
            let texture = &textures[t].1;
            let mut own = Vec::new();
            for sec in &texture.sprites[s].secondary_textures {
                let guid = Guid::parse(&sec.guid);
                let known = guid.is_some_and(|g| imported.contains_key(&g));
                let found = match guid {
                    Some(guid) => imported
                        .entry(guid)
                        .or_insert_with(|| {
                            let Some(asset) = self.db.by_guid(guid) else {
                                packed.warnings.push(format!(
                                    "atlas {}: secondary texture {} of {} is missing; it is not \
                                     packed",
                                    def.name, sec.name, texture.name
                                ));
                                return None;
                            };
                            match self.cache.get_or_import(
                                self.textures,
                                &asset.file,
                                &meta_path(&asset.file),
                            ) {
                                Ok(texture) => Some(texture),
                                Err(e) => {
                                    packed.warnings.push(format!(
                                        "atlas {}: secondary texture {} does not import ({e:#}); \
                                         it is not packed",
                                        def.name,
                                        asset.file.display()
                                    ));
                                    None
                                }
                            }
                        })
                        .clone(),
                    None => None,
                };
                let Some(found) = found else {
                    // Reported once per texture (when first looked up); an unreadable GUID per sprite.
                    if guid.is_none() && !known {
                        packed.warnings.push(format!(
                            "atlas {}: secondary texture {} of {} names no asset; it is not packed",
                            def.name, sec.name, texture.name
                        ));
                    }
                    continue;
                };
                if found.width != texture.width
                    || found.height != texture.height
                    || found.format != unbound_assets::texture::TextureFormat::Rgba8
                {
                    packed.warnings.push(format!(
                        "atlas {}: secondary texture {} ({}) of {} is not the size or a byte \
                         format of its texture; it is not packed",
                        def.name, sec.name, found.name, texture.name
                    ));
                    continue;
                }
                own.push((sec.name.clone(), found));
            }
            secondary.push(own);
        }
        let meshes: Vec<_> = chosen
            .iter()
            .map(|&(t, s)| render_mesh(&textures[t].1.sprites[s]))
            .collect();
        let target_settings = self.textures.settings(&def.texture_meta);
        // The Editor checks the sprites' rects against the page size before packing.
        let needed = sprite_atlas::needed_size(
            chosen.iter().map(|&(t, s)| {
                let r = textures[t].1.sprites[s].texture_rect;
                [r.width, r.height]
            }),
            target_settings.max_size,
        );
        if needed != target_settings.max_size {
            packed.oversize = Some(needed);
            return Ok(packed);
        }
        let settings = PackSettings {
            max_size: target_settings.max_size,
            padding: def.padding,
            block_offset: def.block_offset,
            allow_rotation: def.allow_rotation,
            tight: def.tight,
            // PVRTC needs square textures: iOS's PVRTC formats (TextureImporterFormat 30-33).
            square: matches!(target_settings.explicit_format, Some(30..=33)),
            alpha_dilation: def.alpha_dilation,
        };
        let names: Vec<String> = chosen
            .iter()
            .map(|&(t, s)| textures[t].1.sprites[s].name.clone())
            .collect();
        let inputs: Vec<PackSprite> = chosen
            .iter()
            .zip(&meshes)
            .zip(&names)
            .map(|((&(t, s), (vertices, indices)), name)| {
                let texture = &textures[t].1;
                let sprite = &texture.sprites[s];
                let r = sprite.texture_rect;
                let rect = sprite
                    .texture_rect_bounds
                    .unwrap_or([r.x, r.y, r.width, r.height]);
                let ppu = sprite.texture_pixels_per_unit;
                PackSprite {
                    name,
                    texture_guid: textures[t].0.0,
                    texture: t,
                    secondary_hash: secondary_hash(&textures[t].1.sprites[s].secondary_textures),
                    texture_size: [texture.width, texture.height],
                    rect,
                    uv_transform: [
                        ppu,
                        r.x + sprite.pivot[0] * r.width,
                        ppu,
                        r.y + sprite.pivot[1] * r.height,
                    ],
                    vertices,
                    indices,
                }
            })
            .collect();
        let layout = match sprite_atlas::layout(&inputs, &settings) {
            Ok(layout) => layout,
            Err(sprite_atlas::PackError::Oversize { needed }) => {
                packed.oversize = Some(needed);
                return Ok(packed);
            }
        };
        let hash = blake3::hash(bytes).to_hex();
        for (page, &[w, h]) in layout.pages.iter().enumerate() {
            let texels = layout.page_pixels(page as u32, &settings, |i| {
                textures[chosen[i].0].1.mips[0].data.as_slice()
            });
            let mut page_settings = target_settings.clone();
            // Pages store alpha whatever their texels (an opaque page is unmeasured).
            page_settings.format_alpha = Some(true);
            page_settings.max_size = page_settings.max_size.max(w.max(h));
            let mut texture = unbound_assets::texture::import_pixels(
                &def.name,
                w,
                h,
                texels,
                page_settings.clone(),
            );
            self.textures.store(&mut texture)?;
            packed.page_names.push(format!(
                "sactx-{page}-{w}x{h}-{}-{}-{}",
                compression_name(&texture),
                def.name,
                &hash[..8]
            ));
            texture.name = packed.page_names[page].clone();
            packed.pages.push(texture);
            packed.uv_sizes.push([w, h]);
            // The page's secondary pages, one per shader property its sprites have (in the order
            // the sprites list them); a sprite without one leaves its place empty. A page takes the
            // atlas's settings and the colour space of its first texture (measured: a linear mask
            // makes a linear page, an sRGB texture under `_NormalMap` an sRGB one; textures of
            // both kinds in one page are unmeasured).
            let mut properties: Vec<(&str, bool)> = Vec::new();
            for (i, own) in secondary.iter().enumerate() {
                if layout.placements[i].page != page as u32 {
                    continue;
                }
                for (property, texture) in own {
                    if !properties.iter().any(|(p, _)| p == property) {
                        properties.push((property, texture.srgb));
                    }
                }
            }
            let mut pages = Vec::new();
            for (property, srgb) in properties {
                let texels = layout.page_pixels(page as u32, &settings, |i| {
                    secondary[i]
                        .iter()
                        .find(|(p, _)| p == property)
                        .map_or(&[][..], |(_, t)| t.mips[0].data.as_slice())
                });
                let mut own_settings = page_settings.clone();
                own_settings.srgb = srgb;
                own_settings.srgb_flag = srgb;
                let mut texture =
                    unbound_assets::texture::import_pixels(&def.name, w, h, texels, own_settings);
                self.textures.store(&mut texture)?;
                texture.name =
                    format!("{}{SECONDARY_SEPARATOR}{property}", packed.page_names[page]);
                pages.push((property.to_string(), texture));
            }
            packed.secondary_pages.push(pages);
        }
        for i in sprite_atlas::texture_order(&inputs) {
            let (t, s) = chosen[i];
            let sprite = &textures[t].1.sprites[s];
            let p = layout.placements[i];
            packed.sprites.push(PackedSprite {
                texture: textures[t].0.0,
                file_id: sprite.file_id,
                name: sprite.name.clone(),
                page: p.page,
                texture_rect: layout.texture_rect(i, inputs[i].rect),
                uv_transform: layout.uv_transform(i, inputs[i].uv_transform),
                rotation: p.rotation,
                secondary: secondary[i].iter().map(|(p, _)| p.clone()).collect(),
            });
        }
        Ok(packed)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sprite(mesh: Option<unbound_assets::sprite_mesh::SpriteMesh>) -> SpriteDef {
        let mut def: SpriteDef = serde_json::from_value(serde_json::json!({
            "file_id": 21300000, "file_id_source": "Fixed", "name": "S",
            "rect": {"x": 0.0, "y": 0.0, "width": 40.0, "height": 20.0},
            "texture_rect": {"x": 4.0, "y": 6.0, "width": 40.0, "height": 20.0},
            "alignment": 0, "pivot": [0.25, 0.5], "border": [0.0, 0.0, 0.0, 0.0],
            "pixels_per_unit": 100.0, "texture_pixels_per_unit": 100.0, "mesh_type": "Tight",
            "extrude": 1, "generate_physics_shape": false, "outline": [], "physics_shape": [],
            "sprite_id": ""
        }))
        .unwrap();
        def.mesh = mesh;
        def
    }

    #[test]
    fn secondary_sets_key_whole_sets_and_none_packs_first() {
        use unbound_assets::sprite::SecondaryTextureDef as Def;
        let def = |name: &str, guid: &str| Def {
            name: name.into(),
            guid: guid.into(),
        };
        let normal = def("_NormalMap", "11111111111111111111111111111111");
        let mask = def("_MaskTex", "22222222222222222222222222222222");
        let other = def("_NormalMap", "33333333333333333333333333333333");
        // No secondary textures sorts before any set.
        assert_eq!(secondary_hash(&[]), 0);
        let both = secondary_hash(&[normal.clone(), mask.clone()]);
        assert!(both > 0);
        // A set is the same whatever order the sprite lists it in.
        assert_eq!(secondary_hash(&[mask.clone(), normal.clone()]), both);
        // Another texture or one property fewer is another set.
        assert_ne!(secondary_hash(&[other.clone(), mask]), both);
        assert_ne!(secondary_hash(&[normal.clone()]), secondary_hash(&[other]));
        assert_ne!(secondary_hash(&[normal]), both);
    }

    #[test]
    fn render_mesh_is_the_import_mesh_or_the_rect_quad() {
        let (vertices, indices) = render_mesh(&sprite(None));
        // The quad in units from the pivot (10, 10): the Editor's quad order.
        assert_eq!(
            vertices,
            [[-0.1, 0.1], [0.3, 0.1], [-0.1, -0.1], [0.3, -0.1]]
        );
        assert_eq!(indices, [0, 1, 2, 2, 1, 3]);
        let mesh = unbound_assets::sprite_mesh::SpriteMesh {
            vertices: vec![[0.0, 0.0], [0.1, 0.0], [0.0, 0.1]],
            uv: vec![[0.0, 0.0]; 3],
            triangles: vec![0, 1, 2],
        };
        assert_eq!(render_mesh(&sprite(Some(mesh.clone()))).0, mesh.vertices);
    }

    #[test]
    fn page_keys_resolve_only_packed_atlases() {
        let set = AtlasSet::default();
        assert!(page(&set, "0123456789abcdef0123456789abcdef:0").is_err());
        assert!(page(&set, "not-a-key").is_err());
        let json = to_json(&AtlasSet {
            atlases: Vec::new(),
            cancelled: Some("Packing Error".into()),
        });
        assert_eq!(json["cancelled"], "Packing Error");
    }

    #[test]
    fn skipped_packables_are_reported_and_keyed() {
        let root =
            std::env::temp_dir().join(format!("unbound-atlas-skipped-{}", std::process::id()));
        let assets = root.join("Assets");
        std::fs::create_dir_all(assets.join("F/Nested")).unwrap();
        let meta = |path: &Path, guid: &str| {
            std::fs::write(
                format!("{}.meta", path.display()),
                format!("fileFormatVersion: 2\nguid: {guid}\n"),
            )
            .unwrap()
        };
        meta(&assets.join("F"), "f0000000000000000000000000000000");
        meta(&assets.join("F/Nested"), "f1000000000000000000000000000000");
        std::fs::write(assets.join("F/a.png"), b"").unwrap();
        meta(&assets.join("F/a.png"), "a0000000000000000000000000000000");
        let atlas = assets.join("A.spriteatlas");
        std::fs::write(&atlas, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!687078895 &1\nSpriteAtlas:\n  m_Name: A\n  m_EditorData:\n    packables:\n    - {fileID: 102900000, guid: f0000000000000000000000000000000, type: 3}\n").unwrap();
        meta(&atlas, "c0000000000000000000000000000000");
        let textures = TextureImporter::default();
        let cache = AssetCache::new(root.join("cache"));
        let scan = || {
            AssetDb::scan(&[unbound_project::assetdb::Root {
                dir: &assets,
                virtual_path: "Assets".into(),
            }])
        };
        let db = scan();
        let importer = AtlasImporter {
            db: &db,
            textures: &textures,
            cache: &cache,
        };
        let before = importer.dependencies(&atlas).unwrap();
        assert!(before.iter().any(|p| p.ends_with("a.png")));
        // A scripted importer's file in the folder: reported, and part of the key.
        std::fs::write(assets.join("F/Nested/b.psb"), b"").unwrap();
        meta(
            &assets.join("F/Nested/b.psb"),
            "b0000000000000000000000000000000",
        );
        let db = scan();
        let importer = AtlasImporter {
            db: &db,
            textures: &textures,
            cache: &cache,
        };
        let skipped = importer.skipped(&[(
            Guid::parse("f0000000000000000000000000000000").unwrap(),
            102900000,
        )]);
        assert_eq!(
            skipped.iter().map(|(p, _)| p.as_str()).collect::<Vec<_>>(),
            ["Assets/F/Nested/b.psb"]
        );
        let after = importer.dependencies(&atlas).unwrap();
        assert!(after.iter().any(|p| p.ends_with("b.psb")) && after.len() == before.len() + 1);
        std::fs::remove_dir_all(&root).ok();
    }

    #[test]
    fn a_pack_that_leaves_packables_out_is_incomplete() {
        let root =
            std::env::temp_dir().join(format!("unbound-atlas-incomplete-{}", std::process::id()));
        let assets = root.join("Assets");
        std::fs::create_dir_all(assets.join("F")).unwrap();
        std::fs::create_dir_all(assets.join("E")).unwrap();
        let meta = |path: &Path, guid: &str| {
            std::fs::write(
                format!("{}.meta", path.display()),
                format!("fileFormatVersion: 2\nguid: {guid}\n"),
            )
            .unwrap()
        };
        meta(&assets.join("F"), "f0000000000000000000000000000000");
        meta(&assets.join("E"), "e0000000000000000000000000000000");
        std::fs::write(assets.join("F/b.psb"), b"").unwrap();
        meta(&assets.join("F/b.psb"), "b0000000000000000000000000000000");
        let atlas = |name: &str, folder: &str, guid: &str| {
            let file = assets.join(format!("{name}.spriteatlas"));
            std::fs::write(&file, format!("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!687078895 &1\nSpriteAtlas:\n  m_Name: {name}\n  m_EditorData:\n    packables:\n    - {{fileID: 102900000, guid: {folder}, type: 3}}\n")).unwrap();
            meta(&file, guid);
            file
        };
        let scripted = atlas(
            "S",
            "f0000000000000000000000000000000",
            "c0000000000000000000000000000000",
        );
        let empty = atlas(
            "N",
            "e0000000000000000000000000000000",
            "d0000000000000000000000000000000",
        );
        let db = AssetDb::scan(&[unbound_project::assetdb::Root {
            dir: &assets,
            virtual_path: "Assets".into(),
        }]);
        let textures = TextureImporter::default();
        let cache = AssetCache::new(root.join("cache"));
        let importer = AtlasImporter {
            db: &db,
            textures: &textures,
            cache: &cache,
        };
        let import = |file: &Path| {
            let bytes = std::fs::read(file).unwrap();
            let meta =
                unbound_yaml::parse_value(&std::fs::read_to_string(meta_path(file)).unwrap())
                    .unwrap();
            importer.import(file, &bytes, &meta).unwrap()
        };
        // A scripted importer's file left out: incomplete; nothing to pack: complete.
        assert!(!import(&scripted).complete);
        assert!(import(&empty).complete);
        // A variant without a master: not packed, incomplete (its pack is not stored).
        let orphan = assets.join("V.spriteatlas");
        std::fs::write(&orphan, "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!687078895 &1\nSpriteAtlas:\n  m_Name: V\n  m_EditorData:\n    variantMultiplier: 2\n    packables: []\n  m_MasterAtlas: {fileID: 0}\n  m_IsVariant: 1\n").unwrap();
        meta(&orphan, "a1000000000000000000000000000000");
        let packed = import(&orphan);
        assert!(!packed.complete);
        assert!(
            packed
                .warnings
                .iter()
                .any(|w| w.contains("has no master atlas")),
            "{:?}",
            packed.warnings
        );
        std::fs::remove_dir_all(&root).ok();
    }

    #[test]
    fn atlas_set_merge_replaces_matching_guid_and_preserves_others() {
        let g1 = Guid::parse("10000000000000000000000000000001").unwrap();
        let g2 = Guid::parse("20000000000000000000000000000002").unwrap();
        let g3 = Guid::parse("30000000000000000000000000000003").unwrap();
        let make = |name: &str| PackedAtlas {
            name: name.into(),
            tag: name.into(),
            include_in_build: true,
            tight: true,
            master: None,
            scale: 1.0,
            uv_sizes: Vec::new(),
            pages: Vec::new(),
            page_names: Vec::new(),
            secondary_pages: Vec::new(),
            sprites: Vec::new(),
            oversize: None,
            warnings: Vec::new(),
            complete: true,
        };
        let mut set = AtlasSet {
            atlases: vec![(g1, make("A")), (g2, make("B"))],
            cancelled: None,
        };
        let update = AtlasSet {
            atlases: vec![(g2, make("B-updated")), (g3, make("C"))],
            cancelled: None,
        };
        set.merge(&update);
        assert_eq!(set.atlases.len(), 3);
        assert_eq!(set.atlases[0].1.name, "A");
        assert_eq!(set.atlases[1].1.name, "B-updated");
        assert_eq!(set.atlases[2].1.name, "C");

        // A cancelled pack leaves the existing set untouched.
        let cancelled = AtlasSet {
            atlases: vec![(g1, make("A-bad"))],
            cancelled: Some("oversize".into()),
        };
        set.merge(&cancelled);
        assert_eq!(set.atlases[0].1.name, "A");
    }
}
