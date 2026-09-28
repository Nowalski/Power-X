using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PowerX.Core.Transactions;

namespace PowerX.Core.Tweaks;

/// <summary>
/// The single entry point for reading and changing tweak state. GUI and CLI both call this —
/// tweak logic is never duplicated. Every mutation is recorded in the <see cref="ChangeLog"/>.
/// </summary>
public sealed class TweakEngine
{
    private readonly IReadOnlyDictionary<string, TweakDefinition> _catalog;
    private readonly ChangeLog _changeLog;
    private readonly ILogger _log;
    private readonly string _sessionId = Guid.NewGuid().ToString("N")[..8];

    public TweakEngine(IEnumerable<TweakDefinition> catalog, ChangeLog? changeLog = null, ILogger<TweakEngine>? log = null)
    {
        _catalog = catalog.ToDictionary(t => t.Id);
        _changeLog = changeLog ?? new ChangeLog();
        _log = log ?? NullLogger<TweakEngine>.Instance;
    }

    public IReadOnlyCollection<TweakDefinition> Catalog => (IReadOnlyCollection<TweakDefinition>)_catalog.Values;

    public TweakDefinition? Find(string id) => _catalog.GetValueOrDefault(id);

    public IEnumerable<TweakDefinition> Search(string term) => _catalog.Values.Where(t =>
        t.Id.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        t.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        t.Tags.Any(g => g.Contains(term, StringComparison.OrdinalIgnoreCase)));

    public TweakStatus GetStatus(string id, TweakContext? context = null)
    {
        var def = _catalog[id];
        var ctx = context ?? TweakContext.Detect();
        if (!def.SupportsBuild(ctx.WindowsBuild))
        {
            return new TweakStatus(def, TweakState.NotApplicable, $"Not applicable to build {ctx.WindowsBuild}");
        }
        try
        {
            return new TweakStatus(def, def.Operation.Detect(ctx), null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Detect failed for {Id}", id);
            return new TweakStatus(def, TweakState.Unknown, ex.Message);
        }
    }

    public IReadOnlyList<TweakStatus> GetAllStatus(TweakContext? context = null)
    {
        var ctx = context ?? TweakContext.Detect();
        return _catalog.Keys.Select(id => GetStatus(id, ctx)).ToList();
    }

    /// <summary>Apply or revert one tweak. Detects, mutates, verifies and records the observed state.</summary>
    public ChangeRecord Execute(string id, ChangeAction action, TweakContext? context = null)
    {
        var def = _catalog[id];
        var ctx = context ?? TweakContext.Detect();

        TweakState before;
        try
        {
            before = def.Operation.Detect(ctx);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Pre-change detect failed for {Id}", id);
            before = TweakState.Unknown;
        }

        TweakOutcome outcome;
        bool operationAttempted = false;
        if (!def.SupportsBuild(ctx.WindowsBuild))
        {
            outcome = TweakOutcome.Fail($"Not applicable to Windows build {ctx.WindowsBuild}");
        }
        else if (def.Privilege == PrivilegeLevel.Administrator && !ctx.IsElevated && !ctx.DryRun)
        {
            outcome = TweakOutcome.Fail("This change requires administrator rights.");
        }
        else
        {
            try
            {
                operationAttempted = true;
                outcome = action == ChangeAction.Apply ? def.Operation.Apply(ctx) : def.Operation.Revert(ctx);
                if (outcome.Success && !ctx.DryRun && !def.Operation.Verify(ctx))
                {
                    outcome = TweakOutcome.Fail("Verification after write failed.");
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "{Action} failed for {Id}", action, id);
                outcome = TweakOutcome.Fail(ex.Message);
            }
        }

        TweakState resultingState = outcome.ResultingState;
        if (!ctx.DryRun && operationAttempted)
        {
            try
            {
                resultingState = def.Operation.Detect(ctx);
                if (outcome.Success && resultingState == TweakState.Unknown)
                    outcome = TweakOutcome.Fail("The operation returned, but its live state could not be determined.");
                else if (outcome.Success && resultingState !=
                         (action == ChangeAction.Apply ? TweakState.Applied : TweakState.Default))
                    outcome = TweakOutcome.Fail($"The live state is {resultingState}, which does not match the requested result.");
                if (!outcome.Success)
                    outcome = outcome with
                    {
                        Message = AppendDetail(outcome.Message, $"Live state after the attempt: {resultingState}.")
                    };
            }
            catch (Exception ex)
            {
                resultingState = TweakState.Unknown;
                _log.LogWarning(ex, "Post-change detect failed for {Id}", id);
                if (outcome.Success)
                    outcome = TweakOutcome.Fail("The operation returned, but live state after the attempt could not be read.");
                else
                    outcome = outcome with
                    {
                        Message = AppendDetail(outcome.Message, "Live state after the attempt could not be read.")
                    };
            }
        }
        else if (resultingState == TweakState.Unknown)
        {
            resultingState = before;
        }

        var record = new ChangeRecord
        {
            TweakId = id,
            TweakName = def.Name,
            Action = action,
            PreviousState = before.ToString(),
            ResultingState = resultingState.ToString(),
            Success = outcome.Success,
            Message = outcome.Message,
            Timestamp = DateTimeOffset.UtcNow,
            SessionId = _sessionId,
            WindowsBuild = ctx.WindowsBuild,
        };
        if (!ctx.DryRun)
        {
            try { _changeLog.Append(record); }
            catch (Exception ex)
            {
                _log.LogError(ex, "Could not record {Action} for {Id}", action, id);
                record = record with
                {
                    Success = false,
                    Message = AppendDetail(record.Message,
                        $"The live state is {record.ResultingState}, but change history could not be saved: {ex.Message}")
                };
            }
        }
        return record;
    }

    private static string AppendDetail(string? message, string detail) =>
        string.IsNullOrWhiteSpace(message) ? detail : message + " " + detail;

    /// <summary>Apply a set of tweaks sequentially, continuing after individual failures.</summary>
    public TransactionResult ApplyMany(IEnumerable<string> ids, ChangeAction action = ChangeAction.Apply, TweakContext? context = null)
    {
        var ctx = context ?? TweakContext.Detect();
        var records = new List<ChangeRecord>();
        RestartScope restart = RestartScope.None;

        foreach (var id in ids)
        {
            if (!_catalog.TryGetValue(id, out var def)) continue;
            var rec = Execute(id, action, ctx);
            records.Add(rec);
            if (rec.PreviousState != rec.ResultingState && rec.ResultingState != nameof(TweakState.Unknown))
                restart |= def.Restart;
        }

        return new TransactionResult(records, new RestartScopeSummary(
            restart.HasFlag(RestartScope.Application),
            restart.HasFlag(RestartScope.Explorer),
            restart.HasFlag(RestartScope.SignOut),
            restart.HasFlag(RestartScope.Reboot)));
    }

    public ChangeLog History => _changeLog;
}

public sealed record TweakStatus(TweakDefinition Definition, TweakState State, string? Note);
