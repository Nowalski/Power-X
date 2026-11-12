using System.Runtime.InteropServices;
using Microsoft.Win32;
using PowerX.Core.Processes;

namespace PowerX.Core.Diagnostics;

public sealed record EnvVar(string Name, string Value, bool Machine);

/// <summary>
/// Read/write user and machine environment variables. Machine scope needs elevation.
///
/// This goes to the registry directly instead of through <see cref="Environment.SetEnvironmentVariable(string, string?, EnvironmentVariableTarget)"/>:
/// that API reads values *expanded* and always writes REG_SZ, so editing a variable such as
/// <c>Path</c> silently turns every <c>%SystemRoot%</c> / <c>%USERPROFILE%</c> reference into a
/// fixed path, and a new value typed with <c>%VAR%</c> in it would never be expanded again.
/// Here values are shown unexpanded and an existing REG_EXPAND_SZ stays REG_EXPAND_SZ.
/// </summary>
public static partial class EnvironmentVariables
{
    private const string MachineKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const string UserKey = "Environment";

    public static IReadOnlyList<EnvVar> All()
    {
        var list = new List<EnvVar>();
        Collect(machine: true, list);
        Collect(machine: false, list);
        return list.OrderBy(v => v.Machine).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ActionResult Set(string name, string? value, bool machine)
    {
        if (string.IsNullOrWhiteSpace(name)) return ActionResult.Fail("Name cannot be empty.");
        name = name.Trim();
        if (name.Contains('=') || name.Contains('\0')) return ActionResult.Fail("A variable name cannot contain '=' or a null character.");
        try
        {
            using var key = machine
                ? Registry.LocalMachine.OpenSubKey(MachineKey, writable: true)
                : Registry.CurrentUser.CreateSubKey(UserKey, writable: true);
            if (key is null) return ActionResult.Fail("The environment registry key could not be opened.");

            if (value is null)
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }
            else
            {
                RegistryValueKind? existing = null;
                if (key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
                    existing = key.GetValueKind(name);
                key.SetValue(name, value, KindFor(existing, value));
            }

            BroadcastChange();
            return ActionResult.Ok;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return ActionResult.Fail(machine ? "Administrator rights required for machine variables." : ex.Message);
        }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }

    public static ActionResult Delete(string name, bool machine) => Set(name, null, machine);

    /// <summary>
    /// A list-style variable (Path, PATHEXT, PSModulePath, ...) is edited one entry per line,
    /// like the Windows editor does it, instead of as one long line.
    /// </summary>
    public static bool IsListVariable(string name, string value) =>
        name.EndsWith("PATH", StringComparison.OrdinalIgnoreCase) && value.Contains(';') ||
        name.Equals("PATHEXT", StringComparison.OrdinalIgnoreCase);

    /// <summary>The text shown in the editor: one entry per line for a list-style variable.</summary>
    public static string ToEditorText(string name, string value) =>
        IsListVariable(name, value)
            ? string.Join(Environment.NewLine, value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            : value;

    /// <summary>
    /// The value to store from the editor's text. A variable value cannot hold a line break
    /// (Windows would keep it, and every program reading the variable would get a broken
    /// value), so lines become <c>;</c>-separated entries and blank lines are dropped.
    /// </summary>
    public static string FromEditorText(string text)
    {
        if (text.IndexOfAny(['\r', '\n']) < 0) return text;
        return string.Join(";", text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Length > 0));
    }

    /// <summary>
    /// The registry type to write: keep an existing REG_EXPAND_SZ, and use it for any new value
    /// that references another variable, the way the Windows environment-variable editor does.
    /// </summary>
    internal static RegistryValueKind KindFor(RegistryValueKind? existing, string value) =>
        existing == RegistryValueKind.ExpandString || HasVariableReference(value)
            ? RegistryValueKind.ExpandString
            : RegistryValueKind.String;

    private static bool HasVariableReference(string value)
    {
        int first = value.IndexOf('%');
        return first >= 0 && value.IndexOf('%', first + 1) > first + 1;
    }

    private static void Collect(bool machine, List<EnvVar> into)
    {
        try
        {
            using var key = machine
                ? Registry.LocalMachine.OpenSubKey(MachineKey)
                : Registry.CurrentUser.OpenSubKey(UserKey);
            if (key is null) return;
            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name)) continue;
                var raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                string text = raw switch
                {
                    string s => s,
                    string[] multi => string.Join(";", multi),
                    null => "",
                    _ => raw.ToString() ?? "",
                };
                into.Add(new EnvVar(name, text, machine));
            }
        }
        catch (Exception) { /* scope unreadable */ }
    }

    // Tell running programs (Explorer, new consoles) that the environment changed, the same
    // broadcast Environment.SetEnvironmentVariable and the Windows editor send.
    private const uint HWND_BROADCAST = 0xFFFF;
    private const uint WM_SETTINGCHANGE = 0x001A;
    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, string lParam,
        uint flags, uint timeoutMs, out nint result);

    private static void BroadcastChange()
    {
        try { SendMessageTimeout((nint)HWND_BROADCAST, WM_SETTINGCHANGE, 0, "Environment", SMTO_ABORTIFHUNG, 1000, out _); }
        catch (Exception) { /* best effort: new processes pick the change up regardless */ }
    }
}
