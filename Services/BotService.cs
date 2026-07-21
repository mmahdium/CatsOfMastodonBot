using System.Net;
using CatsOfMastodonBot.DTOs;
using CatsOfMastodonBot.Repositories;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace CatsOfMastodonBot.Services;

public class BotService
{
    private readonly TelegramBotClient _botClient;
    private readonly AppConfig _config;
    private readonly ILogger<BotService> _logger;
    private readonly MediaAttachmentRepository _mediaAttachmentRepository;
    private readonly PostRepository _postRepository;

    public BotService(AppConfig config, PostRepository postRepository,
        MediaAttachmentRepository mediaAttachmentRepository, ILogger<BotService> logger)
    {
        if (config.Socks5Proxy is not null)
        {
            var webProxy = new WebProxy(config.Socks5Proxy);
            var botHttpClient = new HttpClient(new SocketsHttpHandler { Proxy = webProxy, UseProxy = true });
            _botClient = new TelegramBotClient(config.TelegramBotToken, botHttpClient);
        }
        else
        {
            _botClient = new TelegramBotClient(config.TelegramBotToken);
        }

        _config = config;
        _logger = logger;
        _postRepository = postRepository;
        _mediaAttachmentRepository = mediaAttachmentRepository;

        _botClient.DropPendingUpdates();
        _botClient.OnMessage += OnMessage;
        _botClient.OnUpdate += OnUpdate;
        _botClient.OnError += OnError;
    }

    private async Task OnMessage(Message message, UpdateType type)
    {
        if (message.Chat.Type is not ChatType.Private) return;
        if (message.Text is not null && message.Chat.Id.ToString() == _config.AdminNumericId &&
            message.Text.Contains("/getdangling"))
        {
            _logger.LogInformation("Received /getdangling command");
            var danglingPost = await _mediaAttachmentRepository.GetRandomDanglingAsync();

            if (danglingPost == null)
            {
                await _botClient.SendMessage(_config.AdminNumericId, "No dangling posts found.");
                _logger.LogInformation("No dangling posts found.");
                return;
            }

            await _botClient.SendPhoto(_config.AdminNumericId, danglingPost.MediaAttachment.PreviewUrl,
                $"<a href=\"" + danglingPost.Post.Url + "\"> Mastodon </a>", ParseMode.Html
                , replyMarkup: new InlineKeyboardMarkup()
                    .AddButton("Approve", $"approve-{danglingPost.MediaAttachment.Id}")
                    .AddButton("Reject", $"reject-{danglingPost.MediaAttachment.Id}"));

            _logger.LogInformation("Sent dangling post to admin.");
        }
        else if (message.Text is not null && message.Chat.Id.ToString() == _config.AdminNumericId &&
                 message.Text.Contains("/pause"))

        {
            PeriodicFetchService.PostCollectionPaused = true;
            await _botClient.SendMessage(_config.AdminNumericId, "Collection paused.");
            _logger.LogInformation("Pausing post collection.");
        }
        else if (message.Text is not null && message.Chat.Id.ToString() == _config.AdminNumericId &&
                 message.Text.Contains("/resume"))

        {
            PeriodicFetchService.PostCollectionPaused = false;
            await _botClient.SendMessage(_config.AdminNumericId, "Collection resumed.");
            _logger.LogInformation("Resuming post collection.");
        }

        else if (message.Text is not null && message.Chat.Id.ToString() == _config.AdminNumericId &&
                 message.Text.Contains("/cleanup"))
        {
            await _botClient.SendMessage(_config.AdminNumericId,
                "Are you sure you want to cleanup stale media attachments and orphan posts and accounts?",
                replyMarkup: new InlineKeyboardMarkup().AddButtons(
                    InlineKeyboardButton.WithCallbackData("Yes", "cleanup_confirm"),
                    InlineKeyboardButton.WithCallbackData("No", "cleanup_cancel")));
        }
        else
        {
            await _botClient.SendMessage(message.Chat.Id, "See you here!🐈\n@catsofmastodon");
            _logger.LogInformation(
                "Sent welcome message to ChatId: {ChatId}, FirstName: {FirstName}, LastName: {LastName}, Username: {Username}, MessageText: {MessageText}",
                message.Chat.Id, message.Chat.FirstName, message.Chat.LastName, message.Chat.Username, message.Text);
        }
    }

    private async Task OnUpdate(Update update)
    {
        switch (update)
        {
            case { CallbackQuery: { } callbackQuery }:
            {
                if (callbackQuery.Data == null || callbackQuery.Message == null ||
                    callbackQuery.Message.Chat.Id.ToString() != _config.AdminNumericId)
                {
                    _logger.LogError(
                        $"Received null callback query data or message {callbackQuery.Data} - or message is not from admin");
                }

                // Approve or reject a post
                else if (callbackQuery.Data.Contains("approve-") || callbackQuery.Data.Contains("reject-"))
                {
                    var parts = callbackQuery.Data.Split('-');
                    if (parts.Length != 2)
                    {
                        _logger.LogError("Invalid callback query data format.");
                        return;
                    }

                    var action = parts[0];
                    var mediaId = parts[1];

                    switch (action)
                    {
                        case "approve":
                            var approveMediaResult = await _mediaAttachmentRepository.ApproveAsync(mediaId);
                            if (approveMediaResult is not null)
                            {
                                await _botClient.SendPhoto(_config.ChannelNumericId,
                                    approveMediaResult.MediaAttachment.Url,
                                    $"Post from " + $"<a href=\"" + approveMediaResult.Post.Url + "\">" +
                                    approveMediaResult.Account.DisplayName + " </a>", ParseMode.Html);

                                await _botClient.AnswerCallbackQuery(callbackQuery.Id,
                                    "Media attachment approved and sent to channel.");
                                await _botClient.DeleteMessage(callbackQuery.Message.Chat.Id, callbackQuery.Message.Id);
                            }
                            else
                            {
                                await _botClient.AnswerCallbackQuery(callbackQuery.Id,
                                    "Media attachment was approved before.");
                                await _botClient.DeleteMessage(callbackQuery.Message.Chat.Id, callbackQuery.Message.Id);
                            }

                            break;

                        case "reject":
                            var rejectMediaResult = await _mediaAttachmentRepository.RejectAsync(mediaId);
                            if (rejectMediaResult > 0)
                            {
                                await _botClient.AnswerCallbackQuery(callbackQuery.Id,
                                    "Media attachment rejected successfully ");
                                await _botClient.DeleteMessage(callbackQuery.Message.Chat.Id, callbackQuery.Message.Id);
                            }
                            else
                            {
                                await _botClient.AnswerCallbackQuery(callbackQuery.Id,
                                    "Media attachment was rejected before.");
                                await _botClient.DeleteMessage(callbackQuery.Message.Chat.Id, callbackQuery.Message.Id);
                            }

                            break;
                        default:
                            _logger.LogError($"Invalid action: {action}");
                            return;
                    }
                }
                /*if (callbackQuery.Data == "new_random")
                {
                    await HandleStartMessage.HandleStartMessageAsync(callbackQuery.Message, bot, db, logger,
                        callbackQuery);
                    break;
                }*/

                else if (callbackQuery.Data.Contains("cleanup_"))
                {
                    if (callbackQuery.Data == "cleanup_confirm")
                    {
                        var deletedEntities = await _mediaAttachmentRepository.CleanupStaleWithOrphansAsync();
                        await _botClient.AnswerCallbackQuery(callbackQuery.Id,
                            "Deleted " + deletedEntities + " entities.");
                        await _botClient.DeleteMessage(callbackQuery.Message.Chat.Id, callbackQuery.Message.Id);
                    }

                    if (callbackQuery.Data == "cleanup_cancel")
                    {
                        await _botClient.AnswerCallbackQuery(callbackQuery.Id, "Cleanup cancelled.");
                        await _botClient.DeleteMessage(callbackQuery.Message.Chat.Id,
                            callbackQuery.Message.Id); // TODO: improve error handling
                    }
                }
                else
                {
                    _logger.LogError($"Received unhandled callback query {callbackQuery.Data}");
                }

                break;
            }
            default: _logger.LogInformation($"Received unhandled update {update.Type}"); break;
        }
    }

    private async Task OnError(Exception exception, HandleErrorSource source)
    {
        _logger.LogError(exception, "Error while polling updates");
    }


    public async Task SendPostToAdmin(MastodonPostDto post)
    {
        foreach (var media in post.MediaAttachments)
            try
            {
                await _botClient.SendPhoto(_config.AdminNumericId, media.PreviewUrl,
                    $"<a href=\"" + post.Url + "\"> Mastodon </a>", ParseMode.Html
                    , replyMarkup: new InlineKeyboardMarkup()
                        .AddButton("Approve", $"approve-{media.Id}")
                        .AddButton("Reject", $"reject-{media.Id}"));

                _logger.LogInformation("Sent message to admin: " + media.PreviewUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error while sending message to admin: " + ex.Message + " - Media URL: " +
                                 media.PreviewUrl);
            }
    }
}