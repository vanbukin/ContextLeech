using System.Collections.Generic;
using System.IO;

namespace ContextLeech.Services.Static.DotnetSolutionDependencies.Models;

public class FileWithDependents
{
    public FileWithDependents(FileInfo file, HashSet<FileInfo> dependents)
    {
        File = file;
        Dependents = dependents;
    }

    public FileInfo File { get; }

    /// <summary>
    ///     The files that depend on the current file.
    /// </summary>
    public HashSet<FileInfo> Dependents { get; }
}
