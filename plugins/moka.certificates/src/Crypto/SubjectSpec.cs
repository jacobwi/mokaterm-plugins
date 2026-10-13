using System.Security.Cryptography.X509Certificates;

namespace Moka.Certificates.Crypto;

/// <summary>
/// The subject of a new certificate or request, field by field. The lengths are X.520's upper bounds, which CAs and
/// OpenSSL hold certificates to.
/// </summary>
public sealed record SubjectSpec
{
	public const int MaxCommonName = 64;
	public const int MaxOrganization = 64;
	public const int MaxLocality = 128;
	public const int MaxEmail = 255;

	public string CommonName { get; init; } = "";

	public string Organization { get; init; } = "";

	public string OrganizationalUnit { get; init; } = "";

	public string Locality { get; init; } = "";

	public string State { get; init; } = "";

	/// <summary>Two letters, such as US.</summary>
	public string Country { get; init; } = "";

	public string Email { get; init; } = "";

	public bool IsEmpty => new[] { CommonName, Organization, OrganizationalUnit, Locality, State, Country, Email }.All(string.IsNullOrWhiteSpace);

	/// <summary>Every reason the fields cannot make a name, for the form to show.</summary>
	public IReadOnlyList<string> Problems()
	{
		List<string> problems = [];
		Check(problems, "The common name", CommonName, MaxCommonName);
		Check(problems, "The organization", Organization, MaxOrganization);
		Check(problems, "The organizational unit", OrganizationalUnit, MaxOrganization);
		Check(problems, "The locality", Locality, MaxLocality);
		Check(problems, "The state", State, MaxLocality);
		string country = Country.Trim();
		if (country.Length > 0 && (country.Length != 2 || !country.All(char.IsAsciiLetter)))
		{
			problems.Add("The country is two letters, such as US or DE.");
		}

		string email = Email.Trim();
		if (email.Length > MaxEmail || (email.Length > 0 && (!email.All(char.IsAscii) || email.Count(c => c == '@') != 1 || email.Any(char.IsWhiteSpace))))
		{
			problems.Add("The email address is one ASCII address, such as admin@example.com.");
		}

		return problems;
	}

	/// <summary>
	/// The name, encoded from the most general part to the most specific (C, ST, L, O, OU, CN, then the email address),
	/// the order OpenSSL and CAs write. The builder encodes what it was given last first, so the parts go in backwards.
	/// </summary>
	/// <exception cref="CertificateException">A field is not one <see cref="Problems"/> accepts.</exception>
	public X500DistinguishedName ToName()
	{
		if (Problems() is [{ } first, ..])
		{
			throw new CertificateException(first);
		}

		X500DistinguishedNameBuilder builder = new();
		if (Email.Trim() is { Length: > 0 } email)
		{
			builder.AddEmailAddress(email);
		}

		if (CommonName.Trim() is { Length: > 0 } commonName)
		{
			builder.AddCommonName(commonName);
		}

		if (OrganizationalUnit.Trim() is { Length: > 0 } unit)
		{
			builder.AddOrganizationalUnitName(unit);
		}

		if (Organization.Trim() is { Length: > 0 } organization)
		{
			builder.AddOrganizationName(organization);
		}

		if (Locality.Trim() is { Length: > 0 } locality)
		{
			builder.AddLocalityName(locality);
		}

		if (State.Trim() is { Length: > 0 } state)
		{
			builder.AddStateOrProvinceName(state);
		}

		if (Country.Trim() is { Length: > 0 } country)
		{
			builder.AddCountryOrRegion(country.ToUpperInvariant());
		}

		return builder.Build();
	}

	private static void Check(List<string> problems, string label, string value, int max)
	{
		string trimmed = value.Trim();
		if (trimmed.Length > max)
		{
			problems.Add($"{label} is at most {max} characters.");
		}
		else if (trimmed.Any(char.IsControl))
		{
			problems.Add($"{label} cannot hold control characters.");
		}
	}
}
