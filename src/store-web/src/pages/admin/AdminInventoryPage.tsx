import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, qs } from '../../lib/api'
import { Image } from '../../ui/Image'
import { Alert, Badge, Button, Input } from '../../ui/primitives'
import { FilterBar, PageHeader, Pager, TableEmpty, TableShell, TableSkeleton, Td, Th } from '../../features/admin/AdminTable'
import { useAuth } from '../../app/providers/AuthProvider'
import { formatDateTime, formatPrice } from '../../lib/format'
import type { Paged } from '../../lib/types'

interface StockRow {
  variantId: string
  productId: string
  productName: string
  productSlug: string
  variantName?: string
  sku: string
  thumbnailUrl?: string
  stockQuantity: number
  reservedQuantity: number
  availableQuantity: number
  lowStockThreshold: number
  trackInventory: boolean
  isLowStock: boolean
  isOutOfStock: boolean
  price: number
  costPrice?: number
  unit: string
}

interface Movement {
  id: string
  type: number
  quantityChange: number
  quantityAfter: number
  reference?: string
  note?: string
  performedByName?: string
  createdAt: string
}

const MOVEMENT_LABEL: Record<number, string> = {
  0: 'Opening balance',
  1: 'Purchase',
  2: 'Sale',
  3: 'Adjustment',
  4: 'Return',
  5: 'Damage',
  6: 'Reserved',
  7: 'Reservation released',
}

export default function AdminInventoryPage() {
  const [searchParams] = useSearchParams()
  const { can } = useAuth()

  const [search, setSearch] = useState('')
  const [lowStock, setLowStock] = useState(searchParams.get('lowStock') === 'true')
  const [outOfStock, setOutOfStock] = useState(searchParams.get('outOfStock') === 'true')
  const [page, setPage] = useState(1)
  const [historyFor, setHistoryFor] = useState<StockRow | null>(null)

  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'inventory', { search, lowStock, outOfStock, page }],
    queryFn: () =>
      api.get<Paged<StockRow>>(
        `/admin/inventory${qs({
          search,
          lowStock: lowStock || undefined,
          outOfStock: outOfStock || undefined,
          page,
          pageSize: 20,
        })}`,
      ),
    placeholderData: keepPreviousData,
  })

  return (
    <div>
      <PageHeader
        title="Inventory"
        description="Stock by variant. Adjustments record a target count, not a difference."
      />

      <FilterBar>
        <form
          onSubmit={(event) => {
            event.preventDefault()
            setPage(1)
          }}
          className="flex gap-2"
        >
          <Input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="SKU, barcode or product…"
            className="h-9 w-72"
            aria-label="Search inventory"
          />
          <Button type="submit" variant="outline" size="sm">
            Search
          </Button>
        </form>

        <label className="flex items-center gap-2 text-sm text-ink-600">
          <input
            type="checkbox"
            checked={lowStock}
            onChange={(event) => {
              setLowStock(event.target.checked)
              setPage(1)
            }}
            className="h-4 w-4 rounded border-ink-300 text-saffron-500"
          />
          Low stock only
        </label>

        <label className="flex items-center gap-2 text-sm text-ink-600">
          <input
            type="checkbox"
            checked={outOfStock}
            onChange={(event) => {
              setOutOfStock(event.target.checked)
              setPage(1)
            }}
            className="h-4 w-4 rounded border-ink-300 text-saffron-500"
          />
          Out of stock only
        </label>
      </FilterBar>

      {isLoading ? (
        <TableSkeleton columns={6} />
      ) : (
        <>
          <TableShell>
            <thead>
              <tr>
                <Th>Variant</Th>
                <Th>SKU</Th>
                <Th align="right">On hand</Th>
                <Th align="right">Available</Th>
                <Th align="right">Value</Th>
                <Th align="right">Actions</Th>
              </tr>
            </thead>
            <tbody>
              {data?.items.length === 0 && <TableEmpty colSpan={6} message="Nothing matches those filters." />}

              {data?.items.map((row) => (
                <tr key={row.variantId} className="transition-colors hover:bg-ink-50/60">
                  <Td>
                    <div className="flex items-center gap-3">
                      <Image src={row.thumbnailUrl} alt="" width={36} height={36} className="h-9 w-9 shrink-0 rounded-lg" />
                      <div className="min-w-0">
                        <span className="block truncate text-ink-800">{row.productName}</span>
                        <span className="block text-[11px] text-ink-400">
                          {row.variantName ?? row.unit}
                          {row.isOutOfStock && <Badge tone="sale"> Out</Badge>}
                          {!row.isOutOfStock && row.isLowStock && <Badge tone="warning"> Low</Badge>}
                        </span>
                      </div>
                    </div>
                  </Td>

                  <Td className="font-mono text-[11px] text-ink-500">{row.sku}</Td>

                  <Td align="right" className="font-medium text-ink-800">
                    {row.stockQuantity}
                  </Td>

                  <Td align="right">
                    <span
                      className={`font-semibold ${
                        row.isOutOfStock ? 'text-chilli-600' : row.isLowStock ? 'text-saffron-600' : 'text-ink-800'
                      }`}
                    >
                      {row.availableQuantity}
                    </span>
                    {row.reservedQuantity > 0 && (
                      <span className="block text-[10px] text-ink-400">{row.reservedQuantity} reserved</span>
                    )}
                  </Td>

                  <Td align="right" className="whitespace-nowrap text-xs text-ink-500">
                    {formatPrice(row.stockQuantity * (row.costPrice ?? row.price))}
                  </Td>

                  <Td align="right">
                    <div className="flex justify-end gap-1">
                      {can('inventory.view-history') && (
                        <Button variant="ghost" size="sm" onClick={() => setHistoryFor(row)}>
                          History
                        </Button>
                      )}
                      {can('inventory.adjust') && <AdjustButton row={row} />}
                    </div>
                  </Td>
                </tr>
              ))}
            </tbody>
          </TableShell>

          {data && (
            <Pager page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onChange={setPage} />
          )}
        </>
      )}

      {historyFor && <HistoryDrawer row={historyFor} onClose={() => setHistoryFor(null)} />}
    </div>
  )
}

/**
 * Stock adjustment.
 *
 * Asks for the counted quantity ("the shelf has 47"), not a delta ("add 3") — that is how a stock
 * take actually works, and it removes a whole class of double-application mistakes. A reason is
 * mandatory, because an unexplained stock change is impossible to investigate weeks later.
 */
function AdjustButton({ row }: { row: StockRow }) {
  const queryClient = useQueryClient()
  const [open, setOpen] = useState(false)
  const [quantity, setQuantity] = useState(String(row.stockQuantity))
  const [reason, setReason] = useState('')

  const adjust = useMutation({
    mutationFn: () =>
      api.post<void>('/admin/inventory/adjust', {
        variantId: row.variantId,
        newQuantity: Number(quantity),
        reason: reason.trim(),
      }),
    onSuccess: async () => {
      setOpen(false)
      setReason('')
      await queryClient.invalidateQueries({ queryKey: ['admin', 'inventory'] })
      await queryClient.invalidateQueries({ queryKey: ['admin', 'dashboard'] })
    },
  })

  if (!open) {
    return (
      <Button variant="outline" size="sm" onClick={() => setOpen(true)}>
        Adjust
      </Button>
    )
  }

  return (
    <div className="fixed inset-0 z-50 grid place-items-center p-4">
      <button type="button" aria-label="Cancel" onClick={() => setOpen(false)} className="absolute inset-0 bg-ink-900/40" />

      <form
        onSubmit={(event) => {
          event.preventDefault()
          adjust.mutate()
        }}
        className="animate-fade-rise relative w-full max-w-sm rounded-xl bg-paper-raised p-5 shadow-overlay"
      >
        <h3 className="font-display text-base font-bold text-ink-900">Adjust stock</h3>
        <p className="mt-1 truncate text-xs text-ink-500">
          {row.productName} · <span className="font-mono">{row.sku}</span>
        </p>

        {adjust.isError && (
          <div className="mt-3">
            <Alert tone="error">{(adjust.error as Error).message}</Alert>
          </div>
        )}

        <label htmlFor="counted" className="mt-4 block text-sm font-medium text-ink-700">
          Counted quantity
        </label>
        <Input
          id="counted"
          type="number"
          min={0}
          inputMode="numeric"
          value={quantity}
          onChange={(event) => setQuantity(event.target.value)}
          autoFocus
          className="mt-1"
        />
        <p className="mt-1 text-xs text-ink-400">
          Currently {row.stockQuantity} on hand.{' '}
          {Number(quantity) !== row.stockQuantity && (
            <span className={Number(quantity) > row.stockQuantity ? 'text-cardamom-600' : 'text-chilli-600'}>
              {Number(quantity) > row.stockQuantity ? '+' : ''}
              {Number(quantity) - row.stockQuantity} change
            </span>
          )}
        </p>

        <label htmlFor="reason" className="mt-4 block text-sm font-medium text-ink-700">
          Reason <span className="text-chilli-500">*</span>
        </label>
        <Input
          id="reason"
          value={reason}
          onChange={(event) => setReason(event.target.value)}
          placeholder="Stock take, damaged, delivery received…"
          required
          className="mt-1"
        />

        <div className="mt-5 flex gap-2">
          <Button type="submit" loading={adjust.isPending} disabled={!reason.trim()}>
            Save adjustment
          </Button>
          <Button type="button" variant="ghost" onClick={() => setOpen(false)}>
            Cancel
          </Button>
        </div>
      </form>
    </div>
  )
}

function HistoryDrawer({ row, onClose }: { row: StockRow; onClose: () => void }) {
  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'inventory', row.variantId, 'history'],
    queryFn: () => api.get<Paged<Movement>>(`/admin/inventory/${row.variantId}/history${qs({ pageSize: 50 })}`),
  })

  return (
    <div className="fixed inset-0 z-50 flex justify-end">
      <button type="button" aria-label="Close" onClick={onClose} className="absolute inset-0 bg-ink-900/40" />

      <div className="animate-fade-rise relative w-full max-w-md overflow-y-auto bg-paper shadow-overlay">
        <header className="sticky top-0 flex items-start justify-between gap-3 border-b border-ink-100 bg-paper-raised px-5 py-4">
          <div className="min-w-0">
            <p className="truncate text-sm font-bold text-ink-900">{row.productName}</p>
            <p className="font-mono text-xs text-ink-400">{row.sku}</p>
          </div>
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="grid h-9 w-9 shrink-0 place-items-center rounded-lg text-ink-500 hover:bg-ink-100"
          >
            <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true">
              <path d="M6 6l12 12M18 6 6 18" strokeLinecap="round" />
            </svg>
          </button>
        </header>

        <div className="p-5">
          <p className="text-xs text-ink-400">
            Every movement, newest first. This ledger is append-only — it is never edited, which is
            what makes a stock figure explainable.
          </p>

          {isLoading ? (
            <div className="mt-4 space-y-2" aria-busy="true">
              {Array.from({ length: 5 }, (_, index) => (
                <div key={index} className="skeleton h-14 w-full" />
              ))}
            </div>
          ) : (
            <ul className="mt-4 space-y-2">
              {data?.items.length === 0 && <li className="text-sm text-ink-400">No movements recorded.</li>}

              {data?.items.map((movement) => (
                <li key={movement.id} className="card-surface p-3">
                  <div className="flex items-start justify-between gap-2">
                    <div className="min-w-0">
                      <p className="text-xs font-semibold text-ink-800">
                        {MOVEMENT_LABEL[movement.type] ?? 'Movement'}
                      </p>
                      <p className="text-[11px] text-ink-400">
                        {formatDateTime(movement.createdAt)}
                        {movement.performedByName && ` · ${movement.performedByName}`}
                      </p>
                      {movement.note && <p className="mt-1 text-[11px] text-ink-500">{movement.note}</p>}
                      {movement.reference && (
                        <p className="font-mono text-[10px] text-ink-400">{movement.reference}</p>
                      )}
                    </div>

                    <div className="shrink-0 text-right">
                      <p
                        className={`text-sm font-bold ${
                          movement.quantityChange > 0 ? 'text-cardamom-600' : 'text-chilli-600'
                        }`}
                      >
                        {movement.quantityChange > 0 ? '+' : ''}
                        {movement.quantityChange}
                      </p>
                      <p className="text-[10px] text-ink-400">→ {movement.quantityAfter}</p>
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </div>
  )
}
