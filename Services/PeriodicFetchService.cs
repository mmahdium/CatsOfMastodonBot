using CatsOfMastodonBot.DTOs;
using CatsOfMastodonBot.Models;
using CatsOfMastodonBot.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CatsOfMastodonBot.Services;

public class PeriodicFetchService(
    IServiceScopeFactory scopeFactory,
    ILogger<PeriodicFetchService> logger,
    AppConfig config)
    : BackgroundService
{
    public static bool PostCollectionPaused { get; set; } = false;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Starting the background job.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));

        using var scope = scopeFactory.CreateScope();

        var mastodonService = scope.ServiceProvider.GetRequiredService<MastodonService>();
        var postRepository = scope.ServiceProvider.GetRequiredService<PostRepository>();
        var botService = scope.ServiceProvider.GetRequiredService<BotService>();

        var firstRun = true;
        while (firstRun || await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (PostCollectionPaused)
            {
                logger.LogInformation("Post collection is paused, skipping fetch.");
                continue;
            }

            logger.LogInformation("Fetching posts from mastodon");
            var posts = await mastodonService.FetchCatPostsAsync(config.PostsPerRequest);
            logger.LogInformation("Fetched {count} posts", posts.Count);
            var newlyInserted = 0;

            foreach (var mastodonPostDto in posts)
            {
                if (mastodonPostDto.MediaAttachments.Count == 0 ||
                    mastodonPostDto.MediaAttachments.All(media => media.Type != "image") || mastodonPostDto.Account.Bot)
                    continue;

                mastodonPostDto.MediaAttachments =
                    mastodonPostDto.MediaAttachments.Where(media => media.Type == "image").ToList();
                var post = MapToPost(mastodonPostDto);
                try
                {
                    if (await postRepository.InsertIfNotExistsAsync(post) > 0)
                    {
                        await botService.SendPostToAdmin(mastodonPostDto);
                        newlyInserted++;
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "An error occured while inserting post");
                }
            }

            logger.LogInformation("Inserted {newlyInserted} new posts", newlyInserted);

            firstRun = false;
        }
    }

    private Post MapToPost(MastodonPostDto dto)
    {
        return new Post
        {
            Id = dto.Id,
            Url = dto.Url,
            Account = new Account
            {
                Id = dto.Account.Id,
                Username = dto.Account.Username,
                Acct = dto.Account.Acct,
                DisplayName = dto.Account.DisplayName,
                IsBot = dto.Account.Bot,
                Url = dto.Account.Url,
                AvatarStatic = dto.Account.AvatarStatic
            },
            MediaAttachments = dto.MediaAttachments.Select(media => new MediaAttachment
            {
                Id = media.Id,
                Type = media.Type,
                Url = media.Url,
                PreviewUrl = media.PreviewUrl,
                RemoteUrl = media.RemoteUrl ?? string.Empty,
                Approved = false,
                Rejected = false
            }).ToList()
        };
    }
}