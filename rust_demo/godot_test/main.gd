extends Node3D
# GDScript port of the Rust/Bevy voxel demo in ../src — same seed, same
# SplitMix64/value-noise terrain, same entity counts and wander rules, for an
# FPS / iteration-speed comparison. Everything is built programmatically.

# ---------------------------------------------------------------------------
# config.rs
# ---------------------------------------------------------------------------
const WORLD_WIDTH := 256
const WORLD_DEPTH := 256
const WORLD_SEED := 12345
const TARGET_VISIBLE_VOXELS := 200.0
const TERRAIN_MAX_HEIGHT := 8
const WATER_LEVEL := 1
const TERRAIN_CHUNK_SIZE := 16
const MOVER_COUNT := 15000
const MOVER_MIN_SPEED := 2.0
const MOVER_MAX_SPEED := 6.0
const TREE_COUNT := 220
const ROCK_COUNT := 60
const HOUSE_COUNT := 180
const CAMERA_AZIMUTH_DEGREES := 45.0
const CAMERA_ELEVATION_DEGREES := 38.0
const BODY_HEIGHT := 0.9

# ---------------------------------------------------------------------------
# world.rs — SplitMix64 + value noise. GDScript ints are 64-bit two's
# complement and wrap, so u64 math works via signed wrapping; the only care
# needed is logical (not arithmetic) right shifts, done via mask.
# u64 constants that don't fit in i64 are written as their signed equivalents.
# ---------------------------------------------------------------------------
const SM64_ADD := -7046029254386353131      # 0x9E3779B97F4A7C15
const SM64_MUL1 := -4658895280553007687     # 0xBF58476D1CE4E5B9
const SM64_MUL2 := -7723592293110705685     # 0x94D049BB133111EB
const HASH_X := 5871781006564002453         # 0x517CC1B727220A95
const HASH_Z := 2685821657736338717         # 0x2545F4914F6CDD1D

static func lshr(x: int, n: int) -> int:
	return (x >> n) & ((1 << (64 - n)) - 1)

# u64 modulo for a signed-wrapped 64-bit value (m must be positive).
static func umod(x: int, m: int) -> int:
	if x >= 0:
		return x % m
	# (x + 2^64) % m, computed without overflowing.
	var a := 9223372036854775807 % m  # (2^63 - 1) % m
	var pow64 := (2 * ((a + 1) % m)) % m  # 2^64 % m
	return ((x % m) + m + pow64) % m

var _rng_state := 0

func splitmix64_step(state: int) -> int:
	# Returns the new state; the mixed output is in _rng_out.
	state += SM64_ADD
	var z := state
	z = (z ^ lshr(z, 30)) * SM64_MUL1
	z = (z ^ lshr(z, 27)) * SM64_MUL2
	_rng_out = z ^ lshr(z, 31)
	return state

var _rng_out := 0

func rand01() -> float:
	_rng_state = splitmix64_step(_rng_state)
	return float(lshr(_rng_out, 40)) / 16777216.0

func hash01(seed: int, x: int, z: int) -> float:
	var s := seed ^ (x * HASH_X) ^ (z * HASH_Z)
	splitmix64_step(s)
	return float(lshr(_rng_out, 40)) / 16777216.0

func value_noise(seed: int, x: float, z: float) -> float:
	var x0 := floorf(x)
	var z0 := floorf(z)
	var tx := smoothstep(0.0, 1.0, x - x0)
	var tz := smoothstep(0.0, 1.0, z - z0)
	var xi := int(x0)
	var zi := int(z0)
	var a := hash01(seed, xi, zi)
	var b := hash01(seed, xi + 1, zi)
	var c := hash01(seed, xi, zi + 1)
	var d := hash01(seed, xi + 1, zi + 1)
	return lerpf(lerpf(a, b, tx), lerpf(c, d, tx), tz)

func fbm(seed: int, x: float, z: float) -> float:
	var total := 0.0
	var amplitude := 1.0
	var frequency := 1.0
	var norm := 0.0
	for octave in 3:
		total += value_noise(seed ^ (octave * 0x9E37), x * frequency, z * frequency) * amplitude
		norm += amplitude
		amplitude *= 0.5
		frequency *= 2.0
	return total / norm

# TerrainType
enum { WATER, SAND, GRASS, DRY_GRASS, EARTH, ROCK }

var heights := PackedByteArray()
var types := PackedByteArray()
var variants := PackedByteArray()

func generate_world() -> void:
	heights.resize(WORLD_WIDTH * WORLD_DEPTH)
	types.resize(WORLD_WIDTH * WORLD_DEPTH)
	variants.resize(WORLD_WIDTH * WORLD_DEPTH)
	for z in WORLD_DEPTH:
		for x in WORLD_WIDTH:
			var base := fbm(WORLD_SEED, x * 0.02, z * 0.02)
			var hills := fbm(WORLD_SEED ^ 0xABCD, x * 0.008, z * 0.008)
			var h := base * 3.5 + hills * 6.0 - 1.2
			var lake := sqrt(pow(x - 70.0, 2) + pow(z - 170.0, 2))
			if lake < 42.0:
				h -= (1.0 - lake / 42.0) * 4.0
			var height := clampi(roundi(h), 0, TERRAIN_MAX_HEIGHT)
			var variant_roll := hash01(WORLD_SEED ^ 0xC0FFEE, x, z)
			var moisture := fbm(WORLD_SEED ^ 0x5EED, x * 0.015, z * 0.015)
			var type: int
			if height <= WATER_LEVEL:
				type = WATER
				height = WATER_LEVEL
			elif height == WATER_LEVEL + 1:
				type = SAND
			elif height >= TERRAIN_MAX_HEIGHT - 1:
				type = ROCK
			elif moisture < 0.35:
				type = DRY_GRASS
			elif variant_roll > 0.93:
				type = EARTH
			else:
				type = GRASS
			var i := z * WORLD_WIDTH + x
			heights[i] = height
			types[i] = type
			variants[i] = int(variant_roll * 255.0)

func height_at(x: float, z: float) -> float:
	var xi := clampi(int(floorf(x)), 0, WORLD_WIDTH - 1)
	var zi := clampi(int(floorf(z)), 0, WORLD_DEPTH - 1)
	return float(heights[zi * WORLD_WIDTH + xi])

func is_water(x: float, z: float) -> bool:
	var xi := clampi(int(floorf(x)), 0, WORLD_WIDTH - 1)
	var zi := clampi(int(floorf(z)), 0, WORLD_DEPTH - 1)
	return types[zi * WORLD_WIDTH + xi] == WATER

# ---------------------------------------------------------------------------
# terrain.rs — palette + chunk meshing (vertex colors, one shared material)
# ---------------------------------------------------------------------------
const BASE_COLORS := {
	WATER: Color(0.16, 0.32, 0.55),
	SAND: Color(0.76, 0.70, 0.50),
	GRASS: Color(0.26, 0.46, 0.21),
	DRY_GRASS: Color(0.50, 0.48, 0.24),
	EARTH: Color(0.45, 0.36, 0.24),
	ROCK: Color(0.52, 0.52, 0.50),
}

func cell_color(type: int, height: int, variant: int) -> Color:
	var c: Color = BASE_COLORS[type]
	var shade := 0.92 + (variant / 255.0) * 0.16 + (float(height) / TERRAIN_MAX_HEIGHT) * 0.12
	return Color(c.r * shade, c.g * shade, c.b * shade)

func neighbor_height(x: int, z: int) -> int:
	if x < 0 or z < 0 or x >= WORLD_WIDTH or z >= WORLD_DEPTH:
		return 0
	return heights[z * WORLD_WIDTH + x]

class MeshData:
	var positions := PackedVector3Array()
	var normals := PackedVector3Array()
	var colors := PackedColorArray()
	var indices := PackedInt32Array()

	func quad(verts: Array, normal: Vector3, color: Color) -> void:
		var base := positions.size()
		for v in verts:
			positions.push_back(v)
			normals.push_back(normal)
			colors.push_back(color)
		# Godot front faces wind clockwise (opposite of Bevy/three.js).
		for idx in [0, 2, 1, 0, 3, 2]:
			indices.push_back(base + idx)

	func into_mesh() -> ArrayMesh:
		var arrays := []
		arrays.resize(Mesh.ARRAY_MAX)
		arrays[Mesh.ARRAY_VERTEX] = positions
		arrays[Mesh.ARRAY_NORMAL] = normals
		arrays[Mesh.ARRAY_COLOR] = colors
		arrays[Mesh.ARRAY_INDEX] = indices
		var mesh := ArrayMesh.new()
		mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
		return mesh

func build_chunk_mesh(cx: int, cz: int) -> ArrayMesh:
	var m := MeshData.new()
	var x0 := cx * TERRAIN_CHUNK_SIZE
	var z0 := cz * TERRAIN_CHUNK_SIZE
	for z in range(z0, mini(z0 + TERRAIN_CHUNK_SIZE, WORLD_DEPTH)):
		for x in range(x0, mini(x0 + TERRAIN_CHUNK_SIZE, WORLD_WIDTH)):
			var i := z * WORLD_WIDTH + x
			var h := float(heights[i])
			var top := cell_color(types[i], heights[i], variants[i])
			var side := Color(top.r * 0.72, top.g * 0.72, top.b * 0.72)
			m.quad([
				Vector3(x, h, z), Vector3(x, h, z + 1),
				Vector3(x + 1, h, z + 1), Vector3(x + 1, h, z),
			], Vector3.UP, top)
			for dir in [[-1, 0], [1, 0], [0, -1], [0, 1]]:
				var nh := float(neighbor_height(x + dir[0], z + dir[1]))
				if nh >= h:
					continue
				var verts: Array
				var normal := Vector3(dir[0], 0, dir[1])
				if dir[0] == -1:
					verts = [Vector3(x, h, z), Vector3(x, nh, z), Vector3(x, nh, z + 1), Vector3(x, h, z + 1)]
				elif dir[0] == 1:
					verts = [Vector3(x + 1, h, z + 1), Vector3(x + 1, nh, z + 1), Vector3(x + 1, nh, z), Vector3(x + 1, h, z)]
				elif dir[1] == -1:
					verts = [Vector3(x + 1, h, z), Vector3(x + 1, nh, z), Vector3(x, nh, z), Vector3(x, h, z)]
				else:
					verts = [Vector3(x, h, z + 1), Vector3(x, nh, z + 1), Vector3(x + 1, nh, z + 1), Vector3(x + 1, h, z + 1)]
				m.quad(verts, normal, side)
	return m.into_mesh()

func vertex_color_material() -> StandardMaterial3D:
	var mat := StandardMaterial3D.new()
	mat.vertex_color_use_as_albedo = true
	mat.vertex_color_is_srgb = true
	mat.roughness = 1.0
	return mat

# ---------------------------------------------------------------------------
# Mover mesh: body prism + head cube merged, vertex-colored (movers.rs)
# ---------------------------------------------------------------------------
const SKIN_COLOR := Color(0.87, 0.72, 0.58)
const PALETTE := [
	Color(0.85, 0.25, 0.20), Color(0.20, 0.45, 0.85), Color(0.90, 0.75, 0.20),
	Color(0.85, 0.85, 0.88), Color(0.30, 0.25, 0.35),
]

func push_box(m: MeshData, center: Vector3, half: Vector3, color: Color) -> void:
	var c := center
	var h := half
	var faces := [
		[Vector3.UP, [Vector3(-h.x, h.y, -h.z), Vector3(-h.x, h.y, h.z), Vector3(h.x, h.y, h.z), Vector3(h.x, h.y, -h.z)]],
		[Vector3.DOWN, [Vector3(-h.x, -h.y, h.z), Vector3(-h.x, -h.y, -h.z), Vector3(h.x, -h.y, -h.z), Vector3(h.x, -h.y, h.z)]],
		[Vector3.RIGHT, [Vector3(h.x, h.y, h.z), Vector3(h.x, -h.y, h.z), Vector3(h.x, -h.y, -h.z), Vector3(h.x, h.y, -h.z)]],
		[Vector3.LEFT, [Vector3(-h.x, h.y, -h.z), Vector3(-h.x, -h.y, -h.z), Vector3(-h.x, -h.y, h.z), Vector3(-h.x, h.y, h.z)]],
		[Vector3.BACK, [Vector3(-h.x, h.y, h.z), Vector3(-h.x, -h.y, h.z), Vector3(h.x, -h.y, h.z), Vector3(h.x, h.y, h.z)]],
		[Vector3.FORWARD, [Vector3(h.x, h.y, -h.z), Vector3(h.x, -h.y, -h.z), Vector3(-h.x, -h.y, -h.z), Vector3(-h.x, h.y, -h.z)]],
	]
	for face in faces:
		var verts: Array = []
		for corner in face[1]:
			verts.push_back(c + corner)
		m.quad(verts, face[0], color)

func person_mesh(body_color: Color) -> ArrayMesh:
	var m := MeshData.new()
	push_box(m, Vector3(0, BODY_HEIGHT * 0.5, 0), Vector3(0.25, BODY_HEIGHT * 0.5, 0.175), body_color)
	push_box(m, Vector3(0, BODY_HEIGHT + 0.2, 0), Vector3(0.2, 0.2, 0.2), SKIN_COLOR)
	return m.into_mesh()

# ---------------------------------------------------------------------------
# Movers state (SoA) + 5 MultiMeshes (one per palette variant)
# ---------------------------------------------------------------------------
var mover_x := PackedFloat32Array()
var mover_z := PackedFloat32Array()
var mover_y := PackedFloat32Array()
var mover_heading := PackedFloat32Array()
var mover_speed := PackedFloat32Array()
var mover_retarget := PackedFloat32Array()
var mover_rng := PackedInt64Array()
var mover_multimeshes: Array[MultiMesh] = []

var overlay: Label
var fps_smoothed := -1.0
var next_log_s := 0.0

func _ready() -> void:
	generate_world()
	_setup_camera_and_light()
	_spawn_terrain()
	_spawn_decorations()
	_spawn_movers()
	_setup_overlay()
	print("scene ready in %d ms" % Time.get_ticks_msec())

func _setup_camera_and_light() -> void:
	var center := Vector3(WORLD_WIDTH * 0.5, 0, WORLD_DEPTH * 0.5)
	var azimuth := deg_to_rad(CAMERA_AZIMUTH_DEGREES)
	var elevation := deg_to_rad(CAMERA_ELEVATION_DEGREES)
	var camera := Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = TARGET_VISIBLE_VOXELS * sin(elevation)  # vertical extent, like FixedVertical
	camera.near = 1.0
	camera.far = 1200.0
	var offset := Vector3(
		cos(elevation) * cos(azimuth), sin(elevation), cos(elevation) * sin(azimuth)
	) * 400.0
	camera.position = center + offset
	add_child(camera)
	camera.look_at(center, Vector3.UP)

	var sun := DirectionalLight3D.new()
	sun.light_energy = 1.3
	sun.shadow_enabled = true
	add_child(sun)
	sun.look_at_from_position(Vector3(100, 200, 40), Vector3.ZERO, Vector3.UP)

	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0.10, 0.12, 0.16)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(0.85, 0.9, 1.0)
	env.ambient_light_energy = 0.6
	var world_env := WorldEnvironment.new()
	world_env.environment = env
	add_child(world_env)

func _spawn_terrain() -> void:
	var mat := vertex_color_material()
	for cz in WORLD_DEPTH / TERRAIN_CHUNK_SIZE:
		for cx in WORLD_WIDTH / TERRAIN_CHUNK_SIZE:
			var mi := MeshInstance3D.new()
			mi.mesh = build_chunk_mesh(cx, cz)
			mi.material_override = mat
			add_child(mi)

func _multimesh_node(mesh: Mesh, count: int) -> MultiMesh:
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = mesh
	mm.instance_count = count
	var node := MultiMeshInstance3D.new()
	node.multimesh = mm
	add_child(node)
	return mm

func box_mesh(size: Vector3, color: Color) -> ArrayMesh:
	var m := MeshData.new()
	push_box(m, Vector3.ZERO, size * 0.5, color)
	return m.into_mesh()

func _spawn_decorations() -> void:
	_rng_state = WORLD_SEED ^ 0xDEC00001
	var mat := vertex_color_material()

	# Trees (3 canopy variants -> 3 canopy multimeshes, like the 3 materials)
	var trunk_mm := _multimesh_node(box_mesh(Vector3(0.35, 1.0, 0.35), Color(0.35, 0.24, 0.14)), TREE_COUNT)
	var canopy_colors := [Color(0.16, 0.38, 0.16), Color(0.22, 0.45, 0.18), Color(0.30, 0.48, 0.14)]
	var canopy_mms: Array[MultiMesh] = []
	for c in canopy_colors:
		canopy_mms.push_back(_multimesh_node(box_mesh(Vector3(1.8, 1.6, 1.8), c), TREE_COUNT))
	var canopy_counts := [0, 0, 0]
	var placed := 0
	var attempts := 0
	while placed < TREE_COUNT and attempts < TREE_COUNT * 30:
		attempts += 1
		_rng_state = splitmix64_step(_rng_state)
		var x := float(umod(_rng_out, WORLD_WIDTH)) + 0.5
		_rng_state = splitmix64_step(_rng_state)
		var z := float(umod(_rng_out, WORLD_DEPTH)) + 0.5
		var i := int(z) * WORLD_WIDTH + int(x)
		if types[i] != GRASS:
			continue
		var variant := variants[i]
		if (variant * 7 + (int(x) / 16) * 13 + (int(z) / 16) * 29) % 5 > 1:
			continue
		var y := float(heights[i])
		_rng_state = splitmix64_step(_rng_state)
		var ci := umod(_rng_out, 3)
		trunk_mm.set_instance_transform(placed, Transform3D(Basis(), Vector3(x, y + 0.5, z)))
		var cmm: MultiMesh = canopy_mms[ci]
		cmm.set_instance_transform(canopy_counts[ci], Transform3D(Basis(), Vector3(x, y + 1.8, z)))
		canopy_counts[ci] += 1
		placed += 1
	trunk_mm.visible_instance_count = placed
	for ci in 3:
		canopy_mms[ci].visible_instance_count = canopy_counts[ci]

	# Rocks
	var rock_mm := _multimesh_node(box_mesh(Vector3(1.0, 0.7, 1.2), Color(0.45, 0.45, 0.44)), ROCK_COUNT)
	placed = 0
	attempts = 0
	while placed < ROCK_COUNT and attempts < ROCK_COUNT * 30:
		attempts += 1
		_rng_state = splitmix64_step(_rng_state)
		var x := float(umod(_rng_out, WORLD_WIDTH)) + 0.5
		_rng_state = splitmix64_step(_rng_state)
		var z := float(umod(_rng_out, WORLD_DEPTH)) + 0.5
		var i := int(z) * WORLD_WIDTH + int(x)
		if types[i] != ROCK and types[i] != EARTH and types[i] != DRY_GRASS:
			continue
		rock_mm.set_instance_transform(placed, Transform3D(Basis(), Vector3(x, heights[i] + 0.35, z)))
		placed += 1
	rock_mm.visible_instance_count = placed

	# Houses
	var wall_mm := _multimesh_node(box_mesh(Vector3(3.0, 2.5, 4.0), Color(0.75, 0.68, 0.55)), HOUSE_COUNT)
	var roof_mm := _multimesh_node(box_mesh(Vector3(3.4, 0.8, 4.4), Color(0.60, 0.25, 0.18)), HOUSE_COUNT)
	placed = 0
	attempts = 0
	while placed < HOUSE_COUNT and attempts < HOUSE_COUNT * 60:
		attempts += 1
		_rng_state = splitmix64_step(_rng_state)
		var x := float(umod(_rng_out, WORLD_WIDTH - 8)) + 4.0
		_rng_state = splitmix64_step(_rng_state)
		var z := float(umod(_rng_out, WORLD_DEPTH - 8)) + 4.0
		var i := int(z) * WORLD_WIDTH + int(x)
		if types[i] != GRASS:
			continue
		var y := float(heights[i])
		wall_mm.set_instance_transform(placed, Transform3D(Basis(), Vector3(x, y + 1.25, z)))
		roof_mm.set_instance_transform(placed, Transform3D(Basis(), Vector3(x, y + 2.9, z)))
		placed += 1
	wall_mm.visible_instance_count = placed
	roof_mm.visible_instance_count = placed

	# Give every multimesh node the shared vertex-color material.
	for node in get_children():
		if node is MultiMeshInstance3D:
			node.material_override = mat

func _spawn_movers() -> void:
	mover_x.resize(MOVER_COUNT)
	mover_z.resize(MOVER_COUNT)
	mover_y.resize(MOVER_COUNT)
	mover_heading.resize(MOVER_COUNT)
	mover_speed.resize(MOVER_COUNT)
	mover_retarget.resize(MOVER_COUNT)
	mover_rng.resize(MOVER_COUNT)

	var mat := vertex_color_material()
	var per_variant := MOVER_COUNT / PALETTE.size()
	for c in PALETTE:
		var mm := _multimesh_node(person_mesh(c), per_variant)
		mover_multimeshes.push_back(mm)
	for node in get_children():
		if node is MultiMeshInstance3D and node.material_override == null:
			node.material_override = mat

	_rng_state = WORLD_SEED ^ 0x0DDBA115
	var spawned := 0
	while spawned < MOVER_COUNT:
		var x := rand01() * (WORLD_WIDTH - 2.0) + 1.0
		var z := rand01() * (WORLD_DEPTH - 2.0) + 1.0
		if is_water(x, z):
			continue
		mover_x[spawned] = x
		mover_z[spawned] = z
		mover_y[spawned] = height_at(x, z)
		mover_heading[spawned] = rand01() * TAU
		mover_speed[spawned] = MOVER_MIN_SPEED + rand01() * (MOVER_MAX_SPEED - MOVER_MIN_SPEED)
		_rng_state = splitmix64_step(_rng_state)
		mover_rng[spawned] = _rng_out
		mover_retarget[spawned] = 1.0 + rand01() * 4.0
		spawned += 1
	# Populate initial transforms so the first frame is correct.
	_move_movers(0.0)

var _screenshot_taken := false

func _process(delta: float) -> void:
	_move_movers(delta)
	_update_overlay(delta)
	# `godot --path . -- --screenshot` saves a frame after warm-up and quits —
	# lets headless agents verify visuals without a visible window.
	if not _screenshot_taken and Time.get_ticks_msec() > 4000 \
			and OS.get_cmdline_user_args().has("--screenshot"):
		_screenshot_taken = true
		get_viewport().get_texture().get_image().save_png("res://screenshot.png")
		print("screenshot saved at %d ms" % Time.get_ticks_msec())
		get_tree().quit()

func _move_movers(dt: float) -> void:
	var margin := 2.0
	var max_x := WORLD_WIDTH - margin
	var max_z := WORLD_DEPTH - margin
	var per_variant := MOVER_COUNT / PALETTE.size()
	for i in MOVER_COUNT:
		mover_retarget[i] -= dt
		if mover_retarget[i] <= 0.0:
			var state: int = mover_rng[i]
			state = splitmix64_step(state)
			mover_heading[i] += (float(lshr(_rng_out, 40)) / 16777216.0 - 0.5) * PI
			state = splitmix64_step(state)
			mover_retarget[i] = 1.5 + (float(lshr(_rng_out, 40)) / 16777216.0) * 4.0
			mover_rng[i] = state

		var step: float = mover_speed[i] * dt
		var next_x: float = mover_x[i] + cos(mover_heading[i]) * step
		var next_z: float = mover_z[i] + sin(mover_heading[i]) * step

		var bounced := false
		if next_x < margin or next_x > max_x:
			mover_heading[i] = PI - mover_heading[i]
			bounced = true
		if next_z < margin or next_z > max_z:
			mover_heading[i] = -mover_heading[i]
			bounced = true
		if not bounced and is_water(next_x, next_z):
			mover_heading[i] += PI
			bounced = true
		if bounced:
			next_x = mover_x[i]
			next_z = mover_z[i]

		mover_x[i] = next_x
		mover_z[i] = next_z
		mover_y[i] += (height_at(next_x, next_z) - mover_y[i]) * minf(dt * 12.0, 1.0)

		# Mover i renders as instance i/5 of multimesh i%5 (15000/5 = 3000 each).
		# NOTE: a MultiMesh.buffer bulk-upload variant was tried and measured
		# SLOWER (~98 vs ~117 FPS): 12 interpreted array stores per mover cost
		# more than one native set_instance_transform call. Bulk buffers only
		# pay off when the buffer is built natively (C# / GDExtension).
		mover_multimeshes[i % PALETTE.size()].set_instance_transform(
			i / PALETTE.size(),
			Transform3D(Basis(Vector3.UP, -mover_heading[i]), Vector3(next_x, mover_y[i], next_z))
		)

func _setup_overlay() -> void:
	var layer := CanvasLayer.new()
	add_child(layer)
	overlay = Label.new()
	overlay.position = Vector2(10, 8)
	overlay.add_theme_font_size_override("font_size", 13)
	layer.add_child(overlay)

func _update_overlay(dt: float) -> void:
	var fps := Engine.get_frames_per_second()
	fps_smoothed = fps if fps_smoothed < 0 else fps_smoothed + (fps - fps_smoothed) * 0.1
	overlay.text = "GDScript -> Godot %s -> %s\nFPS: %d\nFrame: %.1f ms\nMovers: %d\nWorld: %d x %d\nVisible scale: ~%d voxels" % [
		Engine.get_version_info().string.split(" ")[0],
		RenderingServer.get_current_rendering_method(),
		int(fps_smoothed), dt * 1000.0, MOVER_COUNT, WORLD_WIDTH, WORLD_DEPTH, int(TARGET_VISIBLE_VOXELS),
	]
	var elapsed := Time.get_ticks_msec() / 1000.0
	if elapsed >= next_log_s:
		next_log_s = elapsed + 5.0
		print("fps: %d (movers: %d)" % [int(fps_smoothed), MOVER_COUNT])
