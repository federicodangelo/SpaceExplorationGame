using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays.Customization;

/// <summary>
/// Ship customization overlay — thin subclass of CustomizationOverlayBase.
/// Slot list is dynamic based on the player's current ship type.
/// </summary>
public class ShipCustomizationOverlay : CustomizationOverlayBase
{
    private ShipSlotType[] _slots = [];

    protected override string Title => _currentShipName;
    protected override (byte R, byte G, byte B) TitleColor => (100, 220, 255);
    protected override float PanelHeight => 200 + _slots.Length * 55;
    protected override int SlotCount => _slots.Length;

    private string _currentShipName = "SHIP CUSTOMIZATION";

    /// <summary>Open overlay, refreshing available slots from the player's current ship type.</summary>
    public void Open(PlayerData player)
    {
        _slots = player.CurrentShipType.AvailableSlots;
        _currentShipName = $"{player.CurrentShipType.Name.ToUpper()} CUSTOMIZATION";
        Open();
    }

    protected override string GetSlotName(int slotIndex) =>
        ShipPartCatalog.GetSlotName(_slots[slotIndex]);

    protected override ICustomizablePart? GetEquippedPart(PlayerData player, int slotIndex) =>
        player.EquippedParts.TryGetValue(_slots[slotIndex], out var part) ? part : null;

    protected override ICustomizablePart[] GetAvailablePartsForSlot(int slotIndex) =>
        ShipPartCatalog.GetPartsForSlot(_slots[slotIndex]);

    protected override bool IsPartOwned(PlayerData player, ICustomizablePart part)
    {
        foreach (var eq in player.EquippedParts.Values)
            if (eq.Id == part.Id) return true;
        return player.OwnedParts.Any(p => p.Id == part.Id);
    }

    protected override bool IsPartInInventory(PlayerData player, ICustomizablePart part) =>
        player.OwnedParts.Any(p => p.Id == part.Id);

    protected override void PerformEquip(PlayerData player, int slotIndex, ICustomizablePart newPart)
    {
        var slot = _slots[slotIndex];
        player.EquippedParts.TryGetValue(slot, out var current);

        // Remove new part from inventory if owned
        var invItem = player.OwnedParts.FirstOrDefault(p => p.Id == newPart.Id);
        if (invItem != null) player.OwnedParts.Remove(invItem);

        // Un-equip current → inventory (skip tier-0 starter parts)
        if (current != null && current.Tier > 0)
            player.OwnedParts.Add(current);

        player.EquippedParts[slot] = (ShipPart)newPart;
        player.RecalculateShipStats();
    }

    protected override void RemoveFromInventory(PlayerData player, ICustomizablePart part)
    {
        var inv = player.OwnedParts.First(p => p.Id == part.Id);
        player.OwnedParts.Remove(inv);
    }

    protected override void RenderStatComparison(SpriteRenderer renderer, float x, float y,
        ICustomizablePart newPart, ICustomizablePart currentPart)
    {
        var n = ((ShipPart)newPart).Stats;
        var o = ((ShipPart)currentPart).Stats;

        var diffs = new List<(string Label, float Diff)>();
        if (n.Acceleration - o.Acceleration != 0) diffs.Add(("ACC", n.Acceleration - o.Acceleration));
        if (n.MaxSpeed - o.MaxSpeed != 0) diffs.Add(("SPD", n.MaxSpeed - o.MaxSpeed));
        if (n.RotationSpeed - o.RotationSpeed != 0) diffs.Add(("ROT", n.RotationSpeed - o.RotationSpeed));
        if (n.MaxHull - o.MaxHull != 0) diffs.Add(("HULL", n.MaxHull - o.MaxHull));
        if (n.MaxFuel - o.MaxFuel != 0) diffs.Add(("FUEL", n.MaxFuel - o.MaxFuel));
        if (n.FtlRange - o.FtlRange != 0) diffs.Add(("FTL", n.FtlRange - o.FtlRange));
        if (n.ShieldStrength - o.ShieldStrength != 0) diffs.Add(("SHD", n.ShieldStrength - o.ShieldStrength));
        if (n.WeaponDamage - o.WeaponDamage != 0) diffs.Add(("DMG", n.WeaponDamage - o.WeaponDamage));
        if (n.FuelEfficiency - o.FuelEfficiency != 0) diffs.Add(("EFF", (n.FuelEfficiency - o.FuelEfficiency) * 100));

        RenderStatDiffs(renderer, x, y, diffs);
    }
}
