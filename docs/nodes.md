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

## Server stats

The **Nodes** page shows every host, this one included: CPU, memory, disks, network traffic, GPUs and the last
hour. Open a host for its history (up to a week), its internet speed, its GPUs with the containers using them,
and its biggest containers. The dashboard shows the busiest hosts at a glance.

Hosts read these through `- /:/host:ro` in their compose file (read-only). Without it, CPU and memory still
show, but disks, network traffic and GPU use per container don't. Added DockiUp before this line was in the
compose? Add it to the `volumes` of DockiUp and of each node, then `docker compose up -d`.

### GPUs

Any GPU the kernel knows shows up, whatever the vendor; each shows what its driver reports. AMD and Intel
work as they are. NVIDIA's driver only reports to its own tools, so give that host's DockiUp container the GPU:

```yaml
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: all
              capabilities: [gpu, utility]
```

This needs the NVIDIA Container Toolkit on that host.

### Internet speed

Each host measures its connection once a day against Cloudflare's speed test, and on **Test now**. A test runs
for about 15 seconds and can move a few hundred MB on a fast line; change how often (or turn it off) in
**Settings > General**.
