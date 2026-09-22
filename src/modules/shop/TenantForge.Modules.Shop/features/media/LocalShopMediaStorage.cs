using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace TenantForge.Modules.Shop.Features.Media;

internal sealed class LocalShopMediaStorage(IConfiguration configuration) : IShopMediaStorage
{
    private readonly string root = ResolveRoot(configuration);

    public async Task<StagedShopMedia> StageAsync(Stream sanitized, CancellationToken ct)
    {
        Directory.CreateDirectory(root);
        var staging = Path.Combine(root, ".staging");
        Directory.CreateDirectory(staging);

        var storageKey = GenerateStorageKey();
        var stagedPath = Path.Combine(staging, $"{storageKey}.tmp");

        await using var output = new FileStream(stagedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        sanitized.Position = 0;
        await sanitized.CopyToAsync(output, ct);

        return new StagedShopMedia(storageKey, stagedPath);
    }

    public Task CommitAsync(StagedShopMedia media, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var finalPath = GetCommittedPath(media.StorageKey);
        var directory = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(directory);
        File.Move(media.StagedPath, finalPath, overwrite: false);
        return Task.CompletedTask;
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var path = GetCommittedPath(storageKey);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);

        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        return Task.FromResult<Stream?>(stream);
    }

    public Task DeleteIfExistsAsync(string storageKey, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var committed = GetCommittedPath(storageKey);
        if (File.Exists(committed)) File.Delete(committed);

        var staged = Path.Combine(root, ".staging", $"{Path.GetFileName(storageKey)}.tmp");
        if (File.Exists(staged)) File.Delete(staged);

        return Task.CompletedTask;
    }

    internal static void ValidateRoot(IConfiguration configuration)
    {
        var root = ResolveRoot(configuration);
        Directory.CreateDirectory(root);
        var probe = Path.Combine(root, $".probe-{Guid.NewGuid():N}");
        File.WriteAllText(probe, string.Empty);
        File.Delete(probe);
    }

    private string GetCommittedPath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || storageKey.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Invalid storage key.");
        }

        var path = Path.GetFullPath(Path.Combine(root, storageKey));
        if (!path.StartsWith(root, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Storage key escaped the media root.");
        }

        return path;
    }

    private static string ResolveRoot(IConfiguration configuration)
    {
        var configured = configuration["Shop:MediaRoot"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException("The 'Shop:MediaRoot' configuration value is required for Shop media storage.");
        }

        var full = Path.GetFullPath(configured);
        if (!Path.IsPathFullyQualified(full))
        {
            throw new InvalidOperationException("The 'Shop:MediaRoot' configuration value must be an absolute path.");
        }

        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    private static string GenerateStorageKey()
    {
        Span<byte> bytes = stackalloc byte[24];
        RandomNumberGenerator.Fill(bytes);
        return $"{Convert.ToHexString(bytes).ToLowerInvariant()}.webp";
    }
}
