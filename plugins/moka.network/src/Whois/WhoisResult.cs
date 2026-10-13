namespace Moka.Network.Whois;

/// <summary>Every answer in the order asked, and a note when a referral was left unfollowed.</summary>
public sealed record WhoisResult(IReadOnlyList<WhoisAnswer> Answers, string? Note);
