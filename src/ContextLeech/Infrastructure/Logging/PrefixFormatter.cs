using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace ContextLeech.Infrastructure.Logging;

public sealed class PrefixFormatter : ConsoleFormatter, IDisposable
{
    private const string LoglevelPadding = ": ";

    private const string DefaultForegroundColor = "\x1B[39m\x1B[22m"; // reset to default foreground color
    private const string DefaultBackgroundColor = "\x1B[49m"; // reset to the background color
    private static readonly string MessagePadding = new(' ', GetLogLevelString(LogLevel.Information).Length + LoglevelPadding.Length);
    private static readonly string NewLineWithMessagePadding = Environment.NewLine + MessagePadding;
    private readonly IDisposable? _optionsReloadToken;

    public PrefixFormatter(IOptionsMonitor<PrefixFormatterOptions> options)
        : base(nameof(PrefixFormatter))
    {
        ArgumentNullException.ThrowIfNull(options);
        ReloadLoggerOptions(options.CurrentValue);
        _optionsReloadToken = options.OnChange(ReloadLoggerOptions);
    }

    private PrefixFormatterOptions FormatterOptions { get; set; }

    public void Dispose()
    {
        _optionsReloadToken?.Dispose();
    }

    [MemberNotNull(nameof(FormatterOptions))]
    private void ReloadLoggerOptions(PrefixFormatterOptions options)
    {
        FormatterOptions = options;
    }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (textWriter is null)
        {
            return;
        }

        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (logEntry.Exception is null && message is null)
        {
            return;
        }

        var logLevel = logEntry.LogLevel;
        var logLevelColors = GetLogLevelConsoleColors(logLevel);
        var logLevelString = GetLogLevelString(logLevel);

        string? timestamp = null;
        var timestampFormat = FormatterOptions.TimestampFormat;
        if (timestampFormat != null)
        {
            var dateTimeOffset = GetCurrentDateTime();
            timestamp = dateTimeOffset.ToString(timestampFormat, CultureInfo.InvariantCulture);
        }

        if (timestamp != null)
        {
            textWriter.Write(timestamp);
        }

        if (!string.IsNullOrEmpty(FormatterOptions.Prefix))
        {
            textWriter.Write(FormatterOptions.Prefix);
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (logLevelString is not null)
        {
            WriteColoredMessage(textWriter, logLevelString, logLevelColors.Background, logLevelColors.Foreground);
        }

        CreateDefaultLogMessage(textWriter, logEntry, message, scopeProvider);
    }

    private static void WriteColoredMessage(
        TextWriter textWriter,
        string message,
        ConsoleColor? background,
        ConsoleColor? foreground)
    {
        // Order: backgroundcolor, foregroundcolor, Message, reset foregroundcolor, reset backgroundcolor
        if (background.HasValue)
        {
            textWriter.Write(GetBackgroundColorEscapeCode(background.Value));
        }

        if (foreground.HasValue)
        {
            textWriter.Write(GetForegroundColorEscapeCode(foreground.Value));
        }

        textWriter.Write(message);
        if (foreground.HasValue)
        {
            textWriter.Write(DefaultForegroundColor); // reset to default foreground color
        }

        if (background.HasValue)
        {
            textWriter.Write(DefaultBackgroundColor); // reset to the background color
        }
    }

    private static string GetBackgroundColorEscapeCode(ConsoleColor color)
    {
        return color switch
        {
            ConsoleColor.Black => "\x1B[40m",
            ConsoleColor.DarkRed => "\x1B[41m",
            ConsoleColor.DarkGreen => "\x1B[42m",
            ConsoleColor.DarkYellow => "\x1B[43m",
            ConsoleColor.DarkBlue => "\x1B[44m",
            ConsoleColor.DarkMagenta => "\x1B[45m",
            ConsoleColor.DarkCyan => "\x1B[46m",
            ConsoleColor.Gray => "\x1B[47m",
            _ => DefaultBackgroundColor // Use default background color
        };
    }

    private static string GetForegroundColorEscapeCode(ConsoleColor color)
    {
        return color switch
        {
            ConsoleColor.Black => "\x1B[30m",
            ConsoleColor.DarkRed => "\x1B[31m",
            ConsoleColor.DarkGreen => "\x1B[32m",
            ConsoleColor.DarkYellow => "\x1B[33m",
            ConsoleColor.DarkBlue => "\x1B[34m",
            ConsoleColor.DarkMagenta => "\x1B[35m",
            ConsoleColor.DarkCyan => "\x1B[36m",
            ConsoleColor.Gray => "\x1B[37m",
            ConsoleColor.Red => "\x1B[1m\x1B[31m",
            ConsoleColor.Green => "\x1B[1m\x1B[32m",
            ConsoleColor.Yellow => "\x1B[1m\x1B[33m",
            ConsoleColor.Blue => "\x1B[1m\x1B[34m",
            ConsoleColor.Magenta => "\x1B[1m\x1B[35m",
            ConsoleColor.Cyan => "\x1B[1m\x1B[36m",
            ConsoleColor.White => "\x1B[1m\x1B[37m",
            _ => DefaultForegroundColor // default foreground color
        };
    }

    private void CreateDefaultLogMessage<TState>(
        TextWriter textWriter,
        in LogEntry<TState> logEntry,
        string message,
        IExternalScopeProvider? scopeProvider)
    {
        var singleLine = FormatterOptions.SingleLine;
        var eventId = logEntry.EventId.Id;
        var exception = logEntry.Exception;

        // Example:
        // info: ConsoleApp.Program[10]
        //       Request received

        // category and event id

        textWriter.Write(LoglevelPadding);
        textWriter.Write(logEntry.Category);
        textWriter.Write('[');


        Span<char> span = stackalloc char[10];
        if (eventId.TryFormat(span, out var charsWritten, provider: CultureInfo.InvariantCulture))
        {
            textWriter.Write(span[..charsWritten]);
        }

        textWriter.Write(']');
        if (!singleLine)
        {
            textWriter.Write(Environment.NewLine);
        }

        // scope information
        WriteScopeInformation(textWriter, scopeProvider, singleLine);
        WriteMessage(textWriter, message, singleLine);

        // Example:
        // System.InvalidOperationException
        //    at Namespace.Class.Function() in File:line X
        if (exception != null)
        {
            // exception message
            WriteMessage(textWriter, exception.ToString(), singleLine);
        }

        if (singleLine)
        {
            textWriter.Write(Environment.NewLine);
        }
    }

    private static void WriteMessage(TextWriter textWriter, string message, bool singleLine)
    {
        if (!string.IsNullOrEmpty(message))
        {
            if (singleLine)
            {
                textWriter.Write(' ');
                WriteReplacing(textWriter, Environment.NewLine, " ", message);
            }
            else
            {
                textWriter.Write(MessagePadding);
                WriteReplacing(textWriter, Environment.NewLine, NewLineWithMessagePadding, message);
                textWriter.Write(Environment.NewLine);
            }
        }

        static void WriteReplacing(TextWriter writer, string oldValue, string newValue, string message)
        {
            var newMessage = message.Replace(oldValue, newValue, StringComparison.Ordinal);
            writer.Write(newMessage);
        }
    }

    private DateTimeOffset GetCurrentDateTime()
    {
        return FormatterOptions.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
    }

    private static string GetLogLevelString(LogLevel logLevel)
    {
        return logLevel switch
        {
            LogLevel.Trace => "trce",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
        };
    }

    private ConsoleColors GetLogLevelConsoleColors(LogLevel logLevel)
    {
        // We shouldn't be outputting color codes for Android/Apple mobile platforms,
        // they have no shell (adb shell is not meant for running apps) and all the output gets redirected to some log file.
        var disableColors = FormatterOptions.ColorBehavior == LoggerColorBehavior.Disabled ||
                            FormatterOptions.ColorBehavior == LoggerColorBehavior.Default;
        if (disableColors)
        {
            return new(null, null);
        }

        // We must explicitly set the background color if we are setting the foreground color,
        // since just setting one can look bad on the users console.
        return logLevel switch
        {
            LogLevel.Trace => new(ConsoleColor.Gray, ConsoleColor.Black),
            LogLevel.Debug => new(ConsoleColor.Gray, ConsoleColor.Black),
            LogLevel.Information => new(ConsoleColor.DarkGreen, ConsoleColor.Black),
            LogLevel.Warning => new(ConsoleColor.Yellow, ConsoleColor.Black),
            LogLevel.Error => new(ConsoleColor.Black, ConsoleColor.DarkRed),
            LogLevel.Critical => new(ConsoleColor.White, ConsoleColor.DarkRed),
            _ => new(null, null)
        };
    }

    private void WriteScopeInformation(TextWriter textWriter, IExternalScopeProvider? scopeProvider, bool singleLine)
    {
        if (FormatterOptions.IncludeScopes && scopeProvider != null)
        {
            var paddingNeeded = !singleLine;
            scopeProvider.ForEachScope((scope, state) =>
            {
                if (paddingNeeded)
                {
                    paddingNeeded = false;
                    state.Write(MessagePadding);
                    state.Write("=> ");
                }
                else
                {
                    state.Write(" => ");
                }

                state.Write(scope);
            }, textWriter);

            if (!paddingNeeded && !singleLine)
            {
                textWriter.Write(Environment.NewLine);
            }
        }
    }

    private readonly struct ConsoleColors
    {
        public ConsoleColors(ConsoleColor? foreground, ConsoleColor? background)
        {
            Foreground = foreground;
            Background = background;
        }

        public ConsoleColor? Foreground { get; }

        public ConsoleColor? Background { get; }
    }
}
