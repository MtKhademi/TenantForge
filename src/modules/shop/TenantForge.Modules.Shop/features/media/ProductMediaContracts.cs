using Microsoft.AspNetCore.Http;

namespace TenantForge.Modules.Shop.Features.Media;

public sealed record UploadProductImageRequest(IFormFile File, string? AltText, int ExpectedGalleryVersion);

public sealed record ReorderProductImagesRequest(IReadOnlyList<string>? ImageIds, int ExpectedGalleryVersion);

public sealed record ProductImageResponse(string Id, string AltText, int DisplayOrder, int Width, int Height, string ContentUrl);

public sealed record ProductGalleryResponse(IReadOnlyList<ProductImageResponse> Images, int GalleryVersion);
