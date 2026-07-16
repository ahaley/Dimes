using Dimes.Domain.WorkOrders;

namespace Dimes.Tests;

/// <summary>The parser for the commit contract the export itself specifies. A full-GUID trailer is a
/// machine-readable claim of completion, so what does and doesn't count as one is the whole game.</summary>
public class WorkOrderTrailerTests
{
    private const string Id = "1f2e3d4c-5b6a-4798-8765-43210fedcba9";

    [Fact]
    public void ParseTrailers_ReadsATrailerAtTheEndOfACommitMessage()
    {
        var ids = WorkOrderTrailer.ParseTrailers($"Add CSV export\n\nDimes change {Id}");

        Assert.Equal([Guid.Parse(Id)], ids);
    }

    [Fact]
    public void ParseTrailers_ReadsATrailerFollowedByMoreLines()
    {
        var ids = WorkOrderTrailer.ParseTrailers(
            $"Add CSV export\n\nDimes change {Id}\nCo-Authored-By: Someone <a@b.c>");

        Assert.Equal([Guid.Parse(Id)], ids);
    }

    [Fact]
    public void ParseTrailers_ReadsEveryClaimWhenACommitCoversSeveralChanges()
    {
        var other = "9a8b7c6d-5e4f-4a3b-8c2d-1e0f9a8b7c6d";

        var ids = WorkOrderTrailer.ParseTrailers($"Shared refactor\n\nDimes change {Id}\nDimes change {other}");

        Assert.Equal([Guid.Parse(Id), Guid.Parse(other)], ids);
    }

    [Fact]
    public void ParseTrailers_IsCaseInsensitiveOnTheKeyword()
    {
        var ids = WorkOrderTrailer.ParseTrailers($"Title\n\nDIMES CHANGE {Id.ToUpperInvariant()}");

        Assert.Equal([Guid.Parse(Id)], ids);
    }

    [Theory]
    [InlineData("  Dimes change {0}")]
    [InlineData("\tDimes change {0}")]
    [InlineData("Dimes  change  {0}")]
    [InlineData("Dimes change {0}.")]
    [InlineData("Dimes change {0}   ")]
    public void ParseTrailers_ToleratesTheWhitespaceAndPunctuationToolsAddOrReflow(string template)
    {
        var ids = WorkOrderTrailer.ParseTrailers("Title\n\n" + string.Format(template, Id));

        Assert.Equal([Guid.Parse(Id)], ids);
    }

    [Fact]
    public void ParseTrailers_IgnoresTheKeywordMidSentence()
    {
        // Prose that merely mentions a change is not a claim that it's done — otherwise quoting a work
        // order back in a commit body would silently mark it reported.
        var ids = WorkOrderTrailer.ParseTrailers(
            $"Discussion\n\nAs noted in Dimes change {Id} we should revisit this later.");

        Assert.Empty(ids);
    }

    [Fact]
    public void ParseTrailers_IgnoresAMalformedId()
    {
        Assert.Empty(WorkOrderTrailer.ParseTrailers("Title\n\nDimes change not-a-guid"));
        Assert.Empty(WorkOrderTrailer.ParseTrailers("Title\n\nDimes change 1f2e3d4c"));
    }

    [Fact]
    public void ParseTrailers_CollapsesARepeatedClaim()
    {
        var ids = WorkOrderTrailer.ParseTrailers($"Title\n\nDimes change {Id}\nDimes change {Id}");

        Assert.Equal([Guid.Parse(Id)], ids);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Just a subject line")]
    public void ParseTrailers_ReturnsNothingForAMessageWithoutATrailer(string? message)
    {
        Assert.Empty(WorkOrderTrailer.ParseTrailers(message));
    }

    [Fact]
    public void ParseBranchIdPrefix_ReadsTheIdPrefixFromAMintedBranchName()
    {
        Assert.Equal("1f2e3d4c", WorkOrderTrailer.ParseBranchIdPrefix("change/1f2e3d4c-add-csv-export"));
    }

    [Fact]
    public void ParseBranchIdPrefix_LowercasesSoMatchingIsCaseInsensitive()
    {
        Assert.Equal("1f2e3d4c", WorkOrderTrailer.ParseBranchIdPrefix("change/1F2E3D4C-add-csv-export"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("main")]
    [InlineData("feature/add-csv-export")]
    [InlineData("change/short-slug")]
    [InlineData("change/1f2e3d4c")]
    public void ParseBranchIdPrefix_ReturnsNullForABranchWeDidntMint(string? branch)
    {
        Assert.Null(WorkOrderTrailer.ParseBranchIdPrefix(branch));
    }
}
