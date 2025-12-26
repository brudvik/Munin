-- Example Lua Script for Munin IRC Client
-- This script demonstrates basic event handling and IRC commands

-- Respond to !hello command
on.message = function(e)
    -- Check if the message is a command
    if e.text and e.text:match("^!hello") then
        e.reply("Hello, " .. e.nick .. "! 👋")
    end
    
    -- Auto-react to mentions of "munin"
    if e.text and e.text:lower():match("munin") then
        print("Someone mentioned Munin in " .. e.channel)
    end
end

-- Welcome users when they join
on.join = function(e)
    -- Only greet in specific channels
    if e.channel == "#welcome" then
        local server = irc.server(e.server)
        if server then
            server:say(e.channel, "Welcome to the channel, " .. e.nick .. "! Type !help for commands.")
        end
    end
end

-- Log when users leave
on.part = function(e)
    print(e.nick .. " left " .. e.channel .. " (" .. (e.reason or "no reason") .. ")")
end

-- React to topic changes
on.topic = function(e)
    print("Topic in " .. e.channel .. " changed to: " .. e.topic)
end

-- Example: Auto-respond to CTCP VERSION
on.ctcp = function(e)
    if e.ctcp == "VERSION" then
        -- Note: Munin handles CTCP automatically, this is just an example
        print("Received VERSION request from " .. e.nick)
    end
end

print("Hello script loaded!")
