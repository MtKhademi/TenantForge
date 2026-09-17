using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TSID.Creator.NET;

namespace TenantForge.Modules.Shop.Infrastructure;

/// <summary>
/// The single EF Core bridge between the Shop identifier's domain type
/// (<see cref="Tsid"/>) and its provider type (signed 64-bit <c>long</c>,
/// stored as PostgreSQL <c>bigint</c>). Every Shop entity map applies this
/// one converter type to its <c>Tsid</c> properties — mirrors
/// TenantForge.Modules.Iam.Infrastructure.TsidValueConverter exactly, but
/// Shop needs its own copy because it is a separate assembly and the IAM
/// class is internal.
/// </summary>
internal sealed class ShopTsidValueConverter : ValueConverter<Tsid, long>
{
    public static readonly ShopTsidValueConverter Shared = new();

    private ShopTsidValueConverter()
        : base(tsid => tsid.ToLong(), value => Tsid.From(value))
    {
    }
}
