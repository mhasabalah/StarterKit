namespace Identity.Contracts.Users.Queries;

public record CheckUserEmailExistsQuery(string Email) : IQuery<bool>;
