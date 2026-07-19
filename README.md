# CatsOfMastodonBot

A Mastodon-to-Telegram bot that curates cat photos from the Fediverse and publishes them to a Telegram channel through admin moderation.

## How It Works

```
Mastodon (#catsofmastodon) --> Bot (every 15 min) --> SQLite --> Admin (Telegram) --> Channel
                                  |                                    |
                            filters:                             Approve / Reject
                            - image only                              via inline
                            - non-bot accounts                        buttons
```

1. A background service polls the Mastodon public API for posts tagged `#catsofmastodon` every 15 minutes.
2. Posts with image attachments from non-bot accounts are stored in a local SQLite database.
3. Each new image is sent to the configured Telegram admin with Approve/Reject inline buttons.
4. When the admin approves, the image is forwarded to the public Telegram channel with a link to the original post.
5. The admin can also use `/getdangling` to manually review unmoderated posts.

## Configuration

All configuration is done via environment variables. A `.env` file is supported (loaded via DotNetEnv).

| Variable | Required | Default | Description |
|---|---|---|---|
| `TELEGRAM_BOT_TOKEN` | Yes | — | Telegram Bot API token from @BotFather |
| `ADMIN_NUMERIC_ID` | Yes | — | Telegram numeric user ID of the admin |
| `CHANNEL_NUMERIC_ID` | Yes | — | Telegram numeric chat ID of the target channel |
| `MASTODON_INSTANCE` | No | `https://haminoa.net` | Mastodon instance URL to fetch posts from |
| `DB_PATH` | No | `/data/com_bot.db` | Path to SQLite database file (or directory for default filename) |
| `SOCKS5_PROXY` | No | — | SOCKS5 proxy URL (e.g. `socks5://tor:9050`) for Telegram traffic |
| `POSTS_PER_REQUEST` | No | `10` | Number of posts to fetch per Mastodon API call (max 40) |

See `.env.example` for a template.

## Docker

```bash
# Build
docker build -t catsofmastodonbot .

# Run
docker run --env-file .env -v /path/to/data:/data catsofmastodonbot
```

### Docker Compose

```yaml
services:
  catsofmastodonbot:
    image: ghcr.io/mmahdium/catsofmastodonbot:latest
    restart: unless-stopped
    env_file: .env
    volumes:
      - ./data:/data
```

```bash
# Run with compose
docker compose up -d
```

## Running Locally

Prerequisites: .NET 10 SDK

```bash
# Set up your .env file
cp .env.example .env
# Edit .env with your values

# Run
dotnet run
```

## Telegram Bot

- Any private message to the bot receives a welcome reply.
- The admin can send `/getdangling` to review the next unmoderated post.
- Each post shows the image preview with Approve and Reject buttons.
- Approved images are posted to the configured channel with a link to the original Mastodon post.

## Project Structure

```
.
├── Program.cs                      # Entry point, DI registration
├── Services/
│   ├── AppConfig.cs                # Environment variable loading and validation
│   ├── Database.cs                 # SQLite connection factory
│   ├── MigrationService.cs         # File-based SQL migration runner
│   ├── MastodonService.cs          # Mastodon API client
│   ├── BotService.cs               # Telegram bot (polling, approval flow)
│   └── PeriodicFetchService.cs     # Background fetch loop (15 min interval)
├── Repositories/
│   ├── PostRepository.cs           # Post and account insert/update logic
│   └── MediaAttachmentRepository.cs # Approve, reject, and random queries
├── Models/
│   ├── Post.cs
│   ├── Account.cs
│   └── MediaAttachment.cs
├── DTOs/
│   ├── MastodonDTOs.cs             # Mastodon API response models
│   └── DatabaseDTOs.cs             # Query result records
├── Migrations/
│   └── 001_initial.sql             # Database schema
├── Dockerfile                      # Alpine-based AOT build
└── .github/workflows/
    └── docker-publish.yml          # CI: build and push to GHCR
```

## Database

SQLite with four tables:

- **Accounts** — Mastodon user profiles (deduplicated by `Acct`)
- **Posts** — Mastodon posts linked to accounts
- **MediaAttachments** — Image attachments linked to posts, with `Approved`/`Rejected` flags
- **SchemaMigrations** — Tracks applied SQL migration files

Migrations are plain `.sql` files in `Migrations/`, applied automatically on startup in filename order.

## License

See [LICENSE](LICENSE) if present.
