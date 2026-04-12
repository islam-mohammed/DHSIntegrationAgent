namespace DHSIntegrationAgent.Application.Providers;

public interface IUserContext
{
    string? UserName { get; set; }
}

public class UserContext : IUserContext
{
    public string? UserName { get; set; }
}
