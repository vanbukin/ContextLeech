using System;
using System.Threading;
using System.Threading.Tasks;
using ContextLeech.Mcp.ClientFactory;
using ContextLeech.Services.Analyzer;
using Microsoft.Extensions.Hosting;

namespace ContextLeech.BackgroundServices;

public class AnalyzeProjectBackgroundService : BackgroundService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ProjectAnalyzer _projectAnalyzer;
    private bool _mcpServerReady;

    public AnalyzeProjectBackgroundService(
        IHostApplicationLifetime lifetime,
        ProjectAnalyzer projectAnalyzer,
        StreamingMcpClientFactory mcpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(projectAnalyzer);
        ArgumentNullException.ThrowIfNull(mcpClientFactory);
        _lifetime = lifetime;
        _projectAnalyzer = projectAnalyzer;
        _mcpServerReady = false;
        _lifetime.ApplicationStarted.Register(() => _mcpServerReady = true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!_mcpServerReady)
        {
            await Task.Delay(1000, stoppingToken);
        }

        await _projectAnalyzer.AnalyzeProjectAsync(stoppingToken);
        _lifetime.StopApplication();
    }
}
