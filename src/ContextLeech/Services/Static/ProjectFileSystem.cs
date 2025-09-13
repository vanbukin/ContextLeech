using System;
using System.IO;

namespace ContextLeech.Services.Static;

public static class ProjectFileSystem
{
    public static bool IsFileWithinDirectory(DirectoryInfo projectRoot, FileInfo projectFile)
    {
        ArgumentNullException.ThrowIfNull(projectRoot);
        ArgumentNullException.ThrowIfNull(projectFile);
        if (!projectRoot.Exists || !projectFile.Exists || projectFile.DirectoryName is null)
        {
            return false;
        }

        var rootPath = Path.GetFullPath(projectRoot.FullName).TrimEnd(Path.DirectorySeparatorChar);
        var filePath = Path.GetFullPath(projectFile.DirectoryName).TrimEnd(Path.DirectorySeparatorChar);
        var rootPathWithSeparator = rootPath + Path.DirectorySeparatorChar;
        return filePath.StartsWith(rootPathWithSeparator, StringComparison.OrdinalIgnoreCase);
    }
}
