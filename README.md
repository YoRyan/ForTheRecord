# ForTheRecord

Many software libraries have been written to make it easier for self-hosted software to send notifications. The idea is simple: A programmer needs just one library to support many different notification targets. Unfortunately, the proliferation of these libraries has created yet another problem for the end user: Picking a notification service that all of these libraries can send notifications to.

ForTheRecord is a service that exposes the one persistent message store you are most likely to check—your Gmail or IMAP email inbox—in a way that is compatible with most every notification library. It includes endpoints designed specifically for:

* [Apprise](https://appriseit.com/)
* [Notify (the Go library)](https://github.com/nikoksr/notify)
* [Ntfy](https://ntfy.sh/)
* [Shoutrrr](https://containrrr.dev/shoutrrr/)
* SMTP
* cURL
* JSON webhook

```sh
curl -H 'From: me' -H 'Subject: Awesome Notification' -d body='Hello, World!' fortherecord.local/api/gmail/messages/import
```

```sh
NTFY_TOPIC=fortherecord.local/ntfy/INBOX ntfy pub -t 'My Awesome Notification' 'Hello, World!'
```

Why the need for a bridge service? The official Gmail API makes generating emails a cinch, but notification libraries do not include support for it, because the HTTP and OAuth flows needed to talk successfully to Gmail are quite complicated and the libraries are not willing to take on a heavy Google SDK dependency. For email providers other than Gmail, ForTheRecord is still useful as a way to expose your IMAP inbox to your homelab in a secure, limited fashion—more secure than, for example, duplicating your SMTP credentials across every app's configuration. And in the future, if IMAP providers follow in Gmail's footsteps and begin to require two-factor authentication, it may become necessary to use ForTheRecord to negotiate the OAuth flows and continue to deliver email notifications.

I consider ForTheRecord the spiritual successor to my [SMTP Translator](https://github.com/YoRyan/smtp-translator) and [Mailrise](https://github.com/YoRyan/mailrise) projects.

## How to Obtain

Official Docker images are available from Docker Hub (`docker.io/YoRyan/ForTheRecord`) and GitHub (`ghcr.io/YoRyan/ForTheRecord`).

To build the service yourself, install a .NET SDK, and then run `dotnet restore` in the source repository.

## Setup for Gmail

First, you'll have to obtain credentials from Google that will allow ForTheRecord to use the Gmail API. The process is very similar to that of[ gogcli](https://github.com/steipete/gogcli?tab=readme-ov-file#quick-start), the Google CLI that has become so fashionable among OpenClaw users:

1. [Create](https://console.cloud.google.com/projectcreate) a project for ForTheRecord in Google Cloud Console.
2. [Enable](https://console.cloud.google.com/apis/api/gmail.googleapis.com) the Gmail API for this project.
3. [Configure](https://console.cloud.google.com/auth/branding) your project's OAuth branding. Personal Google accounts can only create "External" projects, but this is okay.
4. Your project will be initialized in the "Testing" state. You'll have to [add](https://console.cloud.google.com/auth/audience) yourself (or whichever Google account you want to forward mail to) as a test user.
5. [Create](https://console.cloud.google.com/auth/clients) a new client for your project. Choose the "Desktop" type.
6. Download the JSON secrets file that Google provides for your client.

To get up and running, ForTheRecord requires:

* A configuration file in the TOML format
* A copy of the JSON secrets file obtained from Google
* A persistent directory that can be written to by the service, so that the Google SDK can store and retrieve refresh tokens

A minimum viable configuration is as follows:

```toml
[google]
credentials = "/google/client_secret.json"
# It's a good idea to stick with this directory because it's encoded as a volume
# in the Dockerfile definition. That means that if you mount a volume at this
# directory, Docker will automatically populate it with the correct permissions.
tokens_store = "/google/tokensstore"

[http]
# This is an *array* of strings, even if you're only specifying one address!
listen_urls = ["http://[::]:8080"]
```

It specifies the paths to the Google secrets file and token storage directory and also specifies at least one listening address for the HTTP server.

Use the `test-gmail` mode to authenticate with Gmail and verify the connection is working:

```sh
docker run -it --rm -v /path/to/config.toml:/config.toml:ro -v /path/to/client_secret.json:/google/client_secret.json:ro -v googletokens:/google/tokensstore docker.io/YoRyan/ForTheRecord test-gmail --config /config.toml
```

To start listening for traffic and writing emails, use the `serve-gmail` mode:

```sh
docker run -p 8080:8080 -v /path/to/config.toml:/config.toml:ro -v /path/to/client_secret.json:/google/client_secret.json:ro -v googletokens:/google/tokensstore docker.io/YoRyan/ForTheRecord serve-gmail --config /config.toml
```

A suggested Docker Compose file is as follows:

```yaml
services:
  fortherecord:
    image: ghcr.io/yoryan/fortherecord
    restart: unless-stopped
    cap_add:
      - CAP_NET_BIND_SERVICE
    configs:
      - source: fortherecord
        target: /config.toml
    volumes:
      - ./client_secret.json:/google/client_secret.json:ro
      - fortherecord-tokens:/google/tokensstore
    command: serve-gmail --config /config.toml

configs:
  fortherecord:
    content: |
      [google]
      credentials = "/google/client_secret.json"
      tokens_store = "/google/tokensstore"

      [http]
      listen_urls = ["http://[::]:80"]

volumes:
  fortherecord-tokens:
```

## Setup for IMAP

To get up and running, ForTheRecord requires a TOML-formatted configuration file that includes your IMAP credentials. Currently, IMAP credentials can only consist of a username and password; two-factor or SASL authentication is not available.

A minimum viable configuration is as follows:

```toml
[imap]
# Use "imap://" for an unencrypted connection.
connect_url = "imaps://imap.example.com:993"
username = "AzureDiamond"
password = "hunter2"

[http]
# This is an *array* of strings, even if you're only specifying one address!
listen_urls = ["http://[::]:8080"]

```

It specifies your IMAP credentials and at least one listening address for the HTTP server.

Use the `test-imap` mode to authenticate with the IMAP server and verify the connection is working:

```sh
docker run -it --rm -v /path/to/config.toml:/config.toml:ro docker.io/YoRyan/ForTheRecord test-imap --config /config.toml
```

To start listening for traffic and writing emails, use the `serve-imap` mode:

```sh
docker run -p 8080:8080 -v /path/to/config.toml:/config.toml:ro docker.io/YoRyan/ForTheRecord serve-imap --config /config.toml
```

A suggested Docker Compose file is as follows:

```yaml
services:
  fortherecord:
    image: ghcr.io/yoryan/fortherecord
    restart: unless-stopped
    cap_add:
      - CAP_NET_BIND_SERVICE
    configs:
      - source: fortherecord
        target: /config.toml
    command: serve-imap --config /config.toml

configs:
  fortherecord:
    content: |
      [imap]
      connect_url = "imaps://imap.example.com:993"
      username = "AzureDiamond"
      password = "hunter2"

      [http]
      listen_urls = ["http://[::]:80"]
```

## Use Cases

### One-Line Script Notification

### Apprise URL (Change Detection, Home Assistant, Etc.)

### SMTP Forwarder

## Configuration Reference

### Requiring Authentication

### Custom Templates

### Full TOML Reference
