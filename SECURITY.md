# Credential handling

Peak uses the installed Codex CLI and its Windows credential store. It does not read or copy Codex's `auth.json`, accept API keys, or persist provider tokens. Claude's local bridge accepts status-line observations and saves only sanitized usage fields and activity fingerprints. A backup of a previous Claude status-line command is protected using Windows DPAPI for the current user.

Preferences, usage caches, bridge setup, and task observations are stored outside the repository under `%LOCALAPPDATA%/UsageNotch`. Chat titles stay in memory, and persisted usage readings omit account email labels. Provider output and prompts are not written to diagnostic logs. A startup failure can write a local exception diagnostic containing machine paths; do not publish it without reviewing it.

The repository ignores build outputs, research, environment files, credential files, provider data, and diagnostics. Ignore rules do not remove files already committed. Review the staged file list before each publication and scan the staged content with [Gitleaks](https://github.com/gitleaks/gitleaks):

```powershell
git diff --cached --name-only
gitleaks git --pre-commit --staged --redact --no-banner
```

Tests use synthetic observations and temporary directories. Do not replace fixtures with real account data, CLI configuration, conversation logs, screenshots, or credentials. No scanner can guarantee detection of every secret; review unusual files and findings before pushing.
