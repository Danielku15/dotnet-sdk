// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.CommandLine;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.NET.Build.Containers;

namespace containerize;

internal class PushImageToRegistryCommand : CliCommand
{
    internal CliOption<string> ArchivePathOption { get; } = new("--archivepath")
    {
        Description = "The path to the OCI image archive to push to the registry.",
        Required = true
    };

    internal CliOption<string> RegistryOption { get; } = new("--registry")
    {
        Description = "The registry to push to.",
        Required = true
    };

    internal CliOption<string> RepositoryOption { get; } = new("--repository")
    {
        Description = "The registry to push to.",
        Required = false

    };

    internal CliOption<string[]> ImageTagsOption { get; } = new("--imagetags")
    {
        Description = "The tags to associate with the new image.",
        AllowMultipleArgumentsPerToken = true,
        Required = false,
    };

    internal PushImageToRegistryCommand() : base("push", "Push an existing OCI archive to a remote registry without Docker.")
    {
        Options.Add(ArchivePathOption);
        Options.Add(RegistryOption);
        Options.Add(RepositoryOption);
        Options.Add(ImageTagsOption);

        SetAction(async (parseResult, cancellationToken) =>
        {
            string archivePath = parseResult.GetValue(ArchivePathOption)!;
            string registry = parseResult.GetValue(RegistryOption)!;
            string? repository = parseResult.GetValue(RepositoryOption);
            string[]? imageTags = parseResult.GetValue(ImageTagsOption);

            bool traceEnabled = Env.GetEnvironmentVariableAsBool("CONTAINERIZE_TRACE_LOGGING_ENABLED");
            LogLevel verbosity = traceEnabled ? LogLevel.Trace : LogLevel.Information;
            using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(c => c.ColorBehavior = LoggerColorBehavior.Disabled).SetMinimumLevel(verbosity));


            ImageArchivePusher pusher = new(loggerFactory, archivePath, registry, repository, imageTags);
            await pusher.PushAsync(cancellationToken).ConfigureAwait(false);
            return pusher.ExitCode;
        });
    }
}
