using System;
using k8s.Models;

namespace Patchverk;

internal class Patch
{
    internal string Type { get; set; }
    internal V1Patch V1Patch { get; set; }
    internal string Namespace { get; set; }
    internal string Resource { get; set; }
}
