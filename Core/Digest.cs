namespace DotNetDockerRegistry.Core;

public record struct Digest
{
    public Digest(string rawValue)
    {
        if (rawValue.StartsWith("sha256:"))
            Hash = rawValue["sha256:".Length..];
        else
            Hash = rawValue;
    }

    public string Hash { get; }

    public override string ToString()
        => $"sha256:{Hash}";
}