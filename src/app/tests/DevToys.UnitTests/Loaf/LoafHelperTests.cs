#nullable enable
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        extractedFile.Name.Should().Be("-");
        
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
    [InlineData("SHA256(-)=fe6a4de332cfce54fa1dd34ba0028fa512a0984d0cbbed837b6869d67911ac8a 1f8b0800000000000203edcec90902411404d07f368a4e40f83d4b770aa6e1c1dbc0804bfe3a630c0ae27b97a2a00e758ccfcb9796b965ed73dd7b6d6dcfb71675cea1d76918fbb66b398d51f20bdfe271bb9fafa5c49f3a5d96653d040000000000000000003fe609dff765e300280000", true)]
    [InlineData("  SHA256(-)=fe6a4de332cfce54fa1dd34ba0028fa512a0984d0cbbed837b6869d67911ac8a 1f8b0800000000000203edcec90902411404d07f368a4e40f83d4b770aa6e1c1dbc0804bfe3a630c0ae27b97a2a00e758ccfcb9796b965ed73dd7b6d6dcfb71675cea1d76918fbb66b398d51f20bdfe271bb9fafa5c49f3a5d96653d040000000000000000003fe609dff765e300280000  ", true)]
    [InlineData("SHA256(-)=f110b250881bad709595042f29ef203ac09450539b91a8de6428599956c0a5ec 1f8b0800000000000203edcebb0980401084e18dade21a1076f51ef588982ae8dabfdef5a020fe5f320c4c30bd3c4f6f59b5a69564ad5bce2d1b4b62498762711c2c4a5d4793a02f7c93f3f0690f417eca97c3c3bcadbeacde0900000000000000000000000000e02b2e8152fde700280000", true)]
    [InlineData("SHA256(-)=4004dc86e3e4a71fef912b6dfded0057b68681f01ba7842cc23712555c24d96a 1f8b0800000000000203edcec90dc2401004c07913c526803463afd7f1900247fe3e88012444d5a7d5523ffa1a9f97bb917964ad4b9dbdc638f3ada2969cd6eaf3543d8ef5dca3e517bec5ebf1bcdd5b8b3f75090000000000000000007ed106a7b9fe0e00280000", true)]
    [InlineData("SHA256(-)=a053e4f7cd30b05d7934d26e34d5b3cf9bc5d74f6753505fb64b34b242b8d6c4 1f8b0800000000000203edcec10dc2301004c07b53851b40f205dba987162094410bd0222540420d2021663eab95f6b1fbf8bcfa326a5d33e79e5bcf31b67ceb91bd4e73b6c3942dd675ab51ea17bec5e5bc1c4fa5c49f7adcafb75d000000000000000000f06b9ec5ccb1d200280000", true)]
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