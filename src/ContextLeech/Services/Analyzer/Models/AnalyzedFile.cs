using ContextLeech.Mcp.Tools.Models.Request.Enums;

namespace ContextLeech.Services.Analyzer.Models;

public sealed class AnalyzedFile
{
    public AnalyzedFile(
        string filePath,
        string purpose,
        Complexity complexity,
        Category category,
        string[] keyTerms,
        string[]? dependencies,
        string[]? patterns,
        string[]? relatedContexts,
        string? aiGuidance,
        string? errorHandling,
        string? integrationPoints)
    {
        FilePath = filePath;
        Purpose = purpose;
        Complexity = complexity;
        Category = category;
        KeyTerms = keyTerms;
        Dependencies = dependencies;
        Patterns = patterns;
        RelatedContexts = relatedContexts;
        AiGuidance = aiGuidance;
        ErrorHandling = errorHandling;
        IntegrationPoints = integrationPoints;
    }

    public string FilePath { get; }
    public string Purpose { get; }
    public Complexity Complexity { get; }
    public Category Category { get; }
    public string[] KeyTerms { get; }
    public string[]? Dependencies { get; }
    public string[]? Patterns { get; }
    public string[]? RelatedContexts { get; }
    public string? AiGuidance { get; }
    public string? ErrorHandling { get; }
    public string? IntegrationPoints { get; }
}
