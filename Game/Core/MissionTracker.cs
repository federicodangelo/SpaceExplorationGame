namespace SpaceExplorationGame.Core;

/// <summary>
/// Tracks the player's active missions: accept, abandon, turn-in,
/// objective notifications, and HUD-tracked mission state.
/// </summary>
public class MissionTracker
{
    /// <summary>Maximum number of missions the player can have active at once.</summary>
    public const int MaxActive = 3;

    /// <summary>Currently active missions (accepted by the player).</summary>
    public List<Mission> Active { get; set; } = new();

    /// <summary>IDs of missions that have been accepted or completed (prevents re-offering).</summary>
    public HashSet<int> ClaimedIds { get; set; } = new();

    /// <summary>Total missions completed (lifetime stat).</summary>
    public int Completed { get; set; }

    /// <summary>Index into <see cref="Active"/> for the player's preferred tracked mission (-1 = auto).</summary>
    public int TrackedIndex { get; set; } = -1;

    /// <summary>Callback invoked when a bounty mission completes, providing the target system index for world impact.</summary>
    public Action<int>? OnBountyCompleted { get; set; }

    /// <summary>Missions that expired due to deadline. Cleared each frame after being read.</summary>
    public List<Mission> ExpiredLastFrame { get; } = new();

    /// <summary>Message to display when a mission expires. Auto-cleared after timer runs out.</summary>
    public string? ExpiredMessage { get; private set; }

    /// <summary>Timer for the expired message display.</summary>
    public float ExpiredMessageTimer { get; private set; }

    // ── Commands ──

    /// <summary>Accept a mission from the board. Returns false if at max capacity.</summary>
    public bool Accept(Mission mission)
    {
        if (Active.Count >= MaxActive) return false;
        mission.Status = MissionStatus.Active;
        if (mission.IsTimed)
            mission.TimeRemaining = mission.DeadlineSeconds;
        Active.Add(mission);
        ClaimedIds.Add(mission.Id);
        return true;
    }

    /// <summary>Abandon an active mission. The mission ID stays claimed so it won't reappear.</summary>
    public void Abandon(Mission mission)
    {
        Active.Remove(mission);
        ClampTrackedIndex();
    }

    /// <summary>Turn in a completed mission. Returns credits earned (0 if not completed).
    /// If the mission is chained, the next step is activated automatically.</summary>
    public int TurnIn(Mission mission)
    {
        if (mission.Status != MissionStatus.Completed) return 0;
        int reward = mission.CreditReward;
        Active.Remove(mission);
        Completed++;

        // Activate the next chain step
        if (mission.NextChainMission != null && Active.Count < MaxActive)
        {
            Accept(mission.NextChainMission);
        }

        ClampTrackedIndex();
        return reward;
    }

    // ── Timer ──

    /// <summary>Tick mission deadlines. Call each frame with delta time.</summary>
    public void Update(float dt)
    {
        ExpiredLastFrame.Clear();

        // Tick expired message timer
        if (ExpiredMessageTimer > 0)
        {
            ExpiredMessageTimer -= dt;
            if (ExpiredMessageTimer <= 0)
                ExpiredMessage = null;
        }

        for (int i = Active.Count - 1; i >= 0; i--)
        {
            var m = Active[i];
            if (m.IsTimed && m.Status == MissionStatus.Active)
            {
                m.TimeRemaining -= dt;
                if (m.TimeRemaining <= 0)
                {
                    m.TimeRemaining = 0;
                    ExpiredLastFrame.Add(m);
                    Active.RemoveAt(i);
                }
            }
        }

        if (ExpiredLastFrame.Count > 0)
        {
            ClampTrackedIndex();
            ExpiredMessage = $"MISSION EXPIRED: {ExpiredLastFrame[0].Title.ToUpper()}";
            ExpiredMessageTimer = 3.5f;
        }
    }

    // ── Notifications ──

    /// <summary>Notify: player entered a star system. Checks patrol missions.</summary>
    public void NotifySystemEntered(int systemIndex)
    {
        foreach (var m in Active)
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
        foreach (var m in Active)
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
        foreach (var m in Active)
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
        foreach (var m in Active)
        {
            if (m.Status == MissionStatus.Active && m.Type == MissionType.SettlementDelivery
                && m.Target.IsPlanet(systemIndex, planetIndex))
            {
                m.CurrentAmount = 1;
                m.Status = MissionStatus.Completed;
            }
        }
    }

    /// <summary>Notify: a pirate was killed by the player in the given system. Checks bounty missions.</summary>
    public void NotifyPirateKilled(int systemIndex)
    {
        foreach (var m in Active)
        {
            if (m.Status == MissionStatus.Active && m.Type == MissionType.BountyHunt)
            {
                // Location-restricted bounties only count kills in the target system
                if (m.Target.HasSystem && !m.Target.IsSystem(systemIndex))
                    continue;

                m.CurrentAmount++;
                if (m.CurrentAmount >= m.RequiredAmount)
                {
                    m.Status = MissionStatus.Completed;
                    // Notify world impact: bounty completed in this system
                    if (m.Target.HasSystem)
                        OnBountyCompleted?.Invoke(m.Target.SystemIndex);
                }
            }
        }
    }

    /// <summary>Notify: resources were mined in the given system. Checks mining missions.</summary>
    public void NotifyResourceMined(ResourceType resource, int amount, int systemIndex)
    {
        foreach (var m in Active)
        {
            if (m.Status == MissionStatus.Active && m.Type == MissionType.Mining
                && m.TargetResource == resource)
            {
                // Location-restricted mining only counts in the target system
                if (m.Target.HasSystem && !m.Target.IsSystem(systemIndex))
                    continue;

                m.CurrentAmount += amount;
                if (m.CurrentAmount >= m.RequiredAmount)
                    m.Status = MissionStatus.Completed;
            }
        }
    }

    // ── Queries ──

    /// <summary>Whether there are any completed missions ready to turn in.</summary>
    public bool HasCompleted => Active.Any(m => m.Status == MissionStatus.Completed);

    /// <summary>Gets the mission currently tracked in the HUD. Respects player's chosen index.</summary>
    public Mission? GetTracked()
    {
        if (Active.Count == 0) return null;

        // If player has explicitly chosen a mission, show that one
        if (TrackedIndex >= 0 && TrackedIndex < Active.Count)
            return Active[TrackedIndex];

        // Auto: prefer completed missions (need turn-in), then the first active
        return Active.FirstOrDefault(m => m.Status == MissionStatus.Completed)
            ?? Active.FirstOrDefault(m => m.Status == MissionStatus.Active);
    }

    /// <summary>Cycle to the next active mission for HUD tracking.</summary>
    public void CycleTracked()
    {
        if (Active.Count <= 1) return;
        TrackedIndex = ((TrackedIndex < 0 ? 0 : TrackedIndex) + 1) % Active.Count;
    }

    /// <summary>Reset all mission data to initial state.</summary>
    public void Reset()
    {
        Active.Clear();
        ClaimedIds.Clear();
        Completed = 0;
        TrackedIndex = -1;
    }

    /// <summary>Clamp the tracked index after mission list changes.</summary>
    private void ClampTrackedIndex()
    {
        if (Active.Count == 0)
            TrackedIndex = -1;
        else if (TrackedIndex >= Active.Count)
            TrackedIndex = Active.Count - 1;
    }
}
