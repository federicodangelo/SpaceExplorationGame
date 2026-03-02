using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace SpaceExplorationGame.Core;

/// <summary>
/// A single named timing entry./>.
/// </summary>
public struct DebugTimingEntry
{
    public string Name;
    public double ElapsedMs;
}

/// <summary>
/// Lightweight helper for recording named timing entries. Call <see cref="Begin"/> once per
/// update frame, then <see cref="Time"/> for each section. Results are available via
/// <see cref="Entries"/> until the next <see cref="Begin"/> call.
/// </summary>
public sealed class DebugTimer
{
    private readonly Stopwatch _sw = new();
    private readonly List<DebugTimingEntry> _entries = [];

    /// <summary>Current timing entries (valid until the next <see cref="Begin"/> call).</summary>
    public IReadOnlyList<DebugTimingEntry> Entries => _entries;

    /// <summary>Clear previous entries and start a new timing frame.</summary>
    public void Begin() => _entries.Clear();

    /// <summary>Pre-create zero-valued accumulator entries so they are always visible
    /// in the debug overlay even when no iterations run.</summary>
    public void PresetAccumulators(params string[] names)
    {
        foreach (var name in names)
            _entries.Add(new DebugTimingEntry() { Name = name, ElapsedMs = 0 });
    }

    /// <summary>Execute <paramref name="action"/> and record its elapsed time under <paramref name="name"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Time(string name, Action action)
    {
        _sw.Restart();
        action();
        _sw.Stop();
        _entries.Add(new DebugTimingEntry() { Name = name, ElapsedMs = _sw.Elapsed.TotalMilliseconds });
    }

    /// <summary>Execute <paramref name="action"/> and accumulate its elapsed time into an existing entry
    /// with the same <paramref name="name"/>, or create a new one. Useful inside fixed-step loops.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void TimeAndAccumulate(string name, Action action)
    {
        _sw.Restart();
        action();
        _sw.Stop();
        double ms = _sw.Elapsed.TotalMilliseconds;

        for (int i = 0; i < _entries.Count; i++)
        {
            if (_entries[i].Name == name)
            {
                var tmp = _entries[i];
                tmp.ElapsedMs += ms;
                _entries[i] = tmp;
                return;
            }
        }

        _entries.Add(new DebugTimingEntry() { Name = name, ElapsedMs = ms });
    }
}

/// <summary>
/// Interface for objects that can provide hierarchical timing data and extra debug lines.
/// Implemented by <see cref="GameState"/> and <see cref="SpaceExplorationGame.Simulation.ISimulation"/>.
/// </summary>
public interface IDebugInfoProvider
{
    /// <summary>
    /// Return hierarchical timing entries for the last update/render cycle.
    /// Return null if no detailed timing is available.
    /// </summary>
    IReadOnlyList<DebugTimingEntry>? GetDebugTimings();

    /// <summary>
    /// Return additional free-form debug lines to show in the overlay.
    /// Return null or empty if none.
    /// </summary>
    IReadOnlyList<string>? GetDebugInfo();
}

/// <summary>
/// Lightweight accumulator for free-form debug info strings. Call <see cref="Begin"/> once per
/// update frame, then <see cref="Add"/> for each line. Results are available via
/// <see cref="Entries"/> until the next <see cref="Begin"/> call.
/// </summary>
public sealed class DebugInfo
{
    private readonly List<string> _entries = [];

    /// <summary>Current info lines (valid until the next <see cref="Begin"/> call).</summary>
    public IReadOnlyList<string> Entries => _entries;

    /// <summary>Clear previous entries and start a new frame.</summary>
    public void Begin() => _entries.Clear();

    /// <summary>Add a debug info line.</summary>
    public void Add(string line) => _entries.Add(line);

    /// <summary>Add a formatted debug info line using an interpolated string.</summary>
    public void Add(ref DefaultInterpolatedStringHandler handler)
        => _entries.Add(handler.ToStringAndClear());
}
