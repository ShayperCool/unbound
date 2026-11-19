use super::*;
use std::{collections::HashSet, sync::Arc};
use unbound_yaml::{Value, parse_documents};

struct Fixture(HashMap<Guid, Vec<Document>>);

impl PrefabSource for Fixture {
    fn prefab(&self, guid: Guid, stack: &mut Vec<Guid>) -> anyhow::Result<Arc<Vec<Document>>> {
        let docs = self
            .0
            .get(&guid)
            .ok_or_else(|| MissingPrefab(format!("no asset has guid {guid}")))?;
        stack.push(guid);
        let out = flatten(docs, self, stack)?;
        stack.pop();
        // As DocumentLoader: a prefab whose root is a missing prefab's instance does not load.
        if root_is_missing_prefab(&out) {
            return Err(MissingPrefab(format!("the root of {guid} is missing")).into());
        }
        Ok(Arc::new(out))
    }
}

const PREFAB: &str = "%YAML 1.1
--- !u!1 &100
GameObject:
  m_Component:
  - component: {fileID: 101}
  - component: {fileID: 102}
  m_Name: Enemy
  m_IsActive: 1
--- !u!4 &101
Transform:
  m_GameObject: {fileID: 100}
  m_LocalPosition: {x: 0, y: 0, z: 0}
  m_Children:
  - {fileID: 201}
  m_Father: {fileID: 0}
--- !u!114 &102
MonoBehaviour:
  m_GameObject: {fileID: 100}
  hp: 10
  target: {fileID: 201}
  items:
  - 1
--- !u!1 &200
GameObject:
  m_Component:
  - component: {fileID: 201}
  m_Name: Child
--- !u!4 &201
Transform:
  m_GameObject: {fileID: 200}
  m_Children: []
  m_Father: {fileID: 101}
";

const SCENE: &str = "%YAML 1.1
--- !u!1 &1
GameObject:
  m_Component:
  - component: {fileID: 2}
  m_Name: World
--- !u!4 &2
Transform:
  m_GameObject: {fileID: 1}
  m_Children:
  - {fileID: 9001}
  m_Father: {fileID: 0}
--- !u!1001 &50
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 2}
    m_Modifications:
    - target: {fileID: 100, guid: 0000000000000000000000000000abcd, type: 3}
      propertyPath: m_Name
      value: Boss
      objectReference: {fileID: 0}
    - target: {fileID: 102, guid: 0000000000000000000000000000abcd, type: 3}
      propertyPath: hp
      value: 99
      objectReference: {fileID: 0}
    - target: {fileID: 102, guid: 0000000000000000000000000000abcd, type: 3}
      propertyPath: items.Array.size
      value: 2
      objectReference: {fileID: 0}
    - target: {fileID: 102, guid: 0000000000000000000000000000abcd, type: 3}
      propertyPath: items.Array.data[1]
      value: 7
      objectReference: {fileID: 0}
    m_RemovedComponents: []
    m_AddedComponents:
    - targetCorrespondingSourceObject: {fileID: 200, guid: 0000000000000000000000000000abcd, type: 3}
      insertIndex: -1
      addedObject: {fileID: 300}
  m_SourcePrefab: {fileID: 100100000, guid: 0000000000000000000000000000abcd, type: 3}
--- !u!4 &9001 stripped
Transform:
  m_CorrespondingSourceObject: {fileID: 101, guid: 0000000000000000000000000000abcd, type: 3}
  m_PrefabInstance: {fileID: 50}
--- !u!114 &300
MonoBehaviour:
  m_GameObject: {fileID: 7777}
  extra: 1
";

#[test]
fn expands_a_prefab_instance() {
    let guid = Guid::parse("0000000000000000000000000000abcd").unwrap();
    let fixture = Fixture([(guid, parse_documents(PREFAB).unwrap())].into());
    let scene = parse_documents(SCENE).unwrap();
    let docs = flatten(&scene, &fixture, &mut Vec::new()).unwrap();
    let by_id = |id: i64| docs.iter().find(|d| d.file_id == id).unwrap();

    // The stripped root transform keeps its id; other objects get instance ^ source.
    let root_transform = by_id(9001);
    assert_eq!(
        root_transform
            .body
            .path("m_Father.fileID")
            .unwrap()
            .as_i64(),
        Some(2)
    );
    let go = by_id(50 ^ 100);
    assert_eq!(go.body.get("m_Name").unwrap().as_str(), Some("Boss"));
    let behaviour = by_id(50 ^ 102);
    assert_eq!(behaviour.body.get("hp").unwrap().as_i64(), Some(99));
    let items = behaviour.body.get("items").unwrap().as_seq().unwrap();
    assert_eq!(items.len(), 2);
    assert_eq!(items[1].as_i64(), Some(7));
    // References inside the prefab follow its objects.
    assert_eq!(
        behaviour.body.path("target.fileID").unwrap().as_i64(),
        Some(50 ^ 201)
    );
    let child = by_id(50 ^ 201);
    assert_eq!(
        child.body.path("m_Father.fileID").unwrap().as_i64(),
        Some(9001)
    );
    // The added component is listed on its GameObject.
    let child_go = by_id(50 ^ 200);
    let components: Vec<i64> = child_go
        .body
        .get("m_Component")
        .unwrap()
        .as_seq()
        .unwrap()
        .iter()
        .map(|c| c.path("component.fileID").unwrap().as_i64().unwrap())
        .collect();
    assert!(components.contains(&300));
    // The world lists the instance root once.
    let world = by_id(2);
    let children = world.body.get("m_Children").unwrap().as_seq().unwrap();
    assert_eq!(children.len(), 1);
    assert!(docs.iter().all(|d| !d.stripped));
    let _ = Value::Null;
}

#[test]
fn instance_objects_keep_their_source_and_instance() {
    let guid = Guid::parse("0000000000000000000000000000abcd").unwrap();
    let fixture = Fixture([(guid, parse_documents(PREFAB).unwrap())].into());
    let scene = parse_documents(SCENE).unwrap();
    let docs = flatten(&scene, &fixture, &mut Vec::new()).unwrap();
    let by_id = |id: i64| docs.iter().find(|d| d.file_id == id).unwrap();
    for (object, source) in [
        (50 ^ 100, 100),
        (50 ^ 102, 102),
        (50 ^ 201, 201),
        (9001, 101),
    ] {
        let body = &by_id(object).body;
        assert_eq!(
            body.path("m_CorrespondingSourceObject.fileID")
                .unwrap()
                .as_i64(),
            Some(source)
        );
        assert_eq!(
            body.path("m_CorrespondingSourceObject.guid")
                .unwrap()
                .as_guid(),
            Some(guid)
        );
        assert_eq!(
            body.path("m_PrefabInstance.fileID").unwrap().as_i64(),
            Some(50)
        );
    }
    // The file's own PrefabInstance stays; the object the file adds is not part of it.
    assert_eq!(by_id(50).class_id, 1001);
    assert!(by_id(300).body.get("m_PrefabInstance").is_none());
}

/// A prefab nesting PREFAB: its own PrefabInstance stays in its documents, while an instance of it
/// in a scene links every object to the nesting prefab's objects under the scene's instance only.
#[test]
fn nested_instances_link_to_the_nearest_source() {
    let inner = Guid::parse("0000000000000000000000000000abcd").unwrap();
    let outer = Guid::parse("0000000000000000000000000000beef").unwrap();
    let outer_prefab = "%YAML 1.1
--- !u!1001 &60
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications: []
  m_SourcePrefab: {fileID: 100100000, guid: 0000000000000000000000000000abcd, type: 3}
";
    let scene = "%YAML 1.1
--- !u!1001 &70
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications: []
  m_SourcePrefab: {fileID: 100100000, guid: 0000000000000000000000000000beef, type: 3}
";
    let fixture = Fixture(
        [
            (inner, parse_documents(PREFAB).unwrap()),
            (outer, parse_documents(outer_prefab).unwrap()),
        ]
        .into(),
    );
    let asset = fixture.prefab(outer, &mut Vec::new()).unwrap();
    let nested = asset.iter().find(|d| d.file_id == 60 ^ 100).unwrap();
    assert_eq!(
        nested
            .body
            .path("m_CorrespondingSourceObject.guid")
            .unwrap()
            .as_guid(),
        Some(inner)
    );
    assert_eq!(
        nested
            .body
            .path("m_PrefabInstance.fileID")
            .unwrap()
            .as_i64(),
        Some(60)
    );
    assert!(asset.iter().any(|d| d.file_id == 60 && d.class_id == 1001));

    let docs = flatten(&parse_documents(scene).unwrap(), &fixture, &mut Vec::new()).unwrap();
    let object = docs.iter().find(|d| d.file_id == 70 ^ (60 ^ 100)).unwrap();
    assert_eq!(
        object
            .body
            .path("m_CorrespondingSourceObject.fileID")
            .unwrap()
            .as_i64(),
        Some(60 ^ 100)
    );
    assert_eq!(
        object
            .body
            .path("m_CorrespondingSourceObject.guid")
            .unwrap()
            .as_guid(),
        Some(outer)
    );
    assert_eq!(
        object
            .body
            .path("m_PrefabInstance.fileID")
            .unwrap()
            .as_i64(),
        Some(70)
    );
    let instances: Vec<i64> = docs
        .iter()
        .filter(|d| d.class_id == 1001)
        .map(|d| d.file_id)
        .collect();
    assert_eq!(instances, vec![70]);
}

#[test]
fn encodes_documents() {
    let docs = parse_documents(PREFAB).unwrap();
    let bytes = encode_documents(&docs);
    assert_eq!(&bytes[..4], b"UYB1");
    assert_eq!(
        u32::from_le_bytes(bytes[4..8].try_into().unwrap()),
        docs.len() as u32
    );
}

/// An imported prefab (a ScriptedImporter's) names its import's meshes by the asset's own guid:
/// those stay references to the asset, while its own objects follow the instance.
#[test]
fn references_to_an_imports_assets_stay_external() {
    let guid = Guid::parse("0000000000000000000000000000beef").unwrap();
    let prefab = "%YAML 1.1
--- !u!1 &100
GameObject:
  m_Component:
  - component: {fileID: 101}
  - component: {fileID: 102}
  m_Name: Tree
--- !u!4 &101
Transform:
  m_GameObject: {fileID: 100}
  m_Children: []
  m_Father: {fileID: 0}
--- !u!33 &102
MeshFilter:
  m_GameObject: {fileID: 100, guid: 0000000000000000000000000000beef, type: 3}
  m_Mesh: {fileID: 4443394430103177256, guid: 0000000000000000000000000000beef, type: 3}
";
    let scene = "%YAML 1.1
--- !u!1001 &60
PrefabInstance:
  m_Modification:
    m_TransformParent: {fileID: 0}
    m_Modifications: []
    m_RemovedComponents: []
  m_SourcePrefab: {fileID: 100100000, guid: 0000000000000000000000000000beef, type: 3}
";
    let fixture = Fixture([(guid, parse_documents(prefab).unwrap())].into());
    let docs = flatten(&parse_documents(scene).unwrap(), &fixture, &mut Vec::new()).unwrap();
    let filter = docs.iter().find(|d| d.file_id == 60 ^ 102).unwrap();
    // The GameObject is one of the prefab's documents: it follows the instance.
    assert_eq!(
        filter.body.path("m_GameObject.fileID").unwrap().as_i64(),
        Some(60 ^ 100)
    );
    assert!(filter.body.path("m_GameObject.guid").is_none());
    // The mesh is not: it stays the asset's.
    assert_eq!(
        filter.body.path("m_Mesh.fileID").unwrap().as_i64(),
        Some(4443394430103177256)
    );
    assert_eq!(
        filter.body.path("m_Mesh.guid").unwrap().as_guid(),
        Some(guid)
    );
}

const MISSING: &str = "0000000000000000000000000000dead";

/// A scene with an instance (60) of a missing prefab: its stripped objects (root GameObject 600
/// and Transform 601, a child's GameObject 650 and Transform 640, two BoxColliders, a script, a
/// RectTransform), an added child of the root and of the child, components added to the root and
/// the child, and a scene object referencing them.
fn missing_scene(root_modifications: bool) -> String {
    let mods = if root_modifications {
        format!(
            "    - target: {{fileID: 11, guid: {MISSING}, type: 3}}
      propertyPath: m_LocalPosition.x
      value: 2
      objectReference: {{fileID: 0}}
    - target: {{fileID: 10, guid: {MISSING}, type: 3}}
      propertyPath: m_Name
      value: Car
      objectReference: {{fileID: 0}}
    - target: {{fileID: 11, guid: {MISSING}, type: 3}}
      propertyPath: m_LocalRotation.w
      value: 1
      objectReference: {{fileID: 0}}
"
        )
    } else {
        String::new()
    };
    let stripped = |class: i32, name: &str, id: i64, source: i64, extra: &str| {
        format!(
            "--- !u!{class} &{id} stripped\n{name}:\n  m_CorrespondingSourceObject: {{fileID: {source}, guid: {MISSING}, type: 3}}\n  m_PrefabInstance: {{fileID: 60}}\n{extra}"
        )
    };
    let mut scene = format!(
        "%YAML 1.1
--- !u!1 &1
GameObject:
  m_Component:
  - component: {{fileID: 2}}
  - component: {{fileID: 3}}
  m_Name: Holder
--- !u!4 &2
Transform:
  m_GameObject: {{fileID: 1}}
  m_Children: []
  m_Father: {{fileID: 0}}
--- !u!114 &3
MonoBehaviour:
  m_GameObject: {{fileID: 1}}
  wheel: {{fileID: 650}}
  box: {{fileID: 630}}
--- !u!1001 &60
PrefabInstance:
  m_Modification:
    m_TransformParent: {{fileID: 2}}
    m_Modifications:
{mods}    m_AddedGameObjects: []
    m_AddedComponents:
    - targetCorrespondingSourceObject: {{fileID: 10, guid: {MISSING}, type: 3}}
      insertIndex: -1
      addedObject: {{fileID: 700}}
    - targetCorrespondingSourceObject: {{fileID: 20, guid: {MISSING}, type: 3}}
      insertIndex: -1
      addedObject: {{fileID: 710}}
  m_SourcePrefab: {{fileID: 100100000, guid: {MISSING}, type: 3}}
"
    );
    scene += &stripped(
        114,
        "MonoBehaviour",
        610,
        24,
        "  m_GameObject: {fileID: 600}\n  m_Enabled: 1\n  m_Script: {fileID: 11500000, guid: 0000000000000000000000000000beef, type: 3}\n",
    );
    scene += &stripped(65, "BoxCollider", 630, 22, "");
    scene += &stripped(65, "BoxCollider", 620, 23, "");
    scene += &stripped(224, "RectTransform", 645, 25, "");
    scene += &stripped(4, "Transform", 640, 21, "");
    scene += &stripped(1, "GameObject", 650, 20, "");
    scene += &stripped(4, "Transform", 601, 11, "");
    scene += &stripped(1, "GameObject", 600, 10, "");
    scene += "--- !u!1 &800
GameObject:
  m_Component:
  - component: {fileID: 801}
  m_Name: Added
--- !u!4 &801
Transform:
  m_GameObject: {fileID: 800}
  m_Children: []
  m_Father: {fileID: 601}
--- !u!1 &810
GameObject:
  m_Component:
  - component: {fileID: 811}
  m_Name: Deep
--- !u!4 &811
Transform:
  m_GameObject: {fileID: 810}
  m_Children: []
  m_Father: {fileID: 640}
--- !u!135 &700
SphereCollider:
  m_GameObject: {fileID: 600}
--- !u!136 &710
CapsuleCollider:
  m_GameObject: {fileID: 650}
";
    scene
}

fn ids(doc: &Document, list: &str, key: Option<&str>) -> Vec<i64> {
    doc.body
        .get(list)
        .and_then(Value::as_seq)
        .unwrap_or_default()
        .iter()
        .map(|item| {
            let item = match key {
                Some(k) => item.get(k).unwrap(),
                None => item,
            };
            item.get("fileID").unwrap().as_i64().unwrap()
        })
        .collect()
}

fn name_of(docs: &[Document], transform: i64) -> String {
    let t = docs.iter().find(|d| d.file_id == transform).unwrap();
    let go = t
        .body
        .path("m_GameObject.fileID")
        .unwrap()
        .as_i64()
        .unwrap();
    let go = docs.iter().find(|d| d.file_id == go).unwrap();
    go.body.get("m_Name").unwrap().as_str().unwrap().to_string()
}

#[test]
fn a_missing_prefabs_stripped_objects_become_the_editors_placeholders() {
    let scene = parse_documents(&missing_scene(true)).unwrap();
    let docs = flatten(&scene, &Fixture(HashMap::new()), &mut Vec::new()).unwrap();
    let by_id = |id: i64| docs.iter().find(|d| d.file_id == id).unwrap();
    // The root: the stripped GameObject and Transform the m_Name and position modifications
    // target, named after the instance, with the root modifications and the added component.
    let root = by_id(600);
    assert_eq!(
        root.body.get("m_Name").unwrap().as_str(),
        Some(format!("Car (Missing Prefab with guid: {MISSING})").as_str())
    );
    assert_eq!(ids(root, "m_Component", Some("component")), [601, 700]);
    let root_t = by_id(601);
    assert_eq!(
        root_t.body.path("m_LocalPosition.x").unwrap().as_i64(),
        Some(2)
    );
    assert_eq!(
        root_t.body.path("m_Father.fileID").unwrap().as_i64(),
        Some(2)
    );
    // Its children: the added child first, then a placeholder per other stripped object in class
    // id then file id order, numbered by the root's child count.
    let children = ids(root_t, "m_Children", None);
    let names: Vec<String> = children.iter().map(|&t| name_of(&docs, t)).collect();
    assert_eq!(
        names,
        [
            "Added",
            "Placeholder for referenced GameObject in Prefab instance (2)",
            "Placeholder for referenced Transform in Prefab instance (3)",
            "Placeholder for referenced BoxCollider in Prefab instance (4)",
            "Placeholder for referenced BoxCollider in Prefab instance (5)",
            "Placeholder for referenced MonoBehaviour in Prefab instance (6)",
            "Placeholder for referenced RectTransform in Prefab instance (7)",
        ]
    );
    assert_eq!(children[0], 801);
    assert_eq!(children[2], 640);
    assert_eq!(by_id(645).class_id, 224);
    // A stripped GameObject gets a Transform, then what the instance adds to it.
    let wheel = by_id(650);
    let wheel_components = ids(wheel, "m_Component", Some("component"));
    assert_eq!(wheel_components.len(), 2);
    assert_eq!(wheel_components[1], 710);
    assert_eq!(wheel_components[0], children[1]);
    // A stripped component gets a GameObject and a Transform; the referenced objects exist.
    let box_a = by_id(620);
    assert!(!box_a.stripped);
    let owner = box_a
        .body
        .path("m_GameObject.fileID")
        .unwrap()
        .as_i64()
        .unwrap();
    assert_eq!(ids(by_id(owner), "m_Component", Some("component"))[1], 620);
    assert_eq!(
        children[3],
        ids(by_id(owner), "m_Component", Some("component"))[0]
    );
    let script = by_id(610);
    assert_eq!(
        script.body.path("m_Script.guid").unwrap().as_str(),
        Some("0000000000000000000000000000beef")
    );
    assert_ne!(
        script.body.path("m_GameObject.fileID").unwrap().as_i64(),
        Some(600)
    );
    // An added child of a stripped child stays under that child's placeholder.
    assert_eq!(ids(by_id(640), "m_Children", None), [811]);
    // The instance is marked with its root's name for the scene load's error.
    let instance = by_id(60);
    assert_eq!(
        instance.body.get(MISSING_PREFAB_NAME).unwrap().as_str(),
        root.body.get("m_Name").unwrap().as_str()
    );
    assert_eq!(
        instance
            .body
            .path(&format!("{MISSING_PREFAB_ROOT}.fileID"))
            .unwrap()
            .as_i64(),
        Some(600)
    );
    let unique: HashSet<i64> = docs.iter().map(|d| d.file_id).collect();
    assert_eq!(unique.len(), docs.len());
}

#[test]
fn without_root_modifications_a_missing_prefabs_root_is_new_and_unmodified() {
    let scene = parse_documents(&missing_scene(false)).unwrap();
    let docs = flatten(&scene, &Fixture(HashMap::new()), &mut Vec::new()).unwrap();
    let instance = docs.iter().find(|d| d.file_id == 60).unwrap();
    let name = format!("Missing Prefab with guid: {MISSING}");
    assert_eq!(
        instance.body.get(MISSING_PREFAB_NAME).unwrap().as_str(),
        Some(name.as_str())
    );
    let root = docs
        .iter()
        .find(|d| d.class_id == 1 && d.body.get("m_Name").and_then(Value::as_str) == Some(&name))
        .unwrap();
    assert!(![600, 650].contains(&root.file_id));
    let root_t = ids(root, "m_Component", Some("component"))[0];
    let root_t = docs.iter().find(|d| d.file_id == root_t).unwrap();
    assert_eq!(
        root_t.body.path("m_LocalPosition.x").unwrap().as_i64(),
        Some(0)
    );
    // The stripped root objects are placeholders like the others; the added child of the
    // stripped root transform now sits under that transform's placeholder.
    let names: Vec<String> = ids(root_t, "m_Children", None)
        .iter()
        .map(|&t| name_of(&docs, t))
        .collect();
    assert_eq!(
        names[0],
        "Placeholder for referenced GameObject in Prefab instance (1)"
    );
    assert_eq!(
        names[1],
        "Placeholder for referenced GameObject in Prefab instance (2)"
    );
    assert_eq!(names.len(), 8);
    assert_eq!(
        ids(
            docs.iter().find(|d| d.file_id == 601).unwrap(),
            "m_Children",
            None
        ),
        [801]
    );
}

#[test]
fn a_variant_of_a_missing_prefab_does_not_load_and_its_instances_are_missing() {
    let variant_guid = Guid::parse("0000000000000000000000000000cafe").unwrap();
    let variant = format!(
        "%YAML 1.1
--- !u!1001 &5
PrefabInstance:
  m_Modification:
    m_TransformParent: {{fileID: 0}}
    m_Modifications:
    - target: {{fileID: 10, guid: {MISSING}, type: 3}}
      propertyPath: m_Name
      value: Variant
      objectReference: {{fileID: 0}}
  m_SourcePrefab: {{fileID: 100100000, guid: {MISSING}, type: 3}}
"
    );
    let variant = parse_documents(&variant).unwrap();
    let expanded = flatten(&variant, &Fixture(HashMap::new()), &mut Vec::new()).unwrap();
    assert!(root_is_missing_prefab(&expanded));
    // A prefab whose missing instance has a parent is not a variant of it: it still loads.
    let scene = parse_documents(&missing_scene(true)).unwrap();
    let docs = flatten(&scene, &Fixture(HashMap::new()), &mut Vec::new()).unwrap();
    assert!(!root_is_missing_prefab(&docs));

    let scene = format!(
        "%YAML 1.1
--- !u!1001 &70
PrefabInstance:
  m_Modification:
    m_TransformParent: {{fileID: 0}}
    m_Modifications:
    - target: {{fileID: 7, guid: {variant_guid}, type: 3}}
      propertyPath: m_Name
      value: V
      objectReference: {{fileID: 0}}
  m_SourcePrefab: {{fileID: 100100000, guid: {variant_guid}, type: 3}}
"
    );
    let fixture = Fixture([(variant_guid, variant)].into());
    let docs = flatten(&parse_documents(&scene).unwrap(), &fixture, &mut Vec::new()).unwrap();
    let instance = docs.iter().find(|d| d.file_id == 70).unwrap();
    assert_eq!(
        instance.body.get(MISSING_PREFAB_NAME).unwrap().as_str(),
        Some(format!("V (Missing Prefab with guid: {variant_guid})").as_str())
    );
}

/// A source that exists but does not load (an import Unbound cannot make).
struct Broken;

impl PrefabSource for Broken {
    fn prefab(&self, guid: Guid, _: &mut Vec<Guid>) -> anyhow::Result<Arc<Vec<Document>>> {
        anyhow::bail!("importing {guid}: unsupported")
    }
}

#[test]
fn a_source_that_fails_to_load_keeps_placeholders_but_is_no_missing_prefab() {
    let scene = parse_documents(&missing_scene(true)).unwrap();
    let docs = flatten(&scene, &Broken, &mut Vec::new()).unwrap();
    let by_id = |id: i64| docs.iter().find(|d| d.file_id == id).unwrap();
    // References still resolve, but the instance is not marked: no missing-prefab error, no
    // MissingAsset status, and a variant of it would still load.
    assert!(!by_id(650).stripped && !by_id(630).stripped);
    assert_eq!(by_id(600).body.get("m_Name").unwrap().as_str(), Some("Car"));
    assert!(by_id(60).body.get(MISSING_PREFAB_NAME).is_none());
    assert!(!root_is_missing_prefab(&docs));
}

#[test]
fn the_first_name_modification_names_and_picks_the_root_even_of_a_child() {
    // A child's rename listed before the root's (the Editor lists modifications by target): that
    // child's stripped GameObject becomes the root, as in the Editor
    // (tests/projects/missingprefab `W2`); the real root GameObject becomes a placeholder.
    let scene = missing_scene(true).replacen(
        "    m_Modifications:\n",
        &format!(
            "    m_Modifications:\n    - target: {{fileID: 20, guid: {MISSING}, type: 3}}\n      propertyPath: m_Name\n      value: Wheel\n      objectReference: {{fileID: 0}}\n"
        ),
        1,
    );
    let docs = flatten(
        &parse_documents(&scene).unwrap(),
        &Fixture(HashMap::new()),
        &mut Vec::new(),
    )
    .unwrap();
    let by_id = |id: i64| docs.iter().find(|d| d.file_id == id).unwrap();
    assert_eq!(
        by_id(650).body.get("m_Name").unwrap().as_str(),
        Some(format!("Wheel (Missing Prefab with guid: {MISSING})").as_str())
    );
    assert_eq!(
        ids(by_id(650), "m_Component", Some("component")),
        [601, 710]
    );
    assert_eq!(
        by_id(600).body.get("m_Name").unwrap().as_str(),
        Some("Placeholder for referenced GameObject in Prefab instance (2)")
    );
    // The component added to the real root stays on it.
    assert_eq!(ids(by_id(600), "m_Component", Some("component"))[1], 700);
}

#[test]
fn postprocessed_prefabs_are_what_instances_expand_and_the_raw_documents_stay() {
    let dir =
        std::env::temp_dir().join(format!("unbound-scene-postprocess-{}", std::process::id()));
    std::fs::create_dir_all(&dir).unwrap();
    let prefab = dir.join("Enemy.prefab");
    let scene = dir.join("World.unity");
    std::fs::write(&prefab, PREFAB).unwrap();
    std::fs::write(&scene, SCENE).unwrap();
    let guid = Guid::parse("0000000000000000000000000000abcd").unwrap();
    let located = prefab.clone();
    let loader = DocumentLoader::new(move |g| (g == guid).then(|| located.clone()))
        .with_postprocessor(Box::new(|_, file, docs| {
            file.extension()?;
            let mut docs = docs.to_vec();
            let go = docs.iter_mut().find(|d| d.file_id == 100)?;
            if let Value::Map(map) = &mut go.body {
                map.insert("m_Postprocessed", Value::Scalar("1".into()));
            }
            Some(Ok(docs))
        }));
    let postprocessed = |docs: &[Document], id: i64| {
        docs.iter()
            .find(|d| d.file_id == id)
            .and_then(|d| d.body.get("m_Postprocessed"))
            .is_some()
    };
    assert!(postprocessed(
        &loader.prefab(guid, &mut Vec::new()).unwrap(),
        100
    ));
    assert!(!postprocessed(
        &loader.prefab_unprocessed(guid).unwrap(),
        100
    ));
    // A scene's instance expands the postprocessed prefab, its modifications over it.
    let world = loader.load(&scene).unwrap();
    assert!(postprocessed(&world, 50 ^ 100));
    let boss = world.iter().find(|d| d.file_id == 50 ^ 100).unwrap();
    assert_eq!(boss.body.get("m_Name").unwrap().as_str(), Some("Boss"));
    std::fs::remove_dir_all(&dir).ok();
}
