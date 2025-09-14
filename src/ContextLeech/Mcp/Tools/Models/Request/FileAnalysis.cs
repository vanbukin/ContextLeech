using System.ComponentModel;
using System.Runtime.InteropServices;
using ContextLeech.Mcp.Tools.Models.Request.Enums;

namespace ContextLeech.Mcp.Tools.Models.Request;

public record FileAnalysis(
    [Description("Primary purpose and functionality")]
    string Purpose,
    [Description("File complexity level")] Complexity Complexity,
    [Description("File category")] Category Category,
    [Description("Key searchable terms, concepts, entities for AI search and discovery")]
    string[] KeyTerms,
    [Description("Dependencies and imports")]
    string[]? Dependencies,
    [Description("Implementation patterns and conventions")]
    string[]? Patterns,
    [Description("Related files and contexts")]
    string[]? RelatedContexts,
    [Description("Specific guidance for AI agents working with this code")]
    string? AiGuidance,
    [Description("Error handling patterns and strategies")]
    string? ErrorHandling,
    [Optional]
    [Description("Key integration points with other systems")]
    string? IntegrationPoints
);
