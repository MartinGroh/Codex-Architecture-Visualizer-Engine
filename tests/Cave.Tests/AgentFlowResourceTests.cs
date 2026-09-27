using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Cave.Tests;

/// <summary>Verifies portable resources ship with the host and match the dedicated API.</summary>
public sealed class AgentFlowResourceTests
{
    /// <summary>Verifies the downloadable pack contains the standalone contract, clients, assets and license.</summary>
    [Fact]
    public async Task HostIncludesPortablePackAndStandaloneSchema()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.UseSetting("Cave:DemoMode", "true"));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/agent-flow/resources");
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/zip", response.Content.Headers.ContentType?.MediaType);
        using var bytes = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var archive = new ZipArchive(bytes, ZipArchiveMode.Read);
        Assert.NotNull(archive.GetEntry("LICENSE"));
        Assert.NotNull(archive.GetEntry("README.md"));
        Assert.NotNull(archive.GetEntry("embedded/cave_agent_flow.h"));
        Assert.NotNull(archive.GetEntry("examples/poll.mjs"));
        Assert.NotNull(archive.GetEntry("assets/agent_flow_bitmaps.h"));
        Assert.All(archive.Entries, entry => Assert.DoesNotContain("..", entry.FullName));

        using var packagedSchema = await JsonDocument.ParseAsync(archive.GetEntry("schema.json")!.Open());
        using var servedSchema = JsonDocument.Parse(await client.GetStringAsync("/api/agent-flow/schema"));
        Assert.Equal(packagedSchema.RootElement.GetRawText(), servedSchema.RootElement.GetRawText());
        using var example = await JsonDocument.ParseAsync(archive.GetEntry("example.json")!.Open());
        AssertRequiredShape(example.RootElement, servedSchema.RootElement, servedSchema.RootElement);
        Assert.Equal("Demo", example.RootElement.GetProperty("sourceMode").GetString());
    }

    private static void AssertRequiredShape(JsonElement value, JsonElement schema, JsonElement root)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            AssertRequiredShape(value, root.GetProperty("$defs").GetProperty(reference.GetString()![8..]), root);
            return;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        if (schema.TryGetProperty("anyOf", out var variants))
        {
            AssertRequiredShape(value, variants[1], root);
            return;
        }

        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var property in required.EnumerateArray())
            {
                var name = property.GetString()!;
                Assert.True(value.TryGetProperty(name, out var member), $"Missing resource contract property: {name}");
                AssertRequiredShape(member, schema.GetProperty("properties").GetProperty(name), root);
            }
        }

        if (schema.TryGetProperty("items", out var items))
        {
            foreach (var member in value.EnumerateArray())
            {
                AssertRequiredShape(member, items, root);
            }
        }
    }
}
