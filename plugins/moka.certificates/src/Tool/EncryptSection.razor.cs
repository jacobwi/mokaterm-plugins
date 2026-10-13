using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Moka.Certificates.Components;
using Moka.Certificates.Crypto;
using Mokaterm.UI.Common.Components;

namespace Moka.Certificates.Tool;

/// <summary>
/// RSA-OAEP straight to a key for a few bytes, and CMS enveloped data for anything longer to one or more certificates;
/// both decrypted again with the private key. Decrypted content is treated as a secret: hidden until shown and copied
/// as one.
/// </summary>
public sealed partial class EncryptSection : CertificateViewBase
{
	private static readonly IReadOnlyList<EnumOption<EncryptMode>> Modes =
	[
		new(EncryptMode.OaepEncrypt, "RSA-OAEP encrypt"),
		new(EncryptMode.OaepDecrypt, "RSA-OAEP decrypt"),
		new(EncryptMode.CmsEncrypt, "CMS encrypt"),
		new(EncryptMode.CmsDecrypt, "CMS decrypt"),
	];

	private static readonly IReadOnlyList<EnumOption<OaepHash>> OaepHashes = [new(OaepHash.Sha256, "OAEP SHA-256"), new(OaepHash.Sha1, "OAEP SHA-1")];

	private static readonly IReadOnlyList<EnumOption<BinaryEncoding>> Encodings = [new(BinaryEncoding.Base64, "Base64"), new(BinaryEncoding.Hex, "Hex")];

	private EncryptMode _mode = EncryptMode.CmsEncrypt;
	private InputContent _data = InputContent.Empty;
	private InputContent _key = InputContent.Empty;
	private string _password = "";
	private OaepHash _oaepHash = OaepHash.Sha256;
	private BinaryEncoding _encoding = BinaryEncoding.Base64;
	private bool _pkcs1;
	private bool _busy;
	private string? _error;
	private OperationResult? _result;

	private bool Decrypting => _mode is EncryptMode.OaepDecrypt or EncryptMode.CmsDecrypt;

	private string ModeSummary => _mode switch
	{
		EncryptMode.OaepEncrypt or EncryptMode.OaepDecrypt => "RSA-OAEP",
		_ => "CMS enveloped data",
	};

	private string ModeHelp => _mode switch
	{
		EncryptMode.OaepEncrypt => "Encrypts a few bytes straight to an RSA key, such as a password or a symmetric key.",
		EncryptMode.OaepDecrypt => "Decrypts RSA-OAEP ciphertext with the private key it was made for.",
		EncryptMode.CmsEncrypt => "Encrypts text or a file of any size to one or more certificates (S/MIME's format, .p7m).",
		_ => "Opens a CMS encrypted message with your certificate and its private key.",
	};

	/// <summary>How much the recipient key takes, once one is pasted.</summary>
	private string? OaepLimit
	{
		get
		{
			if (_key.IsEmpty)
			{
				return null;
			}

			try
			{
				PublicKey key = SignSection.PublicKeyOf(_key);
				int max = CertificateEncryptor.MaxOaepBytes(key, _oaepHash);
				return string.Create(CultureInfo.CurrentCulture, $"This key takes at most {max} bytes; the data is {_data.ToData().Length}.");
			}
			catch (CertificateException ex)
			{
				return ex.Message;
			}
		}
	}

	private void OnModeChanged(EncryptMode mode)
	{
		_mode = mode;
		_result = null;
		_error = null;
	}

	private async Task RunAsync()
	{
		_error = null;
		_result = null;
		EncryptMode mode = _mode;
		InputContent data = _data;
		InputContent key = _key;
		string password = _password;
		OaepHash hash = _oaepHash;
		BinaryEncoding encoding = _encoding;
		bool pkcs1 = _pkcs1;
		string stem = FileNameFrom(data.FileName is { } file ? Path.GetFileName(file) : "data", "");
		_busy = true;
		StateHasChanged();
		try
		{
			_result = await Task.Run(() => mode switch
			{
				EncryptMode.OaepEncrypt => OaepEncrypt(data, key, hash, encoding, stem),
				EncryptMode.OaepDecrypt => OaepDecrypt(data, key, password, hash),
				EncryptMode.CmsEncrypt => CmsEncrypt(data, key, pkcs1, stem),
				_ => CmsDecrypt(data, key, password),
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

	private static OperationResult OaepEncrypt(InputContent data, InputContent key, OaepHash hash, BinaryEncoding encoding, string stem)
	{
		if (data.IsEmpty)
		{
			throw new CertificateException("Type the data to encrypt.");
		}

		byte[] ciphertext = CertificateEncryptor.EncryptOaep(data.ToData(), SignSection.PublicKeyOf(key), hash);
		return new OperationResult
		{
			Summary = string.Create(CultureInfo.CurrentCulture, $"{ciphertext.Length} bytes of ciphertext; only the private key can open them."),
			TextTitle = "Ciphertext",
			Text = BinaryText.Format(ciphertext, encoding),
			TextFileName = stem + (encoding == BinaryEncoding.Hex ? ".enc.hex" : ".enc.b64"),
			Bytes = ciphertext,
			BytesFileName = stem + ".enc",
			BytesLabel = "Save binary",
		};
	}

	private static OperationResult OaepDecrypt(InputContent data, InputContent key, string password, OaepHash hash)
	{
		byte[] ciphertext = data.Bytes ?? (BinaryText.TryParse(data.Text, out byte[]? parsed)
			? parsed
			: throw new CertificateException("The ciphertext is not base64 or hex."));
		using MaterialSet set = SignSection.Read(key, password, "Paste or open the private key.");
		byte[] plain = CertificateEncryptor.DecryptOaep(ciphertext, set.RequireOnePrivateKey().Key, hash);
		return SignSection.TextOrBytes(new OperationResult { Summary = string.Create(CultureInfo.CurrentCulture, $"{plain.Length} bytes.") }, plain, "Decrypted", "decrypted");
	}

	private static OperationResult CmsEncrypt(InputContent data, InputContent recipients, bool pkcs1, string stem)
	{
		if (data.IsEmpty)
		{
			throw new CertificateException("Type the data, or open the file to encrypt.");
		}

		if (recipients.IsEmpty)
		{
			throw new CertificateException("Paste at least one recipient certificate.");
		}

		using MaterialSet set = CertificateReader.Read(recipients.ToCryptoInput());
		if (set.Certificates.Count == 0)
		{
			throw new CertificateException("No certificate was found among the recipients.");
		}

		byte[] message = CertificateEncryptor.EncryptCms(data.ToData(), set.Certificates, pkcs1);
		return new OperationResult
		{
			Summary = set.Certificates.Count == 1
				? $"Encrypted to {set.Certificates[0].SubjectName.Name}."
				: string.Create(CultureInfo.CurrentCulture, $"Encrypted to {set.Certificates.Count} recipients; any of them can open it."),
			TextTitle = "Encrypted message",
			Text = CertificateConverter.ToPem("CMS", message),
			TextFileName = stem + ".p7m.pem",
			Bytes = message,
			BytesFileName = stem + ".p7m",
		};
	}

	private static OperationResult CmsDecrypt(InputContent message, InputContent key, string password)
	{
		byte[] cms = message.ToStructure() ?? throw new CertificateException("Paste or open the encrypted message.");
		using MaterialSet set = SignSection.Read(key, password, "Paste or open your certificate and private key.");
		PrivateKeyMaterial privateKey = set.RequireOnePrivateKey();
		X509Certificate2 certificate = set.CertificateFor(privateKey) ?? throw new CertificateException("CMS needs your certificate as well as the key: the message names its recipients by certificate.");
		CmsDecryption result = CertificateEncryptor.DecryptCms(cms, certificate, privateKey.Key);
		string summary = string.Create(CultureInfo.CurrentCulture, $"{result.Content.Length} bytes, for {(result.RecipientCount == 1 ? "one recipient" : $"{result.RecipientCount} recipients")}.");
		return SignSection.TextOrBytes(new OperationResult { Summary = summary }, result.Content, "Decrypted", "decrypted");
	}
}
