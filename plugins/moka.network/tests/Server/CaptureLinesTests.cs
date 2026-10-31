using System.Text;
using Moka.Network.Server;
using Mokaterm.Abstractions.Commands;

namespace Moka.Network.Tests.Server;

public sealed class CaptureLinesTests
{
	[Fact]
	public void Write_CutsLinesAtTheBreaksTcpdumpWrites()
	{
		CaptureLines lines = new();

		lines.Write(CommandStream.Output, "14:02:11.394502 IP 10.0.0.5.52344 > 93.184.216.34.443: Flags [S], length 0\n"u8);
		lines.Write(CommandStream.Output, "14:02:11.412884 IP 93.184.216.34.443 > 10.0.0.5.52344: Flags [S.], length 0\r\n"u8);

		Assert.Equal(2, lines.Count);
		Assert.Equal(2, lines.Total);
		Assert.Equal("10.0.0.5:52344", lines.Snapshot()[0].Parsed.Source);
		Assert.Equal(0, lines.Dropped);
	}

	[Fact]
	public void Write_HoldsALineThatArrivesInPieces()
	{
		CaptureLines lines = new();
		byte[] whole = Encoding.UTF8.GetBytes("14:02:12.001123 IP6 2001:db8::1.443 > 2001:db8::2.52344: Flags [P.], length 530\n");

		lines.Write(CommandStream.Output, whole.AsSpan(0, 20).ToArray());
		lines.Write(CommandStream.Output, whole.AsSpan(20, 8).ToArray());
		lines.Write(CommandStream.Output, whole.AsSpan(28).ToArray());

		CaptureEntry entry = Assert.Single(lines.Snapshot());
		Assert.Equal("14:02:12.001123", entry.Parsed.Time);
		Assert.Equal("[2001:db8::1]:443", entry.Parsed.Source);
		Assert.Equal("530", entry.Parsed.Length);
	}

	[Fact]
	public void Write_DecodesACharacterSplitAcrossPieces()
	{
		CaptureLines lines = new();
		byte[] whole = Encoding.UTF8.GetBytes("14:02:12.000000 IP6 2001:db8::é.443: Flags [.], length 0\n");

		lines.Write(CommandStream.Output, whole.AsSpan(0, 27).ToArray());
		lines.Write(CommandStream.Output, whole.AsSpan(27).ToArray());

		Assert.Contains("é", Assert.Single(lines.Snapshot()).Raw, StringComparison.Ordinal);
	}

	[Fact]
	public void Write_KeepsTheLineStillBeingWrittenForTheSnapshotAlone()
	{
		CaptureLines lines = new();

		lines.Write(CommandStream.Output, "14:02:11.394502 IP 10.0.0.5.52344 > 93.184.216.34.443: Flags [S], length 0\n"u8);
		lines.Write(CommandStream.Output, "14:02:11.4"u8);

		Assert.Equal(1, lines.Count);
		Assert.Equal(2, lines.Snapshot().Count);
		lines.Complete();
		Assert.Equal(2, lines.Count);
		Assert.Equal("14:02:11.4", lines.Snapshot()[1].Raw);
	}

	[Fact]
	public void Write_HearsStderrWithoutGatheringIt()
	{
		CaptureLines lines = new();
		int before = lines.Version;

		lines.Write(CommandStream.Error, "tcpdump: listening on eth0\n"u8);

		Assert.Equal(0, lines.Count);
		Assert.Equal(before, lines.Version);
	}

	[Fact]
	public void Write_DropsTheOldestWhenTheRingWrapsAndCountsThem()
	{
		CaptureLines lines = new();

		for (int index = 0; index < CaptureLines.Capacity + 250; index++)
		{
			lines.Write(CommandStream.Output, Encoding.UTF8.GetBytes($"14:02:11.{index % 1000:d3} IP 10.0.0.1.{index} > 10.0.0.2.80: Flags [.], length 0\n"));
		}

		Assert.Equal(CaptureLines.Capacity, lines.Count);
		Assert.Equal(250, lines.Dropped);
		Assert.Equal(CaptureLines.Capacity + 250, lines.Total);
		Assert.Equal($"10.0.0.1:{CaptureLines.Capacity + 249}", lines.Snapshot()[^1].Parsed.Source);
	}

	[Fact]
	public void Write_CutsOneRunawayLineIntoPiecesOfTheMaxLength()
	{
		CaptureLines lines = new();

		lines.Write(CommandStream.Output, Encoding.UTF8.GetBytes(new string('x', CaptureLines.MaxLineCharacters + 50) + "\n"));

		Assert.Equal(2, lines.Count);
		Assert.All(lines.Snapshot(), entry => Assert.True(entry.Raw.Length <= CaptureLines.MaxLineCharacters));
	}

	[Fact]
	public void Version_MovesOnEveryChange_SoAViewKnowsWhenToLook()
	{
		CaptureLines lines = new();
		int before = lines.Version;

		lines.Write(CommandStream.Output, "one\n"u8);
		int after = lines.Version;
		lines.Complete();

		Assert.Equal(before + 1, after);
		Assert.Equal(after + 1, lines.Version);
	}

	[Fact]
	public void AllText_JoinsTheKeptLinesForACopy()
	{
		CaptureLines lines = new();

		lines.Write(CommandStream.Output, "first\n"u8);
		lines.Write(CommandStream.Output, "second\n"u8);

		Assert.Equal($"first{Environment.NewLine}second{Environment.NewLine}", lines.AllText());
	}
}
