using System;
using System.Collections.Generic;
using System.IO;

namespace ContextLeech.Services.ProjectScanner.Models;

public class Project
{
    private readonly List<FileInfo> _files;
    private readonly DirectoryInfo _projectRoot;

    public Project(DirectoryInfo projectRoot)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);
        if (!projectRoot.Exists)
        {
            throw new ArgumentException($"Directory '{projectRoot.FullName}' does not exist.", nameof(projectRoot));
        }

        _projectRoot = projectRoot;
        _files = new();
    }

    public void AddFile(FileInfo file)
    {
        _files.Add(file);
    }

    public IReadOnlyCollection<FileInfo> GetFiles()
    {
        return _files;
    }
}
