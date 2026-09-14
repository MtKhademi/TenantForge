using TSID.Creator.NET;
using TenantForge.Modules.Iam.Domain;
using Xunit;

namespace TenantForge.Api.IntegrationTests;

/// <summary>
/// B017/S19: unit-style coverage for the IAM identifier seam
/// (<see cref="IamId"/>). These tests need no database — they verify the
/// representation rules at the boundary: generation, the <c>long</c>
/// round-trip, canonical formatting, invalid input, and the default value.
/// </summary>
public class IamIdTests
{
    [Fact]
    public void NewId_ProducesCanonical13CharacterString()
    {
        var id = IamId.NewId();

        var formatted = IamId.Format(id);

        Assert.Equal(IamId.CanonicalLength, formatted.Length);
        // Round-trips through the package's own long representation.
        Assert.Equal(id, Tsid.From(id.ToLong()));
    }

    [Fact]
    public void NewId_IsTimeSortableAndUniqueAcrossManyGenerations()
    {
        var ids = Enumerable.Range(0, 5_000).Select(_ => IamId.NewId()).ToList();
        var longs = ids.Select(id => id.ToLong()).ToList();

        Assert.Equal(longs.Count, longs.Distinct().Count());

        // TSIDs are time-sortable: the numeric value is monotonic non-decreasing
        // across generation within a single process.
        Assert.Equal(longs.OrderBy(l => l).ToList(), longs);
    }

    [Fact]
    public void Format_RoundTripsThroughLong()
    {
        var id = IamId.NewId();
        var asString = IamId.Format(id);

        var reparsed = Tsid.From(id.ToLong());

        Assert.Equal(id, reparsed);
        Assert.Equal(asString, IamId.Format(reparsed));
    }

    [Fact]
    public void TryParse_AcceptsCanonicalUppercaseAndNormalizes()
    {
        var id = IamId.NewId();
        var canonical = IamId.Format(id);

        Assert.True(IamId.TryParse(canonical, out var parsed));
        Assert.Equal(id, parsed);
    }

    [Fact]
    public void TryParse_AcceptsLowercaseButFormatNormalizesToUppercase()
    {
        var id = IamId.NewId();
        var canonical = IamId.Format(id);
        var lower = canonical.ToLowerInvariant();

        Assert.True(IamId.TryParse(lower, out var parsed));
        Assert.Equal(id, parsed);
        // Re-emitting always yields the canonical upper-case form.
        Assert.Equal(canonical, IamId.Format(parsed));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_RejectsNullOrBlank(string? value)
    {
        Assert.False(IamId.TryParse(value, out _));
    }

    [Theory]
    // GUID-shaped (with and without hyphens): wrong length.
    [InlineData("11111111-1111-1111-1111-111111111111")]
    [InlineData("11111111111111111111111111111111")]
    // Decimal / numeric: never accept the bigint backing value at the boundary.
    [InlineData("0000000000000")]
    [InlineData("1234567890123")]
    // Wrong length.
    [InlineData("01226N0640J7")]
    [InlineData("01226N0640J7QEXTRA")]
    public void TryParse_RejectsMalformedInput(string value)
    {
        Assert.False(IamId.TryParse(value, out _));
    }

    [Theory]
    // Non-Crockford characters (I, L, O, U are not in the alphabet).
    [InlineData("00000000000IU")]
    [InlineData("00000000000OO")]
    public void TryParse_RejectsNonCrockfordCharacters(string value)
    {
        Assert.False(IamId.TryParse(value, out _));
    }

    [Fact]
    public void TryParse_RejectsNonAsciiWithoutThrowing()
    {
        // A non-ASCII character at a legal position must be rejected cleanly.
        // The package's alphabet table is indexed by raw code point, so a
        // character above 0x7F would otherwise escape as an out-of-range index;
        // the seam bounds the input first.
        const string value = "01226N0640J7é"; // 12 Crockford chars + é (U+00E9)
        Assert.False(IamId.TryParse(value, out _));

        const string highValue = "01226N0640J7ß"; // 12 Crockford chars + ß (U+00DF)
        Assert.False(IamId.TryParse(highValue, out _));
    }

    [Fact]
    public void TryParse_RejectsTheAllZeroDefaultValue()
    {
        // "0000000000000" is a valid 13-char Crockford string but decodes to the
        // all-zero long, which is the domain's "unset" sentinel.
        Assert.False(IamId.TryParse("0000000000000", out var parsed));
        Assert.True(IamId.IsDefault(parsed));
    }

    [Fact]
    public void IsDefault_ReflectsTheAllZeroValue()
    {
        Assert.True(IamId.IsDefault(default(Tsid)));
        Assert.False(IamId.IsDefault(IamId.NewId()));
    }

    [Fact]
    public void ParseThenFormat_IsIdempotentForAnyValidId()
    {
        var id = IamId.NewId();
        var canonical = IamId.Format(id);

        Assert.True(IamId.TryParse(canonical, out var roundTripped));
        Assert.Equal(canonical, IamId.Format(roundTripped));
    }
}
