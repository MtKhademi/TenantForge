# TenantForge deployment

This follows Oracle's `release-*` workflow: GitHub Actions builds the images,
streams them over SSH to the same Ubuntu server, and starts Docker Compose.
There is no registry. Oracle's `oracle` container and port `8580` are untouched.
TenantForge uses its own Compose project, database volume, media volume and
Data Protection key volume. The web service binds `127.0.0.1:8581` by default.

## One-time setup

1. Generate a dedicated, passwordless Ed25519 deploy key on your own computer
   and authorize its **public** half for `oracle-deploy` on the server. This is
   a separate key from Oracle's deploy key, although both currently authorize
   the same server account. In WSL, run:

   ```bash
   install -m 700 -d ~/.ssh
   ssh-keygen -t ed25519 -f ~/.ssh/tenantforge_deploy -C tenantforge-github-actions -N ''
   ssh-copy-id -i ~/.ssh/tenantforge_deploy.pub oracle-deploy@45.82.137.126
   ssh -i ~/.ssh/tenantforge_deploy -o IdentitiesOnly=yes oracle-deploy@45.82.137.126 'docker info >/dev/null && echo ready'
   ```

   If the existing Oracle SSH credential is not offered automatically during
   `ssh-copy-id`, pass `-o IdentityFile=/path/to/existing/oracle/private/key`
   to that command. `ssh-copy-id` appends the new public key to the deploy
   user's `~/.ssh/authorized_keys` and leaves Oracle's key in place.
2. In **MtKhademi/TenantForge** repository Actions secrets, set
   `TENANTFORGE_SSH_PRIVATE_KEY` to the **complete contents** of
   `~/.ssh/tenantforge_deploy` (including BEGIN/END lines). For
   `TENANTFORGE_SSH_HOST_KEY`, use the existing, verified Oracle `known_hosts`
   line for `45.82.137.126`, since the server has not changed. You can also
   retrieve and verify that line as follows (compare the two SHA256 fingerprints
   using your existing trusted SSH connection before setting the secret):

   ```bash
   ssh-keyscan -t ed25519 45.82.137.126 2>/dev/null > ~/.ssh/tenantforge_host_key
   ssh-keygen -lf ~/.ssh/tenantforge_host_key
   ssh oracle-deploy@45.82.137.126 'ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub'
   ```

   To set both secrets without printing the private key in a terminal, use
   GitHub CLI after the fingerprint check:

   ```bash
   gh secret set TENANTFORGE_SSH_PRIVATE_KEY -R MtKhademi/TenantForge < ~/.ssh/tenantforge_deploy
   gh secret set TENANTFORGE_SSH_HOST_KEY -R MtKhademi/TenantForge < ~/.ssh/tenantforge_host_key
   ```

   Keep the private key off chat, commits and the server; only the public key
   belongs in `authorized_keys`.
3. On the server, use the existing `oracle-deploy` account (which already has
   Docker access) to create `/opt/tenantforge` and a private
   `/opt/tenantforge/tenantforge.env`. Copy
   `deploy/tenantforge.env.example`, replace every placeholder, and set file
   mode `600` and owner `oracle-deploy`. The account must be able to write the
   directory so Actions can upload `compose.yml`.
4. Provide a real PostgreSQL password, JWT signing key and production admin
   credentials. Choose a real ZarinPal merchant ID and **public HTTPS** URLs
   for both `PublicApiBaseUrl` and `FrontendResultBaseUrl`. The example values
   are placeholders and cannot run a useful Production installation. The
   configured callback must be reachable through the reverse proxy at the
   public API URL. Both IAM and Shop connect to the Compose database using
   `TENANTFORGE_DB_PASSWORD`; neither service exposes PostgreSQL on a host port.
5. Configure a reverse proxy with HTTPS to `127.0.0.1:8581`. Route all paths
   through this web container: `/api/` and `/health` proxy to the API internally,
   and other paths serve the React app. Configure DNS/TLS before using the
   ZarinPal gateway. Leave the default loopback bind in place. If traffic goes
   through another trusted proxy, configure the API's `ForwardedHeaders`
   allowlist for the real proxy network before relying on per-IP rate limits.
6. Create a GitHub **production** environment if environment policies or
   reviewers are desired. The workflow references this environment; its
   absence is not a substitute for the two repository secrets.

For example, once SSHed in as `oracle-deploy` with an `/opt/tenantforge`
directory owned by that account:

```bash
install -m 700 -d /opt/tenantforge
# Copy deploy/tenantforge.env.example to /opt/tenantforge/tenantforge.env,
# edit all placeholders with a text editor, then:
chmod 600 /opt/tenantforge/tenantforge.env
```

## Release

Merge the deployment PR to `main` first. Create and push a new tag pointing
at the tested commit, e.g. `release-1.0.0`. CI runs backend integration tests,
frontend lint and build; only after they pass does the deploy job build, transfer
and start `tenantforge-api` and `tenantforge-web`. `docker compose up` keeps
the three persistent volumes across releases. Never use `docker compose down -v`
on the production installation.

The deployed site should respond to `curl http://127.0.0.1:8581/health` on
the server, and to `https://your-domain.example/health` via the reverse proxy.
The release workflow waits for the web health check, which in turn checks the
API. A successful health check confirms the server started; verify sign-in,
catalog and payment separately. Tagging does not create production secrets,
configure DNS or install the HTTPS reverse proxy.

Check the installation from the server:

```bash
cd /opt/tenantforge
TENANTFORGE_IMAGE_TAG=release-1.0.0 docker compose --env-file tenantforge.env -f compose.yml ps
curl --fail http://127.0.0.1:8581/health
```

**Operational note:** the API applies IAM and Shop migrations during startup.
Back up the database and media volume before a release with schema changes.
Docker Compose does not roll database migrations back if a new release fails;
the workflow reports failure if the health check does not pass.
