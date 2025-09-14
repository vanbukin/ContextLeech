using System;
using ContextLeech.Services.Static.DotnetSolutionDependenciesAnalyzer.Models;
using ContextLeech.Services.Static.ProjectScanner.Models;

namespace ContextLeech.Services.Static.Metadata.Models;

public class ProjectMetadata
{
    public ProjectMetadata(Project project, DependenciesGraph dependenciesGraph)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(dependenciesGraph);
        Project = project;
        DependenciesGraph = dependenciesGraph;
    }

    public Project Project { get; }
    public DependenciesGraph DependenciesGraph { get; }
}
