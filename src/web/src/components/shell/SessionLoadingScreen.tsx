import { Loader2, ShieldCheck } from 'lucide-react'

/**
 * S02: shown while a stored session token is being verified against
 * `GET /api/auth/me`. It renders before any protected route content so a
 * direct navigation to `/dashboard` never flashes dashboard data behind a
 * dead or slow token check.
 */
export function SessionLoadingScreen() {
  return (
    <main
      className="grid min-h-screen place-items-center bg-background px-6 text-foreground"
      aria-busy="true"
    >
      <div className="flex flex-col items-center gap-5 text-center">
        <span className="inline-flex size-12 items-center justify-center rounded-lg bg-primary text-primary-foreground shadow-soft">
          <ShieldCheck aria-hidden="true" className="size-6" />
        </span>
        <div role="status">
          <p className="flex items-center gap-2.5 text-sm font-medium">
            <Loader2 aria-hidden="true" className="size-4 animate-spin motion-reduce:animate-none" />
            در حال بازیابی نشست شما
            <span className="sr-only">…</span>
          </p>
          <p className="mt-2 text-sm text-muted-foreground">
            ورود شما به TenantForge در حال تأیید است.
          </p>
        </div>
      </div>
    </main>
  )
}
