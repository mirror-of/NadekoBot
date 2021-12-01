using NadekoBot.Services;
using NadekoBot.Modules.Gambling.Common.Events;
using System.Collections.Concurrent;
using NadekoBot.Modules.Gambling.Common;
using Discord;
using Discord.WebSocket;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using NadekoBot.Services.Database.Models;
using System.Text;
using System.Threading;
using NadekoBot.Extensions;
using Serilog;

namespace NadekoBot.Modules.Gambling.Services
{
    public class GamblingEvent : ICurrencyEvent
    {
        private readonly TimeSpan _updateDelay = TimeSpan.FromSeconds(5);
        
        private readonly ITextChannel _ch;
        private readonly IEmbedBuilderService _eb;
        private Timer _t = null;
        private Timer _stopTimer = null;
        private IUserMessage _msg = null;
        public event Func<ulong, Task> OnEnded = delegate { return Task.CompletedTask; };
        
        private readonly ConcurrentDictionary<ulong, (string userName, long amount)> _participants = new();
        private readonly GamblingConfig _gc;
        private readonly EventOptions _opts;
        private readonly ICurrencyService _cur;
        private volatile bool updated = true;
        private readonly IReadOnlyList<long> _rewards;
        private bool ended = false;

        public GamblingEvent(ITextChannel ch, IEmbedBuilderService eb,
            GamblingConfig gc, EventOptions opts, ICurrencyService cur)
        {
            _ch = ch;
            _eb = eb;
            _gc = gc;
            _opts = opts;
            _cur = cur;
            _rewards = opts.Rewards.ToList();
        }
        
        public async Task StopEvent()
        {
            _t?.Change(Timeout.Infinite, Timeout.Infinite);
            _t = null;
            _stopTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _stopTimer = null;

            _cur.OnUserGamble -= OnUserGamble;

            var part = GetSortedParticipants();
            for (int i = 0; i < _rewards.Count; i++)
            {
                var r = _rewards[i];
                if (i < part.Count)
                {
                    var p = part[i].Key;
                    await _cur.AddAsync(p, "event-gambling", r, gamble: false);
                }
            }
            
            await OnEnded(_ch.GuildId);

            ended = true;
            await UpdateMessage();
        }

        private Task OnUserGamble(ulong userId, string userName, long amount)
        {
            _participants.AddOrUpdate(userId, (userName, amount), (_, old) => (userName, old.amount + amount));
            updated = true;
            return Task.CompletedTask;
        }

        public async Task StartEvent()
        {
            _t = new Timer(OnTimerTick, null, _updateDelay, _updateDelay);
            _stopTimer = new Timer(
                Callback,
                null,
                TimeSpan.FromHours(_opts.Hours),
                Timeout.InfiniteTimeSpan);
            
            await UpdateMessage();

            _cur.OnUserGamble += OnUserGamble;
        }

        private async void Callback(object _)
        {
            await StopEvent();
        }

        private async void OnTimerTick(object _)
        {
            try
            {
                await UpdateMessage();
            }
            catch
            {
                // ignored
            }
        }

        private List<KeyValuePair<ulong, (string userName, long amount)>> GetSortedParticipants()
        {
            var part = _participants.ToList();
            return part.OrderByDescending(x => x.Value.amount).ToList();
        }
        
        private string GetDescriptionString()
        {
            var part = GetSortedParticipants();
            var top10 = part.Take(10).ToList();
            var sb = new StringBuilder();
            for (var i = 0; i < top10.Count; ++i)
            {
                var p = top10[i];
                sb.Append($"`{i + 1}.` {p.Value.amount}{_gc.Currency.Sign} - {p.Value.userName}");

                if (i < _rewards.Count)
                    sb.Append($" ({_rewards[i]}{_gc.Currency.Sign})");
                
                sb.AppendLine();
            }

            return sb.ToString();
        }
        
        
        private async Task UpdateMessage()
        {
            var oldUpdated = updated;
            updated = false;
            
            var eb = _eb.Create()
                .WithOkColor()
                .WithDescription(GetDescriptionString());

            if (!ended)
                eb.WithTitle("Gambling event started!");
            else 
                eb.WithTitle("Gambling event ended!");

            if (_msg is null)
                _msg = await _ch.EmbedAsync(eb);
            else if (oldUpdated)
                await _msg.ModifyAsync(x => x.Embed = eb.Build());
            
        }
    }

    public class CurrencyEventsService : INService
    {
        private readonly DiscordSocketClient _client;
        private readonly ICurrencyService _cs;
        private readonly GamblingConfigService _configService;
        private readonly IEmbedBuilderService _eb;

        private readonly ConcurrentDictionary<ulong, ICurrencyEvent> _events =
            new ConcurrentDictionary<ulong, ICurrencyEvent>();


        public CurrencyEventsService(
            DiscordSocketClient client,
            ICurrencyService cs,
            GamblingConfigService configService,
            IEmbedBuilderService eb)
        {
            _client = client;
            _cs = cs;
            _configService = configService;
            _eb = eb;
        }

        public async Task<bool> TryCreateEventAsync(ulong guildId, ulong channelId, CurrencyEvent.Type type,
            EventOptions opts, Func<CurrencyEvent.Type, EventOptions, long, IEmbedBuilder> embed)
        {
            SocketGuild g = _client.GetGuild(guildId);
            SocketTextChannel ch = g?.GetChannel(channelId) as SocketTextChannel;
            if (ch is null)
                return false;

            ICurrencyEvent ce;

            if (type == CurrencyEvent.Type.Reaction)
            {
                ce = new ReactionEvent(_client, _cs, g, ch, opts, _configService.Data, embed);
            }
            else if (type == CurrencyEvent.Type.GameStatus)
            {
                ce = new GameStatusEvent(_client, _cs, g, ch, opts, embed);
            }
            else if (type == CurrencyEvent.Type.Gambling)
            {
                ce = new GamblingEvent(ch, _eb, _configService.Data, opts, _cs);
            }
            else
            {
                return false;
            }

            var added = _events.TryAdd(guildId, ce);
            if (added)
            {
                try
                {
                    ce.OnEnded += OnEventEnded;
                    await ce.StartEvent().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Error starting event");
                    _events.TryRemove(guildId, out ce);
                    return false;
                }
            }

            return added;
        }

        private Task OnEventEnded(ulong gid)
        {
            _events.TryRemove(gid, out _);
            return Task.CompletedTask;
        }
    }
}