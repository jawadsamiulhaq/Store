import type { ReactNode } from 'react'
import { Button } from '../../ui/primitives'

/*
  Shared admin table furniture.

  Admin screens are the same shape over and over — header, filter row, table, pager — so the shell
  is defined once. It keeps every admin screen consistent and stops nine pages each inventing their
  own idea of what a "no results" state looks like.
*/

export function PageHeader({
  title,
  description,
  action,
}: {
  title: string
  description?: string
  action?: ReactNode
}) {
  return (
    <header className="mb-6 flex flex-wrap items-end justify-between gap-3">
      <div>
        <h1 className="text-xl font-bold tracking-tight text-ink-900 sm:text-2xl">{title}</h1>
        {description && <p className="mt-1 text-sm text-ink-500">{description}</p>}
      </div>
      {action}
    </header>
  )
}

/** Horizontal scroll container. Admin tables are wide; the page itself must not scroll sideways. */
export function TableShell({ children }: { children: ReactNode }) {
  return (
    <div className="card-surface overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full min-w-[42rem] text-sm">{children}</table>
      </div>
    </div>
  )
}

export function Th({ children, align = 'left' }: { children: ReactNode; align?: 'left' | 'right' | 'center' }) {
  return (
    <th
      scope="col"
      className={`whitespace-nowrap border-b border-ink-100 bg-paper-sunken px-4 py-2.5 text-xs font-semibold uppercase tracking-wide text-ink-500 text-${align}`}
    >
      {children}
    </th>
  )
}

export function Td({
  children,
  align = 'left',
  className = '',
}: {
  children: ReactNode
  align?: 'left' | 'right' | 'center'
  className?: string
}) {
  return <td className={`border-b border-ink-50 px-4 py-3 text-${align} ${className}`}>{children}</td>
}

/** Table-shaped loading state — same row count and height, so the swap does not jump. */
export function TableSkeleton({ rows = 8, columns = 5 }: { rows?: number; columns?: number }) {
  return (
    <TableShell>
      <tbody>
        {Array.from({ length: rows }, (_, rowIndex) => (
          <tr key={rowIndex}>
            {Array.from({ length: columns }, (_, columnIndex) => (
              <td key={columnIndex} className="border-b border-ink-50 px-4 py-3">
                <div className="skeleton h-4 w-full" />
              </td>
            ))}
          </tr>
        ))}
      </tbody>
    </TableShell>
  )
}

export function TableEmpty({ colSpan, message }: { colSpan: number; message: string }) {
  return (
    <tr>
      <td colSpan={colSpan} className="px-4 py-16 text-center text-sm text-ink-400">
        {message}
      </td>
    </tr>
  )
}

export function Pager({
  page,
  totalPages,
  totalCount,
  onChange,
}: {
  page: number
  totalPages: number
  totalCount: number
  onChange: (page: number) => void
}) {
  if (totalPages <= 1) {
    return (
      <p className="mt-3 text-xs text-ink-400">
        {totalCount.toLocaleString('en-HK')} result{totalCount === 1 ? '' : 's'}
      </p>
    )
  }

  return (
    <div className="mt-4 flex items-center justify-between gap-3">
      <p className="text-xs text-ink-400">
        {totalCount.toLocaleString('en-HK')} results · page {page} of {totalPages}
      </p>

      <div className="flex gap-2">
        <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => onChange(page - 1)}>
          Previous
        </Button>
        <Button variant="outline" size="sm" disabled={page >= totalPages} onClick={() => onChange(page + 1)}>
          Next
        </Button>
      </div>
    </div>
  )
}

/** Filter bar. Wraps rather than scrolls, so no control is ever hidden off-screen on mobile. */
export function FilterBar({ children }: { children: ReactNode }) {
  return <div className="mb-4 flex flex-wrap items-center gap-2">{children}</div>
}
