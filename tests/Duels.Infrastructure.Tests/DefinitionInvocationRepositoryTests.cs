using Duels.Domain.Entities;
using Duels.Infrastructure.Persistence;
using Xunit;

namespace Duels.Infrastructure.Tests;

public class DefinitionInvocationRepositoryTests
{
    // invocations.json is an intentional content stub for M0 — schema and
    // pipeline exist, content lands in M4.
    [Fact]
    public void LoadsRealInvocationsJson_Empty()
    {
        var repo = new DefinitionInvocationRepository();
        Assert.Empty(repo.GetAll());
        Assert.Null(repo.Get("anything"));
    }

    [Fact]
    public void ThrowsOnDuplicateInvocationId()
    {
        var definitions = new List<InvocationDefinition>
        {
            new("dupe", "Dupe A", 10, "effect a", "A", ["H"]),
            new("dupe", "Dupe B", 10, "effect b", "A", ["H"]),
        };

        var ex = Assert.Throws<InvalidOperationException>(() => new DefinitionInvocationRepository(definitions));
        Assert.Contains("dupe", ex.Message);
    }
}
