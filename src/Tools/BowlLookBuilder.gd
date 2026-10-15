extends SceneTree
## Builds the Bowl (plan §7.3): the static arena with lightmap UVs, materials from the Poly Haven CC0 set and a
## LightmapGI ready for tools/dev/bake.ps1, in one of three lighting presets (M4):
##   afternoon  sun + the Alps HDRI (the M2 hero look)                               -> res://scenes/bowl/BowlLook.tscn
##   golden     low warm sun, procedural sky, mountain ridges, floodlights coming on -> BowlLook_Golden.tscn
##   night      moon, stars, four shadowed floodlight spots, emissive lamp heads     -> BowlLook_Night.tscn
## Run: Godot --headless --path <project> -s res://src/Tools/BowlLookBuilder.gd -- [--preset=golden] [--rebuild-meshes]
## The arena meshes under res://assets/bowl/meshes are shared by all presets and only written when missing (or
## with --rebuild-meshes): their lightmap UVs must not change under an existing bake. After --rebuild-meshes,
## rebake every preset.

const A := 55.0          # ellipse semi-axes of the arena floor edge (matches GreyBoxArena)
const B := 38.0
const SEG := 192
const PH := "res://assets/third_party/polyhaven/"
const OUT_MESH := "res://assets/bowl/meshes/"
const OUT_MAT := "res://assets/bowl/materials/"

const SCENES := {
	"afternoon": "res://scenes/bowl/BowlLook.tscn",
	"golden": "res://scenes/bowl/BowlLook_Golden.tscn",
	"night": "res://scenes/bowl/BowlLook_Night.tscn",
}

var arena: Node3D
var mats := {}
var preset := "afternoon"
var rebuild_meshes := false


func _init() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--preset="):
			preset = arg.substr("--preset=".length())
		elif arg == "--rebuild-meshes":
			rebuild_meshes = true
	if not SCENES.has(preset):
		push_error("unknown preset %s (afternoon, golden, night)" % preset)
		quit(1)
		return
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_MESH))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(OUT_MAT))
	arena = Node3D.new()
	arena.name = "BowlLook"
	arena.set_script(load("res://src/Tools/BowlLook.cs"))
	_materials()
	if preset == "afternoon":
		_environment()
	else:
		_environment_procedural()
		_mountains()
	_floor()
	_outside()
	_wall()
	_fence()
	for side in [-1.0, 1.0]:
		_grandstand(side)
	for sx in [-1.0, 1.0]:
		for sz in [-1.0, 1.0]:
			_floodlight(Vector3(sx * 49.0, 0.0, sz * 44.0))
	_scoreboard()
	_lightmap()
	var packed := PackedScene.new()
	var err := packed.pack(arena)
	if err != OK:
		push_error("pack failed: %s" % err)
		quit(1)
		return
	err = ResourceSaver.save(packed, SCENES[preset])
	print("[BowlLookBuilder] saved %s (%s)" % [SCENES[preset], error_string(err)])
	quit(0 if err == OK else 1)


# ------------------------------------------------------------------------------------------- helpers

func _own(n: Node) -> void:
	n.owner = arena
	for c in n.get_children():
		_own(c)


func _add(n: Node, parent: Node = null) -> Node:
	(parent if parent != null else arena).add_child(n)
	_own(n)
	return n


func _tex(path: String) -> Texture2D:
	return load(PH + path)


func _pbr(name: String, folder: String, scale: float, tint := Color.WHITE, metal_from_orm := false) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.resource_name = name
	m.albedo_texture = _tex("%s/%s_diff_2k.jpg" % [folder, folder])
	m.albedo_color = tint
	m.normal_enabled = true
	m.normal_texture = _tex("%s/%s_nor_gl_2k.jpg" % [folder, folder])
	m.orm_texture = _tex("%s/%s_arm_2k.jpg" % [folder, folder])
	if not metal_from_orm:
		m.metallic = 0.0
	m.uv1_triplanar = true
	m.uv1_world_triplanar = true
	m.uv1_scale = Vector3.ONE * scale
	m.uv1_triplanar_sharpness = 4.0
	return _save_mat(name, m)


func _flat(name: String, color: Color, rough: float, metal := 0.0) -> StandardMaterial3D:
	var m := StandardMaterial3D.new()
	m.resource_name = name
	m.albedo_color = color
	m.roughness = rough
	m.metallic = metal
	return _save_mat(name, m)


func _save_mat(name: String, m: Material) -> Material:
	var path := OUT_MAT + name + ".tres"
	ResourceSaver.save(m, path)
	var loaded: Material = load(path)
	mats[name] = loaded
	return loaded


func _materials() -> void:
	_pbr("sand", "gravelly_sand", 0.28, Color(0.95, 0.9, 0.82))
	_pbr("mud", "brown_mud_dry", 0.22, Color(0.85, 0.85, 0.75))
	_pbr("concrete", "concrete_wall_008", 0.45)
	_pbr("corrugated", "corrugated_iron", 0.35, Color(0.85, 0.85, 0.85), true)
	(mats["corrugated"] as StandardMaterial3D).cull_mode = BaseMaterial3D.CULL_DISABLED
	ResourceSaver.save(mats["corrugated"], OUT_MAT + "corrugated.tres")
	_flat("rubber", Color(0.025, 0.025, 0.027), 0.9)
	_flat("steel", Color(0.55, 0.56, 0.57), 0.45, 0.8)
	_flat("paint_white", Color(0.82, 0.82, 0.8), 0.6)
	_flat("paint_red", Color(0.55, 0.06, 0.05), 0.55)
	_flat("wood", Color(0.42, 0.33, 0.22), 0.8)
	var lamp := _flat("lamp", Color(0.9, 0.9, 0.85), 0.2)
	lamp.emission_enabled = true
	lamp.emission = Color(1.0, 0.95, 0.85)
	lamp.emission_energy_multiplier = 0.0
	ResourceSaver.save(lamp, OUT_MAT + "lamp.tres")
	# Lit lamp heads for the golden-hour and night presets (glow picks them up).
	for p in [["lamp_golden", 5.0], ["lamp_night", 14.0]]:
		var lit := _flat(p[0], Color(0.95, 0.95, 0.9), 0.2)
		lit.emission_enabled = true
		lit.emission = Color(1.0, 0.94, 0.82)
		lit.emission_energy_multiplier = p[1]
		ResourceSaver.save(lit, OUT_MAT + p[0] + ".tres")
	_flat("rock", Color(0.16, 0.17, 0.15), 1.0)
	_flat("rock_far", Color(0.20, 0.22, 0.24), 1.0)
	_flat("board", Color(0.02, 0.02, 0.025), 0.4)
	var ground := ShaderMaterial.new()
	ground.resource_name = "ground"
	ground.shader = load("res://assets/shaders/arena_ground.gdshader")
	for k in ["albedo", "normal", "orm"]:
		var suffix: String = {"albedo": "diff_2k.jpg", "normal": "nor_gl_2k.jpg", "orm": "arm_2k.jpg"}[k]
		ground.set_shader_parameter("sand_" + k, _tex("gravelly_sand/gravelly_sand_" + suffix))
		ground.set_shader_parameter("mud_" + k, _tex("brown_mud_dry/brown_mud_dry_" + suffix))
	# Every uniform explicitly: a material saved from a headless run otherwise gets null defaults when the
	# editor re-saves it (e.g. during the lightmap bake), and a null tint renders black.
	ground.set_shader_parameter("sand_scale", 0.32)
	ground.set_shader_parameter("mud_scale", 0.22)
	ground.set_shader_parameter("sand_tint", Color(0.78, 0.72, 0.64))
	ground.set_shader_parameter("mud_tint", Color(0.72, 0.68, 0.62))
	ground.set_shader_parameter("arena_axes", Vector2(A, B))
	ground.set_shader_parameter("rut_strength", 0.6)
	_save_mat("ground", ground)
	var fence := ShaderMaterial.new()
	fence.resource_name = "fence"
	fence.shader = load("res://assets/shaders/chain_link.gdshader")
	fence.set_shader_parameter("cell", 0.065)
	fence.set_shader_parameter("wire", 0.07)
	fence.set_shader_parameter("colour", Color(0.52, 0.53, 0.54))
	_save_mat("fence", fence)


func _environment() -> void:
	var sky_mat := PanoramaSkyMaterial.new()
	sky_mat.panorama = _tex("alps_field/alps_field_4k.hdr")
	var sky := Sky.new()
	sky.sky_material = sky_mat
	sky.radiance_size = Sky.RADIANCE_SIZE_1024
	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	env.sky = sky
	env.sky_rotation = Vector3(0, deg_to_rad(-60.0), 0)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
	env.tonemap_mode = Environment.TONE_MAPPER_AGX
	env.tonemap_exposure = 0.9
	env.ssao_enabled = true
	env.ssao_radius = 1.2
	env.ssao_intensity = 1.8
	env.ssil_enabled = true
	env.glow_enabled = true
	env.glow_intensity = 0.3
	env.fog_enabled = true
	env.fog_light_color = Color(0.72, 0.76, 0.82)
	env.fog_density = 0.0012
	env.fog_aerial_perspective = 0.55
	env.fog_sky_affect = 0.15
	var we := WorldEnvironment.new()
	we.name = "WorldEnvironment"
	we.environment = env
	_add(we)

	# The alps_field sun sits at longitude ~36 deg, elevation ~42 deg in the panorama; rotate with the sky.
	var lon := deg_to_rad(36.0)
	var lat := deg_to_rad(42.0)
	var to_sun := Vector3(sin(lon) * cos(lat), sin(lat), -cos(lon) * cos(lat)).rotated(Vector3.UP, deg_to_rad(-60.0))
	var sun := DirectionalLight3D.new()
	sun.name = "Sun"
	sun.transform = Transform3D(Basis.looking_at(-to_sun, Vector3.UP), to_sun * 50.0)
	sun.light_energy = 2.4
	sun.light_color = Color(1.0, 0.95, 0.88)
	sun.light_angular_distance = 0.6
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 180.0
	sun.light_bake_mode = Light3D.BAKE_DYNAMIC
	_add(sun)


func _ellipse(t: float, off: float) -> Vector3:
	# Point on the ellipse pushed `off` metres outward along its normal.
	var p := Vector3(A * cos(t), 0.0, B * sin(t))
	var n := Vector3(cos(t) / A, 0.0, sin(t) / B).normalized()
	return p + n * off


func _mesh_instance(name: String, mesh: ArrayMesh, mat: String, texel := 0.25) -> MeshInstance3D:
	var path := OUT_MESH + name + ".res"
	if rebuild_meshes or not ResourceLoader.exists(path):
		mesh.surface_set_material(0, mats[mat])
		var err := mesh.lightmap_unwrap(Transform3D.IDENTITY, texel)
		if err != OK:
			push_warning("lightmap_unwrap %s: %s" % [name, error_string(err)])
		ResourceSaver.save(mesh, path)
	var mi := MeshInstance3D.new()
	mi.name = name
	mi.mesh = load(OUT_MESH + name + ".res")
	mi.gi_mode = GeometryInstance3D.GI_MODE_STATIC
	return mi


func _sweep(profile: Array, closed := true) -> ArrayMesh:
	# Sweeps a (offset, y) profile around the ellipse.
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for i in SEG:
		var t0 := TAU * i / SEG
		var t1 := TAU * (i + 1) / SEG
		for k in profile.size() - 1:
			var a: Vector2 = profile[k]
			var b: Vector2 = profile[k + 1]
			var p00 := _ellipse(t0, a.x) + Vector3.UP * a.y
			var p01 := _ellipse(t0, b.x) + Vector3.UP * b.y
			var p10 := _ellipse(t1, a.x) + Vector3.UP * a.y
			var p11 := _ellipse(t1, b.x) + Vector3.UP * b.y
			# Godot front faces are clockwise as seen from the viewer.
			st.add_vertex(p00); st.add_vertex(p11); st.add_vertex(p10)
			st.add_vertex(p00); st.add_vertex(p01); st.add_vertex(p11)
	st.index()
	st.generate_normals()
	return st.commit()


func _box_mesh(size: Vector3) -> ArrayMesh:
	var bm := BoxMesh.new()
	bm.size = size
	var am := ArrayMesh.new()
	am.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, bm.get_mesh_arrays())
	return am


func _cyl_mesh(r: float, h: float) -> ArrayMesh:
	var cm := CylinderMesh.new()
	cm.top_radius = r
	cm.bottom_radius = r
	cm.height = h
	cm.radial_segments = 16
	var am := ArrayMesh.new()
	am.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, cm.get_mesh_arrays())
	return am


# ------------------------------------------------------------------------------------------- pieces

func _floor() -> void:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var rings := 6
	for r in rings:
		var s0 := float(r) / rings
		var s1 := float(r + 1) / rings
		for i in SEG:
			var t0 := TAU * i / SEG
			var t1 := TAU * (i + 1) / SEG
			var a := Vector3(A * cos(t0), 0, B * sin(t0))
			var b := Vector3(A * cos(t1), 0, B * sin(t1))
			st.set_normal(Vector3.UP)
			if r == 0:
				st.add_vertex(Vector3.ZERO); st.add_vertex(a * s1); st.add_vertex(b * s1)
			else:
				st.add_vertex(a * s0); st.add_vertex(b * s1); st.add_vertex(b * s0)
				st.add_vertex(a * s0); st.add_vertex(a * s1); st.add_vertex(b * s1)
	var mi := _mesh_instance("Floor", st.commit(), "ground", 0.35)
	_add(mi)


func _outside() -> void:
	# Ground outside the wall, out to the horizon.
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var r0 := 0.7
	var r1 := 260.0
	for i in 96:
		var t0 := TAU * i / 96
		var t1 := TAU * (i + 1) / 96
		var a0 := _ellipse(t0, r0)
		var b0 := _ellipse(t1, r0)
		var a1 := Vector3(cos(t0), 0, sin(t0)) * r1
		var b1 := Vector3(cos(t1), 0, sin(t1)) * r1
		st.set_normal(Vector3.UP)
		st.add_vertex(a0); st.add_vertex(a1); st.add_vertex(b1)
		st.add_vertex(a0); st.add_vertex(b1); st.add_vertex(b0)
	var mi := _mesh_instance("Outside", st.commit(), "mud", 2.0)
	mi.position.y = -0.01
	_add(mi)


func _wall() -> void:
	# Concrete wall: sloped inner face, rounded cap, vertical back (inner face at the floor edge).
	var prof := [Vector2(0.0, 0.0), Vector2(0.22, 1.25), Vector2(0.27, 1.38), Vector2(0.34, 1.42),
		Vector2(0.62, 1.42), Vector2(0.68, 1.36), Vector2(0.7, 0.0)]
	_add(_mesh_instance("Wall", _sweep(prof), "concrete", 0.2))
	# A red-and-white kerb stripe painted along the foot of the wall.
	var kerb := [Vector2(-0.02, 0.003), Vector2(0.06, 0.35)]
	_add(_mesh_instance("WallStripe", _sweep(kerb), "paint_white", 0.3))


func _fence() -> void:
	# Chain-link catch fence on the wall cap, with steel posts every 3 m.
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for i in SEG:
		var t0 := TAU * i / SEG
		var t1 := TAU * (i + 1) / SEG
		var a := _ellipse(t0, 0.48) + Vector3.UP * 1.42
		var b := _ellipse(t1, 0.48) + Vector3.UP * 1.42
		var h := Vector3.UP * 2.6
		var u0 := float(i) / SEG * 300.0
		var u1 := float(i + 1) / SEG * 300.0
		st.set_uv(Vector2(u0, 0)); st.add_vertex(a)
		st.set_uv(Vector2(u1, 0)); st.add_vertex(b)
		st.set_uv(Vector2(u1, 2.6)); st.add_vertex(b + h)
		st.set_uv(Vector2(u0, 0)); st.add_vertex(a)
		st.set_uv(Vector2(u1, 2.6)); st.add_vertex(b + h)
		st.set_uv(Vector2(u0, 2.6)); st.add_vertex(a + h)
	st.generate_normals()
	var mesh := st.commit()
	mesh.surface_set_material(0, mats["fence"])
	ResourceSaver.save(mesh, OUT_MESH + "Fence.res")
	var mi := MeshInstance3D.new()
	mi.name = "Fence"
	mi.mesh = load(OUT_MESH + "Fence.res")
	mi.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	_add(mi)
	# Fence posts and the tyre barrier are MultiMeshes built at runtime by BowlLook.cs (a headless save
	# would drop their instance buffers).


func _grandstand(side: float) -> void:
	# Stepped concrete terraces behind the long walls, timber benches, corrugated roof on steel columns.
	var stand := Node3D.new()
	stand.name = "Grandstand_%s" % ("N" if side < 0 else "S")
	_add(stand)
	var length := 46.0
	var rows := 12
	var z0 := B + 3.2
	for k in rows:
		var step := _mesh_instance("Terrace_%s_%d" % [stand.name, k], _box_mesh(Vector3(length, 0.42 * (k + 1), 0.8)), "concrete", 0.3)
		step.position = Vector3(0, 0.21 * (k + 1), side * (z0 + 0.8 * k))
		_add(step, stand)
		var bench := _mesh_instance("Bench_%s_%d" % [stand.name, k], _box_mesh(Vector3(length - 1.0, 0.06, 0.32)), "wood", 0.3)
		bench.position = Vector3(0, 0.42 * (k + 1) + 0.03, side * (z0 + 0.8 * k + 0.12))
		_add(bench, stand)
	var back_z := side * (z0 + 0.8 * rows + 0.3)
	var back := _mesh_instance("BackWall_%s" % stand.name, _box_mesh(Vector3(length, 8.0, 0.3)), "corrugated", 0.3)
	back.position = Vector3(0, 4.0, back_z)
	_add(back, stand)
	# Roof: tilted sheet over the terraces.
	var depth := 0.8 * rows + 2.5
	var roof := _mesh_instance("Roof_%s" % stand.name, _box_mesh(Vector3(length + 2.0, 0.08, depth)), "corrugated", 0.4)
	roof.position = Vector3(0, 9.4, side * (z0 + depth * 0.5 - 1.4))
	roof.rotation = Vector3(side * deg_to_rad(-6.0), 0, 0)
	_add(roof, stand)
	for c in 7:
		var x := -length * 0.5 + 2.0 + c * (length - 4.0) / 6.0
		for zz in [z0 + 0.2, z0 + 0.8 * rows]:
			var col := _mesh_instance("Col_%s_%d_%d" % [stand.name, c, int(zz)], _cyl_mesh(0.14, 9.4), "steel", 0.3)
			col.position = Vector3(x, 4.7, side * zz)
			_add(col, stand)


func _floodlight(at: Vector3) -> void:
	var tower := Node3D.new()
	tower.name = "Floodlight_%d_%d" % [int(at.x), int(at.z)]
	_add(tower)
	tower.position = at
	var pole := _mesh_instance("Pole_%s" % tower.name, _cyl_mesh(0.32, 26.0), "steel", 0.3)
	pole.position = Vector3(0, 13.0, 0)
	_add(pole, tower)
	var head := Node3D.new()
	head.name = "Head"
	var aim := (Vector3(0, 0, 0) - (at + Vector3(0, 26.5, 0))).normalized()
	head.transform = Transform3D(Basis.looking_at(aim, Vector3.UP), Vector3(0, 26.5, 0))
	_add(head, tower)
	var frame := _mesh_instance("Frame_%s" % tower.name, _box_mesh(Vector3(5.0, 2.6, 0.35)), "steel", 0.3)
	_add(frame, head)
	for r in 3:
		for c in 5:
			var lamp := MeshInstance3D.new()
			lamp.name = "Lamp_%d_%d" % [r, c]
			var lm := BoxMesh.new()
			lm.size = Vector3(0.75, 0.6, 0.12)
			lm.material = mats["lamp"]
			lamp.mesh = lm
			lamp.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
			lamp.position = Vector3(-2.0 + c * 1.0, -0.85 + r * 0.85, -0.22)
			if preset != "afternoon":
				lamp.material_override = mats["lamp_" + preset]
			_add(lamp, head)
	if preset != "afternoon":
		# One shadowed spot per tower lights the floor and the cars; its indirect light is baked.
		var spot := SpotLight3D.new()
		spot.name = "Flood"
		spot.position = Vector3(0, 0, -0.6)
		spot.light_color = Color(1.0, 0.93, 0.8)
		spot.light_energy = 13.0 if preset == "night" else 4.0
		spot.light_indirect_energy = 1.0
		spot.light_volumetric_fog_energy = 0.6 if preset == "night" else 0.0
		spot.spot_range = 170.0
		spot.spot_attenuation = 0.85
		spot.spot_angle = 48.0
		spot.spot_angle_attenuation = 1.4
		spot.light_size = 1.2
		spot.shadow_enabled = true
		spot.shadow_bias = 0.04
		spot.shadow_normal_bias = 1.2
		spot.light_bake_mode = Light3D.BAKE_DYNAMIC
		_add(spot, head)


func _scoreboard() -> void:
	var sb := Node3D.new()
	sb.name = "Scoreboard"
	_add(sb)
	sb.position = Vector3(A + 9.0, 0, 0)
	sb.rotation = Vector3(0, deg_to_rad(-90.0), 0)
	for lx in [-4.0, 4.0]:
		var leg := _mesh_instance("ScoreLeg_%d" % int(lx), _cyl_mesh(0.3, 7.0), "steel", 0.3)
		leg.position = Vector3(lx, 3.5, 0)
		_add(leg, sb)
	var box := _mesh_instance("ScoreBox", _box_mesh(Vector3(11.0, 5.0, 0.8)), "paint_red", 0.3)
	box.position = Vector3(0, 9.5, 0)
	_add(box, sb)
	var face := _mesh_instance("ScoreFace", _box_mesh(Vector3(10.2, 4.2, 0.1)), "board", 0.3)
	face.position = Vector3(0, 9.5, 0.42)
	_add(face, sb)
	var title := Label3D.new()
	title.name = "Title"
	title.text = "DESTRUCTION DERBY"
	title.font_size = 160
	title.pixel_size = 0.006
	title.modulate = Color(1.0, 0.82, 0.2)
	title.outline_size = 0
	title.position = Vector3(0, 10.6, 0.48)
	_add(title, sb)
	var sub := Label3D.new()
	sub.name = "Info"
	sub.text = "THE BOWL   CARS LEFT 20"
	sub.font_size = 110
	sub.pixel_size = 0.006
	sub.modulate = Color(0.95, 0.25, 0.15)
	sub.outline_size = 0
	sub.position = Vector3(0, 8.6, 0.48)
	_add(sub, sb)


func _environment_procedural() -> void:
	# Golden hour and night: our sky shader (gradient, light glow and disk, clouds, stars) instead of the HDRI,
	# so the sun can sit low and the night can be dark. The mountains are geometry (_mountains).
	var golden := preset == "golden"
	var sky_mat := ShaderMaterial.new()
	sky_mat.shader = load("res://assets/shaders/bowl_sky.gdshader")
	var p := {}
	if golden:
		p = {"zenith_color": Color(0.05, 0.13, 0.40), "horizon_color": Color(1.0, 0.55, 0.25),
			"mid_color": Color(0.85, 0.78, 0.6), "mid_height": 0.08,
			"ground_color": Color(0.10, 0.07, 0.05), "horizon_power": 0.45,
			"glow_color": Color(1.0, 0.5, 0.2), "glow_strength": 1.4, "glow_tightness": 5.0,
			"disk_color": Color(1.0, 0.78, 0.5), "disk_size": 0.03, "disk_energy": 40.0,
			"cloud_cover": 0.38, "cloud_lit": Color(1.0, 0.62, 0.36), "cloud_shade": Color(0.26, 0.22, 0.27),
			"star_density": 0.0, "energy": 0.9}
	else:
		p = {"zenith_color": Color(0.003, 0.005, 0.013), "horizon_color": Color(0.035, 0.034, 0.045),
			"mid_color": Color(0.012, 0.016, 0.03), "mid_height": 0.12,
			"ground_color": Color(0.006, 0.006, 0.008), "horizon_power": 0.5,
			"glow_color": Color(0.35, 0.42, 0.6), "glow_strength": 0.08, "glow_tightness": 18.0,
			"disk_color": Color(0.9, 0.93, 1.0), "disk_size": 0.012, "disk_energy": 6.0,
			"cloud_cover": 0.3, "cloud_lit": Color(0.07, 0.08, 0.11), "cloud_shade": Color(0.015, 0.017, 0.025),
			"star_density": 0.012, "energy": 1.0}
	for k in p:
		sky_mat.set_shader_parameter(k, p[k])
	var sky := Sky.new()
	sky.sky_material = sky_mat
	sky.radiance_size = Sky.RADIANCE_SIZE_512
	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
	env.tonemap_mode = Environment.TONE_MAPPER_AGX
	env.tonemap_exposure = 1.0 if golden else 1.0
	env.ssao_enabled = true
	env.ssao_radius = 1.2
	env.ssao_intensity = 1.8
	env.ssil_enabled = true
	env.glow_enabled = true
	env.glow_intensity = 0.45 if golden else 0.8
	env.glow_bloom = 0.0 if golden else 0.04
	env.glow_hdr_threshold = 1.0
	env.fog_enabled = true
	env.fog_light_color = Color(0.85, 0.6, 0.42) if golden else Color(0.03, 0.035, 0.05)
	env.fog_density = 0.00042 if golden else 0.0009
	env.fog_aerial_perspective = 0.6 if golden else 0.3
	env.fog_sun_scatter = 0.25 if golden else 0.0
	env.fog_sky_affect = 0.1
	# Haze in the air: god rays from the low sun, beams from the floodlights at night.
	env.volumetric_fog_enabled = true
	env.volumetric_fog_density = 0.0009 if golden else 0.0012
	env.volumetric_fog_albedo = Color(0.9, 0.82, 0.72) if golden else Color(0.75, 0.75, 0.78)
	env.volumetric_fog_anisotropy = 0.55 if golden else 0.4
	env.volumetric_fog_length = 140.0
	env.volumetric_fog_temporal_reprojection_amount = 0.5
	env.volumetric_fog_sky_affect = 0.0
	env.volumetric_fog_ambient_inject = 0.0
	var we := WorldEnvironment.new()
	we.name = "WorldEnvironment"
	we.environment = env
	_add(we)

	var light := DirectionalLight3D.new()
	if golden:
		# Low in the west (-X), a little to the south: long shadows across the floor, the stands half lit.
		light.name = "Sun"
		var to_sun := Vector3(-0.92, 0.0, 0.37).normalized() * cos(deg_to_rad(11.0)) + Vector3.UP * sin(deg_to_rad(11.0))
		light.transform = Transform3D(Basis.looking_at(-to_sun, Vector3.UP), to_sun * 50.0)
		light.light_energy = 3.6
		light.light_color = Color(1.0, 0.64, 0.36)
		light.light_angular_distance = 0.8
		light.light_volumetric_fog_energy = 1.0
		light.shadow_enabled = true
		light.directional_shadow_max_distance = 180.0
	else:
		light.name = "Moon"
		var to_moon := Vector3(0.45, 0.0, -0.6).normalized() * cos(deg_to_rad(38.0)) + Vector3.UP * sin(deg_to_rad(38.0))
		light.transform = Transform3D(Basis.looking_at(-to_moon, Vector3.UP), to_moon * 50.0)
		light.light_energy = 0.07
		light.light_color = Color(0.62, 0.72, 1.0)
		light.light_volumetric_fog_energy = 0.0
		light.shadow_enabled = false
	light.light_bake_mode = Light3D.BAKE_DYNAMIC
	_add(light)


func _mountains() -> void:
	# Two rings of ridges far behind the arena (DD1's mountain backdrop): a strip that rises from below the
	# horizon to a noisy ridge line, faceted by height noise. Not shadowed or baked; fog gives the depth.
	var layers := [
		{"name": "MountainsNear", "r": 1350.0, "depth": 350.0, "h": 150.0, "seed": 11, "mat": "rock"},
		{"name": "MountainsFar", "r": 2300.0, "depth": 500.0, "h": 330.0, "seed": 23, "mat": "rock_far"},
	]
	for layer in layers:
		var noise := FastNoiseLite.new()
		noise.seed = layer["seed"]
		noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
		noise.fractal_type = FastNoiseLite.FRACTAL_RIDGED
		noise.fractal_octaves = 5
		noise.frequency = 1.0
		var detail := FastNoiseLite.new()
		detail.seed = layer["seed"] + 100
		detail.frequency = 0.02
		detail.fractal_octaves = 3
		var steps := 900
		var rows := 7
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		var grid := []
		for j in rows:
			var t := float(j) / (rows - 1)
			var row := []
			for i in steps + 1:
				var ang := TAU * i / steps
				var dir := Vector3(cos(ang), 0, sin(ang))
				# Ridge height: ridged noise sampled on a circle, so the ring closes seamlessly.
				var ridge: float = 0.5 + 0.5 * noise.get_noise_2d(cos(ang) * 1.6, sin(ang) * 1.6)
				ridge = 0.2 + 0.8 * pow(ridge, 1.6)
				var r: float = layer["r"] + t * layer["depth"]
				var y: float = lerp(-40.0, ridge * layer["h"], pow(t, 0.8))
				var q := dir * r
				y += detail.get_noise_2d(q.x, q.z) * layer["h"] * 0.12 * t * (1.0 - t) * 4.0
				row.append(dir * r + Vector3.UP * y)
			grid.append(row)
		for j in rows - 1:
			for i in steps:
				var a: Vector3 = grid[j][i]
				var b: Vector3 = grid[j][i + 1]
				var c: Vector3 = grid[j + 1][i]
				var d: Vector3 = grid[j + 1][i + 1]
				# Faces point inwards, at the arena (clockwise as seen from the centre).
				st.add_vertex(a); st.add_vertex(c); st.add_vertex(b)
				st.add_vertex(b); st.add_vertex(c); st.add_vertex(d)
		st.generate_normals()
		var mesh := st.commit()
		mesh.surface_set_material(0, mats[layer["mat"]])
		var path: String = OUT_MESH + layer["name"] + ".res"
		ResourceSaver.save(mesh, path)
		var mi := MeshInstance3D.new()
		mi.name = layer["name"]
		mi.mesh = load(path)
		mi.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
		mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		_add(mi)


func _lightmap() -> void:
	var lm := LightmapGI.new()
	lm.name = "LightmapGI"
	lm.quality = LightmapGI.BAKE_QUALITY_MEDIUM
	lm.bounces = 3
	lm.use_denoiser = true
	lm.directional = true
	lm.max_texture_size = 8192
	lm.texel_scale = 1.0
	lm.generate_probes_subdiv = LightmapGI.GENERATE_PROBES_SUBDIV_8
	lm.environment_mode = LightmapGI.ENVIRONMENT_MODE_SCENE
	_add(lm)
