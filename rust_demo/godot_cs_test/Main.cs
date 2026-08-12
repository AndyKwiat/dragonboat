using Godot;
using System.Collections.Generic;

// C# port of the Rust/Bevy voxel demo in ../src (and the GDScript port in
// ../godot_test) — same seed, same SplitMix64/value-noise terrain, same
// entity counts and wander rules, for an FPS / iteration-speed comparison.
// Movers use the MultiMesh.Buffer bulk upload, which pays off in C# (native
// float[] writes) where it did not in GDScript.
public partial class Main : Node3D
{
    // config.rs
    const int WorldWidth = 256;
    const int WorldDepth = 256;
    const ulong WorldSeed = 12345;
    const float TargetVisibleVoxels = 200.0f;
    const int TerrainMaxHeight = 8;
    const int WaterLevelC = 1;
    const int TerrainChunkSize = 16;
    const int MoverCount = 15000;
    const float MoverMinSpeed = 2.0f;
    const float MoverMaxSpeed = 6.0f;
    const int TreeCount = 220;
    const int RockCount = 60;
    const int HouseCount = 180;
    const float CameraAzimuthDegrees = 45.0f;
    const float CameraElevationDegrees = 38.0f;
    const float BodyHeight = 0.9f;

    // ------------------------------------------------------------------
    // world.rs — SplitMix64 + value noise (real u64 math this time)
    // ------------------------------------------------------------------
    static ulong Splitmix64(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    static float Hash01(ulong seed, long x, long z)
    {
        ulong s = seed ^ ((ulong)x * 0x517CC1B727220A95UL) ^ ((ulong)z * 0x2545F4914F6CDD1DUL);
        return (Splitmix64(ref s) >> 40) / 16777216.0f;
    }

    static float Smooth(float t) => t * t * (3f - 2f * t);

    static float ValueNoise(ulong seed, float x, float z)
    {
        float x0 = Mathf.Floor(x), z0 = Mathf.Floor(z);
        float tx = Smooth(x - x0), tz = Smooth(z - z0);
        long xi = (long)x0, zi = (long)z0;
        float a = Hash01(seed, xi, zi);
        float b = Hash01(seed, xi + 1, zi);
        float c = Hash01(seed, xi, zi + 1);
        float d = Hash01(seed, xi + 1, zi + 1);
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    static float Fbm(ulong seed, float x, float z)
    {
        float total = 0, amplitude = 1, frequency = 1, norm = 0;
        for (ulong octave = 0; octave < 3; octave++)
        {
            total += ValueNoise(seed ^ (octave * 0x9E37), x * frequency, z * frequency) * amplitude;
            norm += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }
        return total / norm;
    }

    enum Terrain : byte { Water, Sand, Grass, DryGrass, Earth, Rock }

    byte[] _heights = new byte[WorldWidth * WorldDepth];
    byte[] _types = new byte[WorldWidth * WorldDepth];
    byte[] _variants = new byte[WorldWidth * WorldDepth];

    void GenerateWorld()
    {
        for (int z = 0; z < WorldDepth; z++)
        {
            for (int x = 0; x < WorldWidth; x++)
            {
                float baseN = Fbm(WorldSeed, x * 0.02f, z * 0.02f);
                float hills = Fbm(WorldSeed ^ 0xABCD, x * 0.008f, z * 0.008f);
                float h = baseN * 3.5f + hills * 6.0f - 1.2f;
                float lake = Mathf.Sqrt((x - 70f) * (x - 70f) + (z - 170f) * (z - 170f));
                if (lake < 42f)
                    h -= (1f - lake / 42f) * 4f;
                int height = Mathf.Clamp(Mathf.RoundToInt(h), 0, TerrainMaxHeight);
                float variantRoll = Hash01(WorldSeed ^ 0xC0FFEE, x, z);
                float moisture = Fbm(WorldSeed ^ 0x5EED, x * 0.015f, z * 0.015f);
                Terrain type;
                if (height <= WaterLevelC) { type = Terrain.Water; height = WaterLevelC; }
                else if (height == WaterLevelC + 1) type = Terrain.Sand;
                else if (height >= TerrainMaxHeight - 1) type = Terrain.Rock;
                else if (moisture < 0.35f) type = Terrain.DryGrass;
                else if (variantRoll > 0.93f) type = Terrain.Earth;
                else type = Terrain.Grass;
                int i = z * WorldWidth + x;
                _heights[i] = (byte)height;
                _types[i] = (byte)type;
                _variants[i] = (byte)(variantRoll * 255f);
            }
        }
    }

    float HeightAt(float x, float z)
    {
        int xi = Mathf.Clamp((int)Mathf.Floor(x), 0, WorldWidth - 1);
        int zi = Mathf.Clamp((int)Mathf.Floor(z), 0, WorldDepth - 1);
        return _heights[zi * WorldWidth + xi];
    }

    bool IsWater(float x, float z)
    {
        int xi = Mathf.Clamp((int)Mathf.Floor(x), 0, WorldWidth - 1);
        int zi = Mathf.Clamp((int)Mathf.Floor(z), 0, WorldDepth - 1);
        return _types[zi * WorldWidth + xi] == (byte)Terrain.Water;
    }

    // ------------------------------------------------------------------
    // terrain.rs — palette + chunk meshing
    // ------------------------------------------------------------------
    static readonly Color[] BaseColors =
    {
        new(0.16f, 0.32f, 0.55f), new(0.76f, 0.70f, 0.50f), new(0.26f, 0.46f, 0.21f),
        new(0.50f, 0.48f, 0.24f), new(0.45f, 0.36f, 0.24f), new(0.52f, 0.52f, 0.50f),
    };

    Color CellColor(byte type, byte height, byte variant)
    {
        Color c = BaseColors[type];
        float shade = 0.92f + (variant / 255f) * 0.16f + ((float)height / TerrainMaxHeight) * 0.12f;
        return new Color(c.R * shade, c.G * shade, c.B * shade);
    }

    int NeighborHeight(int x, int z) =>
        (x < 0 || z < 0 || x >= WorldWidth || z >= WorldDepth) ? 0 : _heights[z * WorldWidth + x];

    class MeshData
    {
        public List<Vector3> Positions = new();
        public List<Vector3> Normals = new();
        public List<Color> Colors = new();
        public List<int> Indices = new();

        public void Quad(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 normal, Color color)
        {
            int b = Positions.Count;
            Positions.AddRange(new[] { v0, v1, v2, v3 });
            for (int i = 0; i < 4; i++) { Normals.Add(normal); Colors.Add(color); }
            // Godot front faces wind clockwise (opposite of Bevy/three.js).
            Indices.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
        }

        public ArrayMesh IntoMesh()
        {
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = Positions.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = Normals.ToArray();
            arrays[(int)Mesh.ArrayType.Color] = Colors.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = Indices.ToArray();
            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }

    ArrayMesh BuildChunkMesh(int cx, int cz)
    {
        var m = new MeshData();
        int x0 = cx * TerrainChunkSize, z0 = cz * TerrainChunkSize;
        for (int z = z0; z < Mathf.Min(z0 + TerrainChunkSize, WorldDepth); z++)
        {
            for (int x = x0; x < Mathf.Min(x0 + TerrainChunkSize, WorldWidth); x++)
            {
                int i = z * WorldWidth + x;
                float h = _heights[i];
                Color top = CellColor(_types[i], _heights[i], _variants[i]);
                Color side = new(top.R * 0.72f, top.G * 0.72f, top.B * 0.72f);
                m.Quad(new(x, h, z), new(x, h, z + 1), new(x + 1, h, z + 1), new(x + 1, h, z),
                    Vector3.Up, top);
                // Side faces down to any lower neighbor.
                float nh = NeighborHeight(x - 1, z);
                if (nh < h) m.Quad(new(x, h, z), new(x, nh, z), new(x, nh, z + 1), new(x, h, z + 1), Vector3.Left, side);
                nh = NeighborHeight(x + 1, z);
                if (nh < h) m.Quad(new(x + 1, h, z + 1), new(x + 1, nh, z + 1), new(x + 1, nh, z), new(x + 1, h, z), Vector3.Right, side);
                nh = NeighborHeight(x, z - 1);
                if (nh < h) m.Quad(new(x + 1, h, z), new(x + 1, nh, z), new(x, nh, z), new(x, h, z), Vector3.Forward, side);
                nh = NeighborHeight(x, z + 1);
                if (nh < h) m.Quad(new(x, h, z + 1), new(x, nh, z + 1), new(x + 1, nh, z + 1), new(x + 1, h, z + 1), Vector3.Back, side);
            }
        }
        return m.IntoMesh();
    }

    StandardMaterial3D VertexColorMaterial()
    {
        return new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = 1.0f,
        };
    }

    // ------------------------------------------------------------------
    // Mover mesh: body prism + head cube, vertex-colored
    // ------------------------------------------------------------------
    static readonly Color SkinColor = new(0.87f, 0.72f, 0.58f);
    static readonly Color[] Palette =
    {
        new(0.85f, 0.25f, 0.20f), new(0.20f, 0.45f, 0.85f), new(0.90f, 0.75f, 0.20f),
        new(0.85f, 0.85f, 0.88f), new(0.30f, 0.25f, 0.35f),
    };

    static void PushBox(MeshData m, Vector3 c, Vector3 h, Color color)
    {
        m.Quad(c + new Vector3(-h.X, h.Y, -h.Z), c + new Vector3(-h.X, h.Y, h.Z), c + new Vector3(h.X, h.Y, h.Z), c + new Vector3(h.X, h.Y, -h.Z), Vector3.Up, color);
        m.Quad(c + new Vector3(-h.X, -h.Y, h.Z), c + new Vector3(-h.X, -h.Y, -h.Z), c + new Vector3(h.X, -h.Y, -h.Z), c + new Vector3(h.X, -h.Y, h.Z), Vector3.Down, color);
        m.Quad(c + new Vector3(h.X, h.Y, h.Z), c + new Vector3(h.X, -h.Y, h.Z), c + new Vector3(h.X, -h.Y, -h.Z), c + new Vector3(h.X, h.Y, -h.Z), Vector3.Right, color);
        m.Quad(c + new Vector3(-h.X, h.Y, -h.Z), c + new Vector3(-h.X, -h.Y, -h.Z), c + new Vector3(-h.X, -h.Y, h.Z), c + new Vector3(-h.X, h.Y, h.Z), Vector3.Left, color);
        m.Quad(c + new Vector3(-h.X, h.Y, h.Z), c + new Vector3(-h.X, -h.Y, h.Z), c + new Vector3(h.X, -h.Y, h.Z), c + new Vector3(h.X, h.Y, h.Z), Vector3.Back, color);
        m.Quad(c + new Vector3(h.X, h.Y, -h.Z), c + new Vector3(h.X, -h.Y, -h.Z), c + new Vector3(-h.X, -h.Y, -h.Z), c + new Vector3(-h.X, h.Y, -h.Z), Vector3.Forward, color);
    }

    static ArrayMesh PersonMesh(Color bodyColor)
    {
        var m = new MeshData();
        PushBox(m, new(0, BodyHeight * 0.5f, 0), new(0.25f, BodyHeight * 0.5f, 0.175f), bodyColor);
        PushBox(m, new(0, BodyHeight + 0.2f, 0), new(0.2f, 0.2f, 0.2f), SkinColor);
        return m.IntoMesh();
    }

    static ArrayMesh BoxMesh(Vector3 size, Color color)
    {
        var m = new MeshData();
        PushBox(m, Vector3.Zero, size * 0.5f, color);
        return m.IntoMesh();
    }

    // ------------------------------------------------------------------
    // Movers state (SoA) + 5 MultiMeshes with bulk buffer upload
    // ------------------------------------------------------------------
    readonly float[] _mx = new float[MoverCount];
    readonly float[] _mz = new float[MoverCount];
    readonly float[] _my = new float[MoverCount];
    readonly float[] _mHeading = new float[MoverCount];
    readonly float[] _mSpeed = new float[MoverCount];
    readonly float[] _mRetarget = new float[MoverCount];
    readonly ulong[] _mRng = new ulong[MoverCount];
    readonly MultiMesh[] _moverMms = new MultiMesh[5];
    // One buffer per variant: (MoverCount/5) instances x 12 floats (3x4 row-major).
    readonly float[][] _buffers = new float[5][];

    Label _overlay;
    double _fpsSmoothed = -1;
    double _nextLogS;
    bool _screenshotTaken;

    public override void _Ready()
    {
        GenerateWorld();
        SetupCameraAndLight();
        SpawnTerrain();
        SpawnDecorations();
        SpawnMovers();
        SetupOverlay();
        GD.Print($"scene ready in {Time.GetTicksMsec()} ms");
    }

    void SetupCameraAndLight()
    {
        var center = new Vector3(WorldWidth * 0.5f, 0, WorldDepth * 0.5f);
        float azimuth = Mathf.DegToRad(CameraAzimuthDegrees);
        float elevation = Mathf.DegToRad(CameraElevationDegrees);
        var camera = new Camera3D
        {
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = TargetVisibleVoxels * Mathf.Sin(elevation),
            Near = 1.0f,
            Far = 1200.0f,
        };
        var offset = new Vector3(
            Mathf.Cos(elevation) * Mathf.Cos(azimuth),
            Mathf.Sin(elevation),
            Mathf.Cos(elevation) * Mathf.Sin(azimuth)) * 400f;
        camera.Position = center + offset;
        AddChild(camera);
        camera.LookAt(center, Vector3.Up);

        var sun = new DirectionalLight3D { LightEnergy = 1.3f, ShadowEnabled = true };
        AddChild(sun);
        sun.LookAtFromPosition(new Vector3(100, 200, 40), Vector3.Zero, Vector3.Up);

        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.10f, 0.12f, 0.16f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.85f, 0.9f, 1.0f),
            AmbientLightEnergy = 0.6f,
        };
        AddChild(new WorldEnvironment { Environment = env });
    }

    void SpawnTerrain()
    {
        var mat = VertexColorMaterial();
        for (int cz = 0; cz < WorldDepth / TerrainChunkSize; cz++)
            for (int cx = 0; cx < WorldWidth / TerrainChunkSize; cx++)
                AddChild(new MeshInstance3D { Mesh = BuildChunkMesh(cx, cz), MaterialOverride = mat });
    }

    MultiMesh MultimeshNode(Mesh mesh, int count, Material mat)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = count };
        AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat });
        return mm;
    }

    void SpawnDecorations()
    {
        ulong rng = WorldSeed ^ 0xDEC00001;
        var mat = VertexColorMaterial();

        var trunkMm = MultimeshNode(BoxMesh(new(0.35f, 1.0f, 0.35f), new(0.35f, 0.24f, 0.14f)), TreeCount, mat);
        Color[] canopyColors = { new(0.16f, 0.38f, 0.16f), new(0.22f, 0.45f, 0.18f), new(0.30f, 0.48f, 0.14f) };
        var canopyMms = new MultiMesh[3];
        for (int c = 0; c < 3; c++)
            canopyMms[c] = MultimeshNode(BoxMesh(new(1.8f, 1.6f, 1.8f), canopyColors[c]), TreeCount, mat);
        int[] canopyCounts = new int[3];
        int placed = 0, attempts = 0;
        while (placed < TreeCount && attempts < TreeCount * 30)
        {
            attempts++;
            float x = Splitmix64(ref rng) % WorldWidth + 0.5f;
            float z = Splitmix64(ref rng) % WorldDepth + 0.5f;
            int i = (int)z * WorldWidth + (int)x;
            if (_types[i] != (byte)Terrain.Grass) continue;
            int variant = _variants[i];
            if ((variant * 7 + ((int)x / 16) * 13 + ((int)z / 16) * 29) % 5 > 1) continue;
            float y = _heights[i];
            int ci = (int)(Splitmix64(ref rng) % 3);
            trunkMm.SetInstanceTransform(placed, new Transform3D(Basis.Identity, new(x, y + 0.5f, z)));
            canopyMms[ci].SetInstanceTransform(canopyCounts[ci], new Transform3D(Basis.Identity, new(x, y + 1.8f, z)));
            canopyCounts[ci]++;
            placed++;
        }
        trunkMm.VisibleInstanceCount = placed;
        for (int c = 0; c < 3; c++) canopyMms[c].VisibleInstanceCount = canopyCounts[c];

        var rockMm = MultimeshNode(BoxMesh(new(1.0f, 0.7f, 1.2f), new(0.45f, 0.45f, 0.44f)), RockCount, mat);
        placed = 0; attempts = 0;
        while (placed < RockCount && attempts < RockCount * 30)
        {
            attempts++;
            float x = Splitmix64(ref rng) % WorldWidth + 0.5f;
            float z = Splitmix64(ref rng) % WorldDepth + 0.5f;
            int i = (int)z * WorldWidth + (int)x;
            if (_types[i] != (byte)Terrain.Rock && _types[i] != (byte)Terrain.Earth && _types[i] != (byte)Terrain.DryGrass) continue;
            rockMm.SetInstanceTransform(placed, new Transform3D(Basis.Identity, new(x, _heights[i] + 0.35f, z)));
            placed++;
        }
        rockMm.VisibleInstanceCount = placed;

        var wallMm = MultimeshNode(BoxMesh(new(3.0f, 2.5f, 4.0f), new(0.75f, 0.68f, 0.55f)), HouseCount, mat);
        var roofMm = MultimeshNode(BoxMesh(new(3.4f, 0.8f, 4.4f), new(0.60f, 0.25f, 0.18f)), HouseCount, mat);
        placed = 0; attempts = 0;
        while (placed < HouseCount && attempts < HouseCount * 60)
        {
            attempts++;
            float x = Splitmix64(ref rng) % (ulong)(WorldWidth - 8) + 4.0f;
            float z = Splitmix64(ref rng) % (ulong)(WorldDepth - 8) + 4.0f;
            int i = (int)z * WorldWidth + (int)x;
            if (_types[i] != (byte)Terrain.Grass) continue;
            float y = _heights[i];
            wallMm.SetInstanceTransform(placed, new Transform3D(Basis.Identity, new(x, y + 1.25f, z)));
            roofMm.SetInstanceTransform(placed, new Transform3D(Basis.Identity, new(x, y + 2.9f, z)));
            placed++;
        }
        wallMm.VisibleInstanceCount = placed;
        roofMm.VisibleInstanceCount = placed;
    }

    void SpawnMovers()
    {
        var mat = VertexColorMaterial();
        int perVariant = MoverCount / Palette.Length;
        for (int v = 0; v < Palette.Length; v++)
        {
            _moverMms[v] = MultimeshNode(PersonMesh(Palette[v]), perVariant, mat);
            _buffers[v] = new float[perVariant * 12];
        }

        ulong rng = WorldSeed ^ 0x0DDBA115;
        int spawned = 0;
        while (spawned < MoverCount)
        {
            float x = (Splitmix64(ref rng) >> 40) / 16777216.0f * (WorldWidth - 2f) + 1f;
            float z = (Splitmix64(ref rng) >> 40) / 16777216.0f * (WorldDepth - 2f) + 1f;
            if (IsWater(x, z)) continue;
            _mx[spawned] = x;
            _mz[spawned] = z;
            _my[spawned] = HeightAt(x, z);
            _mHeading[spawned] = (Splitmix64(ref rng) >> 40) / 16777216.0f * Mathf.Tau;
            _mSpeed[spawned] = MoverMinSpeed + (Splitmix64(ref rng) >> 40) / 16777216.0f * (MoverMaxSpeed - MoverMinSpeed);
            _mRng[spawned] = Splitmix64(ref rng);
            _mRetarget[spawned] = 1.0f + (Splitmix64(ref rng) >> 40) / 16777216.0f * 4.0f;
            spawned++;
        }
        MoveMovers(0);
    }

    public override void _Process(double delta)
    {
        MoveMovers((float)delta);
        UpdateOverlay(delta);
        if (!_screenshotTaken && Time.GetTicksMsec() > 4000
            && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--screenshot") >= 0)
        {
            _screenshotTaken = true;
            GetViewport().GetTexture().GetImage().SavePng("res://screenshot.png");
            GD.Print($"screenshot saved at {Time.GetTicksMsec()} ms");
            GetTree().Quit();
        }
    }

    void MoveMovers(float dt)
    {
        const float margin = 2.0f;
        const float maxX = WorldWidth - margin;
        const float maxZ = WorldDepth - margin;
        int perVariant = MoverCount / Palette.Length;

        for (int i = 0; i < MoverCount; i++)
        {
            _mRetarget[i] -= dt;
            if (_mRetarget[i] <= 0)
            {
                ulong state = _mRng[i];
                _mHeading[i] += ((Splitmix64(ref state) >> 40) / 16777216.0f - 0.5f) * Mathf.Pi;
                _mRetarget[i] = 1.5f + (Splitmix64(ref state) >> 40) / 16777216.0f * 4.0f;
                _mRng[i] = state;
            }

            float step = _mSpeed[i] * dt;
            float nextX = _mx[i] + Mathf.Cos(_mHeading[i]) * step;
            float nextZ = _mz[i] + Mathf.Sin(_mHeading[i]) * step;

            bool bounced = false;
            if (nextX < margin || nextX > maxX) { _mHeading[i] = Mathf.Pi - _mHeading[i]; bounced = true; }
            if (nextZ < margin || nextZ > maxZ) { _mHeading[i] = -_mHeading[i]; bounced = true; }
            if (!bounced && IsWater(nextX, nextZ)) { _mHeading[i] += Mathf.Pi; bounced = true; }
            if (bounced) { nextX = _mx[i]; nextZ = _mz[i]; }

            _mx[i] = nextX;
            _mz[i] = nextZ;
            _my[i] += (HeightAt(nextX, nextZ) - _my[i]) * Mathf.Min(dt * 12f, 1f);

            // Rotation by -heading about Y, written as a 3x4 row-major matrix.
            // Movers are contiguous per variant: variant i/3000, instance i%3000.
            float ch = Mathf.Cos(_mHeading[i]);
            float sh = Mathf.Sin(_mHeading[i]);
            float[] buf = _buffers[i / perVariant];
            int b = (i % perVariant) * 12;
            buf[b] = ch; buf[b + 1] = 0; buf[b + 2] = -sh; buf[b + 3] = nextX;
            buf[b + 4] = 0; buf[b + 5] = 1; buf[b + 6] = 0; buf[b + 7] = _my[i];
            buf[b + 8] = sh; buf[b + 9] = 0; buf[b + 10] = ch; buf[b + 11] = nextZ;
        }

        for (int v = 0; v < Palette.Length; v++)
            _moverMms[v].Buffer = _buffers[v];
    }

    void SetupOverlay()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        _overlay = new Label { Position = new Vector2(10, 8) };
        _overlay.AddThemeFontSizeOverride("font_size", 13);
        layer.AddChild(_overlay);
    }

    void UpdateOverlay(double dt)
    {
        double fps = Engine.GetFramesPerSecond();
        _fpsSmoothed = _fpsSmoothed < 0 ? fps : _fpsSmoothed + (fps - _fpsSmoothed) * 0.1;
        _overlay.Text =
            $"C# -> Godot {Engine.GetVersionInfo()["string"]} -> {RenderingServer.GetCurrentRenderingMethod()}\n" +
            $"FPS: {(int)_fpsSmoothed}\nFrame: {dt * 1000.0:F1} ms\nMovers: {MoverCount}\n" +
            $"World: {WorldWidth} x {WorldDepth}\nVisible scale: ~{(int)TargetVisibleVoxels} voxels";
        double elapsed = Time.GetTicksMsec() / 1000.0;
        if (elapsed >= _nextLogS)
        {
            _nextLogS = elapsed + 5.0;
            GD.Print($"fps: {(int)_fpsSmoothed} (movers: {MoverCount})");
        }
    }
}
