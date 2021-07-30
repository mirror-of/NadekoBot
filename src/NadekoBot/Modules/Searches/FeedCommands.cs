﻿using Discord;
using Discord.Commands;
using NadekoBot.Common.Attributes;
using NadekoBot.Extensions;
using NadekoBot.Modules.Searches.Services;
using System;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace NadekoBot.Modules.Searches
{
    public partial class Searches
    {
        [Group]
        public class FeedCommands : NadekoSubmodule<FeedsService>
        {
            [NadekoCommand, Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageMessages)]
            public async Task Feed(string url, [Leftover] ITextChannel channel = null)
            {
                var success = Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
                if (success)
                {
                    channel = channel ?? (ITextChannel)ctx.Channel;
                    try
                    {
                        var feeds = await CodeHollow.FeedReader.FeedReader.ReadAsync(url).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Log.Information(ex, "Unable to get feeds from that url");
                        success = false;
                    }
                }

                if (success)
                {
                    success = _service.AddFeed(ctx.Guild.Id, channel.Id, url);
                    if (success)
                    {
                        await ReplyConfirmLocalizedAsync(strs.feed_added).ConfigureAwait(false);
                        return;
                    }
                }

                await ReplyErrorLocalizedAsync(strs.feed_not_valid).ConfigureAwait(false);
            }

            [NadekoCommand, Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageMessages)]
            public async Task FeedRemove(int index)
            {
                if (_service.RemoveFeed(ctx.Guild.Id, --index))
                {
                    await ReplyConfirmLocalizedAsync(strs.feed_removed).ConfigureAwait(false);
                }
                else
                    await ReplyErrorLocalizedAsync(strs.feed_out_of_range).ConfigureAwait(false);
            }

            [NadekoCommand, Aliases]
            [RequireContext(ContextType.Guild)]
            [UserPerm(GuildPerm.ManageMessages)]
            public async Task FeedList()
            {
                var feeds = _service.GetFeeds(ctx.Guild.Id);

                if (!feeds.Any())
                {
                    await ctx.Channel.EmbedAsync(_eb.Create()
                        .WithOkColor()
                        .WithDescription(GetText(strs.feed_no_feed)))
                        .ConfigureAwait(false);
                    return;
                }

                await ctx.SendPaginatedConfirmAsync(0, (cur) =>
                {
                    var embed = _eb.Create()
                       .WithOkColor();
                    var i = 0;
                    var fs = string.Join("\n", feeds.Skip(cur * 10)
                        .Take(10)
                        .Select(x => $"`{(cur * 10) + (++i)}.` <#{x.ChannelId}> {x.Url}"));

                    return embed.WithDescription(fs);

                }, feeds.Count, 10).ConfigureAwait(false);
            }
        }
    }
}
