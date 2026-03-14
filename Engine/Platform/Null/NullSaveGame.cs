namespace Engine.Platform.Null;

/// <summary>No-op save game implementation for headless/server contexts.</summary>
public sealed class NullSaveGame : ISaveGame
{
    public void Save(string playerId, string json) { }
    public string? Load(string playerId) => null;
    public void Delete(string playerId) { }
    public IReadOnlyList<SaveGameInfo> ListSaves() => [];
}
