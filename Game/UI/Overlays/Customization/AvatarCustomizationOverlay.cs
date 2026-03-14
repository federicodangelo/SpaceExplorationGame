using SpaceExplorationGame.Core;
using SpaceExplorationGame.UI.Overlays.Customization.Base;

namespace SpaceExplorationGame.UI.Overlays.Customization;

/// <summary>
/// Avatar customization overlay — thin subclass of CustomizationOverlayBase.
/// </summary>
public class AvatarCustomizationOverlay : CustomizationOverlayBase
{
    private static readonly AvatarSlotType[] SlotOrder =
    [
        AvatarSlotType.Suit,
        AvatarSlotType.Helmet,
        AvatarSlotType.Boots
    ];

    protected override string Title => "AVATAR CUSTOMIZATION";
    protected override Color3 TitleColor => new(100, 255, 180);
    protected override float PanelHeight => 420;
    protected override int SlotCount => SlotOrder.Length;

    protected override string GetSlotName(int slotIndex) =>
        AvatarPartCatalog.GetSlotName(SlotOrder[slotIndex]);

    protected override ICustomizablePart? GetEquippedPart(PlayerData player, int slotIndex) =>
        player.EquippedAvatarParts.TryGetValue(SlotOrder[slotIndex], out var part) ? part : null;

    protected override ICustomizablePart[] GetAvailablePartsForSlot(int slotIndex) =>
        AvatarPartCatalog.GetPartsForSlot(SlotOrder[slotIndex]);

    protected override bool IsPartOwned(PlayerData player, ICustomizablePart part)
    {
        foreach (var eq in player.EquippedAvatarParts.Values)
            if (eq.Id == part.Id) return true;
        return player.OwnedAvatarParts.Any(p => p.Id == part.Id);
    }

    protected override bool IsPartInInventory(PlayerData player, ICustomizablePart part) =>
        player.OwnedAvatarParts.Any(p => p.Id == part.Id);

    protected override void PerformEquip(PlayerData player, int slotIndex, ICustomizablePart newPart)
    {
        var slot = SlotOrder[slotIndex];
        player.EquippedAvatarParts.TryGetValue(slot, out var current);

        var invItem = player.OwnedAvatarParts.FirstOrDefault(p => p.Id == newPart.Id);
        if (invItem != null) player.OwnedAvatarParts.Remove(invItem);

        if (current != null)
            player.OwnedAvatarParts.Add(current);

        player.EquippedAvatarParts[slot] = (AvatarPart)newPart;
    }

    protected override void RemoveFromInventory(PlayerData player, ICustomizablePart part)
    {
        var inv = player.OwnedAvatarParts.First(p => p.Id == part.Id);
        player.OwnedAvatarParts.Remove(inv);
    }

    protected override void RenderStatComparison(ISpriteRenderer renderer, float x, float y,
        ICustomizablePart newPart, ICustomizablePart currentPart)
    {
        var n = ((AvatarPart)newPart).Stats;
        var o = ((AvatarPart)currentPart).Stats;

        var diffs = new List<StatDiff>();
        if (n.WalkSpeed != 0 || o.WalkSpeed != 0) diffs.Add(new StatDiff("SPD", o.WalkSpeed, n.WalkSpeed));
        if (n.OxygenCapacity != 0 || o.OxygenCapacity != 0) diffs.Add(new StatDiff("O2", o.OxygenCapacity, n.OxygenCapacity));
        if (n.TerrainPenalty != 0 || o.TerrainPenalty != 0) diffs.Add(new StatDiff("TRN", o.TerrainPenalty * 100, n.TerrainPenalty * 100));
        if (n.WeaponDamage != 0 || o.WeaponDamage != 0) diffs.Add(new StatDiff("DMG", o.WeaponDamage, n.WeaponDamage));
        if (n.Armor != 0 || o.Armor != 0) diffs.Add(new StatDiff("ARM", o.Armor, n.Armor));

        RenderStatDiffs(renderer, x, y, diffs);
    }
}
