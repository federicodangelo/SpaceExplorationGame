using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.Generation;

/// <summary>
/// Tile types used in interior layouts (stations and settlements).
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
}

/// <summary>Whether the interior belongs to a station or a settlement.</summary>
public enum InteriorType
{
    Station,
    Settlement
}

/// <summary>
/// A named room within an interior.
/// </summary>
public class InteriorRoom
{
    public string Name { get; set; } = "";
    public RoomFunction Function { get; set; }
    public TileRect TileRect { get; set; }
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
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public TilePos TilePos { get; set; }
    public string[] DialogueLines { get; set; } = [];
    public Color3 Color { get; set; } = new(100, 200, 255);
}

/// <summary>
/// An interactable object in the interior (terminal, repair station, etc.).
/// </summary>
public class InteriorInteractable
{
    public string Name { get; set; } = "";
    public InteractableType Type { get; set; }
    public TilePos TilePos { get; set; }
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
    HealthStation
}

/// <summary>
/// Complete data for a generated interior.
/// </summary>
public class InteriorData
{
    public InteriorType Type { get; set; }
    public string Name { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public InteriorTileType[,] Tiles { get; set; } = null!;
    public List<InteriorRoom> Rooms { get; set; } = [];
    public List<InteriorNpc> Npcs { get; set; } = [];
    public List<InteriorInteractable> Interactables { get; set; } = [];
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
            Type = InteriorType.Station,
            Name = stationName,
            Width = StationWidth,
            Height = StationHeight,
            Tiles = new InteriorTileType[StationWidth, StationHeight]
        };

        // Fill with void
        for (int x = 0; x < StationWidth; x++)
            for (int y = 0; y < StationHeight; y++)
                data.Tiles[x, y] = InteriorTileType.Void;

        // Generate rooms
        var docking = new InteriorRoom { Name = "DOCKING BAY", Function = RoomFunction.DockingBay, TileRect = new(2, StationHeight / 2 - 5, 10, 10) };
        var command = new InteriorRoom { Name = "COMMAND CENTER", Function = RoomFunction.CommandCenter, TileRect = new(StationWidth - 14, 2, 12, 8) };
        var trading = new InteriorRoom { Name = "TRADING POST", Function = RoomFunction.TradingPost, TileRect = new(StationWidth / 2 - 6, 2, 12, 8) };
        var medbay = new InteriorRoom { Name = "MEDBAY", Function = RoomFunction.Medbay, TileRect = new(StationWidth - 14, StationHeight - 10, 12, 8) };
        var quarters = new InteriorRoom { Name = "CREW QUARTERS", Function = RoomFunction.CrewQuarters, TileRect = new(StationWidth / 2 - 6, StationHeight - 10, 12, 8) };
        var cargo = new InteriorRoom { Name = "CARGO BAY", Function = RoomFunction.CargoBay, TileRect = new(2, 2, 10, 8) };

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

        // Mark docking bay floor as landing pad
        for (int x = docking.TileRect.X + 2; x < docking.TileRect.X + docking.TileRect.Width - 2; x++)
            for (int y = docking.TileRect.Y + 2; y < docking.TileRect.Y + docking.TileRect.Height - 2; y++)
                data.Tiles[x, y] = InteriorTileType.LandingPad;

        // Add decorative crates in cargo bay
        PlaceCrates(data, rng, cargo, 6);

        // Add consoles in command center
        PlaceConsoles(data, command, 1);

        // Add accent floors in trading post
        for (int x = trading.TileRect.X + 2; x < trading.TileRect.X + trading.TileRect.Width - 2; x++)
            for (int y = trading.TileRect.Y + 2; y < trading.TileRect.Y + trading.TileRect.Height - 2; y++)
                if (data.Tiles[x, y] == InteriorTileType.Floor)
                    data.Tiles[x, y] = InteriorTileType.FloorAccent;

        // Spawn point
        data.SpawnPoint = new TilePos(docking.CenterX, docking.CenterY);

        // Cargo terminal (sell mined resources)
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "CARGO TERMINAL",
            Type = InteractableType.CargoTerminal,
            TilePos = new(trading.CenterX, trading.TileRect.Y + 1)
        });
        data.Tiles[trading.CenterX, trading.TileRect.Y + 1] = InteriorTileType.Console;

        data.Interactables.Add(new InteriorInteractable
        {
            Name = "HEALTH STATION",
            Type = InteractableType.HealthStation,
            TilePos = new(medbay.CenterX - 2, medbay.TileRect.Y + 1)
        });
        data.Tiles[medbay.CenterX - 2, medbay.TileRect.Y + 1] = InteriorTileType.Console;

        data.Interactables.Add(new InteriorInteractable
        {
            Name = "MISSION BOARD",
            Type = InteractableType.MissionBoard,
            TilePos = new(command.CenterX, command.TileRect.Y + 1)
        });
        data.Tiles[command.CenterX, command.TileRect.Y + 1] = InteriorTileType.Console;

        data.Interactables.Add(new InteriorInteractable
        {
            Name = "EXIT",
            Type = InteractableType.ExitDoor,
            TilePos = new(docking.TileRect.X + docking.TileRect.Width / 2, docking.TileRect.Y + docking.TileRect.Height - 1)
        });

        // Repair station in docking bay (next to landing pad)
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "REPAIR STATION",
            Type = InteractableType.RepairStation,
            TilePos = new(docking.TileRect.X + 1, docking.CenterY)
        });
        data.Tiles[docking.TileRect.X + 1, docking.CenterY] = InteriorTileType.Console;

        // Ship customization terminal next to landing pad
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "SHIP CUSTOMIZATION",
            Type = InteractableType.ShipCustomization,
            TilePos = new(docking.TileRect.X + docking.TileRect.Width / 2 + 2, docking.TileRect.Y + docking.TileRect.Height - 2)
        });
        data.Tiles[docking.TileRect.X + docking.TileRect.Width / 2 + 2, docking.TileRect.Y + docking.TileRect.Height - 2] = InteriorTileType.Console;

        // Avatar customization terminal (top of docking room)
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "AVATAR CUSTOMIZATION",
            Type = InteractableType.AvatarCustomization,
            TilePos = new(docking.TileRect.X + docking.TileRect.Width / 2 - 2, docking.TileRect.Y + 1)
        });
        data.Tiles[docking.TileRect.X + docking.TileRect.Width / 2 - 2, docking.TileRect.Y + 1] = InteriorTileType.Console;

        // Vehicle customization terminal (top of docking room)
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "VEHICLE CUSTOMIZATION",
            Type = InteractableType.VehicleCustomization,
            TilePos = new(docking.TileRect.X + docking.TileRect.Width / 2 + 2, docking.TileRect.Y + 1)
        });
        data.Tiles[docking.TileRect.X + docking.TileRect.Width / 2 + 2, docking.TileRect.Y + 1] = InteriorTileType.Console;

        // Ship dealer terminal (left side of docking room)
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "SHIP DEALER",
            Type = InteractableType.ShipDealer,
            TilePos = new(docking.TileRect.X + docking.TileRect.Width / 2 - 2, docking.TileRect.Y + docking.TileRect.Height - 2)
        });
        data.Tiles[docking.TileRect.X + docking.TileRect.Width / 2 - 2, docking.TileRect.Y + docking.TileRect.Height - 2] = InteriorTileType.Console;

        // Place NPCs
        PlaceStationNpcs(data, rng);

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
            Tiles = new InteriorTileType[SettlementWidth, SettlementHeight]
        };

        // Fill with void
        for (int x = 0; x < SettlementWidth; x++)
            for (int y = 0; y < SettlementHeight; y++)
                data.Tiles[x, y] = InteriorTileType.Void;

        // Generate settlement layout: central street with buildings on either side
        var landing = new InteriorRoom { Name = "LANDING PAD", Function = RoomFunction.LandingPad, TileRect = new(SettlementWidth / 2 - 4, SettlementHeight - 9, 9, 7) };
        var market = new InteriorRoom { Name = "MARKET", Function = RoomFunction.Market, TileRect = new(2, 4, 10, 8) };
        var cantina = new InteriorRoom { Name = "CANTINA", Function = RoomFunction.Cantina, TileRect = new(SettlementWidth - 12, 4, 10, 8) };
        var housing1 = new InteriorRoom { Name = "HOUSING A", Function = RoomFunction.Housing, TileRect = new(2, 16, 8, 6) };
        var housing2 = new InteriorRoom { Name = "HOUSING B", Function = RoomFunction.Housing, TileRect = new(SettlementWidth - 10, 16, 8, 6) };
        var comms = new InteriorRoom { Name = "COMMS CENTER", Function = RoomFunction.CommsCenter, TileRect = new(SettlementWidth / 2 - 5, 2, 10, 6) };

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

        // Spawn point
        data.SpawnPoint = new TilePos(landing.CenterX, landing.CenterY);

        // Place interactables
        // Cargo terminal (sell mining resources)
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "CARGO TERMINAL",
            Type = InteractableType.CargoTerminal,
            TilePos = new(market.CenterX, market.TileRect.Y + 1)
        });
        data.Tiles[market.CenterX, market.TileRect.Y + 1] = InteriorTileType.Console;

        data.Interactables.Add(new InteriorInteractable
        {
            Name = "MISSION BOARD",
            Type = InteractableType.MissionBoard,
            TilePos = new(comms.CenterX, comms.TileRect.Y + 1)
        });
        data.Tiles[comms.CenterX, comms.TileRect.Y + 1] = InteriorTileType.Console;

        data.Interactables.Add(new InteriorInteractable
        {
            Name = "EXIT",
            Type = InteractableType.ExitDoor,
            TilePos = new(landing.CenterX, landing.TileRect.Y + landing.TileRect.Height - 1)
        });

        // Ship customization terminal next to landing pad
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "SHIP CUSTOMIZATION",
            Type = InteractableType.ShipCustomization,
            TilePos = new(landing.CenterX + 2, landing.TileRect.Y + landing.TileRect.Height - 2)
        });
        data.Tiles[landing.CenterX + 2, landing.TileRect.Y + landing.TileRect.Height - 2] = InteriorTileType.Console;

        // Repair station at the landing pad
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "REPAIR STATION",
            Type = InteractableType.RepairStation,
            TilePos = new(landing.TileRect.X + 1, landing.CenterY)
        });
        data.Tiles[landing.TileRect.X + 1, landing.CenterY] = InteriorTileType.Console;

        // Health station in the cantina (serves as settlement medbay)
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "HEALTH STATION",
            Type = InteractableType.HealthStation,
            TilePos = new(cantina.CenterX - 2, cantina.TileRect.Y + 1)
        });
        data.Tiles[cantina.CenterX - 2, cantina.TileRect.Y + 1] = InteriorTileType.Console;

        // Avatar customization terminal (top of landing pad)
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "AVATAR CUSTOMIZATION",
            Type = InteractableType.AvatarCustomization,
            TilePos = new(landing.CenterX - 2, landing.TileRect.Y + 1)
        });
        data.Tiles[landing.CenterX - 2, landing.TileRect.Y + 1] = InteriorTileType.Console;

        // Vehicle customization terminal (top of landing pad)
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "VEHICLE CUSTOMIZATION",
            Type = InteractableType.VehicleCustomization,
            TilePos = new(landing.CenterX + 2, landing.TileRect.Y + 1)
        });
        data.Tiles[landing.CenterX + 2, landing.TileRect.Y + 1] = InteriorTileType.Console;

        // Ship dealer terminal (left side of landing pad)
        data.Interactables.Add(new InteriorInteractable
        {
            Name = "SHIP DEALER",
            Type = InteractableType.ShipDealer,
            TilePos = new(landing.CenterX - 2, landing.TileRect.Y + landing.TileRect.Height - 2)
        });
        data.Tiles[landing.CenterX - 2, landing.TileRect.Y + landing.TileRect.Height - 2] = InteriorTileType.Console;

        // Place NPCs
        PlaceSettlementNpcs(data, rng);

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
            InteriorTileType.Void => new(10, 10, 15),
            _ => new(40, 40, 40)
        };
    }

    /// <summary>Whether a tile can be walked on.</summary>
    public static bool IsWalkable(InteriorTileType type)
    {
        return type is InteriorTileType.Floor or InteriorTileType.DoorOpen
            or InteriorTileType.FloorAccent or InteriorTileType.LandingPad
            or InteriorTileType.StreetTile or InteriorTileType.Console;
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
                if (tx_InBounds(data, x, ty))
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
                if (tx_InBounds(data, tx, y))
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
                if (tx_InBounds(data, x, ty))
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
                if (tx_InBounds(data, tx, y))
                {
                    if (data.Tiles[tx, y] == InteriorTileType.Wall)
                        data.Tiles[tx, y] = InteriorTileType.DoorOpen;
                    else if (data.Tiles[tx, y] == InteriorTileType.Void)
                        data.Tiles[tx, y] = InteriorTileType.StreetTile;
                }
            }
        }
    }

    private static bool tx_InBounds(InteriorData data, int x, int y)
        => x >= 0 && x < data.Width && y >= 0 && y < data.Height;

    private static void PlaceCrates(InteriorData data, SeededRandom rng, InteriorRoom room, int count)
    {
        var r = room.TileRect;
        for (int i = 0; i < count; i++)
        {
            int cx = rng.NextInt(r.X + 2, r.X + r.Width - 2);
            int cy = rng.NextInt(r.Y + 2, r.Y + r.Height - 2);
            if (tx_InBounds(data, cx, cy) && data.Tiles[cx, cy] == InteriorTileType.Floor)
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
            if (tx_InBounds(data, cx, cy))
                data.Tiles[cx, cy] = InteriorTileType.Console;
        }
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

        // 2-4 random civilians in various rooms
        int civilianCount = rng.NextInt(2, 5);
        for (int i = 0; i < civilianCount; i++)
        {
            var room = rng.Pick(data.Rooms);
            int nx = rng.NextInt(room.TileRect.X + 2, room.TileRect.X + room.TileRect.Width - 2);
            int ny = rng.NextInt(room.TileRect.Y + 2, room.TileRect.Y + room.TileRect.Height - 2);

            if (tx_InBounds(data, nx, ny) && IsWalkable(data.Tiles[nx, ny]))
            {
                data.Npcs.Add(CreateNpc(rng, "CIVILIAN", nx, ny,
                    CivilianDialogue, new Color3((byte)rng.NextInt(120, 220), (byte)rng.NextInt(120, 220), (byte)rng.NextInt(120, 220))));
            }
        }
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
        int civilianCount = rng.NextInt(2, 4);
        for (int i = 0; i < civilianCount; i++)
        {
            var room = rng.Pick(data.Rooms);
            int nx = rng.NextInt(room.TileRect.X + 2, room.TileRect.X + room.TileRect.Width - 2);
            int ny = rng.NextInt(room.TileRect.Y + 2, room.TileRect.Y + room.TileRect.Height - 2);

            if (tx_InBounds(data, nx, ny) && IsWalkable(data.Tiles[nx, ny]))
            {
                data.Npcs.Add(CreateNpc(rng, "SETTLER", nx, ny,
                    CivilianDialogue, new Color3((byte)rng.NextInt(120, 220), (byte)rng.NextInt(120, 220), (byte)rng.NextInt(120, 220))));
            }
        }
    }

    private static InteriorNpc CreateNpc(SeededRandom rng, string role, int x, int y,
        string[] dialoguePool, Color3 color)
    {
        string firstName = rng.Pick(FirstNames);
        string lastName = rng.Pick(LastNames);

        // Pick 1 random dialogue line
        var lines = new string[] { rng.Pick(dialoguePool) };

        return new InteriorNpc
        {
            Name = $"{firstName} {lastName}",
            Role = role,
            TilePos = new(x, y),
            DialogueLines = lines,
            Color = color
        };
    }

    #endregion
}
