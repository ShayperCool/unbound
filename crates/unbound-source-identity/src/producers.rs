/// The producers whose identities the build script makes: (environment variable, crate,
/// modules (none: all of it), locked packages).
pub const PRODUCERS: [(&str, &str, &[&str], &[&str]); 2] = [
    (
        "UNBOUND_IDENTITY_SHADER_COMPILER",
        "unbound_shaders",
        &[],
        &["naga", "shaderc", "shaderc-sys"],
    ),
    (
        "UNBOUND_IDENTITY_BUILTIN_SHADERS",
        "unbound_assets",
        &["builtin_shaders"],
        &["postcard", "serde", "serde_core", "serde_derive"],
    ),
];
