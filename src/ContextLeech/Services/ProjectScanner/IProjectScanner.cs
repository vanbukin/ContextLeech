using System.Collections.Generic;
using System.IO;
using ContextLeech.Services.ProjectScanner.Models;

namespace ContextLeech.Services.ProjectScanner;

public interface IProjectScanner
{
    Project? ScanProject(
        DirectoryInfo projectRoot,
        IEnumerable<string>? defaultDirectoriesToIgnore = null);
}
