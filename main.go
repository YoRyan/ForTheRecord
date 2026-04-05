package main

import (
	"bytes"
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"log"
	"net/url"
	"os"

	"github.com/pelletier/go-toml/v2"
	"github.com/tg123/go-htpasswd"
	"golang.org/x/oauth2"
	"golang.org/x/oauth2/google"
	"google.golang.org/api/gmail/v1"
)

func main() {
	ctx := context.Background()

	var (
		doAuth     bool
		configPath string
		credsPath  string
		tokensPath string
	)
	flag.BoolVar(&doAuth, "auth", false, "Request access and refresh tokens from Google instead of running the service, and write the tokens to the tokens path.")
	flag.StringVar(&configPath, "config", "", "Path to the configuration file.")
	flag.StringVar(&credsPath, "creds", "", "Path to the JSON credentials file obtained from Google.")
	flag.StringVar(&tokensPath, "tokens", "", "Path to the file containing access and refresh tokens written in auth mode.")
	flag.Parse()

	if configPath == "" {
		log.Fatalln("Missing path to configuration file.")
	} else if credsPath == "" {
		log.Fatalln("Missing path to credentials file.")
	} else if tokensPath == "" {
		log.Fatalln("Missing path to tokens file.")
	}

	oa := readCredentials(credsPath)
	if doAuth {
		doRequestAuth(ctx, oa, tokensPath)
	} else {
		cfg := readConfig(configPath)
		fmt.Println(cfg)
	}
}

func readCredentials(path string) (oa *oauth2.Config) {
	creds, err := os.ReadFile(path)
	if err != nil {
		log.Fatalln(err)
	}

	oa, err = google.ConfigFromJSON(creds, gmail.GmailInsertScope, gmail.GmailSendScope)
	if err != nil {
		log.Fatalln(err)
	}
	return
}

// Run in request tokens mode.
func doRequestAuth(ctx context.Context, oa *oauth2.Config, outPath string) {
	authURL := oa.AuthCodeURL("", oauth2.AccessTypeOffline)
	fmt.Println("Navigate to the following URL in your browser:")
	fmt.Println(authURL)

	var redirectURL string
	fmt.Println("")
	fmt.Println("Once you've authorized the request, your browser will redirect to an http://localhost URL that will fail to load. Paste the entire URL here:")
	if _, err := fmt.Scan(&redirectURL); err != nil {
		log.Fatalln("Unable to read redirected URL:", err)
	}

	parsedURL, err := url.Parse(redirectURL)
	if err != nil {
		log.Fatalln("Unable to parse redirected URL:", err)
	}

	tokens, err := oa.Exchange(ctx, parsedURL.Query().Get("code"))
	if err != nil {
		log.Fatalln("Unable to retrieve tokens from Google:", err)
	}

	fmt.Println("")
	fmt.Println("Your access and refresh tokens:")
	b, err := json.Marshal(tokens)
	if err != nil {
		log.Fatalln("Error converting tokens to JSON:", err)
	}

	if _, err := os.Stdout.Write(b); err != nil {
		log.Fatalln(err)
	}
	fmt.Println("")

	if err := os.WriteFile(outPath, b, 0600); err != nil {
		log.Fatalln("Error writing tokens to output file:", err)
	}
	fmt.Println("")
	fmt.Println("Tokens successfully saved to ", outPath, ".")
}

func readConfig(path string) (cfg config) {
	b, err := os.ReadFile(path)
	if err != nil {
		log.Fatalln(err)
	}

	if err := toml.Unmarshal(b, &cfg); err != nil {
		log.Fatalln(err)
	}

	if cfg.http.address == "" && cfg.smtp.address == "" {
		log.Fatalln("No HTTP or SMTP listener is configured. There is nothing to do.")
	} else if cfg.htpasswd.File == nil {
		log.Println("WARNING: htpasswd block is not configured. Authentication will be disabled.")
	}
	return
}

type config struct {
	htpasswd users
	scopes   struct {
		gmail struct {
			insert []string
			send   []string
		}
	}
	http struct {
		address string
	}
	smtp struct {
		address string
	}
}

type users struct {
	*htpasswd.File
}

func (u *users) UnmarshalText(b []byte) (err error) {
	badLineHandler := func(err error) {
		log.Println("Bad line in htpasswd block:", err)
	}
	u.File, err = htpasswd.NewFromReader(bytes.NewReader(b), htpasswd.DefaultSystems, badLineHandler)
	return
}
