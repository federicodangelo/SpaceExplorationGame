using SpaceExplorationGame.Core;
using SpaceExplorationGame.UI.Overlays.Customization.Base;

namespace SpaceExplorationGame.UI.Overlays.Customization;

/// <summary>
/// Ship customization overlay — thin subclass of CustomizationOverlayBase.
/// Slot list is dynamic based on the player's current ship type.
/// </summary>
public class ShipCustomizationOverlay : CustomizationOverlayBase
{
    private ShipSlotType[] _slots = [];

    protected override string Title => _currentShipName;
    protected override Color3 TitleColor => new(100, 220, 255);
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

    protected override void RenderStatComparison(ISpriteRenderer renderer, float x, float y,
        ICustomizablePart newPart, ICustomizablePart currentPart)
    {
        var n = ((ShipPart)newPart).Stats;
        var o = ((ShipPart)currentPart).Stats;

        var diffs = new List<StatDiff>();
        if (n.Acceleration != 0 || o.Acceleration != 0) diffs.Add(new StatDiff("ACC", o.Acceleration, n.Acceleration));
        if (n.MaxSpeed != 0 || o.MaxSpeed != 0) diffs.Add(new StatDiff("SPD", o.MaxSpeed, n.MaxSpeed));
        if (n.RotationSpeed != 0 || o.RotationSpeed != 0) diffs.Add(new StatDiff("ROT", o.RotationSpeed, n.RotationSpeed));
        if (n.MaxHull != 0 || o.MaxHull != 0) diffs.Add(new StatDiff("HULL", o.MaxHull, n.MaxHull));
        if (n.MaxFuel != 0 || o.MaxFuel != 0) diffs.Add(new StatDiff("FUEL", o.MaxFuel, n.MaxFuel));
        if (n.FtlRange != 0 || o.FtlRange != 0) diffs.Add(new StatDiff("FTL", o.FtlRange, n.FtlRange));
        if (n.ShieldStrength != 0 || o.ShieldStrength != 0) diffs.Add(new StatDiff("SHD", o.ShieldStrength, n.ShieldStrength));
        if (n.WeaponDamage != 0 || o.WeaponDamage != 0) diffs.Add(new StatDiff("DMG", o.WeaponDamage, n.WeaponDamage));
        if (n.FuelEfficiency != 0 || o.FuelEfficiency != 0) diffs.Add(new StatDiff("EFF", o.FuelEfficiency * 100, n.FuelEfficiency * 100));
        if (n.CargoCapacity != 0 || o.CargoCapacity != 0) diffs.Add(new StatDiff("CARGO", o.CargoCapacity, n.CargoCapacity));

        RenderStatDiffs(renderer, x, y, diffs);
    }
}
