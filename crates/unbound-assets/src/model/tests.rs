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
    100004: Lid
    400000: //RootNode
    400002: Box
    400004: Lid
    2100000: red
    2300000: Box
    3300000: Box
    4300000: Box
    4300002: Lid
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
    optimizeMeshForGPU: 0
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

/// Two groups, each also named as an object (which the importer does not read); the first a
/// triangle in the XY plane facing +Z.
const OBJ: &str = "o Shape
g Box
v 0 0 0
v 1 0 0
v 0 1 0
vn 0 0 1
usemtl red
f 1//1 2//1 3//1
o Shape
g Lid
v 0 0 1
v 1 0 1
v 0 1 1
f 4 5 6
";

#[test]
fn imports_an_obj_in_unity_space() {
    let model = ModelImporter::default()
        .import(
            Path::new("Assets/Shapes.obj"),
            OBJ.as_bytes(),
            &meta(META_LEGACY),
        )
        .unwrap();
    assert_eq!(model.name, "Shapes");
    assert_eq!(model.scale, 100.0);

    // A root named after the file with one child per group.
    let names: Vec<&str> = model.nodes.iter().map(|n| n.name.as_str()).collect();
    assert_eq!(names, ["Shapes", "Box", "Lid"]);
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
fn an_obj_mesh_is_a_child_named_after_its_group() {
    // Measured: tests/projects/lightmapuvs Names. The object statement is not read, a file
    // without groups has the mesh `default`, and the mesh never moves to the root.
    let obj = "o Only\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n";
    let model = ModelImporter::default()
        .import(Path::new("Single.obj"), obj.as_bytes(), &Value::Null)
        .unwrap();
    let names: Vec<&str> = model.nodes.iter().map(|n| n.name.as_str()).collect();
    assert_eq!(names, ["Single", "default"]);
    // A new-format meta without tables: the root objects get Unity's fixed IDs.
    assert_eq!(model.nodes[0].game_object.id, ROOT_GAME_OBJECT_ID);
    assert_eq!(model.nodes[0].transform.id, ROOT_TRANSFORM_ID);
    assert!(model.nodes[0].renderer.is_none());
    assert!(model.nodes[1].renderer.is_some());
    assert_eq!(model.meshes[0].name, "default");

    // A group that returns after another one is one mesh, in the order of its faces.
    let obj = "v 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 0 1\ng First\nf 1 2 3\ng Second\nf 1 2 4\n\
               g First\nf 2 3 4\ng two words\nf 1 3 4\n";
    let model = import_obj("Groups.obj", obj, NO_OPTIMIZATION);
    assert!(model.warnings.is_empty(), "{:?}", model.warnings);
    let names: Vec<&str> = model.meshes.iter().map(|m| m.name.as_str()).collect();
    assert_eq!(names, ["First", "Second", "two_words"]);
    assert_eq!(model.meshes[0].submeshes[0].len(), 6);
    assert_eq!(model.meshes[1].submeshes[0].len(), 3);
}

#[test]
fn siblings_sort_by_name_without_case_and_with_numbers_as_numbers() {
    // The Editor's order of tests/projects/lightmapuvs Names/OrderFbx.
    let expected = [
        "9", "10", "_under", "a01", "A1", "a1A", "a1b", "a2", "a10", "Alpha", "alpha", "b-2",
        "b.1", "B_0", "Beta", "Zed",
    ];
    let mut names = expected;
    names.reverse();
    names.sort_by(|a, b| hierarchy_name_order(a, b));
    assert_eq!(names, expected);
    // Outside the capture: numbers too long for an integer, and a name that is another's start.
    let mut names = ["x100000000000000000000000", "x99999999999999999999999", "x", "X1y", "x1"];
    names.sort_by(|a, b| hierarchy_name_order(a, b));
    assert_eq!(
        names,
        ["x", "x1", "X1y", "x99999999999999999999999", "x100000000000000000000000"]
    );
}

#[test]
fn mesh_optimization_reorders_triangles_and_vertices_by_its_flags() {
    // Two quads sharing an edge with a separate triangle listed between them, which the polygon
    // order draws first. Measured: tests/projects/lightmapuvs RawOff, OptPolygons, OptVertices
    // and Off.
    let obj = "g Strip\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nv 2 0 0\nv 2 1 0\nv 5 0 0\nv 6 0 0\nv 5 1 0\n\
               f 1 2 3 4\nf 7 8 9\nf 2 5 6 3\n";
    let with = |flags: &str| {
        let text = format!("ModelImporter:\n  meshes:\n{flags}");
        import_obj("Strip.obj", obj, &text)
    };
    let raw = with("    meshOptimizationFlags: 0\n");
    let polygons = with("    meshOptimizationFlags: 1\n");
    let vertices = with("    meshOptimizationFlags: 2\n");
    let both = with("    meshOptimizationFlags: -1\n");
    let triangles = |m: &ImportedModel| -> Vec<[[f32; 3]; 3]> {
        let mesh = &m.meshes[0];
        mesh.submeshes[0]
            .chunks(3)
            .map(|t| [0, 1, 2].map(|k| mesh.positions[t[k] as usize]))
            .collect()
    };
    let first_use = |m: &ImportedModel| {
        let mut seen = Vec::new();
        for &v in &m.meshes[0].submeshes[0] {
            if !seen.contains(&v) {
                seen.push(v);
            }
        }
        seen.iter().enumerate().all(|(i, &v)| i as u32 == v)
    };
    // Polygon order alone moves triangles, not vertices; vertex order alone the reverse.
    assert_ne!(triangles(&raw), triangles(&polygons));
    assert_eq!(raw.meshes[0].positions, polygons.meshes[0].positions);
    assert_eq!(triangles(&raw), triangles(&vertices));
    assert_ne!(raw.meshes[0].positions, vertices.meshes[0].positions);
    assert!(first_use(&vertices) && first_use(&both) && !first_use(&raw));
    assert_eq!(triangles(&polygons), triangles(&both));
    // No flags in the meta is everything; the setting before the flags is the vertex order
    // alone (lightmapuvs OldOptOff and OldOptOn).
    assert_eq!(with("    isReadable: 1\n").meshes[0], both.meshes[0]);
    assert_eq!(with("    optimizeMeshForGPU: 0\n").meshes[0], polygons.meshes[0]);
    assert_eq!(with("    optimizeMeshForGPU: 1\n").meshes[0], both.meshes[0]);
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
            file_order: Vec::new(),
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
        reverse_winding: false,
        pivots: Vec::new(),
        offsets: HashMap::new(),
        material_index: HashMap::new(),
        obj_diffuse: None,
        obj_meshes: Vec::new(),
        obj_defaults: Vec::new(),
        fbx_smoothing: Vec::new(),
        mtl: ModelImporter::default().mtl,
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

/// An ASCII FBX 7.4 with three nodes under the root: `Pivoted`, a triangle around (-50, 10, 0)
/// cm with its pivots there and `Lcl Translation` (50, -10, 0), `Unindexed`, the same without
/// `DefaultAttributeIndex`, and `Plain` beside them.
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
			P: "DefaultAttributeIndex", "int", "Integer", "",0
		}
	}
	Geometry: 102, "Geometry::Unindexed", "Mesh" {
		Vertices: *9 {
			a: -51,9,0,-49,9,0,-50,11,0
		}
		PolygonVertexIndex: *3 {
			a: 0,1,-3
		}
	}
	Model: 202, "Model::Unindexed", "Mesh" {
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
	C: "OO",202,0
	C: "OO",102,202
}
"#;

#[test]
fn a_node_moves_to_its_pivot_and_its_mesh_by_the_opposite() {
    // As in tests/projects/modelaxis (Pivots set, 6000.0.71f1): the node at local zero, its mesh
    // centred on the pivot. A node without pivots keeps its translation and vertices. Without
    // an attribute index the node still moves, but the mesh keeps its file vertices.
    let model = ModelImporter::default()
        .import(
            Path::new("Pivoted.fbx"),
            PIVOTED_FBX.as_bytes(),
            &Value::Null,
        )
        .unwrap();
    let node = |name: &str| model.nodes.iter().find(|n| n.name == name).unwrap();
    let centre = |name: &str| {
        let m = model.meshes.iter().find(|m| m.name == name).unwrap();
        let n = m.positions.len() as f32;
        (0..3)
            .map(|k| m.positions.iter().map(|p| p[k]).sum::<f32>() / n)
            .collect::<Vec<_>>()
    };
    let near = |a: &[f32], b: &[f32]| a.iter().zip(b).all(|(x, y)| (x - y).abs() < 1e-4);
    // Centimetres to metres, mirrored on X.
    assert!(
        near(&node("Pivoted").position, &[0.0, 0.0, 0.0]),
        "{:?}",
        node("Pivoted").position
    );
    let pivoted = centre("Pivoted");
    assert!(near(&pivoted[..2], &[0.0, -0.00333]), "{pivoted:?}");
    assert!(
        near(&node("Plain").position, &[0.0, 0.05, 0.0]),
        "{:?}",
        node("Plain").position
    );
    let plain = centre("Plain");
    assert!(near(&plain[..2], &[-0.01333, 0.00333]), "{plain:?}");
    assert!(near(&node("Unindexed").position, &[0.0, 0.0, 0.0]));
    let unindexed = centre("Unindexed");
    assert!(near(&unindexed[..2], &[0.5, 0.09667]), "{unindexed:?}");
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
    let frames: Vec<f64> = times
        .iter()
        .map(|t| (t * fps * 100.0).round() / 100.0)
        .collect();
    assert_eq!(frames, vec![4.5, 5.0, 6.0, 7.0, 7.5]);
    let whole = frame_times(0.0, 2.0 / fps, fps);
    assert_eq!(whole.len(), 3);
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
    let none = ClipRange::read(&meta(
        "name: A\nmaskType: 3\ntransformMask:\n- path: A\n  weight: 0\n",
    ));
    assert!(none.mask.keeps("A"));
}

#[test]
fn triangles_and_quads_split_like_unity() {
    // A kite whose short diagonal is 1-3: Unity still splits at 0-2 of the reversed corners,
    // giving (c0, c3, c2) and (c0, c2, c1) (measured: tests/projects/lightmapuvs).
    let obj = "o Kite\nv 0 0 0\nv 1 0 0\nv 3 3 0\nv 0 1 0\nv 5 0 0\nv 6 0 0\nv 5 1 0\nf 1 2 3 4\nf 5 6 7\n";
    let model = import_obj("Kite.obj", obj, NO_OPTIMIZATION);
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
    let model = ModelImporter::default()
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
    let unwelded =
        meta("ModelImporter:\n  meshes:\n    generateSecondaryUV: 1\n    weldVertices: 0\n");
    let model = ModelImporter::default()
        .import(Path::new("Quad.obj"), obj.as_bytes(), &unwelded)
        .unwrap();
    assert!(
        model.warnings.iter().any(|w| w.contains("weldVertices")),
        "{:?}",
        model.warnings
    );
    let off = meta("ModelImporter:\n  meshes:\n    generateSecondaryUV: 0\n");
    assert!(ModelSettings::read(&off).secondary_uv.is_none());
    let model = ModelImporter::default()
        .import(Path::new("Quad.obj"), obj.as_bytes(), &off)
        .unwrap();
    assert_eq!(model.meshes[0].uvs.len(), 1);
}

#[test]
fn lightmap_uvs_unwrap_polygons_of_more_than_four_corners() {
    let obj = "o Pentagon\nv 0 0 0\nv 1 0 0\nv 1.3 1 0\nv 0.5 1.6 0\nv -0.3 1 0\nf 1 2 3 4 5\n";
    let on =
        meta("ModelImporter:\n  serializedVersion: 22200\n  meshes:\n    generateSecondaryUV: 1\n");
    let model = ModelImporter::default()
        .import(Path::new("Pentagon.obj"), obj.as_bytes(), &on)
        .unwrap();
    assert!(model.warnings.is_empty(), "{:?}", model.warnings);
    let mesh = &model.meshes[0];
    assert_eq!(mesh.submeshes[0].len(), 9);
    assert_eq!(mesh.uvs[1].len(), mesh.positions.len());
    assert!(
        mesh.uvs[1]
            .iter()
            .flatten()
            .all(|v| v.is_finite() && (0.0..=1.0).contains(v))
    );
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
    let model = ModelImporter::default()
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
fn lightmap_uvs_are_generated_for_meshes_with_zero_area_and_stacked_faces() {
    // A quad, a triangle of three collinear corners on its top edge, and a third face on the
    // quad's right edge (an edge of three faces).
    let obj = "o Rough\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nv 0.3 1 0\nv 1.5 0 0.8\nv 1.5 1 0.8\n\
               v 1.5 0 -0.8\nv 1.5 1 -0.8\n\
               f 1 2 3 4\nf 4 3 5\nf 2 6 7 3\nf 2 8 9 3\n";
    let on = "ModelImporter:\n  serializedVersion: 22200\n  meshes:\n    generateSecondaryUV: 1\n";
    let model = import_obj("Rough.obj", obj, on);
    assert!(
        !model.warnings.iter().any(|w| w.contains("lightmap UVs")),
        "{:?}",
        model.warnings
    );
    let mesh = &model.meshes[0];
    // The zero-area triangle stays in the mesh.
    assert_eq!(mesh.submeshes[0].len(), 3 * 7);
    assert_eq!(mesh.uvs.len(), 2);
    assert_eq!(mesh.uvs[1].len(), mesh.positions.len());
    assert!(
        mesh.uvs[1]
            .iter()
            .flatten()
            .all(|v| v.is_finite() && (0.0..=1.0).contains(v))
    );

    // Nothing but zero-area faces: the unwrap has no triangle left and the mesh is imported
    // empty; without generated lightmap UVs the same faces stay.
    let obj = "o Flat\nv 0 0 0\nv 1 0 0\nv 2 0 0\nv 3 0 0\nf 1 2 3\nf 2 3 4\n";
    let model = import_obj("Flat.obj", obj, on);
    assert!(
        model
            .warnings
            .iter()
            .any(|w| w.contains("the mesh is imported empty")),
        "{:?}",
        model.warnings
    );
    let mesh = &model.meshes[0];
    assert_eq!(mesh.submeshes, vec![Vec::<u32>::new()]);
    assert!(mesh.positions.is_empty() && mesh.normals.is_empty());
    assert!(mesh.uvs.iter().all(|uv| uv.is_empty()));
    let off = "ModelImporter:\n  serializedVersion: 22200\n  meshes:\n    generateSecondaryUV: 0\n";
    let model = import_obj("Flat.obj", obj, off);
    assert_eq!(model.meshes[0].submeshes[0].len(), 6);
    assert!(model.warnings.is_empty(), "{:?}", model.warnings);
}

#[test]
fn weld_vertices_can_preserve_coincident_authored_points() {
    // The Model tab documents that disabling welding preserves distinct source vertices.
    let obj =
        b"o Doubled\nv 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\nf 4 5 6\n";
    let settings = |enabled| {
        meta(&format!(
            "ModelImporter:\n  meshes:\n    weldVertices: {enabled}\n"
        ))
    };
    let welded = ModelImporter::default()
        .import(Path::new("Doubled.obj"), obj, &settings(1))
        .unwrap();
    let separate = ModelImporter::default()
        .import(Path::new("Doubled.obj"), obj, &settings(0))
        .unwrap();
    assert_eq!(welded.meshes[0].positions.len(), 3);
    assert_eq!(separate.meshes[0].positions.len(), 6);
}

fn import_obj(name: &str, obj: &str, meta_text: &str) -> ImportedModel {
    ModelImporter::default()
        .import(Path::new(name), obj.as_bytes(), &meta(meta_text))
        .unwrap()
}

const NO_OPTIMIZATION: &str = "ModelImporter:\n  meshes:\n    meshOptimizationFlags: 0\n";

#[test]
fn a_quad_collapsing_after_welding_keeps_its_valid_triangle() {
    // Corners 2 and 3 share a position: the quad becomes the triangle of its first three.
    let obj = "o Collapsed\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 1 1 0\nf 1 2 3 4\n";
    let model = import_obj("Collapsed.obj", obj, NO_OPTIMIZATION);
    assert_eq!(model.meshes[0].submeshes, vec![vec![0, 2, 1]]);
    // The same with the corner itself repeated, as some exporters write it.
    let obj = "o Repeated\nv 0 0 0\nv 1 0 0\nv 1 1 0\nf 1 2 3 3\n";
    let model = import_obj("Repeated.obj", obj, NO_OPTIMIZATION);
    assert_eq!(model.meshes[0].submeshes, vec![vec![0, 2, 1]]);
}

#[test]
fn faces_folded_by_welding_are_dropped_and_zero_area_faces_kept() {
    // A quad whose second and fourth corners weld, a triangle with two welded corners, and a
    // triangle and quad of collinear but distinct corners (measured: lightmapuvs WeldDiag,
    // WeldTri, FlatTri, FlatQuad).
    let obj = "o Faces\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nv 1 2 0\nv 1 1 0\nv 2 0 0\n\
               v 3 0 0\nv 4 0 0\nv 5 0 0\n\
               f 1 2 3 4\nf 4 3 5 6\nf 3 5 6\nf 7 8 9\nf 7 8 9 10\n";
    let model = import_obj("Faces.obj", obj, NO_OPTIMIZATION);
    let mesh = &model.meshes[0];
    let triangles: Vec<_> = mesh.submeshes[0].chunks(3).collect();
    // The base quad, then the collinear triangle and quad; nothing of the folded faces.
    assert_eq!(triangles.len(), 2 + 1 + 2, "{triangles:?}");
    // Ten control points, one welded; the folded quad's own point (1, 2) keeps an unused
    // vertex with mesh optimization off.
    assert_eq!(mesh.positions.len(), 9);
    let unused: Vec<_> = (0..9)
        .filter(|&v| mesh.normals[v] == UNUSED_VERTEX_NORMAL)
        .collect();
    assert_eq!(unused.len(), 1);
    assert_eq!(mesh.positions[unused[0]], [-1.0, 2.0, 0.0]);
}

#[test]
fn unused_points_keep_vertices_only_without_vertex_optimization() {
    // A pentagon of collinear corners has no triangles (lightmapuvs FlatPoly).
    let obj = "o Flat\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nv 0.25 1 0\nv 0.5 1 0\nv 0.75 1 0\n\
               f 1 2 3 4\nf 4 5 6 7 3\n";
    let raw = import_obj("Flat.obj", obj, NO_OPTIMIZATION);
    let mesh = &raw.meshes[0];
    assert_eq!(mesh.submeshes[0].len(), 6);
    assert_eq!(mesh.positions.len(), 7);
    assert_eq!(mesh.normals[4..], [UNUSED_VERTEX_NORMAL; 3]);
    assert_eq!(
        mesh.uvs.iter().map(|uv| uv.len()).collect::<Vec<_>>(),
        vec![7; mesh.uvs.len()]
    );
    let optimized = import_obj("Flat.obj", obj, "");
    assert_eq!(optimized.meshes[0].positions.len(), 4);
}

#[test]
fn polygons_with_coincident_corners_import() {
    // A square with a square hole bridged into one polygon, reusing its bridge corners: the
    // sweep merges them (lightmapuvs BridgeObj: eight triangles over eight vertices).
    let obj = "o Bridge\nv 0 0 0\nv 2 0 0\nv 2 2 0\nv 0 2 0\n\
               v 0.5 0.5 0\nv 0.5 1.5 0\nv 1.5 1.5 0\nv 1.5 0.5 0\nf 1 2 3 4 1 5 6 7 8 5\n";
    let model = import_obj("Bridge.obj", obj, NO_OPTIMIZATION);
    let mesh = &model.meshes[0];
    assert_eq!(mesh.positions.len(), 8);
    assert_eq!(mesh.submeshes[0].len(), 3 * 8);
    assert!(model.warnings.is_empty(), "{:?}", model.warnings);
}

#[test]
fn crossing_polygons_are_discarded_with_a_warning() {
    // A pentagram beside a valid quad: only the quad stays (lightmapuvs Cross).
    let mut obj = String::from("o Cross\nv 0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\n");
    for k in 0..5 {
        let a = 4.0 * std::f32::consts::PI * k as f32 / 5.0;
        obj += &format!("v {} {} 0\n", 3.0 + a.sin(), a.cos());
    }
    obj += "f 1 2 3 4\nf 5 6 7 8 9\n";
    let model = import_obj("Cross.obj", &obj, NO_OPTIMIZATION);
    assert_eq!(model.meshes[0].submeshes[0].len(), 6);
    assert!(
        model
            .warnings
            .iter()
            .any(|w| w.contains("self-intersecting")),
        "{:?}",
        model.warnings
    );
}

fn assert_group_normals_match_reference(imported: &ImportedModel) {
    let reference = std::fs::read_to_string(
        Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../../tests/projects/lightmapuvs/Reference/unity-6000.0.71f1.jsonl"),
    )
    .unwrap();
    let row: serde_json::Value = reference
        .lines()
        .map(|l| serde_json::from_str::<serde_json::Value>(l).unwrap())
        .find(|r| r["variant"] == "NormGroups" && r["mesh"] == "Groups/0")
        .unwrap();
    let mesh = &imported.meshes[0];
    let positions: Vec<[f32; 3]> = serde_json::from_value(row["vertices"].clone()).unwrap();
    let normals: Vec<[f32; 3]> = serde_json::from_value(row["normals"].clone()).unwrap();
    let indices: Vec<Vec<u32>> = serde_json::from_value(row["subMeshes"].clone()).unwrap();
    assert_eq!(mesh.positions, positions);
    assert_eq!(mesh.submeshes, indices);
    assert_eq!(mesh.normals.len(), normals.len());
    for (actual, expected) in mesh.normals.iter().zip(normals) {
        for i in 0..3 {
            assert!(
                (actual[i] - expected[i]).abs() <= 1.8e-7,
                "{actual:?} != {expected:?}"
            );
        }
    }
}

#[test]
fn fbx_group_normals_match_the_editor_reference() {
    let source = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../tests/projects/lightmapuvs/Sources/Normals/Groups.fbx");
    let settings = meta(
        "ModelImporter:\n  meshes:\n    meshOptimizationFlags: 0\n  tangentSpace:\n    normalImportMode: 1\n    normalSmoothingSource: 1\n",
    );
    let imported = ModelImporter::default()
        .import(&source, &std::fs::read(&source).unwrap(), &settings)
        .unwrap();
    assert_group_normals_match_reference(&imported);
    assert!(
        !imported.warnings.iter().any(|w| w.contains("smoothing")),
        "{:?}",
        imported.warnings
    );
}

#[test]
fn edge_smoothing_with_unenclosed_sharp_edges_matches_the_editor_reference() {
    // Blender "mark sharp" edges that end inside the surface (Slit) and a sharp seam beside
    // enclosing cap rims (Seam), in every calculated-normal variant.
    let root = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../tests/projects/lightmapuvs");
    let reference =
        std::fs::read_to_string(root.join("Reference/unity-6000.0.71f1.jsonl")).unwrap();
    let rows: Vec<serde_json::Value> = reference
        .lines()
        .map(|l| serde_json::from_str(l).unwrap())
        .collect();
    for name in ["Edges", "Slit", "Seam"] {
        for variant in [
            "NormCalc",
            "NormGroups",
            "NormAngle",
            "NormArea",
            "NormUnweighted",
        ] {
            let source = root.join(format!("Assets/Resources/{variant}/{name}.fbx"));
            let meta = meta(&std::fs::read_to_string(source.with_extension("fbx.meta")).unwrap());
            let model = ModelImporter::default()
                .import(&source, &std::fs::read(&source).unwrap(), &meta)
                .unwrap();
            let row = rows
                .iter()
                .find(|r| r["variant"] == variant && r["mesh"] == format!("{name}/0"))
                .unwrap();
            let normals: Vec<[f32; 3]> = serde_json::from_value(row["normals"].clone()).unwrap();
            let indices: Vec<Vec<u32>> = serde_json::from_value(row["subMeshes"].clone()).unwrap();
            let mesh = &model.meshes[0];
            assert_eq!(mesh.submeshes, indices, "{variant}/{name}");
            assert_eq!(mesh.normals.len(), normals.len(), "{variant}/{name}");
            for (actual, expected) in mesh.normals.iter().zip(&normals) {
                for i in 0..3 {
                    assert!(
                        (actual[i] - expected[i]).abs() <= 1.8e-7,
                        "{variant}/{name}: {actual:?} != {expected:?}"
                    );
                }
            }
        }
    }
}

#[test]
fn unsupported_lightmap_settings_keep_the_supported_group_normals() {
    let source = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../tests/projects/lightmapuvs/Sources/Normals/Groups.fbx");
    let settings = meta(
        "ModelImporter:\n  meshes:\n    meshOptimizationFlags: 0\n    generateSecondaryUV: 1\n    weldVertices: 0\n  tangentSpace:\n    normalImportMode: 1\n    normalSmoothingSource: 1\n",
    );
    let imported = ModelImporter::default()
        .import(&source, &std::fs::read(&source).unwrap(), &settings)
        .unwrap();
    assert!(
        imported.warnings.iter().any(|w| w.contains("weldVertices")),
        "{:?}",
        imported.warnings
    );
    assert_group_normals_match_reference(&imported);
}

#[test]
fn obj_material_cache_tracks_library_content_and_missing_inputs() {
    use crate::cache::{AssetCache, Outcome};
    let root = std::env::var_os("XDG_CACHE_HOME")
        .map(std::path::PathBuf::from)
        .unwrap_or_else(|| {
            std::path::PathBuf::from(std::env::var_os("HOME").unwrap()).join(".cache")
        })
        .join("unbound/validation")
        .join(format!("obj-cache-{}", std::process::id()));
    std::fs::create_dir_all(&root).unwrap();
    let source = root.join("Shape.obj");
    let library = root.join("color library.mtl");
    std::fs::write(&source, "mtllib color library.mtl # authored library\no Face\nv 0 0 0\nv 1 0 0\nv 0 1 0\nusemtl Paint\nf 1 2 3\n").unwrap();
    let cache = AssetCache::new(root.join("cache"));
    let load = || {
        cache.get_or_import_traced(
            &ModelImporter::default(),
            &source,
            &source.with_extension("obj.meta"),
        )
    };
    std::fs::write(&library, "newmtl Paint\nKd 0.2 0.4 0.8\nd 0.25\nTr 0.8\n").unwrap();
    let (model, outcome) = load().unwrap();
    assert_eq!(outcome, Outcome::Imported);
    assert_eq!(
        model.materials[0].description.vector("DiffuseColor"),
        Some([0.2, 0.4, 0.8, 1.0])
    );
    assert_eq!(
        model.materials[0].description.float("TransparencyFactor"),
        Some(0.75)
    );
    assert_eq!(load().unwrap().1, Outcome::Hit);
    std::fs::write(&library, "newmtl Paint\nKd 1 0 0\nTr 0.2\n").unwrap();
    let (model, outcome) = load().unwrap();
    assert_eq!(outcome, Outcome::Imported);
    assert_eq!(
        model.materials[0].description.vector("DiffuseColor"),
        Some([1.0, 0.0, 0.0, 1.0])
    );
    assert_eq!(
        model.materials[0].description.float("TransparencyFactor"),
        Some(0.2)
    );
    std::fs::remove_file(&library).unwrap();
    assert!(
        load().is_err(),
        "missing authored MTL must not reuse the cached colors"
    );
    std::fs::write(&library, "newmtl Paint\nmap_Kd Tint.png\n").unwrap();
    let material = &load().unwrap().0.materials[0].description;
    assert!(
        material
            .texture("DiffuseColor")
            .unwrap()
            .relative_path
            .ends_with("Tint.png"),
        "{material:?}"
    );
    let mut obj = std::fs::read_to_string(&source).unwrap();
    obj.push_str("usemtl Other\nf 1 3 2\n");
    std::fs::write(&source, obj).unwrap();
    std::fs::write(
        &library,
        "newmtl Paint\nmap_Kd A.png\nnewmtl Other\nmap_Kd B.png\n",
    )
    .unwrap();
    assert!(
        load()
            .unwrap_err()
            .to_string()
            .contains("multiple diffuse texture maps")
    );
    let bytes = std::fs::read(&source).unwrap();
    let disabled = ModelImporter::default()
        .import(
            &source,
            &bytes,
            &meta("ModelImporter:\n  materials:\n    materialImportMode: 0\n"),
        )
        .unwrap();
    assert_eq!(disabled.meshes[0].submeshes.len(), 2);
    let remap_meta = |names: &[&str]| {
        let mut text =
            "ModelImporter:\n  materials:\n    materialImportMode: 2\n  externalObjects:\n"
                .to_string();
        for name in names {
            text.push_str(&format!("  - first:\n      type: UnityEngine:Material\n      name: {name}\n    second: {{fileID: 2100000, guid: 286abb58d380806380ef7374c960f5c6, type: 2}}\n"));
        }
        meta(&text)
    };
    let remapped = ModelImporter::default()
        .import(&source, &bytes, &remap_meta(&["Paint", "Other"]))
        .unwrap();
    assert_eq!(remapped.meshes[0].submeshes, disabled.meshes[0].submeshes);
    assert!(remapped.materials.iter().all(|material| {
        material
            .remap
            .as_ref()
            .is_some_and(|reference| !reference.is_null())
    }));
    assert!(
        ModelImporter::default()
            .import(&source, &bytes, &remap_meta(&["Paint"]))
            .unwrap_err()
            .to_string()
            .contains("multiple diffuse texture maps")
    );
    drop(cache);
    std::fs::remove_dir_all(root).unwrap();
}

#[test]
fn baked_axis_conversion_rejects_unmeasured_deformers_and_animation() {
    let root = Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("../../tests/projects/modelimport/Assets/Models");
    let settings = meta("ModelImporter:\n  meshes:\n    bakeAxisConversion: 1\n");
    assert!(ModelSettings::read(&settings).bake_axis_conversion);
    assert!(!ModelSettings::read(&Value::Null).bake_axis_conversion);
    for filename in ["Animated.fbx", "Skinned.fbx"] {
        let source = root.join(filename);
        let bytes = std::fs::read(&source).unwrap();
        let error = ModelImporter::default()
            .import(&source, &bytes, &settings)
            .unwrap_err()
            .to_string();
        assert!(
            error.contains(
                "FBX Bake Axis Conversion for animation or deformers is not yet supported"
            ),
            "{error}"
        );
    }
}

/// Take spans and clip ends as the importer derives them (tests/projects/modelimport
/// `smoothing.py` takes: keys at 0.25, 0.5 and 0.75 s).
#[test]
fn take_spans_follow_local_times_and_keys() {
    let take = |name: &str| -> Option<(f64, f64, bool)> {
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join(format!(
            "../../tests/projects/modelimport/Assets/Resources/Smoothing/{name}.fbx"
        ));
        let scene = ufbx::load_file(path.to_str().unwrap(), ufbx::LoadOpts::default()).unwrap();
        let stack = &scene.anim_stacks[0];
        Take::new(&scene, stack).map(|t| (t.begin, t.end, t.empty))
    };
    let close = |a: Option<(f64, f64, bool)>, b: (f64, f64, bool)| {
        let a = a.expect("a clip");
        assert!(
            (a.0 - b.0).abs() < 1e-9 && (a.1 - b.1).abs() < 1e-9 && a.2 == b.2,
            "{a:?} != {b:?}"
        );
    };
    // Only a stop: from 0 to the last key before it.
    close(take("TakeStop"), (0.0, 0.75, false));
    close(take("TakeStopBetween"), (0.0, 0.6, false));
    // Only a start: from 0 to it.
    close(take("TakeStart"), (0.0, 0.5, false));
    // Start after stop: in order.
    close(take("TakeReversed"), (0.5, 0.75, false));
    // No span, an empty one or only the reference span: the keys' range.
    for name in ["TakeNone", "TakeEmpty", "TakeReference"] {
        close(take(name), (0.25, 0.75, false));
    }
    // A single key outside the (empty) span: an empty clip; no curves: no clip.
    close(take("TakeSingleKey"), (0.0, 0.0, true));
    assert!(take("TakeNoCurves").is_none() && take("TakeNoCurvesNoTime").is_none());
}
