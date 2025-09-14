using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using ContextLeech.Services.Static.Metadata.Models;

namespace ContextLeech.Services.Metadata;

public sealed class ProjectMetadataService : IDisposable
{
    private ProjectMetadata? _metadata;

    public ProjectMetadataService()
    {
        MetadataSet = new(false);
    }

    public ManualResetEventSlim MetadataSet { get; }

    public void Dispose()
    {
        MetadataSet.Dispose();
    }

    public void SetMetadata(ProjectMetadata metadata)
    {
        _metadata = metadata;
        MetadataSet.Set();
    }

    public bool TryGetProjectMetadata([NotNullWhen(true)] out ProjectMetadata? metadata)
    {
        if (_metadata is null)
        {
            metadata = null;
            return false;
        }

        metadata = _metadata;
        return true;
    }
}
