using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace BrowserPicker;

internal static class BrowserRegistry
{
    private const string BrowsersKey  = @"SOFTWARE\Clients\StartMenuInternet";
    private const string SettingsKey   = @"Software\BrowserPicker";
    private const string LastBrowserValue = "LastBrowser";

    internal static async Task<List<BrowserItem>> GetInstalledBrowsersAsync()
    {
        // Dictionary keyed by display name (lower-case); user hive wins over machine hive
        var byName = new Dictionary<string, BrowserItem>(StringComparer.OrdinalIgnoreCase);

        ReadFromHive(Registry.LocalMachine, byName);
        ReadFromHive(Registry.CurrentUser, byName);

        // Detect duplicate display names → set tooltip to exe path
        var nameCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in byName.Values)
            nameCount[item.Name] = nameCount.TryGetValue(item.Name, out var c) ? c + 1 : 1;

        var result = new List<BrowserItem>();
        foreach (var item in byName.Values)
        {
            if (nameCount[item.Name] > 1)
                item.ToolTipText = item.ExecutablePath;

            result.Add(item);
        }

        result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return await Task.FromResult(result);
    }

    private static void ReadFromHive(RegistryKey hive, Dictionary<string, BrowserItem> dict)
    {
        try
        {
            using var key = hive.OpenSubKey(BrowsersKey);
            if (key is null)
                return;

            foreach (var browserKeyName in key.GetSubKeyNames())
            {
                // Exclude ourselves from the list
                if (string.Equals(browserKeyName, "BrowserPicker", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var browserKey = key.OpenSubKey(browserKeyName);
                    if (browserKey is null)
                        continue;

                    var displayName = browserKey.GetValue(null) as string
                                      ?? browserKeyName;

                    // Resolve executable path from shell\open\command
                    using var commandKey = browserKey.OpenSubKey(@"shell\open\command");
                    var command = commandKey?.GetValue(null) as string;
                    if (string.IsNullOrWhiteSpace(command))
                        continue;

                    var exePath = ParseExePath(command);
                    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                        continue;

                    var item = new BrowserItem
                    {
                        Name = displayName,
                        ExecutablePath = exePath
                    };

                    // User hive entries overwrite machine hive entries (same key)
                    dict[browserKeyName] = item;
                }
                catch
                {
                    // Skip malformed entries
                }
            }
        }
        catch
        {
            // Hive inaccessible
        }
    }

    private static string ParseExePath(string command)
    {
        command = command.Trim();

        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : string.Empty;
        }

        // Unquoted: take the first token
        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    /// <summary>Returns the executable path of the last chosen browser, or null if none saved.</summary>
    internal static string? GetLastBrowserPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
            return key?.GetValue(LastBrowserValue) as string;
        }
        catch { return null; }
    }

    /// <summary>Persists the chosen browser's executable path as the preference.</summary>
    internal static void SetLastBrowserPath(string executablePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(SettingsKey, writable: true);
            key?.SetValue(LastBrowserValue, executablePath);
        }
        catch { }
    }
}
