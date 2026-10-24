namespace GistDrive.Tests;

/// <summary>
/// An in-memory notification center. NotifierContract runs against it and, when enabled,
/// against Windows itself. SelectCancel stands in for the owner's click.
/// </summary>
public sealed class FakeNotifier : INotifier
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, (string Title, string Body, TimeSpan Remaining)> _countdowns = [];
    private readonly List<(string Title, string Body)> _messages = [];

    public event Action<string>? CancelSelected;

    /// <summary>
    /// The countdowns on show, each with the remaining time its bar shows now.
    /// </summary>
    public IReadOnlyList<(string Tag, string Title, string Body, TimeSpan Remaining)> Countdowns
    {
        get
        {
            lock (_gate) return [.. _countdowns.Select(c => (c.Key, c.Value.Title, c.Value.Body, c.Value.Remaining))];
        }
    }

    public IReadOnlyList<(string Title, string Body)> Messages
    {
        get
        {
            lock (_gate) return [.. _messages];
        }
    }

    public void ShowCountdown(string tag, string title, string body, TimeSpan remaining, TimeSpan window)
    {
        lock (_gate) _countdowns[tag] = (title, body, remaining);
    }

    public bool UpdateCountdown(string tag, TimeSpan remaining, TimeSpan window)
    {
        lock (_gate)
        {
            if (!_countdowns.TryGetValue(tag, out var countdown)) return false;
            _countdowns[tag] = countdown with { Remaining = remaining };
            return true;
        }
    }

    public void ShowMessage(string title, string body)
    {
        lock (_gate) _messages.Add((title, body));
    }

    public void Remove(string tag)
    {
        lock (_gate) _countdowns.Remove(tag);
    }

    public bool IsShowing(string tag)
    {
        lock (_gate) return _countdowns.ContainsKey(tag);
    }

    public void SelectCancel(string tag) => CancelSelected?.Invoke(tag);
}
