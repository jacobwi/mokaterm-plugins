using System.Globalization;
using System.Text;
using System.Text.Json;
using Moka.DevTools.Codecs;
using Moka.DevTools.Data;
using Moka.DevTools.Text;

namespace Moka.DevTools.Jwt;

/// <summary>
/// Reads a compact JWS (three Base64url parts). A five-part JWE is recognized and refused, since its claims are
/// encrypted for someone else. Messages say which part failed and where, and never repeat the token.
/// </summary>
public static class JwtDecoder
{
	/// <summary>The claims that hold a NumericDate, in the order they are listed.</summary>
	public static IReadOnlyList<string> TimeClaims { get; } = ["exp", "nbf", "iat", "auth_time"];

	public static JwtToken Decode(string input)
	{
		ArgumentNullException.ThrowIfNull(input);
		List<string> notes = [];
		string text = input.Trim();
		if (text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			text = text[7..].Trim();
		}

		if (text.Length >= 2 && text[0] is '"' or '\'' && text[^1] == text[0])
		{
			text = text[1..^1];
		}

		if (text.Any(char.IsWhiteSpace))
		{
			text = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
			notes.Add("Spaces and line breaks inside the token were left out.");
		}

		if (text.Length == 0)
		{
			throw new JwtFormatException("Paste a token.");
		}

		if (text.Length > TextLimits.MaxTextChars)
		{
			throw new JwtFormatException("That is far longer than any token.");
		}

		string[] parts = text.Split('.');
		if (parts.Length == 5)
		{
			throw new JwtFormatException(Encrypted(parts[0]));
		}

		if (parts.Length != 3)
		{
			throw new JwtFormatException(string.Create(CultureInfo.InvariantCulture,
				$"A JWT has three parts separated by dots (an encrypted one has five); this has {parts.Length}."));
		}

		byte[] headerBytes = Part(parts[0], "header", notes);
		using JsonDocument header = Json(headerBytes, "header");
		if (header.RootElement.ValueKind != JsonValueKind.Object)
		{
			throw new JwtFormatException("The header is JSON but not an object.");
		}

		string? algorithm = Text(header.RootElement, "alg");
		if (algorithm is null)
		{
			notes.Add("The header names no algorithm (alg).");
		}

		byte[] payloadBytes = Part(parts[1], "payload", notes);
		(string? payloadJson, string? payloadText, List<JwtClaim> claims, List<JwtTime> times) = ReadPayload(payloadBytes, notes);
		byte[] signature = Part(parts[2], "signature", notes);

		return new JwtToken
		{
			HeaderJson = JsonTools.Write(header.RootElement, JsonIndent.TwoSpaces, sortKeys: false, indented: true),
			Algorithm = algorithm,
			Type = Text(header.RootElement, "typ"),
			KeyId = Text(header.RootElement, "kid"),
			PayloadJson = payloadJson,
			PayloadText = payloadText,
			Claims = claims,
			Times = times,
			Signature = signature,
			SignatureText = parts[2],
			SigningInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"),
			Notes = notes,
		};
	}

	private static (string? Json, string? Text, List<JwtClaim> Claims, List<JwtTime> Times) ReadPayload(byte[] bytes, List<string> notes)
	{
		List<JwtClaim> claims = [];
		List<JwtTime> times = [];
		JsonDocument? document = null;
		try
		{
			document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = JsonTools.MaxDepth });
		}
		catch (JsonException)
		{
			// Not JSON: a JWS may carry any payload, so it is shown as it is.
		}

		if (document is null)
		{
			notes.Add("The payload is not JSON, so it has no claims.");
			string text = StrictUtf8.TryDecode(bytes, out string? decoded) ? decoded : HexText.Format(bytes);
			return (null, text, claims, times);
		}

		using (document)
		{
			JsonElement root = document.RootElement;
			string json = JsonTools.Write(root, JsonIndent.TwoSpaces, sortKeys: false, indented: true);
			if (root.ValueKind != JsonValueKind.Object)
			{
				notes.Add("The payload is JSON but not an object, so it has no claims.");
				return (json, null, claims, times);
			}

			foreach (JsonProperty property in root.EnumerateObject())
			{
				claims.Add(new JwtClaim(property.Name, JsonTools.Write(property.Value, JsonIndent.TwoSpaces, sortKeys: false, indented: true), property.Value.ValueKind));
			}

			foreach (string name in TimeClaims)
			{
				if (root.TryGetProperty(name, out JsonElement value))
				{
					times.Add(ReadTime(name, value));
				}
			}

			return (json, null, claims, times);
		}
	}

	private static JwtTime ReadTime(string name, JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Number)
		{
			return new JwtTime(name, null, $"{name} should be a number of seconds since 1970, and is {JsonTools.Describe(value)}.");
		}

		// Past 1e15 seconds (31 million years) no date fits, and scaling to ticks could overflow.
		if (!value.TryGetDecimal(out decimal seconds) || Math.Abs(seconds) > 1e15m)
		{
			return new JwtTime(name, null, $"{name} is outside the years 1 to 9999.");
		}

		decimal ticks = Math.Floor(seconds * TimeSpan.TicksPerSecond);
		decimal first = DateTimeOffset.MinValue.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
		decimal last = DateTimeOffset.MaxValue.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
		return ticks < first || ticks > last
			? new JwtTime(name, null, $"{name} is outside the years 1 to 9999.")
			: new JwtTime(name, DateTimeOffset.UnixEpoch.AddTicks((long)ticks), null);
	}

	private static byte[] Part(string part, string name, List<string> notes)
	{
		if (part.Contains('=', StringComparison.Ordinal))
		{
			notes.Add($"The {name} is padded with '=', which JWS leaves out.");
		}

		try
		{
			return Base64Codec.Decode(part, urlSafe: true).Bytes;
		}
		catch (CodecException ex)
		{
			throw new JwtFormatException($"The {name} is not Base64url. {ex.Message}");
		}
		catch (OverflowException)
		{
			throw new JwtFormatException($"The {name} is too large to read.");
		}
	}

	private static JsonDocument Json(byte[] bytes, string name)
	{
		try
		{
			return JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = JsonTools.MaxDepth });
		}
		catch (JsonException ex)
		{
			throw new JwtFormatException($"The {name} is not JSON: {JsonTools.Located(ex, bytes).Message}");
		}
	}

	private static string? Text(JsonElement element, string name) =>
		element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

	private static string Encrypted(string protectedHeader)
	{
		const string Message = "This is an encrypted token (JWE, five parts): its claims are encrypted for the recipient and cannot be read without the recipient's key.";
		try
		{
			using JsonDocument header = JsonDocument.Parse(Base64Codec.Decode(protectedHeader, urlSafe: true).Bytes);
			string? algorithm = Text(header.RootElement, "alg");
			string? encryption = Text(header.RootElement, "enc");
			return algorithm is null && encryption is null ? Message : $"{Message} Its header says alg {algorithm ?? "(none)"}, enc {encryption ?? "(none)"}.";
		}
		catch (Exception ex) when (ex is CodecException or JsonException or InvalidOperationException)
		{
			return Message;
		}
	}
}
