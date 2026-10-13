namespace Moka.Certificates.ServerFiles;

/// <summary>Where a certificate file is used: by which program, and the detail that says how.</summary>
/// <param name="Source">"nginx", "Apache", "certbot" or "Settings".</param>
/// <param name="Detail">The configuration file and server names, the lineage, or the configured path.</param>
public sealed record CertificateUse(string Source, string Detail);
