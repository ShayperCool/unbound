use super::*;

fn meta(text: &str) -> Value {
    unbound_yaml::parse_value(text).unwrap()
}

/// A Unity 2017-era meta (serializedVersion 19), trimmed.
const META_LEGACY: &str = r#"fileFormatVersion: 2
guid: 18cebb33057f53b81b1f178ea71dfa51
ModelImporter:
  serializedVersion: 19
  fileIDToRecycleName:
    100000: //RootNode
    100002: Box
    100004: Box 1
    400000: //RootNode
    400002: Box
    400004: Box 1
    2100000: red
    2300000: Box
    3300000: Box
    4300000: Box
    4300002: Box 1
    9500000: //RootNode
  materials:
    importMaterials: 0
    materialName: 0
    materialSearch: 1
  animations:
    isReadable: 1
  meshes:
    globalScale: 100
    importBlendShapes: 0
    swapUVChannels: 0
    useFileUnits: 1
    useFileScale: 0
  tangentSpace:
    normalSmoothAngle: 60
    normalImportMode: 0
    tangentImportMode: 2
  importAnimation: 0
  animationType: 2
"#;

#[test]
fn reads_legacy_settings() {
    let s = ModelSettings::read(&meta(META_LEGACY));
    assert_eq!(s.global_scale, 100.0);
    assert!(!s.use_file_scale);
    assert!(!s.import_blend_shapes);
    assert!(!s.import_materials);
    assert!(s.is_readable);
    assert_eq!(s.normals, NormalMode::Import);
    assert_eq!(s.tangents, TangentMode::None);
    assert_eq!(s.animation_type, 2);
    assert!(s.sort_hierarchy_by_name);
    assert!(!s.preserve_hierarchy);
}

#[test]
fn reads_6000_settings() {
    let s = ModelSettings::read(&meta(
        "ModelImporter:\n  materials:\n    materialImportMode: 2\n  meshes:\n    globalScale: 1\n    useFileScale: 1\n    isReadable: 0\n    sortHierarchyByName: 0\n    preserveHierarchy: 1\n  tangentSpace:\n    normalImportMode: 1\n    tangentImportMode: 3\n  animationType: 0\n",
    ));
    assert!(s.use_file_scale && s.import_materials && !s.is_readable);
    assert!(!s.sort_hierarchy_by_name && s.preserve_hierarchy);
    assert_eq!(s.normals, NormalMode::Calculate);
    assert_eq!(s.tangents, TangentMode::Calculate);
    assert_eq!(s.animation_type, 0);
}

/// Two objects named `Box`; the first a triangle in the XY plane facing +Z.
const OBJ: &str = "o Box
v 0 0 0
v 1 0 0
v 0 1 0
vn 0 0 1
usemtl red
f 1//1 2//1 3//1
o Box
v 0 0 1
v 1 0 1
v 0 1 1
f 4 5 6
";

#[test]
fn imports_an_obj_in_unity_space() {
    let model = ModelImporter
        .import(
            Path::new("Assets/Shapes.obj"),
            OBJ.as_bytes(),
            &meta(META_LEGACY),
        )
        .unwrap();
    assert_eq!(model.name, "Shapes");
    assert_eq!(model.scale, 100.0);

    // Two top-level nodes: an explicit root named after the file, children made unique.
    let names: Vec<&str> = model.nodes.iter().map(|n| n.name.as_str()).collect();
    assert_eq!(names, ["Shapes", "Box", "Box 1"]);
    assert_eq!(model.nodes[0].children, [1, 2]);
    assert_eq!(model.nodes[0].game_object.id, 100000);
    assert_eq!(model.nodes[0].transform.id, 400000);
    assert_eq!(model.nodes[2].game_object.id, 100004);
    assert_eq!(model.nodes[2].transform.id, 400004);
    assert_eq!(model.animator.unwrap().id, 9500000);
    assert_eq!(model.avatar.unwrap().id, 9000000);

    let renderer = model.nodes[1].renderer.as_ref().unwrap();
    assert_eq!(renderer.kind, RendererKind::Mesh);
    assert_eq!(renderer.renderer.id, 2300000);
    assert_eq!(renderer.filter.unwrap().id, 3300000);
    assert_eq!(renderer.materials, [Some(0)]);
    assert_eq!(model.materials[0].name, "red");
    assert_eq!(model.materials[0].file_id.id, 2100000);

    let mesh = &model.meshes[renderer.mesh as usize];
    assert_eq!(mesh.name, "Box");
    assert_eq!(mesh.file_id.id, 4300000);
    // Mirrored on X and scaled by 100; winding reversed: (0, 2, 1).
    assert_eq!(
        mesh.positions,
        [[0.0, 0.0, 0.0], [0.0, 100.0, 0.0], [-100.0, 0.0, 0.0]]
    );
    assert_eq!(mesh.submeshes, [vec![0, 1, 2]]);
    assert_eq!(mesh.normals[0], [-0.0, 0.0, 1.0]);
    assert_eq!(mesh.bounds_min, [-100.0, 0.0, 0.0]);
    assert_eq!(mesh.bounds_max, [0.0, 100.0, 0.0]);

    // The second object: unlisted renderer components get free legacy IDs.
    let second = model.nodes[2].renderer.as_ref().unwrap();
    assert_eq!(model.meshes[second.mesh as usize].file_id.id, 4300002);
    assert_eq!(second.renderer.id, 2300002);
    assert_eq!(second.renderer.source, FileIdSource::Derived);
}

#[test]
fn a_single_top_level_node_becomes_the_root() {
    let obj = "o Only\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n";
    let model = ModelImporter
        .import(Path::new("Single.obj"), obj.as_bytes(), &Value::Null)
        .unwrap();
    assert_eq!(model.nodes.len(), 1);
    assert_eq!(model.nodes[0].name, "Single");
    // A new-format meta without tables: the root objects get Unity's fixed IDs.
    assert_eq!(model.nodes[0].game_object.id, ROOT_GAME_OBJECT_ID);
    assert_eq!(model.nodes[0].transform.id, ROOT_TRANSFORM_ID);
    let renderer = model.nodes[0].renderer.as_ref().unwrap();
    assert_eq!(renderer.renderer.id, ROOT_MESH_RENDERER_ID);
    assert_eq!(model.meshes[0].name, "Only");

    let preserved = ModelImporter
        .import(
            Path::new("Single.obj"),
            obj.as_bytes(),
            &meta("ModelImporter:\n  meshes:\n    preserveHierarchy: 1\n"),
        )
        .unwrap();
    assert_eq!(preserved.nodes.len(), 2);
    assert!(preserved.nodes[0].renderer.is_none());
}

#[test]
fn bind_pose_conversion_mirrors_and_scales() {
    let builder_settings = ModelSettings::read(&Value::Null);
    let builder = Builder {
        settings: &builder_settings,
        scale: 2.0,
        name: String::new(),
        legacy: false,
        model: ImportedModel {
            name: String::new(),
            nodes: Vec::new(),
            meshes: Vec::new(),
            materials: Vec::new(),
            animator: None,
            avatar: None,
            scale: 2.0,
            settings: builder_settings.clone(),
            warnings: Vec::new(),
            clips: Vec::new(),
        },
        unique_names: HashMap::new(),
        node_index: HashMap::new(),
        sources: Vec::new(),
        material_index: HashMap::new(),
    };
    // A translation by (1, 2, 3) and a rotation of 90° about Z (x → y).
    let m = ufbx::Matrix {
        m00: 0.0,
        m10: 1.0,
        m20: 0.0,
        m01: -1.0,
        m11: 0.0,
        m21: 0.0,
        m02: 0.0,
        m12: 0.0,
        m22: 1.0,
        m03: 1.0,
        m13: 2.0,
        m23: 3.0,
    };
    let c = builder.matrix(&m);
    // Translation mirrored on X and scaled.
    assert_eq!(&c[12..], &[-2.0, 4.0, 6.0, 1.0]);
    // In the mirrored space the rotation is about Z the other way: x → -y.
    assert_eq!(&c[0..3], &[0.0, -1.0, 0.0]);
    assert_eq!(&c[4..7], &[1.0, 0.0, 0.0]);
}

#[test]
fn unlisted_ids_are_unitys_hash_of_class_and_name() {
    use crate::meta::hashed_file_id;
    // Measured on the 6000.0 Editor (fileIdsGeneration 1 models: the root's objects; a material).
    assert_eq!(hashed_file_id("GameObject", ROOT_NODE_NAME, 0), -927_199_367_670_048_503);
    assert_eq!(hashed_file_id("Transform", ROOT_NODE_NAME, 0), -4_216_859_302_048_453_862);
    assert_eq!(hashed_file_id("MeshFilter", ROOT_NODE_NAME, 0), -5_178_661_895_559_278_292);
    assert_eq!(hashed_file_id("MeshRenderer", ROOT_NODE_NAME, 0), -1_504_981_713_932_161_579);
    assert_eq!(hashed_file_id("Material", "No Name", 0), 670_224_309_865_224_971);
}
