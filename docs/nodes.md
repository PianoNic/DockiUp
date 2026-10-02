# More hosts

A node is another host whose Docker DockiUp manages. The node connects to DockiUp, so it needs no open ports.

1. In `.env`, set `PUBLIC_URL` to the address other hosts reach DockiUp at (not `localhost`):

   ```env
   PUBLIC_URL=https://dockiup.example.com
   ```

   Then run `docker compose up -d`.

2. On the **Nodes** page, press **Add node** and copy the compose file it shows.

3. Run that compose file on the new host with `docker compose up -d`.

The node shows as online within a few seconds. Pick it as the host when you create a project.
