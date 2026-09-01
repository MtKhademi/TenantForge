import { zodResolver } from '@hookform/resolvers/zod'
import { Loader2, LockKeyhole, Moon, ShieldCheck, Sun, TriangleAlert } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Navigate, useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { SessionLoadingScreen } from '@/components/shell/SessionLoadingScreen'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { useAuth } from '@/features/auth/AuthContext'
import {
  ApiUnavailableError,
  InvalidCredentialsError,
} from '@/features/auth/authTypes'
import { developmentAdministratorCredentials } from '@/features/auth/httpAuthAdapter'
import { useTheme } from '@/features/theme/ThemeContext'

const loginSchema = z.object({
  email: z.string().min(1, 'ایمیل الزامی است.').email('یک ایمیل معتبر وارد کنید.'),
  password: z.string().min(1, 'رمز عبور الزامی است.'),
})

type LoginFormValues = z.infer<typeof loginSchema>

export function LoginPage() {
  const { status, sessionNotice, login } = useAuth()
  const { theme, toggleTheme } = useTheme()
  const navigate = useNavigate()
  const [credentialError, setCredentialError] = useState<string | null>(null)

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: {
      email: '',
      password: '',
    },
  })

  // While a stored session is being verified on this tab, do not show the
  // sign-in form — the shell is either about to open or the redirect below
  // will take the user there. After verification, land on the dashboard.
  if (status === 'authenticated') return <Navigate to="/dashboard" replace />
  if (status === 'bootstrapping') return <SessionLoadingScreen />

  async function onSubmit(values: LoginFormValues) {
    setCredentialError(null)
    try {
      await login(values)
      navigate('/dashboard', { replace: true })
    } catch (error) {
      if (error instanceof InvalidCredentialsError) {
        setCredentialError('ایمیل یا رمز عبور درست نیست. دوباره تلاش کنید.')
        return
      }
      setCredentialError(
        error instanceof ApiUnavailableError
          ? 'سرویس ورود در دسترس نیست. اتصال را بررسی کنید و دوباره تلاش کنید.'
          : 'هنگام ورود مشکلی پیش آمد. دوباره تلاش کنید.',
      )
    }
  }

  const sessionNoticeText =
    sessionNotice === 'expired'
      ? 'نشست شما منقضی شده است. دوباره وارد شوید.'
      : sessionNotice === 'unverified'
        ? 'اکنون نمی‌توانیم نشست شما را تأیید کنیم. برای ادامه دوباره وارد شوید.'
        : null

  return (
    <main className="grid min-h-screen bg-background text-foreground lg:grid-cols-[minmax(0,1fr)_minmax(31rem,0.78fr)]">
      <section className="relative flex min-h-[42vh] items-center overflow-hidden border-b border-border bg-sidebar px-6 py-10 lg:min-h-screen lg:border-b-0 lg:border-e lg:px-10">
        <div className="absolute inset-0 bg-[linear-gradient(135deg,transparent_0%,transparent_55%,color-mix(in_oklch,var(--primary)_13%,transparent)_55%,transparent_82%)]" />
        <div className="relative mx-auto w-full max-w-2xl">
          <div className="mb-10 flex items-center gap-3">
            <span className="inline-flex size-11 items-center justify-center rounded-lg bg-primary text-primary-foreground shadow-soft">
              <ShieldCheck aria-hidden="true" className="size-5" />
            </span>
            <div>
              <p className="text-sm font-semibold tracking-[0.08em] text-muted-foreground">TenantForge</p>
              <p className="text-sm text-muted-foreground">مدیریت هویت چندمستاجری، شفاف و قابل فهم</p>
            </div>
          </div>
          <p className="mb-4 text-sm font-semibold tracking-[0.08em] text-primary">برش S01 — ورود توسعه</p>
          <h1 className="max-w-xl text-4xl font-semibold tracking-[-0.02em] text-foreground md:text-6xl">
            سطح مدیریت آرام از همان نخستین ورود.
          </h1>
          <p className="mt-6 max-w-xl text-base leading-8 text-muted-foreground md:text-lg">
            با حساب مدیر توسعه وارد شوید. درخواست شما مستقیم به API TenantForge می‌رود و توکن نشست امضاشده را دریافت می‌کند.
          </p>
        </div>
      </section>

      <section className="flex items-center justify-center px-5 py-10 md:px-8">
        <div className="w-full max-w-md">
          <div className="mb-8 flex items-center justify-between">
            <div>
              <h2 className="text-2xl font-semibold tracking-tight">ورود</h2>
              <p className="mt-2 text-sm text-muted-foreground">از حساب مدیر توسعه مستندشده استفاده کنید.</p>
            </div>
            <SecondaryButton
              type="button"
              className="px-3"
              aria-label={theme === 'dark' ? 'تغییر به پوسته روشن' : 'تغییر به پوسته تیره'}
              onClick={toggleTheme}
            >
              {theme === 'dark' ? <Sun aria-hidden="true" className="size-4" /> : <Moon aria-hidden="true" className="size-4" />}
            </SecondaryButton>
          </div>

          {sessionNoticeText && (
            <div className="mb-5 flex items-start gap-3 rounded-lg border border-warning/50 bg-warning/10 p-3 text-sm text-foreground" role="alert">
              <TriangleAlert aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-warning" />
              <p>{sessionNoticeText}</p>
            </div>
          )}

          <form className="rounded-xl border border-border bg-surface p-5 shadow-soft" onSubmit={handleSubmit(onSubmit)} noValidate>
            <div className="space-y-5">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="email">ایمیل</label>
                <TextInput
                  id="email"
                  type="email"
                  autoComplete="username"
                  aria-invalid={Boolean(errors.email)}
                  aria-describedby={errors.email ? 'email-error' : undefined}
                  {...register('email')}
                />
                {errors.email && <p className="mt-2 text-sm text-destructive" id="email-error">{errors.email.message}</p>}
              </div>

              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="password">رمز عبور</label>
                <TextInput
                  id="password"
                  type="password"
                  autoComplete="current-password"
                  aria-invalid={Boolean(errors.password)}
                  aria-describedby={errors.password ? 'password-error' : undefined}
                  {...register('password')}
                />
                {errors.password && <p className="mt-2 text-sm text-destructive" id="password-error">{errors.password.message}</p>}
              </div>

              {credentialError && (
                <div className="rounded-lg border border-destructive/40 bg-destructive/10 p-3 text-sm text-destructive" role="alert">
                  {credentialError}
                </div>
              )}

              <Button type="submit" className="w-full" disabled={isSubmitting}>
                {isSubmitting ? (
                  <>
                    <Loader2 aria-hidden="true" className="me-2 size-4 animate-spin" />
                    در حال ورود
                  </>
                ) : (
                  <>
                    <LockKeyhole aria-hidden="true" className="me-2 size-4" />
                    ورود
                  </>
                )}
              </Button>
            </div>
          </form>

          <div className="mt-5 rounded-lg border border-border bg-surface-elevated p-4 text-sm text-muted-foreground">
            <p className="font-semibold text-foreground">مدیر توسعه مستندشده</p>
            <p className="mt-2"><span className="font-medium text-foreground">ایمیل:</span> <bdi>{developmentAdministratorCredentials.email}</bdi></p>
            <p><span className="font-medium text-foreground">رمز عبور:</span> <bdi>{developmentAdministratorCredentials.password}</bdi></p>
            <p className="mt-2">نشست و توکن امضاشده آن در همین زبانه مرورگر (sessionStorage) می‌ماند تا رفتار بازیابی پوسته با تازه‌سازی صفحه قابل بررسی باشد.</p>
          </div>
        </div>
      </section>
    </main>
  )
}
