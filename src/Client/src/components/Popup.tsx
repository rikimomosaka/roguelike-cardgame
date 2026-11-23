import { useEffect, useId, useLayoutEffect, useRef, useState } from 'react'
import type { CSSProperties, PointerEvent as ReactPointerEvent, ReactNode } from 'react'
import { usePresentationSettings } from '../presentation'
import './Popup.css'

export type PopupVariant = 'modal' | 'picker' | 'confirm'
export type PopupFooterAlign = 'start' | 'center' | 'end'

type Props = {
  open: boolean
  onClose?: () => void
  title?: ReactNode
  subtitle?: ReactNode
  headRight?: ReactNode
  footer?: ReactNode
  footerAlign?: PopupFooterAlign
  width?: number | string
  panelClassName?: string
  popupClassName?: string
  draggable?: boolean
  recenterAfterMs?: number
  variant?: PopupVariant
  closeOnEsc?: boolean
  closeOnBackdrop?: boolean
  children: ReactNode
}

type PresenceState = 'closed' | 'entering' | 'open' | 'exiting'

const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not(:disabled)',
  'input:not(:disabled)',
  'select:not(:disabled)',
  'textarea:not(:disabled)',
  '[tabindex]:not([tabindex="-1"])',
  '[contenteditable="true"]',
].join(',')

function getFocusableElements(panel: HTMLElement): HTMLElement[] {
  return Array.from(panel.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR))
    .filter(element => !element.hidden && element.getAttribute('aria-hidden') !== 'true')
}

function isTopmostDialog(dialog: HTMLElement): boolean {
  const openDialogs = Array.from(document.querySelectorAll<HTMLElement>(
    '[role="dialog"][aria-modal="true"]:not([aria-hidden="true"])',
  ))
  return openDialogs.at(-1) === dialog
}

export function Popup({
  open,
  onClose,
  title,
  subtitle,
  headRight,
  footer,
  footerAlign = 'end',
  width = 620,
  panelClassName,
  popupClassName,
  draggable = false,
  recenterAfterMs = 10_000,
  variant = 'modal',
  closeOnEsc = true,
  closeOnBackdrop = false,
  children,
}: Props) {
  const { effectiveMotionLevel } = usePresentationSettings()
  const dialogRef = useRef<HTMLDivElement | null>(null)
  const panelRef = useRef<HTMLDivElement | null>(null)
  const previouslyFocusedRef = useRef<HTMLElement | null>(null)
  const dragRef = useRef<{ pointerId: number; x: number; y: number; startX: number; startY: number } | null>(null)
  const [offset, setOffset] = useState({ x: 0, y: 0 })
  const [presence, setPresence] = useState<PresenceState>(open ? 'entering' : 'closed')
  const [lastOpen, setLastOpen] = useState(open)
  const titleId = useId()

  if (open !== lastOpen) {
    setLastOpen(open)
    setPresence(open ? 'entering' : 'exiting')
  }

  useEffect(() => {
    const duration = effectiveMotionLevel === 'off'
      ? 0
      : effectiveMotionLevel === 'reduced'
        ? 90
        : 180

    if (presence === 'entering') {
      const timer = window.setTimeout(() => setPresence('open'), duration)
      return () => window.clearTimeout(timer)
    }
    if (presence === 'exiting') {
      const timer = window.setTimeout(() => setPresence('closed'), duration)
      return () => window.clearTimeout(timer)
    }
  }, [effectiveMotionLevel, presence])

  useEffect(() => {
    if (!open || !draggable || (offset.x === 0 && offset.y === 0)) return
    const timer = window.setTimeout(() => setOffset({ x: 0, y: 0 }), recenterAfterMs)
    return () => window.clearTimeout(timer)
  }, [open, draggable, recenterAfterMs, offset.x, offset.y])

  useEffect(() => {
    if (!open || !draggable) return
    const move = (event: PointerEvent) => {
      const drag = dragRef.current
      if (!drag || event.pointerId !== drag.pointerId) return
      const stage = panelRef.current?.closest('.app-stage') as HTMLElement | null
      const stageRect = stage?.getBoundingClientRect()
      const scale = stageRect && stageRect.width > 0 ? stageRect.width / 1280 : 1
      const panel = panelRef.current
      const maxX = panel ? Math.max(0, (1280 - panel.offsetWidth) / 2) : 320
      const maxY = panel ? Math.max(0, (720 - panel.offsetHeight) / 2) : 240
      setOffset({
        x: Math.max(-maxX, Math.min(maxX, drag.x + (event.clientX - drag.startX) / scale)),
        y: Math.max(-maxY, Math.min(maxY, drag.y + (event.clientY - drag.startY) / scale)),
      })
    }
    const end = (event: PointerEvent) => {
      if (dragRef.current?.pointerId === event.pointerId) dragRef.current = null
    }
    window.addEventListener('pointermove', move)
    window.addEventListener('pointerup', end)
    window.addEventListener('pointercancel', end)
    return () => {
      window.removeEventListener('pointermove', move)
      window.removeEventListener('pointerup', end)
      window.removeEventListener('pointercancel', end)
    }
  }, [open, draggable])

  const beginDrag = (event: ReactPointerEvent<HTMLElement>) => {
    if (!draggable || event.button !== 0) return
    if ((event.target as Element).closest('button, a, input, select, textarea')) return
    dragRef.current = {
      pointerId: event.pointerId,
      x: offset.x,
      y: offset.y,
      startX: event.clientX,
      startY: event.clientY,
    }
    event.currentTarget.setPointerCapture?.(event.pointerId)
    event.preventDefault()
  }

  useLayoutEffect(() => {
    if (!open) return
    previouslyFocusedRef.current = document.activeElement instanceof HTMLElement
      ? document.activeElement
      : null

    const focusTimer = window.setTimeout(() => {
      const panel = panelRef.current
      if (!panel || !isTopmostDialog(dialogRef.current ?? panel)) return
      const preferred = panel.querySelector<HTMLElement>('[autofocus], [data-autofocus]')
      preferred?.focus()
      if (!preferred) (getFocusableElements(panel)[0] ?? panel).focus()
    }, 0)

    return () => {
      window.clearTimeout(focusTimer)
    }
  }, [open])

  useEffect(() => {
    if (open) return
    const previous = previouslyFocusedRef.current
    if (previous?.isConnected) previous.focus()
    previouslyFocusedRef.current = null
  }, [open])

  useEffect(() => () => {
    const previous = previouslyFocusedRef.current
    if (previous?.isConnected) previous.focus()
    previouslyFocusedRef.current = null
  }, [])

  useEffect(() => {
    if (!open) return
    const handleKeyDown = (event: KeyboardEvent) => {
      const dialog = dialogRef.current
      const panel = panelRef.current
      if (!dialog || !panel || !isTopmostDialog(dialog)) return

      if (event.key === 'Escape' && closeOnEsc && onClose) {
        event.preventDefault()
        event.stopPropagation()
        onClose()
        return
      }

      if (event.key !== 'Tab') return
      const focusable = getFocusableElements(panel)
      if (focusable.length === 0) {
        event.preventDefault()
        panel.focus()
        return
      }

      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      const active = document.activeElement
      if (event.shiftKey && (active === first || !panel.contains(active))) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && active === last) {
        event.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', handleKeyDown, true)
    return () => document.removeEventListener('keydown', handleKeyDown, true)
  }, [closeOnEsc, onClose, open])

  if (presence === 'closed') return null

  const panelStyle: CSSProperties = {
    width: typeof width === 'number' ? `min(${width}px, 82%)` : width,
    '--popup-offset-x': `${offset.x}px`,
    '--popup-offset-y': `${offset.y}px`,
  } as CSSProperties
  const exiting = presence === 'exiting' || !open

  return (
    <div
      ref={dialogRef}
      className={`popup popup--${variant}${popupClassName ? ` ${popupClassName}` : ''}`}
      role="dialog"
      aria-modal="true"
      aria-labelledby={title ? titleId : undefined}
      aria-hidden={exiting ? 'true' : undefined}
      data-state={presence}
    >
      <div
        className="popup__dim"
        onClick={open && closeOnBackdrop && onClose ? onClose : undefined}
      />
      <div
        ref={panelRef}
        className={`popup__panel popup__panel--${variant}${panelClassName ? ` ${panelClassName}` : ''}`}
        style={panelStyle}
        tabIndex={-1}
        onClick={(event) => event.stopPropagation()}
      >
        {(title || subtitle || headRight) && (
          <header
            className={`popup__head${draggable ? ' popup__head--draggable' : ''}`}
            onPointerDown={draggable ? beginDrag : undefined}
          >
            <div className="popup__head-main">
              {title ? <div id={titleId} className="popup__title">{title}</div> : null}
              {subtitle ? <div className="popup__subtitle">{subtitle}</div> : null}
            </div>
            {headRight ? <div className="popup__head-right">{headRight}</div> : null}
          </header>
        )}
        <div className="popup__body">{children}</div>
        {footer ? (
          <footer className={`popup__foot popup__foot--${footerAlign}`}>
            {footer}
          </footer>
        ) : null}
      </div>
    </div>
  )
}
