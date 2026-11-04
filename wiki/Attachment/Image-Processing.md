# Image processing (thumbnails and image validation)

The Attachment module reads image headers (upload validation) and creates JPEG thumbnails. Both go through one
port, so the imaging library is a swappable detail.

```
FileValidationService ─┐
ThumbnailJob ──────────┼──> IImageProcessingService  (port: Infrastructure/ImageProcessing/IImageProcessingService.cs)
                       │          │
                       │          └── SkiaImageProcessingService   (default adapter: SkiaSharp, MIT)
```

## The contract

| Method | Must do |
|---|---|
| `IdentifyAsync(stream)` | Read the **header only**, never the pixels: a 50,000 x 50,000 "decompression bomb" is measured, not decoded. Return MIME type + size, or `null` for anything that isn't an image it understands. **Never dispose the caller's stream**, because validation rewinds it and re-reads it for the malware scan. |
| `GenerateThumbnailAsync(stream)` | Return a JPEG that fits inside `Attachment:Image:ThumbnailWidth` x `ThumbnailHeight` with the aspect ratio kept. It must: use the first frame only (animated GIFs); be upright (apply the EXIF orientation when `EnableOptimization`); carry no metadata (no EXIF/GPS); flatten transparency onto **white**; work on non-seekable streams. |

Accepted upload formats are JPEG, PNG, GIF, BMP and WebP (`FileSignatureCatalog`). An adapter must identify all five.

`Attachment.Tests/Unit/ImageProcessingContractTests` is the executable form of this table, down to pixel checks for
EXIF rotations 1, 3, 6 and 8. **Every adapter must pass it unchanged.**

## The default: SkiaSharp

`SkiaImageProcessingService` is the only production file that references SkiaSharp (`ThirdPartyIsolationTests`).
- **License:** MIT. No key and no revenue limit, so CI and `docker build` need no secret.
- **Efficiency:** JPEGs are decoded directly at 1/2, 1/4 or 1/8 size when the thumbnail is much smaller, so a 24 MP
  photo never gets fully decoded.
- **Native library:** `SkiaSharp.NativeAssets.Linux.NoDependencies` ships `libSkiaSharp.so`, which needs only
  glibc/libstdc++. Those are present in the `aspnet` image; verified with `ldd`. The Dockerfile publishes for the
  image's architecture only (`TARGETARCH` → `linux-x64` / `linux-arm64`); the other ~20 platforms' native files would
  add ~190 MB.
- **Security:** Skia parses untrusted uploads. Keep SkiaSharp on current patch releases (Dependabot groups them
  weekly), and keep the megapixel cap (`Attachment:Validation:MaxImageMegapixels`) in place. That cap is checked
  before any decode.

## Switching to another library

Use this for a licensed ImageSharp, Magick.NET, libvips/NetVips, or a cloud image service:

1. Add the package (version in `Directory.Packages.props`) to `Attachment.csproj` only.
2. Add `Infrastructure/ImageProcessing/<Name>ImageProcessingService.cs` implementing `IImageProcessingService`.
3. Change the one registration line in `AttachmentModule`:
   `services.AddScoped<IImageProcessingService, <Name>ImageProcessingService>();`
4. Add `public sealed class <Name>ImageProcessingContractTests : ImageProcessingContractTests` returning your adapter,
   and make it pass.
5. Update `ThirdPartyIsolationTests` (the library check) and [Third-Party-Licenses](../Reference/Third-Party-Licenses.md).
   Then remove both SkiaSharp packages. Nothing else depends on them: QuestPDF ships its own Skia build
   (`libQuestPdfSkia.so`).

### Example: ImageSharp with a license

ImageSharp 3.2+ checks a license key **at build time** (Release). Once you have one (see
[licensing.sixlabors.com](https://licensing.sixlabors.com/)), also:
- add the repo secret `SIXLABORS_LICENSE_KEY` (the full contents of `sixlabors.lic`), and pass
  `SixLaborsLicenseKey: ${{ secrets.SIXLABORS_LICENSE_KEY }}` as an env var to the CI build step;
- in the Dockerfile, mount it as a BuildKit secret and pass `-p:SixLaborsLicenseKey=...` to `dotnet build`/`publish`.
  Never `COPY` a `.lic` file; `.gitignore` and `.dockerignore` already exclude it;
- change `Nothing_references_ImageSharp` in `ThirdPartyIsolationTests` into "only Attachment references it".

The adapter itself:

```csharp
public sealed class ImageSharpImageProcessingService(IOptions<AttachmentOptions> options) : IImageProcessingService
{
    public async Task<ImageIdentity?> IdentifyAsync(Stream stream, CancellationToken ct = default)
    {
        try
        {
            var info = await Image.IdentifyAsync(stream, ct);
            return new ImageIdentity(info.Metadata.DecodedImageFormat?.DefaultMimeType, info.Width, info.Height);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task<ThumbnailResult> GenerateThumbnailAsync(Stream stream, CancellationToken ct = default)
    {
        var o = options.Value.Image;
        var box = new Size(o.ThumbnailWidth, o.ThumbnailHeight);
        using var image = await Image.LoadAsync<Rgba32>(new DecoderOptions { TargetSize = box, MaxFrames = 1 }, stream, ct);
        if (o.EnableOptimization) image.Mutate(x => x.AutoOrient());
        image.Mutate(x => x.Resize(new ResizeOptions { Size = box, Mode = ResizeMode.Max }).BackgroundColor(Color.White));
        image.Metadata.ExifProfile = null;
        var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = o.ThumbnailQuality }, ct);
        output.Position = 0;
        return new ThumbnailResult(output, image.Width, image.Height);
    }
}
```
