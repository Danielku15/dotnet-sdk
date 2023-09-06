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
public abstract class ContainerizeToolTask : ToolTask, ICancelableTask
{
    // Unused, ToolExe is set via targets and overrides this.
    protected override string ToolName => "dotnet";

    private (bool success, string user, string pass) extractionInfo;

    private string DotNetPath
    {
        get
        {
            string path = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "";
            if (string.IsNullOrEmpty(path))
            {
                path = string.IsNullOrEmpty(ToolPath) ? "" : ToolPath;
            }

            return path;
        }
    }

    protected override string GenerateFullPathToTool() => Path.Combine(DotNetPath, ToolExe);

    /// <summary>
    /// Workaround to avoid storing user/pass into the EnvironmentVariables property, which gets logged by the task.
    /// </summary>
    /// <param name="pathToTool"></param>
    /// <param name="commandLineCommands"></param>
    /// <param name="responseFileSwitch"></param>
    /// <returns></returns>
    protected override ProcessStartInfo GetProcessStartInfo(string pathToTool, string commandLineCommands, string responseFileSwitch)
    {
        VSHostObject hostObj = new VSHostObject(HostObject as System.Collections.Generic.IEnumerable<ITaskItem>);
        if (hostObj.ExtractCredentials(out string user, out string pass, (string s) => Log.LogWarning(s)))
        {
            extractionInfo = (true, user, pass);
        }
        else
        {
            Log.LogMessage(MessageImportance.Low, Resource.GetString(nameof(Strings.HostObjectNotDetected)));
        }

        ProcessStartInfo startInfo = base.GetProcessStartInfo(pathToTool, commandLineCommands, responseFileSwitch)!;

        if (extractionInfo.success)
        {
            startInfo.Environment[ContainerHelpers.HostObjectUser] = extractionInfo.user;
            startInfo.Environment[ContainerHelpers.HostObjectPass] = extractionInfo.pass;
        }

        return startInfo;
    }
}

