using System.Collections.ObjectModel;
using HoloRepository.App.Services;
using HoloRepository.Core.Imaging;
using HoloRepository.Core.Markup;
using HoloRepository.Core.Notes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;

namespace HoloRepository.App.Pages;

public sealed partial class ConsultationPage : Page
{
    private readonly AppServices _services = AppServices.Current;
    private readonly ObservableCollection<SyncEvent> _activity = new();
    private readonly ObservableCollection<TranscriptLine> _lines = new();
    private readonly DispatcherTimer _captureTimer = new() { Interval = TimeSpan.FromSeconds(2.2) };
    private int _captureIndex;
    private bool _busy;

    public ConsultationPage()
    {
        InitializeComponent();
        ActivityList.ItemsSource = _activity;
        TranscriptList.ItemsSource = _lines;
        SessionBox.Text = $"MDT-{DateTime.Now:yyyyMMdd}-THORAX";
        foreach (var speaker in ConsultationTranscript.SampleDialogue.Select(d => d.Speaker).Distinct()) SpeakerBox.Items.Add(speaker);
        SpeakerBox.SelectedIndex = 0;

        _services.Hub.Activity += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            _activity.Insert(0, e);
            UpdateLatency();
        });
        _services.Hub.ParticipantsChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateCallState);
        _services.Transcript.LineAdded += (_, line) => DispatcherQueue.TryEnqueue(() =>
        {
            _lines.Add(line);
            TranscriptList.ScrollIntoView(line);
            UpdateTranscriptState();
        });
        _services.Transcript.Cleared += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            _lines.Clear();
            UpdateTranscriptState();
        });
        _captureTimer.Tick += CaptureTimer_Tick;
    }

    public static string MicGlyph(bool on) => on ? "" : "";

    public static string FormatOffset(TimeSpan offset) => offset.ToString(@"mm\:ss");

    public static string FormatEvent(DateTimeOffset at, string description, double? latencyMs) =>
        $"{at:HH:mm:ss}  {description}" + (latencyMs is double ms ? $"  ({ms:0} ms)" : string.Empty);

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        NameBox.Text = _services.Settings.DisplayName;
        _lines.Clear();
        foreach (var l in _services.Transcript.Lines) _lines.Add(l);
        UpdateTranscriptState();
        UpdateCallState();
    }

    private void UpdateCallState()
    {
        var hub = _services.Hub;
        ParticipantGrid.ItemsSource = hub.Participants;
        bool on = hub.IsConnected;
        NoCallText.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        JoinText.Text = on ? "Leave" : "Start consultation";
        JoinIcon.Symbol = on ? Symbol.Cancel : Symbol.Video;
        MicToggle.IsEnabled = CameraToggle.IsEnabled = RemotePinButton.IsEnabled = RemoteViewButton.IsEnabled = on;
        SessionBox.IsEnabled = NameBox.IsEnabled = !on;
        CallStatus.Text = on ? $"{hub.SessionId} · {hub.Participants.Count} participants · encrypted (simulated)" : "Not connected";
        UpdateLatency();
    }

    private void UpdateLatency()
    {
        var ms = _services.Hub.LastRoundTripMs;
        LatencyText.Text = ms is double v
            ? $"Last sync {v:0} ms · budget {LoopbackCollaborationHub.LatencyBudgetMs:0} ms {(v <= LoopbackCollaborationHub.LatencyBudgetMs ? "✓" : "⚠")}"
            : string.Empty;
    }

    private void UpdateTranscriptState() =>
        EmptyTranscriptText.Visibility = _lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private async void Join_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            if (_services.Hub.IsConnected)
            {
                await _services.Hub.LeaveAsync();
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(NameBox.Text))
                {
                    _services.Settings.DisplayName = NameBox.Text.Trim();
                    _services.Settings.Save();
                }
                await _services.Hub.JoinAsync(SessionBox.Text.Trim(), _services.Settings.DisplayName);
            }
        }
        catch (Exception ex)
        {
            CallStatus.Text = ex.Message;
        }
        finally
        {
            _busy = false;
            UpdateCallState();
        }
    }

    private void ShareToggle_Toggled(object sender, RoutedEventArgs e) => _services.ShareViewport = ShareToggle.IsOn;

    private async void RemotePin_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSeries(out string id, out var volume)) return;
        var target = PhantomGenerator.NoduleCentre(volume);
        var peer = LoopbackCollaborationHub.SimulatedPeers[1];
        await _services.Hub.SimulateRemoteAnnotationAsync(new Annotation
        {
            SeriesId = id,
            Kind = AnnotationKind.Callout,
            Start = target,
            Label = "Biopsy target – posterior approach?",
            Author = peer.DisplayName,
        });
    }

    private async void RemoteView_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetSeries(out string id, out var volume)) return;
        var (_, _, index) = MprSlicer.PointToSlice(volume, SlicePlane.Axial, PhantomGenerator.NoduleCentre(volume));
        var peer = LoopbackCollaborationHub.SimulatedPeers[0];
        await _services.Hub.SimulateRemoteViewportAsync(
            new ViewportState(id, SlicePlane.Axial, index, WindowLevel.Lung, -30, 15, peer.DisplayName));
    }

    private bool TryGetSeries(out string id, out Volume volume)
    {
        id = _services.CurrentSeriesId ?? string.Empty;
        volume = null!;
        if (_services.CurrentSeries is null)
        {
            CallStatus.Text = "Open a study first (Studies page) so colleagues have something to mark up.";
            return false;
        }
        volume = _services.Daemon.GetGeometry(id);
        return true;
    }

    // ---- Transcript --------------------------------------------------------------------------

    private void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (CaptureToggle.IsChecked == true)
        {
            if (_captureIndex >= ConsultationTranscript.SampleDialogue.Count || _services.Transcript.IsEmpty)
            {
                _services.Transcript.Clear();
                _captureIndex = 0;
            }
            CaptureText.Text = "Capturing…";
            _captureTimer.Start();
            CaptureTimer_Tick(null, null);
        }
        else
        {
            StopCapture();
        }
    }

    private void CaptureTimer_Tick(object? sender, object? e)
    {
        var dialogue = ConsultationTranscript.SampleDialogue;
        if (_captureIndex >= dialogue.Count)
        {
            StopCapture();
            return;
        }
        var (speaker, text) = dialogue[_captureIndex++];
        _services.Transcript.Add(speaker, text);
    }

    private void StopCapture()
    {
        _captureTimer.Stop();
        CaptureToggle.IsChecked = false;
        CaptureText.Text = "Capture";
    }

    private void LoadSample_Click(object sender, RoutedEventArgs e)
    {
        StopCapture();
        _services.Transcript.LoadSample();
        _captureIndex = ConsultationTranscript.SampleDialogue.Count;
    }

    private void ClearTranscript_Click(object sender, RoutedEventArgs e)
    {
        StopCapture();
        _captureIndex = 0;
        _services.Transcript.Clear();
    }

    private void AddLine_Click(object sender, RoutedEventArgs e) => AddTypedLine();

    private void LineBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            AddTypedLine();
            e.Handled = true;
        }
    }

    private void AddTypedLine()
    {
        if (string.IsNullOrWhiteSpace(LineBox.Text)) return;
        string speaker = string.IsNullOrWhiteSpace(SpeakerBox.Text) ? _services.Settings.DisplayName : SpeakerBox.Text;
        _services.Transcript.Add(speaker, LineBox.Text);
        LineBox.Text = string.Empty;
    }

    private void GenerateNotes_Click(object sender, RoutedEventArgs e)
    {
        StopCapture();
        WindowHelper.Main?.Navigate("notes");
    }
}
