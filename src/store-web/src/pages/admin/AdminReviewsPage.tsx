import { useState } from 'react'
import { Link } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, qs } from '../../lib/api'
import { Alert, Badge, Button, Rating, Select, Textarea } from '../../ui/primitives'
import { FilterBar, PageHeader, Pager } from '../../features/admin/AdminTable'
import { useAuth } from '../../app/providers/AuthProvider'
import { formatDateTime } from '../../lib/format'
import type { Paged } from '../../lib/types'

interface AdminReview {
  id: string
  productId: string
  productName: string
  productSlug: string
  customerName: string
  customerEmail: string
  rating: number
  title?: string
  body: string
  status: number
  isVerifiedPurchase: boolean
  helpfulCount: number
  adminReply?: string
  rejectionReason?: string
  createdAt: string
  moderatedAt?: string
}

const STATUS = { Pending: 0, Approved: 1, Rejected: 2 } as const

export default function AdminReviewsPage() {
  const queryClient = useQueryClient()
  const { can } = useAuth()

  // Defaults to the pending queue, because that is the reason this screen exists.
  const [status, setStatus] = useState<string>(String(STATUS.Pending))
  const [page, setPage] = useState(1)
  const [replyingTo, setReplyingTo] = useState<string | null>(null)
  const [reply, setReply] = useState('')

  const { data, isLoading } = useQuery({
    queryKey: ['admin', 'reviews', { status, page }],
    queryFn: () =>
      api.get<Paged<AdminReview>>(
        `/admin/reviews${qs({ status: status === '' ? undefined : status, page, pageSize: 20 })}`,
      ),
    placeholderData: keepPreviousData,
  })

  const invalidate = async () => {
    await queryClient.invalidateQueries({ queryKey: ['admin', 'reviews'] })
    await queryClient.invalidateQueries({ queryKey: ['admin', 'dashboard'] })
  }

  const moderate = useMutation({
    mutationFn: ({ id, next, reason }: { id: string; next: number; reason?: string }) =>
      api.put<void>(`/admin/reviews/${id}/moderate`, { status: next, rejectionReason: reason ?? null }),
    onSuccess: invalidate,
  })

  const postReply = useMutation({
    mutationFn: ({ id, text }: { id: string; text: string }) =>
      api.put<void>(`/admin/reviews/${id}/reply`, { reply: text }),
    onSuccess: async () => {
      setReplyingTo(null)
      setReply('')
      await invalidate()
    },
  })

  const remove = useMutation({
    mutationFn: (id: string) => api.del<void>(`/admin/reviews/${id}`),
    onSuccess: invalidate,
  })

  return (
    <div>
      <PageHeader
        title="Reviews"
        description="Only approved reviews contribute to a product's star rating."
      />

      <FilterBar>
        <Select
          value={status}
          onChange={(event) => {
            setStatus(event.target.value)
            setPage(1)
          }}
          className="h-9 w-44"
          aria-label="Filter by status"
        >
          <option value="0">Pending</option>
          <option value="1">Approved</option>
          <option value="2">Rejected</option>
          <option value="">All</option>
        </Select>
      </FilterBar>

      {moderate.isError && (
        <div className="mb-4">
          <Alert tone="error">{(moderate.error as Error).message}</Alert>
        </div>
      )}

      {isLoading ? (
        <div className="space-y-3" aria-busy="true">
          {Array.from({ length: 4 }, (_, index) => (
            <div key={index} className="skeleton h-40 w-full" />
          ))}
        </div>
      ) : data?.items.length === 0 ? (
        <p className="card-surface p-10 text-center text-sm text-ink-400">
          Nothing here. {status === '0' && 'The moderation queue is clear.'}
        </p>
      ) : (
        <ul className="space-y-3">
          {data?.items.map((review) => (
            <li key={review.id} className="card-surface p-5">
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <Link
                    to={`/product/${review.productSlug}`}
                    target="_blank"
                    rel="noopener"
                    className="text-sm font-semibold text-ink-800 hover:text-saffron-600"
                  >
                    {review.productName} ↗
                  </Link>

                  <div className="mt-1.5 flex flex-wrap items-center gap-2">
                    <Rating value={review.rating} />
                    {review.isVerifiedPurchase && <Badge tone="success">Verified purchase</Badge>}
                    <Badge
                      tone={
                        review.status === STATUS.Approved
                          ? 'success'
                          : review.status === STATUS.Rejected
                            ? 'sale'
                            : 'warning'
                      }
                    >
                      {['Pending', 'Approved', 'Rejected'][review.status]}
                    </Badge>
                  </div>

                  <p className="mt-1 text-[11px] text-ink-400">
                    {review.customerName} · {review.customerEmail} · {formatDateTime(review.createdAt)}
                  </p>
                </div>
              </div>

              {review.title && <p className="mt-3 font-medium text-ink-800">{review.title}</p>}
              <p className="mt-1.5 text-sm leading-relaxed text-ink-600">{review.body}</p>

              {review.rejectionReason && (
                <p className="mt-2 text-xs text-chilli-600">Rejected: {review.rejectionReason}</p>
              )}

              {review.adminReply && (
                <div className="mt-3 rounded-lg border-l-2 border-saffron-400 bg-saffron-50/60 p-3">
                  <p className="text-xs font-semibold text-saffron-800">Your reply</p>
                  <p className="mt-1 text-sm text-ink-600">{review.adminReply}</p>
                </div>
              )}

              {replyingTo === review.id ? (
                <div className="mt-3">
                  <Textarea
                    rows={3}
                    value={reply}
                    onChange={(event) => setReply(event.target.value)}
                    placeholder="Write a public reply…"
                    aria-label="Reply to review"
                    autoFocus
                  />
                  <div className="mt-2 flex gap-2">
                    <Button
                      size="sm"
                      loading={postReply.isPending}
                      disabled={!reply.trim()}
                      onClick={() => postReply.mutate({ id: review.id, text: reply })}
                    >
                      Post reply
                    </Button>
                    <Button size="sm" variant="ghost" onClick={() => setReplyingTo(null)}>
                      Cancel
                    </Button>
                  </div>
                </div>
              ) : (
                <div className="mt-4 flex flex-wrap gap-2">
                  {can('reviews.moderate') && review.status !== STATUS.Approved && (
                    <Button
                      size="sm"
                      loading={moderate.isPending}
                      onClick={() => moderate.mutate({ id: review.id, next: STATUS.Approved })}
                    >
                      Approve
                    </Button>
                  )}

                  {can('reviews.moderate') && review.status !== STATUS.Rejected && (
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => {
                        // The server requires a reason when rejecting, so it is collected here
                        // rather than letting the request fail.
                        const reason = window.prompt('Why is this review being rejected?')
                        if (reason?.trim()) {
                          moderate.mutate({ id: review.id, next: STATUS.Rejected, reason: reason.trim() })
                        }
                      }}
                    >
                      Reject
                    </Button>
                  )}

                  {can('reviews.reply') && (
                    <Button
                      size="sm"
                      variant="ghost"
                      onClick={() => {
                        setReplyingTo(review.id)
                        setReply(review.adminReply ?? '')
                      }}
                    >
                      {review.adminReply ? 'Edit reply' : 'Reply'}
                    </Button>
                  )}

                  {can('reviews.delete') && (
                    <Button
                      size="sm"
                      variant="ghost"
                      className="text-chilli-600"
                      onClick={() => {
                        if (window.confirm('Delete this review permanently?')) {
                          remove.mutate(review.id)
                        }
                      }}
                    >
                      Delete
                    </Button>
                  )}
                </div>
              )}
            </li>
          ))}
        </ul>
      )}

      {data && <Pager page={data.page} totalPages={data.totalPages} totalCount={data.totalCount} onChange={setPage} />}
    </div>
  )
}
