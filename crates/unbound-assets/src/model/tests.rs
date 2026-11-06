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
    assert_eq!(s.material_import, MaterialImportMode::None);
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
    assert!(s.use_file_scale && !s.is_readable);
    assert_eq!(s.material_import, MaterialImportMode::Description);
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
    // Mirrored on X and scaled by 100, vertices in control point order; winding reversed.
    assert_eq!(
        mesh.positions,
        [[0.0, 0.0, 0.0], [-100.0, 0.0, 0.0], [0.0, 100.0, 0.0]]
    );
    assert_eq!(mesh.submeshes, [vec![0, 2, 1]]);
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
        obj: false,
        name: String::new(),
        legacy: false,
        model: ImportedModel {
            name: String::new(),
            nodes: Vec::new(),
            meshes: Vec::new(),
            materials: Vec::new(),
            animator: None,
            avatar: None,
            human_description: None,
            lod_group: None,
            scale: 2.0,
            settings: builder_settings.clone(),
            warnings: Vec::new(),
            clips: Vec::new(),
        },
        unique_names: HashMap::new(),
        node_index: HashMap::new(),
        sources: Vec::new(),
        material_index: HashMap::new(),
        obj_smoothing: Vec::new(),
        fbx_smoothing: Vec::new(),
        rest_pose: None,
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
    assert_eq!(
        hashed_file_id("GameObject", ROOT_NODE_NAME, 0),
        -927_199_367_670_048_503
    );
    assert_eq!(
        hashed_file_id("Transform", ROOT_NODE_NAME, 0),
        -4_216_859_302_048_453_862
    );
    assert_eq!(
        hashed_file_id("MeshFilter", ROOT_NODE_NAME, 0),
        -5_178_661_895_559_278_292
    );
    assert_eq!(
        hashed_file_id("MeshRenderer", ROOT_NODE_NAME, 0),
        -1_504_981_713_932_161_579
    );
    assert_eq!(
        hashed_file_id("Material", "No Name", 0),
        670_224_309_865_224_971
    );
}

/// An ASCII FBX 7.4 with two nodes under the root: `Pivoted`, a triangle around (-50, 10, 0) cm
/// with its pivots there and `Lcl Translation` (50, -10, 0), and `Plain` beside it.
const PIVOTED_FBX: &str = r#"; FBX 7.4.0 project file
FBXHeaderExtension:  {
	FBXHeaderVersion: 1003
	FBXVersion: 7400
}
GlobalSettings:  {
	Version: 1000
	Properties70:  {
		P: "UpAxis", "int", "Integer", "",1
		P: "UpAxisSign", "int", "Integer", "",1
		P: "FrontAxis", "int", "Integer", "",2
		P: "FrontAxisSign", "int", "Integer", "",1
		P: "CoordAxis", "int", "Integer", "",0
		P: "CoordAxisSign", "int", "Integer", "",1
		P: "UnitScaleFactor", "double", "Number", "",1
	}
}
Objects:  {
	Geometry: 100, "Geometry::Pivoted", "Mesh" {
		Vertices: *9 {
			a: -51,9,0,-49,9,0,-50,11,0
		}
		PolygonVertexIndex: *3 {
			a: 0,1,-3
		}
	}
	Model: 200, "Model::Pivoted", "Mesh" {
		Version: 232
		Properties70:  {
			P: "RotationPivot", "Vector3D", "Vector", "",-50,10,0
			P: "ScalingPivot", "Vector3D", "Vector", "",-50,10,0
			P: "Lcl Translation", "Lcl Translation", "", "A",50,-10,0
		}
	}
	Geometry: 101, "Geometry::Plain", "Mesh" {
		Vertices: *9 {
			a: 1,0,0,2,0,0,1,1,0
		}
		PolygonVertexIndex: *3 {
			a: 0,1,-3
		}
	}
	Model: 201, "Model::Plain", "Mesh" {
		Version: 232
		Properties70:  {
			P: "Lcl Translation", "Lcl Translation", "", "A",0,5,0
		}
	}
}
Connections:  {
	C: "OO",200,0
	C: "OO",100,200
	C: "OO",201,0
	C: "OO",101,201
}
"#;

#[test]
fn a_node_moves_to_its_pivot_and_its_mesh_by_the_opposite() {
    // Measured on the 6000.0 Editor with a Maya node like `Pivoted`: the node at local zero,
    // its mesh centred on the pivot. A node without pivots keeps its translation and vertices.
    let model = ModelImporter
        .import(Path::new("Pivoted.fbx"), PIVOTED_FBX.as_bytes(), &Value::Null)
        .unwrap();
    let node = |name: &str| model.nodes.iter().find(|n| n.name == name).unwrap();
    let centre = |name: &str| {
        let m = model.meshes.iter().find(|m| m.name == name).unwrap();
        let n = m.positions.len() as f32;
        (0..3).map(|k| m.positions.iter().map(|p| p[k]).sum::<f32>() / n).collect::<Vec<_>>()
    };
    let near = |a: &[f32], b: &[f32]| a.iter().zip(b).all(|(x, y)| (x - y).abs() < 1e-4);
    // Centimetres to metres, mirrored on X.
    assert!(near(&node("Pivoted").position, &[0.0, 0.0, 0.0]), "{:?}", node("Pivoted").position);
    let pivoted = centre("Pivoted");
    assert!(near(&pivoted[..2], &[0.0, -0.00333]), "{pivoted:?}");
    assert!(near(&node("Plain").position, &[0.0, 0.05, 0.0]), "{:?}", node("Plain").position);
    let plain = centre("Plain");
    assert!(near(&plain[..2], &[-0.01333, 0.00333]), "{plain:?}");
}

#[test]
fn lod_levels_come_from_case_sensitive_suffixes() {
    assert_eq!(lod_level("Tree_LOD0"), Some(0));
    assert_eq!(lod_level("Gap_LOD12"), Some(12));
    assert_eq!(lod_level("Stone_lod0"), None);
    assert_eq!(lod_level("Plain"), None);
    assert_eq!(lod_level("Tree_LOD"), None);
    assert_eq!(lod_level("Tree_LOD1a"), None);
}

#[test]
fn omitted_settings_take_the_defaults_of_the_meta_version() {
    let current = ModelSettings::read(&meta("ModelImporter:\n  serializedVersion: 22200\n"));
    assert_eq!(current.material_import, MaterialImportMode::Description);
    assert_eq!(current.material_location, MaterialLocation::InPrefab);
    assert_eq!(current.avatar_setup, 0);
    let old = ModelSettings::read(&meta("ModelImporter:\n  serializedVersion: 19\n"));
    assert_eq!(old.material_import, MaterialImportMode::Legacy);
    assert_eq!(old.material_location, MaterialLocation::External);
    assert_eq!(old.avatar_setup, 1);
}

#[test]
fn clip_frames_start_and_end_off_the_take_frames() {
    let fps = 24.0;
    let times = frame_times(4.5 / fps, 7.5 / fps, fps);
    let frames: Vec<f64> = times.iter().map(|t| (t * fps * 100.0).round() / 100.0).collect();
    assert_eq!(frames, vec![4.5, 5.0, 6.0, 7.0, 7.5]);
    let whole = frame_times(0.0, 2.0 / fps, fps);
    assert_eq!(whole.len(), 3);
}

#[test]
fn clip_ranges_interpolate_their_ends() {
    let keys: Vec<Key<1>> = (0..5).map(|i| Key { time: i as f32, value: [i as f32 * 2.0] }).collect();
    let out = clip_range(&keys, 0.5, 2.5, lerp_key);
    let pairs: Vec<(f32, f32)> = out.iter().map(|k| (k.time, k.value[0])).collect();
    assert_eq!(pairs, vec![(0.0, 1.0), (0.5, 2.0), (1.5, 4.0), (2.0, 5.0)]);
}

#[test]
fn reads_clip_masks_curves_and_references() {
    let item = meta(
        r#"name: Walk
takeName: Take 001
internalID: -623603649343465320
firstFrame: 6
lastFrame: 30
hasAdditiveReferencePose: 1
additiveReferencePoseFrame: 18
mirror: 1
keepOriginalPositionY: 0
curves:
- name: Speed
  curve:
    serializedVersion: 2
    curve:
    - serializedVersion: 3
      time: 0.5
      value: 2
      inSlope: 4
      outSlope: -2
      tangentMode: 0
      weightedMode: 0
      inWeight: 0
      outWeight: 0
    preInfinity: 2
    postInfinity: 1
transformMask:
- path: 
  weight: 1
- path: Hips/Spine
  weight: 0
maskType: 0
"#,
    );
    let range = ClipRange::read(&item);
    assert_eq!(range.internal_id, Some(-623603649343465320));
    assert_eq!(range.take, "Take 001");
    assert!(range.has_additive_reference);
    assert_eq!(range.additive_frame, Some(18.0));
    assert!(range.settings.mirror && !range.settings.keep_original_position_y);
    assert_eq!(range.parameters[0].name, "Speed");
    assert_eq!(range.parameters[0].keys[0].out_slope, -2.0);
    assert_eq!(range.parameters[0].post_infinity, 1);
    assert!(range.mask.keeps(""));
    assert!(!range.mask.keeps("Hips/Spine"));
    // Not listed while the mask lists others: left out.
    assert!(!range.mask.keeps("Hips"));
    let none = ClipRange::read(&meta("name: A\nmaskType: 3\ntransformMask:\n- path: A\n  weight: 0\n"));
    assert!(none.mask.keeps("A"));
}

#[test]
fn triangles_and_quads_split_like_unity() {
    // A kite whose short diagonal is 1-3: Unity still splits at 0-2 of the reversed corners,
    // giving (c0, c3, c2) and (c0, c2, c1) (measured: tests/projects/lightmapuvs).
    let obj = "o Kite\nv 0 0 0\nv 1 0 0\nv 3 3 0\nv 0 1 0\nv 5 0 0\nv 6 0 0\nv 5 1 0\nf 1 2 3 4\nf 5 6 7\n";
    let model = ModelImporter
        .import(Path::new("Kite.obj"), obj.as_bytes(), &Value::Null)
        .unwrap();
    let mesh = &model.meshes[0];
    // Mirrored on X; vertices in control point order (an OBJ's first-use order).
    assert_eq!(
        mesh.positions,
        vec![
            [-0.0, 0.0, 0.0],
            [-1.0, 0.0, 0.0],
            [-3.0, 3.0, 0.0],
            [-0.0, 1.0, 0.0],
            [-5.0, 0.0, 0.0],
            [-6.0, 0.0, 0.0],
            [-5.0, 1.0, 0.0],
        ]
    );
    assert_eq!(mesh.submeshes, vec![vec![0, 3, 2, 0, 2, 1, 4, 6, 5]]);
}

#[test]
fn lightmap_uvs_read_their_settings_and_replace_uv2() {
    let obj = "o Quad\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nvt 0 0\nvt 1 0\nvt 1 1\nvt 0 1\nf 1/1 2/2 3/3 4/4\n";
    let on = meta(
        "ModelImporter:\n  serializedVersion: 22200\n  meshes:\n    generateSecondaryUV: 1\n    secondaryUVHardAngle: 30\n    secondaryUVMarginMethod: 1\n    secondaryUVMinLightmapResolution: 10\n",
    );
    let settings = ModelSettings::read(&on).secondary_uv.unwrap();
    assert_eq!(settings.hard_angle, 30.0);
    assert_eq!(
        (settings.angle_distortion, settings.area_distortion),
        (8.0, 15.0)
    );
    assert_eq!(settings.margin_method, 1);
    assert_eq!(settings.min_lightmap_resolution, 10.0);
    let model = ModelImporter
        .import(Path::new("Quad.obj"), obj.as_bytes(), &on)
        .unwrap();
    assert!(model.warnings.is_empty(), "{:?}", model.warnings);
    let mesh = &model.meshes[0];
    assert_eq!(mesh.uvs.len(), 2);
    assert_eq!(mesh.uvs[1].len(), mesh.positions.len());
    // One chart filling the square up to the margin.
    for uv in &mesh.uvs[1] {
        assert!(
            (0.0..=1.0).contains(&uv[0]) && (0.0..=1.0).contains(&uv[1]),
            "{uv:?}"
        );
    }
    let unwelded = meta("ModelImporter:\n  meshes:\n    generateSecondaryUV: 1\n    weldVertices: 0\n");
    let model = ModelImporter
        .import(Path::new("Quad.obj"), obj.as_bytes(), &unwelded)
        .unwrap();
    assert!(model.warnings.iter().any(|w| w.contains("weldVertices")), "{:?}", model.warnings);
    let off = meta("ModelImporter:\n  meshes:\n    generateSecondaryUV: 0\n");
    assert!(ModelSettings::read(&off).secondary_uv.is_none());
    let model = ModelImporter
        .import(Path::new("Quad.obj"), obj.as_bytes(), &off)
        .unwrap();
    assert_eq!(model.meshes[0].uvs.len(), 1);
}

#[test]
fn lightmap_uvs_unwrap_polygons_of_more_than_four_corners() {
    let obj = "o Pentagon\nv 0 0 0\nv 1 0 0\nv 1.3 1 0\nv 0.5 1.6 0\nv -0.3 1 0\nf 1 2 3 4 5\n";
    let on = meta("ModelImporter:\n  serializedVersion: 22200\n  meshes:\n    generateSecondaryUV: 1\n");
    let model = ModelImporter.import(Path::new("Pentagon.obj"), obj.as_bytes(), &on).unwrap();
    assert!(model.warnings.is_empty(), "{:?}", model.warnings);
    let mesh = &model.meshes[0];
    assert_eq!(mesh.submeshes[0].len(), 9);
    assert_eq!(mesh.uvs[1].len(), mesh.positions.len());
    assert!(mesh.uvs[1].iter().flatten().all(|v| v.is_finite() && (0.0..=1.0).contains(v)));
}

#[test]
fn lightmap_uvs_unwrap_parts_of_more_than_a_hundred_triangles() {
    // A connected part of more than a hundred triangles (a 10×6 grid of quads).
    let mut obj = String::from("o Grid\n");
    for y in 0..7 {
        for x in 0..11 {
            obj += &format!("v {x} {y} 0\n");
        }
    }
    for y in 0..6 {
        for x in 0..10 {
            let a = y * 11 + x + 1;
            obj += &format!("f {} {} {} {}\n", a, a + 1, a + 12, a + 11);
        }
    }
    let on =
        meta("ModelImporter:\n  serializedVersion: 22200\n  meshes:\n    generateSecondaryUV: 1\n");
    let model = ModelImporter
        .import(Path::new("Grid.obj"), obj.as_bytes(), &on)
        .unwrap();
    assert!(
        !model.warnings.iter().any(|w| w.contains("lightmap UVs")),
        "{:?}",
        model.warnings
    );
    let mesh = &model.meshes[0];
    assert_eq!(mesh.uvs.len(), 2);
    for uv in &mesh.uvs[1] {
        assert!(
            (0.0..=1.0).contains(&uv[0]) && (0.0..=1.0).contains(&uv[1]),
            "{uv:?}"
        );
    }
}

#[test]
fn weld_vertices_can_preserve_coincident_authored_points() {
    // The Model tab documents that disabling welding preserves distinct source vertices.
    let obj = b"o Doubled\nv 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\nf 4 5 6\n";
    let settings = |enabled| meta(&format!("ModelImporter:\n  meshes:\n    weldVertices: {enabled}\n"));
    let welded = ModelImporter.import(Path::new("Doubled.obj"), obj, &settings(1)).unwrap();
    let separate = ModelImporter.import(Path::new("Doubled.obj"), obj, &settings(0)).unwrap();
    assert_eq!(welded.meshes[0].positions.len(), 3);
    assert_eq!(separate.meshes[0].positions.len(), 6);
}

#[test]
fn a_collapsed_quad_is_reported_instead_of_losing_its_valid_triangle() {
    let obj = b"o Collapsed\nv 0 0 0\nv 1 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3 4\n";
    let error = ModelImporter.import(Path::new("Collapsed.obj"), obj, &Value::Null).unwrap_err();
    assert!(error.to_string().contains("degenerate imported quads"), "{error}");
}

fn assert_group_normals_match_reference(imported: &ImportedModel) {
    let reference = std::fs::read_to_string(Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../tests/projects/lightmapuvs/Reference/unity-6000.0.71f1.jsonl")).unwrap();
    let row: serde_json::Value = reference.lines().map(|l| serde_json::from_str::<serde_json::Value>(l).unwrap())
        .find(|r| r["variant"] == "NormGroups" && r["mesh"] == "Groups/0").unwrap();
    let mesh = &imported.meshes[0];
    let positions: Vec<[f32; 3]> = serde_json::from_value(row["vertices"].clone()).unwrap();
    let normals: Vec<[f32; 3]> = serde_json::from_value(row["normals"].clone()).unwrap();
    let indices: Vec<Vec<u32>> = serde_json::from_value(row["subMeshes"].clone()).unwrap();
    assert_eq!(mesh.positions, positions);
    assert_eq!(mesh.submeshes, indices);
    assert_eq!(mesh.normals.len(), normals.len());
    for (actual, expected) in mesh.normals.iter().zip(normals) {
        for i in 0..3 { assert!((actual[i] - expected[i]).abs() <= 1.8e-7, "{actual:?} != {expected:?}"); }
    }
}

#[test]
fn fbx_group_normals_match_the_editor_reference() {
    let source = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../tests/projects/lightmapuvs/Sources/Normals/Groups.fbx");
    let settings = meta("ModelImporter:\n  meshes:\n    meshOptimizationFlags: 0\n  tangentSpace:\n    normalImportMode: 1\n    normalSmoothingSource: 1\n");
    let imported = ModelImporter.import(&source, &std::fs::read(&source).unwrap(), &settings).unwrap();
    assert_group_normals_match_reference(&imported);
    assert!(!imported.warnings.iter().any(|w| w.contains("smoothing")), "{:?}", imported.warnings);
}

#[test]
fn unsupported_lightmap_settings_keep_the_supported_group_normals() {
    let source = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../tests/projects/lightmapuvs/Sources/Normals/Groups.fbx");
    let settings = meta("ModelImporter:\n  meshes:\n    meshOptimizationFlags: 0\n    generateSecondaryUV: 1\n    weldVertices: 0\n  tangentSpace:\n    normalImportMode: 1\n    normalSmoothingSource: 1\n");
    let imported = ModelImporter.import(&source, &std::fs::read(&source).unwrap(), &settings).unwrap();
    assert!(imported.warnings.iter().any(|w| w.contains("weldVertices")), "{:?}", imported.warnings);
    assert_group_normals_match_reference(&imported);
}
