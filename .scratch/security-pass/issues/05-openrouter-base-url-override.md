# Configurable OpenRouter base URL can exfiltrate the key

Status: open
Type: task

`OpenRouter.cs:36-41` lets `Climber:OpenRouterBaseUrl` override the endpoint, and
`OpenRouterClient.cs:167-171` sends the Bearer key there. Config-only (not user input), so
low, but a typo or tampered config sends the key to an arbitrary host.

## Fix

Validate the base URL is `https` and the expected `openrouter.ai` host, or remove the
override.
