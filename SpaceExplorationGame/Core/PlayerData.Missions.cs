namespace SpaceExplorationGame.Core;

/// <summary>
/// Mission tracking: accept, abandon, turn-in, and objective notifications.
/// </summary>
public partial class PlayerData
{
    // ── Missions ──

    /// <summary>Maximum number of missions the player can have active at once.</summary>
    public const int MaxActiveMissions = 3;

    /// <summary>Currently active missions (accepted by the player).</summary>
    public List<Mission> ActiveMissions { get; set; } = new();

    /// <summary>IDs of missions that have been accepted or completed (prevents re-offering).</summary>
    public HashSet<int> ClaimedMissionIds { get; set; } = new();

    /// <summary>Total missions completed (lifetime stat).</summary>
    public int MissionsCompleted { get; set; }

    /// <summary>Index into ActiveMissions for the player's preferred tracked mission (-1 = auto).</summary>
    public int TrackedMissionIndex { get; set; } = -1;

    /// <summary>Accept a mission from the board. Returns false if at max capacity.</summary>
    public bool AcceptMission(Mission mission)
    {
        if (ActiveMissions.Count >= MaxActiveMissions) return false;
        mission.Status = MissionStatus.Active;
        ActiveMissions.Add(mission);
        ClaimedMissionIds.Add(mission.Id);
        return true;
    }

    /// <summary>Abandon an active mission. The mission ID stays claimed so it won't reappear.</summary>
    public void AbandonMission(Mission mission)
    {
        ActiveMissions.Remove(mission);
        ClampTrackedIndex();
    }

    /// <summary>Turn in a completed mission and collect the reward. Returns credits earned.</summary>
    public int TurnInMission(Mission mission)
    {
        if (mission.Status != MissionStatus.Completed) return 0;
        Credits += mission.CreditReward;
        ActiveMissions.Remove(mission);
        MissionsCompleted++;
        ClampTrackedIndex();
        return mission.CreditReward;
    }

    /// <summary>Notify: player entered a star system. Checks patrol/delivery missions.</summary>
    public void NotifySystemEntered(int systemIndex)
    {
        foreach (var m in ActiveMissions)
        {
            if (m.Status == MissionStatus.Active && m.Target.IsSystem(systemIndex))
            {
                if (m.Type == MissionType.Patrol)
                {
                    m.CurrentAmount = 1;
                    m.Status = MissionStatus.Completed;
                }
            }
        }
    }

    /// <summary>Notify: player docked at a station. Checks delivery missions.</summary>
    public void NotifyStationDocked(int systemIndex)
    {
        foreach (var m in ActiveMissions)
        {
            if (m.Status == MissionStatus.Active && m.Target.IsSystem(systemIndex))
            {
                if (m.Type == MissionType.Delivery)
                {
                    m.CurrentAmount = 1;
                    m.Status = MissionStatus.Completed;
                }
            }
        }
    }

    /// <summary>Notify: player landed on a planet. Checks exploration missions.</summary>
    public void NotifyPlanetLanded(int systemIndex, int planetIndex)
    {
        foreach (var m in ActiveMissions)
        {
            if (m.Status == MissionStatus.Active && m.Type == MissionType.Exploration
                && m.Target.IsPlanet(systemIndex, planetIndex))
            {
                m.CurrentAmount = 1;
                m.Status = MissionStatus.Completed;
            }
        }
    }

    /// <summary>Notify: player entered a settlement. Checks settlement delivery missions.</summary>
    public void NotifySettlementEntered(int systemIndex, int planetIndex)
    {
        foreach (var m in ActiveMissions)
        {
            if (m.Status == MissionStatus.Active && m.Type == MissionType.SettlementDelivery
                && m.Target.IsPlanet(systemIndex, planetIndex))
            {
                m.CurrentAmount = 1;
                m.Status = MissionStatus.Completed;
            }
        }
    }

    /// <summary>Notify: a pirate was killed by the player. Checks bounty missions.</summary>
    public void NotifyPirateKilled()
    {
        foreach (var m in ActiveMissions)
        {
            if (m.Status == MissionStatus.Active && m.Type == MissionType.BountyHunt)
            {
                m.CurrentAmount++;
                if (m.CurrentAmount >= m.RequiredAmount)
                    m.Status = MissionStatus.Completed;
            }
        }
    }

    /// <summary>Notify: resources were mined. Checks mining missions.</summary>
    public void NotifyResourceMined(ResourceType resource, int amount)
    {
        foreach (var m in ActiveMissions)
        {
            if (m.Status == MissionStatus.Active && m.Type == MissionType.Mining
                && m.TargetResource == resource)
            {
                m.CurrentAmount += amount;
                if (m.CurrentAmount >= m.RequiredAmount)
                    m.Status = MissionStatus.Completed;
            }
        }
    }

    /// <summary>Check whether there are any completed missions ready to turn in.</summary>
    public bool HasCompletedMissions => ActiveMissions.Any(m => m.Status == MissionStatus.Completed);

    /// <summary>Gets the mission currently tracked in the HUD. Respects player's chosen index.</summary>
    public Mission? GetTrackedMission()
    {
        if (ActiveMissions.Count == 0) return null;

        // If player has explicitly chosen a mission, show that one
        if (TrackedMissionIndex >= 0 && TrackedMissionIndex < ActiveMissions.Count)
            return ActiveMissions[TrackedMissionIndex];

        // Auto: prefer completed missions (need turn-in), then the first active
        return ActiveMissions.FirstOrDefault(m => m.Status == MissionStatus.Completed)
            ?? ActiveMissions.FirstOrDefault(m => m.Status == MissionStatus.Active);
    }

    /// <summary>Cycle to the next active mission for HUD tracking.</summary>
    public void CycleTrackedMission()
    {
        if (ActiveMissions.Count <= 1) return;
        TrackedMissionIndex = ((TrackedMissionIndex < 0 ? 0 : TrackedMissionIndex) + 1) % ActiveMissions.Count;
    }

    /// <summary>Clamp the tracked index after mission list changes.</summary>
    private void ClampTrackedIndex()
    {
        if (ActiveMissions.Count == 0)
            TrackedMissionIndex = -1;
        else if (TrackedMissionIndex >= ActiveMissions.Count)
            TrackedMissionIndex = ActiveMissions.Count - 1;
    }
}
