using CatsOfMastodonBot.DTOs;
using CatsOfMastodonBot.Models;
using CatsOfMastodonBot.Services;

namespace CatsOfMastodonBot.Repositories;

public class MediaAttachmentRepository(Database database)
{
    public async Task<ActionPostResultDto?> ApproveAsync(string mediaId)
    {
        await using var conn = database.CreateConnection();
        await conn.OpenAsync();

        await using var tx = conn.BeginTransaction();

        try
        {
            var updateCmd = conn.CreateCommand();
            updateCmd.Transaction = tx;
            updateCmd.CommandText =
                """
                UPDATE MediaAttachments
                SET Approved = 1,
                    Rejected = 0
                WHERE Id = $id;
                """;

            updateCmd.Parameters.AddWithValue("$id", mediaId);

            var rowsAffected = await updateCmd.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                await tx.RollbackAsync();
                return null;
            }

            var selectCmd = conn.CreateCommand();
            selectCmd.Transaction = tx;
            selectCmd.CommandText =
                """
                SELECT 
                    p.Id AS PostId,
                    p.Url AS PostUrl,
                    p.AccountId,
                    a.Id AS AccountId,
                    a.Username,
                    a.Acct,
                    a.DisplayName,
                    a.IsBot,
                    a.Url AS AccountUrl,
                    a.AvatarStatic,
                    m.Id AS MediaId,
                    m.Type,
                    m.Url AS MediaUrl,
                    m.PreviewUrl,
                    m.RemoteUrl,
                    m.Approved,
                    m.Rejected,
                    m.PostId AS MediaPostId
                FROM MediaAttachments m
                JOIN Posts p ON m.PostId = p.Id
                JOIN Accounts a ON p.AccountId = a.Id
                WHERE m.Id = $id;
                """;

            selectCmd.Parameters.AddWithValue("$id", mediaId);

            await using var reader = await selectCmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                await tx.RollbackAsync();
                return null;
            }

            var account = new Account
            {
                Id = reader.GetString(reader.GetOrdinal("AccountId")),
                Username = reader.GetString(reader.GetOrdinal("Username")),
                Acct = reader.GetString(reader.GetOrdinal("Acct")),
                DisplayName = reader.GetString(reader.GetOrdinal("DisplayName")),
                IsBot = reader.GetBoolean(reader.GetOrdinal("IsBot")),
                Url = reader.GetString(reader.GetOrdinal("AccountUrl")),
                AvatarStatic = reader.GetString(reader.GetOrdinal("AvatarStatic"))
            };

            var post = new Post
            {
                Id = reader.GetString(reader.GetOrdinal("PostId")),
                Url = reader.GetString(reader.GetOrdinal("PostUrl")),
                Account = account,
                MediaAttachments = new List<MediaAttachment>()
            };

            var media = new MediaAttachment
            {
                Id = reader.GetString(reader.GetOrdinal("MediaId")),
                Type = reader.GetString(reader.GetOrdinal("Type")),
                Url = reader.GetString(reader.GetOrdinal("MediaUrl")),
                PreviewUrl = reader.GetString(reader.GetOrdinal("PreviewUrl")),
                RemoteUrl = reader.GetString(reader.GetOrdinal("RemoteUrl")),
                Approved = reader.GetBoolean(reader.GetOrdinal("Approved")),
                Rejected = reader.GetBoolean(reader.GetOrdinal("Rejected")),
                PostId = reader.IsDBNull(reader.GetOrdinal("MediaPostId"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("MediaPostId")),
                Post = post
            };

            post.MediaAttachments.Add(media);

            await tx.CommitAsync();

            return new ActionPostResultDto(post, account, media);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    public async Task<int> RejectAsync(string mediaId)
    {
        await using var conn = database.CreateConnection();
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();

        cmd.CommandText =
            """
            UPDATE MediaAttachments
            SET Approved = 0,
                Rejected = 1
            WHERE Id = $id;
            """;

        cmd.Parameters.AddWithValue("$id", mediaId);

        return await cmd.ExecuteNonQueryAsync();
    }

    public async Task<ApprovedPostResultDto?> GetRandomApprovedAsync()
    {
        await using var conn = database.CreateConnection();
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();

        cmd.CommandText =
            """
            SELECT

                m.Id,
                m.Type,
                m.Url,
                m.PreviewUrl,
                m.RemoteUrl,

                p.Id,
                p.Url,

                a.Id,
                a.Username,
                a.Acct,
                a.DisplayName,
                a.IsBot,
                a.Url,
                a.AvatarStatic

            FROM MediaAttachments m

            INNER JOIN Posts p
                ON p.Id = m.PostId

            INNER JOIN Accounts a
                ON a.Id = p.AccountId

            WHERE
                m.Approved = 1
                AND m.Rejected = 0

            ORDER BY RANDOM()
            LIMIT 1;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        var media = new MediaAttachment
        {
            Id = reader.GetString(0),
            Type = reader.GetString(1),
            Url = reader.GetString(2),
            PreviewUrl = reader.GetString(3),
            RemoteUrl = reader.IsDBNull(4) ? "" : reader.GetString(4)
        };

        var account = new Account
        {
            Id = reader.GetString(7),
            Username = reader.GetString(8),
            Acct = reader.GetString(9),
            DisplayName = reader.GetString(10),
            IsBot = reader.GetBoolean(11),
            Url = reader.GetString(12),
            AvatarStatic = reader.GetString(13)
        };

        var post = new Post
        {
            Id = reader.GetString(5),
            Url = reader.GetString(6),
            Account = account,
            MediaAttachments = [media]
        };

        return new ApprovedPostResultDto(
            post,
            account,
            media
        );
    }

    public async Task<ActionPostResultDto?> GetRandomDanglingAsync()
    {
        await using var conn = database.CreateConnection();
        await conn.OpenAsync();

        var cmd = conn.CreateCommand();

        cmd.CommandText =
            """
            SELECT
                m.Id,
                m.Type,
                m.Url,
                m.PreviewUrl,
                m.RemoteUrl,
                m.Approved,
                m.Rejected,
                p.Id,
                p.Url,
                p.AccountId,
                a.Id,
                a.Username,
                a.Acct,
                a.DisplayName,
                a.IsBot,
                a.Url,
                a.AvatarStatic
            FROM MediaAttachments m
            INNER JOIN Posts p ON p.Id = m.PostId
            INNER JOIN Accounts a ON a.Id = p.AccountId
            WHERE m.Approved = 0 AND m.Rejected = 0
            ORDER BY RANDOM()
            LIMIT 1;
            """;

        await using var reader = await cmd.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        var media = new MediaAttachment
        {
            Id = reader.GetString(0),
            Type = reader.GetString(1),
            Url = reader.GetString(2),
            PreviewUrl = reader.GetString(3),
            RemoteUrl = reader.IsDBNull(4) ? "" : reader.GetString(4),
            Approved = reader.GetBoolean(5),
            Rejected = reader.GetBoolean(6)
        };

        var account = new Account
        {
            Id = reader.GetString(10),
            Username = reader.GetString(11),
            Acct = reader.GetString(12),
            DisplayName = reader.GetString(13),
            IsBot = reader.GetBoolean(14),
            Url = reader.GetString(15),
            AvatarStatic = reader.GetString(16)
        };

        var post = new Post
        {
            Id = reader.GetString(7),
            Url = reader.GetString(8),
            Account = account,
            MediaAttachments = [media]
        };

        return new ActionPostResultDto(post, account, media);
    }

    public async Task<int> CleanupStaleWithOrphansAsync()
    {
        await using var conn = database.CreateConnection();
        await conn.OpenAsync();

        await using var tx = conn.BeginTransaction();

        try
        {
            // Delete stale media attachments
            var deleteMediaCmd = conn.CreateCommand();
            deleteMediaCmd.Transaction = tx;
            deleteMediaCmd.CommandText =
                """
                DELETE FROM MediaAttachments
                WHERE (Approved = 1 AND Rejected = 1)   -- Inconsistent state
                   OR (Approved = 0 AND Rejected = 0);  -- Never reviewed
                """;

            var mediaDeleted = await deleteMediaCmd.ExecuteNonQueryAsync();

            // Delete orphaned posts (posts with no media attachments)
            var deletePostsCmd = conn.CreateCommand();
            deletePostsCmd.Transaction = tx;
            deletePostsCmd.CommandText =
                """
                DELETE FROM Posts
                WHERE Id NOT IN (SELECT DISTINCT PostId FROM MediaAttachments WHERE PostId IS NOT NULL);
                """;

            var postsDeleted = await deletePostsCmd.ExecuteNonQueryAsync();

            // Delete orphaned accounts (accounts with no posts)
            var deleteAccountsCmd = conn.CreateCommand();
            deleteAccountsCmd.Transaction = tx;
            deleteAccountsCmd.CommandText =
                """
                DELETE FROM Accounts
                WHERE Id NOT IN (SELECT DISTINCT AccountId FROM Posts);
                """;

            var accountsDeleted = await deleteAccountsCmd.ExecuteNonQueryAsync();

            await tx.CommitAsync();
            return mediaDeleted + postsDeleted + accountsDeleted;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }
}