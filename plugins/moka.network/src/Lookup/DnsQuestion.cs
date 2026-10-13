namespace Moka.Network.Lookup;

/// <summary>A question: a name in presentation form, a type and a class, all as numbers so an unknown one still reads.</summary>
public sealed record DnsQuestion(string Name, int Type, int Class);
