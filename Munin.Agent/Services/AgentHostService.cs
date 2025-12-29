using Microsoft.Extensions.Hosting;
using Munin.Agent.Botnet;
using Munin.Agent.Configuration;
using Munin.Agent.Protection;
using Munin.Agent.Protocol;
using Munin.Agent.Scripting;
using Munin.Agent.Stats;
using Munin.Agent.UserDatabase;
using Serilog;

namespace Munin.Agent.Services;

/// <summary>
/// Main host service that orchestrates the agent components.
/// </summary>
public class AgentHostService : BackgroundService
{
    private readonly ILogger _logger;
    private readonly AgentConfigurationService _configService;
    private readonly AgentUserDatabaseService _userDatabase;
    private readonly IrcBotService _botService;
    private readonly ControlServer _controlServer;
    private readonly AgentScriptManager _scriptManager;
    private readonly BotnetService _botnetService;
    private readonly ChannelProtectionService _protectionService;
    private readonly ChannelStatsService _statsService;
    private readonly IHostApplicationLifetime _lifetime;

    public AgentHostService(
        AgentConfigurationService configService,
        AgentUserDatabaseService userDatabase,
        IrcBotService botService,
        ControlServer controlServer,
        AgentScriptManager scriptManager,
        BotnetService botnetService,
        ChannelProtectionService protectionService,
        ChannelStatsService statsService,
        IHostApplicationLifetime lifetime)
    {
        _logger = Log.ForContext<AgentHostService>();
        _configService = configService;
        _userDatabase = userDatabase;
        _botService = botService;
        _controlServer = controlServer;
        _scriptManager = scriptManager;
        _botnetService = botnetService;
        _protectionService = protectionService;
        _statsService = statsService;
        _lifetime = lifetime;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Information("Munin Agent starting...");
        _logger.Information("Version: {Version}", 
            typeof(AgentHostService).Assembly.GetName().Version?.ToString() ?? "1.0.0");

        // Load user database
        await _userDatabase.LoadAsync();

        // Initialize channel protection
        _protectionService.Initialize();
        _protectionService.ActionTaken += OnProtectionActionTaken;

        // Initialize channel stats
        await _statsService.InitializeAsync();

        // Initialize scripting
        await _scriptManager.InitializeAsync();

        // Wire up IRC events
        _botService.MessageReceived += OnIrcMessageReceived;
        _botService.ConnectionStateChanged += OnConnectionStateChanged;

        // Wire up botnet events
        _botnetService.PartylineMessage += OnPartylineMessage;
        _botnetService.OpRequestReceived += OnOpRequestReceived;
        _botnetService.UserSyncReceived += OnUserSyncReceived;

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Initialize IRC connections
            await _botService.InitializeAsync(stoppingToken);

            // Start botnet service
            await _botnetService.StartAsync(stoppingToken);

            _logger.Information("Munin Agent running");

            // Wait for shutdown signal
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.Fatal(ex, "Fatal error in agent host service");
            _lifetime.StopApplication();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Information("Munin Agent stopping...");

        // Stop botnet
        await _botnetService.StopAsync();

        // Disconnect from all IRC servers
        await _botService.ShutdownAsync("Agent shutting down");

        // Save user database
        await _userDatabase.SaveAsync();

        // Dispose script manager
        _scriptManager.Dispose();

        await base.StopAsync(cancellationToken);

        _logger.Information("Munin Agent stopped");
    }

    private async void OnIrcMessageReceived(object? sender, IrcMessageEventArgs e)
    {
        try
        {
            // Extract message details
            var command = e.Message.Command;
            var nick = ExtractNick(e.Message.Prefix);
            var hostmask = e.Message.Prefix ?? "";
            var channel = e.Message.Parameters.Count > 0 ? e.Message.Parameters[0] : "";
            var text = e.Message.Parameters.Count > 1 ? e.Message.Parameters[1] : "";

            // Process based on command type
            switch (command)
            {
                case "PRIVMSG":
                    if (channel.StartsWith("#"))
                    {
                        // Channel message - run protection and stats
                        var protectionResult = await _protectionService.ProcessMessageAsync(
                            e.ServerId, channel, nick, hostmask, text);

                        if (!protectionResult.ActionTaken)
                        {
                            // Record stats only if not actioned
                            _statsService.RecordMessage(e.ServerId, channel, nick, text);

                            // Dispatch to scripts
                            await DispatchToScriptsAsync(e.ServerId, "pub", nick, hostmask, channel, text);
                        }
                    }
                    else
                    {
                        // Private message
                        await DispatchToScriptsAsync(e.ServerId, "msg", nick, hostmask, null, text);
                    }
                    break;

                case "JOIN":
                    if (nick != _botService.GetConnectionStatus()
                        .FirstOrDefault(c => c.Id == e.ServerId)?.Nickname)
                    {
                        await _protectionService.ProcessJoinAsync(e.ServerId, channel, nick, hostmask);
                        _statsService.RecordJoin(e.ServerId, channel, nick);
                        await DispatchToScriptsAsync(e.ServerId, "join", nick, hostmask, channel, null);
                    }
                    break;

                case "PART":
                    _statsService.RecordPart(e.ServerId, channel, nick);
                    await DispatchToScriptsAsync(e.ServerId, "part", nick, hostmask, channel, text);
                    break;

                case "KICK":
                    var kickedNick = e.Message.Parameters.Count > 1 ? e.Message.Parameters[1] : "";
                    var kickReason = e.Message.Parameters.Count > 2 ? e.Message.Parameters[2] : "";
                    
                    await _protectionService.ProcessKickAsync(e.ServerId, channel, nick, hostmask, kickedNick);
                    _statsService.RecordKick(e.ServerId, channel, nick, kickedNick);
                    await DispatchToScriptsAsync(e.ServerId, "kick", nick, hostmask, channel, kickedNick);
                    break;

                case "NICK":
                    var newNick = e.Message.Parameters.Count > 0 ? e.Message.Parameters[0] : "";
                    await DispatchToScriptsAsync(e.ServerId, "nick", nick, hostmask, null, newNick);
                    break;

                case "MODE":
                    var modeChange = string.Join(" ", e.Message.Parameters.Skip(1));
                    await DispatchToScriptsAsync(e.ServerId, "mode", nick, hostmask, channel, modeChange);
                    break;

                case "TOPIC":
                    _statsService.RecordTopicChange(e.ServerId, channel, nick, text);
                    break;

                case "NOTICE":
                    // Check for CTCP
                    if (text.StartsWith('\x01') && text.EndsWith('\x01'))
                    {
                        var ctcpText = text.Trim('\x01');
                        await DispatchToScriptsAsync(e.ServerId, "ctcp", nick, hostmask, channel, ctcpText);
                    }
                    break;
            }

            // Forward to control clients
            var message = AgentMessage.Create(AgentMessageType.IrcMessage,
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    serverId = e.ServerId,
                    command = e.Message.Command,
                    prefix = e.Message.Prefix,
                    parameters = e.Message.Parameters,
                    timestamp = DateTime.UtcNow
                }));

            _ = _controlServer.BroadcastAsync(message);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error processing IRC message");
        }
    }

    private async Task DispatchToScriptsAsync(
        string serverId, 
        string bindType, 
        string nick, 
        string hostmask, 
        string? channel, 
        string? text)
    {
        var user = _userDatabase.MatchUser(hostmask);
        
        var context = new BindContext
        {
            ServerId = serverId,
            Nick = nick,
            Hostmask = hostmask,
            Channel = channel,
            Text = text,
            Args = text?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>(),
            User = user,
            Reply = async msg =>
            {
                var target = channel ?? nick;
                await _botService.SendMessageAsync(serverId, target, msg);
            }
        };

        await _scriptManager.DispatchBindAsync(bindType, context);
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStateEventArgs e)
    {
        var message = AgentMessage.Create(AgentMessageType.ConnectionStateChanged,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                serverId = e.ServerId,
                state = e.State.ToString(),
                timestamp = DateTime.UtcNow
            }));

        _ = _controlServer.BroadcastAsync(message);
    }

    private void OnProtectionActionTaken(object? sender, ProtectionActionEventArgs e)
    {
        _logger.Information("Protection action: {Type} {Action} on {Nick} in {Channel}: {Reason}",
            e.Type, e.Action, e.Nick, e.Channel, e.Reason);
    }

    private void OnPartylineMessage(object? sender, PartylineMessageEventArgs e)
    {
        _logger.Debug("Partyline [{Channel}] <{Nick}@{Bot}> {Message}", 
            e.Channel, e.Nick, e.FromBot, e.Message);
    }

    private async void OnOpRequestReceived(object? sender, OpRequestEventArgs e)
    {
        _logger.Information("Op request from {Bot}: {Nick} in {Channel}", e.FromBot, e.Nick, e.Channel);

        // Check if we have ops and the user is valid
        var user = _userDatabase.MatchUser(e.Hostmask);
        if (user != null && user.HasFlag(UserFlags.Operator, e.Channel))
        {
            await _botService.SendRawAsync(e.IrcServer, $"MODE {e.Channel} +o {e.Nick}");
            _logger.Information("Granted ops to {Nick} in {Channel} (requested by {Bot})", 
                e.Nick, e.Channel, e.FromBot);
        }
    }

    private void OnUserSyncReceived(object? sender, UserSyncEventArgs e)
    {
        _logger.Information("User sync from {Bot}: {Count} users (full={IsFull})", 
            e.FromBot, e.Users.Count, e.IsFullSync);

        // TODO: Merge synced users into local database
    }

    private static string ExtractNick(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix))
            return "";
        var bangIndex = prefix.IndexOf('!');
        return bangIndex > 0 ? prefix[..bangIndex] : prefix;
    }
}
