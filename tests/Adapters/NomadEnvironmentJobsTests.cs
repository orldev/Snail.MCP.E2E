using Snail.MCP.E2E.Adapters;
using Snail.Toolkit.Nomad.Models;

namespace Snail.MCP.E2E.Tests.Adapters;

public sealed class NomadEnvironmentJobsTests
{
    private static Allocation Of(string id, string desired, long created) =>
        new(id, "e2e-shop.app[0]", "e2e-shop", "app", "node", DesiredStatus: desired, CreateTime: created);

    [Fact]
    public void Latest_OldestListedFirst_TakesTheNewestMeantToRun()
    {
        NomadPage<Allocation> page = new([Of("old", "run", 1), Of("stopped", "stop", 3), Of("new", "run", 2)]);

        Assert.Equal("new", NomadEnvironmentJobs.Latest(page)?.Id);
    }

    [Fact]
    public void Latest_NoneMeantToRun_TakesTheNewestOfAll()
    {
        NomadPage<Allocation> page = new([Of("old", "stop", 1), Of("new", "stop", 2)]);

        Assert.Equal("new", NomadEnvironmentJobs.Latest(page)?.Id);
    }

    [Fact]
    public void Latest_NoAllocations_IsNull()
    {
        Assert.Null(NomadEnvironmentJobs.Latest(new NomadPage<Allocation>([])));
    }
}
