using System.IO;
using System.Windows.Media;

namespace LifeDeck.Studio.Plugins;

public class SoundboardPlugin : IActionPlugin, IEventStateProvider
{
    private readonly Dictionary<string, MediaPlayer> _players = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _events = new(StringComparer.OrdinalIgnoreCase);

    public string Id => "soundboard";
    public string DisplayName => "Soundboard";

    public IReadOnlyList<PluginActionDefinition> Actions { get; } = new[]
    {
        new PluginActionDefinition
        {
            Id = "soundboard.toggle",
            DisplayName = "Sound abspielen / stoppen",
            Hint = "Spielt WAV/MP3 ab. Wenn derselbe Sound bereits läuft, stoppt der zweite Tastendruck ihn.",
            BrowseFilter = "Audio|*.wav;*.mp3;*.wma;*.aac;*.m4a|Alle Dateien|*.*",
            SuggestedValues = new[] { "Benutzerdefiniert" }
        }
    };

    public IReadOnlyList<PluginEventDefinition> Events { get; } = new[]
    {
        new PluginEventDefinition
        {
            Id = "soundboard.playing",
            DisplayName = "Sound läuft",
            ValueHint = "true/false",
            SuggestedValues = new[] { "true", "false" }
        },
        new PluginEventDefinition
        {
            Id = "soundboard.lastSound",
            DisplayName = "Letzter Sound",
            ValueHint = "Dateiname",
            SuggestedValues = Array.Empty<string>()
        }
    };

    public Task ExecuteAsync(string action, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !File.Exists(value)) return Task.CompletedTask;

        var key = Path.GetFullPath(value);
        if (_players.TryGetValue(key, out var existing))
        {
            existing.Stop();
            existing.Close();
            _players.Remove(key);
            _events["soundboard.playing"] = "false";
            _events["soundboard.lastSound"] = Path.GetFileName(value);
            return Task.CompletedTask;
        }

        var player = new MediaPlayer();
        player.Open(new Uri(key, UriKind.Absolute));
        player.MediaEnded += (_, _) =>
        {
            player.Close();
            _players.Remove(key);
            _events["soundboard.playing"] = "false";
            _events["soundboard.lastSound"] = Path.GetFileName(value);
        };
        player.Play();
        _players[key] = player;
        _events["soundboard.playing"] = "true";
        _events["soundboard.lastSound"] = Path.GetFileName(value);

        return Task.CompletedTask;
    }

    public IReadOnlyDictionary<string, string> GetCurrentEvents()
    {
        if (!_events.ContainsKey("soundboard.playing")) _events["soundboard.playing"] = "false";
        if (!_events.ContainsKey("soundboard.lastSound")) _events["soundboard.lastSound"] = "";
        return _events;
    }
}
