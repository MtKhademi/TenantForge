namespace TenantForge.Modules.Shop.Features.Media;

internal sealed record StagedShopMedia(string StorageKey, string StagedPath);

internal interface IShopMediaStorage
{
    Task<StagedShopMedia> StageAsync(Stream sanitized, CancellationToken ct);
    Task CommitAsync(StagedShopMedia media, CancellationToken ct);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct);
    Task DeleteIfExistsAsync(string storageKey, CancellationToken ct);
}
