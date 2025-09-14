using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ContextLeech.Mcp.Tools.Models.Request;
using ContextLeech.Mcp.Tools.Models.Response.Enums;
using ContextLeech.Services.Analyzer.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace ContextLeech.Services.Analyzer;

public class FileAnalyzer
{
    private readonly ILogger<FileAnalyzer> _logger;

    private FileAnalysis? _latestLlmResponse;

    public FileAnalyzer(ILogger<FileAnalyzer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }


    [SuppressMessage("ReSharper", "ConvertToUsingDeclaration")]
    public async Task<AnalyzedFile> AnalyzeFileAsync(
        AnalysisQueuedFile file,
        IChatClient chatClient,
        IList<McpClientTool> mcpTools,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(file);
        _latestLlmResponse = null;
        var fileContents = await File.ReadAllTextAsync(file.FileToAnalyze.FullName, Encoding.UTF8, cancellationToken);
        return null!;
    }


    public async Task<FileAnalysisResultStatus> HandleAsync(FileAnalysis fileAnalysis, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _latestLlmResponse = fileAnalysis;
        await Task.Yield();
        return FileAnalysisResultStatus.Error;
    }
}
