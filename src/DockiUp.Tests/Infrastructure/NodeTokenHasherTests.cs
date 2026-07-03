using DockiUp.Infrastructure.Services;

namespace DockiUp.Tests.Infrastructure;

public class NodeTokenHasherTests
{
    [Fact]
    public void Hash_IsDeterministic_ForSameToken()
    {
        var a = NodeTokenHasher.Hash("my-token");
        var b = NodeTokenHasher.Hash("my-token");
        Assert.Equal(a, b);
    }

    [Fact]
    public void Hash_DiffersForDifferentTokens()
    {
        Assert.NotEqual(NodeTokenHasher.Hash("token-a"), NodeTokenHasher.Hash("token-b"));
    }

    [Fact]
    public void Hash_ProducesBase64Sha256_44Chars()
    {
        var hash = NodeTokenHasher.Hash("anything");
        // SHA-256 = 32 bytes -> 44 base64 chars (with padding).
        Assert.Equal(44, hash.Length);
        Assert.Equal(Convert.FromBase64String(hash).Length, 32);
    }

    [Fact]
    public void Generate_ProducesUrlSafeToken_WithoutPadding()
    {
        var token = NodeTokenHasher.Generate();
        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
        Assert.False(string.IsNullOrWhiteSpace(token));
    }

    [Fact]
    public void Generate_IsUnique_AcrossCalls()
    {
        var tokens = Enumerable.Range(0, 100).Select(_ => NodeTokenHasher.Generate()).ToHashSet();
        Assert.Equal(100, tokens.Count);
    }
}
