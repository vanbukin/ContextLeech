using System;
using System.Collections.Generic;
using System.IO;

namespace ContextLeech.Services.Static.DotnetSolutionDependencies.Models;

public class DependenciesGraph
{
    public DependenciesGraph(Dictionary<FileInfo, HashSet<FileInfo>> upstream, Dictionary<FileInfo, HashSet<FileInfo>> downstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(downstream);
        Upstream = upstream;
        Downstream = downstream;
    }

    public Dictionary<FileInfo, HashSet<FileInfo>> Upstream { get; }
    public Dictionary<FileInfo, HashSet<FileInfo>> Downstream { get; }
}
