namespace Moka.Network.Whois;

/// <summary>What an RDAP server sent: its HTTP status and the body, which is JSON or whatever the server sent instead.</summary>
public sealed record RdapReply(int Status, string Json);
