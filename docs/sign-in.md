# Sign-in

DockiUp runs without login by default. To require sign-in, connect an OpenID Connect provider such as
Keycloak, Authentik or Pocket ID.

1. In your provider, create a client for DockiUp with the redirect URL `https://<your-dockiup-address>`.

2. Add these lines to the `environment` of the `dockiup` service in `compose.yml`:

   ```yaml
         Oidc__Authority: https://auth.example.com/realms/home
         Oidc__ClientId: dockiup
   ```

3. Run `docker compose up -d`.

DockiUp now asks everyone to sign in, and shows their name and picture.
