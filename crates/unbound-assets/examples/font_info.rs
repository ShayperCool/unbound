//! Prints a font's face metrics and some glyphs: `cargo run -p unbound-assets --example font_info -- <font> [size] [chars]`.

use unbound_assets::font::FontFace;

fn main() -> anyhow::Result<()> {
    let mut args = std::env::args().skip(1);
    let path = args.next().expect("usage: font_info <font> [size] [chars]");
    let size: f32 = args.next().map_or(Ok(90.0), |s| s.parse())?;
    let chars = args.next().unwrap_or_else(|| "Hx!".into());
    let face = FontFace::from_bytes(std::fs::read(&path)?, 0)?;
    println!("{:?}", face.metrics());
    println!("{:?}", face.details());
    for c in chars.chars() {
        let index = face.glyph_index(c);
        if let Some(g) = face.render_glyph(index, size, false, false) {
            println!(
                "{c:?} #{index}: advance {} bounds {:?} bitmap {}x{} at ({}, {})",
                g.advance, g.bounds, g.width, g.height, g.left, g.top
            );
        }
    }
    // UNBOUND_SDF=<padding>: writes the first character's SDF to /tmp/glyph.sdf (top row first).
    if let (Ok(padding), Some(c)) = (std::env::var("UNBOUND_SDF"), chars.chars().next()) {
        let g = face
            .render_glyph_sdf(face.glyph_index(c), size, padding.parse()?)
            .expect("the face has the glyph");
        std::fs::write("/tmp/glyph.sdf", &g.coverage)?;
        println!("sdf of {c:?}: {}x{} + padding {padding}", g.width, g.height);
    }
    Ok(())
}
