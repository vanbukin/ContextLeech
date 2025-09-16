using System;
using System.ClientModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
using ContextLeech.BackgroundServices;
using ContextLeech.Configuration;
using ContextLeech.Constants;
using ContextLeech.Infrastructure.Logging;
using ContextLeech.Mcp.ClientFactory;
using ContextLeech.Mcp.Tools;
using ContextLeech.Services.Analyzer;
using ContextLeech.Services.Metadata;
using ContextLeech.Services.Static.Metadata;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;

namespace ContextLeech;

public static class Program
{
    [SuppressMessage("Design", "CA1031:Do not catch general exception types")]
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var exitCode = 0;
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.Configure<HostOptions>(static hostOptions =>
        {
            hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
        });
        builder.Services.Configure<ConsoleLifetimeOptions>(static options => options.SuppressStatusMessages = true);
        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetRequiredSection("Logging"));
        builder.Logging
            .AddConsole(options => options.FormatterName = nameof(PrefixFormatter))
            .AddConsoleFormatter<PrefixFormatter, PrefixFormatterOptions>(options =>
            {
                options.ColorBehavior = LoggerColorBehavior.Enabled;
                options.UseUtcTimestamp = false;
                options.SingleLine = true;
                options.IncludeScopes = false;
                options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff]";
                options.Prefix = null;
            });
        builder.Services.AddScoped<OpenAIClient>(_ => new(
            new ApiKeyCredential("lm-studio"),
            new()
            {
                Endpoint = new("http://localhost:8080/v1", UriKind.Absolute),
                NetworkTimeout = TimeSpan.FromDays(1)
            }));
        builder.Services.AddScoped<ChatClient>(sp =>
        {
            var openAiClient = sp.GetRequiredService<OpenAIClient>();
            return openAiClient.GetChatClient("openai/gpt-oss-20b");
        });
        builder.Services.AddScoped<IChatClient>(sp =>
        {
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var msChatClient = sp.GetRequiredService<ChatClient>();
            return msChatClient.AsIChatClient()
                .AsBuilder()
                .UseLogging(loggerFactory)
                .UseFunctionInvocation(loggerFactory, client =>
                {
                    client.AllowConcurrentInvocation = false;
                    client.IncludeDetailedErrors = false;
                    client.MaximumConsecutiveErrorsPerRequest = 0;
                    client.MaximumIterationsPerRequest = 10;
                })
                .Build(sp);
        });
        builder.Services.AddOptions<ApplicationOptions>().Bind(builder.Configuration).ValidateDataAnnotations();
        builder.Services.AddSingleton<ProjectMetadataService>();
        Pipe clientToServerPipe = new();
        Pipe serverToClientPipe = new();
        builder.Services.AddMcpServer()
            .WithStreamServerTransport(clientToServerPipe.Reader.AsStream(), serverToClientPipe.Writer.AsStream())
            .WithTools<ToolAnalyzeFile>(JsonSerializationConstants.McpJsonOptions);
        builder.Services.AddSingleton(_ => new StreamingMcpClientFactory(clientToServerPipe, serverToClientPipe));
        builder.Services.AddSingleton<FileAnalyzer>();
        builder.Services.AddSingleton<ProjectAnalyzer>();
        builder.Services.AddHostedService<AnalyzeProjectBackgroundService>();
        var app = builder.Build();
        try
        {
            app.Logger.StartInitialization();
            var repositoryPath = GetRepositoryPath(app);
            app.Logger.GotProject(repositoryPath.FullName);
            app.Logger.LoadingMetadata();
            var metadata = await StaticMetadataService.LoadMetadataAsync(repositoryPath.FullName, null, null);
            app.Logger.MetadataSet();
            var projectMetadata = app.Services.GetRequiredService<ProjectMetadataService>();
            projectMetadata.SetMetadata(metadata);
            app.Logger.MetadataSet();
            app.Logger.StartingApplicationPipeline();
            await app.RunAsync();
            app.Logger.ApplicationCompleted();
        }
        catch (Exception ex)
        {
            app.Logger.HostTerminated(ex);
            exitCode = -1;
        }

        return exitCode;
    }

    private static DirectoryInfo GetRepositoryPath(WebApplication app)
    {
        var appOptions = app.Services.GetRequiredService<IOptions<ApplicationOptions>>();
        var directoryInfo = new DirectoryInfo(appOptions.Value.RepoPath);
        if (!directoryInfo.Exists)
        {
            throw new InvalidOperationException("Invalid repo path directory");
        }

        var actualDirectory = new DirectoryInfo(directoryInfo.FullName);
        if (!directoryInfo.Exists)
        {
            throw new InvalidOperationException("Can't get repo absolute path");
        }

        return actualDirectory;
    }
}

public static partial class ProgramLoggingExtensions
{
    [LoggerMessage(LogLevel.Information, "Start initialization")]
    public static partial void StartInitialization(this ILogger logger);

    [LoggerMessage(LogLevel.Information, "Got project directory to process: '{ProjectPath}'")]
    public static partial void GotProject(this ILogger logger, string projectPath);

    [LoggerMessage(LogLevel.Information, "Loading metadata")]
    public static partial void LoadingMetadata(this ILogger logger);

    [LoggerMessage(LogLevel.Information, "Metadata loaded")]
    public static partial void MetadataLoaded(this ILogger logger);

    [LoggerMessage(LogLevel.Information, "Metadata set and ready to use in other parts of application")]
    public static partial void MetadataSet(this ILogger logger);

    [LoggerMessage(LogLevel.Information, "Starting application pipeline")]
    public static partial void StartingApplicationPipeline(this ILogger logger);

    [LoggerMessage(LogLevel.Information, "Application completed successfully")]
    public static partial void ApplicationCompleted(this ILogger logger);

    [LoggerMessage(LogLevel.Critical, "Host terminated unexpectedly")]
    public static partial void HostTerminated(this ILogger logger, Exception? ex);
}
