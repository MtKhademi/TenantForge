import type { InputHTMLAttributes, Ref } from 'react'
import { cn } from '@/lib/utils'

type TextInputProps = InputHTMLAttributes<HTMLInputElement> & {
  ref?: Ref<HTMLInputElement>
}

export function TextInput({ className, ref, ...props }: TextInputProps) {
  return (
    <input
      ref={ref}
      className={cn(
        'min-h-11 w-full rounded-md border border-input bg-surface px-3 py-2 text-base text-foreground shadow-none transition-colors placeholder:text-muted-foreground focus-visible:border-ring disabled:cursor-not-allowed disabled:opacity-60 md:text-sm',
        className,
      )}
      {...props}
    />
  )
}
