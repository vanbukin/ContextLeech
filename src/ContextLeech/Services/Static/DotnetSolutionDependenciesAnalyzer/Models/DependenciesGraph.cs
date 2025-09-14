using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using ContextLeech.Constants;

namespace ContextLeech.Services.Static.DotnetSolutionDependenciesAnalyzer.Models;

public class DependenciesGraph
{
    private readonly DirectoryInfo _projectRoot;

    public DependenciesGraph(
        DirectoryInfo projectRoot,
        Dictionary<FileInfo, HashSet<FileInfo>> upstream,
        Dictionary<FileInfo, HashSet<FileInfo>> downstream)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);
        ArgumentNullException.ThrowIfNull(upstream);
        ArgumentNullException.ThrowIfNull(downstream);
        _projectRoot = projectRoot;
        Upstream = upstream;
        Downstream = downstream;
    }

    public Dictionary<FileInfo, HashSet<FileInfo>> Upstream { get; }
    public Dictionary<FileInfo, HashSet<FileInfo>> Downstream { get; }

    public DirectoryInfo GetRoot()
    {
        return _projectRoot;
    }

    public void Merge(DependenciesGraph graphToMerge)
    {
        ArgumentNullException.ThrowIfNull(graphToMerge);

        Merge(Upstream, graphToMerge.Upstream);
        Merge(Downstream, graphToMerge.Downstream);
    }

    private static void Merge(Dictionary<FileInfo, HashSet<FileInfo>> existing, Dictionary<FileInfo, HashSet<FileInfo>> toMerge)
    {
        foreach (var (toMergeKey, toMergeValues) in toMerge)
        {
            var existingKey = existing.Keys.FirstOrDefault(x => x.FullName == toMergeKey.FullName);
            if (existingKey is not null)
            {
                var existingValues = existing[existingKey];
                foreach (var toMergeValue in toMergeValues)
                {
                    var existingValue = existingValues.FirstOrDefault(x => x.FullName == toMergeValue.FullName);
                    if (existingValue is null)
                    {
                        existingValues.Add(toMergeValue);
                    }
                }
            }
            else
            {
                existing[toMergeKey] = toMergeValues;
            }
        }
    }

    public string Serialize()
    {
        var upstream = SerializeDependencies(_projectRoot, Upstream);
        var downstream = SerializeDependencies(_projectRoot, Downstream);
        var container = new SerializationContainer(1, upstream, downstream);
        return JsonSerializer.Serialize(container, JsonSerializationConstants.CommonSerializerOptions);
    }

    private static Dictionary<string, List<string>> SerializeDependencies(
        DirectoryInfo projectRoot,
        Dictionary<FileInfo, HashSet<FileInfo>> dependencies)
    {
        var root = projectRoot.FullName;
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in dependencies)
        {
            var keyRelativePath = Path.GetRelativePath(root, key.FullName).Replace('\\', '/');
            var valuesAccumulator = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var value in values)
            {
                var valueRelativePath = Path.GetRelativePath(root, value.FullName).Replace('\\', '/');
                valuesAccumulator.Add(valueRelativePath);
            }

            result[keyRelativePath] = valuesAccumulator.ToList();
        }

        return result;
    }

    [SuppressMessage("ReSharper", "ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract")]
    public static bool TryDeserialize(
        string json,
        DirectoryInfo projectRoot,
        [NotNullWhen(true)] out DependenciesGraph? graph)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(projectRoot);
        if (!projectRoot.Exists)
        {
            graph = null;
            return false;
        }

        var actualProjectRoot = new DirectoryInfo(projectRoot.FullName);
        if (!actualProjectRoot.Exists)
        {
            graph = null;
            return false;
        }

        var container = JsonSerializer.Deserialize<SerializationContainer>(json, JsonSerializationConstants.CommonSerializerOptions);
        if (container is null || container.FormatVersion != 1)
        {
            graph = null;
            return false;
        }

        if (!TryDeserializeGraph(container.Upstream, actualProjectRoot, out var upstream))
        {
            graph = null;
            return false;
        }

        if (!TryDeserializeGraph(container.Downstream, actualProjectRoot, out var downstream))
        {
            graph = null;
            return false;
        }

        graph = new(actualProjectRoot, upstream, downstream);
        return true;
    }

    private static bool TryDeserializeGraph(
        Dictionary<string, List<string>> graph,
        DirectoryInfo projectRoot,
        [NotNullWhen(true)] out Dictionary<FileInfo, HashSet<FileInfo>>? result)
    {
        var actualProjectRoot = new DirectoryInfo(projectRoot.FullName);
        if (!actualProjectRoot.Exists)
        {
            result = null;
            return false;
        }

        var resultAccumulator = new Dictionary<FileInfo, HashSet<FileInfo>>();
        foreach (var (relativeKey, relativeValues) in graph)
        {
            var absoluteKey = Path.Combine(actualProjectRoot.FullName, relativeKey);

            var tempKeyFileInfo = new FileInfo(absoluteKey);
            if (!tempKeyFileInfo.Exists)
            {
                result = null;
                return false;
            }

            var actualKeyFileInfo = new FileInfo(tempKeyFileInfo.FullName);
            if (!actualKeyFileInfo.Exists)
            {
                result = null;
                return false;
            }

            var valuesAccumulator = new HashSet<FileInfo>();
            foreach (var relativeValue in relativeValues)
            {
                var absoluteValue = Path.Combine(actualProjectRoot.FullName, relativeValue);
                var tempValueFileInfo = new FileInfo(absoluteValue);
                if (!tempValueFileInfo.Exists)
                {
                    result = null;
                    return false;
                }

                var actualValueFileInfo = new FileInfo(tempValueFileInfo.FullName);
                if (!actualValueFileInfo.Exists)
                {
                    result = null;
                    return false;
                }

                valuesAccumulator.Add(actualValueFileInfo);
            }

            resultAccumulator[actualKeyFileInfo] = valuesAccumulator;
        }

        result = resultAccumulator;
        return true;
    }

    private sealed class SerializationContainer
    {
        public SerializationContainer(
            int formatVersion,
            Dictionary<string, List<string>> upstream,
            Dictionary<string, List<string>> downstream)
        {
            FormatVersion = formatVersion;
            Upstream = upstream;
            Downstream = downstream;
        }

        public int FormatVersion { get; }
        public Dictionary<string, List<string>> Upstream { get; }
        public Dictionary<string, List<string>> Downstream { get; }
    }
}
