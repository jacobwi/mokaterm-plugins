using Moka.Red.Core.Icons;
using Mokaterm.UI.Common.Icons;

namespace Moka.Kubernetes.Components;

/// <summary>The icons the plugin draws beyond Mokaterm's, in the same style: stroked paths on a 24 by 24 grid.</summary>
public static class KubernetesIcons
{
	/// <summary>A ship's wheel with eight spokes, the tool itself and the cluster tab.</summary>
	public static readonly MokaIconDefinition Kubernetes = new("mk8s-kubernetes",
		"M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z M12 10.5a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3z M12 3v5 M12 16v5 M3 12h5 M16 12h5 M5.6 5.6l3.5 3.5 M14.9 14.9l3.5 3.5 M18.4 5.6l-3.5 3.5 M9.1 14.9l-3.5 3.5");

	/// <summary>A gauge, one read of the whole cluster: versions, context, nodes.</summary>
	public static readonly MokaIconDefinition Overview = new("mk8s-overview",
		"M12 20a8 8 0 1 1 8-8 M12 12l4.5-3 M18.5 15.5h3 M2.5 15.5h3");

	/// <summary>A hexagon, one pod of many.</summary>
	public static readonly MokaIconDefinition Pod = new("mk8s-pod",
		"M12 3l7.5 4.5v9L12 21l-7.5-4.5v-9z M12 10.5a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3z");

	/// <summary>A page of lines, a log.</summary>
	public static readonly MokaIconDefinition Logs = new("mk8s-logs",
		"M7 3h10v18H7z M10 8h4 M10 12h4 M10 16h4");

	/// <summary>A prompt in a box, one command run to its end.</summary>
	public static readonly MokaIconDefinition Command = new("mk8s-command",
		"M3 5h18v14H3z M7 9l3 3-3 3 M12.5 15H17");

	/// <summary>A circle of two arrows, a rollout moving pods to a new revision.</summary>
	public static readonly MokaIconDefinition Rollout = new("mk8s-rollout",
		"M4.5 12a7.5 7.5 0 1 1 2.2 5.3 M5 21v-4.5h4.5");

	public static readonly MokaIconDefinition Start = MokatermIcons.Play;

	/// <summary>A filled square drawn as a stroke.</summary>
	public static readonly MokaIconDefinition Stop = new("mk8s-stop",
		"M6 6h12v12H6z");
}
