// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.NET.Build.Containers.Resources;

namespace Microsoft.NET.Build.Containers.Tasks;

/// <summary>
/// This task will shell out to the net7.0-targeted application for VS scenarios.
/// </summary>
public partial class PushImageToRegistry : ContainerizeToolTask
{
    protected override string GenerateCommandLineCommands() => GenerateCommandLineCommandsInt();

    /// <remarks>
    /// For unit test purposes
    /// </remarks>
    internal string GenerateCommandLineCommandsInt()
    {
        CommandLineBuilder builder = new();

        //mandatory options
        builder.AppendFileNameIfNotNull(Path.Combine(ContainerizeDirectory, "containerize.dll"));
        builder.AppendSwitch(nameof(PushImageToRegistry));

        // TODO
        return builder.ToString();
    }

}

