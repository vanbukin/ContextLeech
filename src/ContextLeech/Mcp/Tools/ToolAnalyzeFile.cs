using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using ContextLeech.Mcp.Tools.Models.Request;
using ContextLeech.Services.Analyzer;
using ModelContextProtocol.Server;

namespace ContextLeech.Mcp.Tools;

[McpServerToolType]
[SuppressMessage("Design", "CA1052:Static holder types should be Static or NotInheritable")]
public class ToolAnalyzeFile
{
    [McpServerTool(
        Name = "store_file_analysis_result",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        ReadOnly = false,
        UseStructuredContent = false)]
    [Description(
        "Store the AI-optimized knowledge extracted from a file. This preserves the AI's analysis for future search and retrieval.")]
    public static string AnalyzeFile(
        IMcpServer thisServer,
        FileAnalysis fileAnalysis,
        FileAnalyzer fileAnalyzer,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fileAnalysis);
        ArgumentNullException.ThrowIfNull(fileAnalyzer);
        cancellationToken.ThrowIfCancellationRequested();
        fileAnalyzer.Handle(fileAnalysis);
        return "This is the content of the tool call result. The tool call was executed successfully.";
    }
}
