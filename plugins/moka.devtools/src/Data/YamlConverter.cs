using System.Buffers;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Moka.DevTools.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Moka.DevTools.Data;

/// <summary>
/// YAML to JSON and back. Plain scalars are typed by the YAML 1.2 core schema (so "yes" stays a string), merge keys
/// (&lt;&lt;) are applied, and aliases are expanded under a cap, so a document built to grow without end (the billion laughs)
/// is refused instead of filling memory. Anything JSON cannot say (a key that is not a scalar, a duplicate key, .inf) is
/// refused with its line and column.
/// </summary>
public static partial class YamlConverter
{
	/// <summary>The most values the JSON may hold once aliases are expanded.</summary>
	public const int MaxNodes = 1_000_000;

	/// <summary>The most bytes of JSON one conversion writes.</summary>
	public const int MaxOutputBytes = 32 * 1024 * 1024;

	private const string CoreTag = "tag:yaml.org,2002:";

	public static YamlConversion ToJson(string yaml, JsonIndent indent, bool sortKeys)
	{
		ArgumentNullException.ThrowIfNull(yaml);
		List<string> notes = [];
		List<Node> documents = Read(StrictUtf8.WithoutBom(yaml));
		if (documents.Count == 0)
		{
			throw new DataException("There is no YAML document here.");
		}

		ArrayBufferWriter<byte> buffer = new();
		JsonWriterOptions options = new()
		{
			Indented = true,
			IndentCharacter = indent == JsonIndent.Tab ? '\t' : ' ',
			IndentSize = indent switch
			{
				JsonIndent.Tab => 1,
				JsonIndent.FourSpaces => 4,
				_ => 2,
			},
			NewLine = "\n",
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			MaxDepth = JsonTools.MaxDepth + 2,
		};

		Writer state = new(buffer, sortKeys);
		using (Utf8JsonWriter writer = new(buffer, options))
		{
			if (documents.Count == 1)
			{
				state.Write(writer, documents[0], 0);
			}
			else
			{
				notes.Add(string.Create(CultureInfo.InvariantCulture, $"The YAML holds {documents.Count} documents and JSON holds one, so they became the items of an array."));
				writer.WriteStartArray();
				foreach (Node document in documents)
				{
					state.Write(writer, document, 1);
				}

				writer.WriteEndArray();
			}
		}

		if (state.DroppedTags.Count > 0)
		{
			notes.Add($"JSON has no tags, so these were left out and their values kept: {string.Join(", ", state.DroppedTags.Order(StringComparer.Ordinal))}.");
		}

		return new YamlConversion(Encoding.UTF8.GetString(buffer.WrittenSpan), notes);
	}

	public static YamlConversion FromJson(string json, bool sortKeys)
	{
		using JsonDocument document = JsonTools.Parse(json);
		using StringWriter text = new(CultureInfo.InvariantCulture) { NewLine = "\n" };
		EmitterSettings settings = new EmitterSettings()
			.WithBestIndent(2)
			.WithBestWidth(int.MaxValue)
			.WithMaxSimpleKeyLength(1024)
			.WithNewLine("\n");
		Emitter emitter = new(text, settings);
		emitter.Emit(new StreamStart());
		emitter.Emit(new DocumentStart());
		Emit(emitter, document.RootElement, sortKeys);
		emitter.Emit(new DocumentEnd(isImplicit: true));
		emitter.Emit(new StreamEnd());
		return new YamlConversion(text.ToString(), []);
	}

	/// <summary>Checks that the text is YAML; the documents' count is the answer, an error says where it is not.</summary>
	public static int Validate(string yaml)
	{
		ArgumentNullException.ThrowIfNull(yaml);
		return Read(StrictUtf8.WithoutBom(yaml)).Count;
	}

	private static List<Node> Read(string yaml)
	{
		Parser parser = new(new StringReader(yaml));
		List<Node> documents = [];
		ReadState state = new();
		try
		{
			parser.Consume<StreamStart>();
			while (!parser.Accept<StreamEnd>(out _))
			{
				parser.Consume<DocumentStart>();
				state.Anchors.Clear();
				documents.Add(ReadNode(parser, state, 0));
				parser.Consume<DocumentEnd>();
			}
		}
		catch (YamlException ex)
		{
			throw new DataException(Plain(ex.Message), At(ex.Start));
		}
		catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IndexOutOfRangeException)
		{
			// YamlDotNet's scanner throws these on some broken input instead of a YamlException; the last value it did
			// read is the nearest place to point at.
			throw new DataException("The YAML cannot be read past here", At(state.Last));
		}

		return documents;
	}

	private static Node ReadNode(Parser parser, ReadState state, int depth)
	{
		ParsingEvent current = parser.Current ?? throw new DataException("The YAML ends in the middle of a value.");
		state.Last = current.End;
		Dictionary<string, Node> anchors = state.Anchors;
		if (depth > JsonTools.MaxDepth)
		{
			throw new DataException(string.Create(CultureInfo.InvariantCulture, $"The YAML nests deeper than {JsonTools.MaxDepth} levels."), At(current.Start));
		}

		switch (current)
		{
			case AnchorAlias alias:
				parser.MoveNext();
				return anchors.TryGetValue(alias.Value.Value, out Node? target)
					? target
					: throw new DataException($"The alias *{alias.Value.Value} names no anchor defined before it.", At(alias.Start));
			case Scalar scalar:
				parser.MoveNext();
				return Anchor(anchors, scalar, new Node(NodeKind.Scalar, scalar.Start) { Value = scalar.Value, Style = scalar.Style, Tag = TagOf(scalar) });
			case SequenceStart start:
			{
				parser.MoveNext();
				Node sequence = Anchor(anchors, start, new Node(NodeKind.Sequence, start.Start) { Tag = TagOf(start) });
				while (!parser.TryConsume<SequenceEnd>(out _))
				{
					sequence.Items.Add(ReadNode(parser, state, depth + 1));
				}

				return sequence;
			}
			case MappingStart start:
			{
				parser.MoveNext();
				Node mapping = Anchor(anchors, start, new Node(NodeKind.Mapping, start.Start) { Tag = TagOf(start) });
				while (!parser.TryConsume<MappingEnd>(out _))
				{
					Node key = ReadNode(parser, state, depth + 1);
					Node value = ReadNode(parser, state, depth + 1);
					mapping.Entries.Add((key, value));
				}

				return mapping;
			}
			default:
				throw new DataException($"Unexpected {current.GetType().Name} in the YAML.", At(current.Start));
		}
	}

	private static Node Anchor(Dictionary<string, Node> anchors, NodeEvent nodeEvent, Node node)
	{
		if (!nodeEvent.Anchor.IsEmpty)
		{
			anchors[nodeEvent.Anchor.Value] = node;
		}

		return node;
	}

	private static string? TagOf(NodeEvent nodeEvent) => nodeEvent.Tag.IsEmpty || nodeEvent.Tag.IsNonSpecific ? null : nodeEvent.Tag.Value;

	private static void Emit(Emitter emitter, JsonElement element, bool sortKeys)
	{
		switch (element.ValueKind)
		{
			case JsonValueKind.Object:
			{
				List<JsonProperty> properties = [.. element.EnumerateObject()];
				if (sortKeys)
				{
					properties = [.. properties.OrderBy(property => property.Name, StringComparer.Ordinal)];
				}

				emitter.Emit(new MappingStart(AnchorName.Empty, TagName.Empty, isImplicit: true, properties.Count == 0 ? MappingStyle.Flow : MappingStyle.Block));
				foreach (JsonProperty property in properties)
				{
					EmitString(emitter, property.Name, key: true);
					Emit(emitter, property.Value, sortKeys);
				}

				emitter.Emit(new MappingEnd());
				break;
			}
			case JsonValueKind.Array:
				emitter.Emit(new SequenceStart(AnchorName.Empty, TagName.Empty, isImplicit: true, element.GetArrayLength() == 0 ? SequenceStyle.Flow : SequenceStyle.Block));
				foreach (JsonElement item in element.EnumerateArray())
				{
					Emit(emitter, item, sortKeys);
				}

				emitter.Emit(new SequenceEnd());
				break;
			case JsonValueKind.String:
				EmitString(emitter, element.GetString() ?? "", key: false);
				break;
			case JsonValueKind.Number:
				EmitPlain(emitter, element.GetRawText());
				break;
			case JsonValueKind.True:
				EmitPlain(emitter, "true");
				break;
			case JsonValueKind.False:
				EmitPlain(emitter, "false");
				break;
			default:
				EmitPlain(emitter, "null");
				break;
		}
	}

	private static void EmitPlain(Emitter emitter, string value) =>
		emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, value, ScalarStyle.Plain, isPlainImplicit: true, isQuotedImplicit: false));

	/// <summary>
	/// A string as YAML: quoted when plain text would read back as another type (true, 12, null, and the YAML 1.1 words
	/// yes, no, on and off that older readers still turn into booleans), a block when it spans lines, plain otherwise.
	/// The emitter quotes on its own whatever plain text cannot hold.
	/// </summary>
	private static void EmitString(Emitter emitter, string value, bool key)
	{
		ScalarStyle style = !key && value.Contains('\n', StringComparison.Ordinal)
			? ScalarStyle.Literal
			: ReadsAsOtherType(value) ? ScalarStyle.DoubleQuoted : ScalarStyle.Plain;
		emitter.Emit(new Scalar(AnchorName.Empty, TagName.Empty, value, style, isPlainImplicit: true, isQuotedImplicit: true));
	}

	private static bool ReadsAsOtherType(string value) =>
		value.Length == 0
		|| char.IsWhiteSpace(value[0])
		|| char.IsWhiteSpace(value[^1])
		|| NullPattern().IsMatch(value)
		|| BoolPattern().IsMatch(value)
		|| Yaml11BoolPattern().IsMatch(value)
		|| IntPattern().IsMatch(value)
		|| OctalPattern().IsMatch(value)
		|| HexPattern().IsMatch(value)
		|| FloatPattern().IsMatch(value)
		|| InfinityPattern().IsMatch(value)
		|| NotANumberPattern().IsMatch(value);

	private static TextPosition At(Mark mark) => new((int)Math.Max(1, mark.Line), (int)Math.Max(1, mark.Column));

	// YamlDotNet starts its messages with the span, "(Line: 3, Col: 5, Idx: 20) - (Line: 3, Col: 6, Idx: 21): ".
	private static string Plain(string message)
	{
		int cut = message.IndexOf("): ", StringComparison.Ordinal);
		string text = cut >= 0 && message.StartsWith('(') ? message[(cut + 3)..] : message;
		return text.Trim().TrimEnd('.');
	}

	[GeneratedRegex("^(~|null|Null|NULL)$", RegexOptions.CultureInvariant)]
	private static partial Regex NullPattern();

	[GeneratedRegex("^(true|True|TRUE|false|False|FALSE)$", RegexOptions.CultureInvariant)]
	private static partial Regex BoolPattern();

	[GeneratedRegex("^(y|Y|yes|Yes|YES|n|N|no|No|NO|on|On|ON|off|Off|OFF)$", RegexOptions.CultureInvariant)]
	private static partial Regex Yaml11BoolPattern();

	[GeneratedRegex("^[-+]?[0-9]+$", RegexOptions.CultureInvariant)]
	private static partial Regex IntPattern();

	[GeneratedRegex("^0o[0-7]+$", RegexOptions.CultureInvariant)]
	private static partial Regex OctalPattern();

	[GeneratedRegex("^0x[0-9a-fA-F]+$", RegexOptions.CultureInvariant)]
	private static partial Regex HexPattern();

	[GeneratedRegex(@"^[-+]?(\.[0-9]+|[0-9]+(\.[0-9]*)?)([eE][-+]?[0-9]+)?$", RegexOptions.CultureInvariant)]
	private static partial Regex FloatPattern();

	[GeneratedRegex(@"^[-+]?\.(inf|Inf|INF)$", RegexOptions.CultureInvariant)]
	private static partial Regex InfinityPattern();

	[GeneratedRegex(@"^\.(nan|NaN|NAN)$", RegexOptions.CultureInvariant)]
	private static partial Regex NotANumberPattern();

	private enum NodeKind
	{
		Scalar,
		Sequence,
		Mapping,
	}

	/// <summary>One document's anchors, and the end of the last event read, for errors YamlDotNet gives no place for.</summary>
	private sealed class ReadState
	{
		public Dictionary<string, Node> Anchors { get; } = new(StringComparer.Ordinal);

		public Mark Last { get; set; } = Mark.Empty;
	}

	private sealed class Node(NodeKind kind, Mark start)
	{
		public NodeKind Kind { get; } = kind;

		public Mark Start { get; } = start;

		public string Value { get; init; } = "";

		public ScalarStyle Style { get; init; }

		public string? Tag { get; init; }

		public List<Node> Items { get; } = [];

		public List<(Node Key, Node Value)> Entries { get; } = [];
	}

	/// <summary>Writes the tree as JSON, counting values and bytes so aliases cannot expand past the caps.</summary>
	private sealed class Writer(ArrayBufferWriter<byte> buffer, bool sortKeys)
	{
		private int _nodes;

		public SortedSet<string> DroppedTags { get; } = new(StringComparer.Ordinal);

		public void Write(Utf8JsonWriter writer, Node node, int depth)
		{
			if (++_nodes > MaxNodes || buffer.WrittenCount + writer.BytesPending > MaxOutputBytes)
			{
				throw new DataException("With its aliases expanded this document is too large to convert.");
			}

			if (depth > JsonTools.MaxDepth)
			{
				throw new DataException(string.Create(CultureInfo.InvariantCulture, $"With its aliases expanded this nests deeper than {JsonTools.MaxDepth} levels; an alias may point into itself."), At(node.Start));
			}

			NoteTag(node);
			switch (node.Kind)
			{
				case NodeKind.Sequence:
					writer.WriteStartArray();
					foreach (Node item in node.Items)
					{
						Write(writer, item, depth + 1);
					}

					writer.WriteEndArray();
					break;
				case NodeKind.Mapping:
					WriteMapping(writer, node, depth);
					break;
				default:
					WriteScalar(writer, node);
					break;
			}
		}

		private void WriteMapping(Utf8JsonWriter writer, Node node, int depth)
		{
			List<(string Key, Node Value)> entries = [];
			HashSet<string> seen = new(StringComparer.Ordinal);
			List<Node> merges = [];
			foreach ((Node key, Node value) in node.Entries)
			{
				if (key.Kind != NodeKind.Scalar)
				{
					throw new DataException("A key here is a list or a mapping, and JSON keys can only be text.", At(key.Start));
				}

				if (key.Style == ScalarStyle.Plain && key.Value == "<<" && key.Tag is null)
				{
					merges.Add(value);
					continue;
				}

				if (!seen.Add(key.Value))
				{
					throw new DataException($"The key \"{key.Value}\" appears twice in one mapping.", At(key.Start));
				}

				entries.Add((key.Value, value));
			}

			// A merge key adds the keys this mapping does not set itself; with a list, earlier mappings win.
			foreach (Node merge in merges)
			{
				IEnumerable<Node> sources = merge.Kind == NodeKind.Sequence ? merge.Items : [merge];
				foreach (Node source in sources)
				{
					if (source.Kind != NodeKind.Mapping)
					{
						throw new DataException("A merge key (<<) needs a mapping or a list of mappings.", At(source.Start));
					}

					foreach ((Node key, Node value) in source.Entries)
					{
						if (key.Kind == NodeKind.Scalar && seen.Add(key.Value))
						{
							entries.Add((key.Value, value));
						}
					}
				}
			}

			if (sortKeys)
			{
				entries = [.. entries.OrderBy(entry => entry.Key, StringComparer.Ordinal)];
			}

			writer.WriteStartObject();
			foreach ((string key, Node value) in entries)
			{
				writer.WritePropertyName(key);
				Write(writer, value, depth + 1);
			}

			writer.WriteEndObject();
		}

		private static void WriteScalar(Utf8JsonWriter writer, Node node)
		{
			string value = node.Value;
			string? tag = node.Tag is { } full && full.StartsWith(CoreTag, StringComparison.Ordinal) ? full[CoreTag.Length..] : null;
			if (tag is "str" or "binary" or "timestamp" || (tag is null && node.Style != ScalarStyle.Plain))
			{
				writer.WriteStringValue(value);
				return;
			}

			if (tag is "null" || (tag is null && (value.Length == 0 || NullPattern().IsMatch(value))))
			{
				writer.WriteNullValue();
				return;
			}

			if (tag is "bool" || (tag is null && BoolPattern().IsMatch(value)))
			{
				writer.WriteBooleanValue(BoolPattern().IsMatch(value)
					? value[0] is 't' or 'T'
					: throw new DataException($"\"{value}\" is tagged as a boolean but is not one.", At(node.Start)));
				return;
			}

			if (IntPattern().IsMatch(value) || OctalPattern().IsMatch(value) || HexPattern().IsMatch(value))
			{
				if (tag is null or "int" or "float")
				{
					writer.WriteRawValue(Integer(value), skipInputValidation: true);
					return;
				}
			}
			else if (FloatPattern().IsMatch(value) && tag is null or "float")
			{
				writer.WriteRawValue(Float(value), skipInputValidation: true);
				return;
			}
			else if ((InfinityPattern().IsMatch(value) || NotANumberPattern().IsMatch(value)) && tag is null or "float")
			{
				throw new DataException($"JSON has no way to write {value}.", At(node.Start));
			}
			else if (tag is "int" or "float")
			{
				throw new DataException($"\"{value}\" is tagged as a number but is not one.", At(node.Start));
			}

			writer.WriteStringValue(value);
		}

		private void NoteTag(Node node)
		{
			if (node.Tag is { } tag && !tag.StartsWith(CoreTag, StringComparison.Ordinal))
			{
				DroppedTags.Add(tag.StartsWith('!') ? tag : "!" + tag);
			}
		}

		private static string Integer(string value)
		{
			BigInteger number = value.StartsWith("0x", StringComparison.Ordinal)
				? BigInteger.Parse("0" + value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
				: value.StartsWith("0o", StringComparison.Ordinal)
					? value[2..].Aggregate(BigInteger.Zero, (total, digit) => (total * 8) + (digit - '0'))
					: BigInteger.Parse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
			return number.ToString(CultureInfo.InvariantCulture);
		}

		// JSON wants a digit on both sides of the point and no leading plus or zeros: +.5 is 0.5, 1. is 1.0, 007 is 7.
		private static string Float(string value)
		{
			string sign = value.StartsWith('-') ? "-" : "";
			string body = value.TrimStart('+', '-');
			int exponentAt = body.IndexOfAny(['e', 'E']);
			string mantissa = exponentAt < 0 ? body : body[..exponentAt];
			string exponent = exponentAt < 0 ? "" : body[exponentAt..];
			int point = mantissa.IndexOf('.', StringComparison.Ordinal);
			string whole = point < 0 ? mantissa : mantissa[..point];
			string fraction = point < 0 ? "" : mantissa[(point + 1)..];
			whole = whole.TrimStart('0');
			if (whole.Length == 0)
			{
				whole = "0";
			}

			string text = point < 0 ? whole : $"{whole}.{(fraction.Length == 0 ? "0" : fraction)}";
			return sign + text + exponent;
		}
	}
}
