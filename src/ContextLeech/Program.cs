using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using ContextLeech.Services.Static.DotnetSolutionDependencies;

namespace ContextLeech;

public static class Program
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types")]
    public static async Task Main(string[] args)
    {
        var solutionPath = "";
        var result = await DotnetSolutionDependenciesAnalyzer.AnalyzeSolutionAsync(solutionPath);
        PrintResults(result.Upstream);
        Console.WriteLine();
    }

    [SuppressMessage("Performance", "CA1859:Use concrete types when possible for improved performance")]
    private static void PrintResults(Dictionary<string, HashSet<string>> results)
    {
        Console.WriteLine($"\nFound {results.Count} types:");
        Console.WriteLine(new string('-', 80));

        foreach (var (key, values) in results.OrderBy(r => r.Key))
        {
            Console.WriteLine($"File: {key}");

            if (values.Count != 0)
            {
                Console.WriteLine("External dependencies:");
                foreach (var dep in values.OrderBy(d => d))
                {
                    Console.WriteLine($"  - {dep}");
                }
            }
            else
            {
                Console.WriteLine("External dependencies: no");
            }

            Console.WriteLine(new string('-', 80));
        }

        Console.WriteLine(new string('-', 80));
        Console.WriteLine("DONE");
    }
}
