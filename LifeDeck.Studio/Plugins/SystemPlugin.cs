using System.Diagnostics;
using System.IO;

namespace LifeDeck.Studio.Plugins;

public class SystemPlugin : IActionPlugin
{
    public string Id => "system";
    public string DisplayName => "System / PowerShell";

    public IReadOnlyList<PluginActionDefinition> Actions { get; } = new[]
    {
        new PluginActionDefinition
        {
            Id = "system.open",
            DisplayName = "Datei / Programm / Ordner öffnen",
            Hint = "Startet .exe, .bat, .cmd, öffnet Ordner oder Webseiten.",
            BrowseFilter = "Programme und Skripte|*.exe;*.bat;*.cmd;*.ps1;*.lnk|Alle Dateien|*.*",
            SuggestedValues = new[] { "Benutzerdefiniert" }
        },
        new PluginActionDefinition
        {
            Id = "system.powershell",
            DisplayName = "PowerShell-Skript ausführen",
            Hint = "Führt eine .ps1-Datei per PowerShell aus.",
            BrowseFilter = "PowerShell Skripte|*.ps1|Alle Dateien|*.*",
            SuggestedValues = new[] { "Benutzerdefiniert" }
        }
    };

    public IReadOnlyList<PluginEventDefinition> Events { get; } = Array.Empty<PluginEventDefinition>();

    public Task ExecuteAsync(string action, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return Task.CompletedTask;

        if (action == "system.powershell" || string.Equals(action, "PowerShell-Skript ausführen", StringComparison.OrdinalIgnoreCase))
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{value}\"",
                UseShellExecute = true,
                WorkingDirectory = File.Exists(value) ? Path.GetDirectoryName(value)! : Environment.CurrentDirectory
            };
            Process.Start(psi);
        }
        else
        {
            var psi = new ProcessStartInfo
            {
                FileName = value,
                UseShellExecute = true
            };

            if (File.Exists(value))
                psi.WorkingDirectory = Path.GetDirectoryName(value)!;

            Process.Start(psi);
        }

        return Task.CompletedTask;
    }
}
