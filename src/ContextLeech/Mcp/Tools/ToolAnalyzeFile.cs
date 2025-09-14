using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using ContextLeech.Mcp.Tools.Models.Request;
using ContextLeech.Mcp.Tools.Models.Response;
using ContextLeech.Services.Analyzer;
using ModelContextProtocol.Server;

namespace ContextLeech.Mcp.Tools;

[McpServerToolType]
[SuppressMessage("Design", "CA1052:Static holder types should be Static or NotInheritable")]
public class ToolAnalyzeFile
{
    [McpServerTool(Name = "store_file_analysis_result")]
    [Description(
        "Store the AI-optimized knowledge extracted from a file. This preserves the AI's analysis for future search and retrieval.")]
    public static async Task<FileAnalysisResult> AnalyzeFile(
        FileAnalysis fileAnalysis,
        FileAnalyzer fileAnalyzer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileAnalyzer);
        cancellationToken.ThrowIfCancellationRequested();
        var result = await fileAnalyzer.HandleAsync(fileAnalysis, cancellationToken);
        return new(result);
    }
}
