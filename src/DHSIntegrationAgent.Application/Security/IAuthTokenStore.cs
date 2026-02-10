namespace DHSIntegrationAgent.Application.Security;

// In-memory token store.
// Used by the auth header provider to attach Authorization headers automatically.
public interface IAuthTokenStore
{
    // Returns the current token (null/empty if not logged in or backend doesn't use tokens).
    string? GetToken();

    // Sets the current token (null clears it).
    void SetToken(string? token);

    // Clears the token (logout/failed login cleanup).
    void Clear();
}
