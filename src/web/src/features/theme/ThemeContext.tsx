import type { ReactNode } from 'react'
import { createContext, useContext, useEffect, useMemo, useState } from 'react'

type Theme = 'light' | 'dark'

type ThemeContextValue = {
  theme: Theme
  toggleTheme(): void
}

const THEME_KEY = 'tenantforge:theme'
const ThemeContext = createContext<ThemeContextValue | null>(null)

function getInitialTheme(): Theme {
  const storedTheme = window.localStorage.getItem(THEME_KEY)
  if (storedTheme === 'light' || storedTheme === 'dark') return storedTheme
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setTheme] = useState<Theme>(getInitialTheme)

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark')
    document.documentElement.dataset.theme = theme
    window.localStorage.setItem(THEME_KEY, theme)
  }, [theme])

  const value = useMemo<ThemeContextValue>(
    () => ({
      theme,
      toggleTheme() {
        setTheme((currentTheme) => (currentTheme === 'dark' ? 'light' : 'dark'))
      },
    }),
    [theme],
  )

  return <ThemeContext value={value}>{children}</ThemeContext>
}

export function useTheme() {
  const context = useContext(ThemeContext)
  if (!context) {
    throw new Error('useTheme must be used inside ThemeProvider.')
  }
  return context
}
