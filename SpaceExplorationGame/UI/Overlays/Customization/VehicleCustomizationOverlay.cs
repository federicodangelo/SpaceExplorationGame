using SpaceExplorationGame.Core;
using SpaceExplorationGame.Rendering;

namespace SpaceExplorationGame.UI.Overlays.Customization;

/// <summary>
/// Vehicle customization overlay — thin subclass of CustomizationOverlayBase.
/// </summary>
public class VehicleCustomizationOverlay : CustomizationOverlayBase
{
    private static readonly VehicleSlotType[] SlotOrder =
    [
        VehicleSlotType.Engine,
        VehicleSlotType.Chassis,
        VehicleSlotType.Lights
    ];

    protected override string Title => "VEHICLE CUSTOMIZATION";
    protected override Color3 TitleColor => new(255, 180, 80);
    protected override float PanelHeight => 420;
    protected override int SlotCount => SlotOrder.Length;

    protected override string GetSlotName(int slotIndex) =>
        VehiclePartCatalog.GetSlotName(SlotOrder[slotIndex]);

    protected override ICustomizablePart? GetEquippedPart(PlayerData player, int slotIndex) =>
        player.EquippedVehicleParts.TryGetValue(SlotOrder[slotIndex], out var part) ? part : null;

    protected override ICustomizablePart[] GetAvailablePartsForSlot(int slotIndex) =>
        VehiclePartCatalog.GetPartsForSlot(SlotOrder[slotIndex]);

    protected override bool IsPartOwned(PlayerData player, ICustomizablePart part)
    {
        foreach (var eq in player.EquippedVehicleParts.Values)
            if (eq.Id == part.Id) return true;
        return player.OwnedVehicleParts.Any(p => p.Id == part.Id);
    }

    protected override bool IsPartInInventory(PlayerData player, ICustomizablePart part) =>
        player.OwnedVehicleParts.Any(p => p.Id == part.Id);

    protected override void PerformEquip(PlayerData player, int slotIndex, ICustomizablePart newPart)
    {
        var slot = SlotOrder[slotIndex];
        player.EquippedVehicleParts.TryGetValue(slot, out var current);

        var invItem = player.OwnedVehicleParts.FirstOrDefault(p => p.Id == newPart.Id);
        if (invItem != null) player.OwnedVehicleParts.Remove(invItem);

        if (current != null)
            player.OwnedVehicleParts.Add(current);

        player.EquippedVehicleParts[slot] = (VehiclePart)newPart;
    }

    protected override void RemoveFromInventory(PlayerData player, ICustomizablePart part)
    {
        var inv = player.OwnedVehicleParts.First(p => p.Id == part.Id);
        player.OwnedVehicleParts.Remove(inv);
    }

    protected override void RenderStatComparison(SpriteRenderer renderer, float x, float y,
        ICustomizablePart newPart, ICustomizablePart currentPart)
    {
        var n = ((VehiclePart)newPart).Stats;
        var o = ((VehiclePart)currentPart).Stats;

        var diffs = new List<StatDiff>();
        if (n.Acceleration - o.Acceleration != 0) diffs.Add(new StatDiff("ACC", n.Acceleration - o.Acceleration));
        if (n.MaxSpeed - o.MaxSpeed != 0) diffs.Add(new StatDiff("SPD", n.MaxSpeed - o.MaxSpeed));
        if (n.RotationSpeed - o.RotationSpeed != 0) diffs.Add(new StatDiff("ROT", n.RotationSpeed - o.RotationSpeed));
        if (n.Friction - o.Friction != 0) diffs.Add(new StatDiff("GRP", (n.Friction - o.Friction) * 1000));
        if (n.Visibility - o.Visibility != 0) diffs.Add(new StatDiff("VIS", n.Visibility - o.Visibility));

        RenderStatDiffs(renderer, x, y, diffs);
    }
}
