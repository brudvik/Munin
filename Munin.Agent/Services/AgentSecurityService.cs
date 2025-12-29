using Munin.Core.Services;
using Serilog;

namespace Munin.Agent.Services;

/// <summary>
/// Service that manages encryption and authentication for the agent.
/// Wraps the core EncryptionService with agent-specific functionality.
/// </summary>
public class AgentSecurityService
{
    private readonly ILogger _logger;
    private readonly EncryptionService _encryptionService;
    private string? _authToken;
    private bool _isInitialized;

    /// <summary>
    /// Gets whether the security service is initialized and ready.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the underlying encryption service.
    /// </summary>
    public EncryptionService EncryptionService => _encryptionService;

    public AgentSecurityService()
    {
        _logger = Log.ForContext<AgentSecurityService>();
        _encryptionService = new EncryptionService();
    }

    /// <summary>
    /// Initializes the security service with the auth token.
    /// </summary>
    /// <param name="authToken">The authentication token from configuration.</param>
    public void Initialize(string authToken)
    {
        if (string.IsNullOrEmpty(authToken))
            throw new ArgumentException("Auth token cannot be empty", nameof(authToken));

        _authToken = authToken;
        _isInitialized = true;
        _logger.Information("Security service initialized");
    }

    /// <summary>
    /// Creates a new authentication challenge.
    /// </summary>
    /// <returns>Challenge bytes.</returns>
    public byte[] CreateChallenge()
    {
        return AgentSecurity.CreateChallenge();
    }

    /// <summary>
    /// Verifies a challenge response from a client.
    /// </summary>
    /// <param name="challenge">The original challenge sent to client.</param>
    /// <param name="response">The response received from client.</param>
    /// <returns>True if authentication successful.</returns>
    public bool VerifyAuthentication(byte[] challenge, byte[] response)
    {
        if (!_isInitialized || _authToken == null)
        {
            _logger.Warning("Security service not initialized");
            return false;
        }

        var isValid = AgentSecurity.VerifyChallengeResponse(challenge, response, _authToken);
        
        if (!isValid)
        {
            _logger.Warning("Authentication challenge verification failed");
        }

        return isValid;
    }

    /// <summary>
    /// Validates if an IP address is allowed to connect.
    /// </summary>
    /// <param name="ip">The IP address to check.</param>
    /// <param name="allowedList">List of allowed IPs.</param>
    /// <returns>True if allowed.</returns>
    public bool IsIpAllowed(string ip, IEnumerable<string> allowedList)
    {
        return AgentSecurity.IsIpAllowed(ip, allowedList);
    }

    /// <summary>
    /// Cleans up sensitive data on shutdown.
    /// </summary>
    public void Cleanup()
    {
        _authToken = null;
        _encryptionService.WipeMemory();
        _isInitialized = false;
        _logger.Debug("Security service cleaned up");
    }
}
