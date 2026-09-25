import { useRef, useState } from 'react'
import { api } from '../../lib/api'
import { Alert, Button, Input, Spinner } from '../../ui/primitives'

/**
 * An image on the product form. Mirrors `SaveProductImageRequest`, so the form can post the
 * array straight through without a mapping step that could drop a field.
 */
export interface ProductImageDraft {
  /** Absent on an image added in this session; the server creates the row. */
  id?: string
  url: string
  thumbnailUrl?: string | null
  altText?: string | null
  width: number
  height: number
  blurHash?: string | null
  isPrimary: boolean
  displayOrder: number
}

/** What `POST /admin/media/upload` returns — a `MediaAsset`. */
interface MediaAsset {
  url: string
  thumbnailUrl?: string
  width: number
  height: number
  blurHash?: string
}

const ACCEPT = 'image/jpeg,image/png,image/webp,image/avif'

export function ImageUploader({
  images,
  onChange,
  disabled,
}: {
  images: ProductImageDraft[]
  onChange: (next: ProductImageDraft[]) => void
  disabled?: boolean
}) {
  const inputRef = useRef<HTMLInputElement>(null)
  const [uploading, setUploading] = useState(0)
  const [error, setError] = useState<string | null>(null)
  const [dragging, setDragging] = useState(false)

  async function upload(files: FileList | File[]) {
    const list = Array.from(files).filter((file) => file.type.startsWith('image/'))
    if (list.length === 0) return

    setError(null)
    setUploading((count) => count + list.length)

    // Sequential, not parallel. The server produces a WebP derivative, a thumbnail and a blur
    // placeholder for each upload, and firing ten of those at once on a shared box makes every
    // one of them slow rather than making the batch fast.
    const added: ProductImageDraft[] = []

    for (const file of list) {
      try {
        const form = new FormData()
        form.append('file', file)

        const asset = await api.post<MediaAsset>('/admin/media/upload?folder=products', form)

        added.push({
          url: asset.url,
          thumbnailUrl: asset.thumbnailUrl ?? null,
          altText: null,
          width: asset.width,
          height: asset.height,
          blurHash: asset.blurHash ?? null,
          // The first image ever added becomes the primary one, so a product is never left
          // without a card image because nobody thought to tick the box.
          isPrimary: images.length === 0 && added.length === 0,
          displayOrder: images.length + added.length,
        })
      } catch (uploadError) {
        setError(`${file.name}: ${(uploadError as Error).message}`)
      } finally {
        setUploading((count) => count - 1)
      }
    }

    if (added.length > 0) {
      onChange([...images, ...added])
    }
  }

  function patch(index: number, next: Partial<ProductImageDraft>) {
    onChange(images.map((image, i) => (i === index ? { ...image, ...next } : image)))
  }

  function setPrimary(index: number) {
    onChange(images.map((image, i) => ({ ...image, isPrimary: i === index })))
  }

  function remove(index: number) {
    const next = images
      .filter((_, i) => i !== index)
      .map((image, i) => ({ ...image, displayOrder: i }))

    // Removing the primary would otherwise leave the set with none.
    if (next.length > 0 && !next.some((image) => image.isPrimary)) {
      next[0].isPrimary = true
    }

    onChange(next)
  }

  /**
   * Moves an image one place. Buttons rather than drag-and-drop: this is reachable by keyboard
   * and by screen reader, works on touch without a long-press, and the order only ever needs
   * small adjustments once the upload order is roughly right.
   */
  function move(index: number, delta: number) {
    const target = index + delta
    if (target < 0 || target >= images.length) return

    const next = [...images]
    ;[next[index], next[target]] = [next[target], next[index]]
    onChange(next.map((image, i) => ({ ...image, displayOrder: i })))
  }

  return (
    <div className="space-y-3">
      <div
        onDragOver={(event) => {
          event.preventDefault()
          if (!disabled) setDragging(true)
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={(event) => {
          event.preventDefault()
          setDragging(false)
          if (!disabled) void upload(event.dataTransfer.files)
        }}
        className={`rounded-xl border-2 border-dashed p-5 text-center transition-colors ${
          dragging ? 'border-saffron-400 bg-saffron-50' : 'border-ink-200 bg-paper-sunken/50'
        }`}
      >
        <input
          ref={inputRef}
          type="file"
          accept={ACCEPT}
          multiple
          disabled={disabled}
          className="sr-only"
          onChange={(event) => {
            if (event.target.files) void upload(event.target.files)
            // Cleared so re-picking the same file fires change again.
            event.target.value = ''
          }}
        />

        <p className="text-sm text-ink-600">Drop images here, or</p>

        <Button
          type="button"
          variant="outline"
          size="sm"
          className="mt-2"
          disabled={disabled}
          onClick={() => inputRef.current?.click()}
        >
          Choose files
        </Button>

        <p className="mt-2 text-xs text-ink-400">
          JPEG, PNG, WebP or AVIF. A WebP copy, a thumbnail and a blur placeholder are generated on
          upload — which is what lets the storefront reserve the exact space before an image loads.
        </p>

        {uploading > 0 && (
          <p className="mt-3 inline-flex items-center gap-2 text-xs font-medium text-ink-600">
            <Spinner className="h-3.5 w-3.5" />
            Uploading {uploading} file{uploading === 1 ? '' : 's'}…
          </p>
        )}
      </div>

      {error && <Alert tone="error">{error}</Alert>}

      {images.length > 0 && (
        <ul className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {images.map((image, index) => (
            <li key={image.id ?? image.url} className="card-surface overflow-hidden p-2.5">
              <div className="relative">
                <img
                  src={image.thumbnailUrl ?? image.url}
                  alt=""
                  width={image.width}
                  height={image.height}
                  loading="lazy"
                  className="aspect-square w-full rounded-lg bg-paper-sunken object-cover"
                />

                {image.isPrimary && (
                  <span className="absolute left-1.5 top-1.5 rounded-full bg-saffron-500 px-2 py-0.5 text-[10px] font-semibold text-white">
                    Primary
                  </span>
                )}
              </div>

              <Input
                value={image.altText ?? ''}
                onChange={(event) => patch(index, { altText: event.target.value })}
                placeholder="Alt text"
                aria-label={`Alt text for image ${index + 1}`}
                className="mt-2 h-8 text-xs"
              />

              <div className="mt-2 flex flex-wrap items-center gap-1">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  disabled={image.isPrimary}
                  onClick={() => setPrimary(index)}
                >
                  Make primary
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  aria-label={`Move image ${index + 1} earlier`}
                  disabled={index === 0}
                  onClick={() => move(index, -1)}
                >
                  ←
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  aria-label={`Move image ${index + 1} later`}
                  disabled={index === images.length - 1}
                  onClick={() => move(index, 1)}
                >
                  →
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="ml-auto text-chilli-600"
                  onClick={() => remove(index)}
                >
                  Remove
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
