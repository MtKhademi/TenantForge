using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenantForge.Modules.Iam.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TsidIdentifiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                CREATE TEMP TABLE tsid_id_map (
                    kind text NOT NULL,
                    old_id uuid NOT NULL,
                    new_id bigint NOT NULL,
                    CONSTRAINT pk_tsid_id_map PRIMARY KEY (kind, old_id),
                    CONSTRAINT uq_tsid_id_map_new_id UNIQUE (new_id)
                ) ON COMMIT DROP;

                WITH base AS (
                    SELECT ((floor(extract(epoch FROM (clock_timestamp() AT TIME ZONE 'UTC' - timestamp '2020-01-01 00:00:00')) * 1000)::bigint) << 22) AS value
                ), legacy_ids AS (
                    SELECT 'account'::text AS kind, id AS old_id FROM iam_accounts
                    UNION ALL SELECT 'tenant', id FROM iam_tenants
                    UNION ALL SELECT 'membership', id FROM iam_tenant_memberships
                    UNION ALL SELECT 'role', id FROM iam_tenant_roles
                    UNION ALL SELECT 'invitation', id FROM iam_tenant_invitations
                    UNION ALL SELECT 'audit', id FROM iam_audit_events
                ), numbered AS (
                    SELECT kind, old_id, row_number() OVER (ORDER BY kind, old_id)::bigint AS rn
                    FROM legacy_ids
                )
                INSERT INTO tsid_id_map (kind, old_id, new_id)
                SELECT numbered.kind, numbered.old_id, base.value + numbered.rn
                FROM numbered CROSS JOIN base;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM tsid_id_map WHERE new_id <= 0) THEN
                        RAISE EXCEPTION 'B017 TSID migration generated a non-positive identifier.';
                    END IF;

                    IF (SELECT count(*) FROM tsid_id_map) <> (SELECT count(DISTINCT new_id) FROM tsid_id_map) THEN
                        RAISE EXCEPTION 'B017 TSID migration generated duplicate identifiers.';
                    END IF;
                END $$;

                ALTER TABLE iam_accounts ADD COLUMN id_tsid bigint;
                ALTER TABLE iam_tenants ADD COLUMN id_tsid bigint;
                ALTER TABLE iam_tenant_memberships ADD COLUMN id_tsid bigint;
                ALTER TABLE iam_tenant_memberships ADD COLUMN tenant_id_tsid bigint;
                ALTER TABLE iam_tenant_memberships ADD COLUMN account_id_tsid bigint;
                ALTER TABLE iam_tenant_roles ADD COLUMN id_tsid bigint;
                ALTER TABLE iam_tenant_roles ADD COLUMN tenant_id_tsid bigint;
                ALTER TABLE iam_tenant_member_role_assignments ADD COLUMN tenant_membership_id_tsid bigint;
                ALTER TABLE iam_tenant_member_role_assignments ADD COLUMN tenant_role_id_tsid bigint;
                ALTER TABLE iam_tenant_invitations ADD COLUMN id_tsid bigint;
                ALTER TABLE iam_tenant_invitations ADD COLUMN tenant_id_tsid bigint;
                ALTER TABLE iam_audit_events ADD COLUMN id_tsid bigint;
                ALTER TABLE iam_audit_events ADD COLUMN tenant_id_tsid bigint;
                ALTER TABLE iam_audit_events ADD COLUMN actor_account_id_tsid bigint;

                UPDATE iam_accounts a SET id_tsid = m.new_id FROM tsid_id_map m WHERE m.kind = 'account' AND m.old_id = a.id;
                UPDATE iam_tenants t SET id_tsid = m.new_id FROM tsid_id_map m WHERE m.kind = 'tenant' AND m.old_id = t.id;
                UPDATE iam_tenant_memberships tm
                SET id_tsid = mid.new_id,
                    tenant_id_tsid = mt.new_id,
                    account_id_tsid = ma.new_id
                FROM tsid_id_map mid, tsid_id_map mt, tsid_id_map ma
                WHERE mid.kind = 'membership' AND mid.old_id = tm.id
                  AND mt.kind = 'tenant' AND mt.old_id = tm.tenant_id
                  AND ma.kind = 'account' AND ma.old_id = tm.account_id;
                UPDATE iam_tenant_roles tr
                SET id_tsid = mr.new_id,
                    tenant_id_tsid = mt.new_id
                FROM tsid_id_map mr, tsid_id_map mt
                WHERE mr.kind = 'role' AND mr.old_id = tr.id
                  AND mt.kind = 'tenant' AND mt.old_id = tr.tenant_id;
                UPDATE iam_tenant_member_role_assignments a
                SET tenant_membership_id_tsid = mm.new_id,
                    tenant_role_id_tsid = mr.new_id
                FROM tsid_id_map mm, tsid_id_map mr
                WHERE mm.kind = 'membership' AND mm.old_id = a.tenant_membership_id
                  AND mr.kind = 'role' AND mr.old_id = a.tenant_role_id;
                UPDATE iam_tenant_invitations i
                SET id_tsid = mi.new_id,
                    tenant_id_tsid = mt.new_id
                FROM tsid_id_map mi, tsid_id_map mt
                WHERE mi.kind = 'invitation' AND mi.old_id = i.id
                  AND mt.kind = 'tenant' AND mt.old_id = i.tenant_id;
                UPDATE iam_audit_events e
                SET id_tsid = me.new_id,
                    tenant_id_tsid = mt.new_id,
                    actor_account_id_tsid = ma.new_id
                FROM tsid_id_map me, tsid_id_map mt, tsid_id_map ma
                WHERE me.kind = 'audit' AND me.old_id = e.id
                  AND mt.kind = 'tenant' AND mt.old_id = e.tenant_id
                  AND ma.kind = 'account' AND ma.old_id = e.actor_account_id;

                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM iam_accounts WHERE id_tsid IS NULL) THEN RAISE EXCEPTION 'B017 missing account id mapping'; END IF;
                    IF EXISTS (SELECT 1 FROM iam_tenants WHERE id_tsid IS NULL) THEN RAISE EXCEPTION 'B017 missing tenant id mapping'; END IF;
                    IF EXISTS (SELECT 1 FROM iam_tenant_memberships WHERE id_tsid IS NULL OR tenant_id_tsid IS NULL OR account_id_tsid IS NULL) THEN RAISE EXCEPTION 'B017 missing membership mapping'; END IF;
                    IF EXISTS (SELECT 1 FROM iam_tenant_roles WHERE id_tsid IS NULL OR tenant_id_tsid IS NULL) THEN RAISE EXCEPTION 'B017 missing role mapping'; END IF;
                    IF EXISTS (SELECT 1 FROM iam_tenant_member_role_assignments WHERE tenant_membership_id_tsid IS NULL OR tenant_role_id_tsid IS NULL) THEN RAISE EXCEPTION 'B017 missing role-assignment mapping'; END IF;
                    IF EXISTS (SELECT 1 FROM iam_tenant_invitations WHERE id_tsid IS NULL OR tenant_id_tsid IS NULL) THEN RAISE EXCEPTION 'B017 missing invitation mapping'; END IF;
                    IF EXISTS (SELECT 1 FROM iam_audit_events WHERE id_tsid IS NULL OR tenant_id_tsid IS NULL OR actor_account_id_tsid IS NULL) THEN RAISE EXCEPTION 'B017 missing audit mapping'; END IF;
                END $$;

                ALTER TABLE iam_audit_events DROP CONSTRAINT IF EXISTS "FK_iam_audit_events_iam_accounts_actor_account_id";
                ALTER TABLE iam_audit_events DROP CONSTRAINT IF EXISTS "FK_iam_audit_events_iam_tenants_tenant_id";
                ALTER TABLE iam_tenant_invitations DROP CONSTRAINT IF EXISTS "FK_iam_tenant_invitations_iam_tenants_tenant_id";
                ALTER TABLE iam_tenant_member_role_assignments DROP CONSTRAINT IF EXISTS "FK_iam_tenant_member_role_assignments_iam_tenant_memberships_t~";
                ALTER TABLE iam_tenant_member_role_assignments DROP CONSTRAINT IF EXISTS "FK_iam_tenant_member_role_assignments_iam_tenant_roles_tenant_~";
                ALTER TABLE iam_tenant_memberships DROP CONSTRAINT IF EXISTS "FK_iam_tenant_memberships_iam_accounts_account_id";
                ALTER TABLE iam_tenant_memberships DROP CONSTRAINT IF EXISTS "FK_iam_tenant_memberships_iam_tenants_tenant_id";
                ALTER TABLE iam_tenant_roles DROP CONSTRAINT IF EXISTS "FK_iam_tenant_roles_iam_tenants_tenant_id";

                ALTER TABLE iam_tenant_member_role_assignments DROP CONSTRAINT "PK_iam_tenant_member_role_assignments";
                ALTER TABLE iam_audit_events DROP CONSTRAINT "PK_iam_audit_events";
                ALTER TABLE iam_tenant_invitations DROP CONSTRAINT "PK_iam_tenant_invitations";
                ALTER TABLE iam_tenant_roles DROP CONSTRAINT "PK_iam_tenant_roles";
                ALTER TABLE iam_tenant_memberships DROP CONSTRAINT "PK_iam_tenant_memberships";
                ALTER TABLE iam_tenants DROP CONSTRAINT "PK_iam_tenants";
                ALTER TABLE iam_accounts DROP CONSTRAINT "PK_iam_accounts";

                DROP INDEX IF EXISTS "IX_iam_audit_events_actor_account_id";
                DROP INDEX IF EXISTS ix_iam_audit_events_tenant_action;
                DROP INDEX IF EXISTS ix_iam_audit_events_tenant_created;
                DROP INDEX IF EXISTS ix_iam_tenant_invitations_active_email;
                DROP INDEX IF EXISTS "IX_iam_tenant_member_role_assignments_tenant_role_id";
                DROP INDEX IF EXISTS ix_iam_tenant_roles_tenant_normalized_name;
                DROP INDEX IF EXISTS ix_iam_tenant_memberships_account_id;
                DROP INDEX IF EXISTS ix_iam_tenant_memberships_tenant_account;

                ALTER TABLE iam_accounts DROP COLUMN id;
                ALTER TABLE iam_accounts RENAME COLUMN id_tsid TO id;
                ALTER TABLE iam_accounts ALTER COLUMN id SET NOT NULL;

                ALTER TABLE iam_tenants DROP COLUMN id;
                ALTER TABLE iam_tenants RENAME COLUMN id_tsid TO id;
                ALTER TABLE iam_tenants ALTER COLUMN id SET NOT NULL;

                ALTER TABLE iam_tenant_memberships DROP COLUMN id;
                ALTER TABLE iam_tenant_memberships DROP COLUMN tenant_id;
                ALTER TABLE iam_tenant_memberships DROP COLUMN account_id;
                ALTER TABLE iam_tenant_memberships RENAME COLUMN id_tsid TO id;
                ALTER TABLE iam_tenant_memberships RENAME COLUMN tenant_id_tsid TO tenant_id;
                ALTER TABLE iam_tenant_memberships RENAME COLUMN account_id_tsid TO account_id;
                ALTER TABLE iam_tenant_memberships ALTER COLUMN id SET NOT NULL;
                ALTER TABLE iam_tenant_memberships ALTER COLUMN tenant_id SET NOT NULL;
                ALTER TABLE iam_tenant_memberships ALTER COLUMN account_id SET NOT NULL;

                ALTER TABLE iam_tenant_roles DROP COLUMN id;
                ALTER TABLE iam_tenant_roles DROP COLUMN tenant_id;
                ALTER TABLE iam_tenant_roles RENAME COLUMN id_tsid TO id;
                ALTER TABLE iam_tenant_roles RENAME COLUMN tenant_id_tsid TO tenant_id;
                ALTER TABLE iam_tenant_roles ALTER COLUMN id SET NOT NULL;
                ALTER TABLE iam_tenant_roles ALTER COLUMN tenant_id SET NOT NULL;

                ALTER TABLE iam_tenant_member_role_assignments DROP COLUMN tenant_membership_id;
                ALTER TABLE iam_tenant_member_role_assignments DROP COLUMN tenant_role_id;
                ALTER TABLE iam_tenant_member_role_assignments RENAME COLUMN tenant_membership_id_tsid TO tenant_membership_id;
                ALTER TABLE iam_tenant_member_role_assignments RENAME COLUMN tenant_role_id_tsid TO tenant_role_id;
                ALTER TABLE iam_tenant_member_role_assignments ALTER COLUMN tenant_membership_id SET NOT NULL;
                ALTER TABLE iam_tenant_member_role_assignments ALTER COLUMN tenant_role_id SET NOT NULL;

                ALTER TABLE iam_tenant_invitations DROP COLUMN id;
                ALTER TABLE iam_tenant_invitations DROP COLUMN tenant_id;
                ALTER TABLE iam_tenant_invitations RENAME COLUMN id_tsid TO id;
                ALTER TABLE iam_tenant_invitations RENAME COLUMN tenant_id_tsid TO tenant_id;
                ALTER TABLE iam_tenant_invitations ALTER COLUMN id SET NOT NULL;
                ALTER TABLE iam_tenant_invitations ALTER COLUMN tenant_id SET NOT NULL;

                ALTER TABLE iam_audit_events DROP COLUMN id;
                ALTER TABLE iam_audit_events DROP COLUMN tenant_id;
                ALTER TABLE iam_audit_events DROP COLUMN actor_account_id;
                ALTER TABLE iam_audit_events RENAME COLUMN id_tsid TO id;
                ALTER TABLE iam_audit_events RENAME COLUMN tenant_id_tsid TO tenant_id;
                ALTER TABLE iam_audit_events RENAME COLUMN actor_account_id_tsid TO actor_account_id;
                ALTER TABLE iam_audit_events ALTER COLUMN id SET NOT NULL;
                ALTER TABLE iam_audit_events ALTER COLUMN tenant_id SET NOT NULL;
                ALTER TABLE iam_audit_events ALTER COLUMN actor_account_id SET NOT NULL;

                ALTER TABLE iam_accounts ADD CONSTRAINT "PK_iam_accounts" PRIMARY KEY (id);
                ALTER TABLE iam_tenants ADD CONSTRAINT "PK_iam_tenants" PRIMARY KEY (id);
                ALTER TABLE iam_tenant_memberships ADD CONSTRAINT "PK_iam_tenant_memberships" PRIMARY KEY (id);
                ALTER TABLE iam_tenant_roles ADD CONSTRAINT "PK_iam_tenant_roles" PRIMARY KEY (id);
                ALTER TABLE iam_tenant_member_role_assignments ADD CONSTRAINT "PK_iam_tenant_member_role_assignments" PRIMARY KEY (tenant_membership_id, tenant_role_id);
                ALTER TABLE iam_tenant_invitations ADD CONSTRAINT "PK_iam_tenant_invitations" PRIMARY KEY (id);
                ALTER TABLE iam_audit_events ADD CONSTRAINT "PK_iam_audit_events" PRIMARY KEY (id);

                CREATE INDEX "IX_iam_audit_events_actor_account_id" ON iam_audit_events (actor_account_id);
                CREATE INDEX ix_iam_audit_events_tenant_action ON iam_audit_events (tenant_id, action);
                CREATE INDEX ix_iam_audit_events_tenant_created ON iam_audit_events (tenant_id, created_at_utc);
                CREATE INDEX ix_iam_tenant_invitations_active_email ON iam_tenant_invitations (tenant_id, normalized_email, status);
                CREATE INDEX "IX_iam_tenant_member_role_assignments_tenant_role_id" ON iam_tenant_member_role_assignments (tenant_role_id);
                CREATE UNIQUE INDEX ix_iam_tenant_roles_tenant_normalized_name ON iam_tenant_roles (tenant_id, normalized_name);
                CREATE INDEX ix_iam_tenant_memberships_account_id ON iam_tenant_memberships (account_id);
                CREATE UNIQUE INDEX ix_iam_tenant_memberships_tenant_account ON iam_tenant_memberships (tenant_id, account_id);

                ALTER TABLE iam_tenant_memberships ADD CONSTRAINT "FK_iam_tenant_memberships_iam_accounts_account_id" FOREIGN KEY (account_id) REFERENCES iam_accounts (id) ON DELETE RESTRICT;
                ALTER TABLE iam_tenant_memberships ADD CONSTRAINT "FK_iam_tenant_memberships_iam_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES iam_tenants (id) ON DELETE CASCADE;
                ALTER TABLE iam_tenant_roles ADD CONSTRAINT "FK_iam_tenant_roles_iam_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES iam_tenants (id) ON DELETE CASCADE;
                ALTER TABLE iam_tenant_member_role_assignments ADD CONSTRAINT "FK_iam_tenant_member_role_assignments_iam_tenant_memberships_t~" FOREIGN KEY (tenant_membership_id) REFERENCES iam_tenant_memberships (id) ON DELETE CASCADE;
                ALTER TABLE iam_tenant_member_role_assignments ADD CONSTRAINT "FK_iam_tenant_member_role_assignments_iam_tenant_roles_tenant_~" FOREIGN KEY (tenant_role_id) REFERENCES iam_tenant_roles (id) ON DELETE CASCADE;
                ALTER TABLE iam_tenant_invitations ADD CONSTRAINT "FK_iam_tenant_invitations_iam_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES iam_tenants (id) ON DELETE CASCADE;
                ALTER TABLE iam_audit_events ADD CONSTRAINT "FK_iam_audit_events_iam_accounts_actor_account_id" FOREIGN KEY (actor_account_id) REFERENCES iam_accounts (id) ON DELETE RESTRICT;
                ALTER TABLE iam_audit_events ADD CONSTRAINT "FK_iam_audit_events_iam_tenants_tenant_id" FOREIGN KEY (tenant_id) REFERENCES iam_tenants (id) ON DELETE CASCADE;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("The B017 IAM TSID identifier migration is irreversible because the original UUID identity values are removed after the bigint TSID values replace them. Restore from backup if a rollback before this migration is required.");
        }
    }
}
