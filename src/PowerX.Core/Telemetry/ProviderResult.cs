namespace PowerX.Core.Telemetry;

/// <summary>Quality/availability of a metrics provider on the current machine.</summary>
public enum ProviderQuality
{
    /// <summary>Provider works and data is trustworthy.</summary>
    Reliable,
    /// <summary>Data is available but approximate or partially derived.</summary>
    Approximate,
    /// <summary>Provider cannot produce data on this system (missing hardware / API / privilege).</summary>
    Unavailable,
}

/// <summary>
/// Wraps a provider payload with an explicit capability signal. Consumers must render
/// "unavailable" states rather than fabricating zeros — see docs/DECISIONS.md #7.
/// </summary>
/// <typeparam name="T">Payload type.</typeparam>
public sealed record ProviderResult<T>(ProviderQuality Quality, T? Value, string? Detail = null)
{
    public bool HasValue => Quality != ProviderQuality.Unavailable && Value is not null;

    public static ProviderResult<T> Ok(T value) => new(ProviderQuality.Reliable, value);
    public static ProviderResult<T> Approximate(T value, string? detail = null) => new(ProviderQuality.Approximate, value, detail);
    public static ProviderResult<T> NotAvailable(string detail) => new(ProviderQuality.Unavailable, default, detail);
}
