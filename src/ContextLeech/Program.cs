using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using ContextLeech.Services.Static.Metadata;

namespace ContextLeech;

public static class Program
{
    private const string RepoPath = "";

    [SuppressMessage("Design", "CA1031:Do not catch general exception types")]
    public static async Task Main(string[] args)
    {
        var metadata = await StaticMetadataService.LoadMetadataAsync(RepoPath);
        Console.WriteLine("Done");
        Console.WriteLine();
    }
}
