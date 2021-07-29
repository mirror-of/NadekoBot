using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Discord;
using NadekoBot.Extensions;
using SixLabors.ImageSharp.PixelFormats;
using NadekoBot.Common.Attributes;
using NadekoBot.Core.Services;
using System.Threading;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;
using NadekoBot.Modules;
using NadekoBot.Modules.Utility;
using Newtonsoft.Json;
using NLog;

namespace NadekoBot.Modules.Utility
{
    public class RrcState
    {
        public int Index { get; private set; }
        public Rgba32[] Colors { get; }
        public IRole Role { get; }
        public int Seconds { get; }

        public RrcState(IRole role, int seconds, Rgba32[] colors)
        {
            Index = 0;
            Colors = colors;
            Role = role;
            Seconds = seconds;
        }

        public Rgba32 GetColor()
        {
            return Colors[Index];
        }

        public void Next()
        {
            if (++Index >= Colors.Length)
                Index = 0;
        }
    }

    public class RrcModel
    {
        public ulong GuildId { get; set; }
        public ulong RoleId { get; set; }
        public string[] Colors { get; set; }
        public int Seconds { get; set; }

        public RrcModel(ulong guildId, ulong roleId, string[] colors, int seconds)
        {
            GuildId = guildId;
            RoleId = roleId;
            Colors = colors;
            Seconds = seconds;
        }
    }

    public class RotatingRoleColorService : INService
    {
        private readonly DiscordShardedClient _client;
        private readonly Logger _log;
        private readonly ConcurrentDictionary<ulong, Timer> _settings;

        public RotatingRoleColorService(DiscordShardedClient client)
        {
            _client = client;

            if (!File.Exists("data/rrc.json"))
                File.WriteAllText("data/rrc.json", "{}");

            var settingsString = File.ReadAllText("data/rrc.json");

            var list = JsonConvert.DeserializeObject<List<RrcModel>>(settingsString);

            _settings = new ConcurrentDictionary<ulong, Timer>();
            foreach (var item in list)
            {
                _settings[item.RoleId] = CreateTimer(item.GuildId,
                    item.RoleId,
                    item.Seconds,
                    Array.ConvertAll(item.Colors, x => Rgba32.ParseHex(x)));
            }

            _log = LogManager.GetCurrentClassLogger();
        }

        private readonly object locker = new object();

        public void DisableRotatingRole(IRole role)
        {
            if (_settings.TryRemove(role.Id, out var timer))
            {
                timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        public void EnableRotatingRole(IRole role, int seconds, Rgba32[] colors)
        {
            lock (locker)
            {
                DisableRotatingRole(role);
                _settings[role.Id] = CreateTimer(role.Guild.Id, role.Id, seconds, colors);
                Save(role, seconds, colors);
            }
        }

        private void Save(IRole role, int seconds, Rgba32[] colors)
        {
            var settingsString = File.ReadAllText("data/rrc.json");

            var list = JsonConvert.DeserializeObject<List<RrcModel>>(settingsString);

            list.Add(new RrcModel(role.Guild.Id,
                role.Id,
                Array.ConvertAll(colors, r => r.ToHex()),
                seconds));

            File.WriteAllText("data/rrc.json", JsonConvert.SerializeObject(list));
        }

        private Timer CreateTimer(ulong guildId, ulong roleId, int seconds, Rgba32[] colors)
        {
            var role = _client.GetGuild(guildId).GetRole(roleId);
            return new Timer(async stateObj =>
            {
                var state = (RrcState)stateObj;
                try
                {
                    var rgba32 = state.GetColor();
                    await role.ModifyAsync(r => r.Color = new Color(rgba32.R, rgba32.G, rgba32.B));
                    state.Next();
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error changing color of the role {Role} ({RoleId}) in the server {Server} ({ServerId}): {ErrorMessage}",
                        role.ToString(),
                        role.Id,
                        role.Guild.ToString(),
                        role.Guild.Id,
                        ex.Message);
                }
            }, new RrcState(role, seconds, colors), 0, 1000 * seconds);
        }
    }


    public partial class Utility
    {
        public class RotatingRoleColorCommands : NadekoSubmodule<RotatingRoleColorService>
        {
            [NadekoCommand, Usage, Description, Aliases]
            [BotPerm(GuildPerm.ManageRoles)]
            [UserPerm(GuildPerm.Administrator)]
            [RequireContext(ContextType.Guild)]
            public async Task RotateRoleColor([Leftover] IRole role)
            {
                _service.DisableRotatingRole(role);

                await ctx.Channel.SendConfirmAsync(
                    $"Rotating role color has been **disabled** for {role.ToString()} role");
            }

            [NadekoCommand, Usage, Description, Aliases]
            [BotPerm(GuildPerm.ManageRoles)]
            [UserPerm(GuildPerm.Administrator)]
            [RequireContext(ContextType.Guild)]
            public async Task RotateRoleColor(IRole role, int seconds, params Rgba32[] colors)
            {
                _service.EnableRotatingRole(role, seconds, colors);

                var eb = new EmbedBuilder()
                    .WithOkColor()
                    .WithDescription("Rotating Role Color Enabled")
                    .AddField("Role", role.ToString(), true)
                    .AddField("Interval", seconds, true)
                    .AddField("Colors", string.Join("\n", colors.Select(x => x.ToHex())), true);

                await ctx.Channel.EmbedAsync(eb);
            }
        }
    }
}