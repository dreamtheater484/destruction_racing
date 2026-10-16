extends SceneTree
## Builds the look of Crossroads Speedway (plan §7.3) from tools/tracksmith's data (assets/speedway/crossroads.json
## and its rasters): the terrain in tiles with lightmap UVs and the splat-blended ground shader, the outer concrete
## wall with its catch fence, the painted infield walls, edge lines, kerbs, start lines and pit boxes, the
## grandstands, pit garages, start gantries, the billboards that make the crossover blind, floodlights and infield
## dressing, and a LightmapGI ready for tools/dev/bake.ps1, in one of three lighting presets:
##   afternoon  sun + the golden_gate_hills HDRI                              -> res://scenes/speedway/SpeedwayLook.tscn
##   golden     low warm sun, procedural sky, hills, floodlights coming on   -> SpeedwayLook_Golden.tscn
##   night      moon, stars, shadowed floodlights, emissive lamp heads       -> SpeedwayLook_Night.tscn
## Run: Godot --headless --path <project> -s res://src/Tools/SpeedwayLookBuilder.gd -- [--preset=golden] [--rebuild-meshes]
## Meshes under res://assets/speedway/meshes are shared by the presets and only written when missing (or with
## --rebuild-meshes): their lightmap UVs must not change under an existing bake. After --rebuild-meshes, rebake all.
## The layout-specific barriers, the fence posts, the crowd and the trees are added at runtime (SpeedwayLook.cs).

const DATA := "res://assets/speedway/crossroads.json"
const PH := "res://assets/third_party/polyhaven/"
const OUT_MESH := "res://assets/speedway/meshes/"
const OUT_MAT := "res://assets/speedway/materials/"
const SCENES := {
	"afternoon": "res://scenes/speedway/SpeedwayLook.tscn",
	"golden": "res://scenes/speedway/SpeedwayLook_Golden.tscn",
	"night": "res://scenes/speedway/SpeedwayLook_Night.tscn",
}
const WALL_H := 1.2
const FENCE_H := 3.0
const TILE := 64            # terrain tile size in 1 m cells
const PAINT_Y := 0.012      # paint sits this far above the ground

var look_root: Node3D
var mats := {}
var preset := "afternoon"
var rebuild_meshes := false
var data: Dictionary
var hf: Dictionary
var heights: PackedFloat32Array
var surf: Dictionary
var codes: PackedByteArray
var paint := SurfaceTool.new()
var paint_count := 0


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
	for d in [OUT_MESH, OUT_MAT, "res://scenes/speedway/"]:
		DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(d))
	_load_data()
	look_root = Node3D.new()
	look_root.name = "SpeedwayLook"
	look_root.set_script(load("res://src/Tools/SpeedwayLook.cs"))
	_materials()
	if preset == "afternoon":
		_environment()
	else:
		_environment_procedural()
		_hills()
	_terrain()
	_outside()
	_outer_wall()
	_islands()
	_paint_all()
	_grandstand("MainStand", Vector3(0, 0, 79.6), 0.0, 132.0, 14)
	_grandstand("BackStand", Vector3(0, 0, -79.6), PI, 84.0, 10)
	_pit_garages()
	_gantries()
	_billboards()
	_floodlights()
	_infield()
	_lightmap()
	var packed := PackedScene.new()
	var err := packed.pack(look_root)
	if err != OK:
		push_error("pack failed: %s" % err)
		quit(1)
		return
	err = ResourceSaver.save(packed, SCENES[preset])
	print("[SpeedwayLookBuilder] saved %s (%s)" % [SCENES[preset], error_string(err)])
	quit(0 if err == OK else 1)


# ------------------------------------------------------------------------------------------- data

func _load_data() -> void:
	data = JSON.parse_string(FileAccess.get_file_as_string(DATA))
	hf = data["heightfield"]
	heights = FileAccess.get_file_as_bytes(hf["file"]).to_float32_array()
	surf = data["surface"]
	codes = FileAccess.get_file_as_bytes(surf["file"])


func _h(x: float, z: float) -> float:
	var nx: int = hf["nx"]
	var nz: int = hf["nz"]
	var u := clampf((x - hf["x0"]) / hf["cell"], 0.0, nx - 1.001)
	var v := clampf((z - hf["z0"]) / hf["cell"], 0.0, nz - 1.001)
	var i := int(u)
	var j := int(v)
	var fu := u - i
	var fv := v - j
	var a := heights[j * nx + i]
	var b := heights[j * nx + i + 1]
	var c := heights[(j + 1) * nx + i]
	var d := heights[(j + 1) * nx + i + 1]
	return lerpf(lerpf(a, b, fu), lerpf(c, d, fu), fv)


func _code(x: float, z: float) -> int:
	var i := clampi(int(round((x - surf["x0"]) / surf["cell"])), 0, int(surf["nx"]) - 1)
	var j := clampi(int(round((z - surf["z0"]) / surf["cell"])), 0, int(surf["nz"]) - 1)
	return codes[j * int(surf["nx"]) + i]


func _normal(x: float, z: float) -> Vector3:
	var e := 0.5
	return Vector3(_h(x - e, z) - _h(x + e, z), 2.0 * e, _h(x, z - e) - _h(x, z + e)).normalized()


func _line(key_path: Array) -> PackedVector2Array:
	var v: Variant = data
	for k in key_path:
		v = v[k]
	var out := PackedVector2Array()
	for p in v:
		out.append(Vector2(p[0], p[1]))
	return out


func _v3(p: Vector2, lift := 0.0) -> Vector3:
	return Vector3(p.x, _h(p.x, p.y) + lift, p.y)


## Left of a polyline (the pavement side; walls stand to the right): Godot's top view, x right, z down.
func _left(d: Vector2) -> Vector2:
	return Vector2(d.y, -d.x)


## Per-vertex direction of a polyline (averaged at the joints).
func _dir_at(pts: PackedVector2Array, i: int, closed: bool) -> Vector2:
	var n := pts.size()
	var a := pts[(i - 1 + n) % n] if (closed or i > 0) else pts[i]
	var b := pts[(i + 1) % n] if (closed or i < n - 1) else pts[i]
	var d := b - a
	return d.normalized() if d.length() > 1e-4 else Vector2.RIGHT


# ------------------------------------------------------------------------------------------- helpers

func _own(n: Node) -> void:
	n.owner = look_root
	for c in n.get_children():
		_own(c)


func _add(n: Node, parent: Node = null) -> Node:
	(parent if parent != null else look_root).add_child(n)
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


func _mesh_instance(name: String, build: Callable, mat: String, texel := 0.25, gi := true) -> MeshInstance3D:
	## The mesh is built (by `build`) and unwrapped only when its file is missing or --rebuild-meshes is given.
	var path := OUT_MESH + name + ".res"
	if rebuild_meshes or not ResourceLoader.exists(path):
		var mesh: ArrayMesh = build.call()
		for s in mesh.get_surface_count():
			if mesh.surface_get_material(s) == null:
				mesh.surface_set_material(s, mats[mat])
		if gi:
			var err := mesh.lightmap_unwrap(Transform3D.IDENTITY, texel)
			if err != OK:
				push_warning("lightmap_unwrap %s: %s" % [name, error_string(err)])
		ResourceSaver.save(mesh, path)
	var mi := MeshInstance3D.new()
	mi.name = name
	mi.mesh = load(path)
	mi.gi_mode = GeometryInstance3D.GI_MODE_STATIC if gi else GeometryInstance3D.GI_MODE_DYNAMIC
	return mi


func _box_mesh(size: Vector3) -> ArrayMesh:
	var bm := BoxMesh.new()
	bm.size = size
	var am := ArrayMesh.new()
	am.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, bm.get_mesh_arrays())
	return am


func _cyl_mesh(r: float, h: float, seg := 12) -> ArrayMesh:
	var cm := CylinderMesh.new()
	cm.top_radius = r
	cm.bottom_radius = r
	cm.height = h
	cm.radial_segments = seg
	var am := ArrayMesh.new()
	am.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, cm.get_mesh_arrays())
	return am


func _box(name: String, size: Vector3, xf: Transform3D, mat: String, parent: Node = null, texel := 0.3) -> MeshInstance3D:
	var mi := _mesh_instance(name, func(): return _box_mesh(size), mat, texel)
	mi.transform = xf
	_add(mi, parent)
	return mi


func _quad(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3, d: Vector3) -> void:
	## a-b-c-d clockwise as seen from the front (Godot's front faces are clockwise).
	st.add_vertex(a); st.add_vertex(b); st.add_vertex(c)
	st.add_vertex(a); st.add_vertex(c); st.add_vertex(d)


# ------------------------------------------------------------------------------------------- materials

func _materials() -> void:
	_pbr("concrete", "concrete_wall_008", 0.45)
	_pbr("concrete_dark", "concrete_wall_008", 0.45, Color(0.62, 0.62, 0.62))
	_pbr("corrugated", "corrugated_iron", 0.35, Color(0.85, 0.85, 0.85), true)
	(mats["corrugated"] as StandardMaterial3D).cull_mode = BaseMaterial3D.CULL_DISABLED
	ResourceSaver.save(mats["corrugated"], OUT_MAT + "corrugated.tres")
	_pbr("rust", "rust_coarse_01", 0.4)
	_flat("steel", Color(0.55, 0.56, 0.57), 0.45, 0.8)
	_flat("paint_white", Color(0.84, 0.84, 0.82), 0.6)
	_flat("paint_red", Color(0.58, 0.07, 0.05), 0.55)
	_flat("paint_yellow", Color(0.85, 0.62, 0.05), 0.55)
	_flat("paint_blue", Color(0.08, 0.16, 0.42), 0.5)
	_flat("wood", Color(0.42, 0.33, 0.22), 0.8)
	_flat("dark", Color(0.03, 0.03, 0.035), 0.8)
	_flat("glass_dark", Color(0.05, 0.07, 0.09), 0.1, 0.2)
	_flat("canvas_white", Color(0.86, 0.85, 0.8), 0.9)
	_flat("trunk", Color(0.24, 0.18, 0.12), 0.95)
	_flat("leaves", Color(0.16, 0.27, 0.09), 0.9)
	var lamp := _flat("lamp", Color(0.9, 0.9, 0.85), 0.2)
	lamp.emission_enabled = true
	lamp.emission = Color(1.0, 0.95, 0.85)
	lamp.emission_energy_multiplier = 0.0
	ResourceSaver.save(lamp, OUT_MAT + "lamp.tres")
	for p in [["lamp_golden", 5.0], ["lamp_night", 14.0]]:
		var lit := _flat(p[0], Color(0.95, 0.95, 0.9), 0.2)
		lit.emission_enabled = true
		lit.emission = Color(1.0, 0.94, 0.82)
		lit.emission_energy_multiplier = p[1]
		ResourceSaver.save(lit, OUT_MAT + p[0] + ".tres")
	_flat("rock", Color(0.17, 0.2, 0.13), 1.0)
	_flat("rock_far", Color(0.22, 0.26, 0.24), 1.0)
	# Road paint: vertex colours (white lines, red/white kerbs, the chequered start, yellow pit boxes).
	var pm := StandardMaterial3D.new()
	pm.resource_name = "paint"
	pm.vertex_color_use_as_albedo = true
	pm.vertex_color_is_srgb = true
	pm.roughness = 0.62
	pm.albedo_color = Color.WHITE
	_save_mat("paint", pm)
	var fence := ShaderMaterial.new()
	fence.resource_name = "fence"
	fence.shader = load("res://assets/shaders/chain_link.gdshader")
	fence.set_shader_parameter("cell", 0.065)
	fence.set_shader_parameter("wire", 0.07)
	fence.set_shader_parameter("colour", Color(0.52, 0.53, 0.54))
	_save_mat("fence", fence)
	# Billboards: one material per sponsor cell of the livery atlas (2 x 4 cells of 4:1).
	var atlas: Texture2D = load("res://assets/textures/sponsors.png")
	for i in 8:
		var bm := StandardMaterial3D.new()
		bm.resource_name = "sponsor_%d" % i
		bm.albedo_texture = atlas
		bm.uv1_scale = Vector3(0.5, 0.25, 1.0)
		bm.uv1_offset = Vector3((i % 2) * 0.5, (i / 2) * 0.25, 0.0)
		bm.roughness = 0.55
		_save_mat("sponsor_%d" % i, bm)
	_ground_material()


func _ground_material() -> void:
	# The splat from the surface map (0.25 m): R asphalt, G concrete, B dirt, A pit lane.
	var nx: int = surf["nx"]
	var nz: int = surf["nz"]
	var path := OUT_MAT + "splat.res"
	if rebuild_meshes or not ResourceLoader.exists(path):
		var bytes := PackedByteArray()
		bytes.resize(nx * nz * 4)
		for k in nx * nz:
			var c := codes[k]
			var o := k * 4
			bytes[o] = 255 if (c == 1 or c == 2) else 0
			bytes[o + 1] = 255 if c == 3 else 0
			bytes[o + 2] = 255 if c == 4 else 0
			bytes[o + 3] = 255 if c == 2 else 0
		var img := Image.create_from_data(nx, nz, false, Image.FORMAT_RGBA8, bytes)
		img.generate_mipmaps()
		ResourceSaver.save(ImageTexture.create_from_image(img), path)
	var g := ShaderMaterial.new()
	g.resource_name = "ground"
	g.shader = load("res://assets/shaders/speedway_ground.gdshader")
	g.set_shader_parameter("splat", load(path))
	# The splat texels are cell-centred: the rect runs half a cell beyond the first and last centres.
	var c0: float = surf["cell"]
	g.set_shader_parameter("splat_rect", Vector4(surf["x0"] - c0 * 0.5, surf["z0"] - c0 * 0.5, nx * c0, nz * c0))
	for k in ["albedo", "normal", "orm"]:
		var suffix: String = {"albedo": "diff_2k.jpg", "normal": "nor_gl_2k.jpg", "orm": "arm_2k.jpg"}[k]
		g.set_shader_parameter("asphalt_" + k, _tex("asphalt_02/asphalt_02_" + suffix))
		g.set_shader_parameter("concrete_" + k, _tex("concrete_wall_008/concrete_wall_008_" + suffix))
		g.set_shader_parameter("dirt_" + k, _tex("brown_mud_dry/brown_mud_dry_" + suffix))
	# Every uniform explicitly (a headless-saved material otherwise gets null defaults when the editor re-saves it).
	g.set_shader_parameter("asphalt_scale", 0.16)
	g.set_shader_parameter("concrete_scale", 0.22)
	g.set_shader_parameter("dirt_scale", 0.2)
	g.set_shader_parameter("asphalt_tint", Color(0.62, 0.62, 0.64))
	g.set_shader_parameter("pit_tint", Color(0.78, 0.77, 0.75))
	g.set_shader_parameter("concrete_tint", Color(0.82, 0.81, 0.78))
	g.set_shader_parameter("dirt_tint", Color(0.70, 0.62, 0.52))
	g.set_shader_parameter("grass_a", Color(0.20, 0.30, 0.10))
	g.set_shader_parameter("grass_b", Color(0.34, 0.40, 0.16))
	g.set_shader_parameter("grass_dry", Color(0.47, 0.44, 0.27))
	g.set_shader_parameter("groove_strength", 0.35)
	_save_mat("ground", g)


# ------------------------------------------------------------------------------------------- environment

func _environment() -> void:
	var sky_mat := PanoramaSkyMaterial.new()
	sky_mat.panorama = _tex("golden_gate_hills/golden_gate_hills_2k.hdr")
	var sky := Sky.new()
	sky.sky_material = sky_mat
	sky.radiance_size = Sky.RADIANCE_SIZE_512
	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	env.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
	env.tonemap_mode = Environment.TONE_MAPPER_AGX
	env.tonemap_exposure = 0.95
	env.ssao_enabled = true
	env.ssao_radius = 1.2
	env.ssao_intensity = 1.6
	env.glow_enabled = true
	env.glow_intensity = 0.3
	env.fog_enabled = true
	env.fog_light_color = Color(0.74, 0.78, 0.84)
	env.fog_density = 0.0006
	env.fog_aerial_perspective = 0.5
	env.fog_sky_affect = 0.1
	var we := WorldEnvironment.new()
	we.name = "WorldEnvironment"
	we.environment = env
	_add(we)
	# From the north-east: the main stand (south side) is in the sun and its shadow falls away from the track.
	var to_sun := Vector3(0.45, 0.0, -0.55).normalized() * cos(deg_to_rad(44.0)) + Vector3.UP * sin(deg_to_rad(44.0))
	var sun := DirectionalLight3D.new()
	sun.name = "Sun"
	sun.transform = Transform3D(Basis.looking_at(-to_sun, Vector3.UP), to_sun * 50.0)
	sun.light_energy = 2.3
	sun.light_color = Color(1.0, 0.95, 0.88)
	sun.light_angular_distance = 0.6
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 220.0
	sun.light_bake_mode = Light3D.BAKE_DYNAMIC
	_add(sun)


func _environment_procedural() -> void:
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
	env.ssao_enabled = true
	env.ssao_radius = 1.2
	env.ssao_intensity = 1.6
	env.glow_enabled = true
	env.glow_intensity = 0.45 if golden else 0.8
	env.glow_bloom = 0.0 if golden else 0.04
	env.glow_hdr_threshold = 1.0
	env.fog_enabled = true
	env.fog_light_color = Color(0.85, 0.6, 0.42) if golden else Color(0.03, 0.035, 0.05)
	env.fog_density = 0.0004 if golden else 0.0008
	env.fog_aerial_perspective = 0.6 if golden else 0.3
	env.fog_sun_scatter = 0.25 if golden else 0.0
	env.fog_sky_affect = 0.1
	var we := WorldEnvironment.new()
	we.name = "WorldEnvironment"
	we.environment = env
	_add(we)
	var light := DirectionalLight3D.new()
	if golden:
		light.name = "Sun"
		var to_sun := Vector3(-0.92, 0.0, 0.37).normalized() * cos(deg_to_rad(10.0)) + Vector3.UP * sin(deg_to_rad(10.0))
		light.transform = Transform3D(Basis.looking_at(-to_sun, Vector3.UP), to_sun * 50.0)
		light.light_energy = 3.4
		light.light_color = Color(1.0, 0.64, 0.36)
		light.light_angular_distance = 0.8
		light.shadow_enabled = true
		light.directional_shadow_max_distance = 220.0
	else:
		light.name = "Moon"
		var to_moon := Vector3(0.45, 0.0, -0.6).normalized() * cos(deg_to_rad(38.0)) + Vector3.UP * sin(deg_to_rad(38.0))
		light.transform = Transform3D(Basis.looking_at(-to_moon, Vector3.UP), to_moon * 50.0)
		light.light_energy = 0.07
		light.light_color = Color(0.62, 0.72, 1.0)
		light.shadow_enabled = false
	light.light_bake_mode = Light3D.BAKE_DYNAMIC
	_add(light)


func _hills() -> void:
	# Rolling hills far around the speedway (the HDRI shows them in the afternoon): two noisy rings, fog for depth.
	var layers := [
		{"name": "HillsNear", "r": 900.0, "depth": 400.0, "h": 55.0, "seed": 5, "mat": "rock"},
		{"name": "HillsFar", "r": 1700.0, "depth": 600.0, "h": 140.0, "seed": 9, "mat": "rock_far"},
	]
	for layer in layers:
		var noise := FastNoiseLite.new()
		noise.seed = layer["seed"]
		noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
		noise.fractal_octaves = 4
		noise.frequency = 1.0
		var steps := 720
		var rows := 6
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		var grid := []
		for j in rows:
			var t := float(j) / (rows - 1)
			var row := []
			for i in steps + 1:
				var ang := TAU * i / steps
				var dir := Vector3(cos(ang), 0, sin(ang))
				var ridge: float = 0.5 + 0.5 * noise.get_noise_2d(cos(ang) * 2.2, sin(ang) * 2.2)
				var r: float = layer["r"] + t * layer["depth"]
				var y: float = lerp(-20.0, (0.25 + 0.75 * ridge) * layer["h"], pow(t, 0.7))
				row.append(dir * r + Vector3.UP * y)
			grid.append(row)
		for j in rows - 1:
			for i in steps:
				var a: Vector3 = grid[j][i]
				var b: Vector3 = grid[j][i + 1]
				var c: Vector3 = grid[j + 1][i]
				var d: Vector3 = grid[j + 1][i + 1]
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


# ------------------------------------------------------------------------------------------- terrain

func _terrain() -> void:
	# 1 m vertices from the 0.5 m heightfield, in 64 m tiles (culling, and a lightmap region each).
	var x0: float = hf["x0"]
	var z0: float = hf["z0"]
	var w := int((int(hf["nx"]) - 1) * float(hf["cell"]))
	var d := int((int(hf["nz"]) - 1) * float(hf["cell"]))
	var terrain := Node3D.new()
	terrain.name = "Terrain"
	_add(terrain)
	for tz in range(0, d, TILE):
		for tx in range(0, w, TILE):
			var name := "Terrain_%d_%d" % [tx / TILE, tz / TILE]
			var cx := mini(TILE, w - tx)
			var cz := mini(TILE, d - tz)
			var bx := x0 + tx
			var bz := z0 + tz
			var mi := _mesh_instance(name, func(): return _terrain_tile(bx, bz, cx, cz), "ground", 0.5)
			_add(mi, terrain)


func _terrain_tile(bx: float, bz: float, cx: int, cz: int) -> ArrayMesh:
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for j in cz + 1:
		for i in cx + 1:
			var x := bx + i
			var z := bz + j
			st.set_normal(_normal(x, z))
			st.set_uv(Vector2(x, z))
			st.add_vertex(Vector3(x, _h(x, z), z))
	for j in cz:
		for i in cx:
			var a := j * (cx + 1) + i
			var b := a + 1
			var c := a + cx + 1
			var e := c + 1
			st.add_index(a); st.add_index(b); st.add_index(c)
			st.add_index(b); st.add_index(e); st.add_index(c)
	return st.commit()


func _outside() -> void:
	# Grass from the raster's edge out to the hills (the ground shader's splat clamps to grass out here).
	var build := func():
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		var x0: float = hf["x0"]
		var z0: float = hf["z0"]
		var x1 := -x0
		var z1 := -z0
		var far := 1300.0
		var inner := [Vector3(x0, -0.02, z0), Vector3(x1, -0.02, z0), Vector3(x1, -0.02, z1), Vector3(x0, -0.02, z1)]
		var outer := [Vector3(-far, -0.05, -far), Vector3(far, -0.05, -far), Vector3(far, -0.05, far), Vector3(-far, -0.05, far)]
		for k in 4:
			var a: Vector3 = inner[k]
			var b: Vector3 = inner[(k + 1) % 4]
			var c: Vector3 = outer[(k + 1) % 4]
			var e: Vector3 = outer[k]
			st.set_normal(Vector3.UP)
			st.add_vertex(a); st.add_vertex(c); st.add_vertex(b)
			st.add_vertex(a); st.add_vertex(e); st.add_vertex(c)
		return st.commit()
	_add(_mesh_instance("Outside", build, "ground", 4.0, false))


# ------------------------------------------------------------------------------------------- walls

func _wall_mesh(pts: PackedVector2Array, closed: bool, t: float, painted: Callable) -> ArrayMesh:
	## A concrete wall standing to the right of the polyline: a slightly sloped inner face, a cap, the back face
	## down to the ground behind. Where `painted` says so (segment midpoint), red and white bands every 4 m.
	var plain := SurfaceTool.new()
	plain.begin(Mesh.PRIMITIVE_TRIANGLES)
	var bands := SurfaceTool.new()
	bands.begin(Mesh.PRIMITIVE_TRIANGLES)
	var used_plain := false
	var used_bands := false
	var n := pts.size()
	var count := n if closed else n - 1
	var along := 0.0
	for i in count:
		var p0 := pts[i]
		var p1 := pts[(i + 1) % n]
		var r0 := -_left(_dir_at(pts, i, closed))
		var r1 := -_left(_dir_at(pts, (i + 1) % n, closed))
		var seg := p0.distance_to(p1)
		var paint_here: bool = painted.call((p0 + p1) * 0.5)
		var st := bands if paint_here else plain
		if paint_here:
			used_bands = true
		else:
			used_plain = true
		var col := Color.WHITE
		if paint_here:
			col = Color(0.85, 0.85, 0.83) if int(floor((along + seg * 0.5) / 4.0)) % 2 == 0 else Color(0.62, 0.08, 0.06)
		along += seg
		# Profile offsets (to the right) and heights above the local ground at the front and back.
		var prof := [[0.0, -0.15, 0], [0.06, WALL_H - 0.05, 0], [0.12, WALL_H, 0], [t - 0.06, WALL_H, 1], [t, WALL_H - 0.05, 1], [t, -0.15, 2]]
		for k in prof.size() - 1:
			var a: Array = prof[k]
			var b: Array = prof[k + 1]
			var pa0 := _wall_point(p0, r0, a, t)
			var pa1 := _wall_point(p1, r1, a, t)
			var pb0 := _wall_point(p0, r0, b, t)
			var pb1 := _wall_point(p1, r1, b, t)
			st.set_color(col)
			_quad(st, pa0, pa1, pb1, pb0)
	if not closed:
		var st := plain
		used_plain = true
		for e in [[pts[0], -_left(_dir_at(pts, 0, false)), -1.0], [pts[n - 1], -_left(_dir_at(pts, n - 1, false)), 1.0]]:
			var p: Vector2 = e[0]
			var r: Vector2 = e[1]
			var gy := _h(p.x, p.y)
			var a := Vector3(p.x, gy - 0.15, p.y)
			var b := Vector3(p.x + r.x * t, gy - 0.15, p.y + r.y * t)
			var c := Vector3(p.x + r.x * t, gy + WALL_H, p.y + r.y * t)
			var d := Vector3(p.x, gy + WALL_H, p.y)
			if e[2] < 0:
				_quad(st, b, a, d, c)
			else:
				_quad(st, a, b, c, d)
	var mesh := ArrayMesh.new()
	if used_plain:
		plain.generate_normals()
		plain.commit(mesh)
		mesh.surface_set_material(mesh.get_surface_count() - 1, mats["concrete"])
	if used_bands:
		bands.generate_normals()
		bands.commit(mesh)
		mesh.surface_set_material(mesh.get_surface_count() - 1, mats["paint"])
	return mesh


func _wall_point(p: Vector2, r: Vector2, prof: Array, t: float) -> Vector3:
	var off: float = prof[0]
	var q := p + r * off
	# Heights follow the ground at the front face for the face and cap, the ground behind for the back.
	var front_y := _h(p.x, p.y)
	var y: float
	if prof[2] == 2:
		y = _h(q.x, q.y) + prof[1]
	else:
		y = front_y + prof[1]
	return Vector3(q.x, y, q.y)


func _outer_wall() -> void:
	var outer := _line(["walls", "outer"])
	var t: float = data["wall_thickness"]
	_add(_mesh_instance("OuterWall", func(): return _wall_mesh(outer, true, t, func(_m): return false), "concrete", 0.35))
	# Chain-link catch fence on top, set back from the face.
	var build := func():
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		var n := outer.size()
		var u := 0.0
		for i in n:
			var p0 := outer[i]
			var p1 := outer[(i + 1) % n]
			var r0 := -_left(_dir_at(outer, i, true))
			var r1 := -_left(_dir_at(outer, (i + 1) % n, true))
			var a := Vector3(p0.x + r0.x * (t - 0.18), _h(p0.x, p0.y) + WALL_H, p0.y + r0.y * (t - 0.18))
			var b := Vector3(p1.x + r1.x * (t - 0.18), _h(p1.x, p1.y) + WALL_H, p1.y + r1.y * (t - 0.18))
			var u1 := u + p0.distance_to(p1)
			var h := Vector3.UP * FENCE_H
			st.set_uv(Vector2(u, 0)); st.add_vertex(a)
			st.set_uv(Vector2(u1, 0)); st.add_vertex(b)
			st.set_uv(Vector2(u1, FENCE_H)); st.add_vertex(b + h)
			st.set_uv(Vector2(u, 0)); st.add_vertex(a)
			st.set_uv(Vector2(u1, FENCE_H)); st.add_vertex(b + h)
			st.set_uv(Vector2(u, FENCE_H)); st.add_vertex(a + h)
			u = u1
		st.generate_normals()
		return st.commit()
	var mi := _mesh_instance("CatchFence", build, "fence", 0.5, false)
	mi.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	_add(mi)


func _islands() -> void:
	var t: float = data["wall_thickness"]
	var k := 0
	for ring in data["walls"]["islands"]:
		var pts := PackedVector2Array()
		for p in ring:
			pts.append(Vector2(p[0], p[1]))
		var name := "Island_%d" % k
		# Red and white on the approaches to the crossover (the danger zone), plain concrete elsewhere.
		var mi := _mesh_instance(name, func(): return _wall_mesh(pts, true, t, func(m: Vector2): return m.length() < 45.0), "concrete", 0.35)
		_add(mi)
		k += 1


# ------------------------------------------------------------------------------------------- paint

func _paint_all() -> void:
	paint.begin(Mesh.PRIMITIVE_TRIANGLES)
	var white := Color(0.88, 0.88, 0.86)
	# Edge lines 0.35 m in from every permanent wall (the outer wall and the infield islands).
	_paint_line(_line(["walls", "outer"]), true, 0.55, 0.18, white)
	for ring in data["walls"]["islands"]:
		var pts := PackedVector2Array()
		for p in ring:
			pts.append(Vector2(p[0], p[1]))
		_paint_line(pts, true, 0.55, 0.18, white)
		_kerbs(pts)
	# The start lines of both layouts, both directions; and the pit boxes.
	for lname in ["oval", "figure8"]:
		var lay: Dictionary = data["layouts"][lname]
		var pts := _line(["layouts", lname, "points"])
		for key in ["start_forward", "start_reverse"]:
			_start_line(pts, lay["length"], lay[key])
		for b in lay["pit"]["boxes"]:
			_pit_box(_line(["layouts", lname, "pit", "points"]), Vector2(b[0], b[1]))
	var mi := MeshInstance3D.new()
	mi.name = "Paint"
	paint.generate_normals()
	var mesh := paint.commit()
	mesh.surface_set_material(0, mats["paint"])
	ResourceSaver.save(mesh, OUT_MESH + "Paint.res")
	mi.mesh = load(OUT_MESH + "Paint.res")
	mi.gi_mode = GeometryInstance3D.GI_MODE_DYNAMIC
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	_add(mi)


func _ribbon_quad(a: Vector2, b: Vector2, c: Vector2, d: Vector2, col: Color) -> void:
	paint.set_color(col)
	paint.add_vertex(_v3(a, PAINT_Y)); paint.add_vertex(_v3(c, PAINT_Y)); paint.add_vertex(_v3(b, PAINT_Y))
	paint.add_vertex(_v3(a, PAINT_Y)); paint.add_vertex(_v3(d, PAINT_Y)); paint.add_vertex(_v3(c, PAINT_Y))
	paint_count += 1


func _paint_line(pts: PackedVector2Array, closed: bool, off: float, width: float, col: Color) -> void:
	## A line `width` wide whose middle runs `off` metres left of the polyline (on the pavement side). Skipped
	## where the pavement is not road (grass, the islands' own concrete): only over asphalt.
	var n := pts.size()
	var count := n if closed else n - 1
	for i in count:
		var p0 := pts[i]
		var p1 := pts[(i + 1) % n]
		var l0 := _left(_dir_at(pts, i, closed))
		var l1 := _left(_dir_at(pts, (i + 1) % n, closed))
		var m := (p0 + p1) * 0.5 + (l0 + l1).normalized() * off
		if _code(m.x, m.y) != 1 and _code(m.x, m.y) != 2:
			continue
		var a := p0 + l0 * (off - width * 0.5)
		var b := p1 + l1 * (off - width * 0.5)
		var c := p1 + l1 * (off + width * 0.5)
		var d := p0 + l0 * (off + width * 0.5)
		_ribbon_quad(a, b, c, d, col)


func _kerbs(pts: PackedVector2Array) -> void:
	## Red and white kerbs, 0.9 m wide, along the inner edge of the turns (wall points 63.5 m from a turn centre).
	var centres := []
	for c in data["turn_centres"]:
		centres.append(Vector2(c[0], c[1]))
	var r_in: float = float(data["turn_radius"]) - float(data["road_width"]) * 0.5
	var n := pts.size()
	var along := 0.0
	for i in n:
		var p0 := pts[i]
		var p1 := pts[(i + 1) % n]
		var seg := p0.distance_to(p1)
		var on_turn := false
		for c in centres:
			var c2: Vector2 = c
			if absf(p0.distance_to(c2) - r_in) < 0.9 and absf(p1.distance_to(c2) - r_in) < 0.9:
				on_turn = true
		if on_turn:
			var l0 := _left(_dir_at(pts, i, true))
			var l1 := _left(_dir_at(pts, (i + 1) % n, true))
			var col := Color(0.88, 0.88, 0.86) if int(floor((along + seg * 0.5) / 2.0)) % 2 == 0 else Color(0.66, 0.07, 0.05)
			_ribbon_quad(p0 + l0 * 0.05, p1 + l1 * 0.05, p1 + l1 * 0.95, p0 + l0 * 0.95, col)
		along += seg


func _start_line(pts: PackedVector2Array, length: float, s: float) -> void:
	## A white line across the road with a two-row chequered band behind it.
	var n := pts.size()
	var i := int(round(s / (length / n))) % n
	var p := pts[i]
	var d := (pts[(i + 1) % n] - pts[(i - 1 + n) % n]).normalized()
	var l := _left(d)
	var half: float = float(data["road_width"]) * 0.5 - 0.1
	_ribbon_quad(p - l * half - d * 0.3, p - l * half + d * 0.3, p + l * half + d * 0.3, p + l * half - d * 0.3, Color(0.9, 0.9, 0.88))
	var sq := 0.65
	var cols := int(floor(half * 2.0 / sq))
	for row in 2:
		for k in cols:
			var black := (k + row) % 2 == 0
			var a := p - l * half + l * (k * sq) + d * (0.3 + row * sq)
			_ribbon_quad(a, a + d * sq, a + d * sq + l * sq, a + l * sq, Color(0.05, 0.05, 0.05) if black else Color(0.9, 0.9, 0.88))


func _pit_box(lane: PackedVector2Array, at: Vector2) -> void:
	## A yellow frame on the lane at a box (3.2 m x 5.6 m), lined up with the lane.
	var best := 0
	var bd := INF
	for i in lane.size():
		var dd := lane[i].distance_squared_to(at)
		if dd < bd:
			bd = dd
			best = i
	var d := _dir_at(lane, best, false)
	var l := _left(d)
	var yel := Color(0.9, 0.68, 0.08)
	for e in [[1.55, 0.0], [-1.55, 0.0]]:
		var c: Vector2 = at + l * e[0]
		_ribbon_quad(c - d * 2.8 - l * 0.075, c + d * 2.8 - l * 0.075, c + d * 2.8 + l * 0.075, c - d * 2.8 + l * 0.075, yel)
	for f in [2.75, -2.75]:
		var c: Vector2 = at + d * f
		_ribbon_quad(c - l * 1.62 - d * 0.075, c - l * 1.62 + d * 0.075, c + l * 1.62 + d * 0.075, c + l * 1.62 - d * 0.075, yel)


# ------------------------------------------------------------------------------------------- buildings

## Render layer of the stand roofs, their boards and columns. The floodlight towers behind the stands leave it
## out of their shadow casters, so the roofs don't throw a band of shadow across the straights at night.
const STAND_ROOF_LAYER := 1 << 10


func _grandstand(name: String, origin: Vector3, rot_y: float, length: float, rows: int) -> void:
	## Concrete terraces stepping up and back from the wall, timber benches, a corrugated back wall and roof on
	## steel columns. Built in a frame where the stand faces -Z (the track) and rows run +Z; SpeedwayLook.cs seats
	## the crowd in the same frame.
	var stand := Node3D.new()
	stand.name = name
	stand.transform = Transform3D(Basis(Vector3.UP, rot_y), origin)
	_add(stand)
	for k in rows:
		var step := _mesh_instance("%s_Terrace_%d" % [name, k], func(): return _box_mesh(Vector3(length, 0.42 * (k + 1) + 0.4, 0.8)), "concrete", 0.4)
		step.position = Vector3(0, 0.21 * (k + 1) - 0.2, 0.8 * k + 0.4)
		_add(step, stand)
		var bench := _mesh_instance("%s_Bench_%d" % [name, k], func(): return _box_mesh(Vector3(length - 1.0, 0.06, 0.32)), "wood", 0.4)
		bench.position = Vector3(0, 0.42 * (k + 1) + 0.03, 0.8 * k + 0.52)
		_add(bench, stand)
	var depth := 0.8 * rows
	var back := _mesh_instance("%s_Back" % name, func(): return _box_mesh(Vector3(length, 9.0, 0.3)), "corrugated", 0.4)
	back.position = Vector3(0, 4.5, depth + 0.3)
	_add(back, stand)
	var roof := _mesh_instance("%s_Roof" % name, func(): return _box_mesh(Vector3(length + 2.0, 0.08, depth + 3.0)), "corrugated", 0.5)
	roof.position = Vector3(0, 10.2, depth * 0.5 - 1.2)
	roof.rotation = Vector3(deg_to_rad(-6.0), 0, 0)
	roof.layers = STAND_ROOF_LAYER
	_add(roof, stand)
	var cols := int(length / 11.0) + 1
	for c in cols:
		var x := -length * 0.5 + 1.0 + c * (length - 2.0) / (cols - 1)
		for zz in [0.1, depth]:
			var col := _mesh_instance("%s_Col_%d_%d" % [name, c, int(zz)], func(): return _cyl_mesh(0.16, 10.2), "steel", 0.4)
			col.position = Vector3(x, 5.1, zz)
			col.layers = STAND_ROOF_LAYER
			_add(col, stand)
	# Sponsor boards along the front of the roof.
	var boards := int(length / 13.0)
	for b in boards:
		var x := -length * 0.5 + (b + 0.5) * length / boards
		var sponsor := "sponsor_%d" % ((b * 3 + (1 if rot_y > 0.1 else 0)) % 8)
		var q := MeshInstance3D.new()
		q.name = "%s_Board_%d" % [name, b]
		var qm := QuadMesh.new()
		qm.size = Vector2(12.0, 3.0)
		q.mesh = qm
		q.material_override = mats[sponsor]
		q.gi_mode = GeometryInstance3D.GI_MODE_DYNAMIC
		q.position = Vector3(x, 8.4, -1.6)
		q.rotation = Vector3(0, PI, 0)
		q.layers = STAND_ROOF_LAYER
		_add(q, stand)


func _pit_garages() -> void:
	## A garage behind each box, on the far side of the lane from the track: an open-fronted bay with a dark
	## interior, the box number above it, and a shared roof.
	for lname in ["oval", "figure8"]:
		var lay: Dictionary = data["layouts"][lname]
		var lane := _line(["layouts", lname, "pit", "points"])
		var track := _line(["layouts", lname, "points"])
		var k := 0
		for b in lay["pit"]["boxes"]:
			var at := Vector2(b[0], b[1])
			var best := 0
			var bd := INF
			for i in lane.size():
				var dd := lane[i].distance_squared_to(at)
				if dd < bd:
					bd = dd
					best = i
			var d := _dir_at(lane, best, false)
			var side := _left(d)
			# The garage side is away from the racing line.
			var nearest := INF
			for p in track:
				nearest = minf(nearest, (at + side * 6.0).distance_squared_to(p))
			var other := INF
			for p in track:
				other = minf(other, (at - side * 6.0).distance_squared_to(p))
			if nearest < other:
				side = -side
			var centre := at + side * (4.0 + 0.8 + 4.6)
			var gy := _h(centre.x, centre.y)
			var face := Vector3(-side.x, 0, -side.y)        # towards the lane
			var basis := Basis.looking_at(face, Vector3.UP)
			var g := Node3D.new()
			g.name = "Garage_%s_%d" % [lname, k]
			g.transform = Transform3D(basis, Vector3(centre.x, gy, centre.y))
			_add(g)
			# Local frame: -Z faces the lane, +X along it.
			_box("%s_Back" % g.name, Vector3(7.6, 4.2, 0.3), Transform3D(Basis.IDENTITY, Vector3(0, 2.1, 4.3)), "concrete_dark", g)
			for sx in [-3.8, 3.8]:
				_box("%s_Side_%d" % [g.name, int(sx)], Vector3(0.3, 4.2, 8.9), Transform3D(Basis.IDENTITY, Vector3(sx, 2.1, 0.0)), "concrete", g)
			_box("%s_Roof" % g.name, Vector3(8.4, 0.3, 9.6), Transform3D(Basis.IDENTITY, Vector3(0, 4.35, -0.3)), "corrugated", g)
			_box("%s_Lintel" % g.name, Vector3(7.9, 0.9, 0.3), Transform3D(Basis.IDENTITY, Vector3(0, 3.75, -4.3)), "paint_blue", g)
			_box("%s_Floor" % g.name, Vector3(7.3, 0.06, 8.6), Transform3D(Basis.IDENTITY, Vector3(0, 0.03, 0.0)), "concrete_dark", g)
			_box("%s_Bench" % g.name, Vector3(3.0, 0.9, 0.7), Transform3D(Basis.IDENTITY, Vector3(-1.8, 0.45, 3.6)), "rust", g)
			var num := Label3D.new()
			num.name = "Number"
			num.text = "PIT %d" % (k + 1)
			num.font_size = 96
			num.pixel_size = 0.008
			num.modulate = Color(1.0, 0.85, 0.2)
			num.outline_size = 0
			num.position = Vector3(0, 3.75, -4.47)
			num.rotation = Vector3(0, PI, 0)
			_add(num, g)
			k += 1


func _gantries() -> void:
	## An overhead gantry at the forward start line of each layout: two pillars outside the road, a beam with the
	## start lights and the speedway's name.
	for lname in ["oval", "figure8"]:
		var lay: Dictionary = data["layouts"][lname]
		var pts := _line(["layouts", lname, "points"])
		var n := pts.size()
		var i := int(round(float(lay["start_forward"]) / (float(lay["length"]) / n))) % n
		var p := pts[i]
		var d := (pts[(i + 1) % n] - pts[(i - 1 + n) % n]).normalized()
		var g := Node3D.new()
		g.name = "Gantry_%s" % lname
		var y := maxf(_h(p.x, p.y), 0.0)
		g.transform = Transform3D(Basis.looking_at(Vector3(d.x, 0, d.y), Vector3.UP), Vector3(p.x, y, p.y))
		_add(g)
		var span := float(data["road_width"]) * 0.5 + 2.2
		for sx in [-span, span]:
			_box("%s_Pillar_%d" % [g.name, int(sx)], Vector3(0.6, 7.6, 0.6), Transform3D(Basis.IDENTITY, Vector3(sx, 3.8, 0)), "steel", g)
		_box("%s_Beam" % g.name, Vector3(span * 2.0 + 0.6, 1.4, 0.7), Transform3D(Basis.IDENTITY, Vector3(0, 7.0, 0)), "paint_red", g)
		var title := Label3D.new()
		title.name = "Title"
		title.text = "CROSSROADS SPEEDWAY"
		title.font_size = 120
		title.pixel_size = 0.0085
		title.modulate = Color(1.0, 0.85, 0.25)
		title.outline_size = 0
		title.position = Vector3(0, 7.0, 0.37)
		_add(title, g)
		var back := title.duplicate() as Label3D
		back.name = "TitleBack"
		back.position = Vector3(0, 7.0, -0.37)
		back.rotation = Vector3(0, PI, 0)
		_add(back, g)
		# Five start lights facing the grid (behind the line: +Z of this frame faces the oncoming cars).
		for k in 5:
			var lamp := MeshInstance3D.new()
			lamp.name = "StartLight_%d" % k
			var sm := SphereMesh.new()
			sm.radius = 0.22
			sm.height = 0.44
			sm.radial_segments = 12
			sm.rings = 6
			lamp.mesh = sm
			lamp.material_override = mats["dark"]
			lamp.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
			lamp.position = Vector3(-1.6 + k * 0.8, 6.0, 0.36)
			_add(lamp, g)


func _billboards() -> void:
	## Big sponsor boards on the island tips round the crossover: from the approach you can't see who is coming
	## across (DD1's blind crossover).
	var centre := Vector2(0, 0)
	if data["layouts"]["figure8"]["crossover"] != null:
		var c: Array = data["layouts"]["figure8"]["crossover"]["point"]
		centre = Vector2(c[0], c[1])
	var k := 0
	for corner in data["crossover_corners"]:
		var p := Vector2(corner[0], corner[1])
		var out := (p - centre).normalized()
		var at := p + out * 8.5
		var gy := _h(at.x, at.y)
		var b := Node3D.new()
		b.name = "Billboard_%d" % k
		b.transform = Transform3D(Basis.looking_at(Vector3(-out.x, 0, -out.y), Vector3.UP), Vector3(at.x, gy, at.y))
		_add(b)
		for sx in [-4.6, 4.6]:
			_box("%s_Leg_%d" % [b.name, int(sx)], Vector3(0.35, 4.4, 0.35), Transform3D(Basis.IDENTITY, Vector3(sx, 2.2, 0.3)), "steel", b)
		_box("%s_Frame" % b.name, Vector3(10.6, 3.0, 0.25), Transform3D(Basis.IDENTITY, Vector3(0, 2.75, 0.15)), "dark", b)
		for side in [0, 1]:
			var q := MeshInstance3D.new()
			q.name = "Face_%d" % side
			var qm := QuadMesh.new()
			qm.size = Vector2(10.4, 2.6)
			q.mesh = qm
			q.material_override = mats["sponsor_%d" % ((k * 2 + side * 5) % 8)]
			q.gi_mode = GeometryInstance3D.GI_MODE_DYNAMIC
			q.position = Vector3(0, 2.75, -0.01 if side == 0 else 0.31)
			q.rotation = Vector3(0, PI if side == 0 else 0.0, 0)
			_add(q, b)
		k += 1


func _floodlights() -> void:
	# Tower, aim point. Placed and aimed for the most even light on both layouts (a coverage search over the
	# racing lines): the towers behind the stands cross their beams over the middle of the straights, the
	# turn towers light the turn entries, and the infield towers throw light over the far straights.
	var spots := []
	for sx in [1.0, -1.0]:
		for sz in [1.0, -1.0]:
			spots.append([Vector2(sx * 25.0, sz * 96.0), Vector2(-sx * 35.0, sz * 72.0)])
			spots.append([Vector2(sx * 204.0, sz * 18.0), Vector2(sx * 152.0, sz * 36.0)])
	spots.append([Vector2(0, 30), Vector2(0, -50)])
	spots.append([Vector2(0, -30), Vector2(0, 50)])
	var k := 0
	for s in spots:
		var at: Vector2 = s[0]
		var aim2: Vector2 = s[1]
		_floodlight("Floodlight_%d" % k, Vector3(at.x, _h(at.x, at.y), at.y), Vector3(aim2.x, 0.0, aim2.y))
		k += 1


func _floodlight(name: String, at: Vector3, target: Vector3) -> void:
	var tower := Node3D.new()
	tower.name = name
	_add(tower)
	tower.position = at
	var pole := _mesh_instance("%s_Pole" % name, func(): return _cyl_mesh(0.36, 30.0), "steel", 0.4)
	pole.position = Vector3(0, 15.0, 0)
	_add(pole, tower)
	var head := Node3D.new()
	head.name = "Head"
	var aim := (target - (at + Vector3(0, 30.5, 0))).normalized()
	head.transform = Transform3D(Basis.looking_at(aim, Vector3.UP), Vector3(0, 30.5, 0))
	_add(head, tower)
	var frame := _mesh_instance("%s_Frame" % name, func(): return _box_mesh(Vector3(5.0, 2.6, 0.35)), "steel", 0.4)
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
		var spot := SpotLight3D.new()
		spot.name = "Flood"
		spot.position = Vector3(0, 0, -0.6)
		spot.light_color = Color(1.0, 0.93, 0.8)
		spot.light_energy = 22.0 if preset == "night" else 5.5
		spot.light_indirect_energy = 1.0
		spot.light_volumetric_fog_energy = 0.0
		spot.spot_range = 230.0
		spot.spot_attenuation = 0.85
		spot.spot_angle = 52.0
		spot.spot_angle_attenuation = 1.3
		spot.light_size = 1.2
		spot.shadow_enabled = true
		spot.shadow_bias = 0.05
		spot.shadow_normal_bias = 1.4
		spot.light_bake_mode = Light3D.BAKE_DYNAMIC
		if absf(at.z) > 90.0:
			spot.shadow_caster_mask = 0xFFFFF & ~STAND_ROOF_LAYER
		_add(spot, head)


func _infield() -> void:
	## Race-day dressing in the loops: trailers and campers, a hospitality tent and flags.
	var spots := [
		[Vector2(132, 18), 20.0], [Vector2(141, -6), 75.0], [Vector2(126, -28), 110.0], [Vector2(150, 28), 160.0],
		[Vector2(-140, 30), 10.0], [Vector2(-118, 40), 95.0], [Vector2(-152, 6), 60.0], [Vector2(-96, 34), 140.0],
	]
	var k := 0
	for s in spots:
		var p: Vector2 = s[0]
		var rot: float = deg_to_rad(s[1])
		var t := Node3D.new()
		t.name = "Trailer_%d" % k
		t.transform = Transform3D(Basis(Vector3.UP, rot), Vector3(p.x, _h(p.x, p.y), p.y))
		_add(t)
		var body_mat := "canvas_white" if k % 3 != 1 else "paint_blue"
		_box("%s_Body" % t.name, Vector3(2.5, 2.6, 8.0), Transform3D(Basis.IDENTITY, Vector3(0, 1.75, 0)), body_mat, t)
		_box("%s_Stripe" % t.name, Vector3(2.52, 0.3, 8.02), Transform3D(Basis.IDENTITY, Vector3(0, 1.6, 0)), "paint_red", t)
		for wz in [-1.6, 1.6]:
			_box("%s_Window_%d" % [t.name, int(wz * 10)], Vector3(2.54, 0.7, 1.2), Transform3D(Basis.IDENTITY, Vector3(0, 2.2, wz)), "glass_dark", t)
		for wz in [-1.2, 1.2]:
			for wx in [-1.0, 1.0]:
				var wheel := _mesh_instance("%s_Wheel_%d_%d" % [t.name, int(wx), int(wz * 10)], func(): return _cyl_mesh(0.42, 0.3), "dark", 0.4)
				wheel.transform = Transform3D(Basis(Vector3.FORWARD, PI * 0.5), Vector3(wx * 1.15, 0.42, wz))
				_add(wheel, t)
		k += 1
	# A hospitality tent and a flag line in the east loop.
	var tent := Node3D.new()
	tent.name = "Tent"
	tent.position = Vector3(112, _h(112, 0), 0)
	_add(tent)
	_box("Tent_Roof", Vector3(14.0, 0.2, 9.0), Transform3D(Basis.IDENTITY, Vector3(0, 3.2, 0)), "canvas_white", tent)
	for sx in [-6.8, 6.8]:
		for sz in [-4.3, 4.3]:
			var pole := _mesh_instance("Tent_Pole_%d_%d" % [int(sx), int(sz)], func(): return _cyl_mesh(0.06, 3.2), "steel", 0.4)
			pole.position = Vector3(sx, 1.6, sz)
			_add(pole, tent)
	var flag_cols := ["paint_red", "paint_yellow", "paint_blue", "canvas_white"]
	for f in 10:
		var fx := -54.0 + f * 12.0
		var pole := _mesh_instance("FlagPole_%d" % f, func(): return _cyl_mesh(0.07, 16.0), "steel", 0.4)
		pole.position = Vector3(fx, 8.0, 94.0)
		_add(pole, look_root)
		var flag := _mesh_instance("Flag_%d" % f, func(): return _box_mesh(Vector3(2.4, 1.5, 0.03)), flag_cols[f % 4], 0.4)
		flag.position = Vector3(fx + 1.25, 15.0, 94.0)
		_add(flag, look_root)


func _lightmap() -> void:
	var lm := LightmapGI.new()
	lm.name = "LightmapGI"
	lm.quality = LightmapGI.BAKE_QUALITY_MEDIUM
	lm.bounces = 2
	lm.use_denoiser = true
	lm.directional = false
	lm.max_texture_size = 8192
	lm.texel_scale = 1.0
	lm.generate_probes_subdiv = LightmapGI.GENERATE_PROBES_SUBDIV_4
	lm.environment_mode = LightmapGI.ENVIRONMENT_MODE_SCENE
	_add(lm)
