import { DashboardShell } from '@/components/shell/DashboardShell'
import { useAuth } from '@/features/auth/AuthContext'

export function DashboardPage() {
  const { session } = useAuth()

  return (
    <DashboardShell>
      <section id="welcome" className="space-y-8">
        <div className="max-w-3xl">
          <p className="text-sm font-semibold uppercase tracking-[0.18em] text-primary">Welcome shell</p>
          <h2 className="mt-3 text-3xl font-semibold tracking-[-0.035em] md:text-5xl">
            TenantForge is ready for its first authenticated slice.
          </h2>
          <p className="mt-5 text-base leading-7 text-muted-foreground md:text-lg">
            You are signed in as {session?.user.displayName}. This dashboard intentionally contains only orientation content so S00 proves navigation, theme, layout and session refresh without borrowing future roadmap work.
          </p>
        </div>

        <div className="grid gap-4 md:grid-cols-3">
          <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
            <p className="text-sm font-semibold">Current slice</p>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">Frontend-only mock authentication, route protection and responsive shell.</p>
          </div>
          <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
            <p className="text-sm font-semibold">Session behavior</p>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">The S00 mock session is stored in sessionStorage and survives browser refresh until sign out or tab close.</p>
          </div>
          <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
            <p className="text-sm font-semibold">Next replacement point</p>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">S01 can swap the isolated adapter for the real login API without redesigning this screen.</p>
          </div>
        </div>
      </section>
    </DashboardShell>
  )
}
