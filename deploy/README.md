# TenantForge deployment

This follows Oracle's `release-*` workflow: GitHub Actions builds the images,
streams them over SSH to the same Ubuntu server, and starts Docker Compose.
There is no registry. Oracle's `oracle` container and port `8580` are untouched.
TenantForge uses its own Compose project, database volume, media volume and
Data Protection key volume. This initial deployment is a **private Development
demo** using the Sandbox payment provider. The web service binds
`127.0.0.1:8581`; it is not exposed to the internet.

## One-time setup

1. Generate a dedicated, passwordless Ed25519 deploy key on your own computer
   and authorize its **public** half for `oracle-deploy` on the server. This is
   a separate key from Oracle's deploy key, although both currently authorize
   the same server account. In WSL, run:

   ```bash
   install -m 700 -d ~/.ssh
   ssh-keygen -t ed25519 -f ~/.ssh/tenantforge_deploy -C tenantforge-github-actions -N ''
   ssh -i ~/.ssh/oracle_deploy -o IdentitiesOnly=yes \
     oracle-deploy@45.82.137.126 \
     'umask 077; mkdir -p ~/.ssh; read -r key; grep -qxF "$key" ~/.ssh/authorized_keys 2>/dev/null || printf "%s\n" "$key" >> ~/.ssh/authorized_keys' \
     < ~/.ssh/tenantforge_deploy.pub
   ssh -i ~/.ssh/tenantforge_deploy -o IdentitiesOnly=yes oracle-deploy@45.82.137.126 'docker info >/dev/null && echo ready'
   ```

   This uses the existing `~/.ssh/oracle_deploy` key to authenticate only the
   one-time public-key installation. It adds the new public key to the deploy
   user's `~/.ssh/authorized_keys` if absent and leaves Oracle's key in place.
   Do not use `ssh-copy-id` with `IdentityFile=oracle_deploy` here: its
   already-installed check may succeed with the old key and skip the new one.
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
   ssh -i ~/.ssh/oracle_deploy -o IdentitiesOnly=yes oracle-deploy@45.82.137.126 'ssh-keygen -lf /etc/ssh/ssh_host_ed25519_key.pub'
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
4. Choose a long random PostgreSQL password, JWT signing key and demo admin
   password. Set all placeholders in the env file. IAM and Shop both connect
   to the same Compose database using `TENANTFORGE_DB_PASSWORD`; PostgreSQL
   does not expose a host port. Payment simulation is enabled in this private
   Development environment. Never publish it on a public interface.
5. Optionally create a GitHub **staging** environment if environment policies or
   reviewers are desired. The workflow references this environment; its
   absence is not a substitute for the two repository secrets.

Create the server directory with the existing `ubuntu` sudo account (substitute
its working SSH identity if necessary), then create the private env file as
`oracle-deploy`:

```bash
ssh -i ~/.ssh/ar-vps-company.pem ubuntu@45.82.137.126 \
  'sudo install -d -o oracle-deploy -g oracle-deploy -m 700 /opt/tenantforge'
ssh -t -i ~/.ssh/tenantforge_deploy oracle-deploy@45.82.137.126
umask 077
nano /opt/tenantforge/tenantforge.env
# Paste the keys from deploy/tenantforge.env.example with real passwords.
chmod 600 /opt/tenantforge/tenantforge.env
exit
```

## Release

Merge the deployment PR to `main` first. Create and push a new tag pointing
at the tested commit, e.g. `release-1.0.0`. CI runs backend integration tests,
frontend lint and build; only after they pass does the deploy job build, transfer
and start `tenantforge-api` and `tenantforge-web`. `docker compose up` keeps
the three persistent volumes across releases. Never use `docker compose down -v`
on this installation.

The deployed site should respond to `curl http://127.0.0.1:8581/health` on
the server. To browse it privately on your own computer, open an SSH tunnel
from WSL, leave it running, then open `http://localhost:8581` in the Windows
browser:

```bash
ssh -N -i ~/.ssh/tenantforge_deploy -o IdentitiesOnly=yes \
  -L 127.0.0.1:8581:127.0.0.1:8581 oracle-deploy@45.82.137.126
```

The release workflow waits for the web health check, which in turn checks the
API. A successful health check confirms the server started; verify sign-in,
catalog and sandbox payment separately. Do not change `TENANTFORGE_BIND_ADDRESS`
to `0.0.0.0` in Development. Public Production deployment is a separate change
that needs HTTPS, real payment provider configuration and production secrets.

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
