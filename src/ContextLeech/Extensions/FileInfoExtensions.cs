using System;
using System.IO;

namespace ContextLeech.Extensions;

public static class FileInfoExtensions
{
    public static string ProjectRelativePath(this FileInfo fileInfo, DirectoryInfo projectRoot)
    {
        ArgumentNullException.ThrowIfNull(fileInfo);
        ArgumentNullException.ThrowIfNull(projectRoot);
        var projectRootAbsolutePath = projectRoot.FullName;
        var fileAbsolutePath = fileInfo.FullName;
        if (!fileAbsolutePath.StartsWith(projectRootAbsolutePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"File: '{fileInfo.FullName}' not belong to project: '{projectRoot}'");
        }

        return Path.GetRelativePath(projectRootAbsolutePath, fileAbsolutePath).Replace('\\', '/');
    }
}
