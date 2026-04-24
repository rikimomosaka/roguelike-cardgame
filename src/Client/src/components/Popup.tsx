import { useEffect } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import './Popup.css'

export type PopupVariant = 'modal' | 'picker' | 'confirm'

type Props = {
  open: boolean
  onClose?: () => void
  title?: ReactNode
  subtitle?: ReactNode
  headRight?: ReactNode
  footer?: ReactNode
  width?: number | string
  variant?: PopupVariant
  closeOnEsc?: boolean
  closeOnBackdrop?: boolean
  children: ReactNode
}

export function Popup({
  open,
  onClose,
  title,
  subtitle,
  headRight,
  footer,
  width = 620,
  variant = 'modal',
  closeOnEsc = true,
  closeOnBackdrop = false,
  children,
}: Props) {
  useEffect(() => {
    if (!open || !closeOnEsc || !onClose) return
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation()
        onClose()
      }
    }
    window.addEventListener('keydown', handler)
    return () => window.removeEventListener('keydown', handler)
  }, [open, closeOnEsc, onClose])

  if (!open) return null

  const panelStyle: CSSProperties = {
    width: typeof width === 'number' ? `min(${width}px, 82%)` : width,
  }

  return (
    <div
      className={`popup popup--${variant}`}
      role="dialog"
      aria-modal="true"
      aria-label={typeof title === 'string' ? title : undefined}
    >
      <div
        className="popup__dim"
        onClick={closeOnBackdrop && onClose ? onClose : undefined}
      />
      <div
        className={`popup__panel popup__panel--${variant}`}
        style={panelStyle}
        onClick={(e) => e.stopPropagation()}
      >
        {(title || subtitle || headRight) && (
          <header className="popup__head">
            <div className="popup__head-main">
              {title ? <div className="popup__title">{title}</div> : null}
              {subtitle ? <div className="popup__subtitle">{subtitle}</div> : null}
            </div>
            {headRight ? <div className="popup__head-right">{headRight}</div> : null}
          </header>
        )}
        <div className="popup__body">{children}</div>
        {footer ? <footer className="popup__foot">{footer}</footer> : null}
      </div>
    </div>
  )
}
