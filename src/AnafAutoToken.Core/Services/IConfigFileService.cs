namespace AnafAutoToken.Core.Services;

public interface IConfigFileService
{
    Task<string> ReadAccessTokenAsync(CancellationToken cancellationToken = default);
    Task UpdateAccessTokenAsync(string newAccessToken, CancellationToken cancellationToken = default);
    Task CreateBackupAsync(CancellationToken cancellationToken = default);
}
