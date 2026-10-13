namespace Moka.Certificates.ServerFiles;

/// <summary>A certificate file to read, with everything that uses it.</summary>
public sealed record ScanCandidate(string Path, IReadOnlyList<CertificateUse> Uses, CertbotCertificate? Lineage);
