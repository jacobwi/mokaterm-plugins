using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Moka.Certificates.Crypto;

/// <summary>
/// The readable text dump and the JSON form of what the reader described, for copying and saving. Both come from the
/// same <see cref="CertificateInfo"/>, <see cref="RequestInfo"/> and <see cref="KeyInfo"/> the views draw, so what is
/// saved says what was shown.
/// </summary>
public static class CertificateReport
{
	private static readonly JsonWriterOptions JsonOptions = new() { Indented = true };

	public static string Text(CertificateInfo info)
	{
		ArgumentNullException.ThrowIfNull(info);
		StringBuilder text = new();
		Line(text, "Subject", info.Subject);
		Line(text, "Issuer", info.Issuer);
		Line(text, "Serial number", info.SerialNumber);
		Line(text, "Version", info.Version.ToString(CultureInfo.InvariantCulture));
		Line(text, "Valid from", Date(info.NotBefore));
		Line(text, "Valid until", $"{Date(info.NotAfter)} ({ExpiryText(info)})");
		Line(text, "Key", info.Key.Description);
		Line(text, "Signature", info.SignatureAlgorithm);
		Line(text, "Certificate authority", info.IsCertificateAuthority ? "yes" : "no");
		Line(text, "Self-signed", info.IsSelfSigned switch { true => "yes", false => "no", null => "self-issued, signature not checked" });
		if (info.AlternativeNames.Count > 0)
		{
			text.AppendLine("Names:");
			foreach (GeneralNameInfo name in info.AlternativeNames)
			{
				text.Append("  ").AppendLine(name.ToString());
			}
		}

		Extensions(text, info.Extensions);
		Line(text, "SHA-256", info.Sha256);
		Line(text, "SHA-1", info.Sha1);
		Line(text, "Key SHA-256", info.Key.SpkiSha256);
		Line(text, "Key pin", info.Key.Pin);
		return text.ToString();
	}

	public static string Text(RequestInfo info)
	{
		ArgumentNullException.ThrowIfNull(info);
		StringBuilder text = new();
		Line(text, "Subject", info.Subject.Length > 0 ? info.Subject : "(empty)");
		Line(text, "Key", info.Key.Description);
		Line(text, "Signature", $"{info.SignatureAlgorithm}, {SignatureText(info.SignatureValid)}");
		if (info.AlternativeNames.Count > 0)
		{
			text.AppendLine("Names:");
			foreach (GeneralNameInfo name in info.AlternativeNames)
			{
				text.Append("  ").AppendLine(name.ToString());
			}
		}

		Extensions(text, info.Extensions);
		Line(text, "SHA-256", info.Sha256);
		Line(text, "Key SHA-256", info.Key.SpkiSha256);
		return text.ToString();
	}

	public static string Text(KeyInfo info)
	{
		ArgumentNullException.ThrowIfNull(info);
		StringBuilder text = new();
		Line(text, "Key", info.Description);
		Line(text, "Key SHA-256", info.SpkiSha256);
		Line(text, "Key pin", info.Pin);
		if (info.Limitation is { } limitation)
		{
			Line(text, "Note", limitation);
		}

		return text.ToString();
	}

	public static string Json(CertificateInfo info)
	{
		ArgumentNullException.ThrowIfNull(info);
		return Write(writer =>
		{
			writer.WriteString("subject", info.Subject);
			Parts(writer, "subjectParts", info.SubjectParts);
			writer.WriteString("issuer", info.Issuer);
			Parts(writer, "issuerParts", info.IssuerParts);
			writer.WriteString("serialNumber", info.SerialNumber);
			writer.WriteNumber("version", info.Version);
			writer.WriteString("notBefore", info.NotBefore.UtcDateTime);
			writer.WriteString("notAfter", info.NotAfter.UtcDateTime);
			writer.WriteString("expiry", ExpiryName(info.Expiry));
			writer.WriteNumber("daysLeft", info.DaysLeft);
			Names(writer, info.AlternativeNames);
			Key(writer, info.Key);
			writer.WriteString("signatureAlgorithm", info.SignatureAlgorithm);
			writer.WriteString("signatureAlgorithmOid", info.SignatureAlgorithmOid);
			writer.WriteBoolean("certificateAuthority", info.IsCertificateAuthority);
			writer.WriteBoolean("selfIssued", info.IsSelfIssued);
			if (info.IsSelfSigned is { } selfSigned)
			{
				writer.WriteBoolean("selfSigned", selfSigned);
			}
			else
			{
				writer.WriteNull("selfSigned");
			}

			Extensions(writer, info.Extensions);
			writer.WriteString("sha256", info.Sha256);
			writer.WriteString("sha1", info.Sha1);
		});
	}

	public static string Json(RequestInfo info)
	{
		ArgumentNullException.ThrowIfNull(info);
		return Write(writer =>
		{
			writer.WriteString("subject", info.Subject);
			Parts(writer, "subjectParts", info.SubjectParts);
			Names(writer, info.AlternativeNames);
			Key(writer, info.Key);
			writer.WriteString("signatureAlgorithm", info.SignatureAlgorithm);
			if (info.SignatureValid is { } valid)
			{
				writer.WriteBoolean("signatureValid", valid);
			}
			else
			{
				writer.WriteNull("signatureValid");
			}

			Extensions(writer, info.Extensions);
			writer.WriteString("sha256", info.Sha256);
		});
	}

	public static string Json(KeyInfo info)
	{
		ArgumentNullException.ThrowIfNull(info);
		return Write(writer => Key(writer, info));
	}

	/// <summary>"in 42 days", "expired 3 days ago", "not valid until ...".</summary>
	public static string ExpiryText(CertificateInfo info)
	{
		ArgumentNullException.ThrowIfNull(info);
		return info.Expiry switch
		{
			ExpiryState.NotYetValid => $"not valid until {Date(info.NotBefore)}",
			ExpiryState.Expired => info.DaysLeft == -1 ? "expired yesterday" : $"expired {Days(-info.DaysLeft)} ago",
			_ => info.DaysLeft == 0 ? "expires today" : $"expires in {Days(info.DaysLeft)}",
		};
	}

	/// <summary>The uppercase word a status chip shows.</summary>
	public static string ExpiryName(ExpiryState state) => state switch
	{
		ExpiryState.Expiring => "expiring",
		ExpiryState.Expired => "expired",
		ExpiryState.NotYetValid => "not yet valid",
		_ => "valid",
	};

	public static string SignatureText(bool? valid) => valid switch
	{
		true => "signature verifies",
		false => "signature does not verify",
		null => "signature not checked (algorithm not supported here)",
	};

	public static string Date(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

	private static string Days(int days) => days == 1 ? "1 day" : string.Create(CultureInfo.InvariantCulture, $"{days:N0} days");

	private static void Line(StringBuilder text, string label, string value) => text.Append(label).Append(": ").AppendLine(value);

	private static void Extensions(StringBuilder text, IReadOnlyList<ExtensionInfo> extensions)
	{
		if (extensions.Count == 0)
		{
			return;
		}

		text.AppendLine("Extensions:");
		foreach (ExtensionInfo extension in extensions)
		{
			text.Append("  ").Append(extension.Name);
			if (extension.Critical)
			{
				text.Append(" (critical)");
			}

			text.AppendLine();
			foreach (string line in extension.Lines)
			{
				text.Append("    ").AppendLine(line);
			}
		}
	}

	private static string Write(Action<Utf8JsonWriter> body)
	{
		using MemoryStream stream = new();
		using (Utf8JsonWriter writer = new(stream, JsonOptions))
		{
			writer.WriteStartObject();
			body(writer);
			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(stream.ToArray());
	}

	private static void Parts(Utf8JsonWriter writer, string property, IReadOnlyList<NamePart> parts)
	{
		writer.WriteStartArray(property);
		foreach (NamePart part in parts)
		{
			writer.WriteStartObject();
			writer.WriteString("type", part.Label);
			writer.WriteString("value", part.Value);
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
	}

	private static void Names(Utf8JsonWriter writer, IReadOnlyList<GeneralNameInfo> names)
	{
		writer.WriteStartArray("alternativeNames");
		foreach (GeneralNameInfo name in names)
		{
			writer.WriteStartObject();
			writer.WriteString("type", name.Tag);
			writer.WriteString("value", name.Value);
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
	}

	private static void Key(Utf8JsonWriter writer, KeyInfo key)
	{
		writer.WriteStartObject("key");
		writer.WriteString("algorithm", key.Algorithm);
		if (key.Bits is { } bits)
		{
			writer.WriteNumber("bits", bits);
		}

		if (key.Curve is { } curve)
		{
			writer.WriteString("curve", curve);
		}

		writer.WriteString("sha256", key.SpkiSha256);
		writer.WriteString("pin", key.Pin);
		writer.WriteEndObject();
	}

	private static void Extensions(Utf8JsonWriter writer, IReadOnlyList<ExtensionInfo> extensions)
	{
		writer.WriteStartArray("extensions");
		foreach (ExtensionInfo extension in extensions)
		{
			writer.WriteStartObject();
			writer.WriteString("name", extension.Name);
			writer.WriteString("oid", extension.Oid);
			writer.WriteBoolean("critical", extension.Critical);
			writer.WriteStartArray("values");
			foreach (string line in extension.Lines)
			{
				writer.WriteStringValue(line.Trim());
			}

			writer.WriteEndArray();
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
	}
}
