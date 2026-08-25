import { zodResolver } from '@hookform/resolvers/zod'
import { Loader2, LockKeyhole, Moon, ShieldCheck, Sun } from 'lucide-react'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Navigate, useNavigate } from 'react-router-dom'
import { z } from 'zod'
import { Button, SecondaryButton } from '@/components/ui/Button'
import { TextInput } from '@/components/ui/TextInput'
import { useAuth } from '@/features/auth/AuthContext'
import { InvalidCredentialsError } from '@/features/auth/authTypes'
import { mockAdministratorCredentials } from '@/features/auth/mockAuthAdapter'
import { useTheme } from '@/features/theme/ThemeContext'

const loginSchema = z.object({
  email: z.string().min(1, 'Email is required.').email('Enter a valid email address.'),
  password: z.string().min(1, 'Password is required.'),
})

type LoginFormValues = z.infer<typeof loginSchema>

export function LoginPage() {
  const { session, login } = useAuth()
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

  if (session) return <Navigate to="/dashboard" replace />

  async function onSubmit(values: LoginFormValues) {
    setCredentialError(null)
    try {
      await login(values)
      navigate('/dashboard', { replace: true })
    } catch (error) {
      if (error instanceof InvalidCredentialsError) {
        setCredentialError('These mock credentials are not recognized. Check the documented development administrator and try again.')
        return
      }
      setCredentialError('The mock sign-in adapter could not complete the request. Try again.')
    }
  }

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
              <p className="text-sm font-semibold uppercase tracking-[0.18em] text-muted-foreground">TenantForge</p>
              <p className="text-sm text-muted-foreground">Understandable multi-tenant SaaS IAM</p>
            </div>
          </div>
          <p className="mb-4 text-sm font-semibold uppercase tracking-[0.18em] text-primary">Slice S00 foundation</p>
          <h1 className="max-w-xl text-4xl font-semibold tracking-[-0.04em] text-foreground md:text-6xl">
            A calm administration surface from the first login.
          </h1>
          <p className="mt-6 max-w-xl text-base leading-7 text-muted-foreground md:text-lg">
            Sign in with the temporary development administrator to inspect the responsive shell, theme control and mock session behavior that S01 will replace with a real API.
          </p>
        </div>
      </section>

      <section className="flex items-center justify-center px-5 py-10 md:px-8">
        <div className="w-full max-w-md">
          <div className="mb-8 flex items-center justify-between">
            <div>
              <h2 className="text-2xl font-semibold tracking-tight">Sign in</h2>
              <p className="mt-2 text-sm text-muted-foreground">Use the S00 development mock administrator.</p>
            </div>
            <SecondaryButton
              type="button"
              className="px-3"
              aria-label={`Switch to ${theme === 'dark' ? 'light' : 'dark'} theme`}
              onClick={toggleTheme}
            >
              {theme === 'dark' ? <Sun aria-hidden="true" className="size-4" /> : <Moon aria-hidden="true" className="size-4" />}
            </SecondaryButton>
          </div>

          <form className="rounded-xl border border-border bg-surface p-5 shadow-soft" onSubmit={handleSubmit(onSubmit)} noValidate>
            <div className="space-y-5">
              <div>
                <label className="mb-2 block text-sm font-semibold" htmlFor="email">Email</label>
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
                <label className="mb-2 block text-sm font-semibold" htmlFor="password">Password</label>
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
                    Checking mock credentials
                  </>
                ) : (
                  <>
                    <LockKeyhole aria-hidden="true" className="me-2 size-4" />
                    Open dashboard
                  </>
                )}
              </Button>
            </div>
          </form>

          <div className="mt-5 rounded-lg border border-border bg-surface-elevated p-4 text-sm text-muted-foreground">
            <p className="font-semibold text-foreground">Documented mock administrator</p>
            <p className="mt-2"><span className="font-medium text-foreground">Email:</span> {mockAdministratorCredentials.email}</p>
            <p><span className="font-medium text-foreground">Password:</span> {mockAdministratorCredentials.password}</p>
            <p className="mt-2">This S00 adapter stores only a temporary mock session in sessionStorage so refresh can prove shell behavior.</p>
          </div>
        </div>
      </section>
    </main>
  )
}
