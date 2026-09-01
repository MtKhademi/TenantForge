import { DashboardShell } from '@/components/shell/DashboardShell'
import { useAuth } from '@/features/auth/AuthContext'

export function DashboardPage() {
  const { session } = useAuth()

  return (
    <DashboardShell>
      <section id="welcome" className="space-y-8">
        <div className="max-w-3xl">
          <p className="text-sm font-semibold text-primary">پوسته خوش‌آمدگویی</p>
          <h2 className="mt-3 text-3xl font-semibold tracking-[-0.02em] md:text-5xl">
            TenantForge برای نخستین برش احراز هویت‌شده آماده است.
          </h2>
          <p className="mt-5 text-base leading-8 text-muted-foreground md:text-lg">
            شما با <bdi>{session?.user.displayName}</bdi> وارد شده‌اید. این داشبورد عمداً فقط محتوای جهت‌یابی دارد تا برش S01 رفتار ناوبری، پوسته، چیدمان و بازیابی نشست را در برابر API ورود واقعی نشان دهد.
          </p>
        </div>

        <div className="grid gap-4 md:grid-cols-3">
          <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
            <p className="text-sm font-semibold">برش فعلی</p>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">ورود از API توسعه، محافظت از مسیرها و پوسته واکنش‌گرا.</p>
          </div>
          <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
            <p className="text-sm font-semibold">رفتار نشست</p>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">توکن امضاشده در همین زبانه مرورگر می‌ماند. هنگام تازه‌سازی، API پیش از بازگرداندن پوسته آن را دوباره تأیید می‌کند؛ توکن نامعتبر یا منقضی شما را به صفحه ورود برمی‌گرداند.</p>
          </div>
          <div className="rounded-xl border border-border bg-surface p-5 shadow-soft">
            <p className="text-sm font-semibold">برش بعدی</p>
            <p className="mt-2 text-sm leading-6 text-muted-foreground">رابط فارسی راست‌به‌چپ و نوار کناری جمع‌شونده RTL این پوسته احراز هویت‌شده را تکمیل می‌کند.</p>
          </div>
        </div>
      </section>
    </DashboardShell>
  )
}
