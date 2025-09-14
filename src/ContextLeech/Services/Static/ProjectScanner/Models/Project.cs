using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using ContextLeech.Constants;
using ContextLeech.Extensions;

namespace ContextLeech.Services.Static.ProjectScanner.Models;

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

    public DirectoryInfo GetRoot()
    {
        return _projectRoot;
    }

    public void AddFile(FileInfo file)
    {
        _files.Add(file);
    }

    public FileInfo[] GetDotnetSolutions()
    {
        var solutions = new List<FileInfo>();
        foreach (var file in _files)
        {
            if (file.Extension == ".sln")
            {
                solutions.Add(file);
            }
        }

        return solutions.ToArray();
    }

    public IReadOnlyCollection<FileInfo> GetFiles()
    {
        return _files;
    }

    public string Serialize()
    {
        var files = new List<string>();
        foreach (var file in _files)
        {
            var relativePath = file.ProjectRelativePath(_projectRoot);
            files.Add(relativePath);
        }

        var orderedFiles = files.OrderBy(x => x).ToList();
        var container = new SerializationContainer(1, orderedFiles);
        return JsonSerializer.Serialize(container, JsonSerializationConstants.CommonSerializerOptions);
    }

    [SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract")]
    [SuppressMessage("ReSharper", "ConditionalAccessQualifierIsNonNullableAccordingToAPIContract")]
    public static bool TryDeserialize(
        string json,
        DirectoryInfo projectRoot,
        [NotNullWhen(true)] out Project? project)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(projectRoot);
        if (!projectRoot.Exists)
        {
            project = null;
            return false;
        }

        var actualProjectRoot = new DirectoryInfo(projectRoot.FullName);
        if (!actualProjectRoot.Exists)
        {
            project = null;
            return false;
        }

        var container = JsonSerializer.Deserialize<SerializationContainer>(json, JsonSerializationConstants.CommonSerializerOptions);
        if (container is null || container.FormatVersion != 1)
        {
            project = null;
            return false;
        }

        project = new(actualProjectRoot);
        var files = new List<string>();
        if (container.Files?.Count > 0)
        {
            files = container.Files;
        }

        foreach (var relativeFilePath in files)
        {
            var absolutePath = Path.Combine(actualProjectRoot.FullName, relativeFilePath);
            var tempFileInfo = new FileInfo(absolutePath);
            if (!tempFileInfo.Exists)
            {
                project = null;
                return false;
            }

            var actualFileInfo = new FileInfo(tempFileInfo.FullName);
            if (!actualFileInfo.Exists)
            {
                project = null;
                return false;
            }

            project.AddFile(actualFileInfo);
        }

        return true;
    }

    private sealed class SerializationContainer
    {
        public SerializationContainer(int formatVersion, List<string> files)
        {
            FormatVersion = formatVersion;
            Files = files;
        }

        public int FormatVersion { get; }
        public List<string> Files { get; }
    }
}
