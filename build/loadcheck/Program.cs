using System.Text.Json;
using Mokaterm.Gallery.LoadCheck;

// --host-sdk: prints the Mokaterm major.minor this build loads, so the toolchain can compare it with mokaterm.json.
if (args is ["--host-sdk"])
{
	Console.WriteLine(LoadCheckRun.HostSdk);
	return 0;
}

// --runtime-assemblies: the assemblies of .NET itself, which every host has, so no plugin needs to carry one.
if (args is ["--runtime-assemblies"])
{
	foreach (string file in Directory.EnumerateFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll").Order(StringComparer.OrdinalIgnoreCase))
	{
		Console.WriteLine(Path.GetFileNameWithoutExtension(file));
	}

	return 0;
}

if (!LoadCheckArguments.TryParse(args, out LoadCheckArguments? arguments, out string? usage))
{
	await Console.Error.WriteLineAsync(usage);
	return 2;
}

LoadCheckReport report = await new LoadCheckRun(arguments).RunAsync();

foreach (HostReport host in report.Hosts)
{
	Console.WriteLine($"{host.Host} ({host.Mode}): {host.Status}{(host.Problem is null ? "" : " - " + host.Problem)}");
	foreach (string line in host.Registrations)
	{
		Console.WriteLine($"  {line}");
	}
}

foreach (string warning in report.Warnings)
{
	Console.WriteLine($"warning: {warning}");
}

foreach (string problem in report.Problems)
{
	Console.WriteLine($"problem: {problem}");
}

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(arguments.ResultPath))!);
await File.WriteAllTextAsync(arguments.ResultPath, JsonSerializer.Serialize(report, LoadCheckReport.JsonOptions));
return report.Ok ? 0 : 1;
