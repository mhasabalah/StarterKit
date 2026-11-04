using Attachment.Contracts.Attachments.Dtos;
using Attachment.Infrastructure.Configuration;
using Attachment.Infrastructure.Validation;
using Attachment.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Attachment.Tests.Unit;

public sealed class FileValidationServiceTests
{
    private static FileValidationService Create(AttachmentOptions? options = null, IMalwareScanner? scanner = null)
    {
        var wrapped = TestOptions.Wrap(options ?? TestOptions.Create());
        return new(wrapped, scanner ?? new NoOpMalwareScanner(),
            new Attachment.Infrastructure.ImageProcessing.ImageProcessingService(
                wrapped, NullLogger<Attachment.Infrastructure.ImageProcessing.ImageProcessingService>.Instance),
            NullLogger<FileValidationService>.Instance);
    }

    private static Task<FileValidationResult> Validate(
        byte[] content, string fileName, string declared, AttachmentType type = AttachmentType.Other,
        AttachmentOptions? options = null, IMalwareScanner? scanner = null) =>
        Create(options, scanner).ValidateFileAsync(new MemoryStream(content), fileName, declared, content.Length, type);

    [Fact]
    public async Task Valid_png_is_accepted_and_type_is_server_determined()
    {
        var result = await Validate(TestFiles.Png(40, 30), "photo.PNG", "image/png");

        result.IsValid.Should().BeTrue(string.Join(";", result.Errors));
        result.Detected!.Extension.Should().Be(".png");
        result.Detected.ContentType.Should().Be("image/png");
        result.Detected.Type.Should().Be(AttachmentType.Image);
        result.Detected.Width.Should().Be(40);
        result.Detected.Height.Should().Be(30);
    }

    [Fact]
    public async Task Octet_stream_declaration_is_accepted_and_real_type_detected()
    {
        var result = await Validate(TestFiles.Pdf(), "invoice.pdf", "application/octet-stream");

        result.IsValid.Should().BeTrue(string.Join(";", result.Errors));
        result.Detected!.ContentType.Should().Be("application/pdf");
        result.Detected.Type.Should().Be(AttachmentType.Document);
    }

    [Fact]
    public async Task Spoofed_content_type_is_rejected()
    {
        // Real PNG bytes declared as HTML.
        var result = await Validate(TestFiles.Png(), "photo.png", "text/html");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("does not match"));
    }

    [Fact]
    public async Task Html_content_renamed_to_png_is_rejected_by_magic_bytes()
    {
        var html = TestFiles.Text("<html><script>alert(1)</script></html>");

        var result = await Validate(html, "innocent.png", "image/png");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Contains("does not match"));
    }

    [Fact]
    public async Task Pdf_extension_with_png_content_is_rejected()
    {
        var result = await Validate(TestFiles.Png(), "doc.pdf", "application/pdf");

        result.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("page.html", "text/html")]
    [InlineData("page.htm", "text/html")]
    [InlineData("logo.svg", "image/svg+xml")]
    [InlineData("app.js", "application/javascript")]
    [InlineData("setup.exe", "application/octet-stream")]
    [InlineData("macro.docm", "application/vnd.ms-word.document.macroEnabled.12")]
    public async Task Dangerous_extensions_are_blocked(string fileName, string declared)
    {
        var result = await Validate(TestFiles.Text("<svg onload=alert(1)>"), fileName, declared);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("security"));
    }

    [Fact]
    public async Task Svg_stays_blocked_even_when_listed_in_AllowedExtensions()
    {
        var options = TestOptions.Create(o =>
            o.Validation.AllowedExtensions = [.. o.Validation.AllowedExtensions, ".svg", ".html"]);

        var result = await Validate(TestFiles.Text("<svg/>"), "x.svg", "image/svg+xml", options: options);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Extension_outside_the_allowlist_is_rejected()
    {
        var options = TestOptions.Create(o => o.Validation.AllowedExtensions = [".png"]);

        var result = await Validate(TestFiles.Pdf(), "doc.pdf", "application/pdf", options: options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("not allowed"));
    }

    [Fact]
    public async Task Allowed_extension_without_signature_rule_is_rejected()
    {
        var options = TestOptions.Create(o => o.Validation.AllowedExtensions = [".quicktime"]);

        var result = await Validate(TestFiles.Pdf(), "clip.quicktime", "video/quicktime", options: options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("no content verification rule"));
    }

    [Fact]
    public async Task Requested_type_must_match_detected_type()
    {
        var result = await Validate(TestFiles.Png(), "photo.png", "image/png", AttachmentType.Document);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Oversized_file_is_rejected_using_the_real_length()
    {
        var options = TestOptions.Create(o => o.Validation.MaxImageSizeBytes = 100);
        var png = TestFiles.Png(64, 64);

        // Client lies about the size; the stream length is what counts.
        var result = await Create(options).ValidateFileAsync(
            new MemoryStream(png), "big.png", "image/png", declaredFileSize: 10, AttachmentType.Image);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("maximum allowed size"));
    }

    [Fact]
    public async Task Megapixel_bomb_is_rejected_before_decoding()
    {
        var bomb = TestFiles.PngBombHeader(50_000, 50_000); // 2.5 gigapixels, ~100 bytes on disk

        var result = await Validate(bomb, "bomb.png", "image/png");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("megapixels"));
    }

    [Fact]
    public async Task Image_within_megapixel_limit_passes()
    {
        var options = TestOptions.Create(o => o.Validation.MaxImageMegapixels = 0.01); // 10,000 px
        var result = await Validate(TestFiles.Png(100, 100), "ok.png", "image/png", options: options);

        result.IsValid.Should().BeTrue(string.Join(";", result.Errors));
    }

    [Fact]
    public async Task Text_file_with_script_markup_is_rejected()
    {
        var result = await Validate(TestFiles.Text("hello\n<script>alert(1)</script>"), "notes.txt", "text/plain");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Binary_content_with_txt_extension_is_rejected()
    {
        var result = await Validate(TestFiles.Png(), "notes.txt", "text/plain");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Valid_json_and_csv_are_accepted()
    {
        (await Validate(TestFiles.Text("{\"a\":1}"), "data.json", "application/json")).IsValid.Should().BeTrue();
        (await Validate(TestFiles.Text("a,b\n1,2\n"), "data.csv", "text/csv")).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Xml_with_doctype_is_rejected()
    {
        var xml = TestFiles.Text("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///etc/passwd\">]><x>&e;</x>");

        var result = await Validate(xml, "data.xml", "application/xml");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task File_name_path_parts_are_stripped()
    {
        var result = await Validate(TestFiles.Png(), "..\\..\\etc/evil\u0001.png", "image/png");

        result.IsValid.Should().BeTrue(string.Join(";", result.Errors));
        result.SafeFileName.Should().Be("evil.png");
    }

    [Fact]
    public async Task Stream_is_rewound_after_validation()
    {
        var stream = new MemoryStream(TestFiles.Png());
        await Create().ValidateFileAsync(stream, "a.png", "image/png", stream.Length, AttachmentType.Image);

        stream.Position.Should().Be(0);
    }

    [Fact]
    public async Task Malware_scanner_rejection_fails_validation_when_scanning_enabled()
    {
        var scanner = new Mock<IMalwareScanner>();
        scanner.Setup(s => s.ScanAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MalwareScanResult(false, "EICAR"));
        var options = TestOptions.Create(o => o.Validation.EnableVirusScanning = true);

        var result = await Validate(TestFiles.Pdf(), "doc.pdf", "application/pdf", options: options, scanner: scanner.Object);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("malware"));
    }

    [Fact]
    public void Startup_check_fails_when_scanning_enabled_without_a_real_scanner()
    {
        var options = TestOptions.Create(o => o.Validation.EnableVirusScanning = true);
        var provider = new ServiceCollection()
            .AddSingleton<IMalwareScanner, NoOpMalwareScanner>()
            .BuildServiceProvider();

        var act = () => MalwareScannerStartupCheck.Validate(options, provider);

        act.Should().Throw<InvalidOperationException>().WithMessage("*EnableVirusScanning*");
    }

    [Fact]
    public void Startup_check_passes_with_a_real_scanner_or_scanning_disabled()
    {
        var real = new ServiceCollection()
            .AddSingleton<IMalwareScanner, NoOpMalwareScanner>()
            .AddSingleton(new Mock<IMalwareScanner>().Object) // registered later = resolved
            .BuildServiceProvider();

        var enabled = TestOptions.Create(o => o.Validation.EnableVirusScanning = true);
        var disabled = TestOptions.Create();
        var noOpOnly = new ServiceCollection().AddSingleton<IMalwareScanner, NoOpMalwareScanner>().BuildServiceProvider();

        FluentActions.Invoking(() => MalwareScannerStartupCheck.Validate(enabled, real)).Should().NotThrow();
        FluentActions.Invoking(() => MalwareScannerStartupCheck.Validate(disabled, noOpOnly)).Should().NotThrow();
    }
}
