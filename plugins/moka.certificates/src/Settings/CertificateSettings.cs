using Moka.Certificates.Crypto;
using Moka.Certificates.ServerFiles;
using Mokaterm.Abstractions.Settings;

namespace Moka.Certificates.Settings;

/// <summary>
/// The plugin's settings, under <c>plugin.moka.certificates</c> in settings.json. Every range is a Min and Max pair here,
/// read by <see cref="Clamped"/>, by the settings page and by whatever applies the value, so none of them repeats it.
/// </summary>
public sealed record CertificateSettings : ISettingsSection
{
	public const int MinValidityDays = 1;

	/// <summary>Twenty-five years, which covers a long-lived private root.</summary>
	public const int MaxValidityDays = 9125;

	public const int MinExpiringDays = 1;

	public const int MaxExpiringDays = 365;

	public const int MinServerCheckSeconds = 2;

	public const int MaxServerCheckSeconds = 60;

	public const int MaxExtraPaths = 20;

	public static string SectionKey => "plugin.moka.certificates";

	public KeyAlgorithm KeyAlgorithm { get; init; } = KeyAlgorithm.Ecdsa;

	public RsaKeySize RsaKeySize { get; init; } = RsaKeySize.Bits3072;

	public EllipticCurve Curve { get; init; } = EllipticCurve.P256;

	/// <summary>How long a new certificate is valid for.</summary>
	public int ValidityDays { get; init; } = 365;

	public SignatureHash SignatureHash { get; init; } = SignatureHash.Sha256;

	public PfxEncryption PfxEncryption { get; init; } = PfxEncryption.Aes256;

	/// <summary>How many days before its end a certificate is marked as expiring.</summary>
	public int ExpiringDays { get; init; } = 30;

	public int ServerCheckTimeoutSeconds { get; init; } = 10;

	/// <summary>
	/// More certificate files or folders on servers for the System page to read, separated by commas. They are paths on
	/// the servers, not on this machine, so the settings backup carries them.
	/// </summary>
	public string ExtraPaths { get; init; } = "";

	public KeySpec DefaultKey => new(KeyAlgorithm, RsaKeySize, Curve);

	/// <summary>A copy with every value inside what the settings page allows, since settings.json can be edited by hand.</summary>
	public CertificateSettings Clamped() => this with
	{
		KeyAlgorithm = Enum.IsDefined(KeyAlgorithm) ? KeyAlgorithm : KeyAlgorithm.Ecdsa,
		RsaKeySize = Enum.IsDefined(RsaKeySize) ? RsaKeySize : RsaKeySize.Bits3072,
		Curve = Enum.IsDefined(Curve) ? Curve : EllipticCurve.P256,
		ValidityDays = Math.Clamp(ValidityDays, MinValidityDays, MaxValidityDays),
		SignatureHash = Enum.IsDefined(SignatureHash) ? SignatureHash : SignatureHash.Sha256,
		PfxEncryption = Enum.IsDefined(PfxEncryption) ? PfxEncryption : PfxEncryption.Aes256,
		ExpiringDays = Math.Clamp(ExpiringDays, MinExpiringDays, MaxExpiringDays),
		ServerCheckTimeoutSeconds = Math.Clamp(ServerCheckTimeoutSeconds, MinServerCheckSeconds, MaxServerCheckSeconds),
		ExtraPaths = ExtraPathsProblem(ExtraPaths) is null ? string.Join(", ", SplitPaths(ExtraPaths)) : "",
	};

	/// <summary>The extra paths one by one.</summary>
	public IReadOnlyList<string> ExtraPathList() => SplitPaths(ExtraPaths);

	public static IReadOnlyList<string> SplitPaths(string? text) =>
		[.. (text ?? "").Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal)];

	/// <summary>Why the extra paths cannot be used, or null: absolute paths on the server, at most <see cref="MaxExtraPaths"/>.</summary>
	public static string? ExtraPathsProblem(string? text)
	{
		IReadOnlyList<string> paths = SplitPaths(text);
		if (paths.Count > MaxExtraPaths)
		{
			return $"At most {MaxExtraPaths} paths.";
		}

		return paths.Select(ServerCertificateCommands.PathProblem).FirstOrDefault(problem => problem is not null);
	}
}
