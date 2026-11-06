# Development instructions

- Preserve the AI-free baseline on main; use feature/chatgpt-login for the preview.
- Keep CAD writes in the command context, use transactions, and validate selected objects before applying AI plans.
- Do not introduce paid API fallbacks or MCP into this repository.
- Never commit credentials, drawings, catalogs, SDK DLLs, generated files, or personal data.
- Use the GitHub account's noreply identity for both commit author and committer. Inspect all published commit metadata.
- Run the relevant build and tests; report live-account and CAD-host checks separately.
