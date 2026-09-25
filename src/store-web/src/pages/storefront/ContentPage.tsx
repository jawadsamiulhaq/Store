import { useEffect } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { api } from '../../lib/api'
import { EmptyState } from '../../ui/primitives'
import type { ContentPage as ContentPageModel } from '../../lib/types'

/**
 * A CMS page — About, Delivery, Returns, Privacy, Terms.
 *
 * Staff edit these in the admin area, so the client can correct their own legal and delivery copy
 * without a deploy. Only published pages are served; an unpublished one 404s rather than leaking
 * a draft.
 */
export default function ContentPage() {
  const { slug } = useParams()

  const { data: page, isLoading, isError } = useQuery({
    queryKey: ['page', slug],
    queryFn: () => api.get<ContentPageModel>(`/storefront/pages/${slug}`),
    enabled: Boolean(slug),
    staleTime: 15 * 60 * 1000,
  })

  useEffect(() => {
    if (page) {
      document.title = `${page.metaTitle ?? page.title} — Waqas Provision Store`
    }

    return () => {
      document.title = 'Waqas Provision Store — Groceries delivered across Hong Kong'
    }
  }, [page])

  if (isLoading) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-12 sm:px-6" aria-busy="true">
        <div className="skeleton h-9 w-2/3" />
        <div className="mt-6 space-y-3">
          {Array.from({ length: 6 }, (_, index) => (
            <div key={index} className="skeleton h-4 w-full" />
          ))}
        </div>
      </div>
    )
  }

  if (isError || !page) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-16 sm:px-6">
        <EmptyState
          title="This page is not available"
          description="It may not have been published yet, or the link may be out of date."
          action={
            <Link to="/" className="text-sm font-medium text-saffron-600 hover:text-saffron-700">
              Back to the shop →
            </Link>
          }
        />
      </div>
    )
  }

  return (
    <article className="mx-auto max-w-3xl px-4 py-12 sm:px-6">
      <nav aria-label="Breadcrumb" className="text-xs text-ink-400">
        <Link to="/" className="hover:text-saffron-600">
          Home
        </Link>
        <span className="mx-1.5">/</span>
        <span className="text-ink-600">{page.title}</span>
      </nav>

      <h1 className="mt-3 text-3xl font-bold tracking-tight text-ink-900">{page.title}</h1>

      {/*
        Server-sanitised HTML. The API sanitises on write rather than trusting the client to
        sanitise on read, so what is stored is what is safe to render.
      */}
      <div
        className="mt-6 space-y-4 leading-relaxed text-ink-600 [&_a]:text-saffron-600 [&_a]:underline [&_h2]:mt-8 [&_h2]:text-lg [&_h2]:font-bold [&_h2]:text-ink-900 [&_li]:ml-5 [&_li]:list-disc [&_p]:mb-3 [&_ul]:my-3"
        dangerouslySetInnerHTML={{ __html: page.body }}
      />
    </article>
  )
}
