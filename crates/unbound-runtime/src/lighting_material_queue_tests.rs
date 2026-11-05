//! The stored render queue of built-in Materials (`builtin-material-queue`). The installed-file
//! tests read the 6000.0.71f1 install's resources (skipped without it): their expected values
//! are the bytes that install stores (a Native asset-state observation, not a measured API
//! readout). The other tests are mechanism tests over constructed values.
use super::*;
use unbound_yaml::Map;

const DEFAULT_RESOURCES: &str = "0000000000000000e000000000000000";

fn installed() -> Option<std::path::PathBuf> {
    let data = unbound_project::install::installed_data("6000.0.71f1")?;
    let present = data.join("Resources/unity_builtin_extra").is_file()
        && data.join("Resources/unity default resources").is_file();
    present.then_some(data)
}

fn queue(data: &Path, guid: &str, file_id: i64) -> Result<Json> {
    builtin_material_queue(&AssetDb::default(), data, guid, file_id)
}

/// Every class-21 record of both installed files, with the value it stores: an override (3000,
/// 2000) and no override (-1) alike, each named as stored.
#[test]
fn installed_builtin_materials_answer_their_stored_raw_queue() {
    let Some(data) = installed() else {
        eprintln!("no 6000.0.71f1 install: skipped");
        return;
    };
    let expected: &[(&str, i64, &str, i32)] = &[
        (BUILTIN_EXTRA, 10301, "Default-Particle", -1),
        (BUILTIN_EXTRA, 10302, "Default-Diffuse", -1),
        (BUILTIN_EXTRA, 10303, "Default-Material", -1),
        (BUILTIN_EXTRA, 10304, "Default-Skybox", -1),
        (BUILTIN_EXTRA, 10306, "Default-Line", -1),
        (BUILTIN_EXTRA, 10308, "Default-ParticleSystem", 3000),
        (BUILTIN_EXTRA, 10650, "Default-Terrain-Diffuse", -1),
        (BUILTIN_EXTRA, 10651, "Default-Terrain-Specular", -1),
        (BUILTIN_EXTRA, 10652, "Default-Terrain-Standard", -1),
        (BUILTIN_EXTRA, 10754, "Sprites-Default", -1),
        (BUILTIN_EXTRA, 10758, "Sprites-Mask", -1),
        (BUILTIN_EXTRA, 15302, "SpatialMappingOcclusion", 2000),
        (BUILTIN_EXTRA, 15303, "SpatialMappingWireframe", 2000),
        (DEFAULT_RESOURCES, 10100, "Font Material", -1),
        (
            DEFAULT_RESOURCES,
            10756,
            "FrameDebuggerRenderTargetDisplay",
            -1,
        ),
    ];
    for &(guid, file_id, name, raw) in expected {
        let answer = queue(&data, guid, file_id).unwrap();
        assert_eq!(
            answer,
            json!({ "classId": 21, "name": name, "customRenderQueue": raw }),
            "{guid} {file_id}"
        );
    }
}

/// The built-in guid is matched as references write it, in either case.
#[test]
fn the_builtin_guid_is_matched_without_case() {
    let Some(data) = installed() else {
        eprintln!("no 6000.0.71f1 install: skipped");
        return;
    };
    let answer = queue(&data, &BUILTIN_EXTRA.to_uppercase(), 10308).unwrap();
    assert_eq!(answer["customRenderQueue"], 3000);
}

/// Another class at the id (the Particles/Standard Unlit shader, 211) is refused by class.
#[test]
fn an_object_of_another_class_is_refused() {
    let Some(data) = installed() else {
        eprintln!("no 6000.0.71f1 install: skipped");
        return;
    };
    let error = format!("{:#}", queue(&data, BUILTIN_EXTRA, 211).unwrap_err());
    assert!(error.contains("built-in Material 211"), "{error}");
    assert!(
        error.contains("is class 48, not a Material (21)"),
        "{error}"
    );
}

/// An id the file does not hold is an error naming it, not a queue of -1.
#[test]
fn a_missing_object_is_an_error_naming_it() {
    let Some(data) = installed() else {
        eprintln!("no 6000.0.71f1 install: skipped");
        return;
    };
    let error = format!(
        "{:#}",
        queue(&data, BUILTIN_EXTRA, 987_654_321).unwrap_err()
    );
    assert!(error.contains("built-in Material 987654321"), "{error}");
    assert!(error.contains("no object 987654321"), "{error}");
}

/// Only the two built-in resource files are read; a project guid or the Editor's resources are
/// not, whatever is installed.
#[test]
fn only_builtin_resource_files_are_read() {
    let data = Path::new("/nonexistent-unity-data");
    for guid in [
        "0000000000000000d000000000000000",
        "0123456789abcdef0123456789abcdef",
    ] {
        let error = format!("{:#}", queue(data, guid, 10308).unwrap_err());
        assert!(error.contains("is not a built-in resource file"), "{error}");
    }
}

/// A missing resource file is an error naming the file.
#[test]
fn a_missing_resource_file_is_an_error_naming_it() {
    let data = Path::new("/nonexistent-unity-data");
    let error = format!("{:#}", queue(data, BUILTIN_EXTRA, 10308).unwrap_err());
    assert!(
        error.contains("opening /nonexistent-unity-data/Resources/unity_builtin_extra"),
        "{error}"
    );
}

fn material(field: Option<Value>) -> Value {
    let mut map = Map::default();
    map.0
        .push(("m_Name".into(), Value::Scalar("Stand-in".into())));
    if let Some(field) = field {
        map.0.push(("m_CustomRenderQueue".into(), field));
    }
    Value::Map(map)
}

/// -1 (no override) and an override are both kept as stored; so is a value outside [0..5000],
/// which the documentation does not describe for stored data (passed on, not clamped).
#[test]
fn stored_values_are_kept_exactly() {
    for (text, raw) in [
        ("-1", -1),
        ("0", 0),
        ("3000", 3000),
        ("5001", 5001),
        ("-2", -2),
    ] {
        let value = material(Some(Value::Scalar(text.into())));
        assert_eq!(stored_custom_render_queue(&value).unwrap(), raw, "{text}");
    }
}

/// A Material without the field, or with a value that is no 32-bit integer, is an error.
#[test]
fn a_missing_or_malformed_field_is_an_error() {
    let missing = format!(
        "{:#}",
        stored_custom_render_queue(&material(None)).unwrap_err()
    );
    assert!(missing.contains("has no m_CustomRenderQueue"), "{missing}");
    for text in ["", "3000.5", "Transparent", "4294967295"] {
        let value = material(Some(Value::Scalar(text.into())));
        let error = format!("{:#}", stored_custom_render_queue(&value).unwrap_err());
        assert!(error.contains("is not a 32-bit integer"), "{text}: {error}");
    }
    let nested = material(Some(Value::Map(Map::default())));
    let error = format!("{:#}", stored_custom_render_queue(&nested).unwrap_err());
    assert!(error.contains("is not a number"), "{error}");
}
