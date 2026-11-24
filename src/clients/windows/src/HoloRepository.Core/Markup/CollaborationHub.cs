using System.Diagnostics;
using HoloRepository.Core.Imaging;

namespace HoloRepository.Core.Markup;

public sealed record Participant(string Id, string DisplayName, string Role, bool IsLocal, bool MicOn = true, bool CameraOn = true)
{
    public string Initials => string.Concat(DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(p => char.IsLetter(p[0])).Take(2).Select(p => char.ToUpperInvariant(p[0])));
}

/// <summary>Shared camera/viewport state so MDT members look at the same anatomy (FR-05).</summary>
public sealed record ViewportState(
    string SeriesId,
    SlicePlane Plane,
    int SliceIndex,
    WindowLevel Window,
    double YawDegrees,
    double PitchDegrees,
    string Author);

public sealed record SyncEvent(DateTimeOffset At, string Description, double? LatencyMs);

/// <summary>
/// Real-time channel for consultation state. The production implementation would ride on the Webex
/// data channel or the daemon's WebSocket endpoint; the prototype uses <see cref="LoopbackCollaborationHub"/>.
/// </summary>
public interface ICollaborationHub
{
    bool IsConnected { get; }
    string? SessionId { get; }
    IReadOnlyList<Participant> Participants { get; }
    double? LastRoundTripMs { get; }

    event EventHandler<Annotation>? AnnotationReceived;
    event EventHandler<ViewportState>? ViewportReceived;
    event EventHandler<SyncEvent>? Activity;
    event EventHandler? ParticipantsChanged;

    Task JoinAsync(string sessionId, string displayName, CancellationToken cancellationToken = default);
    Task LeaveAsync(CancellationToken cancellationToken = default);
    Task PublishAnnotationAsync(Annotation annotation, CancellationToken cancellationToken = default);
    Task PublishViewportAsync(ViewportState state, CancellationToken cancellationToken = default);
}

/// <summary>
/// Simulates a consultation with remote MDT members. Published messages are "acknowledged" by the
/// simulated peers after a configurable network delay, so latency against the 150 ms budget can be shown.
/// </summary>
public sealed class LoopbackCollaborationHub : ICollaborationHub
{
    public const double LatencyBudgetMs = 150;

    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _simulatedLatency;
    private readonly List<Participant> _participants = new();
    private readonly object _gate = new();

    public LoopbackCollaborationHub(TimeSpan? simulatedLatency = null, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _simulatedLatency = simulatedLatency ?? TimeSpan.FromMilliseconds(35);
        _delay = delay ?? Task.Delay;
    }

    public static IReadOnlyList<Participant> SimulatedPeers { get; } =
    [
        new("peer-radiology", "Consultant Radiologist", "Radiology", false),
        new("peer-surgery", "Thoracic Surgeon", "Surgery", false, CameraOn: true),
        new("peer-oncology", "Clinical Oncologist", "Oncology", false, MicOn: false),
    ];

    public bool IsConnected { get; private set; }
    public string? SessionId { get; private set; }
    public double? LastRoundTripMs { get; private set; }

    public IReadOnlyList<Participant> Participants
    {
        get
        {
            lock (_gate) return _participants.ToList();
        }
    }

    public event EventHandler<Annotation>? AnnotationReceived;
    public event EventHandler<ViewportState>? ViewportReceived;
    public event EventHandler<SyncEvent>? Activity;
    public event EventHandler? ParticipantsChanged;

    public async Task JoinAsync(string sessionId, string displayName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("Session id is required.", nameof(sessionId));
        await _delay(_simulatedLatency, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _participants.Clear();
            _participants.Add(new Participant("local", string.IsNullOrWhiteSpace(displayName) ? "You" : displayName, "Host", true));
            _participants.AddRange(SimulatedPeers);
        }
        SessionId = sessionId;
        IsConnected = true;
        ParticipantsChanged?.Invoke(this, EventArgs.Empty);
        Raise($"Joined consultation {sessionId} with {SimulatedPeers.Count} remote participants", null);
    }

    public Task LeaveAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate) _participants.Clear();
        IsConnected = false;
        Raise($"Left consultation {SessionId}", null);
        SessionId = null;
        ParticipantsChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public async Task PublishAnnotationAsync(Annotation annotation, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        double ms = await RoundTripAsync(cancellationToken).ConfigureAwait(false);
        Raise($"Shared {annotation.Kind.ToString().ToLowerInvariant()} \"{annotation.DisplayLabel}\" with {SimulatedPeers.Count} peers", ms);
    }

    public async Task PublishViewportAsync(ViewportState state, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        double ms = await RoundTripAsync(cancellationToken).ConfigureAwait(false);
        Raise($"Synchronised {state.Plane} viewport (slice {state.SliceIndex + 1}, {state.Window.Describe()})", ms);
    }

    /// <summary>Simulates a remote participant adding markup, as if received over the network.</summary>
    public async Task SimulateRemoteAnnotationAsync(Annotation annotation, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        double ms = await RoundTripAsync(cancellationToken).ConfigureAwait(false);
        var received = annotation with { IsRemote = true };
        AnnotationReceived?.Invoke(this, received);
        Raise($"{received.Author} added \"{received.DisplayLabel}\"", ms);
    }

    /// <summary>Simulates a remote participant steering the shared viewport.</summary>
    public async Task SimulateRemoteViewportAsync(ViewportState state, CancellationToken cancellationToken = default)
    {
        EnsureConnected();
        double ms = await RoundTripAsync(cancellationToken).ConfigureAwait(false);
        ViewportReceived?.Invoke(this, state);
        Raise($"{state.Author} moved the shared {state.Plane} view to slice {state.SliceIndex + 1}", ms);
    }

    private async Task<double> RoundTripAsync(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        await _delay(_simulatedLatency, cancellationToken).ConfigureAwait(false);
        sw.Stop();
        LastRoundTripMs = sw.Elapsed.TotalMilliseconds;
        return LastRoundTripMs.Value;
    }

    private void EnsureConnected()
    {
        if (!IsConnected) throw new InvalidOperationException("Join a consultation before sharing.");
    }

    private void Raise(string description, double? latency) =>
        Activity?.Invoke(this, new SyncEvent(DateTimeOffset.Now, description, latency));
}
