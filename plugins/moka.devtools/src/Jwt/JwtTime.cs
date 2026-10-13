namespace Moka.DevTools.Jwt;

/// <summary>A time claim (exp, nbf, iat, auth_time) read as a date, or why it could not be.</summary>
public sealed record JwtTime(string Claim, DateTimeOffset? When, string? Problem);
