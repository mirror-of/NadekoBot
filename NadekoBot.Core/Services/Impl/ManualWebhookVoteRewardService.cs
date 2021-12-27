using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using NadekoBot.Core.Services.Impl;
using NadekoBot.Extensions;
using NLog;
using NLog.Fluent;
using StackExchange.Redis;

namespace NadekoBot.Core.Services
{
    public interface IVoteRewardService
    {

    }

    public class VoteRewardsModel
    {
        public long AmountPerVote { get; set; } = 100;
        public int HoursCooldown { get; set; } = 12;
        public List<ulong> WebhookIds { get; set; } = new List<ulong>();
    }
    
    public class ManualWebhookVoteRewardService : IVoteRewardService
    {
        private readonly DiscordSocketClient _client;
        private readonly ICurrencyService _cs;
        private readonly ConnectionMultiplexer _multi;
        private readonly IBotCredentials _creds;
        private VoteRewardsModel data;
        private readonly Logger _log;
        private readonly FileSystemWatcher _fs;
        private readonly JsonSerializerOptions _serializer;

        private const string DATA_PATH = "data/vote_rewards.json";

        private readonly Regex _userMentionRegex = new Regex(@"\<\@\!?(?<id>\d+)\>", RegexOptions.Compiled);

        public ManualWebhookVoteRewardService(
            DiscordSocketClient client,
            ICurrencyService cs,
            IDataCache cache,
            IBotCredentials creds)
        {
            _log = LogManager.GetCurrentClassLogger();


            _log.Info("Lallalalala");
            
            _client = client;
            _cs = cs;
            _multi = cache.Redis;
            _creds = creds;

            _serializer = new JsonSerializerOptions()
            {
                WriteIndented = true
            };

            ReloadSettings();
            
            _client.MessageReceived += OnMessage;

            _fs = new FileSystemWatcher("data", "vote_rewards.json");
            _fs.NotifyFilter = NotifyFilters.LastWrite;
            _fs.EnableRaisingEvents = true;
            _fs.Changed += OnFileChanged;
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            _log.Info($"{DATA_PATH} was changed");
            Task.Run(async () =>
            {
                await Task.Delay(1000);
                ReloadSettings();
            });
        }

        private void ReloadSettings()
        {
            if (!File.Exists(DATA_PATH))
            {
                File.WriteAllText(DATA_PATH, JsonSerializer.Serialize(new VoteRewardsModel(), _serializer));
            }

            var txt = File.ReadAllText(DATA_PATH);

            data = JsonSerializer.Deserialize<VoteRewardsModel>(txt);
        }

        private async Task OnMessage(SocketMessage msg)
        {
            if (!msg.Author.IsWebhook || !data.WebhookIds.Contains(msg.Author.Id))
                return;

            var desc = msg.Embeds?.FirstOrDefault()?.Description;
            if (desc is null)
                return;

            var match = _userMentionRegex.Match(desc);
            if (!match.Groups["id"].Success || !ulong.TryParse(match.Groups["id"].Value, out var userId))
            {
                _log.Warn("No mentioned users in the webhook message");
                return;
            }

            if (!TryAddCooldown(userId))
            {
                _log.Warn("User {UserId} was already rewarded recently", userId);
                return;
            }

            await _cs.AddAsync(userId, "voting-reward", data.AmountPerVote, false);
        }

        private bool TryAddCooldown(ulong userId)
        {
            var db = _multi.GetDatabase();
            return db.StringSet($"{_creds.RedisKey()}_ff-vote-reward_{userId}",
                1,
                TimeSpan.FromHours(data.HoursCooldown),
                when: When.NotExists);
        }
    }
}