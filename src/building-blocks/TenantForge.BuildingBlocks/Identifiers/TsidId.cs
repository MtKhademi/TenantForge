using TSID.Creator.NET;

namespace TenantForge.BuildingBlocks.Identifiers;

/// <summary>
/// The system-wide seam for TenantForge public identifiers. Module code that
/// generates, validates, or formats a TSID goes through this class instead of
/// calling the <c>TSID.Creator.NET</c> package directly, so the representation
/// rules live in one reviewable place:
///
/// - databases store the identifier's signed 64-bit value (<c>long</c> /
///   PostgreSQL <c>bigint</c>);
/// - domain and persistence code work with the <see cref="Tsid"/> struct;
/// - HTTP requests/responses and JWT <c>sub</c> claims carry only the canonical
///   13-character Crockford-base32 string.
///
/// Public callers therefore never see the backing integer, and malformed,
/// GUID-shaped or decimal input can never escape as an unhandled
/// <see cref="ArgumentException"/>.
/// </summary>
public static class TsidId
{
    /// <summary>
    /// The length of the canonical TSID string. The Crockford-base32 encoding
    /// of a 64-bit value is always exactly this many characters.
    /// </summary>
    public const int CanonicalLength = 13;

    /// <summary>
    /// Generates a new, globally-unique, time-sortable identifier.
    /// </summary>
    public static Tsid NewId() => TsidCreator.GetTsid();

    /// <summary>
    /// True when <paramref name="id"/> is the struct's default (all-zero)
    /// value. Modules use this in place of <c>Guid.Empty</c> style "unset"
    /// checks.
    /// </summary>
    public static bool IsDefault(Tsid id) => id.ToLong() == 0L;

    /// <summary>
    /// Formats <paramref name="id"/> as the canonical 13-character string used
    /// on every transport boundary (JSON and JWT). Output is always upper-case,
    /// so a value that arrived lower-case is normalized.
    /// </summary>
    public static string Format(Tsid id) => id.ToString();

    /// <summary>
    /// Parses a public identifier string into a <see cref="Tsid"/> without
    /// throwing. Returns false (leaving <paramref name="id"/> at its default)
    /// for null/blank input, the wrong length, non-Crockford characters,
    /// GUID-shaped input, decimal input, and the all-zero "default" value.
    ///
    /// The package's <c>Tsid.From(string)</c> accepts lower-case Crockford text
    /// (it is case-insensitive), and lower-case input is deliberately allowed
    /// here; the canonicalization to upper-case happens only in
    /// <see cref="Format"/>, so an accepted lower-case value is always re-emitted
    /// in canonical form.
    /// </summary>
    public static bool TryParse(string? value, out Tsid id)
    {
        id = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.Length != CanonicalLength)
        {
            // Rejects GUID-shaped input (36 chars, or 32 without hyphens) and
            // most decimal input (up to 19 digits) in a single length check.
            return false;
        }

        if (value.All(char.IsAsciiDigit))
        {
            // A 13-digit decimal number is also valid Crockford text at the
            // byte level. Reject it explicitly so clients can never send or
            // learn to depend on the bigint backing value.
            return false;
        }

        foreach (var character in value)
        {
            // The package indexes its 128-entry alphabet table by raw code
            // point; only ASCII can be looked up safely. This bounds the input
            // before it reaches the package so a non-ASCII character can never
            // escape as an IndexOutOfRangeException.
            if (character > 127)
            {
                return false;
            }
        }

        try
        {
            var parsed = Tsid.From(value);
            // The all-zero value is the domain's "unset" sentinel; it must not
            // be accepted as a real public identifier.
            if (IsDefault(parsed))
            {
                return false;
            }

            id = parsed;
            return true;
        }
        catch
        {
            // Tsid.From throws for any non-Crockford character. Converting that
            // into a clean false is the whole point of this seam.
            return false;
        }
    }

    /// <summary>
    /// Parses an optional public identifier string, returning null (instead of
    /// the struct default) when <paramref name="value"/> is missing or invalid.
    /// Endpoint handlers use this for values such as optional route/query
    /// parameters.
    /// </summary>
    public static Tsid? TryParseNullable(string? value)
        => TryParse(value, out var id) ? id : null;
}
