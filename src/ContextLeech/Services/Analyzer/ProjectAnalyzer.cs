using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ContextLeech.Constants;
using ContextLeech.Extensions;
using ContextLeech.Mcp.ClientFactory;
using ContextLeech.Services.Analyzer.Models;
using ContextLeech.Services.Metadata;
using ContextLeech.Services.Static.FileIo;
using ContextLeech.Services.Static.Metadata.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace ContextLeech.Services.Analyzer;

public class ProjectAnalyzer
{
    private const string Toc = "toc.json";
    private const string ResultsSubDirectory = "results";
    private readonly FileAnalyzer _fileAnalyzer;
    private readonly ILogger<ProjectAnalyzer> _logger;
    private readonly StreamingMcpClientFactory _mcpClientFactory;

    private readonly ProjectMetadataService _projectMetadata;
    private readonly IServiceScopeFactory _scopeFactory;

    public ProjectAnalyzer(
        ProjectMetadataService projectMetadata,
        FileAnalyzer fileAnalyzer,
        IServiceScopeFactory scopeFactory,
        StreamingMcpClientFactory mcpClientFactory,
        ILogger<ProjectAnalyzer> logger)
    {
        ArgumentNullException.ThrowIfNull(projectMetadata);
        ArgumentNullException.ThrowIfNull(fileAnalyzer);
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(mcpClientFactory);
        ArgumentNullException.ThrowIfNull(logger);
        _projectMetadata = projectMetadata;
        _fileAnalyzer = fileAnalyzer;
        _scopeFactory = scopeFactory;
        _mcpClientFactory = mcpClientFactory;
        _logger = logger;
    }

    [SuppressMessage("Style", "IDE0063:Use simple \'using\' statement")]
    [SuppressMessage("ReSharper", "ConvertToUsingDeclaration")]
    public async Task AnalyzeProjectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _logger.StartProjectAnalysis();
        _logger.WaitingForProjectMetadata();
        _projectMetadata.MetadataSet.Wait(cancellationToken);
        if (!_projectMetadata.TryGetProjectMetadata(out var projectMetadata))
        {
            _logger.GotSignalButNoMetadata();
            return;
        }

        _logger.GotMetadataForProject(projectMetadata.Project.GetRoot().FullName);
        _logger.InitResultsToc();
        var toc = InitToc(projectMetadata);
        _logger.InitResultsDirectory();
        var resultsDirectory = InitResultsDirectory(projectMetadata);
        _logger.CreatingAnalysisQueue();
        var queue = CreateAnalysisQueue(projectMetadata, resultsDirectory, toc);
        _logger.AnalysisQueueCreated(queue.Count);
        var progress = 0;
        var total = queue.Count;
        await using (var scope = _scopeFactory.CreateAsyncScope())
        {
            var chatClient = scope.ServiceProvider.GetRequiredService<IChatClient>();
            await using (var mcpClient = await _mcpClientFactory.CreateAsync(cancellationToken))
            {
                var mcpTools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);
                foreach (var file in queue)
                {
                    progress++;
                    _logger.AnalyzingFile(progress, total);
                    var analyzedFile = await _fileAnalyzer.AnalyzeFileAsync(file, chatClient, mcpTools, cancellationToken);
                    SaveAnalyzedFile(file, analyzedFile);
                }
            }
        }

        _logger.AllFilesAnalyzed();
    }

    private static void SaveAnalyzedFile(AnalysisQueuedFile analysisQueuedFile, AnalyzedFile analyzedFile)
    {
        var json = JsonSerializer.Serialize(analyzedFile, JsonSerializationConstants.CommonSerializerOptions);
        File.WriteAllText(
            analysisQueuedFile.FileToStoreResult.FullName,
            json,
            Encoding.UTF8);
    }

    private static List<AnalysisQueuedFile> CreateAnalysisQueue(
        ProjectMetadata metadata,
        DirectoryInfo resultsDirectory,
        Dictionary<string, string> toc)
    {
        var projectRoot = metadata.Project.GetRoot();
        var queue = new List<AnalysisQueuedFile>();
        foreach (var projectFile in metadata.Project.GetFiles())
        {
            var relativeProjectFile = projectFile.ProjectRelativePath(projectRoot);
            var relativeResultFile = toc[relativeProjectFile];
            var resultFileAbsolutePath = Path.Combine(resultsDirectory.FullName, relativeResultFile);
            var resultFile = new FileInfo(resultFileAbsolutePath);
            if (!resultFile.Exists)
            {
                var upstreamDeps = new List<FileInfo>();
                var upstreamKey = metadata.DependenciesGraph.Upstream.Keys.FirstOrDefault(x => x.FullName == projectFile.FullName);
                if (upstreamKey is not null && metadata.DependenciesGraph.Upstream.TryGetValue(upstreamKey, out var metadataUpstreamDeps))
                {
                    foreach (var upstreamDep in metadataUpstreamDeps.OrderBy(x => x.FullName))
                    {
                        upstreamDeps.Add(upstreamDep);
                    }
                }

                var downstreamDeps = new List<FileInfo>();
                var downstreamKey = metadata.DependenciesGraph.Downstream.Keys.FirstOrDefault(x => x.FullName == projectFile.FullName);
                if (downstreamKey is not null && metadata.DependenciesGraph.Downstream.TryGetValue(downstreamKey, out var metadataDownstreamDeps))
                {
                    foreach (var downstreamDep in metadataDownstreamDeps.OrderBy(x => x.FullName))
                    {
                        downstreamDeps.Add(downstreamDep);
                    }
                }

                queue.Add(new(
                    projectFile,
                    new(resultFile.FullName),
                    upstreamDeps,
                    downstreamDeps));
            }
        }

        return
        [
            .. queue.OrderBy(x => x.UpstreamDependencies.Count)
                .ThenBy(x => x.FileToAnalyze.FullName)
        ];
    }

    private static DirectoryInfo InitResultsDirectory(ProjectMetadata metadata)
    {
        var projectRoot = metadata.Project.GetRoot().FullName;
        var analyzeResultsDirectoryAbsolutePath = Path.Combine(
            projectRoot,
            FileSystemConstants.ContextLeechRootDirectory,
            FileSystemConstants.ContextSubDirectory,
            ResultsSubDirectory);
        var analyzeResultsDirectory = new DirectoryInfo(analyzeResultsDirectoryAbsolutePath);
        if (!Directory.Exists(analyzeResultsDirectory.FullName))
        {
            Directory.CreateDirectory(analyzeResultsDirectory.FullName);
        }

        if (!analyzeResultsDirectory.Exists)
        {
            throw new InvalidOperationException("Can't create directory to story analysis results");
        }

        return analyzeResultsDirectory;
    }

    private static Dictionary<string, string> InitToc(ProjectMetadata metadata)
    {
        var toc = CreateToc(metadata);
        if (TryReadExistingToc(metadata, out var existingToc))
        {
            foreach (var relativeFilePath in toc.Keys.ToArray())
            {
                if (existingToc.TryGetValue(relativeFilePath, out var existingAnalyzeResult))
                {
                    toc[relativeFilePath] = existingAnalyzeResult;
                }
            }
        }

        // cleanup garbage
        var projectRoot = metadata.Project.GetRoot().FullName;
        var analyzeResultsDirectoryAbsolutePath = Path.Combine(
            projectRoot,
            FileSystemConstants.ContextLeechRootDirectory,
            FileSystemConstants.ContextSubDirectory,
            ResultsSubDirectory);
        var analyzeResultsDirectory = new DirectoryInfo(analyzeResultsDirectoryAbsolutePath);
        if (analyzeResultsDirectory.Exists)
        {
            var tocValues = toc.Values.ToHashSet();
            var existingAnalyzeResults = analyzeResultsDirectory
                .EnumerateFiles("*.json", SearchOption.TopDirectoryOnly);
            foreach (var existingAnalyzeResult in existingAnalyzeResults)
            {
                if (!tocValues.Contains(existingAnalyzeResult.Name))
                {
                    File.Delete(existingAnalyzeResult.FullName);
                }
            }
        }

        SaveToc(metadata, toc);
        return toc;
    }

    private static Dictionary<string, string> CreateToc(ProjectMetadata metadata)
    {
        var toc = new Dictionary<string, string>();
        foreach (var file in metadata.Project.GetFiles())
        {
            var fileRelativePath = file.ProjectRelativePath(metadata.Project.GetRoot());
            toc[fileRelativePath] = $"{Guid.CreateVersion7():N}.json";
        }

        return toc;
    }

    private static bool TryReadExistingToc(
        ProjectMetadata metadata,
        [NotNullWhen(true)] out Dictionary<string, string>? toc)
    {
        var tocFilePath = Path.Combine(
            metadata.Project.GetRoot().FullName,
            FileSystemConstants.ContextLeechRootDirectory,
            FileSystemConstants.ContextSubDirectory,
            Toc);
        var tocFile = new FileInfo(tocFilePath);
        if (!tocFile.Exists)
        {
            toc = null;
            return false;
        }

        var tocFileJson = File.ReadAllText(tocFile.FullName, Encoding.UTF8);
        var deserializedToc = JsonSerializer.Deserialize<Dictionary<string, string>>(
            tocFileJson,
            JsonSerializationConstants.CommonSerializerOptions);
        if (deserializedToc is null)
        {
            toc = null;
            return false;
        }

        var resultToc = new Dictionary<string, string>();
        foreach (var (relativeFilePath, analyzeResult) in deserializedToc)
        {
            var absoluteFilePath = Path.Combine(metadata.Project.GetRoot().FullName, relativeFilePath);
            var file = new FileInfo(absoluteFilePath);
            if (file.Exists)
            {
                resultToc.Add(relativeFilePath, analyzeResult);
            }
        }

        if (resultToc.Count > 0)
        {
            toc = resultToc;
            return true;
        }

        toc = null;
        return false;
    }

    private static void SaveToc(ProjectMetadata metadata, Dictionary<string, string> toc)
    {
        var tocFilePath = Path.Combine(
            metadata.Project.GetRoot().FullName,
            FileSystemConstants.ContextLeechRootDirectory,
            FileSystemConstants.ContextSubDirectory,
            Toc);
        var tocFile = new FileInfo(tocFilePath);
        var json = JsonSerializer.Serialize(toc, JsonSerializationConstants.CommonSerializerOptions);
        StaticFileIo.Write(tocFile.FullName, json, Encoding.UTF8);
    }
}

public static partial class ProjectAnalyzerLoggingExtensions
{
    [LoggerMessage(LogLevel.Information, "Start project analysis")]
    public static partial void StartProjectAnalysis(this ILogger logger);

    [LoggerMessage(LogLevel.Information, "Waiting for project metadata")]
    public static partial void WaitingForProjectMetadata(this ILogger logger);

    [LoggerMessage(LogLevel.Error, "Got signal that metadata is ready, but metadata was not exists")]
    public static partial void GotSignalButNoMetadata(this ILogger logger);

    [LoggerMessage(LogLevel.Information, "Got metadata for project: '{ProjectPath}'")]
    public static partial void GotMetadataForProject(this ILogger logger, string projectPath);

    [LoggerMessage(LogLevel.Information, "Init results ToC")]
    public static partial void InitResultsToc(this ILogger logger);

    [LoggerMessage(LogLevel.Information, "Init results directory")]
    public static partial void InitResultsDirectory(this ILogger logger);

    [LoggerMessage(LogLevel.Information, "Creating analysis queue")]
    public static partial void CreatingAnalysisQueue(this ILogger logger);

    [LoggerMessage(LogLevel.Information, "Analysis queue created. {FilesToAnalyze} files to analyze")]
    public static partial void AnalysisQueueCreated(this ILogger logger, int filesToAnalyze);

    [LoggerMessage(LogLevel.Information, "All files analyzed")]
    public static partial void AllFilesAnalyzed(this ILogger logger);

    [LoggerMessage(LogLevel.Information, "Analyzing file {FileNumber} of {TotalFiles}")]
    public static partial void AnalyzingFile(this ILogger logger, int fileNumber, int totalFiles);
}
