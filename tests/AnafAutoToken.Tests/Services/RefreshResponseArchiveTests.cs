using AnafAutoToken.Core.Services;
using AnafAutoToken.Shared.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace AnafAutoToken.Tests.Services;

[Collection(DataDirectoryCollection.Name)]
public class RefreshResponseArchiveTests : IDisposable
{
    private const string RawResponse = """{"access_token":"aaa","refresh_token":"bbb","expires_in":7776000}""";

    private readonly RefreshResponseArchive _archive = new(Mock.Of<ILogger<RefreshResponseArchive>>());

    public RefreshResponseArchiveTests() => Cleanup();

    [Fact]
    public async Task SaveAsync_WritesTheResponseVerbatimUnderATimestampedName()
    {
        var refreshedAt = new DateTime(2026, 8, 24, 14, 5, 9, DateTimeKind.Local);

        var path = await _archive.SaveAsync(RawResponse, refreshedAt);

        path.Should().NotBeNull();
        Path.GetFileName(path).Should().Be("refresh_response_2026-08-24_14-05-09.json");
        Path.GetDirectoryName(path).Should().Be(AppPaths.DataDirectory);

        // Archiwum ma być wiernym śladem odpowiedzi - bez reformatowania.
        (await File.ReadAllTextAsync(path!)).Should().Be(RawResponse);
    }

    [Fact]
    public async Task SaveAsync_ConvertsUtcTimestampToLocalTime()
    {
        var utc = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

        var path = await _archive.SaveAsync(RawResponse, utc);

        Path.GetFileName(path).Should().Be($"refresh_response_{utc.ToLocalTime():yyyy-MM-dd_HH-mm-ss}.json");
    }

    [Fact]
    public async Task SaveAsync_NeverOverwritesAnEarlierArchiveFromTheSameSecond()
    {
        var refreshedAt = new DateTime(2026, 8, 24, 14, 5, 9, DateTimeKind.Local);

        var first = await _archive.SaveAsync("""{"first":true}""", refreshedAt);
        var second = await _archive.SaveAsync("""{"second":true}""", refreshedAt);

        second.Should().NotBe(first);
        (await File.ReadAllTextAsync(first!)).Should().Be("""{"first":true}""");
        (await File.ReadAllTextAsync(second!)).Should().Be("""{"second":true}""");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_WithoutAResponseBody_WritesNothing(string? rawResponse)
    {
        var path = await _archive.SaveAsync(rawResponse, DateTime.Now);

        path.Should().BeNull();

        if (Directory.Exists(AppPaths.DataDirectory))
        {
            Directory.GetFiles(AppPaths.DataDirectory, "refresh_response_*.json").Should().BeEmpty();
        }
    }

    [Fact]
    public async Task SaveAsync_CreatesTheDataDirectoryWhenItIsMissing()
    {
        Directory.Exists(AppPaths.DataDirectory).Should().BeFalse();

        var path = await _archive.SaveAsync(RawResponse, DateTime.Now);

        File.Exists(path).Should().BeTrue();
    }

    public void Dispose() => Cleanup();

    private static void Cleanup()
    {
        if (Directory.Exists(TestDataDirectory.Path))
        {
            Directory.Delete(TestDataDirectory.Path, recursive: true);
        }
    }
}
