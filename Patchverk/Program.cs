using System.Collections.ObjectModel;
using System.CommandLine;
using System.Text.Json;
using System.Text.RegularExpressions;
using k8s;
using k8s.Models;

namespace Patchverk;

internal static class Program
{
    private static readonly IDictionary<string, ICollection<Patch>> _patchMap = new Dictionary<string, ICollection<Patch>>()
        {
            { Constants.Deployment, new Collection<Patch>() },
            { Constants.Statefulset, new Collection<Patch>() },
            { Constants.Daemonset, new Collection<Patch>() },
            { Constants.Configmap, new Collection<Patch>() },
            { Constants.Secret, new Collection<Patch>() },
            { Constants.Ingress, new Collection<Patch>() }
        };

    internal static async Task Main(string[] args)
    {
        RootCommand rootCommand = new RootCommand
        {
            Name = "Patchverk",
            Description = "Patch Kubernetes resources"
        };

        CancellationTokenSource tokenSource = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            tokenSource.Cancel();
            e.Cancel = true;
        };

        Option<string> input = new Option<string>("--kubeconfig", "The path to kubeconfig") { IsRequired = true };
        Option<string> system = new Option<string>("--system", "The system subfolder to process") { IsRequired = true };
        rootCommand.Add(input);
        rootCommand.Add(system);
        rootCommand.SetHandler(async (input, system) =>
        {
            await ExecuteInternal(input, system, tokenSource.Token).ConfigureAwait(false);
        }, input, system);

        await rootCommand.InvokeAsync(args).ConfigureAwait(false);
    }

    private static async Task ExecuteInternal(string kubeconfig, string targetSystem, CancellationToken token)
    {
        Kubernetes client = await InitClient(kubeconfig, token).ConfigureAwait(false);
        Console.WriteLine("Version check..");
        try
        {
            await client.GetCodeAsync(token).ConfigureAwait(false);
        }
        catch (Exception exc)
        {
            Console.WriteLine($"Error during version check, will not proceed - {exc}");
            return;
        }

        Console.WriteLine("Enumerating target systems..");
        try
        {
            ISet<string> systems = EnumerateSystems();
            if (!systems.Contains(targetSystem))
            {
                Console.WriteLine("No patches were detected for the provided system name, will not proceed");
                return;
            }
        }
        catch (Exception exc)
        {
            Console.WriteLine($"Error enumerating target systems, will not proceed - {exc}");
            return;
        }

        await GeneratePatches(targetSystem, token).ConfigureAwait(false);
        await ApplyPatches(client, token).ConfigureAwait(false);
    }

    private static async Task ApplyPatches(Kubernetes client, CancellationToken token)
    {
        Console.WriteLine("Applying patches..");
        foreach (var resource in Constants.supportedResourceTypes)
            await PatchResource(client, resource, _patchMap[resource], token).ConfigureAwait(false);

        Console.WriteLine("Patches applied");
    }

    private static async Task<Kubernetes> InitClient(string kubeConfigPath, CancellationToken token)
    {
        KubernetesClientConfiguration config = await KubernetesClientConfiguration.BuildConfigFromConfigFileAsync(new FileInfo(kubeConfigPath)).ConfigureAwait(false);
        Kubernetes client = new Kubernetes(config);
        return client;
    }

    private static async Task PatchResource(Kubernetes client, string type, ICollection<Patch> patchMap, CancellationToken token)
    {
        foreach (var patch in patchMap)
        {
            try
            {
                if (type == Constants.Statefulset)
                    await client.PatchNamespacedStatefulSetAsync(patch.V1Patch, patch.Resource, patch.Namespace, cancellationToken: token).ConfigureAwait(false);
                else if (type == Constants.Deployment)
                    await client.PatchNamespacedDeploymentAsync(patch.V1Patch, patch.Resource, patch.Namespace, cancellationToken: token).ConfigureAwait(false);
                else if (type == Constants.Daemonset)
                    await client.PatchNamespacedDaemonSetAsync(patch.V1Patch, patch.Resource, patch.Namespace, cancellationToken: token).ConfigureAwait(false);
                else if (type == Constants.Configmap)
                    await client.PatchNamespacedConfigMapAsync(patch.V1Patch, patch.Resource, patch.Namespace, cancellationToken: token).ConfigureAwait(false);
            }
            catch (Exception exc)
            {
                Console.WriteLine($"Error patching {type} {patch.Resource} - {exc}");
            }
        }
    }
    private static ISet<string> EnumerateSystems()
    {
        ISet<string> systems = new HashSet<string>();
        IEnumerable<string> directories = Directory.EnumerateDirectories(@"patchverk", "*", SearchOption.TopDirectoryOnly)
            .Select(s => new DirectoryInfo(s))
            .Where(s => !s.Name.StartsWith('.'))
            .Where(s => !s.Attributes.HasFlag(FileAttributes.System))
            .Where(s => !s.Attributes.HasFlag(FileAttributes.Hidden))
            .Select(s => s.Name).ToArray();

        foreach (string dir in directories)
        {
            string systemName = Regex.Replace(dir, @"\\|\/|patchverk", "");
            systems.Add(systemName);
        }
        return systems;
    }

    private static async Task GeneratePatches(string system, CancellationToken token)
    {
        IEnumerable<string> files = Directory.EnumerateFiles($"patchverk/{system}/patch", "*", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            string directive = Regex.Replace(file, @"\\|\/", ".");
            string[] segments = directive.Split('.')[^3..];
            if (!Constants.supportedResourceTypes.Contains(segments[0])) { Console.WriteLine($"{segments[0]} patching is not supported yet"); continue; }
            if (segments[^1] == "default") { continue; }
            string payload = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);

            Type k8sResourceType = Constants.resourceTypeLookup[segments[0]];
            GenerateResource(k8sResourceType, payload, segments[1], segments[^1], segments[0]);
        }
    }

    private static void GenerateResource(Type k8sResourceType, string payload, string nameSpace, string resource, string type)
    {
        dynamic patchContent = JsonSerializer.Deserialize(json: payload, returnType: k8sResourceType);
        Patch patch = new Patch() { V1Patch = new V1Patch(patchContent, V1Patch.PatchType.StrategicMergePatch), Type = type, Namespace = nameSpace, Resource = resource };
        _patchMap[type].Add(patch);
    }
}
