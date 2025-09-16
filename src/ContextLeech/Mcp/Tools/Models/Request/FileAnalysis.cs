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
    [Optional]
    [Description("Dependencies and imports")]
    string[]? Dependencies,
    [Optional]
    [Description("Implementation patterns and conventions")]
    string[]? Patterns,
    [Optional]
    [Description("Related files and contexts")]
    string[]? RelatedContexts,
    [Optional]
    [Description("Specific guidance for AI agents working with this code")]
    string? AiGuidance,
    [Optional]
    [Description("Error handling patterns and strategies")]
    string? ErrorHandling,
    [Optional]
    [Description("Key integration points with other systems")]
    string? IntegrationPoints
);
