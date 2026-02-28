using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Generation;

/// <summary>
/// Tile types used in interior layouts (space stations and settlements).
/// </summary>
public enum InteriorTileType
{
    Void,        // Outside the interior (impassable)
    Floor,       // Generic walkable floor
    Wall,        // Solid wall (impassable)
    DoorOpen,    // Open doorway (walkable)
    Console,     // Interactive terminal (walkable, interactable)
    Crate,       // Storage crate (impassable decoration)
    FloorAccent, // Decorative floor (walkable, different color)
    LandingPad,  // Spawn point floor (walkable)
    StreetTile,  // Settlement outdoor walkway
    Table,       // Furniture — impassable table surface
    Chair,       // Furniture — walkable seat
    Plant,       // Decorative potted plant (impassable)
    Rug,         // Decorative rug on floor (walkable)
    Window,      // Transparent wall section showing exterior (impassable)
    Pipe,        // Exposed pipe / duct (impassable decoration)
    Light,       // Ceiling light marker (walkable, emits glow)
    Shelf,       // Storage shelf (impassable)
    Bed,         // Crew quarters bed (impassable)
    BarCounter,  // Cantina bar counter (impassable)
    Generator,   // Power generator equipment (impassable)
    Antenna,     // Communications antenna (impassable)
}

/// <summary>Whether the interior belongs to a station or a settlement.</summary>
public enum InteriorType
{
    SpaceStation,
    Settlement
}

/// <summary>
/// A named room within an interior.
/// </summary>
public class InteriorRoom
{
    public string Name { get; init; } = "";
    public RoomFunction Function { get; init; }
    public TileRect TileRect { get; init; }
    public int CenterX => TileRect.CenterX;
    public int CenterY => TileRect.CenterY;
}

public enum RoomFunction
{
    DockingBay,
    CommandCenter,
    TradingPost,
    Medbay,
    CrewQuarters,
    CargoBay,
    Corridor,
    // Settlement-specific
    LandingPad,
    Market,
    Cantina,
    Housing,
    Generator,
    CommsCenter
}

/// <summary>
/// An NPC that stands in the interior.
/// </summary>
public class InteriorNpc
{
    public string Name { get; init; } = "";
    public string Role { get; init; } = "";
    public TilePos TilePos { get; init; }
    public string[] DialogueLines { get; init; } = [];
    public Color3 Color { get; init; } = new(100, 200, 255);
    /// <summary>Body width scale (0.7 = thin, 1.0 = normal, 1.3 = stocky).</summary>
    public float BodyScale { get; init; } = 1f;
    /// <summary>Head accessory type (0=none, 1=hat, 2=helmet, 3=hood).</summary>
    public int Accessory { get; init; }
}

/// <summary>
/// An interactable object in the interior (terminal, repair station, etc.).
/// </summary>
public class InteriorInteractable
{
    public string Name { get; init; } = "";
    public InteractableType Type { get; init; }
    public TilePos TilePos { get; init; }
}

public enum InteractableType
{
    RepairStation,
    MissionBoard,
    ExitDoor,
    ShipCustomization,
    AvatarCustomization,
    VehicleCustomization,
    ShipDealer,
    CargoTerminal,
    HealthStation,
    NoticeBoard
}

/// <summary>
/// Complete data for a generated interior.
/// </summary>
public class InteriorData
{
    public InteriorType Type { get; init; }
    public string Name { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public InteriorTileType[,] Tiles { get; init; } = null!;
    /// <summary>Room function for each tile (null if not part of a named room).</summary>
    public RoomFunction?[,] RoomTiles { get; set; } = null!;
    public List<InteriorRoom> Rooms { get; init; } = [];
    public List<InteriorNpc> Npcs { get; init; } = [];
    public List<InteriorInteractable> Interactables { get; init; } = [];
    public TilePos SpawnPoint { get; set; }
}

/// <summary>
/// Procedurally generates interior layouts for space stations and settlements.
/// </summary>
public static class InteriorGenerator
{
    // Station interior size
    private const int StationWidth = 48;
    private const int StationHeight = 36;

    // Settlement interior size
    private const int SettlementWidth = 40;
    private const int SettlementHeight = 32;

    private static readonly string[] FirstNames =
    [
        "Zara", "Kael", "Nova", "Ryker", "Mira", "Joss", "Senna", "Dax",
        "Liora", "Thane", "Vex", "Petra", "Orion", "Cass", "Rune", "Kira",
        "Ash", "Brynn", "Cole", "Dara", "Eli", "Fenn", "Gale", "Hux"
    ];

    private static readonly string[] LastNames =
    [
        "Voss", "Kren", "Stark", "Sol", "Drake", "Vane", "Cross", "Hale",
        "Rook", "Ward", "Shaw", "Reeve", "Marsh", "Cade", "Wren", "Steele"
    ];

    private static readonly string[] TraderDialogue =
    [
        "Looking to trade? I've got the best prices in this sector.",
        "Fresh stock just came in. Take a look.",
        "Credits talk, friend. What do you need?",
        "Don't mind the scratches. Everything works. Mostly."
    ];

    private static readonly string[] MechanicDialogue =
    [
        "Your ship looks like it's been through a meteor shower.",
        "I can patch that hull up. For the right price.",
        "Standard repair or full overhaul? Your call.",
        "I've fixed worse. Much worse. You should see the other guy."
    ];

    private static readonly string[] MedicDialogue =
    [
        "You don't look so good. Let me take a look.",
        "I've patched up worse. Hold still.",
        "The health station can fix you right up. Step on over.",
        "Stay out of firefights if you can. Prevention is the best medicine."
    ];

    private static readonly string[] CommanderDialogue =
    [
        "Welcome aboard. Keep your weapon holstered.",
        "We maintain order here. Don't cause trouble.",
        "This station has stood for decades. I intend to keep it that way.",
        "Report anything suspicious to security."
    ];

    private static readonly string[] CivilianDialogue =
    [
        "Just passing through. You know how it is.",
        "This place isn't bad, once you get used to the recycled air.",
        "Have you been to the outer rim? I hear it's rough out there.",
        "I've been waiting for a ship out of here for weeks.",
        "The food here is terrible, but the company is worse.",
        "Don't trust anyone who says they know a shortcut through the nebula."
    ];

    /// <summary>
    /// Generate a space station interior.
    /// </summary>
    public static InteriorData GenerateStation(SeededRandom rng, string stationName)
    {
        var data = new InteriorData
        {
            Type = InteriorType.SpaceStation,
            Name = stationName,
            Width = StationWidth,
            Height = StationHeight,
            Tiles = new InteriorTileType[StationWidth, StationHeight],
            RoomTiles = new RoomFunction?[StationWidth, StationHeight]
        };

        // Fill with void
        for (int x = 0; x < StationWidth; x++)
            for (int y = 0; y < StationHeight; y++)
                data.Tiles[x, y] = InteriorTileType.Void;

        // Generate rooms with randomized sizes
        int dockW = rng.NextInt(10, 14), dockH = rng.NextInt(10, 14);
        int cmdW = rng.NextInt(10, 14), cmdH = rng.NextInt(7, 10);
        int tradW = rng.NextInt(10, 14), tradH = rng.NextInt(7, 10);
        int medW = rng.NextInt(10, 14), medH = rng.NextInt(7, 10);
        int qtrW = rng.NextInt(10, 14), qtrH = rng.NextInt(7, 10);
        int crgW = rng.NextInt(10, 14), crgH = rng.NextInt(7, 10);

        // Position jitter
        int jx() => rng.NextInt(-1, 2);
        int jy() => rng.NextInt(-1, 2);

        var docking = new InteriorRoom
        {
            Name = "DOCKING BAY",
            Function = RoomFunction.DockingBay,
            TileRect = new(2, StationHeight / 2 - dockH / 2 + jy(), dockW, dockH)
        };
        var command = new InteriorRoom
        {
            Name = "COMMAND CENTER",
            Function = RoomFunction.CommandCenter,
            TileRect = new(StationWidth - cmdW - 2 + jx(), 2 + jy(), cmdW, cmdH)
        };
        var trading = new InteriorRoom
        {
            Name = "TRADING POST",
            Function = RoomFunction.TradingPost,
            TileRect = new(StationWidth / 2 - tradW / 2 + jx(), 2 + jy(), tradW, tradH)
        };
        var medbay = new InteriorRoom
        {
            Name = "MEDBAY",
            Function = RoomFunction.Medbay,
            TileRect = new(StationWidth - medW - 2 + jx(), StationHeight - medH - 2 + jy(), medW, medH)
        };
        var quarters = new InteriorRoom
        {
            Name = "CREW QUARTERS",
            Function = RoomFunction.CrewQuarters,
            TileRect = new(StationWidth / 2 - qtrW / 2 + jx(), StationHeight - qtrH - 2 + jy(), qtrW, qtrH)
        };
        var cargo = new InteriorRoom
        {
            Name = "CARGO BAY",
            Function = RoomFunction.CargoBay,
            TileRect = new(2 + jx(), 2 + jy(), crgW, crgH)
        };

        data.Rooms.AddRange([docking, command, trading, medbay, quarters, cargo]);

        // Carve rooms
        foreach (var room in data.Rooms)
            CarveRoom(data, room);

        // Connect rooms with corridors
        ConnectRooms(data, docking, trading);
        ConnectRooms(data, docking, quarters);
        ConnectRooms(data, docking, cargo);
        ConnectRooms(data, cargo, trading);
        ConnectRooms(data, trading, command);
        ConnectRooms(data, quarters, medbay);
        ConnectRooms(data, command, medbay);

        // Carve alcoves along corridors
        CarveAlcoves(data, rng, 3);

        // Mark docking bay floor as landing pad
        for (int x = docking.TileRect.X + 2; x < docking.TileRect.X + docking.TileRect.Width - 2; x++)
            for (int y = docking.TileRect.Y + 2; y < docking.TileRect.Y + docking.TileRect.Height - 2; y++)
                data.Tiles[x, y] = InteriorTileType.LandingPad;

        // Add decorative crates in cargo bay
        PlaceCrates(data, rng, cargo, 6);

        // Add accent floors in trading post
        for (int x = trading.TileRect.X + 2; x < trading.TileRect.X + trading.TileRect.Width - 2; x++)
            for (int y = trading.TileRect.Y + 2; y < trading.TileRect.Y + trading.TileRect.Height - 2; y++)
                if (data.Tiles[x, y] == InteriorTileType.Floor)
                    data.Tiles[x, y] = InteriorTileType.FloorAccent;

        // Furnish all rooms with appropriate furniture
        foreach (var room in data.Rooms)
            FurnishRoom(data, rng, room);

        // Remove any furniture that blocks doorway access
        ClearDoorwayObstructions(data);

        // Spawn point
        data.SpawnPoint = new TilePos(docking.CenterX, docking.CenterY);

        // Cargo terminal (sell mined resources)
        PlaceInteractable(data, "CARGO TERMINAL", InteractableType.CargoTerminal,
            trading.CenterX, trading.TileRect.Y + 1);

        PlaceInteractable(data, "HEALTH STATION", InteractableType.HealthStation,
            medbay.CenterX - 2, medbay.TileRect.Y + 1);

        PlaceInteractable(data, "MISSION BOARD", InteractableType.MissionBoard,
            command.CenterX, command.TileRect.Y + 1);

        PlaceInteractable(data, "EXIT", InteractableType.ExitDoor,
            docking.TileRect.X + docking.TileRect.Width / 2, docking.TileRect.Y + docking.TileRect.Height - 1,
            markConsole: false);

        // Repair station in docking bay (next to landing pad)
        PlaceInteractable(data, "REPAIR STATION", InteractableType.RepairStation,
            docking.TileRect.X + 1, docking.CenterY);

        // Ship customization terminal next to landing pad
        PlaceInteractable(data, "SHIP CUSTOMIZATION", InteractableType.ShipCustomization,
            docking.TileRect.X + docking.TileRect.Width / 2 + 2, docking.TileRect.Y + docking.TileRect.Height - 2);

        // Avatar customization terminal (top of docking room)
        PlaceInteractable(data, "AVATAR CUSTOMIZATION", InteractableType.AvatarCustomization,
            docking.TileRect.X + docking.TileRect.Width / 2 - 2, docking.TileRect.Y + 1);

        // Vehicle customization terminal (top of docking room)
        PlaceInteractable(data, "VEHICLE CUSTOMIZATION", InteractableType.VehicleCustomization,
            docking.TileRect.X + docking.TileRect.Width / 2 + 2, docking.TileRect.Y + 1);

        // Ship dealer terminal (left side of docking room)
        PlaceInteractable(data, "SHIP DEALER", InteractableType.ShipDealer,
            docking.TileRect.X + docking.TileRect.Width / 2 - 2, docking.TileRect.Y + docking.TileRect.Height - 2);

        // Notice board in crew quarters
        PlaceInteractable(data, "NOTICE BOARD", InteractableType.NoticeBoard,
            quarters.CenterX, quarters.TileRect.Y + 1);

        // Place NPCs
        PlaceStationNpcs(data, rng);

        // Build room tile lookup
        BuildRoomTileMap(data);

        return data;
    }

    /// <summary>
    /// Generate a settlement interior.
    /// </summary>
    public static InteriorData GenerateSettlement(SeededRandom rng, string settlementName)
    {
        var data = new InteriorData
        {
            Type = InteriorType.Settlement,
            Name = settlementName,
            Width = SettlementWidth,
            Height = SettlementHeight,
            Tiles = new InteriorTileType[SettlementWidth, SettlementHeight],
            RoomTiles = new RoomFunction?[SettlementWidth, SettlementHeight]
        };

        // Fill with void
        for (int x = 0; x < SettlementWidth; x++)
            for (int y = 0; y < SettlementHeight; y++)
                data.Tiles[x, y] = InteriorTileType.Void;

        // Generate settlement layout with randomized sizes
        int landW = rng.NextInt(8, 11), landH = rng.NextInt(6, 9);
        int mktW = rng.NextInt(9, 12), mktH = rng.NextInt(7, 10);
        int cantW = rng.NextInt(9, 12), cantH = rng.NextInt(7, 10);
        int housW = rng.NextInt(7, 10), housH = rng.NextInt(5, 8);
        int comW = rng.NextInt(9, 12), comH = rng.NextInt(5, 8);

        var landing = new InteriorRoom
        {
            Name = "LANDING PAD",
            Function = RoomFunction.LandingPad,
            TileRect = new(SettlementWidth / 2 - landW / 2, SettlementHeight - landH - 2, landW, landH)
        };
        var market = new InteriorRoom
        {
            Name = "MARKET",
            Function = RoomFunction.Market,
            TileRect = new(2, 4 + rng.NextInt(-1, 2), mktW, mktH)
        };
        var cantina = new InteriorRoom
        {
            Name = "CANTINA",
            Function = RoomFunction.Cantina,
            TileRect = new(SettlementWidth - cantW - 2, 4 + rng.NextInt(-1, 2), cantW, cantH)
        };
        var housing1 = new InteriorRoom
        {
            Name = "HOUSING A",
            Function = RoomFunction.Housing,
            TileRect = new(2, 15 + rng.NextInt(-1, 2), housW, housH)
        };
        var housing2 = new InteriorRoom
        {
            Name = "HOUSING B",
            Function = RoomFunction.Housing,
            TileRect = new(SettlementWidth - housW - 2, 15 + rng.NextInt(-1, 2), housW, housH)
        };
        var comms = new InteriorRoom
        {
            Name = "COMMS CENTER",
            Function = RoomFunction.CommsCenter,
            TileRect = new(SettlementWidth / 2 - comW / 2, 2, comW, comH)
        };

        data.Rooms.AddRange([landing, market, cantina, housing1, housing2, comms]);

        // Carve rooms
        foreach (var room in data.Rooms)
            CarveRoom(data, room);

        // Create streets connecting buildings
        ConnectWithStreet(data, landing, comms);
        ConnectWithStreet(data, market, cantina);
        ConnectWithStreet(data, landing, housing1);
        ConnectWithStreet(data, landing, housing2);
        ConnectWithStreet(data, housing1, market);
        ConnectWithStreet(data, housing2, cantina);
        ConnectWithStreet(data, comms, market);
        ConnectWithStreet(data, comms, cantina);

        // Landing pad marking
        for (int x = landing.TileRect.X + 1; x < landing.TileRect.X + landing.TileRect.Width - 1; x++)
            for (int y = landing.TileRect.Y + 1; y < landing.TileRect.Y + landing.TileRect.Height - 1; y++)
                data.Tiles[x, y] = InteriorTileType.LandingPad;

        // Market accent floor
        for (int x = market.TileRect.X + 2; x < market.TileRect.X + market.TileRect.Width - 2; x++)
            for (int y = market.TileRect.Y + 2; y < market.TileRect.Y + market.TileRect.Height - 2; y++)
                if (data.Tiles[x, y] == InteriorTileType.Floor)
                    data.Tiles[x, y] = InteriorTileType.FloorAccent;

        // Place decorative crates
        PlaceCrates(data, rng, housing1, 2);
        PlaceCrates(data, rng, housing2, 2);

        // Furnish all rooms with appropriate furniture
        foreach (var room in data.Rooms)
            FurnishRoom(data, rng, room);

        // Remove any furniture that blocks doorway access
        ClearDoorwayObstructions(data);

        // Spawn point
        data.SpawnPoint = new TilePos(landing.CenterX, landing.CenterY);

        // Place interactables
        PlaceInteractable(data, "CARGO TERMINAL", InteractableType.CargoTerminal,
            market.CenterX, market.TileRect.Y + 1);

        PlaceInteractable(data, "MISSION BOARD", InteractableType.MissionBoard,
            comms.CenterX, comms.TileRect.Y + 1);

        PlaceInteractable(data, "EXIT", InteractableType.ExitDoor,
            landing.CenterX, landing.TileRect.Y + landing.TileRect.Height - 1,
            markConsole: false);

        // Ship customization terminal next to landing pad
        PlaceInteractable(data, "SHIP CUSTOMIZATION", InteractableType.ShipCustomization,
            landing.CenterX + 2, landing.TileRect.Y + landing.TileRect.Height - 2);

        // Repair station at the landing pad
        PlaceInteractable(data, "REPAIR STATION", InteractableType.RepairStation,
            landing.TileRect.X + 1, landing.CenterY);

        // Health station in the cantina (serves as settlement medbay)
        PlaceInteractable(data, "HEALTH STATION", InteractableType.HealthStation,
            cantina.CenterX - 2, cantina.TileRect.Y + 1);

        // Avatar customization terminal (top of landing pad)
        PlaceInteractable(data, "AVATAR CUSTOMIZATION", InteractableType.AvatarCustomization,
            landing.CenterX - 2, landing.TileRect.Y + 1);

        // Vehicle customization terminal (top of landing pad)
        PlaceInteractable(data, "VEHICLE CUSTOMIZATION", InteractableType.VehicleCustomization,
            landing.CenterX + 2, landing.TileRect.Y + 1);

        // Ship dealer terminal (left side of landing pad)
        PlaceInteractable(data, "SHIP DEALER", InteractableType.ShipDealer,
            landing.CenterX - 2, landing.TileRect.Y + landing.TileRect.Height - 2);

        // Notice board in cantina
        PlaceInteractable(data, "NOTICE BOARD", InteractableType.NoticeBoard,
            cantina.CenterX, cantina.TileRect.Y + 1);

        // Place NPCs
        PlaceSettlementNpcs(data, rng);

        // Build room tile lookup
        BuildRoomTileMap(data);

        return data;
    }

    /// <summary>Get the color for an interior tile.</summary>
    public static Color3 GetTileColor(InteriorTileType type)
    {
        return type switch
        {
            InteriorTileType.Floor => new(60, 60, 70),
            InteriorTileType.Wall => new(40, 40, 50),
            InteriorTileType.DoorOpen => new(80, 80, 60),
            InteriorTileType.Console => new(30, 80, 120),
            InteriorTileType.Crate => new(100, 80, 50),
            InteriorTileType.FloorAccent => new(70, 65, 80),
            InteriorTileType.LandingPad => new(50, 55, 65),
            InteriorTileType.StreetTile => new(55, 50, 45),
            InteriorTileType.Table => new(90, 70, 50),
            InteriorTileType.Chair => new(70, 60, 55),
            InteriorTileType.Plant => new(35, 80, 40),
            InteriorTileType.Rug => new(80, 50, 60),
            InteriorTileType.Window => new(20, 30, 60),
            InteriorTileType.Pipe => new(70, 70, 80),
            InteriorTileType.Light => new(65, 65, 75),
            InteriorTileType.Shelf => new(85, 70, 55),
            InteriorTileType.Bed => new(70, 55, 80),
            InteriorTileType.BarCounter => new(80, 60, 45),
            InteriorTileType.Generator => new(50, 65, 70),
            InteriorTileType.Antenna => new(60, 80, 90),
            InteriorTileType.Void => new(10, 10, 15),
            _ => new(40, 40, 40)
        };
    }

    /// <summary>Whether a tile can be walked on.</summary>
    public static bool IsWalkable(InteriorTileType type)
    {
        return type is InteriorTileType.Floor or InteriorTileType.DoorOpen
            or InteriorTileType.FloorAccent or InteriorTileType.LandingPad
            or InteriorTileType.StreetTile or InteriorTileType.Console
            or InteriorTileType.Chair or InteriorTileType.Rug
            or InteriorTileType.Light;
    }

    #region Layout Helpers

    private static void CarveRoom(InteriorData data, InteriorRoom room)
    {
        var r = room.TileRect;
        for (int x = r.X; x < r.X + r.Width && x < data.Width; x++)
        {
            for (int y = r.Y; y < r.Y + r.Height && y < data.Height; y++)
            {
                bool isEdge = x == r.X || x == r.X + r.Width - 1 ||
                              y == r.Y || y == r.Y + r.Height - 1;

                data.Tiles[x, y] = isEdge ? InteriorTileType.Wall : InteriorTileType.Floor;
            }
        }
    }

    private static void ConnectRooms(InteriorData data, InteriorRoom a, InteriorRoom b)
    {
        int ax = a.CenterX, ay = a.CenterY;
        int bx = b.CenterX, by = b.CenterY;

        // L-shaped corridor: go horizontal first, then vertical
        CarveCorridorH(data, ax, bx, ay);
        CarveCorridorV(data, bx, ay, by);
    }

    private static void ConnectWithStreet(InteriorData data, InteriorRoom a, InteriorRoom b)
    {
        int ax = a.CenterX, ay = a.CenterY;
        int bx = b.CenterX, by = b.CenterY;

        CarveStreetH(data, ax, bx, ay);
        CarveStreetV(data, bx, ay, by);
    }

    private static void CarveCorridorH(InteriorData data, int x1, int x2, int y)
    {
        int minX = Math.Min(x1, x2);
        int maxX = Math.Max(x1, x2);

        for (int x = minX; x <= maxX; x++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int ty = y + dy;
                if (TileInBounds(data, x, ty))
                {
                    if (dy == -1 || dy == 1)
                    {
                        // Only place wall if currently void
                        if (data.Tiles[x, ty] == InteriorTileType.Void)
                            data.Tiles[x, ty] = InteriorTileType.Wall;
                    }
                    else
                    {
                        // Center of corridor
                        if (data.Tiles[x, ty] == InteriorTileType.Wall)
                            data.Tiles[x, ty] = InteriorTileType.DoorOpen;
                        else if (data.Tiles[x, ty] == InteriorTileType.Void)
                            data.Tiles[x, ty] = InteriorTileType.Floor;
                    }
                }
            }
        }
    }

    private static void CarveCorridorV(InteriorData data, int x, int y1, int y2)
    {
        int minY = Math.Min(y1, y2);
        int maxY = Math.Max(y1, y2);

        for (int y = minY; y <= maxY; y++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int tx = x + dx;
                if (TileInBounds(data, tx, y))
                {
                    if (dx == -1 || dx == 1)
                    {
                        if (data.Tiles[tx, y] == InteriorTileType.Void)
                            data.Tiles[tx, y] = InteriorTileType.Wall;
                    }
                    else
                    {
                        if (data.Tiles[tx, y] == InteriorTileType.Wall)
                            data.Tiles[tx, y] = InteriorTileType.DoorOpen;
                        else if (data.Tiles[tx, y] == InteriorTileType.Void)
                            data.Tiles[tx, y] = InteriorTileType.Floor;
                    }
                }
            }
        }
    }

    private static void CarveStreetH(InteriorData data, int x1, int x2, int y)
    {
        int minX = Math.Min(x1, x2);
        int maxX = Math.Max(x1, x2);

        for (int x = minX; x <= maxX; x++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                int ty = y + dy;
                if (TileInBounds(data, x, ty))
                {
                    if (data.Tiles[x, ty] == InteriorTileType.Wall)
                        data.Tiles[x, ty] = InteriorTileType.DoorOpen;
                    else if (data.Tiles[x, ty] == InteriorTileType.Void)
                        data.Tiles[x, ty] = InteriorTileType.StreetTile;
                }
            }
        }
    }

    private static void CarveStreetV(InteriorData data, int x, int y1, int y2)
    {
        int minY = Math.Min(y1, y2);
        int maxY = Math.Max(y1, y2);

        for (int y = minY; y <= maxY; y++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int tx = x + dx;
                if (TileInBounds(data, tx, y))
                {
                    if (data.Tiles[tx, y] == InteriorTileType.Wall)
                        data.Tiles[tx, y] = InteriorTileType.DoorOpen;
                    else if (data.Tiles[tx, y] == InteriorTileType.Void)
                        data.Tiles[tx, y] = InteriorTileType.StreetTile;
                }
            }
        }
    }

    private static bool TileInBounds(InteriorData data, int x, int y)
        => x >= 0 && x < data.Width && y >= 0 && y < data.Height;

    /// <summary>Carve small alcoves along corridor walls for visual interest.</summary>
    private static void CarveAlcoves(InteriorData data, SeededRandom rng, int count)
    {
        int placed = 0;
        int attempts = count * 20;
        while (placed < count && attempts-- > 0)
        {
            int x = rng.NextInt(2, data.Width - 3);
            int y = rng.NextInt(2, data.Height - 3);

            // Look for a corridor floor tile with a wall on one side and void beyond
            if (!TileInBounds(data, x, y) || data.Tiles[x, y] != InteriorTileType.Floor)
                continue;

            // Try each direction for an alcove
            int[][] dirs = [[0, -1], [0, 1], [-1, 0], [1, 0]];
            var dir = dirs[rng.NextInt(4)];
            int wx = x + dir[0], wy = y + dir[1];
            int vx = x + dir[0] * 2, vy = y + dir[1] * 2;
            int vx2 = x + dir[0] * 3, vy2 = y + dir[1] * 3;

            if (!TileInBounds(data, wx, wy) || data.Tiles[wx, wy] != InteriorTileType.Wall)
                continue;
            if (!TileInBounds(data, vx, vy) || data.Tiles[vx, vy] != InteriorTileType.Void)
                continue;
            if (!TileInBounds(data, vx2, vy2) || data.Tiles[vx2, vy2] != InteriorTileType.Void)
                continue;

            // Carve a 1x2 alcove: replace the wall with floor, place wall around the alcove
            data.Tiles[wx, wy] = InteriorTileType.DoorOpen;
            data.Tiles[vx, vy] = InteriorTileType.Floor;

            // Wall around the new alcove tile
            for (int ddx = -1; ddx <= 1; ddx++)
            {
                for (int ddy = -1; ddy <= 1; ddy++)
                {
                    int nx = vx + ddx, ny = vy + ddy;
                    if (TileInBounds(data, nx, ny) && data.Tiles[nx, ny] == InteriorTileType.Void)
                        data.Tiles[nx, ny] = InteriorTileType.Wall;
                }
            }

            // Place a decoration in the alcove
            var decoration = rng.NextInt(3) switch
            {
                0 => InteriorTileType.Crate,
                1 => InteriorTileType.Plant,
                _ => InteriorTileType.Console
            };
            data.Tiles[vx, vy] = decoration;
            placed++;
        }
    }

    /// <summary>Build a per-tile room function map from the room list.</summary>
    private static void BuildRoomTileMap(InteriorData data)
    {
        foreach (var room in data.Rooms)
        {
            var r = room.TileRect;
            for (int x = r.X; x < r.X + r.Width && x < data.Width; x++)
                for (int y = r.Y; y < r.Y + r.Height && y < data.Height; y++)
                    if (x >= 0 && y >= 0)
                        data.RoomTiles[x, y] = room.Function;
        }
    }

    private static void PlaceCrates(InteriorData data, SeededRandom rng, InteriorRoom room, int count)
    {
        var r = room.TileRect;
        for (int i = 0; i < count; i++)
        {
            int cx = rng.NextInt(r.X + 2, r.X + r.Width - 2);
            int cy = rng.NextInt(r.Y + 2, r.Y + r.Height - 2);
            if (TileInBounds(data, cx, cy) && data.Tiles[cx, cy] == InteriorTileType.Floor)
                data.Tiles[cx, cy] = InteriorTileType.Crate;
        }
    }

    private static void PlaceConsoles(InteriorData data, InteriorRoom room, int count)
    {
        var r = room.TileRect;
        // Place consoles along the top wall of the room
        int startX = r.X + 2;
        int spacing = Math.Max(1, (r.Width - 4) / (count + 1));
        for (int i = 0; i < count; i++)
        {
            int cx = startX + spacing * (i + 1);
            int cy = r.Y + 1;
            if (TileInBounds(data, cx, cy))
                data.Tiles[cx, cy] = InteriorTileType.Console;
        }
    }

    /// <summary>Add an interactable at the given tile and optionally mark the tile as a console.</summary>
    private static void PlaceInteractable(InteriorData data, string name, InteractableType type, int x, int y, bool markConsole = true)
    {
        data.Interactables.Add(new InteriorInteractable { Name = name, Type = type, TilePos = new(x, y) });
        if (markConsole)
            data.Tiles[x, y] = InteriorTileType.Console;
    }

    /// <summary>Place room-specific furniture and decorations based on room function.</summary>
    private static void FurnishRoom(InteriorData data, SeededRandom rng, InteriorRoom room)
    {
        var r = room.TileRect;
        switch (room.Function)
        {
            case RoomFunction.CrewQuarters:
            case RoomFunction.Housing:
                // Place beds along left wall
                for (int y = r.Y + 2; y < r.Y + r.Height - 2; y += 2)
                    if (TileInBounds(data, r.X + 1, y) && data.Tiles[r.X + 1, y] == InteriorTileType.Floor)
                        data.Tiles[r.X + 1, y] = InteriorTileType.Bed;
                // Rug in center
                PlaceRugPatch(data, r.CenterX - 1, r.CenterY - 1, 3, 2);
                // Plant in corner
                PlaceTileIfFloor(data, r.X + r.Width - 2, r.Y + 1, InteriorTileType.Plant);
                break;

            case RoomFunction.Cantina:
                // Bar counter across the top
                for (int x = r.X + 2; x < r.X + r.Width - 2; x++)
                    PlaceTileIfFloor(data, x, r.Y + 2, InteriorTileType.BarCounter);
                // Tables and chairs
                PlaceTableWithChairs(data, r.X + 3, r.CenterY + 1);
                PlaceTableWithChairs(data, r.X + r.Width - 4, r.CenterY + 1);
                // Plants by entrance
                PlaceTileIfFloor(data, r.X + 1, r.Y + r.Height - 2, InteriorTileType.Plant);
                PlaceTileIfFloor(data, r.X + r.Width - 2, r.Y + r.Height - 2, InteriorTileType.Plant);
                break;

            case RoomFunction.CommandCenter:
                // Consoles along walls
                PlaceConsoles(data, room, 3);
                // Central table
                PlaceTileIfFloor(data, r.CenterX, r.CenterY, InteriorTileType.Table);
                PlaceTileIfFloor(data, r.CenterX - 1, r.CenterY, InteriorTileType.Table);
                PlaceTileIfFloor(data, r.CenterX + 1, r.CenterY, InteriorTileType.Table);
                // Chairs around table
                PlaceTileIfFloor(data, r.CenterX, r.CenterY - 1, InteriorTileType.Chair);
                PlaceTileIfFloor(data, r.CenterX, r.CenterY + 1, InteriorTileType.Chair);
                break;

            case RoomFunction.Medbay:
                // Beds along right wall
                for (int y = r.Y + 2; y < r.Y + r.Height - 2; y += 2)
                    PlaceTileIfFloor(data, r.X + r.Width - 2, y, InteriorTileType.Bed);
                // Light in center
                PlaceTileIfFloor(data, r.CenterX, r.CenterY, InteriorTileType.Light);
                // Shelf on left wall
                PlaceTileIfFloor(data, r.X + 1, r.CenterY, InteriorTileType.Shelf);
                break;

            case RoomFunction.TradingPost:
            case RoomFunction.Market:
                // Shelves along walls
                for (int y = r.Y + 2; y < r.Y + r.Height - 2; y += 2)
                {
                    PlaceTileIfFloor(data, r.X + 1, y, InteriorTileType.Shelf);
                    PlaceTileIfFloor(data, r.X + r.Width - 2, y, InteriorTileType.Shelf);
                }
                // Rug in center
                PlaceRugPatch(data, r.CenterX - 1, r.CenterY, 3, 1);
                break;

            case RoomFunction.CargoBay:
                // Extra pipes on walls
                for (int x = r.X + 2; x < r.X + r.Width - 2; x += 3)
                    PlaceTileIfFloor(data, x, r.Y + 1, InteriorTileType.Pipe);
                // Lights
                PlaceTileIfFloor(data, r.CenterX, r.CenterY - 1, InteriorTileType.Light);
                break;

            case RoomFunction.DockingBay:
            case RoomFunction.LandingPad:
                // Pipes along top wall
                for (int x = r.X + 2; x < r.X + r.Width - 2; x += 4)
                    PlaceTileIfFloor(data, x, r.Y + 1, InteriorTileType.Pipe);
                // Lights along center
                PlaceTileIfFloor(data, r.CenterX - 3, r.CenterY - 3, InteriorTileType.Light);
                PlaceTileIfFloor(data, r.CenterX + 3, r.CenterY - 3, InteriorTileType.Light);
                break;

            case RoomFunction.Generator:
                // Generator equipment
                PlaceTileIfFloor(data, r.CenterX, r.CenterY, InteriorTileType.Generator);
                PlaceTileIfFloor(data, r.CenterX - 1, r.CenterY, InteriorTileType.Generator);
                // Pipes
                for (int x = r.X + 1; x < r.X + r.Width - 1; x += 2)
                    PlaceTileIfFloor(data, x, r.Y + 1, InteriorTileType.Pipe);
                break;

            case RoomFunction.CommsCenter:
                // Antenna in center
                PlaceTileIfFloor(data, r.CenterX, r.CenterY, InteriorTileType.Antenna);
                // Consoles
                PlaceConsoles(data, room, 2);
                break;
        }

        // Windows on exterior walls (walls adjacent to Void)
        PlaceWindows(data, rng, room);

        // Lights in corridors and larger rooms
        PlaceRoomLights(data, rng, room);
    }

    /// <summary>Place a small rectangular rug patch.</summary>
    private static void PlaceRugPatch(InteriorData data, int startX, int startY, int w, int h)
    {
        for (int x = startX; x < startX + w; x++)
            for (int y = startY; y < startY + h; y++)
                PlaceTileIfFloor(data, x, y, InteriorTileType.Rug);
    }

    /// <summary>Place a table with chairs on each side.</summary>
    private static void PlaceTableWithChairs(InteriorData data, int cx, int cy)
    {
        PlaceTileIfFloor(data, cx, cy, InteriorTileType.Table);
        PlaceTileIfFloor(data, cx - 1, cy, InteriorTileType.Chair);
        PlaceTileIfFloor(data, cx + 1, cy, InteriorTileType.Chair);
    }

    /// <summary>Place windows on walls adjacent to void (exterior walls).</summary>
    private static void PlaceWindows(InteriorData data, SeededRandom rng, InteriorRoom room)
    {
        var r = room.TileRect;
        // Check each wall tile — if adjacent to void on the outside, maybe place a window
        for (int x = r.X + 2; x < r.X + r.Width - 2; x++)
        {
            // Top wall
            if (TileInBounds(data, x, r.Y) && data.Tiles[x, r.Y] == InteriorTileType.Wall &&
                TileInBounds(data, x, r.Y - 1) && data.Tiles[x, r.Y - 1] == InteriorTileType.Void)
            {
                if (rng.NextBool(0.3f))
                    data.Tiles[x, r.Y] = InteriorTileType.Window;
            }
            // Bottom wall
            if (TileInBounds(data, x, r.Y + r.Height - 1) && data.Tiles[x, r.Y + r.Height - 1] == InteriorTileType.Wall &&
                TileInBounds(data, x, r.Y + r.Height) && data.Tiles[x, r.Y + r.Height] == InteriorTileType.Void)
            {
                if (rng.NextBool(0.3f))
                    data.Tiles[x, r.Y + r.Height - 1] = InteriorTileType.Window;
            }
        }
        for (int y = r.Y + 2; y < r.Y + r.Height - 2; y++)
        {
            // Left wall
            if (TileInBounds(data, r.X, y) && data.Tiles[r.X, y] == InteriorTileType.Wall &&
                TileInBounds(data, r.X - 1, y) && data.Tiles[r.X - 1, y] == InteriorTileType.Void)
            {
                if (rng.NextBool(0.3f))
                    data.Tiles[r.X, y] = InteriorTileType.Window;
            }
            // Right wall
            if (TileInBounds(data, r.X + r.Width - 1, y) && data.Tiles[r.X + r.Width - 1, y] == InteriorTileType.Wall &&
                TileInBounds(data, r.X + r.Width, y) && data.Tiles[r.X + r.Width, y] == InteriorTileType.Void)
            {
                if (rng.NextBool(0.3f))
                    data.Tiles[r.X + r.Width - 1, y] = InteriorTileType.Window;
            }
        }
    }

    /// <summary>Place ceiling lights in rooms that don't already have many.</summary>
    private static void PlaceRoomLights(InteriorData data, SeededRandom rng, InteriorRoom room)
    {
        var r = room.TileRect;
        // Place lights every ~4 tiles in a grid pattern
        for (int x = r.X + 2; x < r.X + r.Width - 2; x += 4)
            for (int y = r.Y + 2; y < r.Y + r.Height - 2; y += 4)
                if (TileInBounds(data, x, y) && data.Tiles[x, y] == InteriorTileType.Floor && rng.NextBool(0.4f))
                    data.Tiles[x, y] = InteriorTileType.Light;
    }

    /// <summary>Place a tile only if the target position currently has a walkable Floor tile.</summary>
    private static void PlaceTileIfFloor(InteriorData data, int x, int y, InteriorTileType tile)
    {
        if (TileInBounds(data, x, y) && data.Tiles[x, y] == InteriorTileType.Floor)
            data.Tiles[x, y] = tile;
    }

    /// <summary>
    /// Removes non-walkable furniture tiles adjacent to DoorOpen tiles
    /// so that room entrances are never obstructed.
    /// </summary>
    private static void ClearDoorwayObstructions(InteriorData data)
    {
        for (int x = 0; x < data.Width; x++)
        {
            for (int y = 0; y < data.Height; y++)
            {
                if (data.Tiles[x, y] != InteriorTileType.DoorOpen) continue;

                // Check all four cardinal neighbours
                ClearIfObstructing(data, x - 1, y);
                ClearIfObstructing(data, x + 1, y);
                ClearIfObstructing(data, x, y - 1);
                ClearIfObstructing(data, x, y + 1);
            }
        }
    }

    /// <summary>
    /// If the tile at (x,y) is a non-walkable furniture/decoration tile
    /// (not a structural Wall), revert it to Floor so doorways stay clear.
    /// </summary>
    private static void ClearIfObstructing(InteriorData data, int x, int y)
    {
        if (!TileInBounds(data, x, y)) return;
        var tile = data.Tiles[x, y];
        // Keep walls, void, and already-walkable tiles
        if (tile == InteriorTileType.Wall || tile == InteriorTileType.Void || IsWalkable(tile))
            return;
        // Everything else is furniture blocking the doorway — clear it
        data.Tiles[x, y] = InteriorTileType.Floor;
    }

    #endregion

    #region NPC Placement

    private static void PlaceStationNpcs(InteriorData data, SeededRandom rng)
    {
        // Trader in trading post
        var tradingRoom = data.Rooms.Find(r => r.Function == RoomFunction.TradingPost);
        if (tradingRoom != null)
        {
            data.Npcs.Add(CreateNpc(rng, "TRADER", tradingRoom.CenterX - 1, tradingRoom.CenterY,
                TraderDialogue, new Color3(255, 220, 80)));
        }

        // Mechanic in docking bay
        var dockingRoom = data.Rooms.Find(r => r.Function == RoomFunction.DockingBay);
        if (dockingRoom != null)
        {
            data.Npcs.Add(CreateNpc(rng, "MECHANIC", dockingRoom.TileRect.X + 2, dockingRoom.CenterY + 1,
                MechanicDialogue, new Color3(200, 150, 100)));
        }

        // Medic in medbay
        var medbayRoom = data.Rooms.Find(r => r.Function == RoomFunction.Medbay);
        if (medbayRoom != null)
        {
            data.Npcs.Add(CreateNpc(rng, "MEDIC", medbayRoom.CenterX + 1, medbayRoom.CenterY,
                MedicDialogue, new Color3(100, 220, 200)));
        }

        // Commander in command center
        var commandRoom = data.Rooms.Find(r => r.Function == RoomFunction.CommandCenter);
        if (commandRoom != null)
        {
            data.Npcs.Add(CreateNpc(rng, "COMMANDER", commandRoom.CenterX, commandRoom.CenterY + 1,
                CommanderDialogue, new Color3(100, 200, 255)));
        }

        // 4-8 random civilians in various rooms
        PlaceRandomCivilians(data, rng, 4, 9, "CIVILIAN");
    }

    private static void PlaceSettlementNpcs(InteriorData data, SeededRandom rng)
    {
        // Trader in market
        var marketRoom = data.Rooms.Find(r => r.Function == RoomFunction.Market);
        if (marketRoom != null)
        {
            data.Npcs.Add(CreateNpc(rng, "TRADER", marketRoom.CenterX - 1, marketRoom.CenterY,
                TraderDialogue, new Color3(255, 220, 80)));
        }

        // Bartender in cantina
        var cantinaRoom = data.Rooms.Find(r => r.Function == RoomFunction.Cantina);
        if (cantinaRoom != null)
        {
            data.Npcs.Add(CreateNpc(rng, "BARTENDER", cantinaRoom.CenterX, cantinaRoom.TileRect.Y + 1,
                [
                    "What'll it be? We've got synthesized drinks.",
                    "You look like you've had a long journey.",
                    "The local brew is... an acquired taste.",
                    "Take a seat. Or don't. I get paid either way."
                ], new Color3(200, 100, 150)));
        }

        // Comms officer
        var commsRoom = data.Rooms.Find(r => r.Function == RoomFunction.CommsCenter);
        if (commsRoom != null)
        {
            data.Npcs.Add(CreateNpc(rng, "COMMS OFFICER", commsRoom.CenterX + 1, commsRoom.CenterY,
                [
                    "Signals are weak out here. Atmospheric interference.",
                    "I monitor all frequencies. Nothing gets past me.",
                    "We got a distress call last week. Turned out to be nothing."
                ], new Color3(100, 255, 200)));
        }

        // Random civilians
        PlaceRandomCivilians(data, rng, 3, 7, "SETTLER");
    }

    private static void PlaceRandomCivilians(InteriorData data, SeededRandom rng, int min, int max, string role)
    {
        int count = rng.NextInt(min, max);
        for (int i = 0; i < count; i++)
        {
            var room = rng.Pick(data.Rooms);
            int nx = rng.NextInt(room.TileRect.X + 2, room.TileRect.X + room.TileRect.Width - 2);
            int ny = rng.NextInt(room.TileRect.Y + 2, room.TileRect.Y + room.TileRect.Height - 2);

            if (TileInBounds(data, nx, ny) && IsWalkable(data.Tiles[nx, ny]))
            {
                data.Npcs.Add(CreateNpc(rng, role, nx, ny,
                    CivilianDialogue, new Color3((byte)rng.NextInt(120, 220), (byte)rng.NextInt(120, 220), (byte)rng.NextInt(120, 220))));
            }
        }
    }

    private static InteriorNpc CreateNpc(
        SeededRandom rng, string role, int x, int y,
        string[] dialoguePool, Color3 color)
    {
        string firstName = rng.Pick(FirstNames);
        string lastName = rng.Pick(LastNames);

        // Pick 2-3 random dialogue lines (no duplicates)
        int lineCount = Math.Min(rng.NextInt(2, 4), dialoguePool.Length);
        var lines = new List<string>(lineCount);
        var used = new HashSet<int>();
        while (lines.Count < lineCount)
        {
            int idx = rng.NextInt(dialoguePool.Length);
            if (used.Add(idx))
                lines.Add(dialoguePool[idx]);
        }

        // Randomize body scale
        float bodyScale = rng.NextFloat(0.7f, 1.3f);

        // Random accessory (0=none ~40%, 1=hat ~20%, 2=helmet ~20%, 3=hood ~20%)
        int accessory = rng.NextFloat() < 0.4f ? 0 : rng.NextInt(1, 4);

        return new InteriorNpc
        {
            Name = $"{firstName} {lastName}",
            Role = role,
            TilePos = new(x, y),
            DialogueLines = lines.ToArray(),
            Color = color,
            BodyScale = bodyScale,
            Accessory = accessory
        };
    }

    #endregion
}
