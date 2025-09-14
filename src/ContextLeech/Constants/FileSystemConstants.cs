using System;
using System.Collections.Generic;

namespace ContextLeech.Constants;

public static class FileSystemConstants
{
    public const string ContextLeechRootDirectory = ".context-leech";
    public const string MetadataSubDirectory = "metadata";

    public static readonly IReadOnlySet<string> EmbeddedDirectoriesToIgnore = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "node_modules",
        ContextLeechRootDirectory
    };
}
