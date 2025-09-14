using System;
using System.IO;
using System.Threading.Tasks;
using ContextLeech.Services.Static.DotnetSolutionDependenciesAnalyzer;
using ContextLeech.Services.Static.DotnetSolutionDependenciesAnalyzer.Models;
using ContextLeech.Services.Static.Metadata.Models;
using ContextLeech.Services.Static.ProjectScanner;

namespace ContextLeech.Services.Static.Metadata;

public static class StaticMetadataService
{
    public static async Task<ProjectMetadata> LoadMetadataAsync(string pathToRepository)
    {
        var projectRoot = new DirectoryInfo(pathToRepository);

        if (!projectRoot.Exists)
        {
            throw new ArgumentException("Invalid path to repository", nameof(pathToRepository));
        }

        if (!StaticProjectScanner.TryReadExisting(projectRoot, out var project))
        {
            project = StaticProjectScanner.Scan(projectRoot);
            StaticProjectScanner.Save(project);
        }

        if (!StaticDotnetSolutionDependenciesAnalyzer.TryReadExisting(projectRoot, out var graph))
        {
            var solutions = project.GetDotnetSolutions();
            if (solutions.Length > 0)
            {
                var accumulatorGraph = new DependenciesGraph(projectRoot, [], []);
                foreach (var solution in solutions)
                {
                    var additionalGraph = await StaticDotnetSolutionDependenciesAnalyzer.AnalyzeSolutionAsync(projectRoot, solution);
                    accumulatorGraph.Merge(additionalGraph);
                }

                graph = accumulatorGraph;
            }

            graph ??= new(projectRoot, [], []);
            StaticDotnetSolutionDependenciesAnalyzer.Save(graph);
        }

        StaticProjectScanner.Save(project);
        return new(project, graph);
    }
}
