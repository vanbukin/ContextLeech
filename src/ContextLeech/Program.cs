using System;
using System.Diagnostics.CodeAnalysis;
using ContextLeech.Services.ProjectScanner.Implementation;

namespace ContextLeech;

[SuppressMessage("Maintainability", "CA1515:Consider making public types internal")]
public static class Program
{
    public static void Main(string[] args)
    {
        var projectScanner = new DefaultProjectScanner();
        var project = projectScanner.ScanProject(new(@""));
        if (project is null)
        {
            Console.WriteLine("Can't scan project");
        }
        else
        {
            var foundFiles = project.GetFiles();
            Console.WriteLine("Project scanned");
            Console.WriteLine($"File found: {foundFiles.Count}");
            foreach (var foundFile in foundFiles)
            {
                Console.WriteLine(foundFile.FullName);
            }
        }
    }
}
