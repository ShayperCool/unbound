use super::*;
use std::sync::Arc;
use unbound_yaml::{Value, parse_documents};

struct Fixture(HashMap<Guid, Vec<Document>>);

impl PrefabSource for Fixture {
    fn prefab(&self, guid: Guid, stack: &mut Vec<Guid>) -> anyhow::Result<Arc<Vec<Document>>> {
        let docs = self.0.get(&guid).unwrap();
        stack.push(guid);
        let out = flatten(docs, self, stack)?;
        stack.pop();
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
