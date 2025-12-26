-- Advanced Lua Script Example: Auto-Op Bot
-- Demonstrates timers, storage, and more complex IRC operations

-- Configuration
local config = {
    opChannel = "#mychannel",
    opPassword = "secretpass",
    autoOpEnabled = true
}

-- Store authorized users in persistent storage
local function loadAuthorizedUsers()
    local users = storage.get("autoop_users")
    if users then
        return users
    end
    return {}
end

local function saveAuthorizedUsers(users)
    storage.set("autoop_users", users)
end

-- Check if user is authorized
local function isAuthorized(nick)
    local users = loadAuthorizedUsers()
    for _, user in ipairs(users) do
        if user:lower() == nick:lower() then
            return true
        end
    end
    return false
end

-- Add user to authorized list
local function addAuthorizedUser(nick)
    local users = loadAuthorizedUsers()
    table.insert(users, nick)
    saveAuthorizedUsers(users)
end

-- Handle messages
on.message = function(e)
    if not e.text then return end
    
    local cmd, args = e.text:match("^!(%w+)%s*(.*)")
    if not cmd then return end
    
    cmd = cmd:lower()
    
    if cmd == "autoop" then
        -- Toggle auto-op
        if isAuthorized(e.nick) then
            config.autoOpEnabled = not config.autoOpEnabled
            e.reply("Auto-op is now " .. (config.autoOpEnabled and "enabled" or "disabled"))
        else
            e.reply("You are not authorized to use this command.")
        end
        
    elseif cmd == "addop" and args ~= "" then
        -- Add user to auto-op list
        if isAuthorized(e.nick) then
            addAuthorizedUser(args)
            e.reply(args .. " added to auto-op list.")
        end
        
    elseif cmd == "opme" then
        -- Request op with password
        local password = args
        if password == config.opPassword then
            local channel = irc.server(e.server):channel(e.channel)
            if channel then
                channel:mode("+o " .. e.nick)
            end
        else
            -- Delay response to prevent brute force
            timer.timeout(function()
                e.reply("Invalid password.")
            end, 2000)
        end
        
    elseif cmd == "stats" then
        -- Show some stats
        local server = irc.server(e.server)
        if server then
            local channel = server:channel(e.channel)
            if channel then
                e.reply("Channel " .. e.channel .. " has " .. channel.userCount .. " users")
            end
        end
    end
end

-- Auto-op authorized users when they join
on.join = function(e)
    if not config.autoOpEnabled then return end
    if e.channel:lower() ~= config.opChannel:lower() then return end
    
    if isAuthorized(e.nick) then
        -- Wait a moment before opping
        timer.timeout(function()
            local channel = irc.server(e.server):channel(e.channel)
            if channel and channel:hasUser(e.nick) then
                channel:mode("+o " .. e.nick)
                print("Auto-opped " .. e.nick .. " in " .. e.channel)
            end
        end, 1000)
    end
end

print("Auto-Op Bot loaded!")
