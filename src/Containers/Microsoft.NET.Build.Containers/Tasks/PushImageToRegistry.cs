// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Microsoft.NET.Build.Containers.Logging;

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
        using MSBuildLoggerProvider loggerProvider = new(Log);
        ILoggerFactory msbuildLoggerFactory = new LoggerFactory(new[] { loggerProvider });

        ImageArchivePusher pusher = new(msbuildLoggerFactory, ArchivePath, Registry, Repository, ImageTags);
        await pusher.PushAsync(cancellationToken).ConfigureAwait(false);

        return !pusher.HasErrors;
    }

    public void Dispose()
    {
        _cancellationTokenSource.Dispose();
    }
}
