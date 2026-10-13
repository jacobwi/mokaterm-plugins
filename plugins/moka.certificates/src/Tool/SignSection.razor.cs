using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Components;
using Moka.Certificates.Crypto;
using Mokaterm.UI.Common.Components;

namespace Moka.Certificates.Tool;

/// <summary>
/// Raw signatures over text or a file with an RSA or ECDSA key, checked against a certificate or a public key, and CMS
/// signatures made and checked. A check says whether the signature holds, never whether the signer is trusted.
/// </summary>
public sealed partial class SignSection : CertificateViewBase
{
	private static readonly IReadOnlyList<EnumOption<SignMode>> Modes =
	[
		new(SignMode.Sign, "Sign"),
		new(SignMode.Verify, "Verify"),
		new(SignMode.CmsSign, "CMS sign"),
		new(SignMode.CmsVerify, "CMS verify"),
	];

	private static readonly IReadOnlyList<EnumOption<SignatureHash>> Hashes = [new(SignatureHash.Sha256, "SHA-256"), new(SignatureHash.Sha384, "SHA-384"), new(SignatureHash.Sha512, "SHA-512")];

	private static readonly IReadOnlyList<EnumOption<RsaSignatureScheme>> Schemes = [new(RsaSignatureScheme.Pkcs1, "PKCS#1 v1.5"), new(RsaSignatureScheme.Pss, "PSS")];

	private static readonly IReadOnlyList<EnumOption<BinaryEncoding>> Encodings = [new(BinaryEncoding.Base64, "Base64"), new(BinaryEncoding.Hex, "Hex")];

	private SignMode _mode = SignMode.Sign;
	private InputContent _data = InputContent.Empty;
	private InputContent _key = InputContent.Empty;
	private InputContent _signature = InputContent.Empty;
	private string _password = "";
	private SignatureHash _hash;
	private RsaSignatureScheme _scheme = RsaSignatureScheme.Pkcs1;
	private BinaryEncoding _encoding = BinaryEncoding.Base64;
	private bool _detached = true;
	private bool _busy;
	private string? _error;
	private OperationResult? _result;

	private string PrimaryText => _mode switch
	{
		SignMode.Sign => "Sign",
		SignMode.CmsSign => "Sign",
		_ => "Verify",
	};

	private string ModeSummary => _mode switch
	{
		SignMode.Sign => "raw signature",
		SignMode.Verify => "check a raw signature",
		SignMode.CmsSign => "CMS (PKCS#7)",
		_ => "check a CMS signature",
	};

	private string ModeHelp => _mode switch
	{
		SignMode.Sign => "A signature over the data with a private key, the same bytes openssl dgst -sign makes.",
		SignMode.Verify => "Whether the signature over the data was made with the key in a certificate or public key.",
		SignMode.CmsSign => "A CMS signature (PKCS#7, .p7s) that carries the signer's certificate, with the content inside or beside it.",
		_ => "Every signature on a CMS structure, checked against its signer's key. Trust in the signer is not judged.",
	};

	protected override void OnInitialized() => _hash = Settings.SignatureHash;

	private void OnModeChanged(SignMode mode)
	{
		_mode = mode;
		_result = null;
		_error = null;
	}

	private async Task RunAsync()
	{
		_error = null;
		_result = null;
		SignMode mode = _mode;
		InputContent data = _data;
		InputContent key = _key;
		InputContent signature = _signature;
		string password = _password;
		SignatureHash hash = _hash;
		RsaSignatureScheme scheme = _scheme;
		BinaryEncoding encoding = _encoding;
		bool detached = _detached;
		DateTimeOffset now = Now;
		string stem = FileNameFrom(data.FileName is { } file ? Path.GetFileName(file) : "data", "");
		_busy = true;
		StateHasChanged();
		try
		{
			_result = await Task.Run(() => mode switch
			{
				SignMode.Sign => Sign(data, key, password, hash, scheme, encoding, stem),
				SignMode.Verify => Verify(data, signature, key, hash, scheme),
				SignMode.CmsSign => SignCms(data, key, password, hash, detached, now, stem),
				_ => VerifyCms(signature, data, key),
			});
		}
		catch (CertificateException ex)
		{
			_error = ex.Message;
		}
		catch (CryptographicException)
		{
			_error = "This could not be done with that key.";
		}
		finally
		{
			_busy = false;
		}
	}

	private static OperationResult Sign(InputContent data, InputContent key, string password, SignatureHash hash, RsaSignatureScheme scheme, BinaryEncoding encoding, string stem)
	{
		RequireData(data);
		using MaterialSet set = Read(key, password, "Paste or open the private key.");
		PrivateKeyMaterial privateKey = set.RequireOnePrivateKey();
		byte[] signature = CertificateSigner.Sign(data.ToData(), privateKey.Key, hash, scheme);
		string algorithm = privateKey.Key is RSA ? $"RSA {(scheme == RsaSignatureScheme.Pss ? "PSS" : "PKCS#1 v1.5")}" : "ECDSA";
		return new OperationResult
		{
			TextTitle = $"Signature, {algorithm} with {CryptoChoices.HashName(hash)}",
			Text = BinaryText.Format(signature, encoding),
			TextFileName = stem + (encoding == BinaryEncoding.Hex ? ".sig.hex" : ".sig.b64"),
			Bytes = signature,
			BytesFileName = stem + ".sig",
			BytesLabel = "Save binary",
		};
	}

	private static OperationResult Verify(InputContent data, InputContent signature, InputContent key, SignatureHash hash, RsaSignatureScheme scheme)
	{
		RequireData(data);
		byte[] signatureBytes = signature.Bytes ?? (BinaryText.TryParse(signature.Text, out byte[]? parsed)
			? parsed
			: throw new CertificateException("The signature is not base64 or hex."));
		PublicKey publicKey = PublicKeyOf(key);
		bool valid = CertificateSigner.Verify(data.ToData(), signatureBytes, publicKey, hash, scheme);
		return new OperationResult
		{
			Verdict = valid,
			Summary = valid
				? $"The signature was made over this data with this key, using {CryptoChoices.HashName(hash)}."
				: "The signature does not match: different data, a different key, or a different hash or padding.",
		};
	}

	private static OperationResult SignCms(InputContent data, InputContent signer, string password, SignatureHash hash, bool detached, DateTimeOffset now, string stem)
	{
		RequireData(data);
		using MaterialSet set = Read(signer, password, "Paste or open the signer's certificate and key.");
		PrivateKeyMaterial key = set.RequireOnePrivateKey();
		X509Certificate2 certificate = set.CertificateFor(key) ?? throw new CertificateException("CMS needs the signer's certificate as well as its key.");
		byte[] cms = CertificateSigner.SignCms(data.ToData(), certificate, key.Key, hash, detached, set.Certificates, now);
		return new OperationResult
		{
			TextTitle = detached ? "CMS signature, detached" : "CMS signature, content inside",
			Text = CertificateConverter.ToPem("CMS", cms),
			TextFileName = stem + ".p7s.pem",
			Bytes = cms,
			BytesFileName = stem + ".p7s",
		};
	}

	private static OperationResult VerifyCms(InputContent signature, InputContent content, InputContent expected)
	{
		byte[] cms = signature.ToStructure() ?? throw new CertificateException("Paste or open the CMS signature.");
		X509Certificate2? expectedSigner = null;
		try
		{
			if (!expected.IsEmpty)
			{
				using MaterialSet set = CertificateReader.Read(expected.ToCryptoInput());
				expectedSigner = set.Certificates.Count > 0
					? X509CertificateLoader.LoadCertificate(set.Certificates[0].RawData)
					: throw new CertificateException("The expected signer box holds no certificate.");
			}

			CmsVerification result = CertificateSigner.VerifyCms(cms, content.IsEmpty ? null : content.ToData(), expectedSigner);
			string summary = result.Valid
				? string.Create(CultureInfo.CurrentCulture, $"{(result.Signers.Count == 1 ? "The signature holds" : $"All {result.Signers.Count} signatures hold")}{(result.Detached ? " over the content given" : "")}.")
				: expectedSigner is not null && result.Signers.All(signer => signer.IsExpectedSigner != true)
					? "The expected signer did not sign this."
					: "At least one signature does not hold.";
			OperationResult output = new() { Verdict = result.Valid, Summary = summary, Signers = result.Signers };
			if (result.Content is { } inside)
			{
				output = TextOrBytes(output, inside, "Signed content", "signed-content");
			}

			return output;
		}
		finally
		{
			expectedSigner?.Dispose();
		}
	}

	/// <summary>Content shown as text when it is UTF-8, and offered as a file either way.</summary>
	internal static OperationResult TextOrBytes(OperationResult result, byte[] content, string title, string stem)
	{
		string? text = null;
		try
		{
			text = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(content);
			if (text.Any(c => char.IsControl(c) && c is not '\r' and not '\n' and not '\t'))
			{
				text = null;
			}
		}
		catch (System.Text.DecoderFallbackException)
		{
			text = null;
		}

		return result with
		{
			TextTitle = title,
			Text = text,
			TextFileName = text is null ? null : stem + ".txt",
			Bytes = content,
			BytesFileName = stem + ".bin",
			BytesLabel = text is null ? "Save" : "Save bytes",
		};
	}

	internal static PublicKey PublicKeyOf(InputContent key)
	{
		if (key.IsEmpty)
		{
			throw new CertificateException("Paste the certificate or public key to check with.");
		}

		using MaterialSet set = CertificateReader.Read(key.ToCryptoInput());
		if (set.Certificates.Count > 0)
		{
			return PublicKey.CreateFromSubjectPublicKeyInfo(set.Certificates[0].PublicKey.ExportSubjectPublicKeyInfo(), out _);
		}

		if (set.PublicKeys.Count > 0)
		{
			return set.PublicKeys[0].Key;
		}

		if (set.PrivateKeys.Count > 0)
		{
			return set.PrivateKeys[0].PublicKey;
		}

		throw new CertificateException(set.Notes.Count > 0 ? set.Notes[0].Message : "No certificate or public key was found there.");
	}

	internal static MaterialSet Read(InputContent input, string password, string empty)
	{
		if (input.IsEmpty)
		{
			throw new CertificateException(empty);
		}

		return CertificateReader.Read(input.ToCryptoInput(), password);
	}

	internal static string BytesNote(byte[] bytes) => string.Create(CultureInfo.CurrentCulture, $"{bytes.Length:N0} bytes, not text");

	private static string SignerNote(CmsSignerResult signer) =>
		signer.SigningTime is { } time ? $"{signer.DigestAlgorithm}, signed {CertificateReport.Date(time)}" : signer.DigestAlgorithm;

	private static void RequireData(InputContent data)
	{
		if (data.IsEmpty)
		{
			throw new CertificateException("Type the data, or open its file.");
		}
	}
}
