package main

import (
	"testing"
)

func TestAuthConfigMissingCreds(t *testing.T) {
	cfg := &config{}
	cfg.Google.Tokens = "tokens.json"
	if err := cfg.validateForAuth(); err == nil {
		t.Error("validateForAuth() = nil")
	}
}

func TestAuthConfigMissingTokens(t *testing.T) {
	cfg := &config{}
	cfg.Google.Credentials = "credentials.json"
	if err := cfg.validateForAuth(); err == nil {
		t.Error("validateForAuth() = nil")
	}
}
func TestAuthConfigIsValid(t *testing.T) {
	cfg := &config{}
	cfg.Google.Credentials = "credentials.json"
	cfg.Google.Tokens = "tokens.json"
	if err := cfg.validateForAuth(); err != nil {
		t.Error("validateForAuth() != nil")
	}
}

func TestServeConfigMissingAddresses(t *testing.T) {
	cfg := &config{}
	if err := cfg.validateForServe(); err == nil {
		t.Error("validateForAuth() = nil")
	}
}

func TestServeConfigNoAuthHttp(t *testing.T) {
	cfg := &config{}
	cfg.Http.Address = "[::1]:8080"
	if err := cfg.validateForServe(); err != nil {
		t.Error("validateForAuth() != nil")
	}
}

func TestServeConfigNoAuthSmtp(t *testing.T) {
	cfg := &config{}
	cfg.Smtp.Address = "[::1]:1025"
	if err := cfg.validateForServe(); err != nil {
		t.Error("validateForAuth() != nil")
	}
}
