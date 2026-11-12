//! Prints a font's face metrics and some glyphs as TextCore loads them:
//! `cargo run -p unbound-assets --example font_info -- <font> [size] [chars]`.

use unbound_assets::font::{FontFace, render_mode};

/// FreeType's `FT_LOAD_NO_HINTING`, TextCore's `GlyphLoadFlags.LOAD_NO_HINTING`.
const LOAD_NO_HINTING: i32 = 2;

fn main() -> anyhow::Result<()> {
    let mut args = std::env::args().skip(1);
    let path = args.next().expect("usage: font_info <font> [size] [chars]");
    let size: i32 = args.next().map_or(Ok(90), |s| s.parse())?;
    let chars = args.next().unwrap_or_else(|| "Hx!".into());
    let face = FontFace::from_bytes(std::fs::read(&path)?, 0)?;
    println!("{:?}", face.metrics());
    println!("{:?}", face.details());
    for c in chars.chars() {
        let index = u32::from(face.glyph_index(c));
        if let Some(g) = face.textcore_glyph(index, size, LOAD_NO_HINTING) {
            println!("{c:?} #{index}: {g:?}");
        }
    }
    // UNBOUND_SDF=<padding>: writes the first character's SDFAA tile to /tmp/glyph.sdf (top row
    // first).
    if let (Ok(padding), Some(c)) = (std::env::var("UNBOUND_SDF"), chars.chars().next()) {
        let tile = face
            .textcore_render(
                u32::from(face.glyph_index(c)),
                size,
                render_mode::SDFAA,
                padding.parse()?,
            )
            .expect("the face has the glyph");
        std::fs::write("/tmp/glyph.sdf", &tile.pixels)?;
        println!("sdf of {c:?}: {:?} + padding {}", tile.glyph, tile.padding);
    }
    Ok(())
}
