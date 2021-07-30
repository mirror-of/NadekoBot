using Discord;
using SixLabors.ImageSharp.PixelFormats;

namespace NadekoBot.Modules.Administration.Common
{
    public class RrcState
    {
        public int Index { get; private set; }
        public SixLabors.ImageSharp.Color[] Colors { get; }
        public IRole Role { get; }
        public int Seconds { get; }

        public RrcState(IRole role, int seconds, SixLabors.ImageSharp.Color[] colors)
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
}