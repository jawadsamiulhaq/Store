import { Link } from 'react-router-dom'
import { useStore } from '../providers/StoreProvider'

export function SiteFooter() {
  const { categories, footerPages, setting } = useStore()
  const year = new Date().getFullYear()

  return (
    <footer className="mt-16 border-t border-ink-100 bg-paper-sunken">
      <div className="mx-auto max-w-7xl px-4 py-12 sm:px-6 lg:px-8">
        <div className="grid gap-10 sm:grid-cols-2 lg:grid-cols-4">
          <div>
            <div className="flex items-center gap-2.5">
              <span className="grid h-9 w-9 place-items-center rounded-xl bg-saffron-500 text-white">
                <svg className="h-5 w-5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.9" aria-hidden="true">
                  <path d="M4 8h16l-1.3 11a2 2 0 0 1-2 1.8H7.3a2 2 0 0 1-2-1.8Z" strokeLinejoin="round" />
                  <path d="M9 8V6a3 3 0 0 1 6 0v2" strokeLinecap="round" />
                </svg>
              </span>
              <span className="font-display font-bold text-ink-900">
                {setting('store.name', 'Waqas Provision Store')}
              </span>
            </div>

            <p className="mt-3 text-sm leading-relaxed text-ink-500">
              {setting('store.tagline', 'Bringing authentic flavours and trusted grocery delivery across Hong Kong')}
            </p>

            <address className="mt-4 space-y-1 text-sm not-italic text-ink-500">
              <p>{setting('store.address', 'Stall S201, 1/F, Ngau Chi Wan Market, Clear Water Bay Rd (MTR exit B), Choi Hung, Hong Kong')}</p>
              <p>
                <a href={`tel:${setting('store.phone')}`} className="hover:text-saffron-600">
                  {setting('store.phone', '+852 9029 1454')}
                </a>
              </p>
              <p>
                <a href={`mailto:${setting('store.email')}`} className="hover:text-saffron-600">
                  {setting('store.email', 'info@waqas.com.hk')}
                </a>
              </p>
              <p>{setting('store.opening-hours', 'Open 7 days, 10:00–22:00')}</p>
            </address>

            <SocialLinks
              facebook={setting('social.facebook')}
              instagram={setting('social.instagram')}
              whatsapp={setting('social.whatsapp')}
            />
          </div>

          <FooterColumn title="Shop">
            <FooterLink to="/shop">All products</FooterLink>
            {categories.slice(0, 6).map((category) => (
              <FooterLink key={category.id} to={`/category/${category.slug}`}>
                {category.name}
              </FooterLink>
            ))}
          </FooterColumn>

          <FooterColumn title="Your account">
            <FooterLink to="/account/orders">My orders</FooterLink>
            <FooterLink to="/track">Track an order</FooterLink>
            <FooterLink to="/account/wishlist">Saved items</FooterLink>
            <FooterLink to="/account/addresses">Addresses</FooterLink>
            <FooterLink to="/cart">Shopping cart</FooterLink>
          </FooterColumn>

          <FooterColumn title="Information">
            {/*
              Driven by the CMS rather than hard-coded, so staff can publish a delivery or returns
              page without a deploy. Falls back to the standard set before anything is published.
            */}
            {footerPages.length > 0 ? (
              footerPages.map((page) => (
                <FooterLink key={page.id} to={`/page/${page.slug}`}>
                  {page.title}
                </FooterLink>
              ))
            ) : (
              <>
                <FooterLink to="/page/about">About us</FooterLink>
                <FooterLink to="/page/delivery">Delivery information</FooterLink>
                <FooterLink to="/page/returns">Returns &amp; refunds</FooterLink>
                <FooterLink to="/page/privacy">Privacy policy</FooterLink>
                <FooterLink to="/page/terms">Terms &amp; conditions</FooterLink>
              </>
            )}
          </FooterColumn>
        </div>

        <div className="mt-10 flex flex-col gap-3 border-t border-ink-200 pt-6 text-xs text-ink-400 sm:flex-row sm:items-center sm:justify-between">
          <p>
            © {year} {setting('store.name', 'Waqas Provision Store')}. All rights reserved.
          </p>
          <p>Delivery across Kowloon, Hong Kong Island and the New Territories.</p>
        </div>
      </div>
    </footer>
  )
}

/**
 * Social and messaging links, each rendered only when the shop has actually set one.
 *
 * These settings existed and nothing read them, so the shop's Facebook page and its WhatsApp
 * number appeared nowhere on the site — and WhatsApp in particular is how a lot of Hong Kong
 * grocery customers place an order in the first place.
 *
 * An unset value renders nothing rather than a dead icon: a link to an empty string looks like a
 * broken page, which is worse than the absence it is trying to hide.
 */
function SocialLinks({
  facebook,
  instagram,
  whatsapp,
}: {
  facebook?: string
  instagram?: string
  whatsapp?: string
}) {
  // wa.me takes digits only — no plus, no spaces, no punctuation.
  const whatsappDigits = whatsapp?.replace(/\D/g, '')

  const links = [
    facebook && { href: facebook, label: 'Facebook', path: 'M14 8.5h2.5V5.2h-2.6c-2.5 0-4 1.6-4 4.2V12H7.5v3.3h2.4V21h3.4v-5.7h2.5l.4-3.3h-2.9V9.8c0-.9.3-1.3 1.2-1.3Z' },
    instagram && { href: instagram, label: 'Instagram', path: 'M7.5 3.5h9a4 4 0 0 1 4 4v9a4 4 0 0 1-4 4h-9a4 4 0 0 1-4-4v-9a4 4 0 0 1 4-4ZM12 8.6a3.4 3.4 0 1 0 0 6.8 3.4 3.4 0 0 0 0-6.8ZM17.1 6.6h.01' },
    whatsappDigits && {
      href: `https://wa.me/${whatsappDigits}`,
      label: 'WhatsApp',
      path: 'M3.5 20.5 4.9 16a8 8 0 1 1 3.1 3.1ZM9 9.2c.3-.7.5-.7.8-.7h.6c.2 0 .5 0 .7.6l.8 1.9c.1.3 0 .5-.1.7l-.4.5c-.2.2-.3.4-.1.7a7 7 0 0 0 3.1 2.6c.3.1.5.1.7-.1l.6-.7c.2-.2.4-.2.6-.1l1.8.9c.3.1.4.3.4.5a2 2 0 0 1-1.3 1.6c-.5.2-1.3.3-3.4-.6a10 10 0 0 1-4.5-4.2c-.5-1-.7-1.9-.5-2.6Z',
    },
  ].filter(Boolean) as { href: string; label: string; path: string }[]

  if (links.length === 0) {
    return null
  }

  return (
    <div className="mt-4 flex items-center gap-2">
      {links.map((link) => (
        <a
          key={link.label}
          href={link.href}
          target="_blank"
          rel="noopener noreferrer"
          aria-label={link.label}
          className="grid h-9 w-9 place-items-center rounded-full border border-ink-200 text-ink-500 transition-colors hover:border-saffron-300 hover:text-saffron-600"
        >
          <svg className="h-4 w-4" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d={link.path} />
          </svg>
        </a>
      ))}
    </div>
  )
}

function FooterColumn({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <h2 className="font-display text-sm font-semibold uppercase tracking-wide text-ink-800">{title}</h2>
      <ul className="mt-3 space-y-2">{children}</ul>
    </div>
  )
}

function FooterLink({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <li>
      <Link to={to} className="text-sm text-ink-500 transition-colors hover:text-saffron-600">
        {children}
      </Link>
    </li>
  )
}
