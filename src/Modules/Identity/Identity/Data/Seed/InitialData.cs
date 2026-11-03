namespace Identity.Data.Seed;

public static class InitialData
{
    public static IEnumerable<ApplicationRole> Roles => new List<ApplicationRole>
    {
        new(RoleConstants.RoleIds.User, RoleConstants.User, RoleConstants.RoleDescriptions.User),
        new(RoleConstants.RoleIds.Admin, RoleConstants.Admin, RoleConstants.RoleDescriptions.Admin),
        new(RoleConstants.RoleIds.SuperAdmin, RoleConstants.SuperAdmin, RoleConstants.RoleDescriptions.SuperAdmin),
        new(RoleConstants.RoleIds.Finance, RoleConstants.Finance, RoleConstants.RoleDescriptions.Finance),
        new(RoleConstants.RoleIds.Support, RoleConstants.Support, RoleConstants.RoleDescriptions.Support)
    };

    /// <summary>
    /// Fixed identity of the initial SuperAdmin. Email/user name/password come from <c>Identity:SeedAdmin</c>;
    /// there is deliberately no password in source.
    /// </summary>
    public static class SuperAdminSeed
    {
        public static readonly Guid Id = Guid.Parse("55555555-5555-5555-5555-555555555555");
        public const string FirstName = "Super";
        public const string LastName = "Admin";
    }
}
