use super::*;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};
use unbound_runtime::shader_declarations::Provider as _;

/// A private directory in our cache (never /tmp, never a project's authored files), removed
/// with everything in it when the test ends.
struct Scratch(PathBuf);

impl Scratch {
    fn new(label: &str) -> Scratch {
        static N: AtomicUsize = AtomicUsize::new(0);
        let dir = unbound_project::cache_root().join("tests").join(format!(
            "{label}-{}-{}",
            std::process::id(),
            N.fetch_add(1, Ordering::Relaxed)
        ));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        Scratch(dir)
    }

    fn write(&self, relative: &str, text: &str) -> PathBuf {
        let path = self.0.join(relative);
        std::fs::create_dir_all(path.parent().unwrap()).unwrap();
        std::fs::write(&path, text).unwrap();
        path
    }
}

impl Drop for Scratch {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

/// Every file and directory below `dir`.
fn entries(dir: &Path) -> Vec<PathBuf> {
    let mut out = Vec::new();
    let mut stack = vec![dir.to_path_buf()];
    while let Some(d) = stack.pop() {
        for e in std::fs::read_dir(&d).into_iter().flatten().flatten() {
            let p = e.path();
            if p.is_dir() {
                stack.push(p.clone());
            }
            out.push(p);
        }
    }
    out.sort();
    out
}

/// Inputs over a scratch project: its `Assets`, includes from `cg` (a directory with a
/// `UnityCG.cginc`, or one without it so the built-in sources are asked for), the given
/// built-in resources and rules. `sources` counts the calls for built-in sources and answers
/// with `cg` once `available` is set.
fn inputs(
    scratch: &Scratch,
    cg: PathBuf,
    unity_data: PathBuf,
    rules: Option<ShaderKeywordRules>,
    calls: Arc<AtomicUsize>,
    available: Arc<AtomicBool>,
) -> Inputs {
    let sources_cg = scratch.0.join("sources/CGIncludes");
    Inputs {
        project_roots: vec![
            scratch.0.join("Assets"),
            scratch.0.join("Library/Unbound/GeneratedShaders"),
        ],
        settings: unbound_shaders::Settings {
            includes: unbound_shaders::Includes {
                project: scratch.0.clone(),
                ..Default::default()
            },
            unity_version: 6000071,
            ..Default::default()
        },
        install_cg_includes: cg,
        unity_data,
        cache_root: scratch.0.join("cache"),
        rules,
        release: "6000.0.71f1".into(),
        builtin_sources: Arc::new(move || {
            calls.fetch_add(1, Ordering::Relaxed);
            if available.load(Ordering::Relaxed) {
                Ok(BuiltinSources {
                    cg_includes: sources_cg.clone(),
                    shader_roots: Vec::new(),
                })
            } else {
                anyhow::bail!("offline")
            }
        }),
    }
}

const MEASURED: Option<ShaderKeywordRules> = Some(ShaderKeywordRules::Unity6000_0_71f1);

fn ask(provider: &Provider, name: &str) -> Value {
    serde_json::from_str(&provider.shader(name)).unwrap()
}

fn ask_compute(provider: &Provider, file: &Path) -> Value {
    serde_json::from_str(&provider.compute(file.to_str().unwrap())).unwrap()
}

fn names(answer: &Value) -> Vec<String> {
    answer["keywords"]
        .as_array()
        .unwrap()
        .iter()
        .map(|k| k["name"].as_str().unwrap().to_string())
        .collect()
}

const STEREO: [&str; 4] = [
    "STEREO_INSTANCING_ON",
    "UNITY_SINGLE_PASS_STEREO",
    "STEREO_MULTIVIEW_ON",
    "STEREO_CUBEMAP_RENDER_ON",
];

fn stereo_and(rest: &[&str]) -> Vec<String> {
    STEREO.iter().chain(rest).map(|s| s.to_string()).collect()
}

#[test]
fn tags_are_stored_as_unity_stores_them() {
    let tags: std::collections::HashMap<String, String> = [
        ("LightMode", "ForwardBase"),
        ("Queue", "Geometry+5"),
        ("IgnoreProjector", "True"),
        ("DisableBatching", "LODFading"),
        ("PerformanceChecks", "False"),
        ("RenderType", "Opaque"),
    ]
    .iter()
    .map(|(k, v)| (k.to_string(), v.to_string()))
    .collect();
    let want: Vec<(String, String)> = [
        ("DisableBatching", "LodFading"),
        ("IGNOREPROJECTOR", "true"),
        ("LIGHTMODE", "FORWARDBASE"),
        ("PerformanceChecks", "False"),
        ("QUEUE", "Geometry+5"),
        ("RenderType", "Opaque"),
    ]
    .iter()
    .map(|(k, v)| (k.to_string(), v.to_string()))
    .collect();
    assert_eq!(canonical_tags(tags.iter()), want);
    // The canonical probe.Probe/KwFixed.passLightModes `ALWAYS`; a pipeline's and the vertex
    // lit modes as written (`UniversalForward`, `Vertex`).
    assert_eq!(one_tag("LightMode", "Always")[0].1, "ALWAYS");
    assert_eq!(
        one_tag("LightMode", "UniversalForward")[0].1,
        "UniversalForward"
    );
    assert_eq!(one_tag("LightMode", "Vertex")[0].1, "Vertex");
}

#[test]
fn surface_options_follow_function_and_model() {
    assert_eq!(
        surface_options("CGPROGRAM\n#pragma surface surf Lambert addshadow noforwardadd\nENDCG"),
        vec!["addshadow".to_string(), "noforwardadd".to_string()]
    );
}

/// A pass's own tags leave out the SubShader's, also a tag both have; the effective tags the
/// renderer reads keep both, the pass's winning.
#[test]
fn own_pass_tags_leave_out_the_subshaders() {
    let lab = ShaderLab::parse(
        r#"Shader "Tests/OwnTags" {
            SubShader {
                Tags { "RenderType"="Opaque" "Queue"="Geometry" "LightMode"="Always" }
                Pass { Tags { "LightMode"="ForwardBase" } }
                Pass { Tags { "RenderType"="Opaque" } }
                Pass { }
            }
        }"#,
    )
    .unwrap();
    let passes = &lab.subshaders[0].passes;
    let own = |i: usize| {
        let mut t: Vec<_> = passes[i]
            .own_tags
            .iter()
            .map(|(k, v)| (k.as_str(), v.as_str()))
            .collect();
        t.sort();
        t
    };
    assert_eq!(own(0), [("LightMode", "ForwardBase")]);
    assert_eq!(own(1), [("RenderType", "Opaque")]);
    assert!(own(2).is_empty());
    assert_eq!(passes[0].states.tags["LightMode"], "ForwardBase");
    assert_eq!(passes[0].states.tags["Queue"], "Geometry");
    assert_eq!(passes[2].states.tags["LightMode"], "Always");
}

/// Installing the provider reads and creates nothing; a question about a fixed-function project
/// shader needs neither the built-in resources nor the built-in sources nor a preprocessor.
#[test]
fn installing_reads_nothing_and_questions_read_only_what_they_need() {
    let scratch = Scratch::new("decl-lazy");
    let calls = Arc::new(AtomicUsize::new(0));
    let provider = Provider::new(inputs(
        &scratch,
        scratch.0.join("no-includes"),
        scratch.0.join("no-install"),
        MEASURED,
        calls.clone(),
        Arc::new(AtomicBool::new(true)),
    ));
    assert!(entries(&scratch.0).is_empty(), "{:?}", entries(&scratch.0));
    assert_eq!(calls.load(Ordering::Relaxed), 0);
    assert!(provider.state.lock().unwrap().project.is_none());

    scratch.write(
        "Assets/Fixed.shader",
        r#"Shader "Tests/Fixed" { SubShader { Tags { "IgnoreProjector"="True" } Pass { Tags { "LightMode"="Always" } Color (1,1,1,1) } } }"#,
    );
    let answer = ask(&provider, "Tests/Fixed");
    assert_eq!(answer["found"], true);
    assert_eq!(answer["subshaders_known"], true);
    assert_eq!(answer["keywords_known"], true);
    assert_eq!(answer["transient"], false);
    assert_eq!(
        names(&answer),
        stereo_and(&["FOG_LINEAR", "FOG_EXP", "FOG_EXP2"])
    );
    assert_eq!(
        answer["subshaders"][0]["tags"],
        json!([["IGNOREPROJECTOR", "true"]])
    );
    assert_eq!(
        answer["subshaders"][0]["passes"][0]["tags"],
        json!([["LIGHTMODE", "ALWAYS"]])
    );
    assert_eq!(
        calls.load(Ordering::Relaxed),
        0,
        "no built-in sources were needed"
    );
    assert!(
        !scratch.0.join("cache").exists(),
        "no built-in resources were read or cached"
    );
    assert!(
        !scratch.0.join("Library").exists(),
        "nothing was created in the project"
    );
}

/// An unmeasured release: project shaders and compute files are unknown (not empty), and not
/// transient; nothing about them is guessed.
#[test]
fn an_unmeasured_release_reports_project_spaces_unknown() {
    let scratch = Scratch::new("decl-unmeasured");
    scratch.write(
        "Assets/Fixed.shader",
        r#"Shader "Tests/Fixed" { SubShader { Pass { } } }"#,
    );
    let compute = scratch.write("Assets/Empty.compute", "#pragma kernel CSMain\n");
    let mut i = inputs(
        &scratch,
        scratch.0.join("no-includes"),
        scratch.0.join("no-install"),
        None,
        Arc::new(AtomicUsize::new(0)),
        Arc::new(AtomicBool::new(true)),
    );
    i.release = "6000.0.72f1".into();
    let provider = Provider::new(i);
    let answer = ask(&provider, "Tests/Fixed");
    assert_eq!(answer["found"], true);
    assert_eq!(answer["subshaders_known"], false);
    assert_eq!(answer["keywords_known"], false);
    assert_eq!(answer["transient"], false);
    assert!(answer["reason"].as_str().unwrap().contains("6000.0.72f1"));
    assert_eq!(answer["keywords"], json!([]));
    let answer = ask_compute(&provider, &compute);
    assert_eq!(answer["keywords_known"], false);
    assert_eq!(answer["transient"], false);
}

/// Unknown is not empty: a GLSL pass, an unresolved include, an unresolved UsePass and an
/// unknown declaration form make the space unknown; a pass that declares nothing gives a known
/// space of the stereo keywords only.
#[test]
fn failures_are_unknown_and_declaring_nothing_is_known() {
    let scratch = Scratch::new("decl-unknown");
    let cg = scratch.0.join("CGIncludes");
    scratch.write("CGIncludes/UnityCG.cginc", "// stand-in\n");
    scratch.write(
        "Assets/Glsl.shader",
        "Shader \"Tests/Glsl\" { SubShader { Pass { GLSLPROGRAM\nvoid main() {}\nENDGLSL } } }",
    );
    scratch.write(
        "Assets/Missing.shader",
        "Shader \"Tests/Missing\" { SubShader { Pass { HLSLPROGRAM\n#pragma vertex vert\n#pragma multi_compile _ A\n#include \"Missing.hlsl\"\nENDHLSL } } }",
    );
    scratch.write(
        "Assets/UsePass.shader",
        "Shader \"Tests/UsePass\" { SubShader { UsePass \"Tests/Nowhere/MAIN\" } }",
    );
    scratch.write(
        "Assets/NewForm.shader",
        "Shader \"Tests/NewForm\" { SubShader { Pass { HLSLPROGRAM\n#pragma multi_compile_somethingnew _ B\nENDHLSL } } }",
    );
    scratch.write(
        "Assets/Empty.shader",
        "Shader \"Tests/Empty\" { SubShader { Pass { HLSLPROGRAM\n#pragma vertex vert\n#pragma fragment frag\nENDHLSL } } }",
    );
    let provider = Provider::new(inputs(
        &scratch,
        cg,
        scratch.0.join("no-install"),
        MEASURED,
        Arc::new(AtomicUsize::new(0)),
        Arc::new(AtomicBool::new(false)),
    ));
    for name in ["Tests/Glsl", "Tests/Missing", "Tests/NewForm"] {
        let answer = ask(&provider, name);
        assert_eq!(answer["found"], true, "{name}");
        assert_eq!(answer["subshaders_known"], true, "{name}");
        assert_eq!(answer["keywords_known"], false, "{name}");
        assert_eq!(answer["transient"], false, "{name}");
        assert_eq!(answer["keywords"], json!([]), "{name}: no partial list");
    }
    let answer = ask(&provider, "Tests/UsePass");
    assert_eq!(answer["subshaders_known"], false);
    assert_eq!(answer["keywords_known"], false);
    // The UsePass shader names no project shader and the built-in resources are missing: may
    // become known, so not kept.
    assert_eq!(answer["transient"], true);
    let answer = ask(&provider, "Tests/Empty");
    assert_eq!(answer["keywords_known"], true);
    assert_eq!(names(&answer), stereo_and(&[]));
    // No project shader has the name and the built-in resources cannot be read, so it may
    // still be a built-in one: not found, and asked again later.
    let answer = ask(&provider, "Tests/Nothing");
    assert_eq!(answer["found"], false);
    assert_eq!(answer["transient"], true);
}

/// A compute file: a missing file is a transient unknown and is answered once it exists; a
/// file without declarations has a known, empty space; a changed file is answered anew (no
/// answer is kept); an unresolved include is unknown.
#[test]
fn compute_answers_follow_the_file() {
    let scratch = Scratch::new("decl-compute");
    let cg = scratch.0.join("CGIncludes");
    scratch.write("CGIncludes/UnityCG.cginc", "// stand-in\n");
    let provider = Provider::new(inputs(
        &scratch,
        cg,
        scratch.0.join("no-install"),
        MEASURED,
        Arc::new(AtomicUsize::new(0)),
        Arc::new(AtomicBool::new(false)),
    ));
    let file = scratch.0.join("Assets/Kw.compute");
    let answer = ask_compute(&provider, &file);
    assert_eq!(answer["keywords_known"], false);
    assert_eq!(answer["transient"], true);

    scratch.write("Assets/Kw.compute", "#pragma kernel CSMain\n");
    let answer = ask_compute(&provider, &file);
    assert_eq!(answer["keywords_known"], true);
    assert_eq!(answer["keywords"], json!([]));

    scratch.write(
        "Assets/Kw.compute",
        "#pragma kernel CSMain\n#pragma kernel CSOther\n#pragma multi_compile _ CS_GLOBAL_A CS_GLOBAL_B\n#pragma multi_compile_local _ CS_LOCAL\n#pragma dynamic_branch CS_DYN\n#pragma multi_compile _ LIGHTMAP_ON\n",
    );
    let answer = ask_compute(&provider, &file);
    assert_eq!(answer["keywords_known"], true);
    // The canonical compute.space.keywordNames.
    assert_eq!(
        names(&answer),
        [
            "CS_LOCAL",
            "CS_GLOBAL_A",
            "CS_GLOBAL_B",
            "CS_DYN",
            "LIGHTMAP_ON"
        ]
    );
    assert_eq!(answer["keywords"][0]["local"], true);
    assert_eq!(answer["keywords"][3]["dynamic"], true);

    scratch.write(
        "Assets/Kw.compute",
        "#pragma kernel CSMain\n#include \"Missing.hlsl\"\n",
    );
    let answer = ask_compute(&provider, &file);
    assert_eq!(answer["keywords_known"], false);
    assert_eq!(answer["transient"], false);
    assert_eq!(answer["keywords"], json!([]));
}

/// Without the install's includes the built-in sources are asked for; while they are not
/// available the space is a transient unknown, and it becomes known when they are.
#[test]
fn an_unavailable_input_recovers() {
    let scratch = Scratch::new("decl-recover");
    scratch.write("sources/CGIncludes/UnityCG.cginc", "// stand-in\n");
    scratch.write(
        "Assets/Hlsl.shader",
        "Shader \"Tests/Hlsl\" { SubShader { Pass { HLSLPROGRAM\n#pragma multi_compile _ HLSL_A\n#pragma shader_feature_local HLSL_LOCAL\nENDHLSL } } }",
    );
    let calls = Arc::new(AtomicUsize::new(0));
    let available = Arc::new(AtomicBool::new(false));
    let provider = Provider::new(inputs(
        &scratch,
        scratch.0.join("no-includes"),
        scratch.0.join("no-install"),
        MEASURED,
        calls.clone(),
        available.clone(),
    ));
    let answer = ask(&provider, "Tests/Hlsl");
    assert_eq!(answer["keywords_known"], false);
    assert_eq!(answer["transient"], true);
    assert_eq!(answer["subshaders_known"], true);
    assert_eq!(calls.load(Ordering::Relaxed), 1);
    let again = ask(&provider, "Tests/Hlsl");
    assert_eq!(again["transient"], true);
    assert_eq!(
        calls.load(Ordering::Relaxed),
        1,
        "a failure backs off across questions"
    );

    available.store(true, Ordering::Relaxed);
    {
        let mut state = provider.state.lock().unwrap();
        let retry_at = state.sources_failed.as_ref().unwrap().0 + SOURCE_RETRY_DELAY;
        assert!(state.sources_at(&provider.inputs, retry_at).is_ok());
    }
    let answer = ask(&provider, "Tests/Hlsl");
    assert_eq!(answer["keywords_known"], true);
    assert_eq!(answer["transient"], false);
    // The canonical probe.Probe/KwHlsl.space.keywordNames and flags.
    assert_eq!(names(&answer), stereo_and(&["HLSL_A", "HLSL_LOCAL"]));
    assert_eq!(answer["keywords"][4]["local"], false);
    assert_eq!(answer["keywords"][5]["local"], true);
    let _ = ask(&provider, "Tests/Hlsl");
    assert_eq!(calls.load(Ordering::Relaxed), 2, "found once, then kept");
}

/// Questions from many threads at once get the same complete answers.
#[test]
fn concurrent_questions_get_whole_answers() {
    let scratch = Scratch::new("decl-threads");
    scratch.write("CGIncludes/UnityCG.cginc", "// stand-in\n");
    scratch.write(
        "Assets/Hlsl.shader",
        "Shader \"Tests/Hlsl\" { SubShader { Pass { HLSLPROGRAM\n#pragma multi_compile _ HLSL_A\nENDHLSL } } }",
    );
    let provider = Provider::new(inputs(
        &scratch,
        scratch.0.join("CGIncludes"),
        scratch.0.join("no-install"),
        MEASURED,
        Arc::new(AtomicUsize::new(0)),
        Arc::new(AtomicBool::new(false)),
    ));
    let first = provider.shader("Tests/Hlsl");
    std::thread::scope(|scope| {
        let handles: Vec<_> = (0..8)
            .map(|_| scope.spawn(|| provider.shader("Tests/Hlsl")))
            .collect();
        for h in handles {
            assert_eq!(h.join().unwrap(), first);
        }
    });
}

/// The probe's own shaders and compute file (read in place, never written) against the canonical
/// tests/projects/shaderkeywords answers, with the installed 6000.0.71f1's includes and built-in
/// resources (skipped without the install).
#[test]
fn probe_shaders_match_the_canonical_reference() {
    let Some(data) = unbound_project::install::installed_data("6000.0.71f1") else {
        eprintln!("no 6000.0.71f1 install: skipped");
        return;
    };
    if !data.join("CGIncludes/UnityCG.cginc").is_file() {
        eprintln!("no 6000.0.71f1 includes: skipped");
        return;
    }
    let probe = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects/shaderkeywords");
    let scratch = Scratch::new("decl-probe");
    let mut i = inputs(
        &scratch,
        data.join("CGIncludes"),
        data.clone(),
        MEASURED,
        Arc::new(AtomicUsize::new(0)),
        Arc::new(AtomicBool::new(false)),
    );
    i.project_roots = vec![probe.join("Assets")];
    i.settings.includes.project = probe.clone();
    let provider = Provider::new(i);
    let reference: std::collections::HashMap<String, Value> =
        std::fs::read_to_string(probe.join("Reference/unity-6000.0.71f1.jsonl"))
            .unwrap()
            .lines()
            .map(|l| {
                let v: Value = serde_json::from_str(l).unwrap();
                (v["k"].as_str().unwrap().to_string(), v["v"].clone())
            })
            .collect();
    for shader in [
        "Probe/KwForms",
        "Probe/KwPlain",
        "Probe/KwHlsl",
        "Probe/KwSurface",
        "Probe/KwFixed",
    ] {
        let answer = ask(&provider, shader);
        let key = |k: &str| &reference[&format!("probe.{shader}.{k}")];
        assert_eq!(answer["found"], true, "{shader}");
        assert_eq!(
            answer["keywords_known"], true,
            "{shader}: {}",
            answer["reason"]
        );
        assert_eq!(
            answer["subshaders_known"], true,
            "{shader}: {}",
            answer["reason"]
        );
        assert_eq!(
            json!(names(&answer)),
            *key("space.keywordNames"),
            "{shader}"
        );
        for (k, want) in answer["keywords"]
            .as_array()
            .unwrap()
            .iter()
            .zip(key("space.keywords").as_array().unwrap())
        {
            assert_eq!(
                k["local"].as_bool().unwrap(),
                !want["isOverridable"].as_bool().unwrap(),
                "{shader} {k}"
            );
            assert_eq!(k["dynamic"], want["isDynamic"], "{shader} {k}");
        }
        let subs = answer["subshaders"].as_array().unwrap();
        assert_eq!(json!(subs.len()), *key("subshaderCount"), "{shader}");
        let counts: Vec<String> = subs
            .iter()
            .map(|s| s["passes"].as_array().unwrap().len().to_string())
            .collect();
        assert_eq!(json!(counts), *key("passCountInSubshader"), "{shader}");
        // LODs from the probe sources (the Fallback Unlit/Color's as serialized); only
        // KwForms's first SubShader may limit its devices (its `#include_with_pragmas`).
        let (lods, restricted): (&[u64], &[bool]) = match shader {
            "Probe/KwForms" => (&[300, 0, 100][..], &[true, false, false][..]),
            "Probe/KwFixed" => (&[400, 200, 100][..], &[false; 3][..]),
            _ => (&[0][..], &[false][..]),
        };
        assert_eq!(
            subs.iter()
                .map(|s| s["lod"].as_u64().unwrap())
                .collect::<Vec<_>>(),
            lods,
            "{shader}"
        );
        assert_eq!(
            subs.iter()
                .map(|s| s["restricted"].as_bool().unwrap())
                .collect::<Vec<_>>(),
            restricted,
            "{shader}"
        );
        let tag = |tags: &Value, name: &str| {
            tags.as_array()
                .unwrap()
                .iter()
                .find(|t| t[0].as_str().unwrap().eq_ignore_ascii_case(name))
                .map(|t| t[1].as_str().unwrap().to_string())
        };
        let mut modes = Vec::new();
        let mut sub_tags = Vec::new();
        for (s, sub) in subs.iter().enumerate() {
            for t in [
                "RenderType",
                "Queue",
                "RenderPipeline",
                "ProbeTag",
                "IgnoreProjector",
                "PreviewType",
                "DisableBatching",
                "LightMode",
            ] {
                if let Some(v) = tag(&sub["tags"], t) {
                    sub_tags.push(format!("{s}:{t}={v}"));
                }
            }
            for (p, pass) in sub["passes"].as_array().unwrap().iter().enumerate() {
                modes.push(format!(
                    "{s}.{p}:{}",
                    tag(&pass["tags"], "LightMode").unwrap_or("-".into())
                ));
            }
        }
        assert_eq!(json!(sub_tags), *key("subshaderTags"), "{shader}");
        assert_eq!(json!(modes), *key("passLightModes"), "{shader}");
    }
    let compute = ask_compute(&provider, &probe.join("Assets/Resources/KwCompute.compute"));
    assert_eq!(
        json!(names(&compute)),
        reference["compute.space.keywordNames"]
    );
    // The canonical's undeclared names are not in the spaces.
    let forms = names(&ask(&provider, "Probe/KwForms"));
    for absent in ["D3D_ONLY", "PLAIN_INCLUDE_KW", "GLOBAL_B"] {
        assert!(!forms.iter().any(|n| n == absent), "{absent}");
    }
}

/// Every built-in shader whose source Unity publishes, read as a project shader: its keyword
/// space against the one Unity serialized for it (6000.0.71f1 installed, its built-in shader
/// sources in the global cache; skipped otherwise). Offline evidence for the source rules on
/// Unity's own shaders, not a proof for every project shader.
#[test]
fn builtin_sources_declare_the_keywords_unity_lists() {
    let Some(data) = unbound_project::install::installed_data("6000.0.71f1") else {
        eprintln!("skipped: no 6000.0.71f1 install");
        return;
    };
    let sources = unbound_project::cache_root().join("builtin-shaders/v1/6000.0.71f1");
    if !sources.join("CGIncludes/UnityCG.cginc").is_file() {
        eprintln!("skipped: no 6000.0.71f1 built-in shader sources");
        return;
    }
    let scratch = Scratch::new("decl-builtin-sources");
    let builtin = unbound_assets::builtin_shaders::declarations(&data, &scratch.0).unwrap();
    let mut i = inputs(
        &scratch,
        sources.join("CGIncludes"),
        data.clone(),
        MEASURED,
        Arc::new(AtomicUsize::new(0)),
        Arc::new(AtomicBool::new(false)),
    );
    i.project_roots = ["DefaultResourcesExtra", "DefaultResources"]
        .iter()
        .map(|d| sources.join(d))
        .collect();
    let provider = Provider::new(i);
    let all = ShaderIndex::scan(&provider.inputs.project_roots).names();
    let (mut same, mut differ, mut unknown) = (0, Vec::new(), Vec::new());
    for name in all {
        let Some(want) = builtin.get(&name) else {
            continue;
        };
        let answer = ask(&provider, &name);
        let got: Vec<(String, bool, bool)> = answer["keywords"]
            .as_array()
            .unwrap()
            .iter()
            .map(|k| {
                (
                    k["name"].as_str().unwrap().to_string(),
                    k["local"].as_bool().unwrap(),
                    k["dynamic"].as_bool().unwrap(),
                )
            })
            .collect();
        let want: Vec<(String, bool, bool)> = want
            .keywords
            .iter()
            .map(|k| (k.name.clone(), k.local, k.dynamic))
            .collect();
        if answer["keywords_known"] != true {
            assert!(
                got.is_empty(),
                "{name}: unknown space must not expose a partial list"
            );
            assert!(
                !answer["reason"].as_str().unwrap().is_empty(),
                "{name}: unknown requires a reason"
            );
            unknown.push(format!("{name}: {}", answer["reason"]));
        } else if got == want {
            same += 1;
        } else {
            differ.push(format!(
                "{name} ({})\n  unity   {want:?}\n  unbound {got:?}",
                answer["reason"]
            ));
        }
    }
    eprintln!(
        "{same} known exact, {} unknown, {} KNOWN WRONG\nunknown:\n{}\nknown wrong:\n{}",
        unknown.len(),
        differ.len(),
        unknown.join("\n"),
        differ.join("\n")
    );
    // Coverage includes every reported unsupported form, never counted as an exact match.
    // The five canonical project shaders above must still remain known and exact.
    assert!(same + unknown.len() > 100, "insufficient source coverage");
    assert!(same > 0, "no source space was known");
    assert!(differ.is_empty(), "{} differ", differ.len());
}

#[test]
fn an_unreadable_project_fallback_never_becomes_a_partial_known_answer() {
    let scratch = Scratch::new("decl-fallback-unreadable");
    scratch.write(
        "Assets/A.shader",
        r#"Shader "Tests/A" { SubShader { Pass { } } Fallback "Tests/B" }"#,
    );
    let b = scratch.write(
        "Assets/B.shader",
        r#"Shader "Tests/B" { SubShader { Pass { } } }"#,
    );
    let provider = Provider::new(inputs(
        &scratch,
        scratch.0.join("no-includes"),
        scratch.0.join("no-install"),
        MEASURED,
        Arc::new(AtomicUsize::new(0)),
        Arc::new(AtomicBool::new(false)),
    ));
    provider.state.lock().unwrap().project(&provider.inputs);
    std::fs::remove_file(&b).unwrap();
    let answer = ask(&provider, "Tests/A");
    assert_eq!(answer["subshaders_known"], false);
    assert_eq!(answer["subshaders"], json!([]));
    assert_eq!(answer["keywords_known"], false);
    assert_eq!(answer["keywords"], json!([]));
    assert_eq!(answer["transient"], true);
    scratch.write(
        "Assets/B.shader",
        r#"Shader "Tests/B" { SubShader { Pass { } } }"#,
    );
    let answer = ask(&provider, "Tests/A");
    assert_eq!(answer["subshaders_known"], true);
    assert_eq!(answer["subshaders"].as_array().unwrap().len(), 2);
}

#[test]
fn serialized_builtins_do_not_enable_unmeasured_release_rules() {
    let scratch = Scratch::new("decl-unmeasured-builtin");
    let data = scratch.0.join("install");
    scratch.write("install/Resources/unity_builtin_extra", "stand-in");
    scratch.write("install/Resources/unity default resources", "stand-in");
    let provider = Provider::new(inputs(
        &scratch,
        scratch.0.join("no-includes"),
        data.clone(),
        None,
        Arc::new(AtomicUsize::new(0)),
        Arc::new(AtomicBool::new(false)),
    ));
    let mut map = DeclarationMap::new();
    map.insert("Standard".into(), Builtin::default());
    let seen = stamp(&unbound_assets::builtin_shaders::declaration_sources(&data)).unwrap();
    provider.state.lock().unwrap().builtin = Some((Arc::new(map), seen));
    let answer = ask(&provider, "Standard");
    assert_eq!(answer["found"], true);
    assert_eq!(answer["keywords_known"], false);
    assert_eq!(answer["subshaders_known"], false);
    assert_eq!(answer["transient"], false);
    assert!(answer["reason"].as_str().unwrap().contains("not measured"));
}

#[test]
fn fallback_extensions_surface_options_and_fog_are_not_partial_known_spaces() {
    let scratch = Scratch::new("decl-unsupported-space");
    scratch.write("CGIncludes/UnityCG.cginc", "// stand-in\n");
    scratch.write(
        "Assets/A.shader",
        r#"Shader "Tests/A" { SubShader { Pass { } } Fallback "Tests/B" }"#,
    );
    scratch.write("Assets/B.shader", "Shader \"Tests/B\" { SubShader { Pass { HLSLPROGRAM\n#pragma multi_compile _ FALLBACK_ONLY\nENDHLSL } } }");
    scratch.write("Assets/Surface.shader", "Shader \"Tests/SurfaceOptions\" { SubShader { CGPROGRAM\n#pragma surface surf Lambert nolightmap\nENDCG } }");
    scratch.write(
        "Assets/Fog.shader",
        r#"Shader "Tests/Fog" { SubShader { Fog { Mode Off } Pass { Color (1,1,1,1) } } }"#,
    );
    let provider = Provider::new(inputs(
        &scratch,
        scratch.0.join("CGIncludes"),
        scratch.0.join("no-install"),
        MEASURED,
        Arc::new(AtomicUsize::new(0)),
        Arc::new(AtomicBool::new(false)),
    ));
    for name in ["Tests/A", "Tests/SurfaceOptions", "Tests/Fog"] {
        let answer = ask(&provider, name);
        assert_eq!(answer["keywords_known"], false, "{name}: {answer}");
        assert_eq!(answer["keywords"], json!([]), "{name}");
        assert!(!answer["reason"].as_str().unwrap().is_empty(), "{name}");
    }
    assert_eq!(
        ask(&provider, "Tests/SurfaceOptions")["subshaders_known"],
        false
    );
    assert_eq!(ask(&provider, "Tests/A")["subshaders_known"], true);
}

#[test]
fn lit_fixed_function_spaces_and_their_fallbacks_are_unknown() {
    let scratch = Scratch::new("decl-fixed-lighting");
    scratch.write("CGIncludes/UnityCG.cginc", "// stand-in\n");
    scratch.write(
        "Assets/Lit.shader",
        r#"Shader "Tests/Lit" { SubShader { Lighting On Pass { Color (1,1,1,1) } } }"#,
    );
    scratch.write("Assets/Fallback.shader", r#"Shader "Tests/LitFallback" { SubShader { Pass { Color (1,1,1,1) } } Fallback "Tests/Lit" }"#);
    scratch.write(
        "Assets/Unlit.shader",
        r#"Shader "Tests/Unlit" { SubShader { Lighting Off Pass { Color (1,1,1,1) } } }"#,
    );
    scratch.write("Assets/Program.shader", "Shader \"Tests/LitProgram\" { SubShader { Lighting On Pass { HLSLPROGRAM\n#pragma vertex vert\n#pragma fragment frag\nENDHLSL } } }");
    let provider = Provider::new(inputs(
        &scratch,
        scratch.0.join("CGIncludes"),
        scratch.0.join("no-install"),
        MEASURED,
        Arc::new(AtomicUsize::new(0)),
        Arc::new(AtomicBool::new(false)),
    ));
    for name in ["Tests/Lit", "Tests/LitFallback"] {
        let answer = ask(&provider, name);
        assert_eq!(answer["keywords_known"], false, "{name}: {answer}");
        assert_eq!(answer["keywords"], json!([]));
        assert!(answer["reason"].as_str().unwrap().contains("Lighting On"));
    }
    for name in ["Tests/Unlit", "Tests/LitProgram"] {
        assert_eq!(ask(&provider, name)["keywords_known"], true, "{name}");
    }
}

/// A built-in shader's `UsePass` passes the reader resolved are ordinary passes with the named
/// pass's name and tags (canonical builtin.Nature/Terrain/*.passLightModes `Picking`). One it
/// could not resolve makes the SubShaders unknown, also through another shader's Fallback, never
/// a pass without tags; the keyword space stays known.
#[test]
fn unresolved_builtin_use_passes_are_unknown_not_tagless() {
    use unbound_assets::builtin_shaders::{DeclaredPass, DeclaredSubShader};
    let scratch = Scratch::new("decl-builtin-use-pass");
    let data = scratch.0.join("install");
    scratch.write("install/Resources/unity_builtin_extra", "stand-in");
    scratch.write("install/Resources/unity default resources", "stand-in");
    scratch.write("CGIncludes/UnityCG.cginc", "// stand-in\n");
    scratch.write(
        "Assets/FallsBack.shader",
        r#"Shader "Tests/FallsBack" { SubShader { Pass { } } Fallback "Tests/Unresolved" }"#,
    );
    let provider = Provider::new(inputs(
        &scratch,
        scratch.0.join("CGIncludes"),
        data.clone(),
        MEASURED,
        Arc::new(AtomicUsize::new(0)),
        Arc::new(AtomicBool::new(false)),
    ));
    let pass = |name: &str, light_mode: &str, use_pass: &str| DeclaredPass {
        name: name.into(),
        tags: vec![("LIGHTMODE".into(), light_mode.into())],
        use_pass: use_pass.into(),
    };
    let shader = |passes: Vec<DeclaredPass>| Builtin {
        subshaders: vec![DeclaredSubShader {
            tags: Vec::new(),
            passes,
            lod: 0,
        }],
        ..Default::default()
    };
    let mut map = DeclarationMap::new();
    map.insert(
        "Tests/Resolved".into(),
        shader(vec![
            pass("FORWARD", "FORWARDBASE", ""),
            pass("Picking", "Picking", ""),
        ]),
    );
    map.insert(
        "Tests/Unresolved".into(),
        shader(vec![
            pass("FORWARD", "FORWARDBASE", ""),
            DeclaredPass {
                use_pass: "Hidden/Nowhere/PASS".into(),
                ..Default::default()
            },
        ]),
    );
    let seen = stamp(&unbound_assets::builtin_shaders::declaration_sources(&data)).unwrap();
    provider.state.lock().unwrap().builtin = Some((Arc::new(map), seen));

    let resolved = ask(&provider, "Tests/Resolved");
    assert_eq!(resolved["subshaders_known"], true, "{resolved}");
    assert_eq!(
        resolved["subshaders"][0]["passes"][1],
        json!({ "name": "Picking", "tags": [["LIGHTMODE", "Picking"]] })
    );

    let unresolved = ask(&provider, "Tests/Unresolved");
    assert_eq!(unresolved["found"], true);
    assert_eq!(unresolved["subshaders_known"], false, "{unresolved}");
    assert_eq!(unresolved["subshaders"], json!([]));
    assert_eq!(unresolved["keywords_known"], true);
    assert_eq!(unresolved["transient"], false);
    assert!(
        unresolved["reason"]
            .as_str()
            .unwrap()
            .contains("UsePass \"Hidden/Nowhere/PASS\" names no pass"),
        "{unresolved}"
    );

    let falls_back = ask(&provider, "Tests/FallsBack");
    assert_eq!(falls_back["subshaders_known"], false, "{falls_back}");
    assert!(
        falls_back["reason"]
            .as_str()
            .unwrap()
            .contains("Hidden/Nowhere/PASS"),
        "{falls_back}"
    );
}

/// Pragmas that may limit the devices a program runs on; other pragmas and words do not.
#[test]
fn renderer_limits_are_read_from_pragmas() {
    for limited in [
        "#pragma only_renderers d3d11",
        "  #pragma exclude_renderers gles",
        "# pragma\texclude_renderers nomrt",
        "#include_with_pragmas \"Pragmas.cginc\"",
    ] {
        assert!(limits_renderers(&format!("a\n{limited}\nb")), "{limited}");
    }
    for plain in [
        "#pragma vertex vert",
        "#pragmaonly_renderers d3d11",
        "// only_renderers d3d11",
        "#pragma multi_compile _ ONLY_RENDERERS",
        "#include \"UnityCG.cginc\"",
        "",
    ] {
        assert!(!limits_renderers(plain), "{plain}");
    }
}

/// Every SubShader carries its `LOD` (0 without one) and whether a pass may limit its devices
/// (a limiting pragma, GLSL, a `UsePass` of such a project pass), in the merged order: own,
/// then the Fallback chain's, a built-in Fallback's LOD from its serialized form and never
/// restricted. Shader.passCount picks by them (managed ShaderPassCountTests).
#[test]
fn subshaders_carry_their_lod_and_device_limits() {
    use unbound_assets::builtin_shaders::{DeclaredPass, DeclaredSubShader};
    let scratch = Scratch::new("decl-lod");
    let data = scratch.0.join("install");
    scratch.write("install/Resources/unity_builtin_extra", "stand-in");
    scratch.write("install/Resources/unity default resources", "stand-in");
    scratch.write("CGIncludes/UnityCG.cginc", "// stand-in\n");
    scratch.write(
        "Assets/Lods.shader",
        "Shader \"Tests/Lods\" {\n\
         SubShader { Tags { \"RenderPipeline\"=\"UniversalPipeline\" } LOD 400 Pass { } }\n\
         SubShader { LOD 200 Pass { Name \"LIMITED\" CGPROGRAM\n#pragma only_renderers d3d11\n#pragma vertex vert\nENDCG } }\n\
         SubShader { Pass { } }\n\
         SubShader { LOD 100 Pass { GLSLPROGRAM\nvoid main() {}\nENDGLSL } }\n\
         SubShader { LOD 50 UsePass \"Tests/Lods/LIMITED\" }\n\
         Fallback \"Tests/Builtin\" }",
    );
    let provider = Provider::new(inputs(
        &scratch,
        scratch.0.join("CGIncludes"),
        data.clone(),
        MEASURED,
        Arc::new(AtomicUsize::new(0)),
        Arc::new(AtomicBool::new(false)),
    ));
    let mut map = DeclarationMap::new();
    map.insert(
        "Tests/Builtin".into(),
        Builtin {
            subshaders: vec![DeclaredSubShader {
                tags: Vec::new(),
                passes: vec![DeclaredPass {
                    name: "FORWARD".into(),
                    tags: vec![("LIGHTMODE".into(), "FORWARDBASE".into())],
                    use_pass: String::new(),
                }],
                lod: 150,
            }],
            ..Default::default()
        },
    );
    let seen = stamp(&unbound_assets::builtin_shaders::declaration_sources(&data)).unwrap();
    provider.state.lock().unwrap().builtin = Some((Arc::new(map), seen));

    let answer = ask(&provider, "Tests/Lods");
    assert_eq!(answer["subshaders_known"], true, "{answer}");
    let subs = answer["subshaders"].as_array().unwrap();
    let field = |name: &str| subs.iter().map(|s| s[name].clone()).collect::<Vec<_>>();
    assert_eq!(field("lod"), [400, 200, 0, 100, 50, 150].map(|l| json!(l)));
    assert_eq!(
        field("restricted"),
        [false, true, false, true, true, false].map(|r| json!(r))
    );
    // The passes are unchanged: one each.
    assert!(
        subs.iter()
            .all(|s| s["passes"].as_array().unwrap().len() == 1)
    );
}
