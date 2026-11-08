using System.Globalization;
using Moka.DevTools.Components;
using Moka.DevTools.Ids;
using Mokaterm.UI.Common.Components;

namespace Moka.DevTools.Tool;

/// <summary>New UUIDs (v4, v7) and ULIDs, one or many, and any pasted UUID or ULID taken apart.</summary>
public sealed partial class IdSection : DevToolViewBase
{
	private static readonly IReadOnlyList<EnumOption<IdKind>> Kinds =
	[
		new(IdKind.UuidV4, "UUID v4"),
		new(IdKind.UuidV7, "UUID v7"),
		new(IdKind.Ulid, "ULID"),
	];

	private IdKind _kind = IdKind.UuidV4;
	private string _countText = "1";
	private bool _upper;
	private bool _lowerUlid;
	private bool _hyphens = true;
	private IReadOnlyList<string>? _generated;
	private string? _generatedKind;
	private string? _countError;
	private string _inspectText = "";
	private IdReport? _report;
	private string? _inspectError;

	private string KindHelp => _kind switch
	{
		IdKind.UuidV4 => "122 random bits. Use it where nothing should be read from the id.",
		IdKind.UuidV7 => "Unix time in milliseconds, then random bits, so ids sort by when they were made (RFC 9562).",
		_ => "Unix time in milliseconds and 80 random bits in 26 characters; a batch made in one millisecond counts up, so it sorts in order.",
	};

	private void Generate()
	{
		if (!int.TryParse(_countText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count < 1 || count > IdGenerator.MaxCount)
		{
			_countError = string.Create(CultureInfo.CurrentCulture, $"How many: a whole number from 1 to {IdGenerator.MaxCount:N0}.");
			return;
		}

		_countError = null;
		IReadOnlyList<string> ids = IdGenerator.Generate(_kind, count, _kind == IdKind.Ulid ? !_lowerUlid : _upper, _hyphens, Now);
		_generated = ids;
		_generatedKind = Kinds.First(kind => kind.Value == _kind).Text;
	}

	private void OnInspectChanged(string? value)
	{
		_inspectText = value ?? "";
		_report = null;
		_inspectError = null;
		if (_inspectText.Trim().Length == 0)
		{
			return;
		}

		try
		{
			_report = IdInspector.Inspect(_inspectText);
		}
		catch (FormatException ex)
		{
			_inspectError = ex.Message;
		}
	}
}
