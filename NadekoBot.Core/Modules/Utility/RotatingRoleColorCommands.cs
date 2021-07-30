using System.Linq;
using Discord;
using NadekoBot.Extensions;
using NadekoBot.Common.Attributes;
using System.Threading.Tasks;
using Discord.Commands;
using NadekoBot.Core.Modules.Administration.Services;

namespace NadekoBot.Modules.Utility
{
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
            public async Task RotateRoleColor(IRole role, int seconds, params SixLabors.ImageSharp.Color[] colors)
            {
                _service.EnableRotatingRole(role, seconds, colors);

                var eb = new EmbedBuilder()
                    .WithOkColor()
                    .WithTitle("Rotating Role Color Enabled")
                    .AddField("Role", role.ToString(), true)
                    .AddField("Interval", seconds, true)
                    .AddField("Colors", string.Join("\n", colors.Select(x => x.ToHex())), true);

                await ctx.Channel.EmbedAsync(eb);
            }
        }
    }
}