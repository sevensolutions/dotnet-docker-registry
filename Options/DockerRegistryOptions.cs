using System.ComponentModel.DataAnnotations;

namespace DotNetDockerRegistry.Options;

public sealed class DockerRegistryOptions
{
    [Required]
    public DockerRegistryStorageOptions Storage { get; set; } = default!;
}

public sealed class DockerRegistryStorageOptions
{
    [Required]
    public DockerRegistryS3StorageOptions S3 { get; set; } = default!;
}

public sealed class DockerRegistryS3StorageOptions
{
    [Required]
    public string ServiceUrl { get; set; } = default!;
    public bool ForcePathStyle { get; set; }
    [Required]
    public string AccessKeyId { get; set; } = default!;
    [Required]
    public string SecretAccessKey { get; set; } = default!;
    [Required]
    public string BucketName { get; set; } = default!;
}
