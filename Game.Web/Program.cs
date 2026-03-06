using System.Runtime.InteropServices.JavaScript;
using SpaceExplorationGame.Audio;
using SpaceExplorationGame.Core;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Menu;
using Engine.Platform.Web;
using SpaceExplorationGame.Core.Config;
using Engine.Network;

namespace SpaceExplorationGame;

/// <summary>
/// WebAssembly entry point. Initializes the game and exposes a per-frame
/// step function that the browser's requestAnimationFrame loop can call.
/// </summary>
public partial class WebMain
{
    private static Game? _game;

    public static void Main()
    {
        try
        {
            Console.WriteLine("[SEG-CS] Main() starting...");

            // Parse URL query parameters (mirrors the SDL CLI argument parser).
            // Supported params: seed, location, sublocation, showcase, star-type, connect, name
            // Short aliases:    s,    l,        sl,          sc,       c,       n
            // Examples:
            //   ?seed=42
            //   ?location=planet&sublocation=on-foot
            //   ?showcase=star-type&star-type=K
            //   ?connect=ws://server:9050/&name=Commander
            var (galaxySeed, autoLaunch, autoDebugLaunch, autoDebugStarType, autoplay, connectUrl, playerNameParam) = ParseUrlParams();

            // Create platform
            var musicProvider = new GameMusicProvider(WebAudioManager.SampleRate);
            var sfxProvider = new GameSfxProvider(WebAudioManager.SampleRate);
            Console.WriteLine("[SEG-CS] Audio providers created");

            var platform = new WebPlatform(
                WindowConfig.WindowTitle,
                WindowConfig.DefaultWindowWidth, WindowConfig.DefaultWindowHeight,
                musicProvider, sfxProvider,
                AudioConfig.AudioMasterVolume, AudioConfig.AudioMusicVolume, AudioConfig.AudioSfxVolume);
            Console.WriteLine("[SEG-CS] WebPlatform created");

            _game = new Game();
            _game.Initialize(platform, galaxySeed);
            Console.WriteLine("[SEG-CS] Game initialized");

            if (autoplay)
                _game.AutoplayBot.Enabled = true;

            if (connectUrl != null)
            {
                var net = new NetworkManager();
                _game.Network = net;
                string playerName = playerNameParam ?? _game.MenuOptions.GetPlayerName();
                Console.WriteLine($"[SEG-CS] Connecting to {connectUrl} as '{playerName}'...");
                _game.ChangeState(new MultiplayerConnectState(connectUrl, playerName));
            }
            else
            {
                _game.ChangeState(new MainMenuState(autoLaunch, autoDebugLaunch, autoDebugStarType));
            }
            Console.WriteLine("[SEG-CS] Initial state set");

            _game.InitializeLoop();
            Console.WriteLine("[SEG-CS] Game loop initialized, ready for frames");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SEG-CS] FATAL: {ex}");
            throw;
        }
    }

    // ── URL parameter parsing ─────────────────────────────────────────────────

    private static (ulong? galaxySeed, StartOption autoLaunch, DebugLaunchRequest autoDebugLaunch, StarClass autoDebugStarType, bool autoplay, string? connectUrl, string? playerName) ParseUrlParams()
    {
        string? seedParam = JsLaunchOptions.GetUrlParam("seed") ?? JsLaunchOptions.GetUrlParam("s");
        string? locationParam = JsLaunchOptions.GetUrlParam("location") ?? JsLaunchOptions.GetUrlParam("l");
        string? subLocParam = JsLaunchOptions.GetUrlParam("sublocation") ?? JsLaunchOptions.GetUrlParam("sl");
        string? showcaseParam = JsLaunchOptions.GetUrlParam("showcase") ?? JsLaunchOptions.GetUrlParam("sc");
        string? starTypeParam = JsLaunchOptions.GetUrlParam("star-type");
        string? debugParam = JsLaunchOptions.GetUrlParam("debug");
        string? autoplayParam = JsLaunchOptions.GetUrlParam("autoplay");
        string? connectParam = JsLaunchOptions.GetUrlParam("connect") ?? JsLaunchOptions.GetUrlParam("c");
        string? nameParam = JsLaunchOptions.GetUrlParam("name") ?? JsLaunchOptions.GetUrlParam("n");

        WindowConfig.Debug = debugParam != null && debugParam != "false" && debugParam != "0";
        bool autoplay = autoplayParam != null && autoplayParam != "false" && autoplayParam != "0";

        ulong? galaxySeed = null;
        if (seedParam != null)
        {
            if (ulong.TryParse(seedParam, out var parsed))
                galaxySeed = parsed;
            else
                Console.Error.WriteLine($"[SEG-CS] Invalid ?seed value '{seedParam}' — ignored.");
        }

        if (showcaseParam != null && (locationParam != null || subLocParam != null))
        {
            Console.Error.WriteLine("[SEG-CS] ?showcase cannot be combined with ?location/?sublocation — location/sublocation ignored.");
            locationParam = null;
            subLocParam = null;
        }

        var autoDebugLaunch = DebugLaunchRequest.None;
        var autoDebugStarType = StarClass.G;
        var autoLaunch = StartOption.None;

        if (showcaseParam != null)
        {
            var debug = ResolveDebugShowcase(showcaseParam);
            if (debug.HasValue)
            {
                autoDebugLaunch = debug.Value;
                if (autoDebugLaunch == DebugLaunchRequest.StarTypeShowcase)
                    autoDebugStarType = ParseStarType(starTypeParam);
            }
            else
            {
                Console.Error.WriteLine($"[SEG-CS] Unknown ?showcase value '{showcaseParam}'. Valid: star-type, planet-type, asteroid, surface-mining.");
            }
        }
        else if (locationParam != null || subLocParam != null)
        {
            var start = ResolveStartFromLocation(locationParam, subLocParam);
            if (start.HasValue)
                autoLaunch = start.Value;
        }

        return (galaxySeed, autoLaunch, autoDebugLaunch, autoDebugStarType, autoplay, connectParam, nameParam);
    }

    private static DebugLaunchRequest? ResolveDebugShowcase(string showcase) =>
        Normalize(showcase) switch
        {
            "star-type" or "star" => DebugLaunchRequest.StarTypeShowcase,
            "planet-type" or "planet" => DebugLaunchRequest.PlanetTypeShowcase,
            "asteroid" or "asteroid-mining" => DebugLaunchRequest.AsteroidShowcase,
            "surface-mining" or "surface" => DebugLaunchRequest.SurfaceMiningShowcase,
            _ => null,
        };

    private static StarClass ParseStarType(string? starTypeArg)
    {
        if (string.IsNullOrWhiteSpace(starTypeArg))
            return StarClass.G;
        if (Enum.TryParse<StarClass>(starTypeArg.Trim(), ignoreCase: true, out var t))
            return t;
        Console.Error.WriteLine($"[SEG-CS] Invalid ?star-type '{starTypeArg}'. Valid: O, B, A, F, G, K, M, WhiteDwarf, Neutron, RedGiant. Using G.");
        return StarClass.G;
    }

    private static StartOption? ResolveStartFromLocation(string? location, string? subLocation)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            Console.Error.WriteLine("[SEG-CS] ?location is required when using ?sublocation.");
            return null;
        }
        string loc = Normalize(location);
        string? sub = string.IsNullOrWhiteSpace(subLocation) ? null : Normalize(subLocation);
        return loc switch
        {
            "solar-system" or "system" => ResolveSolarSystem(sub),
            "space-station" or "station" => ResolveStation(sub),
            "planet" => ResolvePlanet(sub),
            "settlement" => ResolveSettlement(sub),
            _ => LogAndReturnNull($"Unknown ?location value '{location}'. Valid: system, station, planet, settlement."),
        };
    }

    private static StartOption ResolveSolarSystem(string? _) => StartOption.StarSystem;

    private static StartOption ResolveStation(string? sub) =>
        sub switch
        {
            null or "orbit" => StartOption.SpaceStation,
            "menu" => StartOption.SpaceStationMenu,
            "inside" => StartOption.SpaceStationInside,
            _ => LogAndReturn($"Unknown ?sublocation '{sub}' for station. Valid: orbit, docked, inside. Using 'orbit'.", StartOption.SpaceStation),
        };

    private static StartOption ResolvePlanet(string? sub) =>
        sub switch
        {
            null or "orbit" => StartOption.Planet,
            "landed" => StartOption.PlanetSurface,
            "on-foot" or "foot" => StartOption.PlanetSurfaceOnFoot,
            "on-vehicle" or "vehicle" => StartOption.PlanetSurfaceOnVehicle,
            _ => LogAndReturn($"Unknown ?sublocation '{sub}' for planet. Valid: orbit, landed, on-foot, on-vehicle. Using 'orbit'.", StartOption.Planet),
        };

    private static StartOption ResolveSettlement(string? sub) =>
        sub switch
        {
            null or "above" => StartOption.Settlement,
            "inside" => StartOption.SettlementInside,
            "on-foot" or "foot" => StartOption.SettlementOnFoot,
            "on-vehicle" or "vehicle" => StartOption.SettlementOnVehicle,
            _ => LogAndReturn($"Unknown ?sublocation '{sub}' for settlement. Valid: above, inside, on-foot, on-vehicle. Using 'above'.", StartOption.Settlement),
        };

    private static StartOption? LogAndReturnNull(string msg) { Console.Error.WriteLine($"[SEG-CS] {msg}"); return null; }
    private static StartOption LogAndReturn(string msg, StartOption fallback) { Console.Error.WriteLine($"[SEG-CS] {msg}"); return fallback; }
    private static string Normalize(string value) => value.Trim().ToLowerInvariant();

    /// <summary>
    /// Called by JavaScript each frame via requestAnimationFrame.
    /// </summary>
    [JSExport]
    public static void RunOneFrame()
    {
        try
        {
            _game?.RunOneFrame();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SEG-CS] Frame error: {ex}");
            throw;
        }
    }
}
