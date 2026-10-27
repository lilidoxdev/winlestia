-- Clink 1.9+ / Live palette from the shared Winlestia wallpaper detector.
local p=clink.promptfilter(40)
local e="\27["
local colours={accent="196;186;250",secondary="140;215;241",muted="117;131;156"}
local previous=""
local function refresh()
    local f=io.open("__WINLESTIA_TERMINAL_FWD__/palette.colors","r")
    if not f then return end
    local data=f:read("*a");f:close()
    if data==previous then return end
    previous=data
    for key,value in data:gmatch("(%w+)=(%d+;%d+;%d+)") do colours[key]=value end
    for _,key in ipairs({"color.cmd","color.executable","color.argmatcher","color.cmdsep","color.cmdredir"}) do settings.set(key,"sgr 34") end
end
clink.onbeginedit(refresh)
function p:filter()
    refresh()
    local cwd=os.getcwd();local home=os.getenv("USERPROFILE") or ""
    if home~="" and cwd:sub(1,#home):lower()==home:lower() then cwd="~"..cwd:sub(#home+1) end
    local symbol=os.geterrorlevel()==0 and "36" or "31"
    local accent=os.getenv("WT_SESSION") and e.."34m" or e.."38;2;"..colours.accent.."m"
    return e.."90m╭─ "..accent..cwd..e.."90m  ·  CMD  ·  "..os.date("%H:%M")..e.."0m\n"..e.."90m╰─ "..accent.."❯ "..e.."0m",false
end
