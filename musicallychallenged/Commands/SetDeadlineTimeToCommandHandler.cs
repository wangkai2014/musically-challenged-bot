﻿using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using log4net;
using musicallychallenged.Administration;
using musicallychallenged.Config;
using musicallychallenged.Data;
using musicallychallenged.Domain;
using musicallychallenged.Localization;
using musicallychallenged.Logging;
using musicallychallenged.Services;
using musicallychallenged.Services.Events;
using musicallychallenged.Services.Telegram;
using NodaTime;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using User = musicallychallenged.Domain.User;

namespace musicallychallenged.Commands
{
    [DemandCredentials(UserCredentials.Supervisor)]
    public class SetDeadlineTimeToCommandHandler : ITelegramCommandHandler
    {
        private readonly IRepository _repository;
        private readonly BotConfiguration _configuration;
        private readonly IClock _clock;
        private readonly TimeService _timeService;
        private readonly ContestController _contestController;
        private readonly LocStrings _loc;

        public string CommandName { get; } = "deadline";
        public string UserFriendlyDescription => "Set deadline to date & time";

        private static readonly ILog logger = Log.Get(typeof(SetDeadlineTimeToCommandHandler));

        public SetDeadlineTimeToCommandHandler(IRepository repository,
            BotConfiguration configuration,
            IClock clock,
            TimeService timeService,
            ContestController contestController,
            LocStrings loc)
        {
            _repository = repository;
            _configuration = configuration;
            _clock = clock;
            _timeService = timeService;
            _contestController = contestController;
            _loc = loc;
        }

        public async Task ProcessCommandAsync(Dialog dialog, User user)
        {
            var state = _repository.GetOrCreateCurrentState();

            logger.Info($"User {user.GetUsernameOrNameWithCircumflex()} about to submit contest entry description");

            if (state.State != ContestState.Contest && state.State != ContestState.Voting)
            {
                logger.Info($"Bot in state {state.State}, denied");
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,$"Only available in contest/voting state, now in {state.State}");
                return;
            }

            var message = await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Confirm your intentions -- override deadline?", 
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithCallbackData("YES","y"),
                    InlineKeyboardButton.WithCallbackData("NO","n")
                }));

            var response = await dialog.GetCallbackQueryAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);
            await dialog.TelegramClient.EditMessageReplyMarkupAsync(message.Chat.Id,message.MessageId,replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0])); //remove

            if (response?.Data != "y")
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Cancelled");
                return;
            }
            
            await dialog.TelegramClient.AnswerCallbackQueryAsync(response.Id);           
            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId, "Confirmed");

            await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                $"Send hours till new deadline as integer (like, 3.5 hours). " +
                $"Decimal separator is dot (.)");

            var date = await dialog.GetMessageInThreadAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(_configuration.SubmissionTimeoutMinutes)).Token);

            if (!double.TryParse(date.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
                out var deltaHours))
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Failed date parsing :(");
                return;
            }

            var overriddenDeadline = _clock.GetCurrentInstant().Plus(Duration.FromHours(deltaHours));

            message = await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,
                $"Confirm your intentions -- set deadline to {_timeService.FormatDateAndTimeToAnnouncementTimezone(overriddenDeadline)}?", 
                replyMarkup: new InlineKeyboardMarkup(new[]
                {
                    InlineKeyboardButton.WithCallbackData("YES","y"),
                    InlineKeyboardButton.WithCallbackData("NO","n")
                }));

            response = await dialog.GetCallbackQueryAsync(
                new CancellationTokenSource(TimeSpan.FromMinutes(1)).Token);

            await dialog.TelegramClient.EditMessageReplyMarkupAsync(message.Chat.Id,message.MessageId,replyMarkup: new InlineKeyboardMarkup(new InlineKeyboardButton[0])); //remove

            if (response?.Data != "y")
            {
                await dialog.TelegramClient.SendTextMessageAsync(dialog.ChatId,"Cancelled");
                return;
            }
            
            _repository.UpdateState(x=>x.NextDeadlineUTC,overriddenDeadline);           
            await _contestController.UpdateCurrentTaskMessage();

            logger.Info($"Deadline submitted");
        }

        
    }
}