using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Discord;
using Discord.WebSocket;
using NadekoBot.Core.Services;
using Newtonsoft.Json;
using NLog;
using System.Linq;
using NadekoBot.Modules.Administration.Common;

namespace NadekoBot.Core.Modules.Administration.Services
{
    public class RotatingRoleColorService : INService
    {
        private readonly DiscordSocketClient _client;
        private readonly Logger _log;
        private readonly ConcurrentDictionary<ulong, Timer> _settings;

        public RotatingRoleColorService(DiscordSocketClient client)
        {
            _client = client;

            if (!File.Exists("data/rrc.json"))
                File.WriteAllText("data/rrc.json", "[]");

            var list = LoadFromFile();

            _settings = new ConcurrentDictionary<ulong, Timer>();
            foreach (var item in list)
            {
                _settings[item.RoleId] = CreateTimer(item.GuildId,
                    item.RoleId,
                    item.Seconds,
                    Array.ConvertAll(item.Colors, x => SixLabors.ImageSharp.Color.ParseHex(x)));
            }

            _log = LogManager.GetCurrentClassLogger();
        }

        private readonly object locker = new object();

        public void DisableRotatingRole(IRole role, bool save = true)
        {
            lock (locker)
            {
                if (_settings.TryRemove(role.Id, out var timer))
                {
                    timer.Change(Timeout.Infinite, Timeout.Infinite);
                }

                if (save)
                {
                    var list = LoadFromFile();
                    list = list.Where(x => x.RoleId != role.Id)
                        .ToList();
                    SaveToFile(list);
                }
            }
        }

        public void EnableRotatingRole(IRole role, int seconds, SixLabors.ImageSharp.Color[] colors)
        {
            lock (locker)
            {
                DisableRotatingRole(role, false);
                _settings[role.Id] = CreateTimer(role.Guild.Id, role.Id, seconds, colors);
                
                var list = LoadFromFile();
                list.Add(new RrcModel(role.Guild.Id,
                    role.Id,
                    Array.ConvertAll(colors, r => r.ToHex()),
                    seconds));
                SaveToFile(list);
            }
        }

        private void SaveToFile(List<RrcModel> data)
        {
            File.WriteAllText("data/rrc.json", JsonConvert.SerializeObject(data));
        }

        private List<RrcModel> LoadFromFile()
        {
            var settingsString = File.ReadAllText("data/rrc.json");

            return JsonConvert.DeserializeObject<List<RrcModel>>(settingsString) ?? new List<RrcModel>();
        }

        private Timer CreateTimer(ulong guildId, ulong roleId, int seconds, SixLabors.ImageSharp.Color[] colors)
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

}