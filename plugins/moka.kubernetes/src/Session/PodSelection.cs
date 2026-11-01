namespace Moka.Kubernetes.Session;

/// <summary>
/// A pod handed from one tab of the tool to another: the pods tab knows which one was picked, and the logs and
/// command tabs need the name, the namespace it was listed under, and a container when one is known.
/// </summary>
/// <param name="Pod">The pod's name, as kubectl listed it.</param>
/// <param name="Namespace">The namespace the pod was listed under, or null when the listing named none.</param>
/// <param name="Container">A container of the pod, or null when the receiving tab picks its own.</param>
public sealed record PodSelection(string Pod, string? Namespace, string? Container);
