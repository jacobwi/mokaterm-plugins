using System.Security.Cryptography;
using Moka.DevTools.Components;
using Moka.DevTools.Hashing;
using Moka.DevTools.Jwt;
using Moka.DevTools.Text;
using Mokaterm.UI.Common.Components;

namespace Moka.DevTools.Tool;

/// <summary>
/// Reads a JWT: the header, the claims (hidden until shown), the times as dates, the signature, and a check of the
/// signature against a secret, a public key, a certificate or a JWK. The token and the secret are cleared when the tool
/// closes.
/// </summary>
public sealed partial class JwtSection : DevToolViewBase, IDisposable
{
	private static readonly IReadOnlyList<EnumOption<KeyFormat>> SecretFormats =
	[
		new(KeyFormat.Text, "Text"),
		new(KeyFormat.Base64, "Base64"),
		new(KeyFormat.Hex, "Hex"),
	];

	private string _token = "";
	private string _secret = "";
	private string _publicKey = "";
	private KeyFormat _secretFormat = KeyFormat.Text;
	private JwtToken? _jwt;
	private string? _error;
	private JwtVerification? _verification;
	private bool _verifying;
	private int _reads;

	private JwtFamily Family => JwtVerifier.FamilyOf(_jwt?.Algorithm);

	private string Summary => _jwt switch
	{
		null => _error is null ? "Decode a token, read its claims and check its signature" : "Not read",
		{ } jwt => $"{jwt.Algorithm ?? "No alg"}, {JwtValidity.Of(jwt, Now).Text.ToLowerInvariant()}",
	};

	private string NotCheckedText => Family switch
	{
		JwtFamily.None => "alg is none, so there is no signature to check.",
		JwtFamily.Hmac => "Type the secret and press Verify.",
		JwtFamily.PublicKey => "Paste the key and press Verify.",
		_ => _jwt?.Algorithm is { } algorithm ? $"{algorithm} signatures are not checked here." : "The header names no algorithm.",
	};

	public void Dispose()
	{
		// Strings cannot be wiped, but nothing should keep them reachable once the tool is closed.
		_token = "";
		_secret = "";
		_publicKey = "";
		_jwt = null;
		_verification = null;
	}

	private static string VerdictText(JwtVerdict verdict) => verdict switch
	{
		JwtVerdict.Verified => "Signature verified",
		JwtVerdict.Failed => "Signature does not match",
		_ => "Not checked",
	};

	private static ResultTone VerdictTone(JwtVerdict verdict) => verdict switch
	{
		JwtVerdict.Verified => ResultTone.Good,
		JwtVerdict.Failed => ResultTone.Bad,
		_ => ResultTone.Plain,
	};

	private async Task OnTokenChangedAsync(string? value)
	{
		_token = value ?? "";
		_verification = null;
		int read = ++_reads;
		string token = _token;
		if (token.Trim().Length == 0)
		{
			_jwt = null;
			_error = null;
			return;
		}

		(JwtToken? jwt, string? error) = token.Length >= TextLimits.BackgroundChars
			? await Task.Run(() => Decode(token))
			: Decode(token);
		if (read == _reads)
		{
			_jwt = jwt;
			_error = error;
		}
	}

	private static (JwtToken? Token, string? Error) Decode(string token)
	{
		try
		{
			return (JwtDecoder.Decode(token), null);
		}
		catch (JwtFormatException ex)
		{
			return (null, ex.Message);
		}
	}

	private Task ClearAsync() => OnTokenChangedAsync("");

	private void OnSecretChanged(string? value)
	{
		_secret = value ?? "";
		_verification = null;
	}

	private void OnSecretFormatChanged(KeyFormat format)
	{
		_secretFormat = format;
		_verification = null;
	}

	private void OnPublicKeyChanged(string? value)
	{
		_publicKey = value ?? "";
		_verification = null;
	}

	private async Task VerifyAsync()
	{
		if (_jwt is not { } jwt)
		{
			return;
		}

		string secret = _secret;
		KeyFormat format = _secretFormat;
		string publicKey = _publicKey;
		_verifying = true;
		try
		{
			// RSA and EC checks are quick, but a 16384-bit key or a long certificate chain is not, so none run on the renderer.
			_verification = await Task.Run(() => Family == JwtFamily.Hmac ? CheckSecret(jwt, secret, format) : CheckKey(jwt, publicKey));
		}
		finally
		{
			_verifying = false;
		}
	}

	private static JwtVerification CheckSecret(JwtToken jwt, string secret, KeyFormat format)
	{
		if (secret.Length == 0)
		{
			return JwtVerification.NotChecked("Type the secret to check with.");
		}

		byte[] key;
		try
		{
			key = SecretKey.Read(secret, format);
		}
		catch (FormatException ex)
		{
			return JwtVerification.NotChecked(ex.Message);
		}

		try
		{
			return JwtVerifier.VerifyHmac(jwt, key);
		}
		finally
		{
			CryptographicOperations.ZeroMemory(key);
		}
	}

	private static JwtVerification CheckKey(JwtToken jwt, string text)
	{
		try
		{
			using JwtPublicKey key = JwtKeyReader.Read(text, jwt.KeyId);
			return JwtVerifier.VerifyPublicKey(jwt, key);
		}
		catch (JwtFormatException ex)
		{
			return JwtVerification.NotChecked(ex.Message);
		}
		catch (Exception ex) when (ex is CryptographicException or FormatException)
		{
			return JwtVerification.NotChecked("The key could not be read.");
		}
	}
}
