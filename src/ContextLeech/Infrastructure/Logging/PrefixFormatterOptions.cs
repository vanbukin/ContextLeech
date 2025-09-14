using Microsoft.Extensions.Logging.Console;

namespace ContextLeech.Infrastructure.Logging;

public class PrefixFormatterOptions : ConsoleFormatterOptions
{
    public string? Prefix { get; set; }
    public LoggerColorBehavior ColorBehavior { get; set; }
    public bool SingleLine { get; set; }
}
