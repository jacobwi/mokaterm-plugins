namespace Moka.Certificates.ServerFiles;

/// <summary>A certificate file a web server's configuration names, with where and for which names.</summary>
/// <param name="Path">The file, absolute.</param>
/// <param name="Server">"nginx" or "Apache".</param>
/// <param name="ConfigFile">The configuration file that names it.</param>
/// <param name="ServerNames">The server names of the block it is in; empty when it is set outside any.</param>
/// <param name="Directive">The directive, such as ssl_certificate or SSLCertificateChainFile.</param>
public sealed record ConfiguredCertificate(string Path, string Server, string ConfigFile, IReadOnlyList<string> ServerNames, string Directive);
