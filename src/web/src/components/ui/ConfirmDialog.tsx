import { AlertDialog } from '@base-ui/react/alert-dialog'
import { TriangleAlert } from 'lucide-react'
import type { ReactNode } from 'react'
import { Button, SecondaryButton } from './Button'
import { cn } from '@/lib/utils'

/**
 * F051: the one shared irreversible-action confirmation dialog. Built on
 * `@base-ui/react`'s `AlertDialog` (focus-trapped, `Escape`-to-close,
 * backdrop click closes, returns focus to the trigger on close) — no custom
 * dialog markup or focus management is written here.
 *
 * Fully controlled: the caller owns `open`/`onOpenChange` so it can gate the
 * confirm button on its own busy state and keep exactly one in-flight
 * mutation. Cancelling (Escape, backdrop, or the cancel button) closes the
 * dialog and fires no request — the caller never sees a confirm call.
 */
export function ConfirmDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  cancelLabel = 'انصراف',
  onConfirm,
  isConfirming = false,
  tone = 'default',
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  description: ReactNode
  confirmLabel: string
  cancelLabel?: string
  onConfirm: () => void
  isConfirming?: boolean
  tone?: 'default' | 'destructive'
}) {
  return (
    <AlertDialog.Root open={open} onOpenChange={(next) => !isConfirming && onOpenChange(next)}>
      <AlertDialog.Portal>
        <AlertDialog.Backdrop className="fixed inset-0 z-[60] bg-slate-950/50 transition-opacity duration-150 ease-admin data-[ending-style]:opacity-0 data-[starting-style]:opacity-0" />
        <AlertDialog.Popup className="fixed inset-0 z-[60] flex items-center justify-center p-4 outline-none data-[ending-style]:opacity-0 data-[starting-style]:opacity-0">
          <div className="w-full max-w-sm rounded-xl border border-border bg-surface p-5 shadow-raised">
            <div className="flex items-start gap-3">
              <span
                className={cn(
                  'inline-flex size-9 shrink-0 items-center justify-center rounded-lg',
                  tone === 'destructive' ? 'bg-destructive/15 text-destructive' : 'bg-warning/15 text-warning',
                )}
              >
                <TriangleAlert aria-hidden="true" className="size-5" />
              </span>
              <div className="space-y-1.5">
                <AlertDialog.Title className="text-base font-semibold">{title}</AlertDialog.Title>
                <AlertDialog.Description className="text-sm leading-6 text-muted-foreground">
                  {description}
                </AlertDialog.Description>
              </div>
            </div>
            <div className="mt-5 flex flex-wrap justify-end gap-2">
              <SecondaryButton type="button" onClick={() => onOpenChange(false)} disabled={isConfirming}>
                {cancelLabel}
              </SecondaryButton>
              <Button
                type="button"
                onClick={onConfirm}
                disabled={isConfirming}
                className={cn(tone === 'destructive' && 'bg-destructive hover:brightness-110')}
              >
                {isConfirming ? 'در حال ثبت…' : confirmLabel}
              </Button>
            </div>
          </div>
        </AlertDialog.Popup>
      </AlertDialog.Portal>
    </AlertDialog.Root>
  )
}
