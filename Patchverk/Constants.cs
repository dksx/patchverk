using System;
using System.Collections.ObjectModel;
using k8s.Models;

namespace Patchverk;

internal static class Constants
{
    internal const string Deployment = "Deployment";
    internal const string Statefulset = "Statefulset";
    internal const string Daemonset = "Daemonset";
    internal const string Configmap = "Configmap";
    internal const string Secret = "Secret";
    internal const string Ingress = "Ingress";

    internal static readonly ISet<string> supportedResourceTypes = new HashSet<string>() { Configmap, Deployment, Statefulset, Daemonset, Ingress };

    internal static readonly ReadOnlyDictionary<string, Type> resourceTypeLookup = new ReadOnlyDictionary<string, Type>(new Dictionary<string, Type>()
        {
            { Deployment, typeof(V1Deployment) },
            { Statefulset, typeof(V1StatefulSet) },
            { Daemonset, typeof(V1DaemonSet) },
            { Configmap, typeof(V1ConfigMap) },
            { Secret, typeof(V1Secret) },
            { Ingress, typeof(V1Ingress) }
        });
}

