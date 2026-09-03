using PowerX.Core.Processes;

namespace PowerX.Core.Diagnostics;

public sealed record EnvVar(string Name, string Value, bool Machine);

/// <summary>Read/write user and machine environment variables. Machine scope needs elevation.</summary>
public static class EnvironmentVariables
{
    public static IReadOnlyList<EnvVar> All()
    {
        var list = new List<EnvVar>();
        Collect(EnvironmentVariableTarget.Machine, true, list);
        Collect(EnvironmentVariableTarget.User, false, list);
        return list.OrderBy(v => v.Machine).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ActionResult Set(string name, string? value, bool machine)
    {
        if (string.IsNullOrWhiteSpace(name)) return ActionResult.Fail("Name cannot be empty.");
        try
        {
            Environment.SetEnvironmentVariable(name.Trim(), value,
                machine ? EnvironmentVariableTarget.Machine : EnvironmentVariableTarget.User);
            return ActionResult.Ok;
        }
        catch (System.Security.SecurityException) { return ActionResult.Fail("Administrator rights required for machine variables."); }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }

    public static ActionResult Delete(string name, bool machine) => Set(name, null, machine);

    private static void Collect(EnvironmentVariableTarget target, bool machine, List<EnvVar> into)
    {
        try
        {
            foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables(target))
                into.Add(new EnvVar(kv.Key.ToString() ?? "", kv.Value?.ToString() ?? "", machine));
        }
        catch (Exception) { /* scope unreadable */ }
    }
}
