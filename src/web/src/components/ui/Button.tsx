import type { ButtonHTMLAttributes } from 'react'
import { cn } from '@/lib/utils'

export function Button({ className, ...props }: ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      className={cn(
        'inline-flex min-h-10 items-center justify-center rounded-md bg-primary px-4 py-2 text-sm font-semibold text-primary-foreground shadow-soft transition-[background-color,box-shadow,transform] duration-150 ease-admin hover:brightness-105 disabled:pointer-events-none disabled:opacity-60',
        className,
      )}
      {...props}
    />
  )
}

export function SecondaryButton({ className, ...props }: ButtonHTMLAttributes<HTMLButtonElement>) {
  return (
    <button
      className={cn(
        'inline-flex min-h-10 items-center justify-center rounded-md border border-border bg-surface px-4 py-2 text-sm font-semibold text-foreground transition-colors duration-150 ease-admin hover:bg-muted disabled:pointer-events-none disabled:opacity-60',
        className,
      )}
      {...props}
    />
  )
}
