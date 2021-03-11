using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using NadekoBot.Common.Collections;
using NadekoBot.Common.ModuleBehaviors;
using NadekoBot.Common.Replacements;
using NadekoBot.Core.Services;
using NadekoBot.Extensions;
using NadekoBot.Modules;
using NadekoBot.Modules.Administration.Common;
using Newtonsoft.Json;
using NLog;

namespace NadekoBot.Core.Modules.Administration
{
    public partial class Administration
    {
        [Group]
        public class UserSurvey : NadekoSubmodule<UserSurveyService>
        {
            
        }
    }

    public class UserSurveyService : INService, IEarlyBehavior
    {
        private readonly DiscordSocketClient _client;
        private readonly Logger _log;
        private const string configDataPath = "./data/user-survey.json";
        private const string messagesForApprovalFilePath = "./data/approval_messages.json";
        private UserSurveyConfig ConfigData { get; set; }

        // userid, questions
        private readonly ConcurrentDictionary<ulong, Questionnaire> _questionnaires =
            new ConcurrentDictionary<ulong, Questionnaire>();
        
        // messageid, userid
        private ConcurrentDictionary<ulong, (ulong UserId, AnsweredQuestion[])> _messagesForUserApproval =
            new ConcurrentDictionary<ulong, (ulong UserId, AnsweredQuestion[])>();

        private ConcurrentDictionary<ulong, TaskCompletionSource<string>> _reasonCompletionSources =
            new ConcurrentDictionary<ulong, TaskCompletionSource<string>>();

        public UserSurveyService(DiscordSocketClient client)
        {
            _log = LogManager.GetCurrentClassLogger();
            _client = client;

            if (!File.Exists(messagesForApprovalFilePath))
            {
                SaveMessagesForApproval();
            }
            
            _messagesForUserApproval =
                JsonConvert.DeserializeObject<Dictionary<ulong, (ulong UserId, AnsweredQuestion[])>>(File.ReadAllText(messagesForApprovalFilePath))
                    .ToConcurrent();
            ReloadConfig();
            
            _client.ReactionAdded += ClientOnReactionAdded;
            _client.MessageReceived += ClientOnMessageReceived;
        }

        private Task ClientOnMessageReceived(SocketMessage msg)
        {
            if (_reasonCompletionSources.TryRemove(msg.Channel.Id, out var source))
            {
                _ = Task.Run(() => source.SetResult(msg.Content));
                (msg as IUserMessage)?.DeleteAfter(3);
            }

            return Task.CompletedTask;
        }

        private void SaveMessagesForApproval()
        {
            File.WriteAllText(messagesForApprovalFilePath,
                JsonConvert.SerializeObject(_messagesForUserApproval));
        }

        private Task ClientOnReactionAdded(Cacheable<IUserMessage, ulong> msg, ISocketMessageChannel channel,
            SocketReaction reaction)
        {
            _ = Task.Run(async () =>
            {
                if (!(channel is SocketTextChannel gchan))
                    return;

                ReloadConfig();

                var reactionUser = gchan.Guild.GetUser(reaction.UserId);
                if (reactionUser?.IsBot == true)
                    return;
                if (reactionUser == null)
                {
                    _log.Error("Can't find user {0} who reacted to the message {1}.", reaction.UserId, msg.Id);
                    return;
                }

                if (_messagesForUserApproval.TryGetValue(msg.Id, out var data))
                {
                    // in this case reactionUser is moderator
                    await HandleApprovalAction(msg.Id, gchan, data, reaction, reactionUser);
                    return;
                }

                if (msg.Id != ConfigData.InitialMessageId)
                    return;

                _questionnaires.TryRemove(data.UserId, out _);
                var survey = new Questionnaire(this, ConfigData, reactionUser, _client, gchan);
                _questionnaires[reactionUser.Id] = survey;
                await survey.StartAsync();
            });

            return Task.CompletedTask;
        }

        private async Task HandleApprovalAction(ulong messageId, SocketTextChannel channel,
            (ulong UserId, AnsweredQuestion[] Answers) data, SocketReaction reaction, SocketGuildUser reactionUser)
        {
            var matchingAction = ConfigData.Actions.FirstOrDefault(x => x.GetEmote().Name == reaction.Emote.Name);
            if (matchingAction == null)
                return;
            
            _messagesForUserApproval.TryRemove(messageId, out _);
            SaveMessagesForApproval();
            var guild = channel.Guild;

            string reason = null;
            if (matchingAction.ReasonRequired)
            {
                var toDelete = await channel.SendConfirmAsync("Please specify a reason for this action:").ConfigureAwait(false);
                var tsc = new TaskCompletionSource<string>();
                _reasonCompletionSources[channel.Id] = tsc;
                var task = await Task.WhenAny(Task.Delay(30000), tsc.Task);
                if (task == tsc.Task)
                    reason = tsc.Task.GetAwaiter().GetResult();

                toDelete.DeleteAfter(1);
            }

            var user = guild.GetUser(data.UserId); // the target of the approval action
            try
            {
                if (matchingAction.RemoveAction == RemoveAction.Ban)
                {
                    await user.BanAsync(reason: $"Questionnaire ban by {reactionUser}");
                    return;
                }

                if (matchingAction.RemoveAction == RemoveAction.Kick)
                {
                    await user.KickAsync($"Questionnaire kick by {reactionUser}");
                    return;
                }

                foreach (var roleId in matchingAction.RolesToAdd)
                {
                    await user.AddRolesAsync(guild.Roles.Where(x => x.Id == roleId));
                }

                foreach (var roleId in matchingAction.RolesToRemove)
                {
                    await user.RemoveRolesAsync(guild.Roles.Where(x => x.Id == roleId));
                }
            }
            catch (Exception ex)
            {
                _log.Error("Unable to apply action: {0}", ex.Message);
                _log.Error(ex.ToString());
            }
            finally
            {
                var msg = await channel.GetMessageAsync(messageId) as IUserMessage;
                msg?.DeleteAfter(3);
                
                try
                {
                    if (msg != null)
                    {
                        var embed = msg.Embeds.First().ToEmbedBuilder()
                            .AddField($"{matchingAction.Name} by",
                                reactionUser.Mention, false);
                        
                        if (!string.IsNullOrWhiteSpace(reason))
                        {
                            var userNotifyEmbed = new EmbedBuilder()
                                .WithOkColor()
                                .WithDescription(
                                    $"Action {matchingAction.Name} has been selected based on your answers.")
                                .AddField("Reason", reason);
                            _ = (await user.GetOrCreateDMChannelAsync()).EmbedAsync(userNotifyEmbed);
                            embed.AddField($"Reason for {matchingAction.Name} ({matchingAction.RemoveAction})",
                                reason);
                        }

                        var approvalLogChannel = guild.GetTextChannel(ConfigData.ActionedOutputChannel);
                        if (approvalLogChannel != null)
                        {
                            var rep = new ReplacementBuilder()
                                .WithUser(user)
                                .WithServer(_client, user.Guild)
                                .Build();

                            await approvalLogChannel.EmbedAsync(embed
                                .WithColor(ConfigData.ActionedEmbedColor));
                        }
                        else
                        {
                            _log.Warn("Can't find actioned output channel.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn(ex, "Failed sending message to actioned approval channel.");
                }
                
                SocketTextChannel ch;
                if (!string.IsNullOrWhiteSpace(matchingAction.Message)
                    && matchingAction.ChannelId is ulong channelId
                    && (ch = guild.GetTextChannel(channelId)) != null)
                {
                    try
                    {
                        var rep = new ReplacementBuilder()
                            .WithUser(user)
                            .WithServer(_client, user.Guild)
                            .Build();
                        await ch.SendMessageAsync(rep.Replace(matchingAction.Message));
                    }
                    catch (Exception ex)
                    {
                        _log.Warn(ex, "Unable to send message after performing {0} action on {1}", matchingAction.Name, user?.ToString() ?? data.UserId.ToString());
                    }
                }
            }
        }

        public void ReloadConfig()
        {
            ConfigData = JsonConvert.DeserializeObject<UserSurveyConfig>(File.ReadAllText(configDataPath));
        }

        public async Task SendForApproval(SocketGuildUser user, AnsweredQuestion[] answers)
        {
            _questionnaires.TryRemove(user.Id, out _);
            var cid = ConfigData.UnactionedOutputChannel;
            var channel = user.Guild.GetTextChannel(cid);
            if (channel == null)
            {
                _log.Error("Can't get unactioned output channel. Verification for user {0} failed", user.ToString());
                return;
            }
            
            var rep = new ReplacementBuilder()
                .WithUser(user)
                .WithServer(_client, user.Guild)
                .Build();

            var embed = new EmbedBuilder()
                .AddField(ConfigData.UnActionedEmbedTitle, user.Mention)
                .WithColor(ConfigData.UnactionedEmbedColor);
            
            // author stuff
            // todo test with users with no avatar
            embed.WithAuthor(user.ToString(), user.GetAvatarUrl());

            foreach (var answer in answers)
            {
                embed.AddField(answer.Question.Text, answer.Answer, inline: true);
            }

            embed.AddField("Actions", 
                string.Join("\n", ConfigData.Actions.Select(x => $"{x.Emoji} - {x.Name}")),
                false);

            try
            {
                var message = await channel.EmbedAsync(embed);

                _messagesForUserApproval[message.Id] = (user.Id, answers);
                SaveMessagesForApproval();
                foreach (var action in ConfigData.Actions)
                {
                    await message.AddReactionAsync(action.GetEmote());
                    await Task.Delay(1000);
                }
            }
            catch (Exception ex)
            {
                _log.Error("Sending answers for approval for user {0} failed. Unable to send a message to the approval channel or add reactions to it. " +
                           "Please check whether the bot can post messages to that channel and whether emojis are valid.", user.ToString());
                _log.Error(ex.ToString());
            }
        }

        public async Task UnableToSendQuestion(SocketGuildUser user)
        {
            var ch = user.Guild.GetTextChannel(ConfigData.InitialChannelId);
            var msg = (await ch.EmbedAsync(new EmbedBuilder().WithOkColor()
                .WithDescription($"Unable to send question to {user.Mention}. Please open your DMs.")))
                 as IUserMessage;
            msg?.DeleteAfter(ConfigData.ErrorDeleteAfterSecs);
        }

        public int Priority { get; } = 100;
        public ModuleBehaviorType BehaviorType { get; } = ModuleBehaviorType.Blocker;
        public async Task<bool> RunBehavior(DiscordSocketClient client, IGuild guild, IUserMessage msg)
        {
            if (_questionnaires.TryGetValue(msg.Author.Id, out var q))
            {
                if (msg.Content == ConfigData.CancelCommand)
                {
                    _questionnaires.TryRemove(msg.Author.Id, out _);
                    return true;
                }
                await q.Input(msg.Channel, msg.Content);
                return true;
            }

            return false;
        }
    }

    public class AnsweredQuestion
    {
        public Question Question { get; }
        public string Answer { get; }

        public AnsweredQuestion(Question question, string answer)
        {
            Question = question;
            Answer = answer;
        }
    }
    public class Questionnaire
    {
        private readonly UserSurveyService _service;
        private readonly UserSurveyConfig _configData;
        private readonly SocketGuildUser _user;
        private readonly DiscordSocketClient _client;
        private IDMChannel _dmChannel;

        private int _questionIndex = 0;
        private readonly Logger _log;
        private readonly AnsweredQuestion[] _answers;

        public Questionnaire(UserSurveyService service, UserSurveyConfig configData, SocketGuildUser user,
            DiscordSocketClient client, SocketTextChannel startTextChannel)
        {
            _service = service;
            _configData = configData;
            _user = user;
            _client = client;
            _log = LogManager.GetCurrentClassLogger();
            _answers = new AnsweredQuestion[configData.Survey.Questions.Count];
        }

        public async Task Input(IChannel channel, string input)
        {
            if (channel.Id != _dmChannel?.Id)
            {
                return;
            }

            var more = Answer(input);
            if (!more)
            {
                await _dmChannel.TriggerTypingAsync();
                await _dmChannel.SendConfirmAsync(_configData.ConfirmationMessage);
                await SendForApproval();
            }
            else
            {
                try
                {
                    await SendQuestion();
                }
                catch (Exception ex)
                {
                    await _service.UnableToSendQuestion(_user);
                    _log.Warn(ex.ToString());
                }
            }
        }

        private Task SendForApproval()
        {
            return _service.SendForApproval(_user, _answers);
        }

        private Task SendQuestion()
        {
            var question = _configData.Survey.Questions[_questionIndex];
            var embed = new EmbedBuilder()
                .WithColor(_configData.Survey.EmbedColor);
                        
            var rep = new ReplacementBuilder()
                .WithUser(_user)
                .WithServer(_client, _user.Guild)
                .Build();

            if (!string.IsNullOrWhiteSpace(question.Title))
                embed.WithTitle(rep.Replace(question.Title));

            embed.WithDescription(rep.Replace(question.Text));

            return _dmChannel.EmbedAsync(embed);
        }

        
        private bool Answer(string text)
        {
            var question = _configData.Survey.Questions[_questionIndex];

            _answers[_questionIndex] = new AnsweredQuestion(question, text);
            
            _questionIndex++;
            if (_questionIndex >= _configData.Survey.Questions.Count)
                return false;
            
            return true;
        }

        public async Task StartAsync()
        {
            try
            {
                _dmChannel = await _user.GetOrCreateDMChannelAsync();
                await SendQuestion();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed creating user dm channel or sending a question.");
            }
        }
    }
}