using System.Collections.Generic;
using Discord;
using Newtonsoft.Json;

namespace NadekoBot.Modules.Administration.Common
{
    public enum RemoveAction
    {
        None,
        Kick,
        Ban
    }
    public class Action
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        
        [JsonProperty("emoji")]
        public string Emoji { get; set; }

        [JsonProperty("removeAction")]
        public RemoveAction RemoveAction { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("channelId")]
        public ulong? ChannelId { get; set; }

        [JsonProperty("reasonRequired")]
        public bool ReasonRequired { get; set; }

        [JsonProperty("rolesToAdd")]
        public List<ulong> RolesToAdd { get; set; }
        [JsonProperty("rolesToRemove")]
        public List<ulong> RolesToRemove { get; set; }

        public IEmote GetEmote()
        {
            if (Emote.TryParse(Emoji, out var emote))
                return emote;
            
            return new Emoji(Emoji);
        }
    }

    public class Question
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("text")]
        public string Text { get; set; }
    }

    public class Survey
    {
        [JsonProperty("embedColor")]
        public uint EmbedColor { get; set; }

        [JsonProperty("questions")]
        public List<Question> Questions { get; set; }
    }

    public class UserSurveyConfig
    {
        [JsonProperty("cancelCommand")]
        public string CancelCommand { get; set; }
        
        [JsonProperty("initialMessageId")]
        public ulong InitialMessageId { get; set; }
     
        [JsonProperty("initialChannelId")]
        public ulong InitialChannelId { get; set; }
     
        [JsonProperty("initialEmoji")]
        public string InitialEmoji { get; set; }
        
        [JsonProperty("errorDeleteAfterSecs")]
        public int ErrorDeleteAfterSecs { get; set; }
         
        [JsonProperty("unactionedOutputChannel")]
        public ulong UnactionedOutputChannel { get; set; }

        [JsonProperty("unactionedEmbedColor")]
        public uint UnactionedEmbedColor { get; set; }

        [JsonProperty("actionedEmbedTitle")]
        public string ActionedEmbedTitle { get; set; }
        [JsonProperty("unActionedEmbedTitle")]
        public string UnActionedEmbedTitle { get; set; }

        [JsonProperty("actionedOutputChannel")]
        public ulong ActionedOutputChannel { get; set; }

        [JsonProperty("actionedEmbedColor")]
        public uint ActionedEmbedColor { get; set; }
        
        [JsonProperty("confirmationMessage")]
        public string ConfirmationMessage { get; set; }

        [JsonProperty("actions")]
        public List<Action> Actions { get; set; }

        [JsonProperty("survey")]
        public Survey Survey { get; set; }
    }
}