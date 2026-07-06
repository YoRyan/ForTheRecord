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
# in the Containerfile definition. That means that if you mount a volume at this
# directory, Docker will automatically populate it with the correct permissions.
tokens_store = "/google/tokensstore"

[http]
# This is an *array* of strings, even if you're only specifying one address!
listen_urls = ["http://[::]:8080"]
```

It specifies the paths to the Google secrets file and token storage directory and also specifies at least one listening address for the HTTP server.

Use the `test-gmail` mode to authenticate with Gmail and verify the connection is working:

```sh
docker run -it --rm -v /path/to/config.toml:/config.toml:ro -v /path/to/client_secret.json:/google/client_secret.json:ro -v googletokens:/google/tokensstore ghcr.io/YoRyan/ForTheRecord test-gmail --config /config.toml
```

To start listening for traffic and writing emails, use the `serve-gmail` mode:

```sh
docker run -p 8080:8080 -v /path/to/config.toml:/config.toml:ro -v /path/to/client_secret.json:/google/client_secret.json:ro -v googletokens:/google/tokensstore ghcr.io/YoRyan/ForTheRecord serve-gmail --config /config.toml
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
docker run -it --rm -v /path/to/config.toml:/config.toml:ro ghcr.io/YoRyan/ForTheRecord test-imap --config /config.toml
```

To start listening for traffic and writing emails, use the `serve-imap` mode:

```sh
docker run -p 8080:8080 -v /path/to/config.toml:/config.toml:ro ghcr.io/YoRyan/ForTheRecord serve-imap --config /config.toml
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

### One-Line Notification

Generate notifications from shell scripts without any external dependencies:

```sh
curl -H 'From: me' -H 'Subject: My Notification' -H 'Content-Type: text/plain' -d 'Hello, World!' http://fortherecord.local/api/gmail/messages/import/ez
```

(At minimum, emails must include a From: header.)

### Apprise

ForTheRecord's `/apprise` endpoint is designed specifically for Apprise's generic JSON notification [service](https://appriseit.com/services/json/), including support for attachments. But care must be taken if the notification sender uses HTML or Markdown markup. (Change Detection is one application that can be configured to do so.) The behavior of the JSON plugin is [not intuitive](https://github.com/caronc/apprise/issues/1600) when dealing with these formats:

* Apprise strips HTML markup from the message body unless the `format=html` switch, which enables HTML delivery for any plugin, is present.

* Apprise does not communicate the original format in the JSON payload, so ForTheRecord has to assume that the body is plain text unless otherwise specified. You can specify the format yourself by setting the `ftr_input_format` field within the payload.

In short, to connect an Apprise-powered application to ForTheRecord, use a URL in one of the following formats:

```
json://fortherecord.local/apprise?format=text&:ftr_input_format=text
json://fortherecord.local/apprise?format=html&:ftr_input_format=html
json://fortherecord.local/apprise?format=markdown&:ftr_input_format=markdown
```

### Notify (Go library)

Use the `/go/notify` endpoint with the default settings:

```go
httpService := http.New()
httpService.AddReceiversURL("http://fortherecord.local/go/notify")
```

### Ntfy

ForTheRecord emulates a self-hosted Ntfy server, allowing applications that integrate Ntfy to publish directly to your inbox. Not all of the (numerous) mainline Ntfy features are supported, but the most important ones are. The following are known *not* to work:

* Updating and deleting notifications
* Scheduled delivery
* Phone calls
* Android broadcast action buttons
* Message templating (Ntfy's system; ours still works)
* Email notifications and publishing (kind of redundant...)

To use ForTheRecord with Ntfy, set the topic to one of the following:

```
fortherecord.local/ntfy/<target>
fortherecord.local/ntfy_template/<template>/<target>
```

If a Gmail inbox is configured, `<target>` sets the Gmail label ID for the new message (if in doubt about this, use `INBOX`). If an IMAP inbox is configured, `<target>` selects the IMAP folder to place the new message into. For the template endpoint, `<template>` selects the configured template to use.

If the sender publishes Ntfy notifications using JSON payloads (e.g. Jellyfin), use `fortherecord.local/ntfy` as the server.

The Liquid variables made available to Ntfy templates are a perfect match for the fields in Ntfy's JSON message [format](https://docs.ntfy.sh/publish/#publish-as-json), even if the notification was submitted using one of the non-JSON endpoints.

### Shoutrrr

You can use the Generic [service](https://containrrr.dev/shoutrrr/dev/services/generic/) with one of two endpoints:

```
generic://fortherecord.local/shoutrrr
generic://fortherecord.local/shoutrrr/json?template=json
```

For the non-JSON endpoint, the only available Liquid variable is `message`.

### SMTP Forwarder

You can activate the SMTP server using the `smtp.listen_urls` key:

```toml
[smtp]
# Like http.listen_urls, this is also an array.
listen_urls = ["http://[::]:2525"]
```

You can use this server to import emails from applications that can only speak SMTP. Unlike a true SMTP server, all emails received by ForTheRecord will be delivered to your Gmail or IMAP inbox, whether they were addressed to yourself or not.

### HTTP API

These endpoints pass through HTTP request headers directly as email headers. As always, messages must at least include a From: header to be valid email.

#### /api/gmail/messages/import (POST)

Accepts form data POST'ed as `application/x-www-form-urlencoded`. The message `Content-Type` is read from the `bodytype` field. Only available if a Gmail inbox is configured.

| Field | Value |
| --- | --- |
| label_id | Destination Gmail label ID |
| body | Message body |
| body_type | Body MIME type; defaults to `text/plain` |
| internal_date_source | `receivedtime` or `dateheader`; see [Gmail API](https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.messages/import) |
| never_mark_spam | See [Gmail API](https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.messages/import) |
| process_for_calendar | See Gmail API |
| deleted | See Gmail API |

#### /api/gmail/messages/import/ez (POST)

The HTTP body is passed through directly as the message body. Only available if a Gmail inbox is configured.

Please note that cURL sends POST data with a `Content-Type: application/x-www-form-urlencoded` header by default, so when using cURL with this endpoint, you must override this behavior by setting `-H 'Content-Type: text/plain'`.

#### /api/imap/append (POST)

Accepts form data POST'ed as `application/x-www-form-urlencoded`. The message `Content-Type` is read from the `bodytype` field. Only available if an IMAP inbox is configured.

| Field | Value |
| --- | --- |
| folder | Destination IMAP folder |
| flag | Set IMAP flag; multiple values set multiple flags |
| keyword | Set IMAP keyword; multiple values set multiple keywords |
| body | Message body |
| body_type | Body MIME type; defaults to `text/plain` |

#### /api/imap/append/ez (POST)

The HTTP body is passed through directly as the message body. Only available if an IMAP inbox is configured.

Please note that cURL sends POST data with a `Content-Type: application/x-www-form-urlencoded` header by default, so when using cURL with this endpoint, you must override this behavior by setting `-H 'Content-Type: text/plain'`.

### JSON Webhook

The endpoint at `/api/webhook` is designed to be as flexible as possible for webhooks. Set the `ftr_template` field to select a configured template. The built-in template accepts the following JSON fields:

| Field | Type | Value |
| --- | --- | --- |
| to | string | To: header value; defaults to `me` |
| from | string | From: header value; defaults to `ForTheRecord <me>` |
| subject | string | Subject: header value |
| message | string | Message body |
| content_type | string | Body MIME type; defaults to `text/plain` |
| gmail_label_ids | array of string | List of destination Gmail label ID's; defaults to `INBOX` |
| gmail_starred | boolean | If true, applies the Gmail `STARRED` label |
| gmail_important | boolean | If true, applies the Gmail `IMPORTANT` label |
| gmail_internal_date_source | string | See [Gmail API](https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.messages/import) |
| gmail_never_mark_spam | boolean | See Gmail API |
| gmail_process_for_calendar | boolean | See Gmail API |
| gmail_deleted | boolean | See Gmail API |
| imap_folders | array of string | List of destination IMAP folders; defaults to the inbox folder |
| imap_flags | array of string | List of IMAP flags to apply |
| imap_keywords | array of string | List of IMAP keywords to apply |

## Configuration Reference

### Requiring Authentication

You can require senders to authenticate themselves with a username and password. Populate the `auth.htpasswd` key with a string containing valid combinations, one per line, in Apache htpasswd format (you can generate single lines with `htpasswd -n`). Then, authorize usernames for sending by adding them to the `google.scopes.gmail.insert` or `imap.permissions.append` keys, both of which are arrays of strings.

```toml
[auth]
htpasswd = """
AzureDiamond:$apr1$91Lq48nS$Vy6mA8ueFOGlGBsZ4sPSE/
"""

[google.scopes]
gmail.insert = ["AzureDiamond"]

[imap.permissions]
append = ["AzureDiamond"]
```

### Custom Templates

The logic that transforms notification payloads into email messages is internally implemented as a set of [Liquid](https://shopify.github.io/liquid/) templates. These [templates](ForTheRecord/Liquid/) are intended to be as flexible as possible, but should you require even more functionality, you have the option to program your own templates without recompiling ForTheRecord. To do so, insert the template as a string under the `templates.<name>` key, and then senders can select the template by referring to `<name>`.

The method of selecting a template, and then which values are made available to the template, depend on which endpoint the sender is calling. For the endpoints that accept JSON (which is most of them), the `ftr_template` field names the template.

```toml
[templates]
mytemplate = """
From: me
To: me
Subject: Test Subject

{% if ftr.is_gmail -%}
Careful! Google is reading this.
{% endif -%}

{{ message }}
"""
```

#### Liquid Variables

ForTheRecord makes the following variables available to all templates:

##### ftr.user (string/nil)

The currently authenticated username, or `nil` if authentication is not required.

##### ftr.guid (string)

Contains a string GUID randomly generated by .NET's [Guid.NewGuid](https://learn.microsoft.com/en-us/dotnet/api/system.guid.newguid/) function. This is particularly useful for MIME boundaries, which are tokens that separate components of email messages that must not conflict with any other text.

##### ftr.is_gmail (boolean)

True if a Gmail inbox is configured.

##### ftr.is_imap (boolean)

True if an IMAP inbox is configured.

##### json (object)

Maps to the top-level JSON object if this template is accepting a JSON payload. For JSON endpoints, ForTheRecord exposes the JSON fields as Liquid variables, so this variable is useful if one of the fields has a naming conflict with a built-in variable. For example, `json.ftr` can be used to access the `ftr` field, and `json.json` can be used to access the `json` field.

#### Liquid Filters

##### ftr_encode_utf8 (string -> string)

Encodes a string in a UTF-8 Base64 block (`=?UTF-8?B?...`) so that it is safe to embed in email headers.

##### ftr_markdown (string -> string)

Renders Markdown markup as HTML.

##### ftr_gemoji (string -> string/nil)

Converts an emoji shortcode to its corresponding GitHub emoji. Returns `nil` if the shortcode is not accepted by GitHub.

##### ftr_single_shell_escape (string -> string)

Quotes and escapes a string using single quotes for use as a command-line argument.

##### ftr_download (string -> object)

Download a URL, returning an object that contains the obtained Content-Type header (`content_type`) and Base64-encoded file content (`base64`).

### Troubleshooting

You can specify the log level by setting the `log_level` key to `error` (the default), `warning`, `information`, `debug`, or `trace`, which denote progressively increasing levels of log detail.

At the `information` level, ForTheRecord will print the full in-memory configuration at startup. This feature is useful if you suspect the program has misunderstood your configuration file.

### Full TOML Reference

#### log_level (string)

Set the service log level. See [#Troubleshooting](#troubleshooting).

#### auth.htpasswd (string)

Directory of valid username and password combinations for authentication in Apache htpasswd format. Setting this enables and enforces authentication for all endpoints. See [#Requiring Authentication](#requiring-authentication).

#### templates (table of string)

A map of user-defined names to strings containing templates. See [#Custom Templates](#custom-templates).

#### http.listen_urls (array of string)

Addresses for the HTTP server to listen on, in the format of `http://<ip adddress>:<port>`.

There is no default, and you must set `http.listen_urls` and/or `smtp.listen_urls`.

#### smtp.listen_urls (array of string)

Addresses for the SMTP server to listen on, in the format of `smtp://<ip address>:<port>`.

There is no default, and you must set `smtp.listen_urls` and/or `http.listen_urls`.

#### google.credentials (string)

#### google.tokens_store (string)

See [#Setup for Gmail](#setup-for-gmail).

#### google.scopes.gmail.insert (array of string)

List of usernames authorized to import notifications as Gmail messages. See [#Requiring Authentication](#requiring-authentication).

#### imap.connect_url (string)

#### imap.username (string)

#### imap.password (string)

See [#Setup for IMAP](#setup-for-imap).

#### imap.permissions.append (array of string)

List of usernames authorized to import notifications using the IMAP APPEND command. See [#Requiring Authentication](#requiring-authentication).