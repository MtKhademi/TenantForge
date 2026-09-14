using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TSID.Creator.NET;

namespace TenantForge.Modules.Iam.Infrastructure;

/// <summary>
/// The single EF Core bridge between the IAM identifier's domain type
/// (<see cref="Tsid"/>) and its provider type (signed 64-bit <c>long</c>, stored
/// as PostgreSQL <c>bigint</c>). Every IAM entity map applies this one converter
/// type to its <c>Tsid</c> properties — there is no per-entity converter, so the
/// representation rule lives in exactly one class. The backing integer never
/// crosses the HTTP/JWT boundary: transport formatting is owned by
/// <c>TsidId.Format</c>, not by this converter.
///
/// The conversion is applied explicitly on each <c>Tsid</c> property (via
/// <c>Property(...).HasConversion(TsidValueConverter.Shared)</c>) rather than
/// through a model-wide convention, because EF Core strips convention-applied
/// value converters from key properties. Every IAM <c>Tsid</c> property is a
/// primary or foreign key, so explicit per-property application is what makes
/// the conversion actually reach the store.
/// </summary>
internal sealed class TsidValueConverter : ValueConverter<Tsid, long>
{
    /// <summary>
    /// The one converter instance every IAM map applies. The converter is
    /// stateless/immutable, so a single shared instance is safe and keeps the
    /// "one converter for the whole module" rule literal.
    /// </summary>
    public static readonly TsidValueConverter Shared = new();

    private TsidValueConverter()
        : base(tsid => tsid.ToLong(), value => Tsid.From(value))
    {
    }
}
