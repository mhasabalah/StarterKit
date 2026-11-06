using System.Text.Json.Nodes;
using Identity.Domains.Scim.Models;
using Identity.Domains.Users.Models;
using Identity.Services.Scim;

namespace Identity.Tests.Sso;

public class ScimEnterpriseTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Enterprise_attributes_are_read_from_a_resource()
    {
        var manager = Guid.NewGuid();
        var resource = new JsonObject
        {
            [ScimSchemas.EnterpriseUser] = new JsonObject
            {
                ["employeeNumber"] = "E-1",
                ["costCenter"] = "CC",
                ["organization"] = "Acme",
                ["division"] = "EMEA",
                ["department"] = "Platform",
                ["manager"] = new JsonObject { ["value"] = manager.ToString() }
            }
        };

        var enterprise = ScimEnterpriseJson.FromResource(resource)!;

        enterprise.Should().Be(new ScimEnterprise("E-1", "CC", "Acme", "EMEA", "Platform", manager));
    }

    [Fact]
    public void A_resource_without_the_extension_has_none()
    {
        ScimEnterpriseJson.FromResource(new JsonObject { ["userName"] = "a" }).Should().BeNull();
    }

    [Theory]
    [InlineData("department", "Sales")]
    [InlineData("DEPARTMENT", "Sales")]
    public void Patching_an_attribute_sets_it_and_an_empty_value_clears_it(string name, string value)
    {
        var set = ScimEnterpriseJson.Apply(ScimEnterprise.Empty, name, JsonValue.Create(value));
        set.Department.Should().Be(value);

        ScimEnterpriseJson.Apply(set, name, JsonValue.Create("")).Department.Should().BeNull();
        ScimEnterpriseJson.Apply(set, name, null).Department.Should().BeNull();
    }

    [Fact]
    public void Manager_accepts_an_object_a_manager_dot_value_path_and_clears_on_empty()
    {
        var id = Guid.NewGuid();

        var byObject = ScimEnterpriseJson.Apply(ScimEnterprise.Empty, "manager", new JsonObject { ["value"] = id.ToString() });
        var byPath = ScimEnterpriseJson.Apply(ScimEnterprise.Empty, "manager.value", JsonValue.Create(id.ToString()));

        byObject.ManagerId.Should().Be(id);
        byPath.ManagerId.Should().Be(id);
        ScimEnterpriseJson.Apply(byPath, "manager", JsonValue.Create("")).ManagerId.Should().BeNull();
        ScimEnterpriseJson.Apply(byPath, "manager.displayName", JsonValue.Create("Boss")).ManagerId.Should().Be(id);
    }

    [Fact]
    public void An_unknown_enterprise_attribute_is_an_invalidPath()
    {
        var act = () => ScimEnterpriseJson.Apply(ScimEnterprise.Empty, "shoeSize", JsonValue.Create("9"));

        act.Should().Throw<ScimException>().Which.Should().Match<ScimException>(e => e.Status == 400 && e.ScimType == "invalidPath");
    }

    [Theory]
    [InlineData("active", true)]
    [InlineData("name.givenName", true)]
    [InlineData("emails[type eq \"work\"].value", true)]
    [InlineData("phoneNumbers[type eq \"work\"].value", true)]
    [InlineData("urn:ietf:params:scim:schemas:core:2.0:User:userName", true)]
    [InlineData("favouriteColour", false)]
    [InlineData("urn:example:custom:2.0:User:foo", false)]
    public void Only_attributes_of_the_user_schema_are_known_paths(string path, bool known) =>
        ScimUserPaths.IsKnownCore(path).Should().Be(known);

    [Theory]
    [InlineData("urn:ietf:params:scim:schemas:extension:enterprise:2.0:User", "")]
    [InlineData("urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:department", "department")]
    [InlineData("URN:IETF:PARAMS:SCIM:SCHEMAS:EXTENSION:ENTERPRISE:2.0:USER:manager.value", "manager.value")]
    [InlineData("urn:ietf:params:scim:schemas:extension:enterprise:2.0:UserX", null)]
    [InlineData("department", null)]
    public void Enterprise_paths_are_recognised_case_insensitively(string path, string? remainder) =>
        ScimUserPaths.EnterpriseRemainder(path).Should().Be(remainder);

    [Fact]
    public void The_user_resource_returns_the_extension_and_its_schema_only_when_set()
    {
        var user = new ApplicationUser { Id = Guid.NewGuid(), Email = "a@acme.example", FirstName = "A", LastName = "B" };
        var plain = ScimUser.Create(Guid.NewGuid(), user.Id, "a@acme.example", null, true, null, Now);
        var manager = Guid.NewGuid();
        var enriched = ScimUser.Create(Guid.NewGuid(), user.Id, "a@acme.example", null, true,
            new ScimEnterprise(Department: "Platform", ManagerId: manager), Now);

        var without = ScimJson.User(plain, user, "https://api.example.com/scim/v2");
        var with = ScimJson.User(enriched, user, "https://api.example.com/scim/v2");

        without[ScimSchemas.EnterpriseUser].Should().BeNull();
        without["schemas"]!.AsArray().Should().HaveCount(1);
        with["schemas"]!.AsArray().Select(s => s!.GetValue<string>()).Should().Contain(ScimSchemas.EnterpriseUser);
        with[ScimSchemas.EnterpriseUser]!["department"]!.GetValue<string>().Should().Be("Platform");
        with[ScimSchemas.EnterpriseUser]!["manager"]!["value"]!.GetValue<string>().Should().Be(manager.ToString());
        with[ScimSchemas.EnterpriseUser]!["manager"]!["$ref"]!.GetValue<string>().Should().EndWith($"/Users/{manager}");
    }

    [Fact]
    public void A_user_cannot_be_their_own_manager_and_update_replaces_the_extension()
    {
        var userId = Guid.NewGuid();
        var scim = ScimUser.Create(Guid.NewGuid(), userId, "a@acme.example", null, true,
            new ScimEnterprise(Department: "A", ManagerId: userId), Now);
        scim.ManagerScimUserId.Should().BeNull();

        scim.Update("a@acme.example", null, true, new ScimEnterprise(CostCenter: "CC"), Now);

        scim.Department.Should().BeNull();
        scim.CostCenter.Should().Be("CC");
        scim.Revision.Should().Be(2);
    }
}
