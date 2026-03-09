using System.Numerics;
using Arch.Core;
using SpaceExplorationGame.Core.Config;
using SpaceExplorationGame.ECS.Components;
using SpaceExplorationGame.Generation;
using SpaceExplorationGame.Simulation;
using SpaceExplorationGame.States;
using SpaceExplorationGame.UI.Overlays.Menu;

namespace SpaceExplorationGame.Core.Bot;


/// <summary>
/// Actions that the autoplay bot can request from InteriorState.
/// </summary>
public enum InteriorAction
{
    None,
    TakeOff,
    DisembarkOnFoot,
    DismissDialogue,
    Interact,
    TalkToNpc,
    BoardShip,
    OpenStarshipMenu,
    CloseAnyOverlay,
}

/// <summary>
/// Autoplay sub-bot for the interior (space station / settlement) state.
/// Visits all interactables and NPCs in shuffled order, then exits.
/// </summary>
internal sealed class InteriorBot : BotBase
{
    // ── Timing ──────────────────────────────────────────────────────
    private const float InteriorExitTimeMin = 15.0f;
    private const float InteriorExitTimeMax = 40.0f;

    // ── State ────────────────────────────────────────────────────────
    private enum InteriorGoal { VisitInteractables, Exit }
    private InteriorGoal _interiorGoal = InteriorGoal.VisitInteractables;
    private int _interiorInteractableIndex;
    private bool _starshipMenuWasOpen;
    private readonly List<int> _interiorVisitOrder = [];
    private TilePos _lastInteractedTilePos = new(-1, -1);

    // Pathfinding
    private readonly List<TilePos> _interiorPath = [];
    private TilePos _interiorPathTarget = new(-1, -1);
    private float _interiorStuckTimer;
    private float _dialogueCooldown;

    // Randomised per-session timing
    private float _interiorExitTime = 40.0f;

    private float _interiorTimer;
    private float _actionCooldown;

    internal InteriorBot(Random rng) : base(rng)
    {
        _interiorExitTime = RandRange(InteriorExitTimeMin, InteriorExitTimeMax);
    }

    internal void Reset()
    {
        _interiorTimer = 0;
        _actionCooldown = 0;
        _interiorExitTime = RandRange(InteriorExitTimeMin, InteriorExitTimeMax);
        _interiorGoal = InteriorGoal.VisitInteractables;
        _interiorInteractableIndex = 0;
        _interiorPath.Clear();
        _interiorPathTarget = new TilePos(-1, -1);
        _interiorStuckTimer = 0;
        _interiorVisitOrder.Clear();
        _lastInteractedTilePos = new TilePos(-1, -1);
        _starshipMenuWasOpen = false;
        _dialogueCooldown = 0;
        _statusGoal = "";
        _statusAction = "";
    }

    /// <summary>
    /// Walks around the interior, visits interactables, then exits.
    /// Returns true if the bot consumed input. Populates <paramref name="action"/> with any
    /// state-level action to trigger.
    /// </summary>
    internal bool Update(
        Game game,
        InteriorSimulation sim,
        SimulationPlayer simPlayer,
        StarshipMenuOverlay starshipMenu,
        InGameMenuOverlay inGameMenu,
        bool playerInsideShip,
        bool showingDialogue,
        bool anyOverlayOpen,
        out InteriorAction action)
    {
        action = InteriorAction.None;
        if (!Enabled) return false;

        float dt = game.DeltaTime;
        _interiorTimer += dt;
        _actionCooldown = Math.Max(0, _actionCooldown - dt);

        if (sim.EcsWorld.IsAlive(simPlayer.Entity) && anyOverlayOpen)
        {
            ref var blockedInput = ref sim.EcsWorld.Get<AvatarInputComponent>(simPlayer.Entity);
            blockedInput = AvatarInputComponent.Default();
            _statusAction = "Waiting for overlay to close...";
        }

        // ── Close in-game menu if open ──
        if (inGameMenu.IsOpen)
        {
            inGameMenu.Close();
            return true;
        }

        // ── Dismiss dialogue ──
        if (showingDialogue)
        {
            if (sim.EcsWorld.IsAlive(simPlayer.Entity))
            {
                ref var frozenInput = ref sim.EcsWorld.Get<AvatarInputComponent>(simPlayer.Entity);
                frozenInput.DesiredVelocity = Vector2.Zero;
            }

            _dialogueCooldown -= dt;
            if (_dialogueCooldown <= 0)
            {
                action = InteriorAction.DismissDialogue;
                _statusAction = "Reading dialogue...";
                _dialogueCooldown = DialogueLineDelay;
            }
            else
            {
                _statusAction = $"Reading dialogue ({_dialogueCooldown:F1}s)...";
            }
            return true;
        }

        // ── Handle starship menu ──
        if (starshipMenu.IsOpen)
        {
            if (!_starshipMenuWasOpen)
            {
                _actionCooldown = Math.Max(_actionCooldown, ActionDelay);
                _starshipMenuWasOpen = true;
            }

            if (_actionCooldown <= 0)
            {
                if (_interiorTimer > _interiorExitTime + 2.0f || _interiorGoal == InteriorGoal.Exit)
                {
                    action = InteriorAction.TakeOff;
                    _statusAction = "Taking off from interior";
                    _interiorTimer = 0;
                    _interiorExitTime = RandRange(InteriorExitTimeMin, InteriorExitTimeMax);
                    _interiorGoal = InteriorGoal.VisitInteractables;
                    _interiorInteractableIndex = 0;
                    _interiorVisitOrder.Clear();
                }
                else
                {
                    action = InteriorAction.DisembarkOnFoot;
                    _statusAction = "Disembarking on foot";
                    _starshipMenuWasOpen = false;
                }
            }
            else
            {
                _statusAction = $"Ship menu open, waiting ({_actionCooldown:F1}s)...";
            }
            return true;
        }
        else
        {
            _starshipMenuWasOpen = false;
        }

        // ── Handle open service overlays — wait ActionDelay then close ──
        if (anyOverlayOpen)
        {
            if (sim.EcsWorld.IsAlive(simPlayer.Entity))
            {
                ref var frozenInput = ref sim.EcsWorld.Get<AvatarInputComponent>(simPlayer.Entity);
                frozenInput.DesiredVelocity = Vector2.Zero;
            }
            if (_actionCooldown <= 0)
            {
                action = InteriorAction.CloseAnyOverlay;
                _statusAction = "Closing overlay";
                _actionCooldown = ActionDelay;
            }
            else
            {
                _statusAction = $"Viewing overlay ({_actionCooldown:F1}s)...";
            }
            return true;
        }

        // ── Inside ship — open starship menu ──
        if (playerInsideShip)
        {
            action = InteriorAction.OpenStarshipMenu;
            return true;
        }

        var world = sim.EcsWorld;
        if (!world.IsAlive(simPlayer.Entity))
            return true;

        ref var avatarTf = ref world.Get<Transform>(simPlayer.Entity);

        // ── Decide what to do ──
        if (_interiorTimer > _interiorExitTime)
            _interiorGoal = InteriorGoal.Exit;

        _statusGoal = _interiorGoal switch
        {
            InteriorGoal.VisitInteractables => $"VISITING INTERACTABLES ({Math.Max(0f, _interiorExitTime - _interiorTimer):F0}s to exit)",
            InteriorGoal.Exit => "HEADING TO EXIT",
            _ => "INTERIOR"
        };

        // ── Check proximity interactions ──
        if (_actionCooldown <= 0)
        {
            if (_interiorGoal == InteriorGoal.Exit)
            {
                if (sim.NearestInteractable?.Type == InteractableType.ExitDoor)
                {
                    action = InteriorAction.Interact;
                    _statusAction = "Using exit door";
                    _actionCooldown = ActionDelay;
                    return true;
                }

                if (sim.NearShip)
                {
                    action = InteriorAction.BoardShip;
                    _statusAction = "Boarding ship";
                    _actionCooldown = ActionDelay;
                    return true;
                }
            }
            else
            {
                TilePos targetTile = GetGoalTile(sim);

                if (sim.NearestInteractable != null)
                {
                    var interactable = sim.NearestInteractable;
                    if (interactable.TilePos != _lastInteractedTilePos &&
                        interactable.TilePos == targetTile)
                    {
                        action = InteriorAction.Interact;
                        _statusAction = $"Using {interactable.Type}";
                        _actionCooldown = ActionDelay;
                        _lastInteractedTilePos = interactable.TilePos;
                        _interiorInteractableIndex++;
                        _interiorPathTarget = new TilePos(-1, -1);
                        return true;
                    }
                }

                if (sim.NearestNpc != null &&
                    sim.NearestNpc.TilePos != _lastInteractedTilePos &&
                    sim.NearestNpc.TilePos == targetTile)
                {
                    action = InteriorAction.TalkToNpc;
                    _statusAction = $"Talking to {sim.NearestNpc.Name}";
                    _actionCooldown = ActionDelay;
                    _dialogueCooldown = DialogueLineDelay;
                    _lastInteractedTilePos = sim.NearestNpc.TilePos;
                    _interiorInteractableIndex++;
                    _interiorPathTarget = new TilePos(-1, -1);
                    return true;
                }
            }
        }

        // ── Move toward goal via pathfinding ──
        ref var avatarInput = ref world.Get<AvatarInputComponent>(simPlayer.Entity);

        TilePos goalTile = GetGoalTile(sim);
        int fromTileX = (int)(avatarTf.Position.X / WindowConfig.TileSize);
        int fromTileY = (int)(avatarTf.Position.Y / WindowConfig.TileSize);
        TilePos fromTile = new(fromTileX, fromTileY);

        if (goalTile != _interiorPathTarget)
        {
            _interiorPath.Clear();
            _interiorPath.AddRange(FindInteriorPath(sim.Interior, fromTile, goalTile));
            _interiorPathTarget = goalTile;
            _interiorStuckTimer = 0;
        }

        while (_interiorPath.Count > 0)
        {
            Vector2 wpWorld = TilePosToWorld(_interiorPath[0]);
            if (Vector2.Distance(avatarTf.Position, wpWorld) < WindowConfig.TileSize * 0.1f)
            {
                _interiorPath.RemoveAt(0);
                _interiorStuckTimer = 0;
            }
            else
                break;
        }

        if (_interiorPath.Count > 0)
        {
            Vector2 nextWp = TilePosToWorld(_interiorPath[0]);
            Vector2 toNext = nextWp - avatarTf.Position;
            float dist = toNext.Length();
            Vector2 dir = dist > 0.1f ? toNext / dist : Vector2.Zero;
            avatarInput.DesiredVelocity = dir * game.Player.AvatarWalkSpeed;
            _statusAction = $"Path [{_interiorPath.Count} steps] dist:{dist:F0}";

            _interiorStuckTimer += game.DeltaTime;
            if (_interiorStuckTimer > StuckTimeout)
            {
                _interiorPath.Clear();
                _interiorStuckTimer = 0;
                if (_interiorGoal != InteriorGoal.Exit)
                {
                    _interiorInteractableIndex++;
                    _statusAction = "Got stuck, skipping to next target";
                }
                else
                {
                    _statusAction = "Got stuck, recalculating path";
                }
            }
        }
        else
        {
            Vector2 goalWorld = TilePosToWorld(goalTile);
            Vector2 toDirect = goalWorld - avatarTf.Position;
            float directDist = toDirect.Length();
            if (directDist > WindowConfig.TileSize * 0.1f)
            {
                avatarInput.DesiredVelocity = Vector2.Normalize(toDirect) * game.Player.AvatarWalkSpeed;
                _statusAction = $"Direct walk (dist:{directDist:F0})";
            }
            else if (_actionCooldown <= 0)
            {
                avatarInput.DesiredVelocity = Vector2.Zero;
                if (_interiorGoal != InteriorGoal.Exit)
                {
                    action = InteriorAction.Interact;
                    _statusAction = "Interacting at target";
                }
                else
                {
                    _statusAction = "At exit, waiting...";
                }
                _actionCooldown = ActionDelay;
                _interiorInteractableIndex++;
                _interiorPathTarget = new TilePos(-1, -1);
            }
            else
            {
                avatarInput.DesiredVelocity = Vector2.Zero;
                _statusAction = $"Waiting after interact ({_actionCooldown:F1}s)...";
            }
            _interiorStuckTimer = 0;
        }

        return true;
    }

    private TilePos GetGoalTile(InteriorSimulation sim)
    {
        var interior = sim.Interior;

        if (_interiorGoal == InteriorGoal.Exit)
        {
            foreach (var interactable in interior.Interactables)
                if (interactable.Type == InteractableType.ExitDoor)
                    return interactable.TilePos;
            if (interior.LandingPadTilePos.HasValue)
                return interior.LandingPadTilePos.Value;
        }

        int totalItems = interior.Interactables.Count + interior.Npcs.Count;

        if (_interiorVisitOrder.Count == 0 && totalItems > 0)
        {
            for (int i = 0; i < totalItems; i++)
                _interiorVisitOrder.Add(i);
            // Fisher-Yates shuffle
            for (int i = _interiorVisitOrder.Count - 1; i > 0; i--)
            {
                int j = _rng.Next(i + 1);
                (_interiorVisitOrder[i], _interiorVisitOrder[j]) = (_interiorVisitOrder[j], _interiorVisitOrder[i]);
            }
        }

        if (_interiorInteractableIndex < _interiorVisitOrder.Count)
        {
            int mappedIndex = _interiorVisitOrder[_interiorInteractableIndex];
            if (mappedIndex < interior.Interactables.Count)
                return interior.Interactables[mappedIndex].TilePos;
            int npcIndex = mappedIndex - interior.Interactables.Count;
            if (npcIndex < interior.Npcs.Count)
                return interior.Npcs[npcIndex].TilePos;
        }

        // Nothing left — head to exit
        _interiorGoal = InteriorGoal.Exit;
        return GetGoalTile(sim);
    }


    private static List<TilePos> FindInteriorPath(InteriorData interior, TilePos from, TilePos to) =>
        AStarTilePath(interior.Width, interior.Height,
            (x, y) => IsInteriorTileWalkable(interior.Tiles[x, y]),
            from, to);

    private static bool IsInteriorTileWalkable(InteriorTileType tile) => tile switch
    {
        InteriorTileType.Void => false,
        InteriorTileType.Wall => false,
        InteriorTileType.Crate => false,
        InteriorTileType.Table => false,
        InteriorTileType.Plant => false,
        InteriorTileType.Window => false,
        InteriorTileType.Pipe => false,
        InteriorTileType.Shelf => false,
        InteriorTileType.Bed => false,
        InteriorTileType.BarCounter => false,
        InteriorTileType.Generator => false,
        InteriorTileType.Antenna => false,
        _ => true,
    };
}
