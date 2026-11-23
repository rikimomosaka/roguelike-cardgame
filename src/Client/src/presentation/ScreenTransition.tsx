import type { ReactNode } from 'react'
import './ScreenTransition.css'

type Props = {
  transitionKey: string
  children: ReactNode
}

/**
 * Keeps exactly one game screen mounted while giving route changes a shared,
 * pixel-art reveal. The keyed content is replaced atomically; the veil is only
 * a presentation layer and never contains a duplicate interactive screen.
 */
export function ScreenTransition({ transitionKey, children }: Props) {
  return (
    <div className="screen-transition" data-screen={transitionKey}>
      <div key={transitionKey} className="screen-transition__content">
        {children}
      </div>
      <div
        key={`veil-${transitionKey}`}
        className="screen-transition__veil"
        aria-hidden="true"
      >
        <span className="screen-transition__veil-half screen-transition__veil-half--top" />
        <span className="screen-transition__veil-half screen-transition__veil-half--bottom" />
        <span className="screen-transition__sigil">◆</span>
      </div>
    </div>
  )
}
