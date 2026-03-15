using Engine.Network.Client;
using SpaceExplorationGame.Core;

namespace SpaceExplorationGame.UI.Overlays.Map;

internal readonly record struct RemotePlayerBodyKey(int PlanetIndex, int MoonIndex = -1);

internal static class RemotePlayerMapPresenceHelper
{
    public static Dictionary<int, List<RemotePlayer>> CollectRemotePlayersBySystem(Game game)
    {
        var remotePlayersBySystem = new Dictionary<int, List<RemotePlayer>>();
        if (game.Network == null)
            return remotePlayersBySystem;

        foreach (var remote in game.Network.RemotePlayers.Values)
        {
            int systemIndex = remote.Location.SolarSystemIndex;
            if (remote.PlayerId == game.Network.LocalPlayerId || systemIndex < 0)
                continue;

            if (!remotePlayersBySystem.TryGetValue(systemIndex, out var occupants))
            {
                occupants = [];
                remotePlayersBySystem[systemIndex] = occupants;
            }

            occupants.Add(remote);
        }

        return remotePlayersBySystem;
    }

    public static Dictionary<RemotePlayerBodyKey, List<RemotePlayer>> CollectRemotePlayersByBody(Game game, int systemIndex)
    {
        var remotePlayersByBody = new Dictionary<RemotePlayerBodyKey, List<RemotePlayer>>();
        if (game.Network == null)
            return remotePlayersByBody;

        foreach (var remote in game.Network.RemotePlayers.Values)
        {
            var location = remote.Location;
            if (remote.PlayerId == game.Network.LocalPlayerId
                || location.SolarSystemIndex != systemIndex
                || location.PlanetIndex < 0)
            {
                continue;
            }

            var key = new RemotePlayerBodyKey(location.PlanetIndex, location.MoonIndex);
            if (!remotePlayersByBody.TryGetValue(key, out var occupants))
            {
                occupants = [];
                remotePlayersByBody[key] = occupants;
            }

            occupants.Add(remote);
        }

        return remotePlayersByBody;
    }

    public static List<RemotePlayer> GetRemotePlayersForSystem(Dictionary<int, List<RemotePlayer>> remotePlayersBySystem, int systemIndex) =>
        remotePlayersBySystem.TryGetValue(systemIndex, out var occupants) ? occupants : [];

    public static List<RemotePlayer> GetRemotePlayersForBody(Dictionary<RemotePlayerBodyKey, List<RemotePlayer>> remotePlayersByBody, int planetIndex, int moonIndex = -1) =>
        remotePlayersByBody.TryGetValue(new RemotePlayerBodyKey(planetIndex, moonIndex), out var occupants) ? occupants : [];

    public static string FormatRemotePlayerNames(IReadOnlyList<RemotePlayer> occupants)
    {
        return occupants.Count switch
        {
            0 => string.Empty,
            1 => occupants[0].Name.ToUpperInvariant(),
            2 => $"{occupants[0].Name.ToUpperInvariant()}, {occupants[1].Name.ToUpperInvariant()}",
            _ => $"{occupants[0].Name.ToUpperInvariant()}, {occupants[1].Name.ToUpperInvariant()} +{occupants.Count - 2}"
        };
    }

    public static float RenderRemotePlayersInfo(ISpriteRenderer renderer, float x, float y, float maxWidth, IReadOnlyList<RemotePlayer> occupants)
    {
        if (occupants.Count == 0)
            return 0f;

        renderer.DrawRectScreen(x, y - 6, maxWidth, 1, new Color4(40, 55, 90, 150));
        renderer.DrawTextScreen(x, y, occupants.Count == 1 ? "REMOTE PLAYER HERE" : "REMOTE PLAYERS HERE",
            new Color3(120, 180, 220), 1.3f, maxWidth);

        for (int i = 0; i < occupants.Count; i++)
        {
            renderer.DrawTextScreen(x, y + 18 + i * 18,
                $"- {occupants[i].Name.ToUpperInvariant()}{GetLocationSuffix(occupants[i])}",
                new Color3(200, 220, 255), 1.4f, maxWidth);
        }

        return 24 + occupants.Count * 18;
    }

    private static string GetLocationSuffix(RemotePlayer remote)
    {
        var location = remote.Location;
        if (location.IsOnSettlement)
            return $" [SETTLEMENT P{location.PlanetIndex + 1}]";
        if (location.IsOnMoon)
            return $" [MOON P{location.PlanetIndex + 1}M{location.MoonIndex + 1}]";
        if (location.IsOnPlanet)
            return $" [PLANET {location.PlanetIndex + 1}]";
        if (location.IsOnSpaceStation)
            return $" [STATION {location.SpaceStationIndex + 1}]";
        return " [IN SYSTEM]";
    }
}
