# Lantern

Lantern watches the active DHCP leases on a MikroTik router and alerts you through Telegram when a new device appears on your network. Its web dashboard lets you review devices, give them friendly names, and mark them as trusted or ignored.

## Requirements

- A MikroTik router with its REST API available over HTTPS
- A MikroTik user that can read DHCP leases
- A Telegram bot token and destination chat ID
- Docker with Docker Compose, or the .NET 10 SDK for local development

## Run with Docker Compose

Replace the placeholder values in [`docker-compose.yml`](docker-compose.yml), then start Lantern:

```sh
docker compose up -d
```

Open <http://localhost:8080>. Lantern records its initial scan as a baseline; devices discovered on later scans are treated as new and trigger Telegram notifications.

The SQLite database is stored in the `lantern-data` volume. To update the container image:

```sh
docker compose pull
docker compose up -d
```

## Configuration

Lantern is configured entirely through environment variables.

| Variable | Required | Default | Description |
| --- | --- | --- | --- |
| `MIKROTIK_BASE_URL` | Yes | — | Absolute HTTPS URL of the router, for example `https://192.168.88.1`. |
| `MIKROTIK_USERNAME` | Yes | — | MikroTik username used for REST API requests. |
| `MIKROTIK_PASSWORD` | Yes | — | Password for the MikroTik user. |
| `MIKROTIK_ALLOW_INVALID_CERTIFICATE` | No | `false` | Set to `true` for a self-signed router certificate. This disables certificate validation. |
| `TELEGRAM_BOT_TOKEN` | Yes | — | Token issued by BotFather. |
| `TELEGRAM_CHAT_ID` | Yes | — | Non-zero numeric ID of the chat that receives alerts. |
| `LANTERN_PUBLIC_BASE_URL` | No | — | Public dashboard URL included in Telegram alerts. |
| `LANTERN_POLL_INTERVAL_SECONDS` | No | `15` | DHCP lease polling interval. The minimum is 5 seconds. |
| `DATABASE_PATH` | No | `/data/lantern.db` in the container | SQLite database path. |

If your MikroTik router uses a self-signed certificate, prefer installing a trusted certificate when possible. Otherwise set `MIKROTIK_ALLOW_INVALID_CERTIFICATE=true` only on a network you trust.

## Configure Telegram

1. Open [BotFather](https://t.me/BotFather), run `/newbot`, and copy the bot token.
2. Open your new bot and send it `/start`. For group alerts, add the bot to the group and send a message there.
3. Open the following URL, replacing `<token>`, and find the destination chat's numeric `id` in `result[].message.chat.id`:

```text
https://api.telegram.org/bot<token>/getUpdates
```

Set `TELEGRAM_BOT_TOKEN` and `TELEGRAM_CHAT_ID` in `docker-compose.yml`, then restart Lantern with `docker compose up -d`. Group chat IDs are usually negative.

## Configure MikroTik RouterOS

Lantern requires RouterOS 7 and HTTPS REST access. Run these commands as a router administrator, replacing the password, certificate name, and Lantern host IP:

```routeros
/user group add name=lantern policy=read,rest-api
/user add name=lantern group=lantern password="password" address=192.168.88.10/32
/ip service set www-ssl certificate=router-certificate disabled=no
```

If firewall rules block access to the router, allow HTTPS from the Lantern host before the input drop rule:

```routeros
/ip firewall filter add chain=input action=accept protocol=tcp dst-port=443 src-address=192.168.88.10/32 comment="Allow Lantern REST API"
```

Test the lease endpoint:

```sh
curl -u 'lantern:password' https://192.168.88.1/rest/ip/dhcp-server/lease
```

For a self-signed certificate, use `curl -k` and set `MIKROTIK_ALLOW_INVALID_CERTIFICATE=true`. Set `MIKROTIK_BASE_URL` to the router origin, such as `https://192.168.88.1`, without `/rest`.

## Local development

Set the required environment variables, then run:

```sh
dotnet run --project Lantern/Lantern.csproj
```

The application listens on <http://localhost:8080>. Run the tests with:

```sh
dotnet test lantern.slnx
```

## License

Lantern is available under the [MIT License](LICENSE).
