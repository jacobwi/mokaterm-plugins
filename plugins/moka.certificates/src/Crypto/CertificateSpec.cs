namespace Moka.Certificates.Crypto;

/// <summary>What a new certificate or signing request says: who it is for, which names, what for, for how long.</summary>
public sealed record CertificateSpec
{
	public const int MinValidityDays = 1;

	/// <summary>A hundred years: past that, nothing reads the dates the same way.</summary>
	public const int MaxValidityDays = 36_500;

	public const int MaxPathLength = 8;

	public SubjectSpec Subject { get; init; } = new();

	public IReadOnlyList<GeneralNameInfo> Names { get; init; } = [];

	public UsagePresets Usages { get; init; } = UsagePresets.TlsServer;

	public int ValidityDays { get; init; } = 365;

	public SignatureHash Hash { get; init; } = SignatureHash.Sha256;

	/// <summary>For a CA: how many CAs may sit below it. Null sets no limit.</summary>
	public int? PathLength { get; init; }

	public bool IsCertificateAuthority => Usages.HasFlag(UsagePresets.CertificateAuthority);

	/// <summary>Every reason this cannot make a certificate (or, with <paramref name="forRequest"/>, a request).</summary>
	public IReadOnlyList<string> Problems(bool forRequest)
	{
		List<string> problems = [.. Subject.Problems()];
		if (Subject.IsEmpty && Names.Count == 0)
		{
			problems.Add("Give it a common name or at least one name.");
		}

		if (!forRequest)
		{
			problems.AddRange(LimitProblems());
		}
		else if (PathLength is < 0 or > MaxPathLength)
		{
			problems.Add($"The path length is 0 to {MaxPathLength}.");
		}

		return problems;
	}

	/// <summary>Why the validity or the path length cannot be used, whoever the certificate is for.</summary>
	public IReadOnlyList<string> LimitProblems()
	{
		List<string> problems = [];
		if (ValidityDays < MinValidityDays || ValidityDays > MaxValidityDays)
		{
			problems.Add(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"The validity is {MinValidityDays} to {MaxValidityDays:N0} days."));
		}

		if (PathLength is < 0 or > MaxPathLength)
		{
			problems.Add($"The path length is 0 to {MaxPathLength}.");
		}

		return problems;
	}
}
