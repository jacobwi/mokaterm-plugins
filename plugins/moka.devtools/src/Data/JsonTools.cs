using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Moka.DevTools.Text;

namespace Moka.DevTools.Data;

/// <summary>
/// JSON as RFC 8259 has it: no comments, no trailing commas. Formatting keeps every number exactly as written and every
/// key in its order unless sorting is asked for; errors carry the line and column the parser stopped at.
/// </summary>
public static class JsonTools
{
	/// <summary>How deep objects and arrays may nest. Every walk over a document recurses, so this is also its stack.</summary>
	public const int MaxDepth = 256;

	private const int MaxWarnings = 20;

	private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = MaxDepth };

	/// <summary>Parses the text, which the caller disposes. Throws <see cref="DataException"/> with the place it stopped.</summary>
	public static JsonDocument Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		byte[] utf8 = Utf8Positions.Encode(StrictUtf8.WithoutBom(text));
		if (IsBlank(utf8))
		{
			throw new DataException("There is no JSON here.");
		}

		try
		{
			return JsonDocument.Parse(utf8, ReadOptions);
		}
		catch (JsonException ex)
		{
			throw Located(ex, utf8);
		}
	}

	public static string Format(string text, JsonIndent indent, bool sortKeys)
	{
		using JsonDocument document = Parse(text);
		return Write(document.RootElement, indent, sortKeys, indented: true);
	}

	public static string Minify(string text, bool sortKeys)
	{
		using JsonDocument document = Parse(text);
		return Write(document.RootElement, JsonIndent.TwoSpaces, sortKeys, indented: false);
	}

	/// <summary>
	/// What the document holds, and keys that appear twice in one object: legal JSON, but most readers keep only the last,
	/// so it is usually a mistake.
	/// </summary>
	public static JsonSummary Validate(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		string description;
		using (JsonDocument document = Parse(text))
		{
			description = Describe(document.RootElement);
		}

		return new JsonSummary(description, DuplicateKeys(Utf8Positions.Encode(StrictUtf8.WithoutBom(text))));
	}

	/// <summary>"an object with 3 keys", "an array of 10 items", "a string".</summary>
	public static string Describe(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.Object => Count(element.EnumerateObject().Count(), "an object with {0} key", "an object with {0} keys", "an empty object"),
		JsonValueKind.Array => Count(element.GetArrayLength(), "an array of {0} item", "an array of {0} items", "an empty array"),
		JsonValueKind.String => "a string",
		JsonValueKind.Number => "a number",
		JsonValueKind.True or JsonValueKind.False => "a boolean",
		_ => "null",
	};

	/// <summary>Writes an element with the plugin's settings: relaxed escaping (text stays readable), LF line ends.</summary>
	public static string Write(JsonElement element, JsonIndent indent, bool sortKeys, bool indented)
	{
		ArrayBufferWriter<byte> buffer = new();
		JsonWriterOptions options = new()
		{
			Indented = indented,
			IndentCharacter = indent == JsonIndent.Tab ? '\t' : ' ',
			IndentSize = indent switch
			{
				JsonIndent.Tab => 1,
				JsonIndent.FourSpaces => 4,
				_ => 2,
			},
			NewLine = "\n",
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			MaxDepth = MaxDepth + 1,
		};

		try
		{
			using (Utf8JsonWriter writer = new(buffer, options))
			{
				WriteElement(writer, element, sortKeys);
			}
		}
		catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
		{
			throw new DataException("A string holds an escaped half of a character pair, which cannot be written out as text.", ex);
		}

		return Encoding.UTF8.GetString(buffer.WrittenSpan);
	}

	/// <summary>Writes one element, its object keys sorted ordinally when asked; the sort is stable, so duplicates keep their order.</summary>
	internal static void WriteElement(Utf8JsonWriter writer, JsonElement element, bool sortKeys)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
				writer.WriteStartObject();
				IEnumerable<JsonProperty> properties = element.EnumerateObject();
				if (sortKeys)
				{
					properties = properties.OrderBy(property => property.Name, StringComparer.Ordinal);
				}

				foreach (JsonProperty property in properties)
				{
					writer.WritePropertyName(property.Name);
					WriteElement(writer, property.Value, sortKeys);
				}

				writer.WriteEndObject();
				break;
			case JsonValueKind.Array:
				writer.WriteStartArray();
				foreach (JsonElement item in element.EnumerateArray())
				{
					WriteElement(writer, item, sortKeys);
				}

				writer.WriteEndArray();
				break;
			default:
				element.WriteTo(writer);
				break;
		}
	}

	internal static DataException Located(JsonException exception, ReadOnlySpan<byte> utf8)
	{
		string message = exception.Message;
		foreach (string marker in new[] { " LineNumber:", " Path:" })
		{
			int cut = message.IndexOf(marker, StringComparison.Ordinal);
			if (cut > 0)
			{
				message = message[..cut];
			}
		}

		message = message.Trim().TrimEnd('.');
		return exception.LineNumber is { } line && exception.BytePositionInLine is { } column
			? new DataException(message, Utf8Positions.OfLine(utf8, line, column))
			: new DataException(message);
	}

	private static List<string> DuplicateKeys(byte[] utf8)
	{
		List<string> warnings = [];
		Stack<HashSet<string>?> objects = new();
		Utf8JsonReader reader = new(utf8, new JsonReaderOptions { MaxDepth = MaxDepth });
		while (reader.Read() && warnings.Count < MaxWarnings)
		{
			switch (reader.TokenType)
			{
				case JsonTokenType.StartObject:
					objects.Push(new HashSet<string>(StringComparer.Ordinal));
					break;
				case JsonTokenType.StartArray:
					objects.Push(null);
					break;
				case JsonTokenType.EndObject:
				case JsonTokenType.EndArray:
					objects.Pop();
					break;
				case JsonTokenType.PropertyName:
					string name = reader.GetString() ?? "";
					if (objects.Peek() is { } keys && !keys.Add(name))
					{
						TextPosition at = Utf8Positions.OfOffset(utf8, reader.TokenStartIndex);
						warnings.Add($"The key \"{Shorten(name)}\" appears again in the same object at {at}; most readers keep only the last one.");
					}

					break;
			}
		}

		return warnings;
	}

	private static string Shorten(string text) => text.Length > 60 ? text[..60] + "..." : text;

	private static bool IsBlank(ReadOnlySpan<byte> utf8)
	{
		foreach (byte value in utf8)
		{
			if (value is not ((byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n'))
			{
				return false;
			}
		}

		return true;
	}

	private static string Count(int count, string one, string many, string none) => count switch
	{
		0 => none,
		1 => string.Format(CultureInfo.InvariantCulture, one, count),
		_ => string.Format(CultureInfo.InvariantCulture, many, count.ToString("N0", CultureInfo.InvariantCulture)),
	};
}
