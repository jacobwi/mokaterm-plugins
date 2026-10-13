using System.Collections.Frozen;

namespace Moka.Certificates.Crypto;

/// <summary>The object identifiers this plugin reads and writes, and the plain names it shows for them.</summary>
internal static class OidNames
{
	public const string RsaKey = "1.2.840.113549.1.1.1";
	public const string RsaPssKey = "1.2.840.113549.1.1.10";
	public const string EcKey = "1.2.840.10045.2.1";
	public const string DsaKey = "1.2.840.10040.4.1";
	public const string Ed25519 = "1.3.101.112";
	public const string Ed448 = "1.3.101.113";
	public const string X25519 = "1.3.101.110";
	public const string X448 = "1.3.101.111";

	public const string P256 = "1.2.840.10045.3.1.7";
	public const string P384 = "1.3.132.0.34";
	public const string P521 = "1.3.132.0.35";

	public const string Sha1 = "1.3.14.3.2.26";
	public const string Sha256 = "2.16.840.1.101.3.4.2.1";
	public const string Sha384 = "2.16.840.1.101.3.4.2.2";
	public const string Sha512 = "2.16.840.1.101.3.4.2.3";
	public const string Mgf1 = "1.2.840.113549.1.1.8";

	public const string RsaSha1 = "1.2.840.113549.1.1.5";
	public const string RsaSha256 = "1.2.840.113549.1.1.11";
	public const string RsaSha384 = "1.2.840.113549.1.1.12";
	public const string RsaSha512 = "1.2.840.113549.1.1.13";
	public const string RsaPss = "1.2.840.113549.1.1.10";
	public const string EcdsaSha1 = "1.2.840.10045.4.1";
	public const string EcdsaSha256 = "1.2.840.10045.4.3.2";
	public const string EcdsaSha384 = "1.2.840.10045.4.3.3";
	public const string EcdsaSha512 = "1.2.840.10045.4.3.4";

	public const string SubjectKeyIdentifier = "2.5.29.14";
	public const string KeyUsage = "2.5.29.15";
	public const string SubjectAltName = "2.5.29.17";
	public const string IssuerAltName = "2.5.29.18";
	public const string BasicConstraints = "2.5.29.19";
	public const string NameConstraints = "2.5.29.30";
	public const string CrlDistributionPoints = "2.5.29.31";
	public const string CertificatePolicies = "2.5.29.32";
	public const string AuthorityKeyIdentifier = "2.5.29.35";
	public const string ExtendedKeyUsage = "2.5.29.37";
	public const string AuthorityInfoAccess = "1.3.6.1.5.5.7.1.1";
	public const string TlsFeature = "1.3.6.1.5.5.7.1.24";
	public const string SignedCertificateTimestamps = "1.3.6.1.4.1.11129.2.4.2";
	public const string PrecertificatePoison = "1.3.6.1.4.1.11129.2.4.3";

	public const string Ocsp = "1.3.6.1.5.5.7.48.1";
	public const string CaIssuers = "1.3.6.1.5.5.7.48.2";
	public const string CpsQualifier = "1.3.6.1.5.5.7.2.1";
	public const string UserNoticeQualifier = "1.3.6.1.5.5.7.2.2";

	public const string ServerAuth = "1.3.6.1.5.5.7.3.1";
	public const string ClientAuth = "1.3.6.1.5.5.7.3.2";
	public const string CodeSigning = "1.3.6.1.5.5.7.3.3";
	public const string EmailProtection = "1.3.6.1.5.5.7.3.4";

	public const string Pbes2 = "1.2.840.113549.1.5.13";
	public const string Pbkdf2 = "1.2.840.113549.1.5.12";
	public const string Aes256Cbc = "2.16.840.1.101.3.4.1.42";
	public const string SignedData = "1.2.840.113549.1.7.2";
	public const string FriendlyName = "1.2.840.113549.1.9.20";
	public const string UserPrincipalName = "1.3.6.1.4.1.311.20.2.3";

	public static readonly FrozenDictionary<string, string> Extensions = new Dictionary<string, string>
	{
		[SubjectKeyIdentifier] = "Subject key identifier",
		[KeyUsage] = "Key usage",
		[SubjectAltName] = "Subject alternative names",
		[IssuerAltName] = "Issuer alternative names",
		[BasicConstraints] = "Basic constraints",
		[NameConstraints] = "Name constraints",
		[CrlDistributionPoints] = "CRL distribution points",
		[CertificatePolicies] = "Certificate policies",
		["2.5.29.33"] = "Policy mappings",
		["2.5.29.36"] = "Policy constraints",
		["2.5.29.54"] = "Inhibit any policy",
		[AuthorityKeyIdentifier] = "Authority key identifier",
		[ExtendedKeyUsage] = "Extended key usage",
		[AuthorityInfoAccess] = "Authority information access",
		["1.3.6.1.5.5.7.1.11"] = "Subject information access",
		[TlsFeature] = "TLS feature",
		[SignedCertificateTimestamps] = "Signed certificate timestamps",
		[PrecertificatePoison] = "Precertificate poison",
		["1.3.6.1.5.5.7.48.1.5"] = "OCSP no check",
		["2.16.840.1.113730.1.1"] = "Netscape certificate type",
		["2.16.840.1.113730.1.13"] = "Netscape comment",
		["1.3.6.1.4.1.311.20.2"] = "Microsoft certificate template name",
		["1.3.6.1.4.1.311.21.7"] = "Microsoft certificate template",
		["1.3.6.1.4.1.311.21.10"] = "Microsoft application policies",
		["1.3.6.1.4.1.311.21.1"] = "Microsoft CA version",
		["1.3.6.1.4.1.311.21.2"] = "Microsoft previous CA certificate hash",
	}.ToFrozenDictionary(StringComparer.Ordinal);

	public static readonly FrozenDictionary<string, string> Purposes = new Dictionary<string, string>
	{
		[ServerAuth] = "TLS server",
		[ClientAuth] = "TLS client",
		[CodeSigning] = "Code signing",
		[EmailProtection] = "Email protection",
		["1.3.6.1.5.5.7.3.5"] = "IPsec end system",
		["1.3.6.1.5.5.7.3.6"] = "IPsec tunnel",
		["1.3.6.1.5.5.7.3.7"] = "IPsec user",
		["1.3.6.1.5.5.7.3.8"] = "Time stamping",
		["1.3.6.1.5.5.7.3.9"] = "OCSP signing",
		["1.3.6.1.5.5.7.3.17"] = "IPsec IKE",
		["2.5.29.37.0"] = "Any purpose",
		["1.3.6.1.4.1.311.10.3.4"] = "Encrypting file system",
		["1.3.6.1.4.1.311.10.3.12"] = "Document signing",
		["1.3.6.1.4.1.311.20.2.2"] = "Smart card logon",
		["1.3.6.1.4.1.311.10.3.3"] = "Server gated crypto (Microsoft)",
		["2.16.840.1.113730.4.1"] = "Server gated crypto (Netscape)",
		["1.3.6.1.5.2.3.5"] = "Kerberos KDC",
		["1.3.6.1.4.1.311.10.3.1"] = "Microsoft trust list signing",
	}.ToFrozenDictionary(StringComparer.Ordinal);

	public static readonly FrozenDictionary<string, string> Policies = new Dictionary<string, string>
	{
		["2.5.29.32.0"] = "Any policy",
		["2.23.140.1.1"] = "Extended validation (EV)",
		["2.23.140.1.2.1"] = "Domain validated (DV)",
		["2.23.140.1.2.2"] = "Organization validated (OV)",
		["2.23.140.1.2.3"] = "Individual validated (IV)",
		["2.23.140.1.3"] = "EV code signing",
		["2.23.140.1.4.1"] = "Code signing",
		["1.3.6.1.4.1.44947.1.1.1"] = "ISRG domain validated",
	}.ToFrozenDictionary(StringComparer.Ordinal);

	public static readonly FrozenDictionary<string, string> SignatureAlgorithms = new Dictionary<string, string>
	{
		["1.2.840.113549.1.1.4"] = "RSA with MD5",
		[RsaSha1] = "RSA with SHA-1",
		["1.2.840.113549.1.1.14"] = "RSA with SHA-224",
		[RsaSha256] = "RSA with SHA-256",
		[RsaSha384] = "RSA with SHA-384",
		[RsaSha512] = "RSA with SHA-512",
		[RsaPss] = "RSA-PSS",
		[EcdsaSha1] = "ECDSA with SHA-1",
		["1.2.840.10045.4.3.1"] = "ECDSA with SHA-224",
		[EcdsaSha256] = "ECDSA with SHA-256",
		[EcdsaSha384] = "ECDSA with SHA-384",
		[EcdsaSha512] = "ECDSA with SHA-512",
		[Ed25519] = "Ed25519",
		[Ed448] = "Ed448",
		["1.2.840.10040.4.3"] = "DSA with SHA-1",
		["2.16.840.1.101.3.4.3.2"] = "DSA with SHA-256",
	}.ToFrozenDictionary(StringComparer.Ordinal);

	public static readonly FrozenDictionary<string, string> Curves = new Dictionary<string, string>
	{
		[P256] = "P-256",
		[P384] = "P-384",
		[P521] = "P-521",
		["1.3.132.0.10"] = "secp256k1",
		["1.2.840.10045.3.1.1"] = "P-192",
		["1.3.132.0.33"] = "P-224",
		["1.3.36.3.3.2.8.1.1.7"] = "brainpoolP256r1",
		["1.3.36.3.3.2.8.1.1.11"] = "brainpoolP384r1",
		["1.3.36.3.3.2.8.1.1.13"] = "brainpoolP512r1",
	}.ToFrozenDictionary(StringComparer.Ordinal);

	public static readonly FrozenDictionary<string, string> NameAttributes = new Dictionary<string, string>
	{
		["2.5.4.3"] = "CN",
		["2.5.4.4"] = "SN",
		["2.5.4.5"] = "serialNumber",
		["2.5.4.6"] = "C",
		["2.5.4.7"] = "L",
		["2.5.4.8"] = "ST",
		["2.5.4.9"] = "street",
		["2.5.4.10"] = "O",
		["2.5.4.11"] = "OU",
		["2.5.4.12"] = "title",
		["2.5.4.15"] = "businessCategory",
		["2.5.4.17"] = "postalCode",
		["2.5.4.42"] = "GN",
		["2.5.4.97"] = "organizationIdentifier",
		["1.2.840.113549.1.9.1"] = "E",
		["0.9.2342.19200300.100.1.25"] = "DC",
		["0.9.2342.19200300.100.1.1"] = "UID",
		["1.3.6.1.4.1.311.60.2.1.3"] = "jurisdictionC",
	}.ToFrozenDictionary(StringComparer.Ordinal);

	public static readonly FrozenDictionary<string, string> Ciphers = new Dictionary<string, string>
	{
		["2.16.840.1.101.3.4.1.2"] = "AES-128-CBC",
		["2.16.840.1.101.3.4.1.22"] = "AES-192-CBC",
		[Aes256Cbc] = "AES-256-CBC",
		["1.2.840.113549.3.7"] = "3DES-CBC",
		["1.3.14.3.2.7"] = "DES-CBC",
		["1.2.840.113549.3.2"] = "RC2-CBC",
	}.ToFrozenDictionary(StringComparer.Ordinal);

	public static readonly FrozenDictionary<string, string> Prfs = new Dictionary<string, string>
	{
		["1.2.840.113549.2.7"] = "HMAC-SHA1",
		["1.2.840.113549.2.9"] = "HMAC-SHA256",
		["1.2.840.113549.2.10"] = "HMAC-SHA384",
		["1.2.840.113549.2.11"] = "HMAC-SHA512",
	}.ToFrozenDictionary(StringComparer.Ordinal);

	public static readonly FrozenDictionary<string, string> Pkcs12Pbe = new Dictionary<string, string>
	{
		["1.2.840.113549.1.12.1.1"] = "RC4-128 (PKCS#12)",
		["1.2.840.113549.1.12.1.2"] = "RC4-40 (PKCS#12)",
		["1.2.840.113549.1.12.1.3"] = "3DES (PKCS#12, SHA-1)",
		["1.2.840.113549.1.12.1.4"] = "2-key 3DES (PKCS#12, SHA-1)",
		["1.2.840.113549.1.12.1.5"] = "RC2-128 (PKCS#12, SHA-1)",
		["1.2.840.113549.1.12.1.6"] = "RC2-40 (PKCS#12, SHA-1)",
		["1.2.840.113549.1.5.3"] = "DES (PKCS#5, MD5)",
		["1.2.840.113549.1.5.10"] = "DES (PKCS#5, SHA-1)",
	}.ToFrozenDictionary(StringComparer.Ordinal);

	/// <summary>The name for <paramref name="oid"/> from <paramref name="table"/>, or the dotted number when it has none.</summary>
	public static string Name(FrozenDictionary<string, string> table, string? oid) =>
		oid is null ? "unknown" : table.TryGetValue(oid, out string? name) ? name : oid;

	/// <summary>Like <see cref="Name"/>, with the number after a known name: "TLS server (1.3.6.1.5.5.7.3.1)".</summary>
	public static string NameWithNumber(FrozenDictionary<string, string> table, string? oid) =>
		oid is not null && table.TryGetValue(oid, out string? name) ? $"{name} ({oid})" : oid ?? "unknown";
}
