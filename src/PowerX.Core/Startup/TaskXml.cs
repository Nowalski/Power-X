using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;

namespace PowerX.Core.Startup;

/// <summary>
/// Parses a task's own definition XML (<c>IRegisteredTask.Xml</c>) into the handful of fields
/// PowerX shows.
///
/// Why XML rather than the COM object model: every property read on a Task Scheduler COM object is
/// a late-bound IDispatch call that round-trips out of process to the scheduler service. Walking
/// <c>Definition</c> then <c>Triggers</c> then <c>Actions</c> then <c>RegistrationInfo</c> per task
/// is a dozen or more of those; asking for <c>Xml</c> is exactly one, and parsing the result
/// locally is free by comparison. Measured on a machine with 262 tasks: the bare folder walk is
/// ~50 ms, the walk plus per-task XML is ~110 ms, and the same walk through the object model was
/// ~860 ms (logon/boot subset) to ~1470 ms (full inventory).
/// </summary>
internal static class TaskXml
{
    internal sealed record Parsed(
        string Action,
        string Author,
        string Description,
        bool Hidden,
        string Triggers,
        bool IsLogonOrBoot);

    /// <summary>Returns null when the XML is missing or unparseable; callers skip that task, which
    /// is what the previous object-model readers did when a property throw.</summary>
    internal static Parsed? Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return null;
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch (System.Xml.XmlException) { return null; }
        if (doc.Root is null) return null;

        // Take the namespace off the document rather than hard-coding it, so a future schema
        // revision still parses.
        var ns = doc.Root.Name.Namespace;

        var reg = doc.Root.Element(ns + "RegistrationInfo");
        string author = ResolveIndirect(reg?.Element(ns + "Author")?.Value?.Trim() ?? "");
        string description = ResolveIndirect(reg?.Element(ns + "Description")?.Value?.Trim() ?? "");

        bool hidden = string.Equals(
            doc.Root.Element(ns + "Settings")?.Element(ns + "Hidden")?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

        var kinds = new List<string>();
        bool logonOrBoot = false;
        if (doc.Root.Element(ns + "Triggers") is { } triggers)
        {
            foreach (var t in triggers.Elements())
            {
                if (t.Name.LocalName is "LogonTrigger" or "BootTrigger") logonOrBoot = true;
                kinds.Add(TriggerName(t, ns));
            }
        }

        return new Parsed(
            Action: FirstActionText(doc.Root.Element(ns + "Actions"), ns),
            Author: author,
            Description: description,
            Hidden: hidden,
            Triggers: string.Join(", ", kinds.Distinct()),
            IsLogonOrBoot: logonOrBoot);
    }

    private static readonly ConcurrentDictionary<string, string> IndirectCache = new(StringComparer.Ordinal);

    /// <summary>
    /// Windows' own tasks store their author and description as MUI resource references
    /// (<c>$(@%SystemRoot%\system32\foo.dll,-100)</c>) rather than literal text. The COM object
    /// model resolves those for you; the raw XML does not, so showing the XML value verbatim would
    /// put a DLL path on screen where the publisher name belongs. <c>SHLoadIndirectString</c> is
    /// the documented way to resolve one. Results are cached because the same handful of resource
    /// ids repeat across dozens of tasks, and an unresolvable reference falls back to the original
    /// string rather than becoming empty.
    /// </summary>
    private static string ResolveIndirect(string value)
    {
        if (value.Length == 0 || !value.StartsWith("$(@", StringComparison.Ordinal) || !value.EndsWith(')'))
            return value;

        return IndirectCache.GetOrAdd(value, static raw =>
        {
            try
            {
                var sb = new StringBuilder(1024);
                return SHLoadIndirectString(raw[2..^1], sb, sb.Capacity, 0) == 0 && sb.Length > 0
                    ? sb.ToString()
                    : raw;
            }
            catch (Exception) { return raw; }
        });
    }

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHLoadIndirectString(string source, StringBuilder outBuf, int cchOutBuf, nint reserved);

    /// <summary>
    /// The first action's command line, matching what the object-model reader showed: only an
    /// <c>Exec</c> action has a path and arguments, and only the first action was ever displayed.
    /// A task whose first action is a COM handler shows nothing, as before.
    /// </summary>
    private static string FirstActionText(XElement? actions, XNamespace ns)
    {
        var first = actions?.Elements().FirstOrDefault();
        if (first is null || first.Name.LocalName != "Exec") return "";
        string cmd = first.Element(ns + "Command")?.Value?.Trim() ?? "";
        string args = first.Element(ns + "Arguments")?.Value?.Trim() ?? "";
        return $"{cmd} {args}".Trim();
    }

    /// <summary>The same wording the numeric trigger types used to map to, so nothing on screen
    /// changes. A calendar trigger's flavour comes from its schedule child element.</summary>
    private static string TriggerName(XElement trigger, XNamespace ns) => trigger.Name.LocalName switch
    {
        "EventTrigger" => "on an event",
        "TimeTrigger" => "once",
        "IdleTrigger" => "on idle",
        "RegistrationTrigger" => "at registration",
        "BootTrigger" => "at boot",
        "LogonTrigger" => "at logon",
        "SessionStateChangeTrigger" => "on session change",
        "CalendarTrigger" => trigger.Element(ns + "ScheduleByDay") is not null ? "daily"
            : trigger.Element(ns + "ScheduleByWeek") is not null ? "weekly"
            : trigger.Element(ns + "ScheduleByMonth") is not null ? "monthly"
            : trigger.Element(ns + "ScheduleByMonthDayOfWeek") is not null ? "monthly (day of week)"
            : "scheduled",
        _ => "scheduled",
    };
}
