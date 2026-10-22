use super::*;

const SCENE: &str = r#"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &100
GameObject:
  m_ObjectHideFlags: 0
  serializedVersion: 6
  m_Component:
  - component: {fileID: 101}
  - component: {fileID: 102}
  m_Layer: 0
  m_Name: Player Root
  m_IsActive: 1
--- !u!4 &101
Transform:
  m_GameObject: {fileID: 100}
  m_LocalRotation: {x: 0, y: 0.7071068, z: 0, w: 0.7071068}
  m_LocalPosition: {x: 1.5, y: -2, z: 3e-05}
  m_Children: []
  m_Father: {fileID: 0}
--- !u!114 &102
MonoBehaviour:
  m_GameObject: {fileID: 100}
  m_Script: {fileID: 11500000, guid: 0123456789abcdef0123456789abcdef,
    type: 3}
  speed: 4.5
  title: 'It''s a long title that
    wraps onto the next line'
  quoted: "tab\there А"
  plain: a plain scalar that
    continues
  items:
  - name: first
    value: 1
  - name: second
    value: 2
  nested:
    list:
    - 1
    - 2
    empty: {}
  matrix:
  - - 1
    - 2
  emptyValue:
  last: end
--- !u!1001 &200 stripped
PrefabInstance:
  m_ObjectHideFlags: 0
"#;

#[test]
fn reads_a_scene() {
    let docs = parse_documents(SCENE).unwrap();
    assert_eq!(docs.len(), 4);
    let go = &docs[0];
    assert_eq!(
        (go.class_id, go.file_id, go.type_name.as_str()),
        (1, 100, "GameObject")
    );
    assert_eq!(go.body.get("m_Name").unwrap().as_str(), Some("Player Root"));
    let components = go.body.get("m_Component").unwrap().as_seq().unwrap();
    assert_eq!(components.len(), 2);
    assert_eq!(
        components[1]
            .get("component")
            .unwrap()
            .as_ref()
            .unwrap()
            .file_id,
        102
    );

    let t = &docs[1].body;
    assert_eq!(t.path("m_LocalPosition.y").unwrap().as_f64(), Some(-2.0));
    assert_eq!(t.path("m_LocalPosition.z").unwrap().as_f64(), Some(3e-5));
    assert_eq!(t.get("m_Children").unwrap().as_seq().unwrap().len(), 0);

    let mb = &docs[2].body;
    let script = mb.get("m_Script").unwrap().as_ref().unwrap();
    assert_eq!(script.file_id, 11500000);
    assert_eq!(script.guid.to_string(), "0123456789abcdef0123456789abcdef");
    assert_eq!(script.kind, 3);
    assert_eq!(
        mb.get("title").unwrap().as_str(),
        Some("It's a long title that wraps onto the next line")
    );
    assert_eq!(
        mb.get("quoted").unwrap().as_str(),
        Some("tab\there \u{410}")
    );
    assert_eq!(
        mb.get("plain").unwrap().as_str(),
        Some("a plain scalar that continues")
    );
    let items = mb.get("items").unwrap().as_seq().unwrap();
    assert_eq!(items[1].get("name").unwrap().as_str(), Some("second"));
    assert_eq!(items[1].get("value").unwrap().as_i64(), Some(2));
    assert_eq!(mb.path("nested.list").unwrap().as_seq().unwrap().len(), 2);
    assert_eq!(mb.path("nested.empty").unwrap().as_map().unwrap().len(), 0);
    let matrix = mb.get("matrix").unwrap().as_seq().unwrap();
    assert_eq!(matrix[0].as_seq().unwrap().len(), 2);
    assert_eq!(mb.get("emptyValue"), Some(&Value::Null));
    assert_eq!(mb.get("last").unwrap().as_str(), Some("end"));

    assert!(docs[3].stripped);
    assert_eq!(docs[3].class_id, 1001);
}

#[test]
fn reads_a_meta_file() {
    let meta = "fileFormatVersion: 2\nguid: 5f1f4ea6a1d7a7a4bb3f0f1d3b4a1c2e\nTextureImporter:\n  internalIDToNameTable: []\n  spriteSheet:\n    sprites: []\n  spritePixelsToUnits: 100\n  userData: \n  assetBundleName: \n";
    let value = parse_value(meta).unwrap();
    assert_eq!(
        value.get("guid").unwrap().as_guid().unwrap().to_string(),
        "5f1f4ea6a1d7a7a4bb3f0f1d3b4a1c2e"
    );
    assert_eq!(
        value
            .path("TextureImporter.spritePixelsToUnits")
            .unwrap()
            .as_i64(),
        Some(100)
    );
    assert_eq!(value.path("TextureImporter.userData"), Some(&Value::Null));
}

#[test]
fn reads_multiline_double_quoted_and_blank_lines() {
    let text = "a: \"first\n  second\n\n  third\"\nb: 'x\n\n  y'\nc: |\n  line1\n  line2\nd: 1\n";
    let value = parse_value(text).unwrap();
    assert_eq!(
        value.get("a").unwrap().as_str(),
        Some("first second\nthird")
    );
    assert_eq!(value.get("b").unwrap().as_str(), Some("x\ny"));
    assert_eq!(value.get("c").unwrap().as_str(), Some("line1\nline2\n"));
    assert_eq!(value.get("d").unwrap().as_i64(), Some(1));
}
