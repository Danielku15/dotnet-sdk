// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Formats.Tar;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Microsoft.NET.Build.Containers.IntegrationTests;

public class ImageArchivePusherTests
{
    private ITestOutputHelper _testOutput;
    private readonly TestLoggerFactory _loggerFactory;

    public ImageArchivePusherTests(ITestOutputHelper testOutput)
    {
        _testOutput = testOutput;
        _loggerFactory = new TestLoggerFactory(testOutput);
    }

    [Fact]
    public async Task BuildDestinationImageReferences_LoadFromImageConfiguration_Valid()
    {
        // Arrange
        string imageArchivePath = await CreateTestArchiveAsync(new OciImageIndex
        {
            SchemaVersion = 2,
            MediaType = "application/vnd.oci.image.index.v1+json",
            Manifests = new List<OciImageManifestDescriptor>
            {
                new()
                {
                    // "Test"
                    Size = 4,
                    Digest = "sha256:532eaabd9574880dbf76b9b8cc00832c20a6ec113d682299550d7a6e0f345e25",
                    MediaType = "application/vnd.oci.image.manifest.v1+json",
                    Annotations = new Dictionary<string, string>
                    {
                        [OciAnnotations.AnnotationRefName] = "image:latest"
                    }
                },
                new()
                {
                    // "Test"
                    Size = 4,
                    Digest = "sha256:532eaabd9574880dbf76b9b8cc00832c20a6ec113d682299550d7a6e0f345e25",
                    MediaType = "application/vnd.oci.image.manifest.v1+json",
                    Annotations = new Dictionary<string, string>
                    {
                        [OciAnnotations.AnnotationRefName] = "image:v1"
                    }
                },
                new()
                {
                    // "Test"
                    Size = 4,
                    Digest = "sha256:532eaabd9574880dbf76b9b8cc00832c20a6ec113d682299550d7a6e0f345e25",
                    MediaType = "application/vnd.oci.image.manifest.v1+json",
                    Annotations = new Dictionary<string, string>
                    {
                        [OciAnnotations.AnnotationRefName] = "image2:latest"
                    }
                }
            }
        });

        ImageArchivePusher pusher = new(_loggerFactory, imageArchivePath,
            DockerRegistryManager.LocalRegistry,
            null, null
        );

        // Act
        await pusher.ExtractArchiveAsync(default);
        Assert.False(pusher.HasErrors);
        DestinationImageReference[]? destinations = await pusher.BuildDestinationImageReferencesAsync(default);

        // Assert
        Assert.False(pusher.HasErrors);
        Assert.NotNull(destinations);
        Assert.Collection(destinations,
            destination1 =>
            {
                Assert.Equal("image", destination1.Repository);
                Assert.Collection(destination1.Tags,
                    x => Assert.Equal("latest", x),
                    x => Assert.Equal("v1", x)
                );
                Assert.Equal(DestinationImageReferenceKind.RemoteRegistry, destination1.Kind);
                Assert.NotNull(destination1.RemoteRegistry);
            },
            destination2 =>
            {
                Assert.Equal("image2", destination2.Repository);
                Assert.Collection(destination2.Tags, x => Assert.Equal("latest", x));
                Assert.Equal(DestinationImageReferenceKind.RemoteRegistry, destination2.Kind);
                Assert.NotNull(destination2.RemoteRegistry);
            });
    }

    [Fact]
    public async Task BuildDestinationImageReferences_LoadFromImageConfiguration_MissingIndex()
    {
        // Arrange
        string imageArchivePath = await CreateTestArchiveAsync(null);
        ImageArchivePusher pusher = new(_loggerFactory, imageArchivePath,
            DockerRegistryManager.LocalRegistry,
            null, null
        );

        // Act
        await pusher.ExtractArchiveAsync(default);
        Assert.False(pusher.HasErrors);

        // Assert
        await pusher.BuildDestinationImageReferencesAsync(default);
        Assert.True(pusher.HasErrors);
    }
    [Fact]
    public async Task BuildDestinationImageReferences_LoadFromImageConfiguration_InvalidMix()
    {
        // Arrange
        string imageArchivePath = await CreateTestArchiveAsync(null);
        ImageArchivePusher pusher = new(_loggerFactory, imageArchivePath,
            DockerRegistryManager.LocalRegistry,
            null, null
        );

        // Act
        await pusher.ExtractArchiveAsync(default);
        Assert.False(pusher.HasErrors);

        // Assert
        await pusher.BuildDestinationImageReferencesAsync(default);
        Assert.True(pusher.HasErrors);
    }

    [Fact]
    public async Task BuildDestinationImageReferences_LoadFromImageConfiguration_UnsupportedIndexVersion()
    {
        // Arrange
        string imageArchivePath = await CreateTestArchiveAsync(new OciImageIndex
        {
            SchemaVersion = 1,
            MediaType = "application/vnd.oci.image.index.v1+json",
            Manifests = new List<OciImageManifestDescriptor>()
        });
        ImageArchivePusher pusher = new(_loggerFactory, imageArchivePath,
            DockerRegistryManager.LocalRegistry,
            null, null
        );

        // Act
        await pusher.ExtractArchiveAsync(default);
        Assert.False(pusher.HasErrors);

        // Assert
        await pusher.BuildDestinationImageReferencesAsync(default);
        Assert.True(pusher.HasErrors);
    }

    [Fact]
    public async Task BuildDestinationImageReferences_LoadFromImageConfiguration_UnsupportedMediaType()
    {
        // Arrange
        string imageArchivePath = await CreateTestArchiveAsync(new OciImageIndex
        {
            SchemaVersion = 1,
            MediaType = "application/json",
            Manifests = new List<OciImageManifestDescriptor>()
        });
        ImageArchivePusher pusher = new(_loggerFactory, imageArchivePath,
            DockerRegistryManager.LocalRegistry,
            null, null
        );

        // Act
        await pusher.ExtractArchiveAsync(default);
        Assert.False(pusher.HasErrors);

        // Assert
        await pusher.BuildDestinationImageReferencesAsync(default);
        Assert.True(pusher.HasErrors);
    }

    [Fact]

    public async Task BuildDestinationImageReferences_LoadFromImageConfiguration_NoTags()
    {
        // Arrange
        string imageArchivePath = await CreateTestArchiveAsync(new OciImageIndex
        {
            SchemaVersion = 2,
            MediaType = "application/vnd.oci.image.index.v1+json",
            Manifests = new List<OciImageManifestDescriptor>()
        });

        ImageArchivePusher pusher = new(_loggerFactory, imageArchivePath,
            DockerRegistryManager.LocalRegistry,
            null, null
        );

        // Act
        await pusher.ExtractArchiveAsync(default);
        Assert.False(pusher.HasErrors);

        // Assert
        await pusher.BuildDestinationImageReferencesAsync(default);
        Assert.True(pusher.HasErrors);
    }

    [Fact]
    public async Task BuildDestinationImageReferences_LoadFromImageConfiguration_TagsWithoutRepository()
    {
        // Arrange
        string imageArchivePath = await CreateTestArchiveAsync(new OciImageIndex
        {
            SchemaVersion = 2,
            MediaType = "application/vnd.oci.image.index.v1+json",
            Manifests = new List<OciImageManifestDescriptor>
            {
                new()
                {
                    // "Test"
                    Size = 4,
                    Digest = "sha256:532eaabd9574880dbf76b9b8cc00832c20a6ec113d682299550d7a6e0f345e25",
                    MediaType = "application/vnd.oci.image.manifest.v1+json",
                    Annotations = new Dictionary<string, string>
                    {
                        [OciAnnotations.AnnotationRefName] = "image:latest"
                    }
                },
                new()
                {
                    // "Test"
                    Size = 4,
                    Digest = "sha256:532eaabd9574880dbf76b9b8cc00832c20a6ec113d682299550d7a6e0f345e25",
                    MediaType = "application/vnd.oci.image.manifest.v1+json",
                    Annotations = new Dictionary<string, string>
                    {
                        [OciAnnotations.AnnotationRefName] = "tag"
                    }
                },
            }
        });

        ImageArchivePusher pusher = new(_loggerFactory, imageArchivePath,
            DockerRegistryManager.LocalRegistry,
            null, null
        );

        // Act
        await pusher.ExtractArchiveAsync(default);
        Assert.False(pusher.HasErrors);

        // Assert
        await pusher.BuildDestinationImageReferencesAsync(default);
        Assert.True(pusher.HasErrors);
    }

    [Fact]
    public async Task BuildDestinationImageReferences_ProvideExplicitSettings_Valid()
    {
        // Arrange
        string imageArchivePath = await CreateTestArchiveAsync(new OciImageIndex
        {
            SchemaVersion = 2,
            MediaType = "application/vnd.oci.image.index.v1+json",
            Manifests = new List<OciImageManifestDescriptor>
            {
                new()
                {
                    // "Test"
                    Size = 4,
                    Digest = "sha256:532eaabd9574880dbf76b9b8cc00832c20a6ec113d682299550d7a6e0f345e25",
                    MediaType = "application/vnd.oci.image.manifest.v1+json",
                    Annotations = new Dictionary<string, string>
                    {
                        [OciAnnotations.AnnotationRefName] = "image:latest"
                    }
                },
                new()
                {
                    // "Test"
                    Size = 4,
                    Digest = "sha256:532eaabd9574880dbf76b9b8cc00832c20a6ec113d682299550d7a6e0f345e25",
                    MediaType = "application/vnd.oci.image.manifest.v1+json",
                    Annotations = new Dictionary<string, string>
                    {
                        [OciAnnotations.AnnotationRefName] = "image:v1"
                    }
                }
            }
        });

        ImageArchivePusher pusher = new(_loggerFactory, imageArchivePath,
            DockerRegistryManager.LocalRegistry,
            "other-image", new[]{ "latest" }
        );

        // Act
        await pusher.ExtractArchiveAsync(default);
        Assert.False(pusher.HasErrors);

        DestinationImageReference[]? destinations = await pusher.BuildDestinationImageReferencesAsync(default);

        // Assert
        Assert.False(pusher.HasErrors);
        Assert.NotNull(destinations);
        Assert.Collection(destinations,
            destination1 =>
            {
                Assert.Equal("other-image", destination1.Repository);
                Assert.Collection(destination1.Tags, x => Assert.Equal("latest", x));
                Assert.Equal(DestinationImageReferenceKind.RemoteRegistry, destination1.Kind);
                Assert.NotNull(destination1.RemoteRegistry);
            });
    }

    private async Task<string> CreateTestArchiveAsync(
        OciImageIndex? index,
        [CallerMemberName] string testName = "TestName")
    {
        string workingDirectory = Path.Combine(TestSettings.TestArtifactsDirectory, testName);
        Directory.CreateDirectory(workingDirectory);

        string tarPath = Path.Combine(workingDirectory, "image.tar.gz");
        await using TarWriter writer = new(File.OpenWrite(tarPath), TarEntryFormat.Pax);

        if (index != null)
        {
            string indexJson = JsonSerializer.SerializeToNode(index)?.ToJsonString() ?? "";
            using MemoryStream indexStream = new(Encoding.UTF8.GetBytes(indexJson));
            PaxTarEntry configEntry = new(TarEntryType.RegularFile, "index.json")
            {
                DataStream = indexStream
            };
            await writer.WriteEntryAsync(configEntry).ConfigureAwait(false);
        }

        return tarPath;
    }
}
