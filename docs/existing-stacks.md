# Existing stacks

DockiUp picks up every compose project it can read, in place: nothing is moved, copied or restarted.

To let it read your stacks, mount the folder they live in at the **same path** in `compose.yml`:

```yaml
services:
  dockiup:
    volumes:
      - /var/run/docker.sock:/var/run/docker.sock
      - /data/dockiup:/data/dockiup
      - /opt/stacks:/opt/stacks # your folder, same path on both sides
```

Then run `docker compose up -d`. Within a minute the stacks show up with deploys, logs and files.

A project whose folder isn't mounted still shows up and tells you which folder to mount.
