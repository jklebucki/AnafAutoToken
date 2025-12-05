namespace AnafAutoToken.Core.Models;

public class RefreshTokenRequest
{
    public required string RefreshToken { get; init; }
    public string GrantType { get; init; } = "refresh_token";
}
