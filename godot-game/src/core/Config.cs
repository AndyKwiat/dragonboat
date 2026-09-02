using Godot;
using System.Collections.Generic;

// World, camera, trading and UI tunables.
// Boat and combat knobs live next to their systems (BoatConfig, CombatConfig)
// because those are live-tunable; everything here is a constant.
public static class Config
{
    // One map cell is one world unit (~1 m); the terrain file's size is the
    // world's size. Which file, and how many cities, is game-config.yml.
    public const string CityNamesPath = "res://data/city-names.json";
    public const string ProductsPath = "res://data/products.json";
    // Clear water (cells) around the boat's start spot off a large city's shore.
    public const int BoatStartClearanceCells = 30;
    // Auto-docking: running the hull into a city opens its market. The hull
    // is three circles of Width/2 spread over Length/3, so its nose touches
    // land with the boat's centre ~7 units out; 8 is that plus a hair.
    public const float CityDockContactMargin = 8f;
    // After closing a market you get this long (real seconds) before the
    // same quay can pull you in again — on top of having to leave it first.
    public const float CityDockCooldownSec = 5f;

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

    // Column heights per tile id (world units): water is the ground at 0,
    // land rises out of it in small steps (the file's heightmap is ignored
    // for now), cities rise 3x again.
    // water, sand, grass, forest, rock, city
    public static readonly float[] TerrainHeights = { 0, 0.3f, 0.8f, 1f, 1.4f, 2.4f };

    // The nav mesh's knobs are NOT here: they live in nav/NavConfig.cs, which
    // is free of Godot types so the tests can compile the builder (docs/nav.md).

    // Terrain streaming (see TerrainChunks.cs).
    public const int CellsPerChunk = 32;
    public const int MaxVisibleChunks = 110;
    public const int MaxCachedChunks = 360;
    public const double BuildBudgetMs = 5;
    public const int MaxLodStep = 64;    // 8192 cells / (32 × 64) = 4 chunks a side when fully zoomed out
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
    public const float BoatFollowZoom = 3.95f;
    // Follow camera look-ahead: at top speed, sailing straight, the boat sits
    // this many screen px behind centre (scaled by speed, so world offset =
    // px * speedFrac / zoom); stationary = centred. The lead only builds up
    // while you're not steering: it ramps in over RampUpSec of straight sailing
    // and drops back over RampDownSec once you turn. SmoothSec is the 1/e time
    // constant of a final ease on the offset vector, and MaxPxPerSec caps how
    // fast the offset may move on screen (a long lead swinging with the
    // heading during a turn is what this tames).
    public const float FollowLookAheadPx = 520f;
    // The lead never brings the boat closer to a screen edge than this.
    public const float FollowLookAheadMarginPx = 160f;
    // Below this fraction of TerminalSpeed the boat doesn't count as under way:
    // straightness (and so the look-ahead) ramps down, not up.
    public const float FollowLookAheadMinSpeedFrac = 0.2f;
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
    // How far off the world base price a market has to be before it is worth
    // a line under the label, and how many lines a city gets at most.
    public const double CityIntelMinDeviation = 0.2;
    public const int CityIntelMaxLines = 3;
    public const float CityHitRadiusPx = 26f;
    public const float CityHitPadPx = 10f;

    // Market. Prices are supply-driven (src/trading/Supply.cs): a market's
    // stock runs from -MarketSize (severe shortage, the price at the ceiling)
    // through 0 (the city's own settled LocalBase price) to +MarketSize (a
    // glut, the price at the floor). Buying and selling move it one unit at a
    // time; a tenth of whatever is left decays away every midnight.
    public static readonly MarketTerms Market = new()
    {
        SmallSize = 300, MediumSize = 3000, LargeSize = 12000,
        MaxOfBase = 2.5,      // the shortage ceiling, of the WORLD base price
        GlutFactor = 0.8,     // the glut floor, of the city's local price
        BuyMarkup = 1.1,
        DecayPerDay = 0.9,
        UniversalProductId = RationsProductId,
    };

    // Story events. How many of each kind the world holds is
    // game-config.yml's priceEvents / vipEvents (GameConfig).
    public const int StoryEventMinDays = 5, StoryEventMaxDays = 10;
    // How hard an event shoves the city's stock level, as a fraction of its
    // market size; the daily decay walks it back from there.
    public const double StoryEventShockFraction = 0.8;
    // Shortages (dear) are this many times likelier than gluts (cheap).
    public const int StoryEventShortageOddsToOne = 3;
    // VIP delivery events: a city hosts a passenger bound for another city
    // ranked 6th..10th nearest (never the 5 closest, never beyond the 10th).
    public const int VipMinRank = 6, VipMaxRank = 10;
    public const int VipDeliveryMoney = 200, VipDeliveryHonour = 10;
    // On top of that flat reward, a bonus for a fast passage — and a heavy
    // price for a passenger left aboard too long (src/trading/Vips.cs).
    public static readonly VipTerms Vips = new()
    {
        SpeedHonour = 200,        // delivered within a day of coming aboard
        SpeedHonourPerDay = 20,   // less for every further day, down to none
        AbandonDays = 15,         // then they jump ship
        AbandonHonour = 1000,
        WarnDays = 3,
    };
    // Nearest other-city events shown (and discovered) in a city's popup.
    public const int RumourCount = 3;
    // A city has to be paying at least this many times the base price before
    // giving the goods away is offered instead of selling them.
    public const double MarketGiveMinRatio = 1.5;
    public static readonly Color MarketDearColor = new Color("#ff6b6b");
    public static readonly Color MarketCheapColor = new Color("#6bd97a");
    public static readonly Color MarketNormalColor = new Color("#ffffff");

    // How a quote is tinted, from Market.Read: dear is red, cheap is green,
    // near enough the world average is left plain. One rule, used by both the
    // market panel and the map labels.
    public static Color MarketTint(Market.PriceReading r) =>
        !r.OffAverage ? MarketNormalColor : r.Dear ? MarketDearColor : MarketCheapColor;
    public static readonly Color MarketProducerColor = new Color("#f0b429");
    public static readonly Color MarketDebugColor = new Color("#5a5a66");

    // Banking (medium and large cities only; rules in src/banking/Loans.cs).
    // Credit is honour x HonourMultiplier, capped by the city's ceiling;
    // a loan is repaid in full plus the interest, TermDays after it was taken.
    public static readonly LoanTerms Bank = new()
    {
        HonourMultiplier = 10,
        MaxMedium = 5000,
        MaxLarge = 50000,
        Interest = 0.2,
        TermDays = 30,
        LatePenaltyDivisor = 10,     // honour lost per penalty = principal / 10
        LatePenaltyEveryDays = 5,    // one penalty a day late, then every 5 days
        OnTimeHonourDivisor = 50,    // honour earned for paying up on time = principal / 50
        WarnDays = 3,
        Step = 100,                  // borrowing is rounded down to whole hundreds
    };
    // The debt tag under a city's name label, and the loan lines in the clock.
    public static readonly Color BankDebtColor = new Color("#ff9f43");
    public const int BankPopupWidthPx = 560;

    // Event log (bottom left): world news — late-payment honour losses, loans
    // coming due. Lines fade out after this many real seconds.
    public const float EventLogSeconds = 14f;
    public const int EventLogMaxLines = 6;
    public const int EventLogFontPx = 15;

    // Religion (src/religion/Monasteries.cs): houses of monks in one small
    // city in five, where alms buy piety and monks ask for passage.
    public static readonly ReligionTerms Religion = new()
    {
        SmallCitiesPerMonastery = 5,
        GiveMin = 100, GiveMax = 2000, GiveStep = 100,
        GivePietyDivisor = 10,       // 200 money given = 20 piety
        GiveCooldownDays = 7,        // per house
        MonkTermDays = 30,
        MonkPiety = 1000,            // landed in time
        MonkPenalty = 1000,          // he gives up and walks away
        MonkWarnDays = 3,
        MonkOfferOddsToOne = 10,     // appear/lapse odds — ~half the houses have an errand
    };
    public static readonly Color PietyColor = new Color("#c9a7ff");

    // Player.
    public const int StartingMoney = 500;
    public static readonly Dictionary<string, int> StartingInventory = new() { ["RICE"] = 20 };

    // Game time.
    public const int StartMonth = 1, StartDay = 1, StartHour = 8, StartMinute = 0;
    public const float HoursPerSecondSailing = 1f;
    // Fast-forward: hold either Shift to speed the clock. The rate ramps
    // from 1x to FastForwardMaxRate over FastForwardRampUpSec of holding,
    // and drains back over FastForwardRampDownSec on release so the return
    // to normal time isn't jarring.
    public const float FastForwardMaxRate = 5f;
    public const float FastForwardRampUpSec = 1.5f;
    public const float FastForwardRampDownSec = 1f;
    public const int NightStartHour = 20, NightEndHour = 5;
    // Crew stamina bar (top centre, ui/StaminaBar.cs): the sail/oar caption
    // and a bar that empties as the rowers tire. Flashes once spent.
    public static readonly Vector2 StaminaBarPx = new Vector2(260, 10);
    public const int StaminaFontPx = 13;
    public static readonly Color StaminaTrackColor = new Color("#2a2f38");
    public static readonly Color StaminaFullColor = new Color("#6bd97a");
    public static readonly Color StaminaEmptyColor = new Color("#ff6b6b");
    public const float StaminaFlashRadPerSec = 6f;

    public static readonly Color DayColor = new Color("#f2e9d8");
    public static readonly Color NightColor = new Color("#4a5568");
    public const int ClockFontPx = 20;

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
    public const float PopupMaxHeightVh = 100f;
    public const int PopupFontPx = 16;
    public const int PopupTitleFontPx = 28;

    // Wind dial (bottom left, ui/WindDial.cs): an arrow pointing the way the
    // wind blows, projected through the camera rig so it lines up with the
    // map, over a ring; the colour warms as the force approaches
    // BoatConfig.SailFullWind (the same reference the sails use).
    public const int WindDialSizePx = 84;
    public const int WindDialFontPx = 12;
    public const float WindDialHeadPx = 13f;
    public const int WindDialTickFontPx = 9;
    public const float WindDialProbeUnits = 200f;   // how far out the direction is projected
    public static readonly Color WindDialRingColor = new Color("#4a5568");
    public static readonly Color WindDialCalmColor = new Color("#8fa3bf");
    public static readonly Color WindDialBlowColor = new Color("#7fe3ff");

    // Minimap.
    public const int MinimapSizePx = 240;
    public const int MinimapMargin = 12;
    public static readonly Color MinimapRectColor = new Color("#ffffff");
    public const float MinimapWorldFraction = 0.5f;

    // Indexed by tile id: water, sand, grass, forest, rock, city
    public static readonly Color[] TerrainColors =
    {
        new Color(0x1a6dd4FF), new Color(0xdec882FF), new Color(0x4a9e4aFF), new Color(0x266626FF),
        new Color(0x888888FF), new Color(0xcc2222FF),
    };
}
