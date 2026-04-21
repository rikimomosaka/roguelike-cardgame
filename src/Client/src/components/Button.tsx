import type { ButtonHTMLAttributes, ReactNode } from 'react'

type Props = ButtonHTMLAttributes<HTMLButtonElement> & {
  children: ReactNode
  variant?: 'primary' | 'secondary' | 'danger'
}

export function Button({ children, variant = 'primary', ...rest }: Props) {
  return (
    <button className={`btn btn--${variant}`} {...rest}>
      {children}
    </button>
  )
}
