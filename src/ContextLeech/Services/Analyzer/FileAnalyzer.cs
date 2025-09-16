using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ContextLeech.Constants;
using ContextLeech.Extensions;
using ContextLeech.Mcp.Tools.Models.Request;
using ContextLeech.Services.Analyzer.Models;
using ContextLeech.Services.Static.Metadata.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using OpenAI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace ContextLeech.Services.Analyzer;

public class FileAnalyzer
{
    private const string SystemPrompt =
        """
        Role and scope
        - You are a world-class analyzer of .NET repositories (C#, ASP.NET Core, Razor, EF Core) plus typical supporting assets (TS/JS/CSS/SCSS/SQL/JSON/YAML/TOML/Markdown/.resx).
        - Objective: For one provided file at a time, produce a precise, token-efficient, AI-optimized analysis record and persist it via the provided tool. You never execute code or browse the web.
        - Single-pass workflow: interactive data collection first, then one autonomous analysis-and-save call, then exit.

        Interaction protocol (strict)
        1) Collection phase (must follow this exact order; do not call tools yet):
           - Step 1 — ask for file PATH to be analyzed.
           - Step 2 — ask for file CONTENTS to be analyzed.
           - Step 3 — ask for UPSTREAM dependencies (files this file directly uses/imports). Paths must be relative to project root and Unix-style (e.g., src/App/Program.cs). If none, user replies NONE.
           - Step 4 — ask for DOWNSTREAM dependencies (files that reference this file). Same path rules; NONE if no dependents.
           - Step 5 — confirmation: Ask the user to explicitly confirm that analysis should begin and that no further data will be provided.
             • Required reply to proceed: CONFIRM
             • If user does not confirm, cancel politely.
        2) Execution phase (after CONFIRM):
           - Do not ask for anything else.
           - Analyze the file using only the supplied contents and dependency info.
           - Produce a single structured JSON analysis using the Detailed Template (below).
           - Persist results by emitting exactly one call to the provided tool function "store_file_analysis_result" with:
             • fileAnalysis: the JSON object described under "Detailed Template"
           - After the tool returns success, terminate the conversation with no further assistant messages.
           - If the tool reports failure, return one concise error message describing the failure and then stop.

        Detailed Template (output placed in "fileAnalysis")
        - Purpose (string, required, ≤400 tokens): Clear explanation of the file’s role, responsibilities, and architectural significance.
        - Complexity (enum: Low | Medium | High, required): Cognitive/logic complexity, not file size. Use heuristics below to select.
        - Category (enum: Config | Source | Test | Docs | Other, required): Use heuristics below to select.
        - KeyTerms (string[], required, ≤800 tokens): Array of FULL search terms ordered by importance (most essential first). Include technologies, frameworks, patterns, important functions/classes, domain terms. Use full words and only use abbreviations for established terms.
        - Dependencies (string[]?, optional (nullable), ≤400 tokens): Array describing key imports/references and why they’re used (external libraries and internal modules).
        - Patterns (string[]?, optional (nullable), ≤800 tokens): Architectural/design patterns, conventions, and rationale.
        - RelatedContexts (optional; ≤400 tokens): Array of closely related files and contexts with brief relationship notes.
        - AiGuidance (string?, optional (nullable), ≤600 tokens): Concrete guidance for AI agents modifying or extending this code; gotchas, invariants, best practices.
        - ErrorHandling (optional; ≤400 tokens): Array of error scenarios and handling strategies/fallbacks.
        - IntegrationPoints (optional; ≤400 tokens): External systems/services/APIs/datastores and how this file connects.

        Categorization and complexity heuristics
        - Category (derive implicitly; you may include in analysis if schema supports it):
          • Config: *.csproj, *.sln, Directory.Packages.props, NuGet.config, global.json, appsettings*.json, launchSettings.json, CI/CD *.yml/*.yaml, Dockerfile, docker-compose.yml, *.config, tsconfig.json, package.json.
          • Source: *.cs, .razor, .cshtml, .cshtml.cs, .razor.cs, *.ts/*.tsx/*.js/*.jsx, *.scss/*.css, *.sql, *.resx and other source code files
          • Test: under test/ or projects named *.Tests*, frameworks xUnit/NUnit/MSTest.
          • Docs: Markdown and repo docs.
          • Other: images/binaries/assets and any other files.
        - Complexity rubric:
          • Low: DTOs/records/models, constants, simple configs/views/markup.
          • Medium: services/components/controllers with several actions, DI + I/O, validation, moderate branching/async.
          • High: core domain/business logic, complex controllers/services, heavy branching/algorithms/state machines, concurrency, reflection/source generation, multi-layer orchestration.

        Quality and style constraints
        - Be specific and factual; avoid speculation. If unknown or not inferable, set value to null.
        - Optimize for AI comprehension: prefer high-signal details (public surface, routes, DI registrations, configuration keys, noteworthy algorithms) over line-by-line commentary.
        - Order keyTerms strategically: core function → primary technology → key patterns → supporting technologies → business/domain terms.
        - Keep token efficiency; no redundant prose; no chit-chat.

        .NET 8+ and repo-aware heuristics (apply when applicable)
        - Solutions/projects:
          • *.sln: summarize included projects if visible.
          • *.csproj / Directory.Packages.props / NuGet.config: capture TargetFramework(s) (e.g., net8.0+), SDK style, Nullable/ImplicitUsings, analyzers, key PackageReference items (EF Core, Swashbuckle, Serilog, MediatR, AutoMapper, FluentValidation, Polly, Dapper, StackExchange.Redis, Quartz, Hangfire, MassTransit, Azure.*, AWSSDK.*).
          • global.json: SDK pinning/roll-forward.
        - ASP.NET Core:
          • Minimal hosting: WebApplication.CreateBuilder, services.Add..., middleware order (app.Use...), endpoint mappings (MapGet/MapPost/etc.), health checks, Swagger/OpenAPI.
          • MVC/controllers: [ApiController], [Route], [HttpGet]/[HttpPost]/..., route templates, filters, versioning.
          • Razor Pages/Views: @page, routes, linkage to .cshtml.cs, _ViewImports.cshtml, _ViewStart.cshtml.
          • Blazor: .razor components with @page; distinguish Server vs WASM if inferable.
        - Configuration:
          • appsettings*.json: list top-level keys; do not echo secrets.
          • Options pattern: IOptions<T>/OptionsBuilder<T>/services.Configure<T>("Section").
          • launchSettings.json: profiles, applicationUrl, env vars.
        - Data and integrations:
          • EF Core: DbContext, DbSet<T>, OnModelCreating; provider hints by package; migrations summary.
          • SQL: classify DDL vs DML; main tables/views/procs/indexes.
          • External: HttpClient/typed clients, gRPC, queues/buses (MassTransit, Azure Service Bus, RabbitMQ), caches (Redis), schedulers (Quartz/Hangfire), storage (Azure Blob/S3), identity/auth (JWT/OIDC).
        - Resilience/observability:
          • Global exception handling, ProblemDetails, validation flows, logging, Polly policies (retries/timeouts/circuit breakers), compensation strategies.

        Safety and formatting rules
        - Never browse or call external services.
        - Do not leak credentials or secrets from inputs; if present, redact and note purpose.
        - Use null for missing data; do not invent dependencies, routes, or patterns.
        - Paths must be relative to project root and Unix-style (e.g., src/Web/Program.cs).
        - After CONFIRM and the single tool call completes successfully, end the conversation without any additional output.

        First-message script (use verbatim order and structure)
        - Message 1: "Provide the file PATH to analyze, relative to the project root and using Unix-style separators (e.g., src/App/Program.cs)."
        - After receiving path: "Paste the full file CONTENTS TO ANALYZE. If the file is empty, reply EMPTY."
        - After receiving contents: "List UPSTREAM dependencies (files this file directly imports/uses) as relative Unix-style paths. If none, reply NONE."
        - After receiving upstream: "List DOWNSTREAM dependencies (files that depend on this file) as relative Unix-style paths. If none, reply NONE."
        - Then: "Confirm to begin single-pass analysis now. No further data will be provided. Reply CONFIRM to proceed or CANCEL to stop."

        Tool-call rules
        - Persist results with exactly one call to "store_file_analysis_result"
        - On success: emit "Done" and STOP.
        - On failure: emit one concise error message and STOP.
        """;

    private static readonly Func<ChatCompletion, IDictionary<string, BinaryData>> GetAdditionalPropsFunc = GetSerializedAdditionalRawDataFunc();
    private readonly ILogger<FileAnalyzer> _logger;

    private FileAnalysis? _latestLlmResponse;

    public FileAnalyzer(ILogger<FileAnalyzer> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    [SuppressMessage("ReSharper", "ConvertToUsingDeclaration")]
    [SuppressMessage("ReSharper", "NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract")]
    public async Task<AnalyzedFile> AnalyzeFileAsync(
        AnalysisQueuedFile file,
        ProjectMetadata projectMetadata,
        IChatClient chatClient,
        IList<McpClientTool> mcpTools,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(projectMetadata);
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(mcpTools);
        cancellationToken.ThrowIfCancellationRequested();
        _latestLlmResponse = null;
        var filePath = file.FileToAnalyze.ProjectRelativePath(projectMetadata.Project.GetRoot());
        var fileContents = await File.ReadAllTextAsync(file.FileToAnalyze.FullName, Encoding.UTF8, cancellationToken);
        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        var fileContentsForLlm = fileContents?.Trim();
        if (string.IsNullOrWhiteSpace(fileContentsForLlm))
        {
            fileContentsForLlm = "EMPTY";
        }

        var upstream = BuildUpstream(file, projectMetadata) ?? "NONE";
        var downstream = BuildDownstream(file, projectMetadata) ?? "NONE";
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.Assistant, "Provide the file PATH to analyze, relative to the project root and using Unix-style separators (e.g., src/App/Program.cs)."),
            new(ChatRole.User, filePath),
            new(ChatRole.Assistant, "Paste the full file CONTENTS TO ANALYZE. If the file is empty, reply \"EMPTY\"."),
            new(ChatRole.User, fileContentsForLlm),
            new(ChatRole.Assistant, "List UPSTREAM dependencies (files this file directly imports/uses) as relative Unix-style paths. If none, reply \"NONE\"."),
            new(ChatRole.User, upstream),
            new(ChatRole.Assistant, "List DOWNSTREAM dependencies (files that depend on this file) as relative Unix-style paths. If none, reply \"NONE\"."),
            new(ChatRole.User, downstream),
            new(ChatRole.Assistant, "Confirm to begin single-pass analysis now. No further data will be provided. Reply \"CONFIRM\" to proceed or \"CANCEL\" to stop."),
            new(ChatRole.User, "CONFIRM")
        };
        _logger.SendRequestToLlm(filePath);
        var response = await chatClient.GetResponseAsync(
            messages,
            new()
            {
                Tools = [..mcpTools],
                AllowMultipleToolCalls = false,
                ConversationId = Guid.CreateVersion7().ToString("N")
            }, cancellationToken);
        _logger.GotResponseFromLlm(filePath);

        foreach (var responseMessage in response.Messages)
        {
            if (responseMessage.RawRepresentation is ChatCompletion openAiChatCompletion)
            {
                var additionalProps = GetAdditionalPropsFunc(openAiChatCompletion);
                // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
                if (additionalProps is not null && additionalProps.TryGetValue("timings", out var rawTimings))
                {
                    var timings = rawTimings.ToObjectFromJson<Timings>(JsonSerializationConstants.CommonSerializerOptions);
                    if (timings is not null)
                    {
                        _logger.PromptProcessingMetrics(
                            timings.cache_n + timings.prompt_n,
                            timings.cache_n,
                            timings.prompt_n,
                            timings.prompt_per_second,
                            timings.prompt_ms);
                        _logger.TokenGenerationMetrics(
                            timings.predicted_n,
                            timings.predicted_per_second,
                            timings.predicted_ms);
                    }
                }
            }
        }

        messages.AddMessages(response);
        var renderedMessages = string.Join(Environment.NewLine, messages.Select((x, i) => $"{i + 1}) [{x.Role.Value}]").ToArray()).Trim();
        if (messages.Count != 14)
        {
            _logger.LlmMessagesAnomalyDetected(renderedMessages);
        }

        _logger.LlmMessages(renderedMessages);
        if (_latestLlmResponse is null)
        {
            throw new InvalidOperationException("Latest llm response was null!");
        }


        var result = new AnalyzedFile(
            filePath,
            _latestLlmResponse.Purpose,
            _latestLlmResponse.Complexity,
            _latestLlmResponse.Category,
            _latestLlmResponse.KeyTerms ?? [],
            _latestLlmResponse.Dependencies,
            _latestLlmResponse.Patterns,
            _latestLlmResponse.RelatedContexts,
            _latestLlmResponse.AiGuidance,
            _latestLlmResponse.ErrorHandling,
            _latestLlmResponse.IntegrationPoints);
        return result;
    }

    public void Handle(FileAnalysis fileAnalysis)
    {
        _latestLlmResponse = fileAnalysis;
    }

    private static string? BuildUpstream(AnalysisQueuedFile file, ProjectMetadata projectMetadata)
    {
        var projectRoot = projectMetadata.Project.GetRoot();
        var builder = new StringBuilder();
        foreach (var upstreamDependency in file.UpstreamDependencies)
        {
            var upstreamDependencyPath = upstreamDependency.ProjectRelativePath(projectRoot);
            builder.AppendLine(upstreamDependencyPath);
        }

        var result = builder.ToString();
        if (string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        return result;
    }

    private static string? BuildDownstream(AnalysisQueuedFile file, ProjectMetadata projectMetadata)
    {
        var projectRoot = projectMetadata.Project.GetRoot();
        var builder = new StringBuilder();
        foreach (var downstreamDependency in file.DownstreamDependencies)
        {
            var downstreamDependencyPath = downstreamDependency.ProjectRelativePath(projectRoot);
            builder.AppendLine(downstreamDependencyPath);
        }

        var result = builder.ToString();
        if (string.IsNullOrWhiteSpace(result))
        {
            return null;
        }

        return result;
    }

    private static string BuildPrompt(AnalysisQueuedFile file, ProjectMetadata projectMetadata, string fileContents)
    {
        var projectRoot = projectMetadata.Project.GetRoot();
        var filePath = file.FileToAnalyze.ProjectRelativePath(projectRoot);
        var builder = new StringBuilder();
        builder.AppendLine("# FilePath");
        builder.AppendLine(filePath);
        builder.AppendLine();
        builder.AppendLine("# UpstreamDependencies");
        foreach (var upstreamDependency in file.UpstreamDependencies)
        {
            var upstreamDependencyPath = upstreamDependency.ProjectRelativePath(projectRoot);
            builder.AppendLine(upstreamDependencyPath);
        }

        builder.AppendLine();
        builder.AppendLine("# DownstreamDependencies");
        foreach (var downstreamDependency in file.DownstreamDependencies)
        {
            var downstreamDependencyPath = downstreamDependency.ProjectRelativePath(projectRoot);
            builder.AppendLine(downstreamDependencyPath);
        }

        builder.AppendLine();
        builder.AppendLine("# Content");
        builder.Append(fileContents);
        return builder.ToString();
    }

    private static Func<ChatCompletion, IDictionary<string, BinaryData>> GetSerializedAdditionalRawDataFunc()
    {
        var param = Expression.Parameter(typeof(ChatCompletion), "c");

        var prop = typeof(ChatCompletion).GetProperty(
            "SerializedAdditionalRawData",
            BindingFlags.Instance | BindingFlags.NonPublic
        ) ?? throw new MissingMemberException(
            typeof(ChatCompletion).FullName,
            "SerializedAdditionalRawData"
        );

        var body = Expression.Property(param, prop);
        var lambda = Expression.Lambda<Func<ChatCompletion, IDictionary<string, BinaryData>>>(body, param);
        var func = lambda.Compile();
        return func;
    }
#pragma warning disable CA1812
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "NotAccessedPositionalProperty.Local")]
    [SuppressMessage("Style", "IDE1006:Naming Styles")]
    private sealed record Timings(
        int cache_n,
        int prompt_n,
        double prompt_ms,
        double prompt_per_token_ms,
        double prompt_per_second,
        int predicted_n,
        double predicted_ms,
        double predicted_per_token_ms,
        double predicted_per_second);
#pragma warning restore CA1812
}

public static partial class FileAnalyzerLoggingExtensions
{
    [LoggerMessage(LogLevel.Information, "Send request to LLM to analyze: {FilePath}")]
    public static partial void SendRequestToLlm(this ILogger logger, string filePath);

    [LoggerMessage(LogLevel.Information, "Got response from LLM for file: {FilePath}")]
    public static partial void GotResponseFromLlm(this ILogger logger, string filePath);

    [LoggerMessage(LogLevel.Warning, "Chat with anomaly: {Messages}")]
    public static partial void LlmMessagesAnomalyDetected(this ILogger logger, string messages);

    [LoggerMessage(LogLevel.Information, "Chat: {Messages}")]
    public static partial void LlmMessages(this ILogger logger, string messages);

    [LoggerMessage(LogLevel.Information, "Prompt processing metrics. Prompt = cached + processed tokens: {PromptTokens} = {CachedTokens} + {ProcessedTokens}, Prompt processing speed = {ProcessingTokensPerSecond:0.##} t/s, Duration: {PromptProcessingDurationMs:0.##} ms")]
    public static partial void PromptProcessingMetrics(this ILogger logger, int promptTokens, int cachedTokens, int processedTokens, double processingTokensPerSecond, double promptProcessingDurationMs);

    [LoggerMessage(LogLevel.Information, "Token generation metrics. Generated = {GeneratedTokens} tokens, Token generation speed = {GeneratedTokensPerSecond:0.##} t/s, Duration: {GenerationDurationMs:0.##} ms")]
    public static partial void TokenGenerationMetrics(this ILogger logger, int generatedTokens, double generatedTokensPerSecond, double generationDurationMs);
}
