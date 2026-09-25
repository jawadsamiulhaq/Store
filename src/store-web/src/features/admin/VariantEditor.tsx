import { useState } from 'react'
import { Badge, Button, Checkbox, Input, Select } from '../../ui/primitives'

/*
  Options and variants.
  =====================

  The domain rule this screen exists to serve: a product holds identity and merchandising, and
  *price and stock live on the variant*. A grocery sells the same rice in 500 g, 1 kg and 5 kg, and
  the legacy store could not express that at all — one price, one stock figure, and a `unit` column
  that read "piece" for all 4,207 rows.

  So: the admin defines option axes ("Size", "Grind"), the editor generates the combinations, and
  each combination becomes a sellable SKU with its own price.
*/

export interface OptionValueDraft {
  id?: string
  value: string
  hexColor?: string | null
  displayOrder: number
}

export interface OptionDraft {
  id?: string
  name: string
  displayOrder: number
  values: OptionValueDraft[]
}

export interface VariantOptionSelection {
  option: string
  value: string
}

export interface VariantDraft {
  /** Absent for a variant that does not exist server-side yet. */
  id?: string
  name?: string | null
  sku: string
  barcode?: string | null
  price: number
  compareAtPrice?: number | null
  costPrice?: number | null
  stockQuantity: number
  reservedQuantity: number
  lowStockThreshold: number
  trackInventory: boolean
  allowBackorder: boolean
  weightGrams?: number | null
  unit: string
  unitValue?: number | null
  isDefault: boolean
  isActive: boolean
  displayOrder: number
  /** True once the variant has been ordered or stock-adjusted, so it can no longer be deleted. */
  hasHistory?: boolean
  optionValues: VariantOptionSelection[]
}

/** Units the shop actually sells in. Free text would produce "kg", "Kg" and "kgs" within a week. */
const UNITS = ['piece', 'kg', 'g', 'litre', 'ml', 'pack', 'box', 'dozen'] as const

/** Order-independent, case-insensitive identity for a combination. */
function comboKey(selections: VariantOptionSelection[]): string {
  return selections
    .map((s) => `${s.option.trim().toLowerCase()}=${s.value.trim().toLowerCase()}`)
    .sort()
    .join('|')
}

function comboLabel(selections: VariantOptionSelection[]): string {
  return selections.map((s) => s.value).join(' · ')
}

/** Every combination of the option values — the set of SKUs the product could have. */
function cartesian(options: OptionDraft[]): VariantOptionSelection[][] {
  const usable = options
    .map((option) => ({
      name: option.name.trim(),
      values: option.values.map((v) => v.value.trim()).filter(Boolean),
    }))
    .filter((option) => option.name && option.values.length > 0)

  if (usable.length === 0) return []

  return usable.reduce<VariantOptionSelection[][]>(
    (rows, option) =>
      rows.flatMap((row) => option.values.map((value) => [...row, { option: option.name, value }])),
    [[]],
  )
}

export function VariantEditor({
  options,
  variants,
  onOptionsChange,
  onVariantsChange,
  isNewProduct,
}: {
  options: OptionDraft[]
  variants: VariantDraft[]
  onOptionsChange: (next: OptionDraft[]) => void
  onVariantsChange: (next: VariantDraft[]) => void
  isNewProduct: boolean
}) {
  const combos = cartesian(options)

  function patchOption(index: number, patch: Partial<OptionDraft>) {
    onOptionsChange(options.map((option, i) => (i === index ? { ...option, ...patch } : option)))
  }

  function patchVariant(index: number, patch: Partial<VariantDraft>) {
    onVariantsChange(variants.map((variant, i) => (i === index ? { ...variant, ...patch } : variant)))
  }

  /**
   * Builds the missing combinations, keeping every variant that already exists.
   *
   * Matching on the combination rather than on position is what makes this safe to press twice:
   * a variant that already carries a price, a SKU and an order history keeps all of it, and only
   * genuinely new combinations are appended.
   */
  function generate() {
    const existing = new Map(variants.map((variant) => [comboKey(variant.optionValues), variant]))
    const base = variants[0]

    const next = combos.map((combo, index) => {
      const found = existing.get(comboKey(combo))
      if (found) return { ...found, optionValues: combo }

      return {
        sku: '',
        // Seeded from the first variant so a ten-SKU product is not ten prices typed by hand.
        price: base?.price ?? 0,
        stockQuantity: 0,
        reservedQuantity: 0,
        lowStockThreshold: base?.lowStockThreshold ?? 10,
        trackInventory: base?.trackInventory ?? true,
        allowBackorder: false,
        unit: base?.unit ?? 'piece',
        isDefault: false,
        isActive: true,
        displayOrder: index,
        optionValues: combo,
      } satisfies VariantDraft
    })

    // A variant whose combination no longer exists is kept only if the server could not delete it
    // anyway. Dropping one with order history here would just produce a save error.
    const orphans = variants.filter(
      (variant) => variant.hasHistory && !combos.some((combo) => comboKey(combo) === comboKey(variant.optionValues)),
    )

    const merged = [...next, ...orphans.map((variant) => ({ ...variant, isActive: false }))]

    if (!merged.some((variant) => variant.isDefault && variant.isActive)) {
      const first = merged.find((variant) => variant.isActive)
      if (first) first.isDefault = true
    }

    onVariantsChange(merged.map((variant, index) => ({ ...variant, displayOrder: index })))
  }

  function addBlankVariant() {
    onVariantsChange([
      ...variants,
      {
        sku: '',
        price: variants[0]?.price ?? 0,
        stockQuantity: 0,
        reservedQuantity: 0,
        lowStockThreshold: 10,
        trackInventory: true,
        allowBackorder: false,
        unit: variants[0]?.unit ?? 'piece',
        isDefault: variants.length === 0,
        isActive: true,
        displayOrder: variants.length,
        optionValues: [],
      },
    ])
  }

  function removeVariant(index: number) {
    const next = variants.filter((_, i) => i !== index)

    if (next.length > 0 && !next.some((variant) => variant.isDefault && variant.isActive)) {
      const first = next.find((variant) => variant.isActive)
      if (first) first.isDefault = true
    }

    onVariantsChange(next)
  }

  function setDefault(index: number) {
    onVariantsChange(variants.map((variant, i) => ({ ...variant, isDefault: i === index })))
  }

  return (
    <div className="space-y-5">
      {/* ---- Options ---- */}
      <div>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h3 className="font-display text-sm font-bold text-ink-900">Options</h3>
            <p className="text-xs text-ink-400">
              Axes the product varies along — Size, Grind, Flavour. Leave empty for a product sold
              one way only.
            </p>
          </div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            onClick={() =>
              onOptionsChange([...options, { name: '', displayOrder: options.length, values: [] }])
            }
          >
            Add option
          </Button>
        </div>

        {options.length > 0 && (
          <div className="mt-3 space-y-2">
            {options.map((option, index) => (
              <OptionRow
                key={option.id ?? `new-${index}`}
                option={option}
                onChange={(patch) => patchOption(index, patch)}
                onRemove={() => onOptionsChange(options.filter((_, i) => i !== index))}
              />
            ))}

            <div className="flex flex-wrap items-center gap-3 pt-1">
              <Button type="button" size="sm" disabled={combos.length === 0} onClick={generate}>
                Generate variants{combos.length > 0 ? ` (${combos.length})` : ''}
              </Button>
              <p className="text-xs text-ink-400">
                Keeps the price, SKU and stock of every combination that already exists.
              </p>
            </div>
          </div>
        )}
      </div>

      {/* ---- Variants ---- */}
      <div>
        <div className="flex flex-wrap items-center justify-between gap-2">
          <div>
            <h3 className="font-display text-sm font-bold text-ink-900">
              Variants <span className="font-normal text-ink-400">({variants.length})</span>
            </h3>
            <p className="text-xs text-ink-400">
              Price and stock live here, not on the product. Every product needs at least one.
            </p>
          </div>
          <Button type="button" variant="outline" size="sm" onClick={addBlankVariant}>
            Add variant
          </Button>
        </div>

        {variants.length === 0 ? (
          <p className="mt-3 rounded-xl border border-dashed border-ink-200 p-4 text-center text-sm text-ink-400">
            No variants yet. Add one, or define options above and generate them.
          </p>
        ) : (
          <div className="mt-3 overflow-x-auto rounded-xl border border-ink-100">
            <table className="w-full min-w-[56rem] text-sm">
              <thead className="border-b border-ink-100 bg-ink-50 text-left text-[11px] uppercase tracking-wide text-ink-400">
                <tr>
                  <th className="px-3 py-2 font-medium">Variant</th>
                  <th className="px-3 py-2 font-medium">SKU</th>
                  <th className="px-3 py-2 font-medium">Price</th>
                  <th className="px-3 py-2 font-medium">Compare at</th>
                  <th className="px-3 py-2 font-medium">Cost</th>
                  <th className="px-3 py-2 font-medium">Unit</th>
                  <th className="px-3 py-2 font-medium">Stock</th>
                  <th className="px-3 py-2 font-medium">Default</th>
                  <th className="px-3 py-2" />
                </tr>
              </thead>
              <tbody className="divide-y divide-ink-100">
                {variants.map((variant, index) => (
                  <tr key={variant.id ?? `new-${index}`} className={variant.isActive ? '' : 'opacity-55'}>
                    <td className="px-3 py-2">
                      {variant.optionValues.length > 0 ? (
                        <span className="font-medium text-ink-800">{comboLabel(variant.optionValues)}</span>
                      ) : (
                        <Input
                          value={variant.name ?? ''}
                          onChange={(event) => patchVariant(index, { name: event.target.value })}
                          placeholder="e.g. 1 kg"
                          aria-label={`Variant ${index + 1} name`}
                          className="h-8 w-28 text-xs"
                        />
                      )}
                      {variant.hasHistory && (
                        <span className="mt-1 block text-[10px] text-ink-400">has history</span>
                      )}
                    </td>

                    <td className="px-3 py-2">
                      <Input
                        value={variant.sku}
                        onChange={(event) => patchVariant(index, { sku: event.target.value })}
                        placeholder="auto"
                        aria-label={`SKU for variant ${index + 1}`}
                        className="h-8 w-28 font-mono text-xs uppercase"
                      />
                    </td>

                    <td className="px-3 py-2">
                      <Input
                        type="number"
                        min={0}
                        step="0.01"
                        value={variant.price}
                        onChange={(event) => patchVariant(index, { price: Number(event.target.value) })}
                        required
                        aria-label={`Price for variant ${index + 1}`}
                        className="h-8 w-24 text-xs"
                      />
                    </td>

                    <td className="px-3 py-2">
                      <Input
                        type="number"
                        min={0}
                        step="0.01"
                        value={variant.compareAtPrice ?? ''}
                        onChange={(event) =>
                          patchVariant(index, {
                            compareAtPrice: event.target.value === '' ? null : Number(event.target.value),
                          })
                        }
                        placeholder="—"
                        aria-label={`Compare-at price for variant ${index + 1}`}
                        className="h-8 w-24 text-xs"
                      />
                    </td>

                    <td className="px-3 py-2">
                      <Input
                        type="number"
                        min={0}
                        step="0.01"
                        value={variant.costPrice ?? ''}
                        onChange={(event) =>
                          patchVariant(index, {
                            costPrice: event.target.value === '' ? null : Number(event.target.value),
                          })
                        }
                        placeholder="—"
                        aria-label={`Cost price for variant ${index + 1}`}
                        className="h-8 w-24 text-xs"
                      />
                    </td>

                    <td className="px-3 py-2">
                      <div className="flex gap-1">
                        <Input
                          type="number"
                          min={0}
                          step="0.01"
                          value={variant.unitValue ?? ''}
                          onChange={(event) =>
                            patchVariant(index, {
                              unitValue: event.target.value === '' ? null : Number(event.target.value),
                            })
                          }
                          placeholder="500"
                          aria-label={`Unit value for variant ${index + 1}`}
                          className="h-8 w-16 text-xs"
                        />
                        <Select
                          value={variant.unit}
                          onChange={(event) => patchVariant(index, { unit: event.target.value })}
                          aria-label={`Unit for variant ${index + 1}`}
                          className="h-8 w-20 text-xs"
                        >
                          {UNITS.map((unit) => (
                            <option key={unit} value={unit}>
                              {unit}
                            </option>
                          ))}
                        </Select>
                      </div>
                    </td>

                    <td className="px-3 py-2">
                      {variant.id ? (
                        /*
                          Read-only once the variant exists. Stock moves through the Inventory
                          screen, which writes a ledger row recording who moved it and why; a
                          product form that wrote this column directly would leave the ledger
                          unable to account for the difference. The server ignores it too.
                        */
                        <div className="w-20">
                          <span className="block text-xs font-medium text-ink-700">{variant.stockQuantity}</span>
                          {variant.reservedQuantity > 0 && (
                            <span className="block text-[10px] text-ink-400">
                              {variant.reservedQuantity} held
                            </span>
                          )}
                        </div>
                      ) : (
                        <Input
                          type="number"
                          min={0}
                          value={variant.stockQuantity}
                          onChange={(event) =>
                            patchVariant(index, { stockQuantity: Math.max(0, Number(event.target.value)) })
                          }
                          aria-label={`Opening stock for variant ${index + 1}`}
                          className="h-8 w-20 text-xs"
                        />
                      )}
                    </td>

                    <td className="px-3 py-2">
                      <input
                        type="radio"
                        name="default-variant"
                        checked={variant.isDefault}
                        disabled={!variant.isActive}
                        onChange={() => setDefault(index)}
                        aria-label={`Make variant ${index + 1} the default`}
                        className="h-4 w-4 border-ink-300 text-saffron-500"
                      />
                    </td>

                    <td className="px-3 py-2">
                      <div className="flex items-center justify-end gap-1">
                        <Checkbox
                          label={<span className="text-xs">Active</span>}
                          checked={variant.isActive}
                          onChange={(event) => patchVariant(index, { isActive: event.target.checked })}
                        />
                        {/* A variant with history cannot be deleted — orders and the stock ledger
                            both point at it. Deactivating is the honest equivalent. */}
                        {!variant.hasHistory && (
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            className="text-chilli-600"
                            onClick={() => removeVariant(index)}
                          >
                            Remove
                          </Button>
                        )}
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {!isNewProduct && variants.some((variant) => variant.id) && (
          <p className="mt-2 text-xs text-ink-400">
            Stock is shown read-only here — change it on the Inventory screen so the adjustment is
            recorded in the ledger. New variants added now take their opening balance from this
            form.
          </p>
        )}

        {variants.length > 0 && !variants.some((variant) => variant.isActive) && (
          <p className="mt-2 text-xs font-medium text-chilli-600">
            At least one variant must stay active, otherwise the product has no price.
          </p>
        )}
      </div>
    </div>
  )
}

function OptionRow({
  option,
  onChange,
  onRemove,
}: {
  option: OptionDraft
  onChange: (patch: Partial<OptionDraft>) => void
  onRemove: () => void
}) {
  const [draft, setDraft] = useState('')

  function addValue() {
    const value = draft.trim()
    if (!value) return

    if (!option.values.some((existing) => existing.value.toLowerCase() === value.toLowerCase())) {
      onChange({
        values: [...option.values, { value, hexColor: null, displayOrder: option.values.length }],
      })
    }

    setDraft('')
  }

  return (
    <div className="rounded-xl border border-ink-100 p-3">
      <div className="flex items-center gap-2">
        <Input
          value={option.name}
          onChange={(event) => onChange({ name: event.target.value })}
          placeholder="Option name (e.g. Size)"
          aria-label="Option name"
          className="h-9 max-w-xs text-sm"
        />
        <Button type="button" variant="ghost" size="sm" className="ml-auto text-chilli-600" onClick={onRemove}>
          Remove option
        </Button>
      </div>

      <div className="mt-2.5 flex flex-wrap items-center gap-1.5">
        {option.values.map((value, index) => (
          <Badge key={value.id ?? `${value.value}-${index}`} tone="neutral">
            <span className="inline-flex items-center gap-1.5">
              {value.value}
              <button
                type="button"
                onClick={() =>
                  onChange({
                    values: option.values
                      .filter((_, i) => i !== index)
                      .map((v, i) => ({ ...v, displayOrder: i })),
                  })
                }
                aria-label={`Remove ${value.value}`}
                className="text-ink-400 transition-colors hover:text-chilli-600"
              >
                ×
              </button>
            </span>
          </Badge>
        ))}

        <div className="flex items-center gap-1">
          <Input
            value={draft}
            onChange={(event) => setDraft(event.target.value)}
            onKeyDown={(event) => {
              // Enter adds a value; without this it would submit the whole product form.
              if (event.key === 'Enter') {
                event.preventDefault()
                addValue()
              }
            }}
            placeholder="Add value, press Enter"
            aria-label={`Add a value to ${option.name || 'this option'}`}
            className="h-8 w-44 text-xs"
          />
          <Button type="button" variant="ghost" size="sm" onClick={addValue}>
            Add
          </Button>
        </div>
      </div>
    </div>
  )
}
