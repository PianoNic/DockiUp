# Variables and secrets

Each project's **Environment** tab holds the values its compose file uses as `${NAME}`.

- **Variable**: a plain value you can read and change, like `PORT` or `LOG_LEVEL`.
- **Secret**: encrypted with your `VAULT_MASTER_KEY`. It is never shown again, only replaced, and is hidden in deploy logs.

Give entries a **group** to keep them tidy, for example `Database` or `Mail`.

## Move an existing .env

1. Open the project's **Environment** tab and press **Import .env**.
2. Check each line: names like `*_PASSWORD`, `*_TOKEN` or `*_KEY` start as secrets, and groups come from the file's section comments.
3. Press **Import**. Leave **Remove the imported lines** on to take them out of `.env`.

The values apply on the next deploy. Anything still in `.env` keeps working; a value in the Environment tab wins over the same name in `.env`.
