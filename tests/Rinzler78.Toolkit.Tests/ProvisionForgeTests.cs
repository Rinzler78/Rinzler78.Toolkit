using AwesomeAssertions;
using Xunit;

namespace Rinzler78.Toolkit.Tests;

public sealed class ProvisionForgeTests : IDisposable
{
    private const string NoPolicy = """{"branch_policies":[]}""";

    private readonly ScratchRepository _repository = new("_common.sh", "_provision-forge.sh");

    public void Dispose() => _repository.Dispose();

    private string Provision(string rulesets = "[]", string policies = NoPolicy)
    {
        _repository.GitHubRecords(rulesets, policies);
        var result = _repository.Script("_provision-forge.sh", "owner/repository");
        result.Succeeded.Should().BeTrue(result.Error);
        return _repository.GitHubCalls;
    }

    private static string CallTo(string calls, string request) =>
        calls.Split("---\n").Single(call => call.StartsWith(request, StringComparison.Ordinal));

    private static string RulesetNamed(string calls, string name) =>
        calls.Split("---\n").Single(call => call.Contains($"\"name\":\"{name}\"", StringComparison.Ordinal));

    [Fact]
    public void Workflows_get_a_read_only_token_and_cannot_approve_pull_requests()
    {
        var calls = Provision();

        CallTo(calls, "api -X PUT repos/owner/repository/actions/permissions/workflow")
            .Should().Contain("default_workflow_permissions=read")
            .And.Contain("can_approve_pull_request_reviews=false");
    }

    [Fact]
    public void The_release_environment_admits_release_tags_only()
    {
        var calls = Provision();

        CallTo(calls, "api -X PUT repos/owner/repository/environments/release")
            .Should().Contain("\"custom_branch_policies\":true")
            .And.Contain("\"protected_branches\":false");
        CallTo(calls, "api -X POST repos/owner/repository/environments/release/deployment-branch-policies")
            .Should().Contain("name=v*").And.Contain("type=tag");
    }

    [Fact]
    public void An_existing_release_tag_policy_is_not_added_twice()
    {
        var calls = Provision(policies: """{"branch_policies":[{"id":7,"name":"v*","type":"tag"}]}""");

        calls.Should().NotContain("-X POST repos/owner/repository/environments/release/deployment-branch-policies");
    }

    [Fact]
    public void Develop_takes_squashed_pull_requests_only_with_a_linear_signed_history()
    {
        var develop = RulesetNamed(Provision(), "develop");

        develop.Should().StartWith("api -X POST repos/owner/repository/rulesets --input -")
            .And.Contain("refs/heads/develop")
            .And.Contain("\"required_linear_history\"")
            .And.Contain("\"required_signatures\"")
            .And.Contain("\"allowed_merge_methods\":[\"squash\"]")
            .And.Contain("\"context\":\"verify\"")
            .And.Contain("\"context\":\"lint\"");
    }

    [Fact]
    public void Master_takes_merge_commits_only_and_no_linear_history_rule()
    {
        var master = RulesetNamed(Provision(), "master");

        master.Should().Contain("refs/heads/master")
            .And.Contain("\"allowed_merge_methods\":[\"merge\"]")
            .And.Contain("\"required_signatures\"")
            .And.NotContain("required_linear_history");
    }

    [Fact]
    public void Release_tags_can_be_neither_moved_nor_deleted()
    {
        var tags = RulesetNamed(Provision(), "release tags");

        tags.Should().Contain("\"target\":\"tag\"")
            .And.Contain("refs/tags/v*")
            .And.Contain("\"type\":\"update\"")
            .And.Contain("\"type\":\"deletion\"");
    }

    [Fact]
    public void An_existing_ruleset_is_replaced_in_place_rather_than_duplicated()
    {
        var calls = Provision(rulesets: """[{"id":42,"name":"develop"}]""");

        calls.Should().Contain("api -X PUT repos/owner/repository/rulesets/42 --input -");
        calls.Split("---\n").Should().NotContain(call =>
            call.StartsWith("api -X POST repos/owner/repository/rulesets", StringComparison.Ordinal) &&
            call.Contains("\"name\":\"develop\"", StringComparison.Ordinal));
    }
}
