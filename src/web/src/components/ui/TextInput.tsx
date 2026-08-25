import type { InputHTMLAttributes } from 'react'
import { cn } from '@/lib/utils'

export function TextInput({ className, ...props }: InputHTMLAttributes<HTMLInputElement>) {
  return (
    <input
      className={cn(
        'min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground shadow-none transition-colors placeholder:text-muted-foreground focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60 md:text-sm',
        className,
      )}
      {...props}
    />
  )
}
