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
            You are signed in as {session?.user.displayName}. This dashboard intentionally contains only orientation content so S01 proves navigation, theme, layout and session refresh against the real login API without borrowing future roadmap work.
          </p>
        </div>

        <div className="grid gap-4 md:grid-cols-3">
          <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
            <p className="text-sm font-semibold">Current slice</p>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">Login through the development API, route protection and responsive shell.</p>
          </div>
          <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
            <p className="text-sm font-semibold">Session behavior</p>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">The signed token stays in this browser tab. On refresh the API re-verifies it before the shell is restored; invalid or expired tokens return you to sign-in.</p>
          </div>
          <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
            <p className="text-sm font-semibold">Next slice</p>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">The Persian RTL interface and the collapsible RTL sidebar refine this authenticated shell.</p>
          </div>
        </div>
      </section>
    </DashboardShell>
  )
}
