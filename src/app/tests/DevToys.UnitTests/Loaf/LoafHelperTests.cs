using DevToys.Api;
using DevToys.Loaf.Helpers;
using DevToys.Loaf.SmartDetection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OneOf;
using Xunit;

namespace DevToys.UnitTests.Loaf;

public class LoafHelperTests
{
    private readonly Mock<ILogger> _logger;
    private readonly Mock<IFileStorage> _fileStorage;

    public LoafHelperTests()
    {
        _logger = new Mock<ILogger>();
        _fileStorage = new Mock<IFileStorage>();
    }

    [Fact]
    public async Task CreateLoafAsync_SimpleText_ReturnsValidLoaf()
    {
        // Arrange
        string input = "Hello, World!";
        
        // Act
        var result = await LoafHelper.CreateLoafAsync(
            OneOf<FileInfo, string>.FromT1(input),
            _fileStorage.Object,
            _logger.Object,
            CancellationToken.None);

        // Assert
        result.HasSucceeded.Should().BeTrue();
        result.Data.Should().NotBeNullOrWhiteSpace();
        result.Data.Should().StartWith("SHA256(-)=");
        result.Data.Should().Contain(" ");
    }

    [Fact]
    public async Task VerifyLoafAsync_ValidLoaf_ReturnsTrue()
    {
        // Arrange
        string input = "test content";
        var createResult = await LoafHelper.CreateLoafAsync(
            OneOf<FileInfo, string>.FromT1(input),
            _fileStorage.Object,
            _logger.Object,
            CancellationToken.None);

        // Act
        var verifyResult = await LoafHelper.VerifyLoafAsync(
            createResult.Data!,
            _logger.Object,
            CancellationToken.None);

        // Assert
        verifyResult.HasSucceeded.Should().BeTrue();
        verifyResult.Data.Should().BeTrue();
    }

    [Fact]
    public async Task VerifyLoafAsync_InvalidFormat_ReturnsFalse()
    {
        // Arrange
        string invalidLoaf = "not a valid loaf format";

        // Act
        var result = await LoafHelper.VerifyLoafAsync(
            invalidLoaf,
            _logger.Object,
            CancellationToken.None);

        // Assert
        result.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task VerifyLoafAsync_CorruptedHash_ReturnsFalse()
    {
        // Arrange
        string input = "test content";
        var createResult = await LoafHelper.CreateLoafAsync(
            OneOf<FileInfo, string>.FromT1(input),
            _fileStorage.Object,
            _logger.Object,
            CancellationToken.None);

        // Corrupt the hash by changing one character
        string corruptedLoaf = createResult.Data!.Replace("SHA256(-)=", "SHA256(-)=0");

        // Act
        var verifyResult = await LoafHelper.VerifyLoafAsync(
            corruptedLoaf,
            _logger.Object,
            CancellationToken.None);

        // Assert
        verifyResult.HasSucceeded.Should().BeTrue();
        verifyResult.Data.Should().BeFalse();
    }

    [Fact]
    public async Task ExtractLoafAsync_ValidLoaf_ReturnsOriginalContent()
    {
        // Arrange
        string originalContent = "Hello, LoaF Archive!";
        var createResult = await LoafHelper.CreateLoafAsync(
            OneOf<FileInfo, string>.FromT1(originalContent),
            _fileStorage.Object,
            _logger.Object,
            CancellationToken.None);

        // Act
        var extractResult = await LoafHelper.ExtractLoafAsync(
            createResult.Data!,
            _logger.Object,
            CancellationToken.None);

        // Assert
        extractResult.HasSucceeded.Should().BeTrue();
        extractResult.Data.Should().HaveCount(1);
        
        var extractedFile = extractResult.Data![0];
        extractedFile.Name.Should().Be("content");
        
        string extractedContent = System.Text.Encoding.UTF8.GetString(extractedFile.Data);
        extractedContent.Should().Be(originalContent);
    }

    [Fact]
    public async Task ExtractLoafAsync_InvalidFormat_ReturnsEmpty()
    {
        // Arrange
        string invalidLoaf = "invalid format";

        // Act
        var result = await LoafHelper.ExtractLoafAsync(
            invalidLoaf,
            _logger.Object,
            CancellationToken.None);

        // Assert
        result.HasSucceeded.Should().BeFalse();
        result.Data.Should().BeEmpty();
    }
}

public class LoafDataTypeDetectorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("not a loaf", false)]
    [InlineData("SHA256(-)=", false)]
    [InlineData("SHA256(-)=abc123", false)]
    [InlineData("SHA256(-)=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef 48656c6c6f", true)]
    [InlineData("  SHA256(-)=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef 48656c6c6f  ", true)]
    public async Task TryDetectDataAsync_VariousInputs_ReturnsExpectedResult(string? input, bool expectedSuccess)
    {
        // Arrange
        var detector = new LoafDataTypeDetector();
        var baseResult = new DataDetectionResult(true, input);

        // Act
        var result = await detector.TryDetectDataAsync(input!, baseResult, CancellationToken.None);

        // Assert
        result.Success.Should().Be(expectedSuccess);
        if (expectedSuccess)
        {
            result.Data.Should().Be(input);
        }
    }
}