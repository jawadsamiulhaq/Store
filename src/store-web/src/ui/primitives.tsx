import type { ButtonHTMLAttributes, InputHTMLAttributes, ReactNode, SelectHTMLAttributes } from 'react'
import { Link } from 'react-router-dom'

/*
  Shared primitives.

  Every interactive element here has a visible focus state, a disabled state, and a minimum
  44px touch target on the sizes used in mobile flows — the three things most commonly skipped
  and most commonly needed.
*/

type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'outline'
type ButtonSize = 'sm' | 'md' | 'lg'

const buttonBase =
  'inline-flex items-center justify-center gap-2 rounded-lg font-medium ' +
  'transition-[background-color,border-color,color,opacity,transform] duration-150 ' +
  // Scale on press only. Composited, and small enough not to read as a bounce.
  'active:scale-[0.98] ' +
  'disabled:pointer-events-none disabled:opacity-50 ' +
  'focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-saffron-500'

const buttonVariants: Record<ButtonVariant, string> = {
  primary: 'bg-saffron-500 text-white hover:bg-saffron-600 shadow-sm',
  secondary: 'bg-ink-800 text-paper hover:bg-ink-900',
  outline: 'border border-ink-200 bg-paper-raised text-ink-800 hover:border-ink-300 hover:bg-ink-50',
  ghost: 'text-ink-700 hover:bg-ink-100',
  danger: 'bg-chilli-500 text-white hover:bg-chilli-600',
}

const buttonSizes: Record<ButtonSize, string> = {
  // min-h keeps the tap target comfortable even when the label is short.
  sm: 'h-9 min-h-9 px-3 text-sm',
  md: 'h-11 min-h-11 px-5 text-sm',
  lg: 'h-12 min-h-12 px-6 text-base',
}

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  loading?: boolean
  fullWidth?: boolean
}

export function Button({
  variant = 'primary',
  size = 'md',
  loading = false,
  fullWidth = false,
  className = '',
  children,
  disabled,
  ...rest
}: ButtonProps) {
  return (
    <button
      {...rest}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={`${buttonBase} ${buttonVariants[variant]} ${buttonSizes[size]} ${
        fullWidth ? 'w-full' : ''
      } ${className}`}
    >
      {loading && <Spinner />}
      {children}
    </button>
  )
}

/** Link styled as a button. Kept separate so navigation stays a real anchor. */
export function ButtonLink({
  to,
  variant = 'primary',
  size = 'md',
  fullWidth = false,
  className = '',
  children,
}: {
  to: string
  variant?: ButtonVariant
  size?: ButtonSize
  fullWidth?: boolean
  className?: string
  children: ReactNode
}) {
  return (
    <Link
      to={to}
      className={`${buttonBase} ${buttonVariants[variant]} ${buttonSizes[size]} ${
        fullWidth ? 'w-full' : ''
      } ${className}`}
    >
      {children}
    </Link>
  )
}

export function Spinner({ className = 'h-4 w-4' }: { className?: string }) {
  return (
    <svg className={`animate-spin ${className}`} viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle className="opacity-20" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="3" />
      <path
        className="opacity-90"
        fill="currentColor"
        d="M12 2a10 10 0 0 1 10 10h-3a7 7 0 0 0-7-7V2Z"
      />
    </svg>
  )
}

// ---- Form controls ----------------------------------------------------------------------------

interface FieldProps {
  label: string
  htmlFor?: string
  error?: string
  hint?: string
  required?: boolean
  children: ReactNode
}

export function Field({ label, htmlFor, error, hint, required, children }: FieldProps) {
  return (
    <div className="space-y-1.5">
      <label htmlFor={htmlFor} className="block text-sm font-medium text-ink-700">
        {label}
        {required && <span className="ml-0.5 text-chilli-500">*</span>}
      </label>

      {children}

      {/*
        The hint/error slot always occupies a line, so validating a field does not push the rest
        of the form down — a small but very visible source of layout shift.
      */}
      <p className={`min-h-4 text-xs ${error ? 'text-chilli-600' : 'text-ink-400'}`}>
        {error ?? hint ?? ''}
      </p>
    </div>
  )
}

const controlBase =
  'w-full rounded-lg border bg-paper-raised px-3 text-sm text-ink-800 ' +
  'placeholder:text-ink-300 transition-colors duration-150 ' +
  'focus:border-saffron-400 focus:outline-2 focus:outline-offset-1 focus:outline-saffron-500 ' +
  'disabled:cursor-not-allowed disabled:bg-paper-sunken disabled:text-ink-400'

export function Input({
  invalid,
  className = '',
  ...rest
}: InputHTMLAttributes<HTMLInputElement> & { invalid?: boolean }) {
  return (
    <input
      {...rest}
      aria-invalid={invalid || undefined}
      className={`${controlBase} h-11 ${invalid ? 'border-chilli-400' : 'border-ink-200'} ${className}`}
    />
  )
}

export function Select({
  invalid,
  className = '',
  children,
  ...rest
}: SelectHTMLAttributes<HTMLSelectElement> & { invalid?: boolean }) {
  return (
    <select
      {...rest}
      aria-invalid={invalid || undefined}
      className={`${controlBase} h-11 ${invalid ? 'border-chilli-400' : 'border-ink-200'} ${className}`}
    >
      {children}
    </select>
  )
}

export function Textarea({
  invalid,
  className = '',
  ...rest
}: React.TextareaHTMLAttributes<HTMLTextAreaElement> & { invalid?: boolean }) {
  return (
    <textarea
      {...rest}
      aria-invalid={invalid || undefined}
      className={`${controlBase} py-2.5 ${invalid ? 'border-chilli-400' : 'border-ink-200'} ${className}`}
    />
  )
}

// ---- Feedback -----------------------------------------------------------------------------------

export function Badge({
  tone = 'neutral',
  children,
}: {
  tone?: 'neutral' | 'sale' | 'new' | 'success' | 'warning'
  children: ReactNode
}) {
  const tones = {
    neutral: 'bg-ink-100 text-ink-700',
    sale: 'bg-chilli-500 text-white',
    new: 'bg-saffron-500 text-white',
    success: 'bg-cardamom-100 text-cardamom-700',
    warning: 'bg-saffron-100 text-saffron-800',
  } as const

  return (
    <span
      className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-semibold ${tones[tone]}`}
    >
      {children}
    </span>
  )
}

export function Alert({
  tone = 'info',
  title,
  children,
}: {
  tone?: 'info' | 'error' | 'success' | 'warning'
  title?: string
  children: ReactNode
}) {
  const tones = {
    info: 'border-ink-200 bg-ink-50 text-ink-700',
    error: 'border-chilli-200 bg-chilli-50 text-chilli-700',
    success: 'border-cardamom-200 bg-cardamom-50 text-cardamom-700',
    warning: 'border-saffron-200 bg-saffron-50 text-saffron-800',
  } as const

  return (
    <div
      className={`rounded-lg border px-4 py-3 text-sm ${tones[tone]}`}
      role={tone === 'error' ? 'alert' : 'status'}
    >
      {title && <p className="mb-0.5 font-semibold">{title}</p>}
      {children}
    </div>
  )
}

export function EmptyState({
  title,
  description,
  action,
}: {
  title: string
  description?: string
  action?: ReactNode
}) {
  return (
    <div className="flex flex-col items-center justify-center rounded-xl border border-dashed border-ink-200 px-6 py-16 text-center">
      <h3 className="text-lg font-semibold text-ink-800">{title}</h3>
      {description && <p className="mt-1.5 max-w-sm text-sm text-ink-500">{description}</p>}
      {action && <div className="mt-6">{action}</div>}
    </div>
  )
}

/** Star rating. Renders a single accessible label rather than five, which screen readers spell out. */
export function Rating({
  value,
  count,
  size = 'sm',
}: {
  value: number
  count?: number
  size?: 'sm' | 'md'
}) {
  const dimension = size === 'sm' ? 'h-3.5 w-3.5' : 'h-4 w-4'

  return (
    <span className="inline-flex items-center gap-1" aria-label={`Rated ${value} out of 5`}>
      <span className="flex" aria-hidden="true">
        {[1, 2, 3, 4, 5].map((star) => (
          <svg
            key={star}
            className={`${dimension} ${star <= Math.round(value) ? 'text-saffron-400' : 'text-ink-200'}`}
            viewBox="0 0 20 20"
            fill="currentColor"
          >
            <path d="M10 1.6l2.47 5.01 5.53.8-4 3.9.94 5.5L10 14.2l-4.94 2.6.94-5.5-4-3.9 5.53-.8L10 1.6z" />
          </svg>
        ))}
      </span>
      {count !== undefined && (
        <span className="text-xs text-ink-400">({count})</span>
      )}
    </span>
  )
}
