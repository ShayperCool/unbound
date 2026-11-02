//! What shaders declare, for the managed `Shader`/`ComputeShader` metadata API (the provider of
//! [`unbound_runtime::shader_declarations`], whose module documentation gives the answers'
//! format): SubShaders with their tags and passes (a shader's `Fallback` chain's SubShaders
//! follow its own, as `Shader.subshaderCount` counts them) and the keyword space.
//!
//! Built-in shaders answer from Unity's serialized shaders in the install's resources (their
//! keyword lists and tags are the engine's own data). Project shaders answer from their ShaderLab
//! source: tags as Unity stores them ([`canonical_tags`]) and the keywords their programs declare
//! ([`unbound_shaders::keywords`]). Both of those are rules of a release, so project shaders are
//! described only for a release the compatibility profile gives rules for
//! ([`unbound_compat::ShaderKeywordRules`]); otherwise they are reported unknown and the managed
//! side keeps its name-based behaviour.
//!
//! Installing the provider reads nothing. The first question scans the project's shader roots;
//! the built-in resources are read when a built-in shader (or a fallback to one) is asked about,
//! Unity's built-in shader sources only when a program needs their includes or a `UsePass` names
//! one of their passes. A failure to get an input is reported as a transient unknown (asked again
//! later), never as an empty or partial answer. Answers are not kept here: shader sources are the
//! session's snapshot the renderer draws with too ([`ShaderIndex`] parses each shader once,
//! generated shaders are picked up when they appear), so the same question gets the same answer
//! as long as the renderer's view is the same.
use serde_json::{Value, json};
use std::collections::HashSet;
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::{Duration, Instant};
use unbound_app::shaderlab::{PassInfo, ShaderIndex, ShaderLab};
use unbound_assets::builtin_shaders::{DeclarationMap, Declarations as Builtin};
use unbound_compat::ShaderKeywordRules;
use unbound_shaders::keywords::{Declared, Space};

/// Unity's built-in shader sources (`builtin_shaders-<release>.zip`): their includes and the
/// directories of their `.shader` files.
pub struct BuiltinSources {
    pub cg_includes: PathBuf,
    pub shader_roots: Vec<PathBuf>,
}

/// Finds (and, the first time, downloads) the built-in shader sources.
pub type FindBuiltinSources = dyn Fn() -> anyhow::Result<BuiltinSources> + Send + Sync;

/// Where the provider finds what it reads. Gathering these reads nothing.
pub struct Inputs {
    /// The project's Assets, its packages and its generated-shaders directory, scanned when
    /// first asked (a directory that does not exist is skipped, never created).
    pub project_roots: Vec<PathBuf>,
    /// How programs are preprocessed (no cache directory: nothing is compiled or cached; the
    /// include directory is resolved when first needed).
    pub settings: unbound_shaders::Settings,
    /// The install's `Editor/Data/CGIncludes` (used when it has `UnityCG.cginc`).
    pub install_cg_includes: PathBuf,
    /// The install's `Editor/Data` (its built-in resources).
    pub unity_data: PathBuf,
    pub cache_root: PathBuf,
    /// The compatibility profile's rules for the project's release (None: not measured).
    pub rules: Option<ShaderKeywordRules>,
    /// The project's release, for messages.
    pub release: String,
    pub builtin_sources: Arc<FindBuiltinSources>,
}

/// Something not known, why, and whether asking again may help.
#[derive(Debug, Clone)]
struct Unknown {
    reason: String,
    transient: bool,
}

impl Unknown {
    fn fixed(reason: impl Into<String>) -> Unknown {
        Unknown {
            reason: reason.into(),
            transient: false,
        }
    }

    fn transient(reason: impl Into<String>) -> Unknown {
        Unknown {
            reason: reason.into(),
            transient: true,
        }
    }
}

/// The resource files' sizes and modification times when they were read: a change reads them
/// again (their cache entry is keyed by their bytes).
type Stamp = Vec<(u64, Option<std::time::SystemTime>)>;

fn stamp(files: &[PathBuf]) -> Option<Stamp> {
    files
        .iter()
        .map(|f| {
            let m = std::fs::metadata(f).ok()?;
            Some((m.len(), m.modified().ok()))
        })
        .collect()
}

const SOURCE_RETRY_DELAY: Duration = Duration::from_secs(30);

#[derive(Default)]
struct State {
    project: Option<ShaderIndex>,
    builtin: Option<(Arc<DeclarationMap>, Stamp)>,
    sources: Option<(PathBuf, ShaderIndex)>,
    // Input failures are transient, but repeated metadata reads must not repeat large reads or downloads.
    builtin_failed: Option<(Option<Stamp>, Instant, Unknown)>,
    sources_failed: Option<(Instant, Unknown)>,
    compiler: Option<unbound_shaders::ShaderCompiler>,
    /// Conditions already warned about.
    warned: HashSet<String>,
}

/// The provider the runtime asks.
pub struct Provider {
    inputs: Inputs,
    state: Mutex<State>,
}

impl Provider {
    /// Reads nothing: see the module documentation.
    pub fn new(inputs: Inputs) -> Provider {
        Provider {
            inputs,
            state: Mutex::new(State::default()),
        }
    }
}

impl unbound_runtime::shader_declarations::Provider for Provider {
    fn shader(&self, name: &str) -> String {
        let mut state = self.state.lock().unwrap_or_else(|e| e.into_inner());
        let answer = state.describe(&self.inputs, name);
        state.warn(&answer);
        answer.to_string()
    }

    fn compute(&self, file: &str) -> String {
        let mut state = self.state.lock().unwrap_or_else(|e| e.into_inner());
        let answer = match state.compute_keywords(&self.inputs, file) {
            Ok(keywords) => json!({
                "found": true, "keywords_known": true, "transient": false, "reason": "",
                "keywords": keywords_json(&keywords),
            }),
            Err(u) => json!({
                "found": true, "keywords_known": false, "transient": u.transient,
                "reason": format!("compute shader {file}: {}", u.reason), "keywords": [],
            }),
        };
        state.warn(&answer);
        answer.to_string()
    }
}

/// Tag names Unity stores upper-cased (looked up case-insensitively).
const UPPER_NAMES: [&str; 4] = ["Queue", "IgnoreProjector", "LightMode", "ShadowSupport"];
/// `LightMode` values Unity stores upper-cased; others (`Vertex`, `VertexLM`, a render
/// pipeline's own) as written.
const UPPER_LIGHT_MODES: [&str; 8] = [
    "Always",
    "ForwardBase",
    "ForwardAdd",
    "Deferred",
    "ShadowCaster",
    "Meta",
    "MotionVectors",
    "GrabPass",
];
/// Tags whose values Unity stores as `true`/`false`.
const BOOLEAN_TAGS: [&str; 5] = [
    "IgnoreProjector",
    "ForceSupported",
    "CanUseSpriteAtlas",
    "TerrainCompatible",
    "DisableBatching",
];

/// Tags as Unity stores them from a source, under the 6000.0.71f1 rules (the canonical
/// tests/projects/shaderkeywords tags, and every built-in shader's serialized tags against its
/// source): `Queue`, `IgnoreProjector`, `LightMode` and `ShadowSupport` names upper-cased; the
/// built-in pipeline's light modes upper-cased; boolean tags lower-cased (`DisableBatching`
/// `LODFading` as `LodFading`). Sorted by name.
pub fn canonical_tags<'a>(
    tags: impl Iterator<Item = (&'a String, &'a String)>,
) -> Vec<(String, String)> {
    let mut out: Vec<(String, String)> = tags
        .map(|(k, v)| {
            let name = match UPPER_NAMES.iter().find(|n| n.eq_ignore_ascii_case(k)) {
                Some(n) => n.to_ascii_uppercase(),
                None => k.clone(),
            };
            let value = if k.eq_ignore_ascii_case("LightMode")
                && UPPER_LIGHT_MODES.iter().any(|m| m.eq_ignore_ascii_case(v))
            {
                v.to_ascii_uppercase()
            } else if BOOLEAN_TAGS.iter().any(|t| t.eq_ignore_ascii_case(k))
                && (v.eq_ignore_ascii_case("true") || v.eq_ignore_ascii_case("false"))
            {
                v.to_ascii_lowercase()
            } else if k.eq_ignore_ascii_case("DisableBatching")
                && v.eq_ignore_ascii_case("LODFading")
            {
                "LodFading".to_string()
            } else {
                v.clone()
            };
            (name, value)
        })
        .collect();
    out.sort();
    out
}

fn one_tag(name: &str, value: &str) -> Vec<(String, String)> {
    let (k, v) = (name.to_string(), value.to_string());
    canonical_tags([(&k, &v)].into_iter())
}

fn tags_json(tags: &[(String, String)]) -> Value {
    Value::Array(tags.iter().map(|(k, v)| json!([k, v])).collect())
}

fn keywords_json(keywords: &[Declared]) -> Value {
    serde_json::to_value(keywords).unwrap_or(Value::Array(Vec::new()))
}

fn builtin_subshaders(builtin: &Builtin) -> Vec<Value> {
    builtin
        .subshaders
        .iter()
        .map(|sub| {
            json!({
                "tags": tags_json(&sub.tags),
                "passes": sub.passes.iter().map(|p| json!({ "name": p.name, "tags": tags_json(&p.tags) })).collect::<Vec<_>>(),
            })
        })
        .collect()
}

/// An answer for a shader, its parts known or not.
struct Description {
    found: bool,
    builtin: bool,
    subshaders: Result<Vec<Value>, Unknown>,
    keywords: Result<Vec<Declared>, Unknown>,
}

impl Description {
    fn not_found(why: Option<Unknown>) -> Description {
        let why = why.unwrap_or_else(|| Unknown::fixed("no shader by this name"));
        Description {
            found: false,
            builtin: false,
            subshaders: Err(why.clone()),
            keywords: Err(why),
        }
    }

    fn to_json(&self, name: &str) -> Value {
        let mut reasons = Vec::new();
        let mut transient = false;
        for part in [self.subshaders.as_ref().err(), self.keywords.as_ref().err()]
            .into_iter()
            .flatten()
        {
            transient |= part.transient;
            if !reasons.contains(&part.reason) {
                reasons.push(part.reason.clone());
            }
        }
        let reason = if reasons.is_empty() || !self.found && !transient {
            String::new()
        } else {
            format!("shader `{name}`: {}", reasons.join("; "))
        };
        json!({
            "found": self.found,
            "builtin": self.builtin,
            "subshaders_known": self.subshaders.is_ok(),
            "keywords_known": self.keywords.is_ok(),
            "transient": transient,
            "reason": reason,
            "subshaders": self.subshaders.as_ref().map(|s| Value::Array(s.clone())).unwrap_or(json!([])),
            "keywords": self.keywords.as_ref().map(|k| keywords_json(k)).unwrap_or(json!([])),
        })
    }
}

impl State {
    /// Warns once per condition an answer reports.
    fn warn(&mut self, answer: &Value) {
        let reason = answer["reason"].as_str().unwrap_or("");
        if !reason.is_empty() && self.warned.insert(reason.to_string()) {
            tracing::warn!("{reason}");
        }
    }

    fn project(&mut self, inputs: &Inputs) -> &mut ShaderIndex {
        self.project
            .get_or_insert_with(|| ShaderIndex::scan(&inputs.project_roots))
    }

    /// The built-in shaders' declarations, read again when the resource files changed.
    fn builtin(&mut self, inputs: &Inputs) -> Result<Arc<DeclarationMap>, Unknown> {
        let files = unbound_assets::builtin_shaders::declaration_sources(&inputs.unity_data);
        let now = stamp(&files);
        if let (Some((map, seen)), Some(now)) = (&self.builtin, &now)
            && seen == now
        {
            return Ok(map.clone());
        }
        if let Some((seen, when, why)) = &self.builtin_failed
            && seen == &now && when.elapsed() < SOURCE_RETRY_DELAY
        {
            return Err(why.clone());
        }
        self.builtin = None;
        self.builtin_failed = None;
        let map = match unbound_assets::builtin_shaders::declarations(&inputs.unity_data, &inputs.cache_root) {
            Ok(map) => map,
            Err(e) => {
                let why = Unknown::transient(format!("Unity's built-in shaders cannot be read: {e:#}"));
                self.builtin_failed = Some((now, Instant::now(), why.clone()));
                return Err(why);
            }
        };
        let map = Arc::new(map);
        if let Some(now) = now {
            self.builtin = Some((map.clone(), now));
        }
        Ok(map)
    }

    /// Failed source discovery retries at most once in 30 seconds across all shader questions.
    fn sources(&mut self, inputs: &Inputs) -> Result<&mut (PathBuf, ShaderIndex), Unknown> {
        self.sources_at(inputs, Instant::now())
    }

    fn sources_at(&mut self, inputs: &Inputs, now: Instant) -> Result<&mut (PathBuf, ShaderIndex), Unknown> {
        if self.sources.is_none() {
            if let Some((when, why)) = &self.sources_failed
                && now.duration_since(*when) < SOURCE_RETRY_DELAY
            {
                return Err(why.clone());
            }
            let found = match (inputs.builtin_sources)() {
                Ok(found) => found,
                Err(e) => {
                    let why = Unknown::transient(format!("Unity's built-in shader sources are not available: {e:#}"));
                    self.sources_failed = Some((now, why.clone()));
                    return Err(why);
                }
            };
            self.sources_failed = None;
            self.sources = Some((found.cg_includes, ShaderIndex::scan(&found.shader_roots)));
        }
        Ok(self.sources.as_mut().unwrap())
    }

    /// The preprocessor, its includes resolved when first needed.
    fn compiler(&mut self, inputs: &Inputs) -> Result<&unbound_shaders::ShaderCompiler, Unknown> {
        if self.compiler.is_none() {
            let cg = if inputs.install_cg_includes.join("UnityCG.cginc").is_file() {
                inputs.install_cg_includes.clone()
            } else {
                self.sources(inputs)
                    .map_err(|u| {
                        Unknown::transient(format!(
                            "its programs' includes are not available ({})",
                            u.reason
                        ))
                    })?
                    .0
                    .clone()
            };
            let mut settings = inputs.settings.clone();
            settings.includes.cg_includes = Some(cg);
            settings.cache_dir = None;
            let compiler = unbound_shaders::ShaderCompiler::new(settings).map_err(|e| {
                Unknown::transient(format!("shader programs cannot be read: {e:#}"))
            })?;
            self.compiler = Some(compiler);
        }
        Ok(self.compiler.as_ref().unwrap())
    }

    fn describe(&mut self, inputs: &Inputs, name: &str) -> Value {
        self.description(inputs, name).to_json(name)
    }

    /// A project shader hides a built-in one of the same name (as `Shader.Find` finds it).
    fn description(&mut self, inputs: &Inputs, name: &str) -> Description {
        if name.is_empty() {
            return Description::not_found(None);
        }
        if let Some(lab) = self.project(inputs).get(name).cloned() {
            return self.describe_project(inputs, &lab);
        }
        // A project shader that exists but cannot be read now: scan again next time.
        if self.project(inputs).names().iter().any(|n| n == name) {
            self.project = None;
            return Description::not_found(Some(Unknown::transient(
                "its source cannot be read or parsed",
            )));
        }
        let map = match self.builtin(inputs) {
            Ok(map) => map,
            Err(u) => return Description::not_found(Some(u)),
        };
        let Some(builtin) = map.get(name) else {
            return Description::not_found(None);
        };
        if inputs.rules.is_none() {
            let why = Unknown::fixed(format!(
                "SubShaders and keyword spaces of built-in shaders are not measured for Unity {}",
                inputs.release
            ));
            return Description {
                found: true,
                builtin: true,
                subshaders: Err(why.clone()),
                keywords: Err(why),
            };
        }
        let mut subshaders = Ok(builtin_subshaders(builtin));
        if let Ok(list) = &mut subshaders
            && let Err(u) = self.fallbacks(inputs, &builtin.fallback, list, 0)
        {
            subshaders = Err(u);
        }
        let keywords = builtin
            .keywords
            .iter()
            .map(|k| Declared {
                name: k.name.clone(),
                local: k.local,
                dynamic: k.dynamic,
            })
            .collect();
        Description {
            found: true,
            builtin: true,
            subshaders,
            keywords: Ok(keywords),
        }
    }

    fn describe_project(&mut self, inputs: &Inputs, lab: &ShaderLab) -> Description {
        let Some(_rules) = inputs.rules else {
            let why = Unknown::fixed(format!(
                "SubShaders and keyword spaces of project shaders are not measured for Unity {}",
                inputs.release
            ));
            return Description {
                found: true,
                builtin: false,
                subshaders: Err(why.clone()),
                keywords: Err(why),
            };
        };
        let mut subshaders = self.subshaders(inputs, lab);
        if let (Ok(list), Some(fallback)) = (&mut subshaders, &lab.fallback)
            && let Err(u) = self.fallbacks(inputs, fallback, list, 0)
        {
            subshaders = Err(u);
        }
        Description {
            found: true,
            builtin: false,
            subshaders,
            keywords: self.keywords(inputs, lab),
        }
    }

    /// A fallback's SubShaders (and its fallback's), by Unity's resolution of the name (the
    /// legacy shaders also answer to their old names, `VertexLit`). A fallback no shader answers
    /// to adds none.
    fn fallbacks(
        &mut self,
        inputs: &Inputs,
        name: &str,
        out: &mut Vec<Value>,
        depth: u32,
    ) -> Result<(), Unknown> {
        if name.is_empty() {
            return Ok(());
        }
        if depth > 8 {
            return Err(Unknown::fixed(format!(
                "its Fallback chain does not end ({name})"
            )));
        }
        let legacy = format!("Legacy Shaders/{name}");
        for candidate in [name, legacy.as_str()] {
            if let Some(lab) = self.project(inputs).get(candidate).cloned() {
                if inputs.rules.is_none() {
                    return Err(Unknown::fixed(format!(
                        "its Fallback `{candidate}` is a project shader, not described for Unity {}",
                        inputs.release
                    )));
                }
                out.extend(self.subshaders(inputs, &lab)?);
                return match &lab.fallback {
                    Some(f) => self.fallbacks(inputs, f, out, depth + 1),
                    None => Ok(()),
                };
            }
        }
        for candidate in [name, legacy.as_str()] {
            if self.project(inputs).names().iter().any(|n| n == candidate) {
                self.project = None;
                return Err(Unknown::transient(format!(
                    "its Fallback `{candidate}` cannot be read or parsed"
                )));
            }
        }
        let map = self.builtin(inputs).map_err(|u| {
            Unknown::transient(format!("its Fallback `{name}` is not known: {}", u.reason))
        })?;
        for candidate in [name, legacy.as_str()] {
            if let Some(builtin) = map.get(candidate) {
                out.extend(builtin_subshaders(builtin));
                let next = builtin.fallback.clone();
                return self.fallbacks(inputs, &next, out, depth + 1);
            }
        }
        Ok(())
    }

    /// The name and own tags of the pass a `UsePass "Shader/PASS"` names (pass names compare
    /// case-insensitively): a project shader's, else a built-in shader's as Unity serialized it.
    fn use_pass_meta(
        &mut self,
        inputs: &Inputs,
        target: &str,
        depth: u32,
    ) -> Result<(String, Vec<(String, String)>), Unknown> {
        let unresolved = || Unknown::fixed(format!("its UsePass \"{target}\" names no pass"));
        let (shader, pass) = target.rsplit_once('/').ok_or_else(unresolved)?;
        if let Some(lab) = self.project(inputs).get(shader).cloned() {
            let found = lab
                .subshaders
                .iter()
                .flat_map(|s| s.passes.iter())
                .find(|p| p.name.eq_ignore_ascii_case(pass))
                .cloned()
                .ok_or_else(unresolved)?;
            return match &found.use_pass {
                Some(next) if depth < 4 => self.use_pass_meta(inputs, next, depth + 1),
                Some(_) => Err(unresolved()),
                None => Ok((found.name.clone(), canonical_tags(found.own_tags.iter()))),
            };
        }
        let map = self.builtin(inputs).map_err(|u| {
            Unknown::transient(format!(
                "its UsePass \"{target}\" is not known: {}",
                u.reason
            ))
        })?;
        map.get(shader)
            .and_then(|b| {
                b.subshaders
                    .iter()
                    .flat_map(|s| s.passes.iter())
                    .find(|p| p.name.eq_ignore_ascii_case(pass))
            })
            .map(|p| (p.name.clone(), p.tags.clone()))
            .ok_or_else(unresolved)
    }

    /// The pass a `UsePass` names, with its program: a project shader's, else one of Unity's
    /// built-in shader sources'.
    fn use_pass_program(
        &mut self,
        inputs: &Inputs,
        target: &str,
        depth: u32,
    ) -> Result<PassInfo, Unknown> {
        let unresolved = || Unknown::fixed(format!("its UsePass \"{target}\" names no pass"));
        let (shader, pass) = target.rsplit_once('/').ok_or_else(unresolved)?;
        let find = |lab: &ShaderLab| {
            lab.subshaders
                .iter()
                .flat_map(|s| s.passes.iter())
                .find(|p| p.name.eq_ignore_ascii_case(pass))
                .cloned()
        };
        let found = match self.project(inputs).get(shader).map(find) {
            Some(found) => found,
            None => {
                let (_, index) = self.sources(inputs).map_err(|u| {
                    Unknown::transient(format!(
                        "its UsePass \"{target}\" is not known: {}",
                        u.reason
                    ))
                })?;
                index.get(shader).and_then(find)
            }
        }
        .ok_or_else(unresolved)?;
        match &found.use_pass {
            Some(next) if depth < 4 => self.use_pass_program(inputs, next, depth + 1),
            Some(_) => Err(unresolved()),
            None => Ok(found),
        }
    }

    /// A project shader's own SubShaders: tags and passes (names and own tags as Unity stores
    /// them; a surface program stands for the passes Unity generates for it).
    fn subshaders(&mut self, inputs: &Inputs, lab: &ShaderLab) -> Result<Vec<Value>, Unknown> {
        let mut out = Vec::new();
        for sub in &lab.subshaders {
            let mut passes = Vec::new();
            let mut surface_done = false;
            for pass in &sub.passes {
                if let Some(program) = pass.program.as_ref().filter(|p| p.surface) {
                    if !surface_done {
                        surface_done = true;
                        for generated in unbound_shaders::keywords::surface_passes(
                            &surface_options(&program.code),
                        ) {
                            let tags = one_tag("LightMode", generated.light_mode);
                            passes
                                .push(json!({ "name": generated.name, "tags": tags_json(&tags) }));
                        }
                    }
                    continue;
                }
                let (name, tags) = if pass.grab.is_some() {
                    (String::new(), one_tag("LightMode", "GrabPass"))
                } else if let Some(target) = &pass.use_pass {
                    self.use_pass_meta(inputs, target, 0)?
                } else {
                    (pass.name.clone(), canonical_tags(pass.own_tags.iter()))
                };
                passes.push(json!({ "name": name, "tags": tags_json(&tags) }));
            }
            out.push(
                json!({ "tags": tags_json(&canonical_tags(sub.tags.iter())), "passes": passes }),
            );
        }
        Ok(out)
    }

    /// The keywords a project shader's own SubShaders declare (an error: the space is unknown).
    fn keywords(&mut self, inputs: &Inputs, lab: &ShaderLab) -> Result<Vec<Declared>, Unknown> {
        let mut space = Space::shader();
        for sub in &lab.subshaders {
            let mut surface_done = false;
            for pass in &sub.passes {
                if pass.grab.is_some() {
                    continue;
                }
                let pass = match &pass.use_pass {
                    Some(target) => self.use_pass_program(inputs, target, 0)?,
                    None => pass.clone(),
                };
                if pass.glsl {
                    return Err(Unknown::fixed("a GLSLPROGRAM pass's keywords are not read"));
                }
                let Some(program) = &pass.program else {
                    space.add_fixed_function();
                    continue;
                };
                if program.surface && surface_done {
                    continue;
                }
                let p = unbound_shaders::Program {
                    file: program.file.clone(),
                    source: program.code.clone(),
                    cg: program.cg,
                    light_mode: pass.states.tags.get("LightMode").cloned(),
                };
                let compiler = self.compiler(inputs)?;
                let pragmas = compiler.pragma_lines(&p).map_err(|e| {
                    Unknown::fixed(format!(
                        "a pass's keyword declarations cannot be read: {e:#}"
                    ))
                })?;
                let added = if program.surface {
                    surface_done = true;
                    space.add_surface(&pragmas, &surface_options(&program.code))
                } else {
                    space.add_program(&pragmas)
                };
                added.map_err(|e| Unknown::fixed(format!("{e:#}")))?;
            }
        }
        Ok(space.keywords)
    }

    fn compute_keywords(&mut self, inputs: &Inputs, file: &str) -> Result<Vec<Declared>, Unknown> {
        if inputs.rules.is_none() {
            return Err(Unknown::fixed(format!(
                "keyword spaces of compute shaders are not measured for Unity {}",
                inputs.release
            )));
        }
        let source = std::fs::read_to_string(file)
            .map_err(|e| Unknown::transient(format!("it cannot be read: {e}")))?;
        let program = unbound_shaders::compute::ComputeProgram {
            file: Path::new(file).to_path_buf(),
            source,
        };
        let compiler = self.compiler(inputs)?;
        compiler
            .compute_keywords(&program)
            .map_err(|e| Unknown::fixed(format!("its keyword declarations cannot be read: {e:#}")))
    }
}

/// A surface program's `#pragma surface` options: the words after its function and lighting
/// model.
fn surface_options(code: &str) -> Vec<String> {
    code.lines()
        .map(str::trim)
        .find_map(|l| {
            let mut words = l.strip_prefix("#pragma")?.split_whitespace();
            (words.next()? == "surface").then(|| words.skip(2).map(String::from).collect())
        })
        .unwrap_or_default()
}

#[cfg(test)]
mod tests;
