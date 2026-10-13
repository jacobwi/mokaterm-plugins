using System.Globalization;
using Moka.DevTools.Text;

namespace Moka.DevTools.Codecs;

/// <summary>Runs one <see cref="CodecRequest"/>: the one place that decides what each codec takes and gives.</summary>
public static class CodecRunner
{
	public static CodecOutput Run(CodecRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		if (request.FileBytes is null && request.Text.Length > TextLimits.MaxTextChars)
		{
			throw new CodecException(string.Create(CultureInfo.InvariantCulture, $"The text is longer than {TextLimits.MaxTextChars / (1024 * 1024)} MiB, the most this works on."));
		}

		return request.Direction == CodecDirection.Encode ? Encode(request) : Decode(request);
	}

	private static CodecOutput Encode(CodecRequest request) => request.Kind switch
	{
		CodecKind.Base64 => new CodecOutput(Base64Codec.Encode(BytesOf(request), urlSafe: false, request.Padding), null),
		CodecKind.Base64Url => new CodecOutput(Base64Codec.Encode(BytesOf(request), urlSafe: true, request.Padding), null),
		CodecKind.Hex => new CodecOutput(HexCodec.Encode(BytesOf(request), request.UpperHex, request.Separator), null),
		CodecKind.Url => new CodecOutput(UrlCodec.Encode(TextOf(request), request.UrlMode), null),
		_ => new CodecOutput(HtmlCodec.Encode(TextOf(request), request.EncodeNonAscii), null),
	};

	private static CodecOutput Decode(CodecRequest request)
	{
		string text = TextOf(request);
		switch (request.Kind)
		{
			case CodecKind.Base64:
			case CodecKind.Base64Url:
				(byte[] bytes, string? note) = Base64Codec.Decode(text, request.Kind == CodecKind.Base64Url);
				return new CodecOutput(null, bytes, note);
			case CodecKind.Hex:
				return new CodecOutput(null, HexCodec.Decode(text));
			case CodecKind.Url:
				return new CodecOutput(null, UrlCodec.Decode(text, request.UrlMode, request.PlusIsSpace));
			default:
				(string decoded, string? htmlNote) = HtmlCodec.Decode(text);
				return new CodecOutput(decoded, null, htmlNote);
		}
	}

	private static byte[] BytesOf(CodecRequest request)
	{
		if (request.FileBytes is { } bytes)
		{
			return bytes;
		}

		try
		{
			return StrictUtf8.Encode(request.Text);
		}
		catch (FormatException ex)
		{
			throw new CodecException(ex.Message);
		}
	}

	private static string TextOf(CodecRequest request)
	{
		if (request.FileBytes is not { } bytes)
		{
			return request.Text;
		}

		return StrictUtf8.TryDecode(bytes, out string? text)
			? StrictUtf8.WithoutBom(text)
			: throw new CodecException("This works on text, and the file is not UTF-8 text.");
	}
}
