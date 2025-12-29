-- Example Lua script for MuninAgent
-- Demonstrates Eggdrop-style binds and IRC commands

-- Public channel command: !hello
bind("pub", "-", "!hello", function(ctx)
    ctx.reply("Hello, " .. ctx.nick .. "! 👋")
    return true -- Handled, stop further processing
end)

-- Private message command: !help (anyone can use)
bind("msg", "-", "!help", function(ctx)
    ctx.reply("MuninAgent Commands:")
    ctx.reply("  !hello - Get a greeting")
    ctx.reply("  !stats - Show channel stats (ops only)")
    ctx.reply("  !who - List channel users")
    return true
end)

-- Stats command (requires op flag)
bind("pub", "o", "!stats", function(ctx)
    local server = agent.server(ctx.server)
    if not server then
        ctx.reply("Server not found")
        return true
    end
    
    ctx.reply(string.format("Connected as %s to %s:%d",
        server.nick, server.host, server.port))
    ctx.reply("Channels: " .. table.concat(server.channels, ", "))
    return true
end)

-- Join event handler
bind("join", "-", "*", function(ctx)
    -- Check if user is in our database
    local user = users.match(ctx.host)
    if user then
        -- Auto-op known operators
        if users.hasflags(user.handle, "o", ctx.channel) then
            putserv(ctx.server, string.format("MODE %s +o %s", ctx.channel, ctx.nick))
            agent.log("info", "Auto-opped " .. ctx.nick .. " (" .. user.handle .. ")")
        end
    end
    return false -- Allow other binds to process
end)

-- Part event handler
bind("part", "-", "*", function(ctx)
    agent.log("debug", ctx.nick .. " left " .. ctx.channel)
    return false
end)

-- CTCP VERSION response
bind("ctcp", "-", "VERSION", function(ctx)
    putnotice(ctx.server, ctx.nick, "\001VERSION MuninAgent 1.0 - https://github.com/your/munin\001")
    return true
end)

-- CTCP PING response
bind("ctcp", "-", "PING", function(ctx)
    putnotice(ctx.server, ctx.nick, "\001PING " .. (ctx.text or "") .. "\001")
    return true
end)

-- Owner-only: raw command execution
bind("msg", "n", "!raw", function(ctx)
    local parts = {}
    for i, arg in ipairs(ctx.args) do
        if i > 1 then -- Skip the command itself
            table.insert(parts, arg)
        end
    end
    
    if #parts > 0 then
        local raw = table.concat(parts, " ")
        putserv(ctx.server, raw)
        ctx.reply("Sent: " .. raw)
    else
        ctx.reply("Usage: !raw <irc command>")
    end
    return true
end)

-- Master-only: reload scripts
bind("msg", "m", "!rehash", function(ctx)
    ctx.reply("Reloading scripts...")
    -- Note: This would need to call into the agent's script manager
    -- For now just acknowledge the command
    ctx.reply("Done!")
    return true
end)

-- Example timer usage (if timer API is available)
-- timer(300, function()
--     agent.log("info", "5-minute timer fired")
-- end, "five_minute_check")

agent.log("info", "Hello script loaded successfully")
