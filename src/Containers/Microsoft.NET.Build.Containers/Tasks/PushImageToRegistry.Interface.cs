// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Build.Framework;

namespace Microsoft.NET.Build.Containers.Tasks;

partial class PushImageToRegistry
{
    /// <summary>
    /// The path to the OCI image archive to push to the registry.
    /// </summary>
    [Required]
    public string ArchivePath { get; set; }

    /// <summary>
    /// The registry to push to.
    /// </summary>
    [Required]
    public string Registry { get; set; }

    /// <summary>
    /// The name of the output image that will be pushed to the registry.
    /// If not provided, the name will be extracted from the "org.opencontainers.image.ref.name"
    /// annotation in the archive. 
    /// </summary>
    public string? Repository { get; set; }

    /// <summary>
    /// The tags to associate with the pushed image in the registry.
    /// If not provided the tags will be extracted from the "org.opencontainers.image.ref.name"
    /// annotation in the archive.
    /// </summary>
    [Required]
    public string[]? ImageTags { get; set; }


    public PushImageToRegistry()
    {
        ArchivePath = "";
        Registry = "";
    }
}
