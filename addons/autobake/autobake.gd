@tool
extends EditorPlugin
## Unattended lightmap baking (plan §8.1). Godot exposes no script API to bake a LightmapGI,
## so this opens the scene, selects its LightmapGI and presses the editor's own "Bake Lightmaps" button.
## Usage: Godot --path <project> --editor -- --autobake=res://path/scene.tscn

var _scene := ""
var _frames := 0
var _state := 0
var _lightmap: LightmapGI
var _start_ms := 0
var _busy := false


func _enter_tree() -> void:
	for arg in OS.get_cmdline_user_args():
		if arg.begins_with("--autobake="):
			_scene = arg.substr("--autobake=".length())
	set_process(_scene != "")
	if _scene != "":
		print("[autobake] scene: ", _scene)


func _process(_delta: float) -> void:
	# Editor progress dialogs (bake, save) pump the main loop and re-enter _process; ignore those calls.
	if _busy:
		return
	_frames += 1
	match _state:
		0:
			if _frames > 60:
				EditorInterface.open_scene_from_path(_scene)
				_next(1)
		1:
			# The editor may restore the last session's scene first; wait until ours is the edited one.
			var root := EditorInterface.get_edited_scene_root()
			if root == null or root.scene_file_path != _scene:
				if _frames > 3600:
					_fail("scene did not open: " + _scene)
				return
			if _frames > 30:
				_lightmap = _find_lightmap(root)
				if _lightmap == null:
					_fail("no LightmapGI in scene")
					return
				EditorInterface.set_main_screen_editor("3D")
				EditorInterface.get_selection().clear()
				EditorInterface.get_selection().add_node(_lightmap)
				_next(2)
		2:
			if _frames > 30:
				var button := _find_button(EditorInterface.get_base_control(), "Bake Lightmaps")
				if button == null:
					_fail("Bake Lightmaps button not found")
					return
				print("[autobake] baking...")
				_start_ms = Time.get_ticks_msec()
				_next(3)
				_busy = true
				button.emit_signal("pressed")
				_busy = false
		3:
			if _lightmap.light_data != null:
				print("[autobake] baked in %d ms" % (Time.get_ticks_msec() - _start_ms))
				_next(4)
				_busy = true
				EditorInterface.save_scene()
				_busy = false
			elif _frames % 30 == 0:
				# The editor asks where to save the bake on the first bake; answer it.
				var file_dialog := _find_visible_file_dialog(get_tree().root)
				if file_dialog != null:
					var path := _scene.get_basename() + ".lmbake"
					print("[autobake] bake file: ", path)
					file_dialog.hide()
					_busy = true
					file_dialog.file_selected.emit(path)
					_busy = false
					return
				# Bake errors are shown in a modal dialog rather than printed; surface them.
				var dialog := _find_visible_dialog(get_tree().root)
				if dialog != null:
					_fail("editor dialog: " + dialog.dialog_text)
				elif Time.get_ticks_msec() - _start_ms > 15 * 60 * 1000:
					_fail("timeout")
		4:
			if _frames > 30:
				print("[autobake] done")
				get_tree().quit(0)


func _next(state: int) -> void:
	_state = state
	_frames = 0


func _fail(message: String) -> void:
	push_error("[autobake] " + message)
	set_process(false)
	get_tree().quit(1)


func _find_lightmap(node: Node) -> LightmapGI:
	if node == null:
		return null
	if node is LightmapGI:
		return node
	for child in node.get_children():
		var found := _find_lightmap(child)
		if found != null:
			return found
	return null


func _find_visible_file_dialog(node: Node) -> EditorFileDialog:
	if node is EditorFileDialog and (node as EditorFileDialog).visible:
		return node
	for child in node.get_children():
		var found := _find_visible_file_dialog(child)
		if found != null:
			return found
	return null


func _find_visible_dialog(node: Node) -> AcceptDialog:
	if node is AcceptDialog and not node is EditorFileDialog and (node as AcceptDialog).visible and (node as AcceptDialog).dialog_text != "":
		return node
	for child in node.get_children():
		var found := _find_visible_dialog(child)
		if found != null:
			return found
	return null


func _find_button(node: Node, text: String) -> Button:
	if node is Button and (node as Button).text.contains(text) and (node as Button).is_visible_in_tree():
		return node
	for child in node.get_children():
		var found := _find_button(child, text)
		if found != null:
			return found
	return null
