using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ContextLeech.Services.ProjectScanner.Models;

namespace ContextLeech.Services.ProjectScanner.Implementation;

public class DefaultProjectScanner : IProjectScanner
{
    public Project? ScanProject(
        DirectoryInfo projectRoot,
        IEnumerable<string>? defaultDirectoriesToIgnore = null)
    {
        // ReSharper disable once ConditionalAccessQualifierIsNonNullableAccordingToAPIContract
        if (projectRoot?.Exists is not true)
        {
            return null;
        }

        var gitIgnore = LoadGitIgnoreRules(projectRoot);
        var osSpecificDirNameComparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var dirsToIgnore = new HashSet<string>(defaultDirectoriesToIgnore ?? [], StringComparer.OrdinalIgnoreCase)
        {
            ".git"
        };
        var stack = new Stack<DirectoryInfo>();
        stack.Push(projectRoot);
        var project = new Project(projectRoot);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (IsIgnoredByDirectoryName(current, dirsToIgnore, osSpecificDirNameComparison))
            {
                continue;
            }

            if (IsIgnoredDirByGitIgnore(current, projectRoot, gitIgnore))
            {
                continue;
            }

            IEnumerable<FileInfo> files;
            try
            {
                files = current.EnumerateFiles("*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                continue; // Skip directories we cannot access
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                // Skip if file is under an excluded directory (paranoia check)
                if (IsIgnoredDirByGitIgnore(file, projectRoot, gitIgnore))
                {
                    continue;
                }

                project.AddFile(file);
            }

            // Enumerate subdirectories and push onto stack
            IEnumerable<DirectoryInfo> subDirs;
            try
            {
                subDirs = current.EnumerateDirectories("*", SearchOption.TopDirectoryOnly);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            foreach (var subDir in subDirs)
            {
                if (IsIgnoredByDirectoryName(subDir, dirsToIgnore, osSpecificDirNameComparison))
                {
                    continue;
                }

                if (IsIgnoredDirByGitIgnore(subDir, projectRoot, gitIgnore))
                {
                    continue;
                }

                // Skip symlinks to avoid cycles
                if ((subDir.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                stack.Push(subDir);
            }
        }

        return project;
    }

    private static bool IsIgnoredDirByGitIgnore(DirectoryInfo currentDirectory, DirectoryInfo rootDirectory, Ignore.Ignore ignore)
    {
        var relativePath = Path.TrimEndingDirectorySeparator(Path.GetRelativePath(rootDirectory.FullName, currentDirectory.FullName));
        relativePath = relativePath.Replace('\\', '/');
        return ignore.IsIgnored(relativePath);
    }

    private static bool IsIgnoredDirByGitIgnore(FileInfo currentFile, DirectoryInfo rootDirectory, Ignore.Ignore ignore)
    {
        var relativePath = Path.TrimEndingDirectorySeparator(Path.GetRelativePath(rootDirectory.FullName, currentFile.FullName));
        relativePath = relativePath.Replace('\\', '/');
        return ignore.IsIgnored(relativePath);
    }

    private static bool IsIgnoredByDirectoryName(DirectoryInfo currentDirectory, HashSet<string> directoriesToIgnore, StringComparison osSpecificDirNameComparison)
    {
        foreach (var dirToIgnore in directoriesToIgnore)
        {
            if (string.Equals(currentDirectory.Name, dirToIgnore, osSpecificDirNameComparison))
            {
                return true;
            }
        }

        return false;
    }


    private static Ignore.Ignore LoadGitIgnoreRules(DirectoryInfo projectRoot)
    {
        var gitIgnoreAbsolutePath = Path.Combine(projectRoot.FullName, ".gitignore");
        if (File.Exists(gitIgnoreAbsolutePath))
        {
            var atLeastOneRuleAdded = false;
            Ignore.Ignore gitignore = new();
            var gitIgnoreContents = File.ReadAllLines(gitIgnoreAbsolutePath, Encoding.UTF8);
            foreach (var ignoreRule in gitIgnoreContents.Where(x => !string.IsNullOrWhiteSpace(x) && !x.StartsWith('#')))
            {
                gitignore.Add(ignoreRule);
                atLeastOneRuleAdded = true;
            }

            if (atLeastOneRuleAdded)
            {
                return gitignore;
            }
        }

        return new();
    }

    private static string NormalizeDirPath(string path)
    {
        var full = Path.GetFullPath(path);
        full = $"{full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)}{Path.DirectorySeparatorChar}";
        return full;
    }
}
