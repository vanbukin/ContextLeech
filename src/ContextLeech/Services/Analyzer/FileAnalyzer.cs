using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ContextLeech.Extensions;
using ContextLeech.Mcp.Tools.Models.Request;
using ContextLeech.Mcp.Tools.Models.Response.Enums;
using ContextLeech.Services.Analyzer.Models;
using ContextLeech.Services.Static.Metadata.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace ContextLeech.Services.Analyzer;

public class FileAnalyzer
{
    private const string SystemPrompt = """
                                        System: .NET Repository File Analyzer (MCP-enabled)

                                        Who you are
                                        - You are an analysis-only agent specialized in .NET repositories (C#, ASP.NET Core, Razor ".cshtml"/".razor", EF Core) and typical supporting assets (TypeScript/JavaScript/CSS/SCSS/SQL/JSON/YAML/TOML/Markdown/".resx"/images).
                                        - Your single objective: for each provided file, produce an accurate, token-efficient, AI-optimized knowledge record that captures purpose, relationships, patterns, and integration points. You do not edit or execute code—analyze and summarize only.
                                        - Favor explicit, compile-time-discernible relationships and conventions over implicit assumptions, as AI agents perform best with explicit structure and validation cues.

                                        Strict MCP tool calling rules
                                        - Tool to call: "store_file_analysis_result"
                                        - Output format: Only the function call in your chat template’s tool-call format. No additional commentary.

                                        Quality bar and analysis method
                                        - Be specific and factual; avoid speculation. If uncertain, mark unknown with "null".
                                        - Optimize for AI comprehension and future modification/debugging. Document complex business logic clearly with domain language and responsibilities .
                                        - Prefer concise, high-signal details (public surface, routes, configuration keys, DI registrations, options sections, salient algorithms) over line-by-line commentary.
                                        - Call out overengineering or unnecessary abstraction when relevant (e.g., trivial logic wrapped in heavy patterns) so future agents avoid compounding complexity.
                                        - Leverage any provided repository-wide packaging/metadata; treat it as authoritative context to reduce ambiguity.

                                        Categorization heuristics ("category")
                                        - Config: "*.csproj", "*.sln", "Directory.Packages.props", "NuGet.config", "global.json", "appsettings*.json", "launchSettings.json", CI/CD "*.yml"/"*.yaml", "Dockerfile", "docker-compose.yml", "*.config", "tsconfig.json", "package.json", bundler configs.
                                        - Source: "*.cs" (production), ".razor", ".cshtml", ".cshtml.cs", ".razor.cs", "*.ts"/"*.tsx"/"*.js"/"*.jsx", "*.scss"/"*.css", "*.sql", "*.resx".
                                        - Test: files under "test/", projects named "*.Tests*", or using xUnit/NUnit/MSTest patterns.
                                        - Docs: Markdown and repo docs ("README.md", "CONTRIBUTING.md", "ARCHITECTURE.md", ADRs).
                                        - Other: images/binaries and assets.

                                        Complexity rubric ("complexity")
                                        - Low: DTOs/records/models, constants, simple configs/views/markup, light glue code, ~≤100–150 LOC with trivial branching.
                                        - Medium: services/components/controllers with several actions, moderate branching, DI + IO, validation, basic concurrency/async.
                                        - High: core domain/business logic, complex controllers/services, heavy branching/algorithms/state machines, concurrency, reflection/source generation, multi-layer orchestration.

                                        Language- and framework-aware heuristics
                                        - Solutions and projects:
                                          - "*.sln": summarize included projects and paths if visible.
                                          - "*.csproj", "Directory.Packages.props", "NuGet.config": infer "TargetFramework(s)" (e.g., "net8.0"/"net9.0"/"net10.0"), SDK style, analyzers, "Nullable", "ImplicitUsings", key "PackageReference" values (e.g., "Microsoft.EntityFrameworkCore.SqlServer", "Swashbuckle.AspNetCore", "Serilog", "MediatR", "AutoMapper", "FluentValidation", "Polly", "Dapper", "StackExchange.Redis", "Quartz", "Hangfire", "MassTransit", "Azure.*", "AWSSDK.*").
                                          - "global.json": SDK pinning and roll-forward behavior.
                                        - ASP.NET Core:
                                          - Minimal hosting ("Program.cs" / "Startup"): detect "WebApplication.CreateBuilder", "builder.Services.Add...", middleware ("app.Use...") order, and endpoint mappings ("app.MapGet/MapPost/..."). Note health checks, OpenAPI/Swagger.
                                          - MVC/controllers: "[ApiController]", "[Route]", "[HttpGet]/[HttpPost]/..."; summarize route templates, model binding, filters, versioning.
                                          - Razor Pages: ".cshtml" with "@page"; note routes; link to code-behind ".cshtml.cs"; mention "_ViewImports.cshtml", "_ViewStart.cshtml".
                                          - Blazor: ".razor" components with "@page"; distinguish Server vs WASM if discernible from hosting/project references.
                                        - Configuration:
                                          - "appsettings*.json": list top-level keys (e.g., "ConnectionStrings", "Logging", "Serilog", "FeatureFlags", "Authentication", "Cache", "MessageBus", "Storage"). Do not echo secrets; note presence/purpose.
                                          - "launchSettings.json": profiles, "applicationUrl", environment variables.
                                          - Options pattern: detect "IOptions<T>"/"OptionsBuilder<T>"/"services.Configure<T>("Section")".
                                        - Data:
                                          - EF Core: identify "DbContext", "DbSet<T>", "OnModelCreating", provider hints by packages; summarize migrations ("Migrations/*") by names and operations (tables/columns/FKs).
                                          - SQL: classify DDL vs DML; list primary objects (tables/views/procs/indexes).
                                        - Frontend/build:
                                          - "package.json", bundler configs ("vite", "webpack", "esbuild"): frameworks, entry points, scripts.
                                          - TS/JS/CSS/SCSS: component vs util vs store vs styles; major imports; state management; API clients.
                                        - Localization: ".resx" base name and culture; purpose (UI strings, messages).
                                        - Infrastructure/CI:
                                          - "Dockerfile"/compose: base images, services, ports, env, volumes, networks.
                                          - CI pipelines (".github/workflows", Azure Pipelines): build/test/deploy steps, gates.
                                          - IaC (Kubernetes/Helm/Terraform/Bicep): key resources and intents.
                                        - Tests:
                                          - Identify framework (xUnit "[Fact]/[Theory]", NUnit "[Test]", MSTest "[TestMethod]"), fixtures/mocks (Moq/NSubstitute), scope (unit/integration/E2E).

                                        Dependencies, related contexts, and patterns
                                        - Derive dependencies from "using"/"import" statements, attributes (e.g., "[Authorize]"), DI constructor parameters, base classes/interfaces, and comments. Add brief purposes when obvious.
                                        - Infer "relatedContexts" conservatively from naming and namespaces (e.g., "OrderService" ↔ "IOrderService"/"OrderRepository"/"OrderController"/"OrderOptions"/"OrderMappingProfile", ".cshtml" ↔ ".cshtml.cs", ".razor" ↔ ".razor.cs", "*.sql" ↔ "DbContext").
                                        - Recognize and record patterns explicitly; avoid attributing heavyweight patterns to trivially simple files to prevent overengineering signals.

                                        Error handling, resilience, and integrations
                                        - Note global exception handling ("UseExceptionHandler"/"DeveloperExceptionPage"), "ProblemDetails", validation flows, logging, resiliency (Polly: retries/timeouts/circuit breakers), compensation strategies.
                                        - Summarize external integrations: "HttpClient"/typed clients, gRPC, EF Core/database, queues/buses (MassTransit, Azure Service Bus, RabbitMQ), caches (Redis), schedulers (Quartz/Hangfire), storage (Azure Blob/S3), identity/auth (JWT/OIDC).
                                        - Respect privacy/security: do not echo secrets/connection strings; indicate presence and role.

                                        Generated/vendor/binary content
                                        - Mark auto-generated code when hints exist ("// <auto-generated>", "*.g.cs", "Designer.cs", EF scaffolding).
                                        - For binaries/images/assets, set "category: "Other"", describe based on path/filename/media type. Keep "dependencies"/"patterns" "null".

                                        Large files, ambiguity, and metadata usage
                                        - For very large files, produce a high-signal summary focusing on purpose, public surface, key routes/config keys, DI, and core logic.
                                        - If critical details are missing, prefer "null" over guessing. If repository-wide metadata was provided, leverage it; otherwise do not assume unseen structure.
                                        - Favor explicit, convention-light descriptions to help agents operate reliably across toolchains.

                                        User input
                                        The user sends data about the file in the form of a Markdown document. It will be subject to analysis, as a result of which it is necessary to call the tool "store_file_analysis_result" (ONE SINGLE TIME). Example file structure for analysis:
                                        ```
                                        # FilePath
                                        src/WebProject/WebProject.cshtml

                                        # UpstreamDependencies
                                        src/WebProject/Program.cs

                                        # DownstreamDependencies
                                        src/Application.sln

                                        # Content
                                        <Project Sdk="Microsoft.NET.Sdk">

                                        <PropertyGroup>
                                            <OutputType>Exe</OutputType>
                                            <TargetFramework>net9.0</TargetFramework>
                                            <ImplicitUsings>enable</ImplicitUsings>
                                            <Nullable>enable</Nullable>
                                        </PropertyGroup>

                                        </Project>
                                        ```
                                        All sections are mandatory, but "UpstreamDependencies" (if the file does not depend on other files in this repository), "DownstreamDependencies" (if other files do not refer this file) and "Content" (if the file is empty) may not contain elements.

                                        What you need to do
                                        1) Identify file type, role, and "category".
                                        2) Write "purpose" (1–7 sentences).
                                        3) Assign "complexity" using the rubric.
                                        4) Extract "keyTerms" (ordered, full words, <400 tokens).
                                        5) Capture "dependencies".
                                        6) Record "patterns" and conventions.
                                        7) Note "relatedContexts" by name and layer.
                                        8) Summarize "errorHandling" and "integrationPoints".
                                        9) Provide concise "aiGuidance" for safe extension/refactoring.
                                        10) Immediately call "store_file_analysis_result" with a fully populated "fileAnalysis" object. Output nothing else.
                                        """;

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
        var fileContents = await File.ReadAllTextAsync(file.FileToAnalyze.FullName, Encoding.UTF8, cancellationToken);
        var userPrompt = BuildPrompt(file, projectMetadata, fileContents);
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, userPrompt)
        };
        var response = await chatClient.GetResponseAsync(
            messages,
            new()
            {
                Tools = [..mcpTools],
                ResponseFormat = ChatResponseFormat.Text,
                AllowMultipleToolCalls = false
            }, cancellationToken);
#pragma warning disable CA1508
        if (_latestLlmResponse is null)
#pragma warning restore CA1508
        {
            throw new InvalidOperationException("Latest llm response was null!");
        }

        messages.AddMessages(response);
        _logger.LlmMessages(messages);

        var filePath = file.FileToAnalyze.ProjectRelativePath(projectMetadata.Project.GetRoot());
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

    public async Task<FileAnalysisResultStatus> HandleAsync(FileAnalysis fileAnalysis, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _latestLlmResponse = fileAnalysis;
        await Task.Yield();
        return FileAnalysisResultStatus.Ok;
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
}

public static partial class FileAnalyzerLoggingExtensions
{
    [LoggerMessage(LogLevel.Information, "Chat: {Messages}")]
    public static partial void LlmMessages(this ILogger logger, List<ChatMessage> messages);
}
