// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Formats.Tar;
using System.Text.Json;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.NET.Build.Containers.Logging;
using Microsoft.NET.Build.Containers.Resources;

namespace Microsoft.NET.Build.Containers.Tasks;

public sealed partial class PushImageToRegistry : Microsoft.Build.Utilities.Task, ICancelableTask, IDisposable
{
    /// <summary>
    /// Unused. For interface parity with the ToolTask implementation of the task.
    /// </summary>
    public string ToolExe { get; set; }

    /// <summary>
    /// Unused. For interface parity with the ToolTask implementation of the task.
    /// </summary>
    public string ToolPath { get; set; }

    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public void Cancel() => _cancellationTokenSource.Cancel();

    public override bool Execute()
    {
        return Task.Run(() => ExecuteAsync(_cancellationTokenSource.Token)).GetAwaiter().GetResult();
    }

    internal async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        string? extractedPath = await ExtractArchiveAsync(Path.GetFullPath(ArchivePath), cancellationToken);
        if (Log.HasLoggedErrors)
        {
            return false;
        }

        try
        {
            return await ExecutePushAsync(extractedPath!, cancellationToken);
        }
        catch (Exception e)
        {
            if (BuildEngine != null)
            {
                Log.LogErrorWithCodeFromResources(nameof(Strings.ArchiveExtractionFailed), e.Message);
                Log.LogMessage(MessageImportance.Low, "Details: {0}", e);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(extractedPath))
                {
                    Directory.Delete(extractedPath, true);
                }
            }
            catch (Exception e)
            {
                if (BuildEngine != null)
                {
                    Log.LogErrorWithCodeFromResources(nameof(Strings.ArchiveExtractionFailed), e.Message);
                    Log.LogMessage(MessageImportance.Low, "Details: {0}", e);
                }
            }
        }

        return !Log.HasLoggedErrors;
    }

    private async Task<bool> ExecutePushAsync(string extractedPath, CancellationToken cancellationToken)
    {
        using MSBuildLoggerProvider loggerProvider = new(Log);
        ILoggerFactory msbuildLoggerFactory = new LoggerFactory(new[] { loggerProvider });
        DestinationImageReference[]? destinationImageReference =
            await BuildDestinationImageReferencesAsync(extractedPath, msbuildLoggerFactory, cancellationToken);

        if (Log.HasLoggedErrors)
        {
            return false;
        }

        if (destinationImageReference == null)
        {
            // nothing to do
            return true;
        }

        foreach (DestinationImageReference imageReference in destinationImageReference)
        {
            await ExecutePushAsync(imageReference, cancellationToken);

            if (Log.HasLoggedErrors)
            {
                return false;
            }
        }

        return true;
    }

    private async Task<string?> ExtractArchiveAsync(string archivePath, CancellationToken cancellationToken)
    {
        try
        {
            string path = Path.Combine(ContentStore.TempPath, $"extract-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            await TarFile.ExtractToDirectoryAsync(archivePath, path, true, cancellationToken);
            return path;
        }
        catch (Exception e)
        {
            if (BuildEngine != null)
            {
                Log.LogErrorWithCodeFromResources(nameof(Strings.ArchiveExtractionFailed), e.Message);
                Log.LogMessage(MessageImportance.Low, "Details: {0}", e);
            }
        }

        return null;
    }

    private async Task ExecutePushAsync(DestinationImageReference imageReference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await imageReference.RemoteRegistry!.PushAsync(
                ArchivePath,
                imageReference,
                cancellationToken).ConfigureAwait(false);

            if (BuildEngine != null)
            {
                Log.LogMessage(MessageImportance.High, "Pushed image to registry '{1}'", imageReference);
            }
        }
        catch (ContainerHttpException e)
        {
            if (BuildEngine != null)
            {
                Log.LogErrorFromException(e, true);
            }
        }
        catch (Exception e)
        {
            if (BuildEngine != null)
            {
                Log.LogErrorWithCodeFromResources(nameof(Strings.RegistryOutputPushFailed), e.Message);
                Log.LogMessage(MessageImportance.Low, "Details: {0}", e);
            }
        }
    }

    private Task<DestinationImageReference[]?> BuildDestinationImageReferencesAsync(string extractedPath, ILoggerFactory msbuildLoggerFactory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Nothing provided -> We load the information from the OCI archive and build the destinations
        if (string.IsNullOrEmpty(Repository) && ImageTags == null)
        {
            return TryLoadDestinationsFromOciImageAsync(extractedPath, msbuildLoggerFactory, cancellationToken);
        }
        // everything provided -> Push accordingly
        else if (!string.IsNullOrEmpty(Repository) && ImageTags is { Length: > 0 })
        {
            return Task.FromResult<DestinationImageReference[]?>(new[]
            {
                DestinationImageReference.CreateFromSettings(
                    Repository,
                    ImageTags,
                    msbuildLoggerFactory,
                    Registry)
            });
        }
        // partially provided -> error
        else
        {
            Log.LogErrorWithCodeFromResources(nameof(Strings.RepositoryAndTagsProvidedPartially), nameof(Repository), nameof(ImageTags));
            return Task.FromResult<DestinationImageReference[]?>(null);
        }
    }

    private async Task<DestinationImageReference[]?> TryLoadDestinationsFromOciImageAsync(string extractedPath,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            string indexJsonPath = Path.Combine(extractedPath, "index.json");
            return await TryLoadDestinationsFromOciIndexAsync(indexJsonPath, loggerFactory, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            if (BuildEngine != null)
            {
                Log.LogErrorWithCodeFromResources(nameof(Strings.LoadingRepositoryAndTagsFromArchiveFailed), e.Message);
                Log.LogMessage(MessageImportance.Low, "Details: {0}", e);
            }

            return null;
        }
    }

    private async Task<DestinationImageReference[]?> TryLoadDestinationsFromOciIndexAsync(string indexJsonPath, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        OciImageIndex index = await OciImageIndex.LoadFromFileAsync(indexJsonPath, cancellationToken).ConfigureAwait(false);
        return LoadAndValidateRepositoryWithTags(index)
            .GroupBy(t => t.repository)
            .Select(t => DestinationImageReference.CreateFromSettings(t.Key,
                t.Select(i => i.tag).ToArray(),
                loggerFactory, Registry))
            .ToArray();
    }

    private static IEnumerable<(string repository, string tag)> LoadAndValidateRepositoryWithTags(OciImageIndex index)
    {
        // we are building here a structure like:
        // [0] => { [0] => "repository", [1] => "tag" }
        // [1] => { [0] => "repository", [1] => "tag2" }
        // [2] => { [0] => "repository2", [1] => "tag" }

        string[][] allTags = index.Manifests
            .Select(m => m.Annotations?.TryGetValue(OciAnnotations.AnnotationRefName, out var v) ?? false ? v : "")
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v.Split(':'))
            .ToArray();

        if (allTags.Length == 0)
        {
            throw new FormatException($"No '{OciAnnotations.AnnotationRefName}' annotations found defining repositories and tags for pushing");
        }


        // validate data
        var invalidTags = new StringBuilder();
        foreach (string[] repositoryAndTag in allTags)
        {
            if (repositoryAndTag.Length != 2)
            {
                invalidTags.AppendLine(
                    $"Only '{OciAnnotations.AnnotationRefName}' annotations with format 'repository:tag' are supported");
            }
        }

        if (invalidTags.Length > 0)
        {
            throw new FormatException(invalidTags.ToString());
        }

        return allTags.Select(t => (repository: t[0], tag: t[1]));
    }

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
    }
}
