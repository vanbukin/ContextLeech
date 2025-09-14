using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace ContextLeech.Services.Analyzer.Models;

[SuppressMessage("Design", "CA1002:Do not expose generic lists")]
public sealed class AnalysisQueuedFile
{
    public AnalysisQueuedFile(
        FileInfo fileToAnalyze,
        FileInfo fileToStoreResult,
        List<FileInfo> upstreamDependencies,
        List<FileInfo> downstreamDependencies)
    {
        ArgumentNullException.ThrowIfNull(fileToAnalyze);
        ArgumentNullException.ThrowIfNull(fileToStoreResult);
        ArgumentNullException.ThrowIfNull(upstreamDependencies);
        ArgumentNullException.ThrowIfNull(downstreamDependencies);
        FileToAnalyze = fileToAnalyze;
        FileToStoreResult = fileToStoreResult;
        UpstreamDependencies = upstreamDependencies;
        DownstreamDependencies = downstreamDependencies;
    }

    public FileInfo FileToAnalyze { get; }
    public FileInfo FileToStoreResult { get; }
    public List<FileInfo> UpstreamDependencies { get; }
    public List<FileInfo> DownstreamDependencies { get; }
}
