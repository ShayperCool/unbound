/// The producers whose identities the build script makes: (environment variable, the directory
/// below `crates/` of the crate that makes the output).
pub const PRODUCERS: [(&str, &str); 2] = [
    ("UNBOUND_IDENTITY_SHADER_COMPILER", "unbound-shaders"),
    ("UNBOUND_IDENTITY_BUILTIN_SHADERS", "unbound-assets"),
];
