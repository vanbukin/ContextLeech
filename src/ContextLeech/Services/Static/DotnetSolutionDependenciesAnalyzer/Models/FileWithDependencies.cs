using System;
using System.Collections.Generic;
using System.IO;

namespace ContextLeech.Services.Static.DotnetSolutionDependenciesAnalyzer.Models;

public class FileWithDependencies
{
    public FileWithDependencies(FileInfo file, HashSet<FileInfo> dependencies)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(dependencies);
        File = file;
        Dependencies = dependencies;
    }

    public FileInfo File { get; }

    /// <summary>
    ///     The files that the current file depends on.
    /// </summary>
    public HashSet<FileInfo> Dependencies { get; }
}
