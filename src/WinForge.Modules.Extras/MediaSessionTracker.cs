using Windows.Media.Control;

namespace WinForge.Modules.Extras;

/// <summary>Tracks media sessions and orders them: playing first, then most recently activated.</summary>
internal sealed class MediaSessionTracker
{
    private readonly Dictionary<string, long> _activationStamps = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _playingSessions = new(StringComparer.OrdinalIgnoreCase);
    private long _stampCounter;

    /// <summary>Updates activation stamps and returns true when any session newly started playing.</summary>
    public bool Update(IReadOnlyCollection<GlobalSystemMediaTransportControlsSession> sessions)
    {
        var newlyActivated = false;
        var liveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in sessions)
        {
            var id = session.SourceAppUserModelId;
            liveIds.Add(id);
            var isPlaying = IsPlaying(session);
            var wasPlaying = _playingSessions.Contains(id);
            if (isPlaying && !wasPlaying)
            {
                _activationStamps[id] = ++_stampCounter;
                _playingSessions.Add(id);
                newlyActivated = true;
            }
            else if (!isPlaying) _playingSessions.Remove(id);
            else if (!_activationStamps.ContainsKey(id)) _activationStamps[id] = ++_stampCounter;
        }
        foreach (var staleId in _activationStamps.Keys.Where(id => !liveIds.Contains(id)).ToArray())
        {
            _activationStamps.Remove(staleId);
            _playingSessions.Remove(staleId);
        }
        return newlyActivated;
    }

    public GlobalSystemMediaTransportControlsSession[] Order(
        IEnumerable<GlobalSystemMediaTransportControlsSession> sessions) => sessions
        .OrderByDescending(IsPlaying)
        .ThenByDescending(session => _activationStamps.GetValueOrDefault(session.SourceAppUserModelId))
        .ToArray();

    public static bool IsPlaying(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            return session.GetPlaybackInfo().PlaybackStatus ==
                   GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        }
        catch { return false; }
    }
}

