using Noosphere.Domain;

namespace Noosphere.Tests;

public class RulesTests
{
    [Theory]
    [InlineData("Customer portal", "customer-portal")]
    [InlineData("Réseau Auto+", "reseau-auto")]
    [InlineData("  --Bank (SEPA)-- ", "bank-sepa")]
    [InlineData("!!!", "item")]
    public void Slugify_builds_kebab_case_ids(string name, string expected) => Assert.Equal(expected, Rules.Slugify(name));

    [Theory]
    [InlineData("crm-to-billing-customers", true)]
    [InlineData("a1", true)]
    [InlineData("Crm", false)]
    [InlineData("crm--billing", false)]
    [InlineData("-crm", false)]
    [InlineData("crm_billing", false)]
    [InlineData("", false)]
    public void Ids_are_kebab_case_slugs(string id, bool valid) => Assert.Equal(valid, Rules.IsValidId(id));

    [Fact]
    public void Long_names_give_ids_of_at_most_64_characters()
    {
        var id = Rules.Slugify(new string('a', 30) + " " + new string('b', 60));
        Assert.True(Rules.IsValidId(id));
        Assert.Equal(64, id.Length);
    }

    [Fact]
    public void Application_completeness_counts_team_tech_description_flow_and_lifecycle()
    {
        var app = new Application { Name = "X", DomainId = "d" };
        Assert.Equal(0.2, Rules.Completeness(app, hasFlow: false));
        app.TeamId = "t";
        app.Tech = "Go";
        Assert.Equal(0.6, Rules.Completeness(app, hasFlow: false));
        app.Description = "Does things.";
        Assert.Equal(1, Rules.Completeness(app, hasFlow: true));
    }

    [Fact]
    public void Flow_completeness_counts_schedule_objects_owner_and_description()
    {
        var flow = new Flow { Name = "F", FromId = "a", ToId = "b" };
        Assert.Equal(0, Rules.Completeness(flow));
        flow.Schedule = "Daily";
        flow.DataObjectIds = ["customer"];
        Assert.Equal(0.5, Rules.Completeness(flow));
        flow.OwnerTeamId = "t";
        Assert.Equal(0.75, Rules.Completeness(flow));
    }

    [Fact]
    public void Flow_sensitivity_is_the_most_sensitive_carried_object()
    {
        Assert.Equal(Sensitivity.Sensitive, Rules.FlowSensitivity([Sensitivity.Internal, Sensitivity.Sensitive, Sensitivity.Personal]));
        Assert.Equal(Sensitivity.Public, Rules.FlowSensitivity([Sensitivity.Public]));
        Assert.Equal(Sensitivity.Internal, Rules.FlowSensitivity([]));
    }

    [Fact]
    public void Fold_ignores_accents_and_case() => Assert.Equal("reseau auto+ eleve", Rules.Fold("Réseau Auto+ ÉLÈVE"));
}
