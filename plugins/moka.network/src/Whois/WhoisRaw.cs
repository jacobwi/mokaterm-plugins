namespace Moka.Network.Whois;

/// <summary>What a whois server sent, as bytes, and whether the read stopped before the server closed.</summary>
public sealed record WhoisRaw(byte[] Bytes, bool Cut, bool TimedOut);
