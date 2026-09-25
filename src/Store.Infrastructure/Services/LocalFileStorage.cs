using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Store.Application.Common;

namespace Store.Infrastructure.Services;

public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public string LocalRootPath { get; set; } = "wwwroot/uploads";
    public string PublicBaseUrl { get; set; } = "/uploads";

    /// <summary>Widest stored derivative. Anything larger is wasted bytes on every device.</summary>
    public int MaxWidth { get; set; } = 1600;

    public int ThumbnailWidth { get; set; } = 400;
    public long MaxFileSizeBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>
/// Stores uploads on the local filesystem and derives optimised variants.
/// </summary>
/// <remarks>
/// Every upload produces, in one pass: a size-capped original, a WebP derivative, a thumbnail for
/// grid cards, and a tiny blurred base64 placeholder. Doing this at upload time rather than at
/// request time is what lets the storefront emit a correct <c>srcset</c>, reserve exact layout
/// space (CLS), and never ship a 3 MB phone photo to a product card — the failure the legacy
/// store had, hotlinking unsized Firebase and Unsplash originals.
/// <para>
/// The interface is deliberately storage-agnostic, so moving to S3, Azure Blob or a CDN is a new
/// implementation rather than a change at every call site.
/// </para>
/// </remarks>
public sealed class LocalFileStorage(
    Microsoft.Extensions.Options.IOptions<StorageOptions> options,
    IHostEnvironment environment,
    ILogger<LocalFileStorage> logger) : IFileStorage
{
    private readonly StorageOptions _options = options.Value;

    private static readonly HashSet<string> AllowedImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif", "image/avif"
    };

    public async Task<StoredFile> SaveAsync(
        Stream content,
        string fileName,
        string contentType,
        string? folder = null,
        CancellationToken ct = default)
    {
        if (content.Length > _options.MaxFileSizeBytes)
        {
            throw new InvalidOperationException(
                $"File exceeds the maximum upload size of {_options.MaxFileSizeBytes / 1024 / 1024} MB.");
        }

        var root = Path.Combine(environment.ContentRootPath, _options.LocalRootPath);
        var relativeFolder = SanitiseFolder(folder);
        var targetDirectory = Path.Combine(root, relativeFolder);

        Directory.CreateDirectory(targetDirectory);

        // The stored name never reuses the client's, which could contain path separators, a
        // double extension, or a name that collides with an existing file.
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var safeStem = $"{DateTime.UtcNow:yyyyMMdd}-{Guid.CreateVersion7():N}";

        if (!AllowedImageTypes.Contains(contentType))
        {
            return await SaveRawAsync(content, targetDirectory, relativeFolder, safeStem, extension, ct);
        }

        return await SaveImageAsync(content, targetDirectory, relativeFolder, safeStem, ct);
    }

    private async Task<StoredFile> SaveRawAsync(
        Stream content,
        string targetDirectory,
        string relativeFolder,
        string stem,
        string extension,
        CancellationToken ct)
    {
        var name = stem + extension;
        var path = Path.Combine(targetDirectory, name);

        await using (var file = File.Create(path))
        {
            content.Position = 0;
            await content.CopyToAsync(file, ct);
        }

        return new StoredFile(
            Url: PublicUrl(relativeFolder, name),
            ThumbnailUrl: null,
            WebpUrl: null,
            AvifUrl: null,
            FileName: name,
            SizeBytes: new FileInfo(path).Length,
            Width: 0,
            Height: 0,
            BlurHash: null);
    }

    private async Task<StoredFile> SaveImageAsync(
        Stream content,
        string targetDirectory,
        string relativeFolder,
        string stem,
        CancellationToken ct)
    {
        content.Position = 0;

        using var image = await Image.LoadAsync<Rgba32>(content, ct);

        // Strips EXIF along with everything else in the metadata block. Phone photos routinely
        // carry GPS coordinates, which must not be republished on a public product page.
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;

        var originalWidth = image.Width;
        var originalHeight = image.Height;

        if (image.Width > _options.MaxWidth)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(_options.MaxWidth, 0),
                Mode = ResizeMode.Max
            }));
        }

        // WebP is the primary format: broadly supported and materially smaller than JPEG at
        // equivalent quality.
        var mainName = $"{stem}.webp";
        var mainPath = Path.Combine(targetDirectory, mainName);
        await image.SaveAsWebpAsync(mainPath, new WebpEncoder { Quality = 82 }, ct);

        // Grid cards request this, so a listing of 24 products never downloads 24 full-size images.
        var thumbName = $"{stem}-thumb.webp";
        var thumbPath = Path.Combine(targetDirectory, thumbName);

        using (var thumbnail = image.Clone(x => x.Resize(new ResizeOptions
               {
                   Size = new Size(_options.ThumbnailWidth, 0),
                   Mode = ResizeMode.Max
               })))
        {
            await thumbnail.SaveAsWebpAsync(thumbPath, new WebpEncoder { Quality = 78 }, ct);
        }

        var blurHash = await CreateBlurPlaceholderAsync(image, ct);

        logger.LogInformation(
            "Stored image {Name} ({Width}x{Height}, {Size} bytes)",
            mainName, image.Width, image.Height, new FileInfo(mainPath).Length);

        return new StoredFile(
            Url: PublicUrl(relativeFolder, mainName),
            ThumbnailUrl: PublicUrl(relativeFolder, thumbName),
            WebpUrl: PublicUrl(relativeFolder, mainName),
            AvifUrl: null,
            FileName: mainName,
            SizeBytes: new FileInfo(mainPath).Length,

            // The *displayed* dimensions, so the markup reserves the right box.
            Width: image.Width,
            Height: image.Height,
            BlurHash: blurHash);
    }

    /// <summary>
    /// Builds a ~20px-wide base64 WebP shown behind the real image while it loads.
    /// </summary>
    /// <remarks>
    /// Small enough to inline in the HTML/JSON without meaningfully adding to the payload, which
    /// means the image slot is never blank and never a grey rectangle that shifts on load.
    /// </remarks>
    private static async Task<string?> CreateBlurPlaceholderAsync(Image<Rgba32> source, CancellationToken ct)
    {
        try
        {
            using var tiny = source.Clone(x => x.Resize(new ResizeOptions
            {
                Size = new Size(20, 0),
                Mode = ResizeMode.Max
            }));

            using var buffer = new MemoryStream();
            await tiny.SaveAsWebpAsync(buffer, new WebpEncoder { Quality = 40 }, ct);

            return $"data:image/webp;base64,{Convert.ToBase64String(buffer.ToArray())}";
        }
        catch
        {
            // A missing placeholder degrades the loading experience slightly; it must never fail
            // the upload itself.
            return null;
        }
    }

    public Task DeleteAsync(string url, CancellationToken ct = default)
    {
        if (!url.StartsWith(_options.PublicBaseUrl, StringComparison.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }

        var relative = url[_options.PublicBaseUrl.Length..].TrimStart('/', '\\');
        var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, _options.LocalRootPath));
        var path = Path.GetFullPath(Path.Combine(root, relative));

        // Path traversal guard. Without it, a crafted URL containing "../" could delete files
        // anywhere the process can write.
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Refused to delete {Url}: resolved outside the upload root", url);
            return Task.CompletedTask;
        }

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        // Remove the generated thumbnail alongside its parent.
        var thumb = Path.Combine(
            Path.GetDirectoryName(path)!,
            $"{Path.GetFileNameWithoutExtension(path)}-thumb.webp");

        if (File.Exists(thumb))
        {
            File.Delete(thumb);
        }

        return Task.CompletedTask;
    }

    private string PublicUrl(string folder, string fileName) =>
        string.IsNullOrEmpty(folder)
            ? $"{_options.PublicBaseUrl}/{fileName}"
            : $"{_options.PublicBaseUrl}/{folder.Replace('\\', '/')}/{fileName}";

    /// <summary>Reduces a caller-supplied folder to a single safe path segment.</summary>
    private static string SanitiseFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return string.Empty;
        }

        var cleaned = new string(folder
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .ToArray());

        return cleaned.Length > 50 ? cleaned[..50] : cleaned;
    }
}

public static class StorageRegistration
{
    public static IServiceCollection AddFileStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.AddSingleton<IFileStorage, LocalFileStorage>();

        return services;
    }
}
