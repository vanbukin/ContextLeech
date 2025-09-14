using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text;

namespace ContextLeech.Services.Static.FileIo;

public static class StaticFileIo
{
    public static void Write(string pathToFile, string content, Encoding encoding)
    {
        var directoryToCreate = Path.GetDirectoryName(pathToFile);
        if (directoryToCreate is null)
        {
            throw new InvalidOperationException("Can't get file directory");
        }

        if (!Directory.Exists(directoryToCreate))
        {
            Directory.CreateDirectory(directoryToCreate);
        }

        File.WriteAllText(pathToFile, content, encoding);
    }

    public static bool TryReadExisting(string pathToFile, Encoding encoding, [NotNullWhen(true)] out string? content)
    {
        if (!File.Exists(pathToFile))
        {
            content = null;
            return false;
        }

        content = File.ReadAllText(pathToFile, encoding);
        return true;
    }
}
