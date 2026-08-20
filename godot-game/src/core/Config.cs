using Godot;
using System.Collections.Generic;

// World, camera, trading and UI tunables, mirroring ../game/src/config.ts.
// Boat and combat knobs live next to their systems (BoatConfig, CombatConfig)
// because those are live-tunable; everything here is a constant.
public static class Config
{
    // World units per map cell — change this to resize the whole world.
    public const float TileScale = 20f;

    public const string MapPath = "res://data/map.dmap";
    public const string CitiesPath = "res://data/cities.json";
    public const string ProductsPath = "res://data/products.json";

    public const float MaxZoom = 20f;                 // screen px per world unit
    public const float ZoomWheelSensitivity = 0.0015f;
    public const float KeyPanPxPerSec = 600f;         // keyboard pan speed in screen px/sec

    // 3D camera rigs, cycled with V. zoom is still screen px per world unit,
    // measured at the focus point. pitch = angle between the view direction
    // and the ground (90 = top-down); yaw = rotation of the view about the
    // vertical (0 = screen-up is world north/-y, positive turns clockwise).
    public const string DefaultRig = "isometric";
    public static readonly string[] RigCycle = { "isometric", "perspective" };
    public record RigConfig(bool Perspective, float PitchDeg, float YawDeg, float FovDeg);
    public static readonly Dictionary<string, RigConfig> Rigs = new()
    {
        // Classic isometric pitch, orthographic, north a little left of straight up.
        ["isometric"] = new RigConfig(false, 35.264f, 23f, 0f),
        ["perspective"] = new RigConfig(true, 45f, 0f, 45f),
    };
    public const float OrthoDistance = 30000f;        // how far back the ortho camera sits (only clipping)

    // Column heights per terrain index (world units): water is the ground at
    // 0, land rises out of it, cities rise 3x again.
    // ocean, beach, grassland, forest, mountain, river, canal, city
    public static readonly float[] TerrainHeights = { 0, 3, 8, 10, 14, 0, 0, 24 };

    // Terrain streaming (see TerrainChunks.cs).
    public const int CellsPerChunk = 32;
    public const int MaxVisibleChunks = 110;
    public const int MaxCachedChunks = 360;
    public const double BuildBudgetMs = 5;
    public const int MaxLodStep = 16;
    public const float SideShadeX = 0.72f;
    public const float SideShadeY = 0.55f;
    public const int BoundsPadCells = 2;
    public const float MaxViewDistance = 40000f;

    // Grid overlay on the ground plane.
    public const float GridFadeMinPxPerTile = 8f;
    public const float GridFadeMaxPxPerTile = 24f;
    public const float GridMaxAlpha = 0.35f;
    public static readonly Color GridColor = new Color(0x444444FF);

    // Boat handling, size and lean live in boat/BoatConfig.cs; combat in
    // combat/CombatConfig.cs. Both are live-tunable (core/Tweaks.cs).
    public const float BoatFollowZoom = 2.5f;
    // Follow camera look-ahead: at top speed, sailing straight, the boat sits
    // this many screen px behind centre (scaled by speed, so world offset =
    // px * speedFrac / zoom); stationary = centred. The lead only builds up
    // while you're not steering: it ramps in over RampUpSec of straight sailing
    // and drops back over RampDownSec once you turn. SmoothSec is the 1/e time
    // constant of a final ease on the offset vector, and MaxPxPerSec caps how
    // fast the offset may move on screen (a long lead swinging with the
    // heading during a turn is what this tames).
    public const float FollowLookAheadPx = 520f;
    public const float FollowLookAheadMaxPxPerSec = 250f;
    public const float FollowLookAheadRampUpSec = 6f;
    public const float FollowLookAheadRampDownSec = 2f;
    public const float FollowLookAheadSmoothSec = 1.6f;
    // Edge camera: the view holds still until the boat comes within this many
    // screen px of a screen edge, then pans just enough to keep it inside.
    public const float EdgeCamMarginPx = 140f;

    // City labels: font size (px), weight, colour and the zoom below which
    // the label is hidden, per city size.
    public record LabelStyle(int FontPx, bool Bold, bool Heavy, Color Color, float MinZoom);
    public static readonly Dictionary<string, LabelStyle> CityLabel = new()
    {
        ["small"] = new LabelStyle(11, false, false, new Color("#e6e6e6"), 0.22f),
        ["medium"] = new LabelStyle(14, true, false, new Color("#f5f5f5"), 0.15f),
        ["large"] = new LabelStyle(20, true, true, new Color("#ffd166"), 0f),
    };
    public static readonly Color CityHoverColor = new Color("#7fe3ff");
    public const float CityLabelOffsetPx = 6f;
    // Price intel under a discovered city's name (see CityLabels.RefreshIntel).
    public const int CityIntelFontPx = 10;
    public const float CityIntelBoxPx = 220f;
    public const float CityHitRadiusPx = 26f;
    public const float CityHitPadPx = 10f;

    // Market.
    public const double MarketBuyMarkup = 1.1;
    public const double MarketSmallStapleDiscount = 0.6;   // a small city's one non-rice staple
    public const double MarketMediumLuxuryDiscount = 0.75; // a medium city's one luxury
    public static readonly HashSet<string> LuxuryProductIds = new() { "CLOTHES", "FURNITURE", "JEWELRY" };

    // Story events.
    public const int StoryEventCount = 40;
    public const int StoryEventMinDays = 7, StoryEventMaxDays = 21;
    public const double StoryEventLowMultiplier = 0.5, StoryEventHighMultiplier = 1.5;
    // High-price events are this many times likelier than low-price ones.
    public const int StoryEventHighOddsToOne = 2;
    // Nearest other-city events shown (and discovered) in a city's popup.
    public const int RumourCount = 3;
    public static readonly Color MarketDearColor = new Color("#ff6b6b");
    public static readonly Color MarketCheapColor = new Color("#6bd97a");
    public static readonly Color MarketNormalColor = new Color("#ffffff");

    // Player.
    public const int StartingMoney = 500;
    public static readonly Dictionary<string, int> StartingInventory = new() { ["RICE"] = 20 };

    // Game time.
    public const int StartMonth = 1, StartDay = 1, StartHour = 8, StartMinute = 0;
    public const float HoursPerSecondSailing = 1f;
    public const float ZoomTimeNearZoom = 3.5f, ZoomTimeFarZoom = 0.37f, ZoomTimeNearRate = 1f, ZoomTimeFarRate = 2.5f;
    public const int NightStartHour = 20, NightEndHour = 5;
    public static readonly Color DayColor = new Color("#f2e9d8");
    public static readonly Color NightColor = new Color("#4a5568");
    public const int ClockFontPx = 20;

    // Crew.
    public static readonly CrewLimits Crew = new()
    {
        Starting = 1, Min = 1, Max = 20, SpeedAtMin = 1, SpeedAtMax = 2, SpeedFullCrew = 10, CargoPerCrew = 100,
    };

    // Upkeep.
    public const string RationsProductId = "RICE";
    public const float RationsHoursPerUnit = 8f;
    public const int RationsLowUnits = 10;
    public static readonly Color LowColor = new Color("#ff6b6b");
    public const int WagesMoneyPerDay = 1;

    // City popup.
    public const float PopupWidthVw = 62f;
    public const float PopupMinWidthPx = 520f;
    public const float PopupMaxWidthPx = 900f;
    public const float PopupMaxHeightVh = 80f;
    public const int PopupFontPx = 16;
    public const int PopupTitleFontPx = 28;

    // Minimap.
    public const int MinimapSizePx = 240;
    public const int MinimapMargin = 12;
    public static readonly Color MinimapRectColor = new Color("#ffffff");
    public const float MinimapWorldFraction = 0.5f;

    // Indexed by terrain value: ocean, beach, grassland, forest, mountain, river, canal, city
    public static readonly Color[] TerrainColors =
    {
        new Color(0x1a6dd4FF), new Color(0xdec882FF), new Color(0x4a9e4aFF), new Color(0x266626FF),
        new Color(0x888888FF), new Color(0x1a6dd4FF), new Color(0x135ba1FF), new Color(0xcc2222FF),
    };
}
