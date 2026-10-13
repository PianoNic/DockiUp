# AI assistants (MCP)

DockiUp speaks [MCP](https://modelcontextprotocol.io) at `https://<your-dockiup-address>/mcp`, so assistants
like Claude Code can look at your projects, read logs and deploy.

What an assistant can do:

| Tool | Does |
|---|---|
| `list_projects` | Every project on every host, with its containers |
| `get_logs` | Recent log lines of a project |
| `list_deployments`, `get_deployment` | Deployment history and logs |
| `list_image_updates` | Services with a newer image |
| `deploy` | Queues a deployment, like **Deploy now** |
| `control_project` | Starts, stops or restarts a project |

It can't delete anything, prune, or read variables and secrets.

## Connect Claude Code

With [sign-in](/sign-in) turned on, the assistant signs in as you through your provider.

1. In your provider, add `http://localhost:33418/callback` as a redirect URL of DockiUp's client.

2. Run:

   ```bash
   claude mcp add --transport http dockiup https://<your-dockiup-address>/mcp \
     --client-id <your Oidc__ClientId> --callback-port 33418
   ```

3. In Claude Code, run `/mcp`, pick **dockiup** and sign in.

Set `PUBLIC_URL` (or `Oidc__PublicUrl`) so the sign-in links point at the right address.

Without sign-in, `/mcp` is open like the rest of DockiUp: leave out `--client-id` and `--callback-port`.
