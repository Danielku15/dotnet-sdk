// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Formats.Tar;
using Microsoft.Extensions.Logging;
using Microsoft.NET.Build.Containers.Resources;

namespace Microsoft.NET.Build.Containers;

public class ImageArchivePusher
{
    private string _extractedPath = string.Empty;

    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _log;
    private readonly string _archivePath;
    private readonly string _registry;
    private readonly string? _repository;
    private readonly string[]? _imageTags;

    public int ExitCode { get; private set; }

    public bool HasErrors => ExitCode != 0;

    public ImageArchivePusher(ILoggerFactory loggerFactory, string archivePath, string registry, string? repository, string[]? imageTags)
    {
        _loggerFactory = loggerFactory;
        _archivePath = archivePath;
        _registry = registry;
        _repository = repository;
        _imageTags = imageTags;
        _log = loggerFactory.CreateLogger<ImageArchivePusher>();
    }

    private void LogErrorWithCodeFromResources(string resourceName, params object?[] args)
    {
        _log.LogError(DiagnosticMessage.ErrorFromResourceWithCode(resourceName, args));
        if (ExitCode == 0)
        {
            ExitCode = 1;
        }
    }

    private void LogErrorWithCodeFromResources(Exception e, string resourceName, params object?[] args)
    {
        _log.LogError(e, DiagnosticMessage.ErrorFromResourceWithCode(resourceName, args));
        if (ExitCode == 0)
        {
            ExitCode = 1;
        }
    }

    public async Task PushAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await ExtractArchiveAsync(cancellationToken);
        if (HasErrors)
        {
            return;
        }

        try
        {
            await ExecutePushAsync(cancellationToken);
        }
        catch (Exception e)
        {
            LogErrorWithCodeFromResources(e, nameof(Strings.ImageArchivePusher_ArchivePushFailed), e.Message);
        }
        finally
        {
            try
            {
                if (!string.IsNullOrEmpty(_extractedPath) && Directory.Exists(_extractedPath))
                {
                    Directory.Delete(_extractedPath, true);
                }
            }
            catch (Exception e)
            {
                LogErrorWithCodeFromResources(e, nameof(Strings.ImageArchivePusher_ArchiveCleanupFailed), e.Message);
            }
        }
    }

    internal async Task ExtractArchiveAsync(CancellationToken cancellationToken)
    {
        try
        {
            string path = Path.Combine(ContentStore.TempPath, $"extract-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            await TarFile.ExtractToDirectoryAsync(_archivePath, path, true, cancellationToken);
            _extractedPath = path;
        }
        catch (Exception e)
        {
            LogErrorWithCodeFromResources(e, nameof(Strings.ImageArchivePusher_ArchiveExtractionFailed), e.Message);
        }
    }

    private async Task ExecutePushAsync(CancellationToken cancellationToken)
    {
        DestinationImageReference[]? destinationImageReference = await BuildDestinationImageReferencesAsync(cancellationToken);

        if (HasErrors)
        {
            return;
        }

        if (destinationImageReference == null)
        {
            // nothing to do
            return;
        }

        foreach (DestinationImageReference imageReference in destinationImageReference)
        {
            await ExecutePushAsync(imageReference, cancellationToken);

            if (HasErrors)
            {
                return;
            }
        }
    }

    private async Task ExecutePushAsync(DestinationImageReference imageReference, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await imageReference.RemoteRegistry!.PushAsync(
                _extractedPath,
                imageReference,
                cancellationToken).ConfigureAwait(false);

            _log.LogInformation(Strings.ContainerBuilder_ImageUploadedToRegistry, imageReference, _registry);
        }
        catch (Exception e)
        {
            LogErrorWithCodeFromResources(e, nameof(Strings.RegistryOutputPushFailed), e.Message);
        }
    }


    internal async Task<DestinationImageReference[]?> BuildDestinationImageReferencesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Nothing provided -> We load the information from the OCI archive and build the destinations
        if (string.IsNullOrEmpty(_repository) && _imageTags == null)
        {
            return await TryLoadDestinationsFromOciImageAsync(cancellationToken);
        }
        // everything provided -> Push accordingly
        else if (!string.IsNullOrEmpty(_repository) && _imageTags is { Length: > 0 })
        {
            return new[]
            {
                DestinationImageReference.CreateFromSettings(
                    _repository,
                    _imageTags,
                    _loggerFactory,
                    _registry)
            };
        }
        // only repository name provided, tags from archive
        else if (!string.IsNullOrEmpty(_repository) && _imageTags == null)
        {
            var all = await TryLoadDestinationsFromOciImageAsync(cancellationToken);
            if (all == null)
            {
                return null;
            }

            // Only if we only have one repository name with multiple tags we can take a user defined repository as override
            if (all.Length != 1)
            {
                LogErrorWithCodeFromResources(
                    nameof(Strings.ImageArchivePusher_MultipleRepositoriesInFile_CustomRepository));
                return null;
            }

            return new[]
            {
                DestinationImageReference.CreateFromSettings(
                    _repository,
                    all[0].Tags,
                    _loggerFactory,
                    _registry)
            };
        }
        // only tags name provided, repository from archive
        else if (string.IsNullOrEmpty(_repository) && _imageTags is { Length: > 0 })
        {
            var all = await TryLoadDestinationsFromOciImageAsync(cancellationToken);
            if (all == null)
            {
                return null;
            }

            // Only if we only have one repository name with multiple tags we can take a user defined repository as override
            if (all.Length != 1)
            {
                LogErrorWithCodeFromResources(
                    nameof(Strings.ImageArchivePusher_MultipleRepositoriesInFile_CustomTags));
                return null;
            }

            return new[]
            {
                DestinationImageReference.CreateFromSettings(
                    all[0].Repository,
                    _imageTags,
                    _loggerFactory,
                    _registry)
            };
        }
        // partially provided -> error
        else
        {
            LogErrorWithCodeFromResources(nameof(Strings.ImageArchivePusher_RepositoryAndTagsProvidedPartially), nameof(_repository), nameof(_imageTags));
            return null;
        }
    }
    private async Task<DestinationImageReference[]?> TryLoadDestinationsFromOciImageAsync(CancellationToken cancellationToken)
    {
        try
        {
            string indexJsonPath = Path.Combine(_extractedPath, "index.json");
            return await TryLoadDestinationsFromOciIndexAsync(indexJsonPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            LogErrorWithCodeFromResources(nameof(Strings.ImageArchivePusher_LoadingRepositoryAndTagsFromArchiveFailed), e.Message);
            return null;
        }
    }

    private async Task<DestinationImageReference[]?> TryLoadDestinationsFromOciIndexAsync(string indexJsonPath, CancellationToken cancellationToken)
    {
        OciImageIndex index = await OciImageIndex.LoadFromFileAsync(indexJsonPath, cancellationToken).ConfigureAwait(false);
        return LoadAndValidateRepositoryWithTags(index)
            .GroupBy(t => t.repository)
            .Select(t => DestinationImageReference.CreateFromSettings(t.Key,
                t.Select(i => i.tag).ToArray(),
                _loggerFactory, _registry))
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
            throw new FormatException(DiagnosticMessage.ErrorFromResourceWithCode(
                nameof(Strings.ImageArchivePusher_InvalidAnnotations), OciAnnotations.AnnotationRefName));
        }


        // validate data
        var invalidTags = new StringBuilder();
        foreach (string[] repositoryAndTag in allTags)
        {
            if (repositoryAndTag.Length != 2)
            {
                invalidTags.Append(string.Join(':', repositoryAndTag));
            }
        }

        if (invalidTags.Length > 0)
        {
            throw new FormatException(DiagnosticMessage.ErrorFromResourceWithCode(
                nameof(Strings.ImageArchivePusher_WrongAnnotationContent), OciAnnotations.AnnotationRefName,
                string.Join(", ", invalidTags)));
        }

        return allTags.Select(t => (repository: t[0], tag: t[1]));
    }

}
