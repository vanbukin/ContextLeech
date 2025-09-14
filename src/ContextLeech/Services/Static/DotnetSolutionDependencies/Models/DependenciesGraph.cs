using System;
using System.Collections.Generic;

namespace ContextLeech.Services.Static.DotnetSolutionDependencies.Models;

public class DependenciesGraph
{
    public DependenciesGraph(Dictionary<string, HashSet<string>> upstream, Dictionary<string, HashSet<string>> downstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(downstream);
        Upstream = upstream;
        Downstream = downstream;
    }

    public Dictionary<string, HashSet<string>> Upstream { get; }
    public Dictionary<string, HashSet<string>> Downstream { get; }
}
